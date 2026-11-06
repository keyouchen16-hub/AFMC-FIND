using System.Net.Http;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Afmc.Find.Core;

namespace Afmc.Find.App;

public sealed class DownloadLauncher(SearchService service, string directory)
{
    public async Task<string> PreparePlaybackAsync(SearchResult result, CancellationToken ct)
    {
        var hash = Links.MagnetHash(result.Magnet);
        if (hash is not null && result.ShareUrl is null &&
            (result.Hash is null || Links.NormalizeHash(result.Hash) == hash))
        {
            var candidate = result.TorrentUrl ?? result.Observations.FirstOrDefault(o =>
                o.TorrentUrl is not null && (Links.NormalizeHash(o.Hash) ?? Links.MagnetHash(o.Magnet)) == hash)?.TorrentUrl;
            if (candidate is not null)
            {
                // A verified torrent includes the file list and its own discovery information.
                using var quick = CancellationTokenSource.CreateLinkedTokenSource(ct);
                quick.CancelAfter(TimeSpan.FromSeconds(2));
                try { return await PrepareAsync(result with { Magnet = null, Hash = hash, TorrentUrl = candidate }, quick.Token); }
                catch (Exception e) when (!ct.IsCancellationRequested && e is OperationCanceledException or HttpRequestException or IOException or SourceException or InvalidDataException) { }
            }
        }
        return await PrepareAsync(result, ct);
    }
    public async Task<string> PrepareAsync(SearchResult result, CancellationToken ct)
    {
        if(result.ShareUrl is not null)throw new SourceException("网盘分享请使用浏览器打开，不能作为种子交给下载软件。");
        var expected=Links.NormalizeHash(result.Hash);
        if (Links.MagnetHash(result.Magnet) is {} magnetHash)
        {
            if(expected is not null && expected!=magnetHash)throw new SourceException("磁力标识与所选资源不一致，已停止交接。");
            var verified=await service.VerifySiteMagnetAsync(result,ct);
            if(Links.MagnetHash(verified)!=magnetHash)throw new SourceException("来源返回了不同的磁力标识，已停止交接。");
            return PublicTorrentDiscovery.Complete(verified, result.PublicDiscoveryAllowed);
        }
        var url = await service.ResolveTorrentAsync(result, ct);
        var bytes = await service.GetBytesAsync(Links.WebUri(url), TorrentFile.MaxBytes, ct, true);
        var info=TorrentFile.Validate(bytes);
        if(expected is not null && expected!=Links.NormalizeHash(info.Hash))throw new SourceException("种子标识与所选资源不一致，已停止交接。请核对来源页面。");
        ct.ThrowIfCancellationRequested();
        var folder = Path.Combine(directory, "Torrents"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + ".torrent");
        await File.WriteAllBytesAsync(path, bytes, ct); return path;
    }
    public static void OpenPrepared(string value, string directory, Action<ProcessStartInfo>? start=null)
    {
        if (Links.MagnetHash(value) is null)
        {
            var folder = Path.GetFullPath(Path.Combine(directory, "Torrents")) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(value);
            if (!path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) || !path.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)) throw new SourceException("已拒绝未知文件类型。");
            TorrentFile.Validate(File.ReadAllBytes(path));
        }
        try
        {
            var info=new ProcessStartInfo(value) {UseShellExecute=true};
            if(start is null)Process.Start(info);else start(info);
        }
        catch(System.ComponentModel.Win32Exception e)
        {
            throw new SourceException(e.NativeErrorCode is 2 or 3 or 1155 ?
                "未找到可用的下载软件或文件关联。请在 Windows 默认应用中设置 magnet / .torrent，或复制磁力链接。":
                "下载软件未能打开。请检查软件是否可用，或复制磁力链接后手动添加。");
        }
    }
    public static void OpenPage(string url) => Process.Start(new ProcessStartInfo(Links.WebUri(url).AbsoluteUri) { UseShellExecute = true });
}
