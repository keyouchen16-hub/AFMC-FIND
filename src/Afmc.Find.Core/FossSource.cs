using System.Globalization;
namespace Afmc.Find.Core;

public static class FossSource
{
    public const string Feed = "https://fosstorrents.com/feed/torrents.xml";
    public static IReadOnlyList<SearchResult> Parse(byte[] bytes, string query)
    {
        var xml = SearchService.Xml(bytes);
        var channel = xml.Root?.Element("channel");
        if (xml.Root?.Name.LocalName != "rss" || channel?.Element("title")?.Value.StartsWith("FOSS Torrents - RSS Feed for Torrent Files", StringComparison.Ordinal) != true)
            throw new SourceException("FOSS Torrents 未返回有效种子目录。");
        var items = channel.Elements("item").Take(10000).ToArray();
        var valid = new List<SearchResult>();
        foreach (var item in items)
        {
            var title = item.Element("title")?.Value.Trim();
            if (title is null || title.Length is < 1 or > 1000 || title.Any(char.IsControl)) continue;
            if (!Uri.TryCreate(item.Element("link")?.Value, UriKind.Absolute, out var url) || url.Scheme != "https" || url.Host != "fosstorrents.com" || !url.IsDefaultPort || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0 || !url.AbsolutePath.StartsWith("/direct-files/", StringComparison.Ordinal)) continue;
            var filename = Uri.UnescapeDataString(url.AbsolutePath[14..]);
            if (filename.Length == 0 || !filename.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) || filename.Contains('/') || filename.Contains('\\') || filename.Contains("..") || filename.Any(char.IsControl)) continue;
            // Feed GUID is not a verified BitTorrent infohash; resolve and validate the .torrent when opening.
            valid.Add(new() {Title=title, Source="FOSS Torrents", TorrentUrl=url.AbsoluteUri, DetailsUrl="https://fosstorrents.com/", Category="软件",
                Published=DateTimeOffset.TryParse(item.Element("pubDate")?.Value, CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)?date:null});
        }
        if (items.Length > 0 && valid.Count == 0) throw new SourceException("FOSS Torrents 目录缺少有效种子地址。");
        var terms = SearchQuality.Canonical(query).Split(' ',StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return [];
        return valid.Where(r=>terms.All(t=>SearchQuality.Canonical(r.Title).Contains(t,StringComparison.Ordinal))).DistinctBy(r=>r.TorrentUrl).Take(100).ToArray();
    }
}
public sealed partial class SearchService
{
    private async Task<IReadOnlyList<SearchResult>> FossAsync(string query,CancellationToken ct)=>FossSource.Parse(await MediaBytes(FossSource.Feed,ct).ConfigureAwait(false),query);
}
