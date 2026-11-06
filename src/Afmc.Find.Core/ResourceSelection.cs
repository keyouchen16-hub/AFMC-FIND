using System.Text.RegularExpressions;
namespace Afmc.Find.Core;
public static class ResourceSelection
{
    public static bool Matches(SearchResult row,int category,int resolution)
    {
        var kind=SearchQuality.Category(row);
        var matches=category switch {0=>true,1=>kind is "影视" or "动漫",2=>kind=="音乐",3=>kind=="书籍",4=>kind=="软件",5=>kind=="游戏",6=>kind=="科研",_=>false};
        if(!matches)return false;
        if(category!=1||resolution==0)return true;
        var token=resolution switch {1=>"720p",2=>"1080p",3=>"2160p",4=>"4320p",_=>"invalid"};
        return Regex.IsMatch(SearchQuality.Canonical(row.Title),@"\b"+token+@"\b",RegexOptions.None,TimeSpan.FromSeconds(1));
    }
}
