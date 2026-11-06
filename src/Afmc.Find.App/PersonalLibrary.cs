using System.IO;
using System.Text.Json;
using Afmc.Find.Core;
namespace Afmc.Find.App;

public sealed class PersonalLibrary
{
    private readonly string directory;
    public List<string> History {get;private set;}=[];
    public List<SearchResult> Favorites {get;private set;}=[];
    public PersonalLibrary(string directory)
    {
        this.directory=directory;
        History=Read<List<string>>("history.dat")??[];
        Favorites=Read<List<SearchResult>>("favorites.dat")??[];
        if(History.Count>50||History.Any(q=>string.IsNullOrWhiteSpace(q)||q.Length>200||q.Any(char.IsControl))||Favorites.Count>500||Favorites.Any(r=>string.IsNullOrWhiteSpace(r.Title)||r.Title.Length>1000))throw new IOException("个人记录格式无效，原文件已保留。");
    }
    private T? Read<T>(string name)
    {
        var path=Path.Combine(directory,name);if(!File.Exists(path))return default;
        if(new FileInfo(path).Length>8*1024*1024)throw new IOException("个人记录过大。");
        return JsonSerializer.Deserialize<T>(Secret.Unprotect(File.ReadAllText(path)));
    }
    private void Save<T>(string name,T value)
    {
        Directory.CreateDirectory(directory);var path=Path.Combine(directory,name);var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            var protectedText=Secret.Protect(JsonSerializer.Serialize(value));
            if(System.Text.Encoding.UTF8.GetByteCount(protectedText)>8*1024*1024)throw new IOException("个人记录超过容量限制，原记录已保留。");
            File.WriteAllText(temp,protectedText);
            for(var attempt=0;;attempt++)
            {
                try {if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);break;}
                catch(Exception e) when(attempt<6 && e is IOException or UnauthorizedAccessException && (e.HResult&0xffff) is 5 or 32 or 33 or 1175)
                {Thread.Sleep(25*(attempt+1));}
            }
        }
        finally{try{if(File.Exists(temp))File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    public void Remember(string query)
    {
        query=query.Trim();if(query.Length is <1 or >200||query.Any(char.IsControl))return;
        var next=History.Where(q=>!q.Equals(query,StringComparison.OrdinalIgnoreCase)).Prepend(query).Take(50).ToList();Save("history.dat",next);History=next;
    }
    public void ClearHistory(){Save("history.dat",Array.Empty<string>());History=[];}
    public static string Key(SearchResult row)=>Links.MagnetHash(row.Magnet)??Links.NormalizeHash(row.Hash)??row.ShareUrl??row.DetailsUrl??row.TorrentUrl??row.Source+"|"+row.Title;
    public bool IsFavorite(SearchResult row)=>Favorites.Any(r=>Key(r)==Key(row));
    public void Toggle(SearchResult row)
    {
        var exists=IsFavorite(row);var next=Favorites.Where(r=>Key(r)!=Key(row)).ToList();
        if(!exists){if(next.Count>=500)throw new IOException("收藏最多500条，请先移除部分收藏。");next.Insert(0,row);}
        Save("favorites.dat",next);Favorites=next;
    }
}
