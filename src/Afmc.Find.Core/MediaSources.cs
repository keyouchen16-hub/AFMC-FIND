using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
namespace Afmc.Find.Core;
public static class MediaSources
{
    public static string NameQuery(string query)=>SearchQuality.Canonical(Regex.Replace(SearchQuality.Canonical(query),@"\b(?:s\d{2,3}|e\d{2,3}|\d{3,4}p|(?:19|20)\d{2}|hevc|bluray|x264|x265)\b"," ",RegexOptions.None,TimeSpan.FromSeconds(1)));
    public static IReadOnlyList<string> ShowIds(byte[] bytes,string query)
    {
        using var doc=JsonDocument.Parse(bytes);if(doc.RootElement.ValueKind!=JsonValueKind.Array)throw new SourceException("TVmaze 剧名解析响应无效。");
        var name=NameQuery(query);var ids=new List<string>();
        foreach(var entry in doc.RootElement.EnumerateArray().Take(20))
        {
            if(!entry.TryGetProperty("show",out var show)||!show.TryGetProperty("name",out var title)||SearchQuality.Canonical(title.GetString()??"")!=name)continue;
            if(show.TryGetProperty("externals",out var ext)&&ext.TryGetProperty("imdb",out var id)&&id.ValueKind==JsonValueKind.String&&Regex.IsMatch(id.GetString()!,@"^tt\d{7,10}$"))ids.Add(id.GetString()![2..]);
        }
        return ids.Distinct().Take(2).ToArray();
    }
    public static IReadOnlyList<SearchResult> Dmhy(byte[] bytes)
    {
        var xml=SearchService.Xml(bytes);var channel=xml.Root?.Element("channel");
        if(xml.Root?.Name.LocalName!="rss"||channel?.Element("title")?.Value.Contains("花園")!=true)throw new SourceException("动漫花园未返回有效RSS。");
        var items=channel.Elements("item").Take(100).ToArray();var result=new List<SearchResult>();
        foreach(var item in items)
        {
            var title=item.Element("title")?.Value.Trim();var hash=Links.MagnetHash(item.Element("enclosure")?.Attribute("url")?.Value);
            if(title is null||title.Length is <1 or >1000||hash is null)continue;
            var link=item.Element("link")?.Value;string? details=null;
            if(Uri.TryCreate(link,UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https"&&uri.Host=="share.dmhy.org"&&uri.UserInfo.Length==0&&uri.AbsolutePath.StartsWith("/topics/view/"))details=new UriBuilder(uri){Scheme="https",Port=-1}.Uri.AbsoluteUri;
            var cat=item.Element("category")?.Value??"未知";cat=cat switch{"動畫" or "季度全集"=>"动漫","音樂" or "動漫音樂" or "同人音樂"=>"音乐","漫畫"=>"书籍","遊戲"=>"游戏",_=>cat};
            result.Add(new(){Title=title,Source="动漫花园",Hash=hash,Magnet="magnet:?xt=urn:"+hash+"&dn="+Uri.EscapeDataString(title),DetailsUrl=details,Category=cat,Published=DateTimeOffset.TryParse(item.Element("pubDate")?.Value,CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)?date:null});
        }
        if(items.Length>0&&result.Count==0)throw new SourceException("动漫花园结果缺少有效磁力哈希。");return result;
    }
    public static IReadOnlyList<SearchResult> Eztv(byte[] bytes,string id,string query,out int rawCount)
    {
        using var doc=JsonDocument.Parse(bytes);var root=doc.RootElement;
        if(root.ValueKind!=JsonValueKind.Object)throw new SourceException("EZTV 响应无效。");
        if(!root.TryGetProperty("torrents",out var rows)||rows.ValueKind!=JsonValueKind.Array)
        {if(root.TryGetProperty("torrents_count",out var count)&&count.GetInt64()==0){rawCount=0;return [];}throw new SourceException("EZTV 响应无效。");}
        rawCount=rows.GetArrayLength();var result=new List<SearchResult>();var name=NameQuery(query);
        foreach(var row in rows.EnumerateArray().Take(100))
        {
            string Text(string k)=>row.TryGetProperty(k,out var p)?p.ToString():"";
            long? Number(string k)=>long.TryParse(Text(k),out var n)&&n>=0?n:null;
            var title=Text("title");var hash=Links.NormalizeHash(Text("hash"));var magnet=Text("magnet_url");
            if(title.Length is <1 or >1000||hash is null||Links.MagnetHash(magnet)!=hash||Text("imdb_id").TrimStart('0')!=id.TrimStart('0'))continue;
            if(!(" "+SearchQuality.Canonical(title)+" ").Contains(" "+name+" ",StringComparison.Ordinal)||!SearchQuality.MatchesConstraints(query,title))continue;
            var epoch=Number("date_released_unix");
            result.Add(new(){Title=title,Hash=hash,Magnet=magnet,Source="EZTV",Category="影视",Size=Number("size_bytes"),Seeders=Number("seeds"),Published=epoch is >=0 and <=253402300799?DateTimeOffset.FromUnixTimeSeconds(epoch.Value):null,DetailsUrl="https://eztvx.to/api/get-torrents?imdb_id="+id});
        }
        return result;
    }
}
public sealed partial class SearchService
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string,(DateTimeOffset Time,byte[] Data)> mediaCache=new();
    private async Task<byte[]> MediaBytes(string url,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();if(mediaCache.TryGetValue(url,out var c)&&now()-c.Time<TimeSpan.FromMinutes(5))return c.Data;
        var data=await GetBytesAsync(new Uri(url),4*1024*1024,ct).ConfigureAwait(false);
        if(mediaCache.Count>=64)foreach(var old in mediaCache.OrderBy(x=>x.Value.Time).Take(16))mediaCache.TryRemove(old.Key,out _);
        mediaCache[url]=(now(),data);return data;
    }
    private async Task<IReadOnlyList<SearchResult>> MediaAsync(string kind,string query,CancellationToken ct,int pages=3)
    {
        if(kind=="Dmhy")return MediaSources.Dmhy(await MediaBytes("https://share.dmhy.org/topics/rss/rss.xml?keyword="+Uri.EscapeDataString(query),ct).ConfigureAwait(false));
        var name=MediaSources.NameQuery(query);if(name.Length<2)return [];
        var ids=MediaSources.ShowIds(await MediaBytes("https://api.tvmaze.com/search/shows?q="+Uri.EscapeDataString(name),ct).ConfigureAwait(false),query);
        var collected=new List<SearchResult>();
        try{foreach(var id in ids)for(var page=1;page<=pages;page++)
        {
            var rows=MediaSources.Eztv(await MediaBytes($"https://eztvx.to/api/get-torrents?imdb_id={id}&limit=100&page={page}",ct).ConfigureAwait(false),id,query,out var raw);
            collected.AddRange(rows);if(raw<100)break;
        }}catch(Exception e) when(e is not OutOfMemoryException&&!ct.IsCancellationRequested&&collected.Count>0){throw new PartialSourceException(collected,"EZTV 后续请求失败："+Links.Error(e));}
        return ResultMerger.Merge(collected);
    }
}
