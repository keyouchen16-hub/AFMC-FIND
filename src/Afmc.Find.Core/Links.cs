using System.Net;
using System.Text.RegularExpressions;

namespace Afmc.Find.Core;

public static partial class Links
{
    [GeneratedRegex("^[a-fA-F0-9]{40}$")]
    private static partial Regex Hex40();
    [GeneratedRegex("^1220[a-fA-F0-9]{64}$")]
    private static partial Regex V2();
    [GeneratedRegex("^[A-Z2-7]{32}$", RegexOptions.IgnoreCase)]
    private static partial Regex Base32();
    public static string? NormalizeHash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (Hex40().IsMatch(value)) return "btih:" + value.ToLowerInvariant();
        if (value.StartsWith("btih:", StringComparison.OrdinalIgnoreCase)) return NormalizeHash(value[5..]);
        if (value.StartsWith("btmh:", StringComparison.OrdinalIgnoreCase)) value = value[5..];
        if (V2().IsMatch(value)) return "btmh:" + value.ToLowerInvariant();
        if (!Base32().IsMatch(value)) return null;
        var bytes = new List<byte>(); int bits = 0, acc = 0;
        foreach (var c in value.ToUpperInvariant())
        {
            acc = (acc << 5) | "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(c); bits += 5;
            if (bits >= 8) { bits -= 8; bytes.Add((byte)(acc >> bits)); acc &= (1 << bits) - 1; }
        }
        return "btih:" + Convert.ToHexString(bytes.ToArray()).ToLowerInvariant();
    }
    public static string? MagnetHash(string? value)
    {
        if (value is null || value.Length > 16384 || value.Any(char.IsControl) || !value.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var pair in value[8..].Split('&'))
        {
            var p = pair.Split('=', 2);
            if (p.Length != 2 || !p[0].Equals("xt", StringComparison.OrdinalIgnoreCase)) continue;
            var xt = Uri.UnescapeDataString(p[1]);
            if (xt.StartsWith("urn:btih:", StringComparison.OrdinalIgnoreCase)) { var h = NormalizeHash(xt[9..]); if (h is not null) return h; }
            if (xt.StartsWith("urn:btmh:", StringComparison.OrdinalIgnoreCase) && V2().IsMatch(xt[9..])) return "btmh:" + xt[9..].ToLowerInvariant();
        }
        return null;
    }
    public static Uri WebUri(string value)
    {
        if (value.Length > 16384 || value.Any(char.IsControl) || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 || (uri.Scheme != "https" && uri.Scheme != "http")) throw new SourceException("链接必须是有效的 HTTP(S) 地址，不能包含用户名或密码。");
        return uri;
    }
    public static Uri Endpoint(string value)
    {
        var uri = WebUri(value.Trim());
        if (uri.Fragment.Length != 0 || uri.Query.Length != 0) throw new SourceException("请填写不含查询参数的 Torznab API 地址，API Key 单独填写。");
        if (uri.Scheme == "http" && !uri.IsLoopback) throw new SourceException("远程搜索服务请使用 HTTPS；HTTP 仅允许本机地址。");
        return uri;
    }
    public static string Error(Exception e) => e switch
    {
        SourceException => e.Message,
        OperationCanceledException => "请求超时或已取消",
        HttpRequestException h when h.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "认证失败，请检查 API Key 或访问权限",
        HttpRequestException h when h.StatusCode == HttpStatusCode.TooManyRequests => "搜索源限流，请稍后重试",
        HttpRequestException => "网络请求失败，请检查搜索源连接",
        System.Xml.XmlException => "搜索源返回了无效 XML",
        System.Text.Json.JsonException => "搜索源返回了无效 JSON",
        _ => "操作失败，请检查配置或稍后重试"
    };
}
