using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace Afmc.Find.Core;

public static class GlobalSources
{
    public static bool Supports(string kind) => kind is "Knaben" or "BitSearch" or "Nyaa";
    public static Uri QueryUri(string kind, string query, int page) => new(kind switch
    {
        "Knaben" => "https://api.knaben.org/v2/search?q=" + Uri.EscapeDataString(query) + "&s=100&f=" + ((page-1)*100),
        "BitSearch" => "https://bitsearch.eu/api/v1/search?q=" + Uri.EscapeDataString(query) + "&limit=100&sort=relevance&page=" + page,
        "Nyaa" => "https://nyaa.si/?page=rss&q=" + Uri.EscapeDataString(query) + "&c=0_0&f=0",
        _ => throw new SourceException("未知的公开搜索源。")
    });
    private static string? Text(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static long? Number(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) && n>=0 ? n : null;
    private static string? Web(string? value)
    {
        try { var uri=value is null?null:Links.WebUri(value); return uri?.Scheme=="https" && !uri.IsLoopback ? uri.AbsoluteUri : null; }
        catch(SourceException) { return null; }
    }
    private static DateTimeOffset? Date(string? value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)?dt:null;
    private static string? Magnet(string? hash, string title, string? provided=null)
    {
        var key=Links.NormalizeHash(hash);
        var fromMagnet=Links.MagnetHash(provided);
        if(key is not null && fromMagnet is not null && key!=fromMagnet) return null;
        if(fromMagnet is not null) return provided;
        return key is null?null:"magnet:?xt=urn:"+key+"&dn="+Uri.EscapeDataString(title);
    }
    public static IReadOnlyList<SearchResult> Parse(string kind, byte[] bytes) => kind == "Nyaa" ? ParseNyaa(bytes) : ParseJson(kind,bytes);
    private static IReadOnlyList<SearchResult> ParseJson(string kind, byte[] bytes)
    {
        if(kind is not ("Knaben" or "BitSearch"))throw new SourceException("未知的 JSON 搜索源。");
        using var doc=JsonDocument.Parse(bytes,new JsonDocumentOptions {MaxDepth=32});
        var root=doc.RootElement;
        if(root.ValueKind!=JsonValueKind.Object || (kind=="BitSearch" && (!root.TryGetProperty("success",out var success) || success.ValueKind!=JsonValueKind.True)) || !root.TryGetProperty(kind=="Knaben"?"hits":"results",out var rows) || rows.ValueKind!=JsonValueKind.Array)
            throw new SourceException(kind+" 返回了无效结果或接口错误。");
        var result=new List<SearchResult>();
        foreach(var item in rows.EnumerateArray().Take(300))
        {
            if(item.ValueKind!=JsonValueKind.Object)continue;
            var title=Text(item,"title"); if(string.IsNullOrWhiteSpace(title)||title.Length>1000)continue;
            var magnet=Magnet(Text(item,kind=="Knaben"?"hash":"infohash"),title,Text(item,"magnetUrl"));
            if(magnet is null)continue;
            var origin=Text(item,"tracker");
            var source=kind=="Knaben"&&!string.IsNullOrWhiteSpace(origin)?"Knaben（"+origin[..Math.Min(origin.Length,80)]+"）":kind;
            var id=Text(item,"id");
            var category=kind=="Knaben"?Text(item,"category"):Number(item,"category") switch
            {1=>"其他",2=>"电影",3=>"剧集",4=>"动漫",5=>"软件",6=>"游戏",7=>"音乐",8=>"有声书",9=>"电子书 / 课程",10=>"成人",_=>null};
            result.Add(new SearchResult {Title=title,Source=source,Magnet=magnet,Hash=Links.MagnetHash(magnet),
                CoverUrl=ResultPresentation.SafeCover(Text(item,"coverUrl")) ?? ResultPresentation.SafeCover(Text(item,"poster")) ?? ResultPresentation.SafeCover(Text(item,"thumbnail")),
                DetailsUrl=kind=="Knaben"?Web(Text(item,"details")):id is null?null:"https://bitsearch.eu/torrent/"+Uri.EscapeDataString(id),
                Size=Number(item,kind=="Knaben"?"bytes":"size"),Seeders=Number(item,"seeders"),Category=category??"未知",
                Published=Date(Text(item,kind=="Knaben"?"date":"createdAt"))});
        }
        if(rows.GetArrayLength()>0&&result.Count==0)throw new SourceException(kind+" 未返回有效磁力哈希，接口可能已变化。");
        return result;
    }
    private static IReadOnlyList<SearchResult> ParseNyaa(byte[] bytes)
    {
        var doc=SearchService.Xml(bytes);
        if(doc.Root?.Name.LocalName!="rss" || doc.Root.Element("channel") is null)throw new SourceException("Nyaa 未返回有效 RSS。");
        XNamespace ns="https://nyaa.si/xmlns/nyaa";
        var rows=doc.Root.Element("channel")!.Elements("item").Take(100).ToList();
        var result=new List<SearchResult>();
        foreach(var item in rows)
        {
            var title=item.Element("title")?.Value;
            if(string.IsNullOrWhiteSpace(title)||title.Length>1000)continue;
            var magnet=Magnet(item.Element(ns+"infoHash")?.Value,title);
            if(magnet is null)continue;
            var details=Web(item.Element("guid")?.Value);
            if(details is not null && new Uri(details).Host!="nyaa.si")details=null;
            result.Add(new SearchResult {Title=title,Source="Nyaa",Magnet=magnet,Hash=Links.MagnetHash(magnet),DetailsUrl=details,
                Size=SiteSources.ParseSize(item.Element(ns+"size")?.Value??""),Seeders=long.TryParse(item.Element(ns+"seeders")?.Value,out var seeds)&&seeds>=0?seeds:null,
                Category=item.Element(ns+"category")?.Value??"未知",Published=Date(item.Element("pubDate")?.Value)});
        }
        if(rows.Count>0&&result.Count==0)throw new SourceException("Nyaa RSS 缺少有效哈希，接口可能已变化。");
        return result;
    }
}
