using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
namespace Afmc.Find.Core;

public static class SearchQuality
{
    private static string Replace(string s,string pattern,string value)=>Regex.Replace(s,pattern,value,RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1));
    private static int ChineseNumber(string s)
    {
        if(int.TryParse(s,out var n))return n;
        const string digits="零一二三四五六七八九";
        if(s=="十")return 10;
        if(s.Contains('十')){var parts=s.Split('十');return (parts[0].Length==0?1:digits.IndexOf(parts[0][0]))*10+(parts[1].Length==0?0:digits.IndexOf(parts[1][0]));}
        return s.Length==1?digits.IndexOf(s[0]):-1;
    }
    public static string Canonical(string text)
    {
        var s=text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        s=Replace(s,@"(?<=[\u3400-\u9fff])((?:19|20)\d{2})(?!\d)"," $1 ");
        s=s.Replace("中文字幕","中字",StringComparison.Ordinal);
        s=Regex.Replace(s,@"第([0-9一二三四五六七八九十]+)(季|集)",m=>{var n=ChineseNumber(m.Groups[1].Value);return n>=0?" "+(m.Groups[2].Value=="季"?"s":"e")+n.ToString("00",CultureInfo.InvariantCulture)+" ":m.Value;},RegexOptions.None,TimeSpan.FromSeconds(1));
        s=Regex.Replace(s,@"\bseason[ ._-]*(\d{1,2})\b",m=>" s"+int.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture).ToString("00")+" ",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1));
        s=Regex.Replace(s,@"\b(s\d{1,2})(e\d{1,3})\b", "$1 $2",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1));
        s=Regex.Replace(s,@"\b([se])(\d{1,3})\b",m=>m.Groups[1].Value+int.Parse(m.Groups[2].Value,CultureInfo.InvariantCulture).ToString("00"),RegexOptions.None,TimeSpan.FromSeconds(1));
        s=Replace(s,@"\b4k\b","2160p");s=Replace(s,@"\b8k\b","4320p");
        s=Replace(s,@"(?<!\w)v(?=\d+\.)","");
        return Regex.Replace(s,@"[^\p{L}\p{N}]+"," ",RegexOptions.None,TimeSpan.FromSeconds(1)).Trim();
    }
    private static bool Contains(string title,string token)=>Regex.IsMatch(token,@"[\u3400-\u9fff]")?title.Contains(token,StringComparison.Ordinal):(" "+title+" ").Contains(" "+token+" ",StringComparison.Ordinal);
    private static IEnumerable<string> Constraints(string query)
    {
        var canonical=Canonical(query);
        foreach(Match match in Regex.Matches(canonical,@"\b(?:(?:19|20)\d{2}|\d{3,4}p|s\d{2,3}|e\d{2,3})\b",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1)))yield return match.Value;
        foreach(Match match in Regex.Matches(query,@"(?<!\d)\d{1,4}(?:\.\d{1,4})+(?!\d)",RegexOptions.None,TimeSpan.FromSeconds(1)))yield return Canonical(match.Value);
        foreach(var tag in new[]{"国语","粤语","中字","双语"})if(canonical.Contains(tag,StringComparison.Ordinal))yield return tag;
    }
    public static bool MatchesConstraints(string query,string title)=>Constraints(query).All(t=>Contains(Canonical(title),t));
    // A transfer/playback observation cannot establish that a result answers the query.
    public static int MatchTier(string query, string title)
    {
        var q=Canonical(query);if(q.Length==0)return 0;
        var constraints=Constraints(query).ToArray();
        var name=" "+q+" ";
        foreach(var constraint in constraints)name=name.Replace(" "+constraint+" "," ",StringComparison.Ordinal);
        var tokens=name.Split(' ',StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
        var t=Canonical(title);
        if(tokens.Length==0)return constraints.All(c=>Contains(t,c))?0:3;
        var matched=tokens.Count(token=>Contains(t,token));
        if(matched==0)return 3;
        if(matched<tokens.Length)return 2;
        return constraints.All(c=>Contains(t,c))?0:1;
    }
    private static double NameOrderScore(string query,string title)
    {
        var name=" "+Canonical(query)+" ";
        foreach(var constraint in Constraints(query))name=name.Replace(" "+constraint+" "," ",StringComparison.Ordinal);
        name=Replace(name,@"\b(?:epub|pdf|mobi|flac|mp3|aac|bluray|hevc|x264|x265)\b","");
        var tokens=name.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        if(tokens.Length==0)return 0;
        var clean=Regex.Replace(title,@"^(?:(?:\[[^\]]{1,100}\]|【[^】]{1,100}】)\s*)+","",RegexOptions.None,TimeSpan.FromSeconds(1));
        clean=Regex.Replace(clean,@"^(?:https?://)?www\.[^\s]+\s*-\s*","",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1));
        var words=Canonical(clean).Split(' ',StringSplitOptions.RemoveEmptyEntries);var position=-1;var first=-1;
        foreach(var token in tokens)
        {
            var found=Array.FindIndex(words,position+1,word=>word==token);
            if(found<0)return tokens.Length>1?-12:0;
            if(first<0)first=found;position=found;
        }
        // Name order and position outweigh popularity, without treating them as proof of content.
        var seriesBonus=0;
        if(Regex.IsMatch(Canonical(query),@"\bs\d{2,3}\b"))
        {
            var boundary=Array.FindIndex(words,w=>Regex.IsMatch(w,@"^(?:(?:19|20)\d{2}|s\d{2,3})$"));
            if(boundary>0&&words.Take(boundary).SequenceEqual(tokens))seriesBonus=24;
        }
        return 12 + (first==0?30:first<=2?12:0)+seriesBonus;
    }
    public static double Score(string query,SearchResult row,DateTimeOffset now)
    {
        var q=Canonical(query);var title=Canonical(row.Title);var tokens=q.Split(' ',StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
        if(tokens.Length==0)return 0;
        var coverage=tokens.Count(t=>Contains(title,t))/(double)tokens.Length;
        var score=70*coverage*coverage+(q==title?5:0);
        score+=NameOrderScore(query,row.Title);
        if(Category(row)!="其他")score+=8;
        if(TechnicalSummary(row).Length>0)score+=8;
        if(q==title&&Category(row)=="其他"&&TechnicalSummary(row).Length==0)score-=10;
        const string extras=@"\b(?:addons?|plugins?|tutorials?|courses?|overview|fundamentals|guide|bible|essentials)\b|教程|课程|插件|指南|入门";
        var softwareQuery=Category(new SearchResult{Title=query,Source="query"})=="软件";
        if((softwareQuery||Regex.IsMatch(query,@"\d+\.\d+"))&&!Regex.IsMatch(query,extras,RegexOptions.IgnoreCase))
        {
            if(Regex.IsMatch(row.Title,extras,RegexOptions.IgnoreCase))score-=30;
            if(softwareQuery&&(Category(row) is "书籍" or "影视" || Regex.IsMatch(row.Title,@"\.(?:mp4|mkv|avi|webm)\b",RegexOptions.IgnoreCase)))score-=35;
        }
        if(Regex.IsMatch(row.Category,@"成人|\b(?:xxx|porn|adult)\b",RegexOptions.IgnoreCase) &&
            !Regex.IsMatch(query,@"成人|色情|\b(?:xxx|porn|adult|hentai)\b",RegexOptions.IgnoreCase))score-=80;
        score+=Math.Min(20,Math.Log10(1d+Math.Max(0,row.Seeders??0))*5);
        if(row.Published is {} date&&date<=now)score+=5/(1+(now-date).TotalDays/365);
        score-=Constraints(query).Count(t=>!Contains(title,t))*100;
        return score;
    }
    public static string Category(SearchResult row)
    {
        var text=(row.Category+" "+row.Title).ToLowerInvariant();
        if(Regex.IsMatch(text,@"dataset|科研|数据集|academic"))return "科研";
        if(Regex.IsMatch(text,@"e[- ]?books?|audio.?book|书|\.epub\b|\.pdf\b"))return "书籍";
        if(Regex.IsMatch(text,@"anime|动漫|动画"))return "动漫";
        // Explicit video metadata/file types outrank generic soundtrack words such as Dual Audio.
        if(Regex.IsMatch(row.Category.ToLowerInvariant(),@"\b(?:movies?|film|video|tv)\b|电影|影视|剧|视频") ||
           Regex.IsMatch(row.Title,@"\.(?:mkv|mp4|m4v|avi|mov|webm|m2ts)\b",RegexOptions.IgnoreCase))return "影视";
        text=Regex.Replace(text,@"\b(?:dual|multi(?:ple)?)\s*[- .]?\s*audio\b","");
        if(Regex.IsMatch(text,@"music|audio|音乐|\.flac\b|\.mp3\b"))return "音乐";
        if(Regex.IsMatch(text,@"software|application|软件|linux|ubuntu|\b(?:debian|blender|libreoffice|gimp)\b|\.iso\b"))return "软件";
        if(Regex.IsMatch(text,@"game|游戏|console"))return "游戏";
        if(Regex.IsMatch(text,@"movie|film|电影|剧|tv|video|视频|\b(?:1080p|2160p|720p|bluray|web.?dl|s\d{2})\b"))return "影视";
        return "其他";
    }
    // Only infer a work identity from explicit media metadata, never from the query.
    public static IReadOnlyList<string> WorkKeys(SearchResult row)
    {
        if(row.MetadataConflict)return [];
        if(Category(row) is not ("影视" or "动漫"))return [];
        var normalized=Canonical(row.Title);var years=Regex.Matches(normalized,@"\b(?:19|20)\d{2}\b");
        if(years.Count!=1)return [];
        var raw=Regex.Match(row.Title,@"(?<!\d)(?:19|20)\d{2}(?!\d)");if(!raw.Success)return [];
        var season=Regex.Match(normalized,@"\bs\d{2,3}(?: e\d{2,3})?\b").Value;
        if(Regex.IsMatch(normalized,@"\be\d{2,3}\b")&&season.Length==0)return [];
        var prefix=row.Title[..raw.Index];
        var parts=Regex.Split(prefix,@"\s*[·|/]\s*|(?<=[\u3400-\u9fff])[\s._-]+(?=[A-Za-z])");
        return parts.Select(Canonical).Select(s=>Replace(s,@"\b(?:s\d{2,3}|e\d{2,3}|director s cut|extended|remastered|unrated)\b","").Trim())
            .Where(s=>s.Length is >=2 and <=100).Select(s=>years[0].Value+":"+season+":"+s).Distinct().ToArray();
    }
    public static IReadOnlyList<string> Aliases(string query,IEnumerable<SearchResult> rows)
    {
        var q=Canonical(query);var alternatives=new HashSet<string>(StringComparer.Ordinal){query};
        foreach(var row in rows)
        {
            if(!MatchesConstraints(query,row.Title))continue;
            var keys=WorkKeys(row);if(keys.Count!=2)continue;
            var names=keys.Select(k=>k[(k.LastIndexOf(':')+1)..]).ToArray();
            if(!names.Any(n=>Regex.IsMatch(n,@"[\u3400-\u9fff]"))||!names.Any(n=>Regex.IsMatch(n,@"[a-z]")))continue;
            for(var i=0;i<2;i++)if(q==names[i]||q.StartsWith(names[i]+" ",StringComparison.Ordinal))alternatives.Add(names[1-i]+q[names[i].Length..]);
            if(alternatives.Count>=8)break;
        }
        return alternatives.Take(8).ToArray();
    }
    public static string Explain(string query,SearchResult row)
    {
        var q=Canonical(query);var title=Canonical(row.Title);
        var match=!MatchesConstraints(query,row.Title)?"缺少指定年份 / 季集 / 版本":q.Length==0?"已收藏资源":q.Split(' ',StringSplitOptions.RemoveEmptyEntries).All(t=>Contains(title,t))?(Constraints(query).Any()?"关键词与指定条件匹配":"关键词匹配 · 请核对类别与版本"):"部分名称匹配";
        if(MatchTier(query,row.Title)==3)match="名称未匹配";
        return match+" · "+(row.Seeders>0?"来源报告有做种":row.Seeders==0?"来源报告零做种":"做种未知")+(row.MetadataConflict?" · 来源信息有差异":"");
    }
    public static string TechnicalSummary(SearchResult row)
    {
        var tags=Regex.Matches(row.Title,@"\b(?:\d{3,4}p|[48]k|BluRay|WEB[- .]?DL|x26[45]|H[ .]?26[45]|HEVC|FLAC|MP3|EPUB|PDF|S\d{1,2}E\d{1,3})\b|国语|粤语|中字|双语",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1)).Select(m=>m.Value).Distinct(StringComparer.OrdinalIgnoreCase).Take(6);
        return string.Join(" · ",tags);
    }
}
