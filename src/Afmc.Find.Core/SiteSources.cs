using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Afmc.Find.Core;

public static class SiteSources
{
    public static IReadOnlyList<SourceConfig> Catalog => [
        new() {Id="global-foss",Name="FOSS Torrents",Kind="Foss",Endpoint="https://fosstorrents.com/",Description="开源软件、系统与游戏目录 · 标题关键词匹配 · 单批最多100条 · 首次读取目录较慢，缓存5分钟 · 未知大小/做种不估算"},
        new() {Id="global-piratebay",Name="海盗湾",Kind="PirateBay",Endpoint="https://thepiratebay.org/index.html",Description="APIBay直连搜索 · 单批最多100条 · 标题/类别/大小/做种/时间 · 与TPB镜像结果按哈希去重"},
        new() {Id="global-dmhy",Name="动漫花园",Kind="Dmhy",Endpoint="https://share.dmhy.org",Description="中文动漫、音乐等 · 关键词RSS单批 · 做种/大小未知不估算"},
        new() {Id="global-eztv",Name="EZTV",Kind="Eztv",Endpoint="https://eztvx.to/api/",Description="剧集 · 剧名经TVmaze匹配IMDb后检索 · 最多2个同名剧/各3页 · 编号与标题双校验；中文别名可能无结果 · TVmaze CC BY-SA https://www.tvmaze.com/api"},
        new() { Id="global-linuxtracker", Name="LinuxTracker", Kind="LinuxTracker", Endpoint="https://linuxtracker.org", Description="Linux与开源软件 · 关键词搜索最多3页 · 仅使用本站报告做种，不叠加外部统计" },
        new() { Id="official-debian", Name="Debian 官方", Kind="Debian", Endpoint="https://www.debian.org/CD/torrent-cd/", Description="官方稳定版系统镜像 · amd64/arm64 CD/DVD种子目录 · 1小时缓存 · 非综合搜索源" },
        new() { Id="global-torrentscsv", Name="Torrents.csv", Kind="TorrentsCsv", Endpoint="https://torrents-csv.com", Description="综合资源 · 公开 API · 游标翻页，最多 3 页 · 5 分钟缓存" },
        new() { Id="global-limetorrents", Name="LimeTorrents", Kind="LimeTorrents", Endpoint="https://www.limetorrents.fun", Description="综合资源 · 真实结果表及哈希 · 排除广告 · 模糊日期显示未知" },
        new() { Id="global-tokyotosho", Name="Tokyo Toshokan", Kind="TokyoTosho", Endpoint="https://www.tokyotosho.info", Description="日系动漫、音乐及其他资源 · 关键词 RSS · 做种未知时不估算" },
        new() { Id="global-academic", Name="Academic Torrents", Kind="Academic", Endpoint="https://academictorrents.com", Description="科研数据集与论文 · 官方元数据本地搜索 · 会话内缓存 24 小时" },
        new() { Id="external-xincili", Name="无极磁链", Kind="Website", Endpoint="https://july.xincili01ng.top/sh/?channel=&cid=2513", Enabled=false, Description="原站入口 · 网页返回标题但无磁力；下载引导外部应用，未计入聚合" },
        new() { Id="global-cilixiong", Name="磁力熊", Kind="CiliXiong", Endpoint="https://www.cilixiong.cc", Description="电影/剧集 · 中文片名 · 每次前 8 部的磁力版本与海报" },
        new() { Id="global-knaben", Name="Knaben", Kind="Knaben", Endpoint="https://knaben.org", Description="综合聚合 · 官方 API v2 · 显示原始索引来源" },
        new() { Id="global-bitsearch", Name="BitSearch", Kind="BitSearch", Endpoint="https://bitsearch.eu", Description="综合搜索 · 官方 API · 免费额度 200 请求/IP/天" },
        new() { Id="global-nyaa", Name="Nyaa", Kind="Nyaa", Endpoint="https://nyaa.si", Description="动漫、音乐及东亚资源 · 官方 RSS · 单次最多 75 条" },
        new() { Id="site-zhongzilou", Name="种子楼", Kind="Zhongzilou", Endpoint="https://m.zhongzilou.com", Description="真实聚合 · 标题、大小、时间及磁力链接" },
        new() { Id="site-tpb", Name="TPB Proxy", Kind="Tpb", Endpoint="https://www.tpbproxypirate.com/", Enabled=true, Description="真实聚合 · 经入口公布的 thepiratebay.party 镜像查询" },
        new() { Id="site-qiankun", Name="乾坤磁力", Kind="Website", Endpoint="https://qiankun6.com/", Enabled=false, Description="导航入口 · 当前为资源站导航，不作为种子结果" },
        new() { Id="site-btaa", Name="BTAA", Kind="Website", Endpoint="https://btaa.xyz/", Enabled=false, Description="2026-11-04复查：域名停放页，无搜索服务；未计入聚合" },
        new() { Id="site-cilihezi", Name="磁力盒子", Kind="Website", Endpoint="https://www.cilihezi.top/", Enabled=false, Description="导航入口 · 当前 BT 搜索转到百度" },
        new() { Id="site-heima", Name="黑马磁力", Kind="Website", Endpoint="https://heimacili.org/", Enabled=false, Description="2026-11-04复查：已更新镜像入口；org/top搜索均403，未计入聚合" }
    ];
    public static List<SourceConfig> IncludeCatalog(IEnumerable<SourceConfig> saved)
    {
        var result=saved.ToList();
        for(var i=0;i<result.Count;i++)
        {
            var current=result[i];
            if(current.Kind=="Website" && Catalog.FirstOrDefault(s=>s.Id==current.Id) is {} known)
                result[i]=current with {Description=known.Description,Endpoint=current.Id=="site-heima"&&current.Endpoint.TrimEnd('/')=="https://heimacili.com"?known.Endpoint:current.Endpoint};
        }
        foreach(var source in Catalog) if(!result.Any(x=>x.Id==source.Id)) result.Add(source);
        return result;
    }
    public static IReadOnlyList<SearchResult> ParseZhongzilou(string html)
    {
        var doc=new HtmlDocument(); doc.LoadHtml(html);
        var blocks=doc.DocumentNode.SelectNodes("//ul[contains(concat(' ',normalize-space(@class),' '),' list-group ')]");
        if(blocks is null)
        {
            if(html.Contains("没有找到") || html.Contains("未找到") || html.Contains("没有搜索到")) return [];
            throw new SourceException("种子楼页面结构已变化或需要验证，请打开原站检查。");
        }
        var result=new List<SearchResult>();
        foreach(var block in blocks.Take(100))
        {
            var title=block.SelectSingleNode(".//li[contains(concat(' ',normalize-space(@class),' '),' title ')]//a[starts-with(@href,'/info-')]");
            if(title is null) continue;
            var path=title.GetAttributeValue("href","");
            var hash=Regex.Match(path,@"^/info-([a-fA-F0-9]{40})(?:$|[./?])",RegexOptions.None,TimeSpan.FromSeconds(1));
            if(!hash.Success) continue;
            string Field(string cls)=>Text(block.SelectSingleNode(".//dd[contains(concat(' ',normalize-space(@class),' '),' "+cls+" ')]")?.InnerText ?? "");
            var name=Text(title.InnerText); if(name.Length==0) continue;
            var h=hash.Groups[1].Value.ToLowerInvariant();
            result.Add(new() { Title=name, Source="种子楼", Hash=h, Magnet="magnet:?xt=urn:btih:"+h+"&dn="+Uri.EscapeDataString(name), DetailsUrl="https://m.zhongzilou.com"+path,
                Size=ParseSize(Field("text-size")), Category=Field("text-type"), Published=DateTimeOffset.TryParse(Field("text-time"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var dt)?dt:null });
        }
        if(result.Count==0) throw new SourceException("种子楼未返回可识别的结果；请打开原站核对。");
        return result;
    }
    private static string Text(string value)=>Regex.Replace(WebUtility.HtmlDecode(value),@"\s+"," ").Trim();
    public static long? ParseSize(string text)
    {
        var m=Regex.Match(text,@"([\d.]+)\s*(Ti?B|Gi?B|Mi?B|Ki?B|B)",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1));
        if(!m.Success || !double.TryParse(m.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)) return null;
        var factor=m.Groups[2].Value.ToUpperInvariant().Replace("I","") switch {"TB"=>Math.Pow(1024,4),"GB"=>Math.Pow(1024,3),"MB"=>1048576d,"KB"=>1024d,_=>1};
        return n>=0 && n*factor<long.MaxValue?(long)(n*factor):null;
    }
    public static int Relevance(string query,string title)
    {
        static string Normalize(string s)=>Regex.Replace(s.Normalize(NormalizationForm.FormKC).ToLowerInvariant(),@"[^\p{L}\p{N}]+"," ").Trim();
        var q=Normalize(query); var t=Normalize(title); if(q.Length==0) return 0;
        var tokens=q.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        var covered=tokens.Count(token=>Regex.IsMatch(token,@"[\u3400-\u9fff]") ? t.Contains(token,StringComparison.Ordinal) : (" "+t+" ").Contains(" "+token+" ",StringComparison.Ordinal));
        return (t==q?1000:0)+(t.Contains(q,StringComparison.Ordinal)?2000:0)+(covered==tokens.Length?1000:0)+covered*100-Math.Min(t.Length,300);
    }
    public static int Rank(string query,SearchResult result) => Relevance(query,result.Title) - (MissingConstraint(query,result.Title)?6000:0)
        + (Links.MagnetHash(result.Magnet) is not null ? 1200 : 0) + (result.Size is > 0 ? 200 : 0) + (result.Seeders is > 0 ? 100 : 0);
    private static bool MissingConstraint(string query,string title) => Regex.Matches(query,@"(?<!\d)(?:(?:19|20)\d{2}|\d{1,4}(?:\.\d{1,4})+)(?!\d)").Any(m=>!Regex.IsMatch(title,@"(?<!\d)"+Regex.Escape(m.Value)+@"(?!\d)",RegexOptions.IgnoreCase));
    public static string MatchReason(string query,string title)=>MissingConstraint(query,title)?"缺少指定年份 / 版本，匹配较弱":Relevance(query,title)>=1000?"关键词匹配 · 请核对类别与版本":"部分匹配 · 请核对名称";
    public static IReadOnlyList<SearchResult> ParseTpb(string html)
    {
        var doc=new HtmlDocument(); doc.LoadHtml(html);
        var table=doc.DocumentNode.SelectSingleNode("//*[@id='searchResult']");
        if(table is null) throw new SourceException("TPB 未返回结果表，镜像可能失效或需要验证。");
        var result=new List<SearchResult>();
        foreach(var row in table.SelectNodes(".//tr") ?? Enumerable.Empty<HtmlNode>())
        {
            var cells=row.SelectNodes("./td"); if(cells is null || cells.Count<8) continue;
            var title=cells[1].SelectSingleNode(".//a[contains(@href,'/torrent/')]");
            var magnet=WebUtility.HtmlDecode(cells[3].SelectSingleNode(".//a[starts-with(@href,'magnet:')]")?.GetAttributeValue("href","") ?? "");
            if(title is null || Links.MagnetHash(magnet) is not { } hash) continue;
            var details=new Uri(new Uri("https://thepiratebay.party"),title.GetAttributeValue("href",""));
            result.Add(new() {Title=Text(title.InnerText),Source="TPB Proxy",Magnet=magnet,Hash=hash,DetailsUrl=Links.WebUri(details.AbsoluteUri).AbsoluteUri,Category=Text(cells[0].InnerText),Size=ParseSize(Text(cells[4].InnerText)),Seeders=long.TryParse(Text(cells[5].InnerText),out var n)&&n>=0?n:null});
        }
        return result;
    }
}
