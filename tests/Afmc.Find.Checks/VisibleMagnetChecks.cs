using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Afmc.Find.App;
using Afmc.Find.Core;
using LibVLCSharp.Shared;

internal static class VisibleMagnetChecks
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(System.Drawing.Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    public static int Run()
    {
        var output = Path.GetFullPath(Environment.GetEnvironmentVariable("AFMC_COMPARE_OUTPUT") ?? "docs/evidence/v0820/visible");
        var duration = int.TryParse(Environment.GetEnvironmentVariable("AFMC_COMPARE_SECONDS"), out var configuredDuration)
            ? Math.Clamp(configuredDuration, 30, 600) : 240;
        Directory.CreateDirectory(output);
        var profile = Path.Combine(Path.GetTempPath(), "afmc-visible-" + Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(profile);
        store.Save(SiteSources.Catalog.Select(source => source with { Enabled = false }));
        var app = new Application { Resources = new ResourceDictionary { Source = new Uri("/AFMC.Find;component/Theme.xaml", UriKind.Relative) } };
        var main = new MainWindow(store, new SearchService());
        var exit = 1;
        PlayerWindow? window = null;
        main.Loaded += async (_, _) =>
        {
            var samples = new List<object>();
            var watch = new Stopwatch();
            double? firstDecoded = null, metadata = null;
            double observed = 0, stall = 0, longestStall = 0, currentStall = 0, previousWall = 0;
            long firstTime = 0, previousTime = 0, time = 0;
            var frames = 0;
            var visibleSamples = 0;
            var occludedSamples = 0;
            var outcome = "Running";
            var cleanup = "Pending";
            async Task Write()
            {
                var report = new
                {
                    Hash = "2EE672984434179AA8A7C5D2EBD0A94B38EB2960",
                    Mode = "Visible production PlayerWindow, isolated profile, no injected peers",
                    ObservationLimitSeconds = duration,
                    MetadataSeconds = metadata, FirstDecodedSeconds = firstDecoded,
                    PostStartObservedSeconds = observed, PostStartObservationComplete = observed >= 120,
                    PlaybackTimeMs = time, DecodedFrames = frames,
                    PlaybackAdvancedSeconds = firstDecoded.HasValue ? Math.Max(0, time - firstTime) / 1000d : 0,
                    PlaybackToWallRatio = observed > 0 ? Math.Max(0, time - firstTime) / 1000d / observed : (double?)null,
                    StallSeconds = stall, LongestStallSeconds = longestStall,
                    StallDefinition = "One-second samples with clock advance <=20ms after decoded startup; not an audio-quality assertion.",
                    VisibleSamples = visibleSamples, OccludedSamples = occludedSamples,
                    VisibilityDefinition = "Visible player and center-point hit belongs to player/overlay window; no desktop input injected.",
                    Outcome = outcome, Cleanup = cleanup, Samples = samples
                };
                var temporary = Path.Combine(output, "progress.json.tmp");
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, Path.Combine(output, "progress.json"), true);
            }
            try
            {
                ((Button)main.FindName("PlayerEntryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(400);
                window = app.Windows.OfType<PlayerWindow>().Single();
                var overlay = (FrameworkElement)window.FindName("VideoOverlay");
                var overlayWindow = Window.GetWindow(overlay);
                var hostHandle = new WindowInteropHelper(window).Handle;
                var overlayHandle = new WindowInteropHelper(overlayWindow).Handle;
                window.Topmost = true;
                await Task.Delay(150);
                window.Activate(); overlayWindow.Activate(); SetForegroundWindow(overlayHandle);
                window.Topmost = false;
                watch.Start();
                var opening = window.OpenAsync("magnet:?xt=urn:btih:2EE672984434179AA8A7C5D2EBD0A94B38EB2960");
                while (watch.Elapsed.TotalSeconds < duration)
                {
                    var now = watch.Elapsed.TotalSeconds;
                    var native = window.NativePlayer;
                    var status = window.TorrentSession?.Status;
                    if (status?.Metadata == true) metadata ??= now;
                    using (var media = native?.Media)
                    {
                        time = native?.Time ?? 0;
                        frames = media?.Statistics.DecodedVideo ?? 0;
                    }
                    if (firstDecoded is null && frames >= 10 && time >= 1000)
                    {
                        firstDecoded = now; firstTime = time;
                    }
                    else if (firstDecoded.HasValue)
                    {
                        if (time - previousTime <= 20)
                        {
                            currentStall += now - previousWall; stall += now - previousWall;
                            longestStall = Math.Max(longestStall, currentStall);
                        }
                        else currentStall = 0;
                    }
                    if (firstDecoded.HasValue) observed = now - firstDecoded.Value;
                    previousTime = time; previousWall = now;
                    var center = overlay.PointToScreen(new Point(overlay.ActualWidth / 2, overlay.ActualHeight / 2));
                    var rootHit = GetAncestor(WindowFromPoint(new System.Drawing.Point((int)center.X, (int)center.Y)), 2);
                    var visible = window.IsVisible && (rootHit == hostHandle || rootHit == overlayHandle);
                    if (visible) visibleSamples++; else occludedSamples++;
                    samples.Add(new
                    {
                        Seconds = now, Status = status, Frames = frames, TimeMs = time,
                        State = native?.State.ToString() ?? "NotCreated", CenterUnoccluded = visible,
                        StallSeconds = stall, LongestStallSeconds = longestStall,
                        StatusText = ((TextBlock)window.FindName("PlayerStatus")).Text
                    });
                    await Write();
                    if (observed >= 120)
                    {
                        outcome = "120-second visible post-start observation complete";
                        exit = 0; break;
                    }
                    if (opening.IsFaulted) await opening;
                    if (opening.IsCompleted && (native?.State is VLCState.Error or VLCState.Ended || window.TorrentSession is null))
                        throw new IOException("Player stopped before the observation completed.");
                    await Task.Delay(1000);
                }
                if (exit != 0) outcome = $"{duration}-second overall limit reached before full post-start observation";
            }
            catch (Exception error)
            {
                outcome = error.GetType().Name + ": " + error.Message;
                Console.Error.WriteLine(outcome);
            }
            finally
            {
                try
                {
                    if (window is not null) await window.ClosePlayerAsync();
                    cleanup = "Player closed and its session disposed";
                }
                catch (Exception error) { cleanup = error.GetType().Name; exit = 1; }
                try { await Write(); }
                finally { main.ExitApplication(); app.Shutdown(exit); app.Dispatcher.InvokeShutdown(); }
            }
        };
        app.MainWindow = main;
        app.Dispatcher.BeginInvoke(new Action(main.Show));
        app.Run();
        return exit;
    }
}
