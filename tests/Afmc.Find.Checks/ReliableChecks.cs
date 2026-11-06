using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Afmc.Find.Core;
internal static class ReliableChecks
{
    private static int passed;
    private static void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);passed++;Console.WriteLine("PASS "+name);}
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> get):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>Task.FromResult(get(r));}
    private static HttpResponseMessage Ok(byte[] bytes)=>new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
    public static async Task Run(bool live)
    {
        var folder="docs/evidence/v073/";var linux=File.ReadAllBytes(folder+"linux-ubuntu.html");var debian=File.ReadAllBytes(folder+"debian.html");
        var rows=ReliableSources.ParseLinux(linux);Check(rows.Count==25&&rows.All(r=>Links.MagnetHash(r.Magnet)==r.Hash),"Linux captured search yields valid hashes");
        Check(rows[0].Seeders==9,"Linux only local seeds, not summed external count");
        Check(rows.All(r=>r.Published is null&&r.Category=="软件"),"Linux uncertain date remains unknown");
        var unsafeHtml=Encoding.UTF8.GetString(linux).Replace("href=\"/torrents/","href=\"https://evil.invalid/torrents/");
        try{ReliableSources.ParseLinux(Encoding.UTF8.GetBytes(unsafeHtml));throw new Exception("unsafe links accepted");}catch(SourceException){Check(true,"Linux rejects foreign detail/hash links");}
        try{ReliableSources.ParseLinux(File.ReadAllBytes(folder+"linux-empty.html"));throw new Exception("unavailable catalog accepted");}catch(SourceException){Check(true,"Linux unavailable catalog is failure, not empty success");}
        try{ReliableSources.ParseLinux(Encoding.UTF8.GetBytes("<html>challenge</html>"));throw new Exception("challenge accepted");}catch(SourceException){Check(true,"Linux challenge rejected");}
        Check(ReliableSources.LinuxUri("ubuntu & x",2).Query.Contains("ubuntu%20%26%20x"),"Linux query encoded and page explicit");
        var dir=ReliableSources.DebianDirectories[1];var official=ReliableSources.ParseDebian(debian,dir);
        Check(official.Count>0&&official.All(r=>r.TorrentUrl!.EndsWith(".iso.torrent")),"Debian official torrent filenames parsed");
        Check(official.All(r=>r.Size is null&&r.Seeders is null&&r.Published is null),"Debian metadata file size never becomes ISO size");
        Check(ReliableSources.Filter(official,"debian amd64").Count>0&&ReliableSources.Filter(official,"debian missing-title").Count==0,"Debian local search requires all terms");
        try{ReliableSources.ParseDebian(debian,new Uri("https://evil.invalid/"));throw new Exception("foreign directory accepted");}catch(SourceException){Check(true,"Debian directory allowlist enforced");}
        var bad=Encoding.UTF8.GetString(debian).Replace("href=\"debian-","href=\"../debian-");
        try{ReliableSources.ParseDebian(Encoding.UTF8.GetBytes(bad),dir);throw new Exception("traversal accepted");}catch(SourceException){Check(true,"Debian path traversal rejected");}
        var source=SiteSources.Catalog.Single(s=>s.Kind=="LinuxTracker");var calls=0;var reports=new List<SourceReport>();
        using(var service=new SearchService(new Handler(r=>{calls++;return Ok(linux);})))
        {
            await service.SearchAsync("ubuntu",[source],reports.Add,CancellationToken.None);
            Check(calls==2&&reports.Single().Results.Count==25,"repeated Linux page stops pagination");
            await service.SearchAsync("ubuntu",[source],_=>{},CancellationToken.None);
            Check(calls==2,"Linux repeated query uses bounded cache");
        }
        var ds=SiteSources.Catalog.Single(s=>s.Kind=="Debian");calls=0;reports.Clear();
        using(var service=new SearchService(new Handler(r=>{calls++;return Ok(debian);})))
        {
            await service.SearchAsync("debian",[ds],reports.Add,CancellationToken.None);
            await service.SearchAsync("amd64",[ds],_=>{},CancellationToken.None);
            Check(calls==4,"Debian directory cache reused across query terms");
        }
        calls=0;reports.Clear();
        using(var service=new SearchService(new Handler(r=>++calls==1?Ok(debian):new(HttpStatusCode.BadGateway))))
        {
            await service.SearchAsync("debian",[ds],reports.Add,CancellationToken.None);
            Check(reports.Single().Error is not null&&reports.Single().Results.Count>0,"Debian later directory failure preserves earlier matches");
        }
        if(live)
        {
            using var service=new SearchService();var evidence=new List<object>();
            foreach(var pair in new[]{(source,"ubuntu"),(ds,"debian")})
            {
                reports.Clear();await service.SearchAsync(pair.Item2,[pair.Item1],reports.Add,CancellationToken.None);
                var report=reports.Single();Check(report.Error is null&&report.Results.Count>0,pair.Item1.Name+" real search succeeds");
                var item=report.Results.First();var uri=item.TorrentUrl??item.DetailsUrl+"download/";
                var bytes=await service.GetBytesAsync(new Uri(uri),4*1024*1024,CancellationToken.None,true);var info=TorrentFile.Validate(bytes);
                Check(item.Hash is null||Links.NormalizeHash(info.Hash)==item.Hash,pair.Item1.Name+" real torrent metadata validates hash");
                evidence.Add(new{Source=pair.Item1.Name,Query=pair.Item2,Count=report.Results.Count,report.ElapsedMilliseconds,ValidatedTorrent=uri,info.Hash,info.Name,BodyDownloaded=false});
            }
            File.WriteAllText(folder+"reliable-live.json",JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
        }
        Console.WriteLine($"RELIABLE PASSED {passed}");
    }
}
