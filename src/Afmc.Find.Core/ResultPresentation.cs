using System.Text;
using System.Text.RegularExpressions;

namespace Afmc.Find.Core;

public static class ResultPresentation
{
    private static string Normalize(string text) => Regex.Replace(text.Normalize(NormalizationForm.FormKC).ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
    private static bool Contains(string title,string token) => Regex.IsMatch(token,@"[\u3400-\u9fff]") ? title.Contains(token,StringComparison.Ordinal) : (" "+title+" ").Contains(" "+token+" ",StringComparison.Ordinal);
    public static double Score(string query,SearchResult row,DateTimeOffset now)
    {
        return SearchQuality.Score(query,row,now);
    }
    public static string Explain(string query,SearchResult row) => SearchQuality.Explain(query,row);

    public static string? SafeCover(string? value)
    {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.UserInfo.Length>0||!uri.IsDefaultPort)return null;
        var host=uri.IdnHost.ToLowerInvariant();
        return host is "i.nacloud.cc" or "image.tmdb.org" or "m.media-amazon.com" || host=="img.doubanio.com" || host.EndsWith(".doubanio.com",StringComparison.Ordinal) || host=="archive.org"&&uri.AbsolutePath.StartsWith("/services/img/",StringComparison.Ordinal) ? uri.AbsoluteUri : null;
    }
    private static IEnumerable<string> FilmKeys(SearchResult row)
    {
        if(row.MetadataConflict)yield break;
        // Only complete film identities; never guess from the search query or publication date.
        if(Regex.IsMatch(row.Title,@"\bS\d{1,2}E?\d*\b|第.+[季集]|\bseason\b",RegexOptions.IgnoreCase))yield break;
        var years=Regex.Matches(row.Title,@"(?<!\d)(?:19|20)\d{2}(?!\d)");
        if(years.Count!=1)yield break;
        if(!(row.Category.Contains("电影")||row.Category.Contains("Movie",StringComparison.OrdinalIgnoreCase)||Regex.IsMatch(row.Title,@"\b(?:BluRay|BDRip|WEB[- .]?DL|1080p|2160p|720p)\b",RegexOptions.IgnoreCase)))yield break;
        var year=years[0];
        var prefix=row.Title[..year.Index];
        // Release names often carry a Chinese and an English alias separated by punctuation.
        var aliases=prefix.Split(" · ",StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(part=>Regex.Split(part,@"(?<=[\u3400-\u9fff])[\s._-]+(?=[A-Za-z])"));
        foreach(var alias in aliases)
        {
            var clean=Regex.Replace(Normalize(alias),@"\b(?:director.?s?\s*cut|extended|remastered|unrated)\b", "",RegexOptions.IgnoreCase);
            clean=Normalize(clean);
            if(clean.Length>=2 && clean.Length<=100)yield return year.Value+":"+clean;
        }
    }
    public static List<SearchResult> WithCovers(IEnumerable<SearchResult> rows)
    {
        var list=rows.ToList();
        var covers=new Dictionary<string,List<SearchResult>>(StringComparer.Ordinal);
        var candidates=list.Concat(list.Where(r=>!r.MetadataConflict).SelectMany(r=>r.Observations.Select(o=>o.ToResult())));
        foreach(var row in candidates.Where(r=>SafeCover(r.CoverUrl)!=null && !r.CoverInferred))
            foreach(var key in FilmKeys(row)) { if(!covers.TryGetValue(key,out var found))covers[key]=found=[];found.Add(row); }
        return list.Select(row=>
        {
            if(row.ThumbnailUrl!=null)return row;
            var matches=FilmKeys(row).Where(covers.ContainsKey).SelectMany(k=>covers[k]).ToList();
            var urls=matches.Select(r=>r.CoverUrl).Distinct().ToArray();
            return urls.Length==1?row with {CoverUrl=urls[0],CoverInferred=true,CoverSource=matches[0].Source}:row;
        }).ToList();
    }
}
