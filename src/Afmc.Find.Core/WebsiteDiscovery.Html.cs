using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Afmc.Find.Core;

public sealed partial class WebsiteDiscovery
{
    private static readonly Regex MagnetText=new(@"magnet:\?[^\s<>""'\\]+",RegexOptions.IgnoreCase);
    private sealed record PageLink(string Title,string Url);
    private static bool IsContent(HtmlNode node)=>!node.AncestorsAndSelf().Any(n=>n.Name is "script" or "style" or "nav" or "footer" or "aside"||Regex.IsMatch(n.GetAttributeValue("class",""),@"(?:^|[\s_-])(?:related|recommend|recomm|sidebar|comments)(?:$|[\s_-])",RegexOptions.IgnoreCase));
    private static string LinkTitle(HtmlNode node)
    {
        var text=Clean(node.InnerText);if(text.Length<3)text=Clean(node.GetAttributeValue("title",node.SelectSingleNode(".//img")?.GetAttributeValue("alt","")??""));return text;
    }
    private static IEnumerable<PageLink> ContentLinks(Page page)
    {
        var html=Html(page.Text);
        return (html.DocumentNode.SelectNodes("//a[@href]")??Enumerable.Empty<HtmlNode>()).Where(IsContent)
            .Select(n=>new{Node=n,Title=LinkTitle(n),Url=Local(page.Url,n.GetAttributeValue("href",""))})
            .Where(x=>x.Url is not null&&x.Url!=page.Url.AbsoluteUri&&new Uri(x.Url).AbsolutePath!="/"&&x.Title.Length is >=3 and <=240
                &&!Regex.IsMatch(x.Title,@"^(?:最新投稿|最新发布|更多|首页|排行榜|我要投稿|联系我们|关于我们|登录|注册)$")
                &&!Regex.IsMatch(new Uri(x.Url).AbsolutePath,@"/(?:tag|category|page|author|user|search|login|logout|register|delete|remove|checkout)(?:/|$)",RegexOptions.IgnoreCase))
            .OrderByDescending(x=>x.Node.AncestorsAndSelf().Any(n=>n.Name is "h1" or "h2" or "h3" or "article")||x.Title.Contains('《'))
            .Select(x=>new PageLink(x.Title,x.Url!)).DistinctBy(x=>x.Url);
    }
    private static string ProbeWord(string title)
    {
        var named=Regex.Match(title,"《([^》]{2,60})》");if(named.Success)return named.Groups[1].Value;
        title=Regex.Replace(title,@"\b(?:19|20)\d{2}\b.*$","");title=Regex.Replace(title,@"[|【\[（(].*$","");
        title=Clean(title);return title.Length>60?title[..60]:title;
    }
    private async Task<IReadOnlyList<string>> AutoProbe(Page home,Budget budget,Action<string> step,CancellationToken ct)
    {
        var direct=ParseResults(home.Bytes,home.Url,home.Url.Host);
        if(direct.Count>0){var title=Html(home.Text).DocumentNode.SelectSingleNode("//h1")?.InnerText??direct[0].Title;var word=ProbeWord(title);if(word.Length>=2)return [word];}
        var links=ContentLinks(home).Take(40).ToArray();
        var probes=new List<string>();
        // Sample both ends: a category's newest posts may not yet have downloads.
        foreach(var link in links.TakeLast(3).Concat(links.Take(3)).DistinctBy(x=>x.Url))
        {
            try
            {
                step("检查本站样本详情："+link.Title);
                var page=await Fetch(new Uri(link.Url),budget,ct).ConfigureAwait(false);
                if(ParseResults(page.Bytes,page.Url,home.Url.Host).Count==0)continue;
                var word=ProbeWord(link.Title);if(word.Length>=2&&!probes.Contains(word))probes.Add(word);
                if(probes.Count>=3)break;
            }
            catch(Exception ex)when(ex is HttpRequestException or SourceException or TaskCanceledException){ct.ThrowIfCancellationRequested();}
        }
        // An observed listing title is a candidate, never proof: the search and negative control still run.
        var fallback=links.Select(l=>ProbeWord(l.Title)).FirstOrDefault(t=>t.Length>=2);
        if(probes.Count>0)return probes;
        return fallback is not null?[fallback]:throw new SourceException("未找到可自动验证的本站样本。请填写该站确有的作品或资源名，再识别一次。");
    }
    private static IEnumerable<PageLink> DownloadLinks(Page page)
    {
        var doc=Html(page.Text);
        foreach(var node in doc.DocumentNode.SelectNodes("//a[@href]")??Enumerable.Empty<HtmlNode>())
        {
            if(!IsContent(node))continue;
            var text=LinkTitle(node);var url=Local(page.Url,node.GetAttributeValue("href",""));if(url is null||url==page.Url.AbsoluteUri)continue;
            if(Regex.IsMatch(text+url,"字幕|subtitle|login|register|logout|delete|remove|purchase|checkout",RegexOptions.IgnoreCase))continue;
            if(Regex.IsMatch(text,"种子|磁力|torrent|download|下载",RegexOptions.IgnoreCase)||Regex.IsMatch(new Uri(url).AbsolutePath,@"(?:\.torrent$|/(?:download|torrent|dl)(?:[./]|$))",RegexOptions.IgnoreCase))yield return new(text,url);
        }
    }
    private static string WithContext(string title,string context)=>title.Contains(context,StringComparison.OrdinalIgnoreCase)||context==title?title:context+" · "+title;
    private async Task<IReadOnlyList<SearchResult>> FollowDetails(Page search,string query,string source,Budget budget,CancellationToken ct,bool requireComplete=false)
    {
        var results=new List<SearchResult>();
        var queue=new Queue<(PageLink Link,int Depth,string Context,string Detail)>();
        foreach(var link in ContentLinks(search).Where(l=>Matches(l.Title,query)).Take(6))queue.Enqueue((link,1,link.Title,link.Url));
        var visited=new HashSet<string>{search.Url.AbsoluteUri};
        while(queue.Count>0&&(budget.Requests<18||budget.Pages.ContainsKey(queue.Peek().Link.Url)))
        {
            var next=queue.Dequeue();if(!visited.Add(next.Link.Url))continue;
            try
            {
                var page=await Fetch(new Uri(next.Link.Url),budget,ct).ConfigureAwait(false);
                if(page.Type.Contains("bittorrent",StringComparison.OrdinalIgnoreCase)||page.Bytes.Length>0&&page.Bytes[0]=='d')
                {
                    var info=TorrentFile.Validate(page.Bytes);
                    results.Add(new(){Title=WithContext(info.Name,next.Context),Source=source,TorrentUrl=page.Url.AbsoluteUri,Hash="btih:"+info.Hash,Size=info.Size,DetailsUrl=next.Detail});continue;
                }
                var doc=Html(page.Text);var heading=Clean(doc.DocumentNode.SelectSingleNode("//h1")?.InnerText??"");
                var rows=ParseResults(page.Bytes,page.Url,source);
                if(next.Depth==1&&heading.Length>0&&!Matches(heading,query)&&!rows.Any(r=>Matches(r.Title,query)))continue;
                string? poster=null;
                if(heading.Length>0&&Matches(heading,query))
                {
                    var image=doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content","");
                    if(!string.IsNullOrEmpty(image)){try{poster=PublicUrl(new Uri(page.Url,image).AbsoluteUri).AbsoluteUri;}catch(SourceException){}catch(UriFormatException){}}
                }
                foreach(var row in rows)
                    results.Add(row with{Title=WithContext(row.Title,next.Context),DetailsUrl=page.Url.AbsoluteUri,CoverUrl=poster,CoverSource=poster is null?null:source});
                if(next.Depth<3)
                    foreach(var link in DownloadLinks(page).Where(l=>!rows.Any(r=>r.TorrentUrl==l.Url)).Take(4))queue.Enqueue((link,next.Depth+1,next.Context,next.Detail));
            }
            catch(Exception ex)when(ex is HttpRequestException or SourceException or TaskCanceledException or System.Text.Json.JsonException){ct.ThrowIfCancellationRequested();}
        }
        if(requireComplete&&queue.Count>0)throw new SourceException("本轮请求额度不足以完成对照验证，尚未确认可添加。");
        return results;
    }
    private static string ResourceTitle(HtmlNode node,HtmlDocument doc,string magnet)
    {
        var container=node.AncestorsAndSelf().FirstOrDefault(n=>n.Name is "li" or "tr")??node.ParentNode;
        if(container is not null)
        {
            var named=container.Descendants().FirstOrDefault(n=>n.Name is not ("script" or "style")&&Regex.IsMatch(n.GetAttributeValue("class",""),@"(?:^|[\s_-])(?:name|title|filename)(?:$|[\s_-])",RegexOptions.IgnoreCase));
            if(named is not null&&Clean(named.InnerText).Length>3)return Clean(named.InnerText);
            var label=container.SelectNodes(".//a[@href]")?.FirstOrDefault(a=>a!=node&&!a.GetAttributeValue("href","").StartsWith("magnet:")&&LinkTitle(a).Length>3);
            if(label is not null)return LinkTitle(label);
            var text=Clean(MagnetText.Replace(container.InnerText,""));
            if(text.Length is >3 and <300&&!Regex.IsMatch(text,@"^(?:magnet|download|下载|磁力链接|种子下载)[\s:：]*$",RegexOptions.IgnoreCase))return text;
        }
        if(magnet.Length>0)
        {
            var dn=Regex.Match(magnet,@"(?:[?&])dn=([^&]+)");if(dn.Success)return Uri.UnescapeDataString(dn.Groups[1].Value.Replace('+',' '));
        }
        return Clean(doc.DocumentNode.SelectSingleNode("//h1")?.InnerText??"");
    }
    private static IEnumerable<SearchResult> HtmlDownloads(string text,Uri page,string source)
    {
        var doc=Html(text);var rows=new List<SearchResult>();
        void Add(HtmlNode node,string? magnet,string? torrent)
        {
            var hash=Links.MagnetHash(magnet);if(hash is null&&torrent is null)return;
            var title=ResourceTitle(node,doc,magnet??"");if(title.Length is <1 or >1000)return;
            var size=Regex.Match(title,@"(?<![\p{L}\p{N}.])\d+(?:\.\d+)?\s*(?:Ti?B|Gi?B|Mi?B|Ki?B|B)(?![a-z])",RegexOptions.IgnoreCase);
            rows.Add(new(){Title=title,Source=source,Magnet=hash is null?null:magnet,Hash=hash,TorrentUrl=torrent,DetailsUrl=page.AbsoluteUri,Size=size.Success?SiteSources.ParseSize(size.Value):null});
        }
        foreach(var node in doc.DocumentNode.Descendants().Where(IsContent))
        {
            if(node.Name=="a")
            {
                var href=WebUtility.HtmlDecode(node.GetAttributeValue("href",""));
                if(Links.MagnetHash(href)is not null)Add(node,href,null);
                else if(CloudShares.Provider(href)is not null)
                {var title=ResourceTitle(node,doc,"");if(title.Length>0)rows.Add(new(){Title=title,Source=source,ShareUrl=href,DetailsUrl=page.AbsoluteUri});}
                else if(Uri.TryCreate(page,href,out var url)&&url.AbsolutePath.EndsWith(".torrent",StringComparison.OrdinalIgnoreCase))
                {try{Add(node,null,PublicUrl(url.AbsoluteUri).AbsoluteUri);}catch(SourceException){}}
            }
            foreach(var attr in new[]{"data-clipboard-text","data-magnet","data-url","value"})
                foreach(Match match in MagnetText.Matches(WebUtility.HtmlDecode(node.GetAttributeValue(attr,""))))Add(node,match.Value,null);
            if(node.NodeType==HtmlNodeType.Text)
                foreach(Match match in MagnetText.Matches(WebUtility.HtmlDecode(node.InnerText)))Add(node,match.Value,null);
        }
        return rows.DistinctBy(Identity);
    }
}
