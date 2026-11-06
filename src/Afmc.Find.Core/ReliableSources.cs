using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
namespace Afmc.Find.Core;

public static class ReliableSources
{
    public static bool Supports(string kind)=>kind is "LinuxTracker" or "Debian";
    public static Uri LinuxUri(string query,int page)=>new("https://linuxtracker.org/torrents/?q="+Uri.EscapeDataString(query)+"&page="+page);
    public static IReadOnlyList<Uri> DebianDirectories=>new[]{"amd64/bt-cd/","amd64/bt-dvd/","arm64/bt-cd/","arm64/bt-dvd/"}.Select(p=>new Uri("https://cdimage.debian.org/debian-cd/current/"+p)).ToArray();
    private static string Text(string? value)=>WebUtility.HtmlDecode(value??"").Trim();
    private static HtmlDocument Document(byte[] data){var doc=new HtmlDocument();doc.LoadHtml(Encoding.UTF8.GetString(data));return doc;}
    public static IReadOnlyList<SearchResult> ParseLinux(byte[] bytes)
    {
        var doc=Document(bytes);var table=doc.DocumentNode.SelectSingleNode("//table[contains(concat(' ',normalize-space(@class),' '),' torrent-table ')]");
        // An observed empty response says the legacy catalog is unavailable; it is not proof of zero matches.
        if(table is null)throw new SourceException("LinuxTracker 目录暂不可用或页面结构改变，请稍后重试。");
        var rows=table.SelectNodes(".//tbody/tr")?.ToArray()??[];var results=new List<SearchResult>();
        foreach(var row in rows.Take(25))
        {
            var cells=row.SelectNodes("./td");var link=row.SelectSingleNode(".//a[contains(concat(' ',normalize-space(@class),' '),' torrent-name ')]");
            if(cells is null||cells.Count<5||link is null)continue;
            var title=Text(link.InnerText);var path=Text(link.GetAttributeValue("href",""));
            var match=Regex.Match(path,@"^/torrents/([a-fA-F0-9]{40})/$",RegexOptions.None,TimeSpan.FromSeconds(1));
            if(!match.Success||title.Length is <1 or >1000)continue;
            var hash=Links.NormalizeHash(match.Groups[1].Value)!;
            var seedText=Text(cells[4].SelectSingleNode("./strong")?.InnerText);
            results.Add(new(){Title=title,Source="LinuxTracker",Category="软件",Hash=hash,Magnet="magnet:?xt=urn:"+hash+"&dn="+Uri.EscapeDataString(title),DetailsUrl="https://linuxtracker.org"+path,
                Size=SiteSources.ParseSize(Text(cells[3].InnerText)),Seeders=long.TryParse(seedText,NumberStyles.AllowThousands,CultureInfo.InvariantCulture,out var n)&&n>=0?n:null});
        }
        if(rows.Length>0&&results.Count==0)throw new SourceException("LinuxTracker 结果缺少有效哈希。");
        return results;
    }
    public static IReadOnlyList<SearchResult> ParseDebian(byte[] bytes,Uri directory)
    {
        if(!DebianDirectories.Contains(directory))throw new SourceException("不受支持的官方目录。");
        var doc=Document(bytes);var table=doc.DocumentNode.SelectSingleNode("//table[@id='indexlist']");
        if(table is null)throw new SourceException("Debian 官方目录结构改变或暂不可用。");
        var results=new List<SearchResult>();
        foreach(var a in table.SelectNodes(".//td[@class='indexcolname']/a[@href]")??Enumerable.Empty<HtmlNode>())
        {
            var name=Text(a.GetAttributeValue("href",""));
            if(!Regex.IsMatch(name,@"^debian-[A-Za-z0-9.+_-]+\.iso\.torrent$",RegexOptions.None,TimeSpan.FromSeconds(1)))continue;
            results.Add(new(){Title=name[..^8],Source="Debian 官方",Category="软件",TorrentUrl=new Uri(directory,name).AbsoluteUri,DetailsUrl=directory.AbsoluteUri});
        }
        // Directory sizes are .torrent metadata sizes, NOT the ISO sizes; leave size/seeds/date unknown.
        if(results.Count==0)throw new SourceException("Debian 官方目录没有有效种子条目。");
        return results.Take(30).ToArray();
    }
    public static IReadOnlyList<SearchResult> Filter(IEnumerable<SearchResult> rows,string query)
    {
        var tokens=SearchQuality.Canonical(query).Split(' ',StringSplitOptions.RemoveEmptyEntries);
        return rows.Where(r=>tokens.All(t=>SearchQuality.Canonical(r.Title).Contains(t,StringComparison.Ordinal))).ToArray();
    }
}

public sealed partial class SearchService
{
    private async Task<IReadOnlyList<SearchResult>> ReliableAsync(string kind,string query,CancellationToken ct,int page=1)
    {
        async Task<IReadOnlyList<SearchResult>> Fetch(Uri uri,TimeSpan ttl,Func<byte[],IReadOnlyList<SearchResult>> parse)
        {
            ct.ThrowIfCancellationRequested();
            if(publicCache.TryGetValue(uri.AbsoluteUri,out var cache)&&now()-cache.Time<ttl)return cache.Results;
            var rows=parse(await GetBytesAsync(uri,2*1024*1024,ct).ConfigureAwait(false));
            if(publicCache.Count>=96)foreach(var old in publicCache.OrderBy(x=>x.Value.Time).Take(24))publicCache.TryRemove(old.Key,out _);
            publicCache[uri.AbsoluteUri]=(now(),rows);return rows;
        }
        if(kind=="LinuxTracker")return await Fetch(ReliableSources.LinuxUri(query,page),TimeSpan.FromMinutes(5),ReliableSources.ParseLinux).ConfigureAwait(false);
        if(kind!="Debian")throw new SourceException("未知来源。");
        var collected=new List<SearchResult>();
        try
        {
            foreach(var directory in ReliableSources.DebianDirectories)
                collected.AddRange(await Fetch(directory,TimeSpan.FromHours(1),bytes=>ReliableSources.ParseDebian(bytes,directory)).ConfigureAwait(false));
            return ReliableSources.Filter(collected,query);
        }
        catch(Exception e) when(e is not OutOfMemoryException && !ct.IsCancellationRequested && collected.Count>0)
        {throw new PartialSourceException(ReliableSources.Filter(collected,query),"Debian 部分目录不可用："+Links.Error(e));}
    }
}
