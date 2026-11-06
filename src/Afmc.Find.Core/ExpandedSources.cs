using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Afmc.Find.Core;

public static class ExpandedSources
{
    public static bool Supports(string kind)=>kind is "TorrentsCsv" or "LimeTorrents" or "TokyoTosho" or "Academic";
    public static Uri CsvUri(string query,long? cursor=null)=>new("https://torrents-csv.com/service/search?q="+Uri.EscapeDataString(query)+(cursor is null?"":"&after="+cursor.Value.ToString(CultureInfo.InvariantCulture)));
    public static Uri QueryUri(string kind,string query,int page=1)=>new(kind switch
    {
        "LimeTorrents"=>"https://www.limetorrents.fun/search/all/"+Uri.EscapeDataString(query)+"/seeds/"+page+"/",
        "TokyoTosho"=>"https://www.tokyotosho.info/rss.php?terms="+Uri.EscapeDataString(query),
        "Academic"=>"https://academictorrents.com/database.xml",
        _=>throw new SourceException("未知的扩展来源。")
    });
    private static string Text(string? value)=>Regex.Replace(WebUtility.HtmlDecode(value??""),@"\s+"," ",RegexOptions.None,TimeSpan.FromSeconds(1)).Trim();
    private static string? Title(string? value){var title=Text(value);return title.Length is >0 and <=1000?title:null;}
    private static string Magnet(string hash,string title)=>"magnet:?xt=urn:"+hash+"&dn="+Uri.EscapeDataString(title);
    private static long? Number(string? value)=>long.TryParse(value,NumberStyles.AllowThousands,CultureInfo.InvariantCulture,out var n)&&n>=0?n:null;
    private static string? JsonText(JsonElement row,string key)=>row.TryGetProperty(key,out var p)&&p.ValueKind==JsonValueKind.String?p.GetString():null;
    private static long? JsonNumber(JsonElement row,string key)=>row.TryGetProperty(key,out var p)&&p.ValueKind==JsonValueKind.Number&&p.TryGetInt64(out var n)&&n>=0?n:null;
    private static DateTimeOffset? Epoch(long? value)=>value is >=0 and <=253402300799?DateTimeOffset.FromUnixTimeSeconds(value.Value):null;
    public sealed record CsvPage(IReadOnlyList<SearchResult> Results,long? Next);
    public static CsvPage ParseCsv(byte[] data)
    {
        using var doc=JsonDocument.Parse(data,new JsonDocumentOptions{MaxDepth=24});var root=doc.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("torrents",out var rows)||rows.ValueKind!=JsonValueKind.Array)
            throw new SourceException("Torrents.csv 未返回有效搜索数据。");
        var results=new List<SearchResult>();
        foreach(var row in rows.EnumerateArray().Take(100))
        {
            if(row.ValueKind!=JsonValueKind.Object||Title(JsonText(row,"name")) is not {} title||Links.NormalizeHash(JsonText(row,"infohash")) is not {} hash)continue;
            results.Add(new(){Title=title,Source="Torrents.csv",Hash=hash,Magnet=Magnet(hash,title),Size=JsonNumber(row,"size_bytes"),Seeders=JsonNumber(row,"seeders"),Published=Epoch(JsonNumber(row,"created_unix")),
                DetailsUrl="https://torrents-csv.com/?q="+Uri.EscapeDataString(title)});
        }
        if(rows.GetArrayLength()>0&&results.Count==0)throw new SourceException("Torrents.csv 结果缺少有效哈希。");
        return new(results,JsonNumber(root,"next"));
    }
    public static IReadOnlyList<SearchResult> ParseLime(byte[] data)
    {
        var doc=new HtmlDocument();doc.LoadHtml(Encoding.UTF8.GetString(data));
        var table=doc.DocumentNode.SelectSingleNode("//table[contains(concat(' ',normalize-space(@class),' '),' table2 ')]");
        if(table is null)
        {
            if(doc.DocumentNode.SelectNodes("//h2")?.Any(n=>Text(n.InnerText).Equals("No results found",StringComparison.OrdinalIgnoreCase))==true)return [];
            throw new SourceException("LimeTorrents 页面结构改变或需要验证。");
        }
        var results=new List<SearchResult>();var candidates=0;
        foreach(var row in table.SelectNodes(".//tr[td]")??Enumerable.Empty<HtmlNode>())
        {
            candidates++;var cells=row.SelectNodes("./td");if(cells is null||cells.Count<5)continue;
            var link=cells[0].SelectSingleNode(".//div[contains(concat(' ',normalize-space(@class),' '),' tt-name ')]/a[starts-with(@href,'/') and contains(@href,'-torrent-')]");
            if(link is null||Title(link.InnerText) is not {} title)continue;
            var path=WebUtility.HtmlDecode(link.GetAttributeValue("href",""));
            if(!Uri.TryCreate(new Uri("https://www.limetorrents.fun"),path,out var details)||details.Scheme!="https"||details.Host!="www.limetorrents.fun"||details.UserInfo.Length>0)continue;
            // Extract the advertised infohash only; never fetch the external torrent cache.
            var hashes=(cells[0].SelectNodes(".//a[@href]")??Enumerable.Empty<HtmlNode>()).Select(a=>WebUtility.HtmlDecode(a.GetAttributeValue("href",""))).Select(value=>
            {
                if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Host is not ("itorrents.net" or "itorrents.org")||uri.UserInfo.Length>0)return null;
                var match=Regex.Match(uri.AbsolutePath,@"^/torrent/([a-fA-F0-9]{40})\.torrent$",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1));
                return match.Success?Links.NormalizeHash(match.Groups[1].Value):null;
            }).Where(h=>h is not null).Distinct().ToArray();
            if(hashes.Length!=1)continue;var hash=hashes[0]!;
            var category=Text(cells[1].InnerText);var marker=category.LastIndexOf(" - in ",StringComparison.OrdinalIgnoreCase);
            results.Add(new(){Title=title,Source="LimeTorrents",Hash=hash,Magnet=Magnet(hash,title),DetailsUrl=details.AbsoluteUri,
                Size=SiteSources.ParseSize(Text(cells[2].InnerText)),Seeders=Number(Text(cells[3].InnerText)),Category=marker>=0?category[(marker+6)..]:"未知"});
        }
        if(candidates>0&&results.Count==0)throw new SourceException("LimeTorrents 结果缺少有效哈希。");
        return results.Take(100).ToList();
    }
    public static IReadOnlyList<SearchResult> ParseTokyo(byte[] data)
    {
        // Observed upstream empty-query response is only these closing tags.
        if(Encoding.UTF8.GetString(data).Trim().Replace("\r","")=="</channel>\n</rss>")return [];
        var doc=SearchService.Xml(data);var channel=doc.Root?.Element("channel");
        if(doc.Root?.Name.LocalName!="rss"||channel is null||channel.Element("title")?.Value.Contains("Tokyo Toshokan",StringComparison.Ordinal)!=true)
            throw new SourceException("Tokyo Toshokan 未返回有效搜索 RSS。");
        var items=channel.Elements("item").Take(100).ToList();var results=new List<SearchResult>();
        foreach(var item in items)
        {
            if(Title(item.Element("title")?.Value) is not {} title)continue;
            var html=new HtmlDocument();html.LoadHtml(item.Element("description")?.Value??"");
            var magnets=(html.DocumentNode.SelectNodes("//a[starts-with(@href,'magnet:')]")??Enumerable.Empty<HtmlNode>()).Select(n=>Links.MagnetHash(WebUtility.HtmlDecode(n.GetAttributeValue("href","")))).Where(h=>h is not null).Distinct().ToArray();
            if(magnets.Length!=1)continue;var hash=magnets[0]!;
            var details=item.Element("guid")?.Value.Trim();
            if(!Uri.TryCreate(details,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="www.tokyotosho.info"||uri.UserInfo.Length>0)details=null;
            var size=Regex.Match(html.DocumentNode.InnerText,@"Size:\s*([\d.]+\s*[KMGT]?i?B)",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1));
            results.Add(new(){Title=title,Source="Tokyo Toshokan",Hash=hash,Magnet=Magnet(hash,title),DetailsUrl=details,
                Category=Title(item.Element("category")?.Value)??"未知",Size=size.Success?SiteSources.ParseSize(size.Groups[1].Value):null,
                Published=DateTimeOffset.TryParse(item.Element("pubDate")?.Value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var date)?date:null});
        }
        if(items.Count>0&&results.Count==0)throw new SourceException("Tokyo Toshokan RSS 缺少有效磁力哈希。");
        return results;
    }
    public sealed record AcademicEntry(SearchResult Result,string SearchText);
    public static IReadOnlyList<AcademicEntry> ParseAcademic(byte[] data)
    {
        var doc=SearchService.Xml(data);var channel=doc.Root?.Element("channel");
        if(doc.Root?.Name.LocalName!="rss"||channel is null)throw new SourceException("Academic Torrents 未返回有效数据库 RSS。");
        var items=channel.Elements("item").Take(10000).ToList();var results=new List<AcademicEntry>();
        foreach(var item in items)
        {
            if(Title(item.Element("title")?.Value) is not {} title||Links.NormalizeHash(item.Element("infohash")?.Value) is not {} hash)continue;
            var result=new SearchResult{Title=title,Source="Academic Torrents",Hash=hash,Magnet=Magnet(hash,title),Size=Number(item.Element("size")?.Value),Category=Title(item.Element("category")?.Value)??"科研数据",
                DetailsUrl="https://academictorrents.com/details/"+hash[5..]};
            var description=Text(item.Element("description")?.Value);if(description.Length>12000)description=description[..12000];
            results.Add(new(result,title+" "+description));
        }
        if(items.Count>0&&results.Count==0)throw new SourceException("Academic Torrents 数据库缺少有效哈希。");
        return results;
    }
    public static IReadOnlyList<SearchResult> SearchAcademic(IReadOnlyList<AcademicEntry> entries,string query)
    {
        var tokens=query.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
        return entries.Where(e=>tokens.All(t=>e.SearchText.Contains(t,StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(e=>SiteSources.Relevance(query,e.Result.Title)).Take(100).Select(e=>e.Result).ToList();
    }
}
