using System.IO;
using System.Text.Json;

namespace Afmc.Find.App;

public sealed record Preferences
{
    public bool CloseToTray {get;init;}=true;
    public bool MinimizeToTray {get;init;}
    public bool RememberHistory {get;init;}=true;
    public bool ReduceMotion {get;init;}
    public static Preferences Load(string directory)
    {
        var path=Path.Combine(directory,"preferences.json");
        if(!File.Exists(path))return new();
        var result=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path))??throw new IOException("设置文件为空。");
        return result;
    }
    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"preferences.json");var temp=path+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(this,new JsonSerializerOptions {WriteIndented=true}));
        if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);
    }
}
