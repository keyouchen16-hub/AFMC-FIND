using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Afmc.Find.Core;

public static class CiliXiong
{
    private static string Clean(string value)=>Regex.Replace(WebUtility.HtmlDecode(value),@"\s+"," ").Trim();
    public static IReadOnlyList<Uri> SearchLinks(string html)
    {
        if(html.Contains("系统限制的搜索时间间隔"))throw new SourceException("磁力熊要求搜索间隔至少 5 秒，请稍后重试。");
        var doc=new HtmlDocument();doc.LoadHtml(html);
        var links=(doc.DocumentNode.SelectNodes("//a[@href]")??Enumerable.Empty<HtmlNode>())
            .Select(a=>a.GetAttributeValue("href",""))
            .Where(h=>Regex.IsMatch(h,@"^/(movie|drama)/\d+\.html$"))
            .Distinct().Take(8).Select(h=>new Uri("https://www.cilixiong.cc"+h)).ToList();
        if(links.Count==0 && !html.Contains("没有找到") && !html.Contains("找到 0") && !html.Contains("没有查询到") && !html.Contains("没有搜索到相关的内容"))
            throw new SourceException("磁力熊未返回可识别的搜索结果，可能需要验证。");
        return links;
    }
    public static IReadOnlyList<SearchResult> ParseDetail(string html,Uri uri)
    {
        if(uri.Scheme!="https"||uri.Host!="www.cilixiong.cc")throw new SourceException("磁力熊详情地址不匹配。");
        var doc=new HtmlDocument();doc.LoadHtml(html);
        var title=Clean(doc.DocumentNode.SelectSingleNode("//h1")?.InnerText??"");
        if(title.Length==0)throw new SourceException("磁力熊详情缺少影片名。");
        var img=doc.DocumentNode.SelectSingleNode("//img[contains(concat(' ',normalize-space(@class),' '),' rounded-2 ')]")?.GetAttributeValue("src","");
        var poster=Uri.TryCreate(img,UriKind.Absolute,out var imageUri)&&imageUri.Scheme=="https"&&imageUri.Host=="i.nacloud.cc"?imageUri.AbsoluteUri:null;
        var result=new List<SearchResult>();
        var area=doc.DocumentNode.SelectSingleNode("//*[contains(concat(' ',normalize-space(@class),' '),' mv_down ')]");
        foreach(var a in area?.SelectNodes(".//a[starts-with(@href,'magnet:')]")??Enumerable.Empty<HtmlNode>())
        {
            var magnet=WebUtility.HtmlDecode(a.GetAttributeValue("href",""));
            if(Links.MagnetHash(magnet) is not {} hash)continue;
            var release=Clean(a.InnerText);
            var size=Regex.Match(release,@"\[(\d+(?:\.\d+)?)\s*([GMT])(?:i?B)?\]",RegexOptions.IgnoreCase);
            result.Add(new SearchResult {Title=(title+" · "+release)[..Math.Min(title.Length+3+release.Length,1000)],Source="磁力熊",Magnet=magnet,Hash=hash,DetailsUrl=uri.AbsoluteUri,
                CoverUrl=poster,Size=size.Success?SiteSources.ParseSize(size.Groups[1].Value+size.Groups[2].Value+"B"):null,Category=uri.AbsolutePath.StartsWith("/drama/")?"剧集":"电影"});
        }
        if(result.Count==0)throw new SourceException("磁力熊详情没有有效磁力链接。");
        return result;
    }
}

internal sealed class PartialSourceException(IReadOnlyList<SearchResult> results,string message):Exception(message)
{
    public IReadOnlyList<SearchResult> Results {get;}=results;
}
