using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using Afmc.Find.Core;

namespace Afmc.Find.App;

public static class PlaybackMedia
{
    public static void Configure(Media media, string path)
    {
        media.AddOption(":file-caching=500");
        if (Path.GetExtension(path).Equals(".mkv", StringComparison.OrdinalIgnoreCase)) {
            // VLC's native Matroska preloader follows end-of-file SeekHead
            // metadata before decoding. Its bundled FFmpeg demuxer supports
            // a different index-reading path while retaining seek support.
            media.AddOption(":demux=avformat");
        }
    }
}

public static class PlaybackAddress
{
    public static async Task<Uri> ValidateDirectAsync(string input, CancellationToken ct)
    {
        var uri = Links.WebUri(input);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 511);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var type = response.Content.Headers.ContentType?.MediaType ?? "";
        if (type.Contains("html", StringComparison.OrdinalIgnoreCase) || type.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("这是网页或文本地址，请粘贴视频文件直链（如 MP4、MKV），而不是详情页或网盘分享页。");
        var final = response.RequestMessage!.RequestUri!;
        if (!type.StartsWith("video/", StringComparison.OrdinalIgnoreCase) && !TorrentPlayback.IsVideo(final.AbsolutePath))
            throw new InvalidDataException("该地址未返回可识别的视频文件。请使用视频直链或种子。");
        return Links.WebUri(final.AbsoluteUri);
    }
}

// Never allow managed exceptions to escape a libVLC native callback.
public sealed class PlaybackStreamInput(Stream stream, CancellationToken token) : MediaInput
{
    public string? LastReadError { get; private set; }
    private readonly byte[] buffer = new byte[128 * 1024];
    public override bool Open(out ulong size) { size = (ulong)stream.Length; return !token.IsCancellationRequested; }
    public override int Read(IntPtr destination, uint length)
    {
        try
        {
            var count = stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(length, buffer.Length)), token).AsTask().GetAwaiter().GetResult();
            if (count > 0) Marshal.Copy(buffer, 0, destination, count);
            return count;
        }
        catch (Exception e) when (e is not OutOfMemoryException) { LastReadError = e.GetType().Name; return -1; }
    }
    public override bool Seek(ulong offset)
    {
        try { return !token.IsCancellationRequested && offset <= (ulong)stream.Length && stream.Seek((long)offset, SeekOrigin.Begin) >= 0; }
        catch (Exception e) when (e is not OutOfMemoryException) { return false; }
    }
    public override void Close() { }
}
