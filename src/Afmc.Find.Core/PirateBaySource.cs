using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Afmc.Find.Core;

public static class PirateBaySource
{
    public static Uri SearchUri(string query) => new("https://apibay.org/q.php?q=" + Uri.EscapeDataString(query) + "&cat=0");

    public static IReadOnlyList<SearchResult> Parse(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new SourceException("海盗湾接口未返回有效结果数组。");
        var rows = doc.RootElement.EnumerateArray().Take(100).ToArray();
        var results = new List<SearchResult>();
        var empty = false;
        foreach (var row in rows)
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            string Text(string key) => row.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number ? value.ToString() : "";
            long? Number(string key) => long.TryParse(Text(key), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 0 ? n : null;
            var title = WebUtility.HtmlDecode(Text("name")).Trim();
            var id = Number("id");
            var rawHash = Text("info_hash");
            if (rows.Length == 1 && id == 0 && title == "No results returned" && rawHash == new string('0', 40)) { empty = true; continue; }
            var hash = Links.NormalizeHash(rawHash);
            if (id is null or <= 0 || title.Length is < 1 or > 1000 || title.Any(char.IsControl) || hash is null || rawHash.All(c => c == '0')) continue;
            var category = Number("category");
            var categoryName = category switch
            {
                102 or 601 => "书籍",
                >= 100 and < 200 => "音乐",
                >= 200 and < 300 => "影视",
                >= 300 and < 400 => "软件",
                >= 400 and < 500 => "游戏",
                _ => "未知"
            };
            var epoch = Number("added");
            results.Add(new SearchResult
            {
                Title = title, Source = "海盗湾", Hash = hash,
                Magnet = "magnet:?xt=urn:" + hash + "&dn=" + Uri.EscapeDataString(title),
                DetailsUrl = "https://thepiratebay.org/description.php?id=" + id.Value.ToString(CultureInfo.InvariantCulture),
                Size = Number("size"), Seeders = Number("seeders"), Category = categoryName,
                Published = epoch is > 0 and <= 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(epoch.Value) : null
            });
        }
        if (rows.Length > 0 && results.Count == 0 && !empty) throw new SourceException("海盗湾结果缺少有效条目或磁力哈希。");
        return ResultMerger.Merge(results);
    }
}

public sealed partial class SearchService
{
    private async Task<IReadOnlyList<SearchResult>> PirateBayAsync(string query, CancellationToken ct) =>
        PirateBaySource.Parse(await MediaBytes(PirateBaySource.SearchUri(query).AbsoluteUri, ct).ConfigureAwait(false));
}
