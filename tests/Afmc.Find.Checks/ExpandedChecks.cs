using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Afmc.Find.Core;

internal static class ExpandedChecks
{
    private const string Hash="0123456789abcdef0123456789abcdef01234567";
    private static int passed;
    private static byte[] Bytes(string text)=>Encoding.UTF8.GetBytes(text);
    private static void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);passed++;Console.WriteLine("PASS "+name);}
    private static void Reject(Action action,string name){try{action();}catch(Exception ex) when(ex is SourceException or System.Xml.XmlException){Check(true,name);return;}throw new InvalidOperationException(name);}
    private static string Csv(string hash=Hash,int next=7)=>JsonSerializer.Serialize(new {torrents=new[]{new {infohash=hash,name="Ubuntu fixture",size_bytes=12345L,seeders=9,created_unix=long.MaxValue}},next});
    private static SourceConfig Source(string kind)=>SiteSources.Catalog.Single(s=>s.Kind==kind);
    private static HttpResponseMessage Response(string body)=>new(HttpStatusCode.OK){Content=new StringContent(body)};
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(send(request));}
    public static async Task Run(bool live)
    {
        var parsed=ExpandedSources.ParseCsv(Bytes(Csv()));
        Check(parsed.Next==7&&parsed.Results.Single().Hash=="btih:"+Hash,"CSV valid hash, cursor and fields");
        Check(parsed.Results.Single().Published is null,"CSV out-of-range timestamp remains unknown");
        Reject(()=>ExpandedSources.ParseCsv(Bytes(Csv("bad"))),"CSV rejects invalid hash");
        Reject(()=>ExpandedSources.ParseCsv(Bytes("{}")),"CSV rejects wrong response shape");
        Check(ExpandedSources.ParseCsv(Bytes("{\"torrents\":[],\"next\":null}")).Results.Count==0,"CSV valid empty response");
        Check(ExpandedSources.CsvUri("中文 &x=1",7).Query.Contains("%26x%3D1")&&ExpandedSources.CsvUri("x",7).Query.EndsWith("&after=7"),"CSV query encoded and cursor used");
        var row=$"<tr><td><div class='tt-name'><a href='http://itorrents.net/torrent/{Hash}.torrent'></a><a href='/ubuntu-torrent-1.html'>Ubuntu</a></div></td><td>1 Year+ - in Applications</td><td>2 GB</td><td>1,234</td><td>0</td></tr>";
        var lime=ExpandedSources.ParseLime(Bytes("<table class='table3'>"+row+"</table><table class='table2'>"+row+"</table>"));
        Check(lime.Count==1&&lime[0].Seeders==1234&&lime[0].Size==2147483648L,"Lime skips sponsored table and parses numeric fields");
        Check(lime[0].Published is null&&lime[0].Category=="Applications","Lime does not invent relative publication dates");
        Reject(()=>ExpandedSources.ParseLime(Bytes("<table class='table2'>"+row.Replace("itorrents.net","evil.invalid")+"</table>")),"Lime rejects foreign hash-cache URL");
        Reject(()=>ExpandedSources.ParseLime(Bytes("<html>challenge</html>")),"Lime challenge is failure, not empty success");
        Check(ExpandedSources.ParseLime(Bytes("<h2>No results found</h2><table class='table3'>"+row+"</table>")).Count==0,"Lime no-results ignores advertisements");
        var tokyo=$"<rss><channel><title>x - Tokyo Toshokan</title><item><title>Fixture</title><description><![CDATA[<a href='magnet:?xt=urn:btih:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'>Magnet</a><br/>Size: 12.5 MB]]></description><guid>https://www.tokyotosho.info/details.php?id=1</guid></item></channel></rss>";
        var t=ExpandedSources.ParseTokyo(Bytes(tokyo)).Single();
        Check(t.Hash=="btih:"+new string('0',40)&&t.Size==13107200&&t.Seeders is null,"Tokyo normalizes Base32 and preserves unknown seeds");
        Check(ExpandedSources.ParseTokyo(Bytes("</channel>\n</rss>")).Count==0,"Tokyo observed empty response handled");
        Reject(()=>ExpandedSources.ParseTokyo(Bytes("<rss><channel/></rss>")),"Tokyo wrong feed rejected");
        Reject(()=>ExpandedSources.ParseTokyo(Bytes("<!DOCTYPE rss [<!ENTITY x SYSTEM 'file:///C:/secret'>]><rss><channel>&x;</channel></rss>")),"RSS external entities prohibited");
        var academic=$"<rss><channel><item><title>Handwritten digits</title><infohash>{Hash}</infohash><description>MNIST dataset</description><size>100</size><category>Dataset</category></item></channel></rss>";
        var a=ExpandedSources.ParseAcademic(Bytes(academic));
        Check(ExpandedSources.SearchAcademic(a,"MNIST dataset").Count==1&&ExpandedSources.SearchAcademic(a,"MNIST absent").Count==0,"Academic title/description requires all query terms");
        var disabled=Source("TorrentsCsv") with {Enabled=false};
        Check(!SiteSources.IncludeCatalog([disabled]).Single(s=>s.Id==disabled.Id).Enabled,"Catalog migration retains disabled preference");
        var calls=0;using(var service=new SearchService(new Handler(r=>{calls++;return Response(r.RequestUri!.Query.Contains("after=7")?"{\"torrents\":[],\"next\":null}":Csv());})))
        {
            var reports=new List<SourceReport>();await service.SearchAsync("ubuntu",[Source("TorrentsCsv")],reports.Add,CancellationToken.None);
            Check(calls==2&&reports.Single().Results.Count==1&&reports[0].Error is null,"CSV follows cursor then stops on empty");
            await service.SearchAsync("ubuntu",[Source("TorrentsCsv")],_=>{},CancellationToken.None);
            Check(calls==2,"CSV pages cached across identical searches");
        }
        using(var service=new SearchService(new Handler(r=>r.RequestUri!.Query.Contains("after=")?new(HttpStatusCode.BadGateway):Response(Csv()))))
        {
            var reports=new List<SourceReport>();var summary=await service.SearchAsync("ubuntu",[Source("TorrentsCsv")],reports.Add,CancellationToken.None);
            Check(summary.Failed==1&&reports.Single().Results.Count==1&&reports[0].Error is not null,"CSV later-page failure keeps earlier valid results");
        }
        calls=0;using(var service=new SearchService(new Handler(_=>{calls++;return Response(academic);})))
        {
            var reports=new List<SourceReport>();await service.SearchAsync("mnist",[Source("Academic")],reports.Add,CancellationToken.None);await service.SearchAsync("digits",[Source("Academic")],reports.Add,CancellationToken.None);
            Check(calls==1&&reports.All(r=>r.Results.Count==1),"Academic database fetched once for different queries");
        }
        calls=0;using(var service=new SearchService(new Handler(_=>
        {
            var page=++calls;
            return Response("<table class='table2'>"+string.Concat(Enumerable.Range(1,40).Select(i=>row.Replace(Hash,((page-1)*40+i).ToString("x40"))))+"</table>");
        })))
        {
            var reports=new List<SourceReport>();await service.SearchAsync("ubuntu",[Source("LimeTorrents")],reports.Add,CancellationToken.None);
            Check(calls==3&&reports.Single().Results.Count==120,"Lime follows 40-item pages, capped at three pages");
        }
        // Captured first-party responses prove selectors match the current remote format.
        var evidence="docs/evidence/v067";var probe=Path.Combine(evidence,"probe");
        if(Directory.Exists(probe))
        {
            Check(ExpandedSources.ParseCsv(File.ReadAllBytes(Path.Combine(probe,"torrentscsv.txt"))).Results.Count==25,"CSV captured live response parsed");
            Check(ExpandedSources.ParseLime(File.ReadAllBytes(Path.Combine(probe,"limetorrents.txt"))).Count>0,"Lime captured live response parsed");
            Check(ExpandedSources.ParseTokyo(File.ReadAllBytes(Path.Combine(probe,"tokyotosho.txt"))).Count>0,"Tokyo captured live response parsed");
            Check(ExpandedSources.SearchAcademic(ExpandedSources.ParseAcademic(File.ReadAllBytes(Path.Combine(probe,"academic-database.txt"))),"mnist").Count>0,"Academic captured database search parsed");
        }
        if(live)
        {
            using var service=new SearchService();var output=new List<object>();
            foreach(var (kind,query) in new[]{("TorrentsCsv","ubuntu"),("LimeTorrents","ubuntu"),("TokyoTosho","naruto"),("Academic","mnist")})
            {
                var reports=new List<SourceReport>();var summary=await service.SearchAsync(query,[Source(kind)],reports.Add,CancellationToken.None);
                var report=reports.Single();output.Add(new {kind,query,summary,Count=report.Results.Count,report.Error,Sample=report.Results.Take(3).Select(r=>new {r.Title,r.Size,r.Seeders,r.Published,r.DetailsUrl}),ValidHashes=report.Results.All(r=>Links.MagnetHash(r.Magnet)==r.Hash)});
                Console.WriteLine($"LIVE {kind}: {report.Results.Count} results; {report.Error??"OK"}");
            }
            Directory.CreateDirectory(evidence);File.WriteAllText(Path.Combine(evidence,"expanded-live.json"),JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true}));
            // Preserve evidence before asserting remote health.
            using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence,"expanded-live.json")));
            Check(json.RootElement.EnumerateArray().All(r=>r.GetProperty("Count").GetInt32()>0&&r.GetProperty("Error").ValueKind==JsonValueKind.Null&&r.GetProperty("ValidHashes").GetBoolean()),"All four new sources return valid live results");
        }
        Console.WriteLine($"EXPANDED PASSED {passed}");
    }
}

