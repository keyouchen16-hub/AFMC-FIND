using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Afmc.Find.Core;
internal static class FossChecks
{
    static int passed;
    static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
    sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> f):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>Task.FromResult(f(r));}
    public static async Task Run(bool live)
    {
        const string folder="docs/evidence/v076/";
        var data=File.ReadAllBytes(folder+"foss-feed-complete.xml");
        var rows=FossSource.Parse(data,"Blender");
        Check(rows.Count>0&&rows.All(r=>r.Title.Contains("Blender",StringComparison.OrdinalIgnoreCase)),"FOSS captured title search");
        Check(FossSource.Parse(data,"Blender Windows").All(r=>r.Title.Contains("Windows")),"FOSS all query terms required");
        Check(rows.All(r=>r.TorrentUrl!.StartsWith("https://fosstorrents.com/direct-files/")&&r.TorrentUrl.EndsWith(".torrent")),"FOSS uses verified directory torrent URLs");
        Check(rows.All(r=>r.Hash is null&&r.Magnet is null&&r.Size is null&&r.Seeders is null),"FOSS GUID and unknown fields are not fabricated metadata");
        Check(FossSource.Parse(data,"unmatched_afmc_q9846").Count==0,"FOSS legitimate no match");
        Check(FossSource.Parse(data,"").Count==0,"FOSS empty query does not enumerate feed");
        foreach(var link in new[]{"https://evil.invalid/direct-files/demo.torrent","https://fosstorrents.com/direct-files/%2e%2e%2fsecret.torrent","https://fosstorrents.com/direct-files/demo.torrent?redirect=1","http://fosstorrents.com/direct-files/demo.torrent"})
        {
            var xml="<rss><channel><title>FOSS Torrents - RSS Feed for Torrent Files</title><item><title>Demo</title><link>"+link+"</link></item></channel></rss>";
            try{FossSource.Parse(Encoding.UTF8.GetBytes(xml),"Demo");throw new Exception("unsafe link accepted");}catch(SourceException){Check(true,"FOSS unsafe torrent link rejected");}
        }
        try{FossSource.Parse(Encoding.UTF8.GetBytes("<html/>"),"Demo");throw new Exception("challenge accepted");}catch(SourceException){Check(true,"FOSS challenge is error");}
        var source=SiteSources.Catalog.Single(s=>s.Kind=="Foss");var calls=0;var reports=new List<SourceReport>();
        using(var service=new SearchService(new Handler(r=>{calls++;return new(HttpStatusCode.OK){Content=new ByteArrayContent(data)};})))
        {await service.SearchAsync("Blender",[source],reports.Add,CancellationToken.None);await service.SearchAsync("Krita",[source],reports.Add,CancellationToken.None);Check(calls==1&&reports.All(r=>r.Results.Count>0&&r.Error is null),"FOSS one feed reused for distinct queries");Check(reports[0].Results.Count==rows.Count&&rows.Count>1,"FOSS distinct torrent files sharing a source page stay separate");}
        if(live)
        {
            var evidence=new List<object>();using var service=new SearchService();
            foreach(var query in new[]{"Blender","Krita"})
            {
                reports.Clear();await service.SearchAsync(query,[source],reports.Add,CancellationToken.None);var report=reports.Single();
                if(report.Error is not null)File.WriteAllText(folder+"foss-live-error.txt",report.Error);
                Check(report.Error is null&&report.Results.Count>0,"FOSS live "+query);
                var item=report.Results.First();var torrent=await service.GetBytesAsync(new Uri(item.TorrentUrl!),4*1024*1024,CancellationToken.None,true);
                var info=TorrentFile.Validate(torrent);
                var expected=Uri.UnescapeDataString(new Uri(item.TorrentUrl!).Segments.Last())[..^8];
                Check(info.Name==expected,"FOSS real torrent filename matches catalog");
                evidence.Add(new{Query=query,Count=report.Results.Count,report.ElapsedMilliseconds,Torrent=item.TorrentUrl,info.Hash,info.Name,BodyDownloaded=false});
                File.WriteAllText(folder+"foss-live.json",JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
            }
        }
        Console.WriteLine($"FOSS PASSED {passed}");
    }
}
