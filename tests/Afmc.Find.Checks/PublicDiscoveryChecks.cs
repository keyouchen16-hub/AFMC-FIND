using Afmc.Find.Core;
using Afmc.Find.App;
using System.Net;
using System.Net.Http;

internal static class PublicDiscoveryChecks
{
    public static async Task RunAsync()
    {
        var passed=0;
        void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
        var hash="0123456789abcdef0123456789abcdef01234567";
        var magnet="magnet:?xt=urn:btih:"+hash+"&dn=Sample";
        var publicRow=PublicTorrentDiscovery.Mark(new SearchResult{Title="Sample 2020",Source="Public",Magnet=magnet},"PirateBay");
        var result=PublicTorrentDiscovery.Complete(magnet,publicRow.PublicDiscoveryAllowed);
        Check(Links.MagnetHash(result)==Links.MagnetHash(magnet)&&result.StartsWith(magnet),"public hints preserve resource identity and original parameters");
        Check(result.Split("&tr=").Length==7,"bounded six public discovery hints");
        Check(PublicTorrentDiscovery.Complete(result,true)==result,"enrichment is idempotent");
        var authenticated=magnet+"&tr="+Uri.EscapeDataString("https://tracker.example/announce?passkey=test-only");
        Check(PublicTorrentDiscovery.Complete(authenticated,true)==authenticated,"existing authenticated tracker preserved without additions");
        Check(PublicTorrentDiscovery.Complete(magnet,false)==magnet,"manual and unknown magnets are unchanged");
        Check(!PublicTorrentDiscovery.Mark(publicRow,"Torznab").PublicDiscoveryAllowed,"Torznab cannot inherit public discovery permission");
        var merged=ResultMerger.Merge([publicRow]);
        Check(ResultMerger.Merge(merged)[0].PublicDiscoveryAllowed,"public provenance survives observation round trip and repeated merge");
        Check(!new SearchResult{Title="Old record",Source="Unknown"}.PublicDiscoveryAllowed,"old records default to no added public trackers");
        using var service=new SearchService(new Handler(hash));
        SearchResult? fetched=null;
        await service.SearchAsync("sample",[new SourceConfig{Kind="PirateBay",Name="Renamed public source"}],r=>fetched=r.Results.FirstOrDefault(),CancellationToken.None,1);
        Check(fetched?.PublicDiscoveryAllowed==true,"actual built-in public search path attaches provenance independently of display name");
        var launcher=new DownloadLauncher(service,System.IO.Path.GetFullPath(".tools/public-discovery-check"));
        var prepared=await launcher.PreparePlaybackAsync(fetched!,CancellationToken.None);
        Check(prepared.Contains("&tr=")&&Links.MagnetHash(prepared)=="btih:"+hash,"playback handoff supplies public tracker hints for fetched result");
        Console.WriteLine($"PUBLIC DISCOVERY PASSED {passed}");
    }
    private sealed class Handler(string hash):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("[{\"id\":1,\"name\":\"Sample 2020\",\"info_hash\":\""+hash+"\",\"size\":1024,\"seeders\":10,\"category\":201,\"added\":1700000000}]")});
    }
}
