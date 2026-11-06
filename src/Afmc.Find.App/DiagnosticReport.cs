using System.Runtime.InteropServices;
using Afmc.Find.Core;
namespace Afmc.Find.App;
public static class DiagnosticReport
{
    public static string Build(IEnumerable<SourceReport> reports)
    {
        // Deliberately whitelist fields; never include exception text, URLs, names or queries.
        var lines=new List<string>{ProductInfo.Title,"系统："+Environment.OSVersion.VersionString,"架构："+RuntimeInformation.ProcessArchitecture,".NET："+Environment.Version,"来源状态（不含搜索词、资源标题、邮箱、地址或密钥）："};
        foreach(var r in reports)
        {
            var kind=r.SourceKind is "Archive" or "Tpb" or "Zhongzilou" or "CiliXiong" or "Knaben" or "BitSearch" or "Nyaa" or "TorrentsCsv" or "LimeTorrents" or "TokyoTosho" or "Academic"?r.SourceKind:"自定义来源";
            var code=r.FailureCode is "RATE_LIMIT" or "ACCESS" or "NETWORK" or "TIMEOUT" or "RESPONSE"?r.FailureCode:r.Error is null?"OK":"UNKNOWN";
            lines.Add($"{kind} | {code} | {r.Results.Count}条 | {r.ElapsedMilliseconds}ms | 冷却跳过={r.CooldownSkipped}");
        }
        return string.Join(Environment.NewLine,lines);
    }
}
