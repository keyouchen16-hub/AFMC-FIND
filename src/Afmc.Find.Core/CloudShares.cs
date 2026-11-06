using System.Text.RegularExpressions;

namespace Afmc.Find.Core;

public static class CloudShares
{
    // Only recognized share paths, never a provider homepage, login page or executable URL.
    public static string? Provider(string? value)
    {
        if(value is null||value.Any(char.IsControl)||!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo.Length>0)return null;
        if(!Regex.IsMatch(uri.AbsolutePath,@"^/s/[A-Za-z0-9_-]{4,}/?$"))return null;
        return uri.Host.ToLowerInvariant() switch
        {
            "pan.quark.cn"=>"夸克网盘", "pan.baidu.com"=>"百度网盘",
            "www.alipan.com" or "www.aliyundrive.com"=>"阿里云盘",
            "www.123pan.com" or "www.123865.com"=>"123 云盘", _=>null
        };
    }
}
