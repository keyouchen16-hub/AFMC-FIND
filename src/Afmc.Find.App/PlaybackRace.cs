using System.IO;
using LibVLCSharp.Shared;
using Afmc.Find.Core;

namespace Afmc.Find.App;

public sealed record PlaybackCandidate(string Title, Func<CancellationToken, Task<string>> Prepare, Action<PlaybackEvidence>? Observed = null, string? ResourceKey = null);
public sealed record PlaybackWinner(string Title, TorrentPlayback Session, string? ResourceKey = null);

// Independent decoder probes. A tracker count, connected peer or metadata alone cannot win.
public static class PlaybackRace
{
    public static async Task<PlaybackWinner> RunAsync(IReadOnlyList<PlaybackCandidate> candidates,
        Func<TorrentPlayback> create, LibVLC vlc, Action<string> report, CancellationToken ct,
        TimeSpan? attemptTimeout = null)
    {
        using var race = CancellationTokenSource.CreateLinkedTokenSource(ct);
        race.CancelAfter(TimeSpan.FromSeconds(60));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var budget = new PlaybackStartupBudget();
        // Output modules are instance options, not per-media options. Probes must
        // never create a native video window or send audio to the user's speakers.
        using var probeVlc = new LibVLC("--vout=dummy", "--aout=dummy", "--avcodec-hw=none", "--no-video-title-show", "--no-lua", "--no-metadata-network-access");
        using var slots = new SemaphoreSlim(2);
        var winner = new TaskCompletionSource<PlaybackWinner>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Probe(PlaybackCandidate candidate, int index)
        {
            TorrentPlayback? session = null;
            var entered = false; var retained = false;
            PlaybackEvidence? evidence = null;
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(race.Token);
            try
            {
                await slots.WaitAsync(race.Token); entered = true;
                attempt.CancelAfter(attemptTimeout ?? TimeSpan.FromSeconds(25));
                var token = attempt.Token;
                report($"正在试播 {index + 1}/{candidates.Count} · {candidate.Title}");
                var address = await candidate.Prepare(token);
                session = create();
                var opening = session.OpenAsync(address, token);
                var peerExtension = false;
                var statusAt = DateTime.UtcNow;
                while (!opening.IsCompleted) {
                    if (attemptTimeout is null && !peerExtension && !token.IsCancellationRequested && session.Status.Peers > 0) {
                        peerExtension = true;
                        attempt.CancelAfter(TimeSpan.FromSeconds(30));
                        report($"版本 {index + 1} 已连接资源节点，正在获取视频信息…");
                    }
                    if (DateTime.UtcNow-statusAt >= TimeSpan.FromSeconds(2)) {
                        statusAt = DateTime.UtcNow;
                        report($"正在试播 {index + 1}/{candidates.Count} · 获取视频列表 · 已连接 {session.Status.Peers} 个资源节点");
                    }
                    await Task.WhenAny(opening, Task.Delay(100));
                }
                await opening;
                var file = session.Videos.OrderByDescending(f => f.Length).First();
                var stream = await session.SelectAsync(file, token);
                using var input = new PlaybackStreamInput(stream, token);
                using var media = new Media(probeVlc, input);
                PlaybackMedia.Configure(media, file.Path);
                media.AddOption(":vout=dummy"); media.AddOption(":aout=dummy"); media.AddOption(":file-caching=300");
                // A headless probe has no GPU surface. LibVLCSharp's per-player
                // configuration must also disable HW decoding (not just vout).
                using var decoder = new MediaPlayer(probeVlc) { EnableHardwareDecoding = false };
                var progressExtension = false;
                long receivedAtExtension = 0;
                var extensionAt = DateTime.UtcNow;
                try
                {
                    if (!decoder.Play(media)) throw new IOException("视频无法打开");
                    while (media.Statistics.DecodedVideo < 10 || decoder.Time < 1000)
                    {
                        token.ThrowIfCancellationRequested();
                        if (attemptTimeout is null) lock (budget) {
                            var elapsed = clock.Elapsed.TotalSeconds;
                            var before = budget.Deadline;
                            var deadline = budget.Observe(index, elapsed, session.Status.Downloaded);
                            if (deadline > before && !race.IsCancellationRequested)
                                race.CancelAfter(TimeSpan.FromSeconds(Math.Max(.001, deadline - elapsed)));
                        }
                        if (DateTime.UtcNow-statusAt >= TimeSpan.FromSeconds(2)) {
                            statusAt = DateTime.UtcNow;
                            var network = session.Status;
                            var state = network.Verified > 0 ? "正在解码验证画面" : "等待视频片段下载与校验";
                            report($"正在试播 {index + 1}/{candidates.Count} · {state} · 已接收 {network.Downloaded / 1048576d:0.0} MB · {network.Rate / 1024d:0} KB/s · {network.Peers} 个资源节点");
                        }
                        if (attemptTimeout is null && !progressExtension && session.Status.Downloaded > 0) {
                            progressExtension = true;
                            receivedAtExtension = session.Status.Downloaded;
                            extensionAt = DateTime.UtcNow;
                            attempt.CancelAfter(TimeSpan.FromSeconds(30));
                            report($"版本 {index + 1} 已收到数据，正在等待校验与解码…");
                        }
                        // Continue an actively progressing piece within the hard
                        // bounded race budget; do not discard a nearly complete
                        // startup piece merely because its first block arrived early.
                        if (attemptTimeout is null && progressExtension && DateTime.UtcNow-extensionAt > TimeSpan.FromSeconds(5)
                            && session.Status.Downloaded > receivedAtExtension + 64*1024) {
                            receivedAtExtension = session.Status.Downloaded;
                            extensionAt = DateTime.UtcNow;
                            attempt.CancelAfter(TimeSpan.FromSeconds(30));
                        }
                        if (decoder.State is VLCState.Error or VLCState.Ended) throw new IOException("没有解码到视频画面");
                        await Task.Delay(100, token);
                    }
                    token.ThrowIfCancellationRequested();
                    evidence = PlaybackEvidence.Playable;
                    retained = winner.TrySetResult(new PlaybackWinner(candidate.Title, session, candidate.ResourceKey));
                    if (retained) race.Cancel();
                }
                finally
                {
                    attempt.Cancel(); // unblock native stream reads before Stop joins the decoder thread
                    await Task.Run(() => decoder.Stop());
                }
            }
            catch (OperationCanceledException) { if (!ct.IsCancellationRequested && !winner.Task.IsCompletedSuccessfully) evidence = PlaybackEvidence.TimedOut; }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                evidence = PlaybackEvidence.PlaybackFailed;
                if (!race.IsCancellationRequested) report($"版本 {index + 1} 暂时未能播放，继续尝试其他版本…");
            }
            finally
            {
                attempt.Cancel();
                if (evidence != PlaybackEvidence.Playable && session?.Status.Verified > 0) evidence = PlaybackEvidence.DataVerified;
                else if (evidence is PlaybackEvidence.TimedOut && session?.Status.Downloaded > 0) evidence = PlaybackEvidence.DataReceived;
                if (evidence is {} observed && !ct.IsCancellationRequested) candidate.Observed?.Invoke(observed);
                if (session is not null && !retained) await session.DisposeAsync();
                if (entered) slots.Release();
            }
        }
        var tasks = candidates.Take(3).Select((c, i) => Probe(c, i)).ToArray();
        await Task.WhenAll(tasks);
        if (winner.Task.IsCompletedSuccessfully)
        {
            var result = await winner.Task;
            if (ct.IsCancellationRequested) { await result.Session.DisposeAsync(); ct.ThrowIfCancellationRequested(); }
            return result;
        }
        ct.ThrowIfCancellationRequested();
        throw new IOException("本次候选版本在限时内未能解码出画面。可能缺少在线做种或网络较慢；可换其他资源重试，这不代表资源永久失效。");
    }
}
