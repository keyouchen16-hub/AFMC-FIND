using System.Text.Json;
using System.Text.RegularExpressions;
namespace Afmc.Find.Core;
public sealed record JackettConnection(string BaseUrl, string ApiKey)
{
    public static JackettConnection Parse(string address, string key)
    {
        var uri = Links.WebUri(address.Trim());
        if (uri.Fragment.Length != 0) throw new SourceException("地址不能包含片段标记。");
        var keys = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).Where(p => Uri.UnescapeDataString(p[0]).Equals("apikey", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (keys.Length > 1) throw new SourceException("链接包含重复的 API Key。");
        if (keys.Length == 1 && keys[0].Length == 2) key = Uri.UnescapeDataString(keys[0][1].Replace("+", " "));
        key = key.Trim();
        if (key.Length == 0 || key.Length > 512 || key.Any(char.IsControl)) throw new SourceException("请填写自己 Jackett 页面上显示的 API Key。");
        var path = uri.AbsolutePath;
        var marker = path.IndexOf("/api/v2.0/", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0) path = path[..marker];
        else if (path.EndsWith("/UI/Dashboard", StringComparison.OrdinalIgnoreCase)) path = path[..^13];
        var clean = new UriBuilder(uri) { Path = path.TrimEnd('/') + "/", Query = "", Fragment = "" }.Uri;
        return new(Links.Endpoint(clean.AbsoluteUri).AbsoluteUri, key);
    }
    public IReadOnlyList<SourceConfig> ParseIndexers(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new SourceException("该地址没有返回 Jackett 搜索源列表。");
        var output = new List<SourceConfig>();
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("configured", out var c) || c.ValueKind != JsonValueKind.True) continue;
            if (!row.TryGetProperty("id", out var idValue) || idValue.ValueKind != JsonValueKind.String) continue;
            var id = idValue.GetString()!;
            if (!Regex.IsMatch(id, "^[a-zA-Z0-9_-]{1,100}$")) continue;
            var name = row.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : id;
            if (string.IsNullOrWhiteSpace(name) || name.Length > 160 || name.Any(char.IsControl)) name = id;
            var endpoint = new Uri(new Uri(BaseUrl), "api/v2.0/indexers/" + id + "/results/torznab/api").AbsoluteUri;
            if (output.Any(s => s.Endpoint == endpoint)) continue;
            output.Add(new SourceConfig { Name = name, Endpoint = endpoint, ApiKey = ApiKey, Description = "通过 Jackett 接入" });
            if (output.Count >= 1000) break;
        }
        return output;
    }
}
public sealed partial class SearchService
{
    public async Task<IReadOnlyList<SourceConfig>> JackettIndexersAsync(JackettConnection connection, CancellationToken ct)
    {
        var endpoint = Links.Endpoint(connection.BaseUrl);
        var url = new Uri(endpoint, "api/v2.0/indexers?configured=true&apikey=" + Uri.EscapeDataString(connection.ApiKey));
        return connection.ParseIndexers(await GetBytesAsync(url, 4 * 1024 * 1024, ct));
    }
}
