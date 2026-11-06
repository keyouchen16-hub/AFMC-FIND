using System.Text.RegularExpressions;

namespace Afmc.Find.Core;

public static class ResourceDisplay
{
    // Display shortening is deliberately separate from matching/grouping and download identity.
    public static string Name(SearchResult row)
    {
        if(!IsVideo(row))return row.Title;
        var name = row.Title.Trim();
        var parts=name.Split(" · ",StringSplitOptions.None);
        // Only shorten an explicit bilingual release; a dot may be part of a subtitle or edition.
        if(parts.Length==2 && SearchQuality.WorkKeys(row).Count==2 &&
           Regex.IsMatch(parts[0],@"[\u3400-\u9fff]") && !Regex.IsMatch(parts[1],@"[\u3400-\u9fff]") && Regex.IsMatch(parts[1],@"[a-z]",RegexOptions.IgnoreCase) &&
           Regex.IsMatch(parts[1],@"\b(?:1080p|2160p|720p|WEB[- .]?DL|BluRay)\b",RegexOptions.IgnoreCase)) {
            var year=Regex.Match(parts[1],@"(?<!\d)(?:19|20)\d{2}(?!\d)");
            if(year.Success)name=parts[0].Trim()+" ("+year.Value+")";
        }
        var cut = Regex.Match(name,@"(?i)(?:[. _-]+)(?:2160p|1080p|720p|4320p|480p|WEB[- .]?DL|WEBRip|BluRay|BDRip|x26[45]|H[. ]?26[45])\b");
        if(cut.Success && cut.Index >= 2) name=name[..cut.Index].TrimEnd('.', ' ', '_', '-');
        return string.IsNullOrWhiteSpace(name)?row.Title:name;
    }
    public static bool IsVideo(SearchResult row) => SearchQuality.Category(row) is "影视" or "动漫";
    public static string PlaybackLabel(SearchResult row,PlaybackHealth health,DateTimeOffset now)
    {
        if(!IsVideo(row) || row.ShareUrl is not null)return "—";
        var label=health.Get(row,now).Label;
        return health.Tier(row,now) switch {
            0 => "近期起播成功",
            1 => "起播未确认",
            3 => label.StartsWith("收到数据",StringComparison.Ordinal)?"首片未就绪":label.StartsWith("本次连接超时",StringComparison.Ordinal)?"本次超时":"本次失败",
            _ => "未验证"
        };
    }
}
