using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Afmc.Find.App;
using Afmc.Find.Core;

internal static class IntegrityChecks
{
    private static int passed;
    private static void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);passed++;Console.WriteLine("PASS "+name);}
    private sealed class Handler(byte[] bytes):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});}
    public static byte[] TestTorrent(string name="AFMC-handoff-test.txt")=>Encoding.ASCII.GetBytes($"d4:infod6:lengthi1e4:name{name.Length}:{name}12:piece lengthi16384e6:pieces20:00000000000000000000ee");
    public static async Task Run()
    {
        var hash=new string('a',40);var a=new SearchResult{Title="Troy",Source="A",Hash=hash,Size=2L<<30,Published=new DateTimeOffset(2020,1,1,0,0,0,TimeSpan.Zero),CoverUrl="https://image.tmdb.org/a.jpg"};
        var b=a with {Title="Troy.2004.1080p.BluRay",Source="B",Size=4L<<30,Seeders=8,Published=a.Published!.Value.AddDays(1),CoverUrl=null,DetailsUrl="https://example.com/b"};
        var c=b with {Source="C",Size=null,Seeders=12,DetailsUrl="https://example.com/c"};
        var merged=ResultMerger.Merge([a,b,c]).Single();
        Check(merged.Title==b.Title&&merged.Size==b.Size&&merged.DetailsUrl==b.DetailsUrl,"coherent primary record selected");
        Check(merged.MetadataConflict&&merged.MetadataNote.Contains("大小存在来源差异"),"size conflict disclosed");
        Check(merged.MetadataNote.Contains("各来源发布时间不同"),"publication disagreement disclosed");
        Check(merged.MetadataNote.Contains("做种依据：C")&&merged.Seeders==12,"maximum seed count has provenance");
        Check(merged.Observations.Count==3&&merged.Observations.Single(o=>o.Source=="A").Size==a.Size,"raw source values retained");
        var json=JsonSerializer.Serialize(merged);
        Check(json==JsonSerializer.Serialize(ResultMerger.Merge([c,b,a]).Single()),"reversed arrival produces same metadata");
        Check(json==JsonSerializer.Serialize(ResultMerger.Merge([ResultMerger.Merge([a,c]).Single(),b]).Single()),"incremental merge preserves ownership");
        Check(json==JsonSerializer.Serialize(ResultMerger.Merge([merged,merged]).Single()),"repeated merge is idempotent");
        var restored=JsonSerializer.Deserialize<SearchResult>(json)!;
        Check(restored.Observations.Count==3&&restored.MetadataConflict,"provenance survives persistence");
        Check(SearchQuality.WorkKeys(merged).Count==0,"conflicting records excluded from work inference");
        Check(MainWindow.ResourceInfo(merged).Contains("A：Troy")&&MainWindow.ResourceInfo(merged).Contains("大小依据：B"),"copy information includes evidence");
        var rounding=ResultMerger.Merge([b,b with{Source="Rounded",Size=b.Size+1000}]).Single();
        Check(!rounding.MetadataConflict,"minor size rounding is not a conflict");
        var year=ResultMerger.Merge([b,b with{Source="Year",Title="Troy.2024.1080p.BluRay"}]).Single();
        Check(year.MetadataConflict&&year.MetadataNote.Contains("年份存在来源差异"),"different title years block inference");
        var known=b with {CoverUrl="https://image.tmdb.org/example.jpg"};
        Check(ResultPresentation.WithCovers([merged,known])[0].CoverUrl is null,"conflicting row does not inherit poster");
        var nohash=a with{Hash=null};Check(ResultMerger.Merge([nohash,nohash]).Count==2,"no hash never merges by title");
        var now=DateTimeOffset.UtcNow;
        var video=new SearchResult{Title="Example 4K",Source="Test",Category="电影"};
        Check(ResourceSelection.Matches(video,1,3),"4K alias matches 2160p selection");
        Check(!ResourceSelection.Matches(video,1,2),"1080p selection excludes 4K");
        Check(!ResourceSelection.Matches(video with{Title="Example"},1,2),"unknown resolution excluded when specified");
        Check(!ResourceSelection.Matches(video with{Title="Example 1080p60"},1,2),"partial resolution token is not accepted");
        Check(ResourceSelection.Matches(video with{Category="音乐",Title="Example.mp3"},2,3),"music ignores dormant video resolution");
        Check(ResourceSelection.Matches(video with{Category="数据集",Title="Example"},6,0),"dataset category matches source metadata");
        Check(!ResourceSelection.Matches(video with{Category="未知",Title="Example"},3,0),"unknown category does not impersonate document");
        double Rank(string query,string title,long seeds=1)=>SearchQuality.Score(query,new SearchResult{Title=title,Source="Test",Seeders=seeds},now);
        Check(Rank("Silver River S01E01","Silver River S01E01")>Rank("Silver River S01E01","The River Silver Story S01E01",10000),"ordered series name beats rearranged popular words");
        Check(Rank("Falcon S01","Falcon S01")>Rank("Falcon S01","Young Falcon S01",10000),"exact series title beats prefixed different title");
        Check(Rank("Falcon S01","Falcon 2010 S01")>Rank("Falcon S01","Falcon and Daughter 2025 S01",10000),"complete series name beats suffix extension");
        Check(Rank("ExampleOS 24.04","ExampleOS 24.04 desktop.iso")>Rank("ExampleOS 24.04","Recorder 3.2 for ExampleOS 24.04",10000),"software title beats another app targeting that platform");
        Check(Rank("Linux Mint","Linux Mint desktop.iso")>Rank("Linux Mint","Linux Mint comparison 2160p.mp4",10000),"software image outranks a popular introduction video");
        Check(Rank("Example 2004 1080p","Example 2004 1080p")>Rank("Example 2004 1080p","Example 2024 1080p",long.MaxValue),"name bonus never overrides explicit wrong year");
        Check(SearchQuality.Explain("Troy",a).Contains("请核对"),"short keyword does not claim verified identity");
        var withCover=ResultMerger.Merge([b with{Size=100,Source="Z",CoverUrl="https://image.tmdb.org/test.jpg"},b with{Title="Troy.2004.1080p.BluRay.HEVC",Size=100,Source="A"}]).Single();
        var inferred=ResultPresentation.WithCovers([withCover]).Single();
        Check(inferred.CoverUrl is not null&&inferred.CoverInferred&&inferred.CoverSource=="Z","retained source poster reused only through explicit film identity");
        var folder=Path.Combine(Path.GetTempPath(),"afmc-integrity-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var torrent=TestTorrent();var info=TorrentFile.Validate(torrent);using var service=new SearchService(new Handler(torrent));var launcher=new DownloadLauncher(service,folder);
        var row=new SearchResult{Title=info.Name,Source="Test",Hash=info.Hash,TorrentUrl="https://example.com/test.torrent"};
        var path=await launcher.PrepareAsync(row,CancellationToken.None);
        Check(File.Exists(path)&&TorrentFile.Validate(File.ReadAllBytes(path)).Hash==info.Hash,"torrent prepared with expected hash");
        var invoked=false;DownloadLauncher.OpenPrepared(path,folder,start=>{invoked=true;Check(start.FileName==path&&start.UseShellExecute,"torrent dispatch uses exact prepared path");});Check(invoked,"torrent dispatch invoked");
        try{await launcher.PrepareAsync(row with{Hash=hash},CancellationToken.None);throw new InvalidOperationException("wrong torrent accepted");}catch(SourceException e){Check(e.Message.Contains("不一致"),"wrong torrent hash rejected");}
        try{await launcher.PrepareAsync(row with{Hash=hash,Magnet="magnet:?xt=urn:btih:"+info.Hash},CancellationToken.None);throw new InvalidOperationException("wrong magnet accepted");}catch(SourceException e){Check(e.Message.Contains("不一致"),"wrong magnet hash rejected");}
        var magnet="magnet:?xt=urn:btih:"+info.Hash+"&dn=AFMC%20test";
        Check(await launcher.PrepareAsync(row with{Magnet=magnet},CancellationToken.None)==magnet,"matching magnet prepared unchanged");
        var sameHash = row with { Magnet=magnet, TorrentUrl=null, Observations=[ResourceObservation.From(row)] };
        var metadataPath = await launcher.PreparePlaybackAsync(sameHash,CancellationToken.None);
        Check(File.Exists(metadataPath) && TorrentFile.Validate(File.ReadAllBytes(metadataPath)).Hash==info.Hash,"playback uses matching torrent from another observation before magnet metadata exchange");
        var alternative = "magnet:?xt=urn:btih:"+hash;
        Check(await launcher.PreparePlaybackAsync(row with { Hash=hash, Magnet=alternative },CancellationToken.None)==alternative,"mismatched torrent falls back to independently verified original magnet");        DownloadLauncher.OpenPrepared(magnet,folder,start=>Check(start.FileName==magnet&&start.Arguments=="","magnet dispatched without shell concatenation"));
        try{DownloadLauncher.OpenPrepared(magnet,folder,_=>throw new Win32Exception(1155));throw new InvalidOperationException("missing association not surfaced");}catch(SourceException e){Check(e.Message.Contains("未找到"),"missing client has actionable error");}
        try{DownloadLauncher.OpenPrepared(magnet,folder,_=>throw new Win32Exception(5));throw new InvalidOperationException("client failure not surfaced");}catch(SourceException e){Check(e.Message.Contains("未能打开"),"failed client has actionable error");}
        invoked=false;try{DownloadLauncher.OpenPrepared(Path.Combine(folder,"outside.torrent"),folder,_=>invoked=true);throw new InvalidOperationException("outside path accepted");}catch(SourceException){Check(!invoked,"outside prepared directory rejected before dispatch");}
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();try{await launcher.PrepareAsync(row,cancelled.Token);throw new InvalidOperationException("cancel ignored");}catch(OperationCanceledException){Check(true,"cancelled preparation never launches");}
        var library=new PersonalLibrary(folder);library.Remember("original");var history=Path.Combine(folder,"history.dat");
        var before=File.ReadAllBytes(history);
        using(var held=new FileStream(history,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            try{library.Remember("must-not-commit");throw new InvalidOperationException("locked record overwritten");}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException){Check(library.History.SequenceEqual(["original"]),"failed commit preserves in-memory history");}
        }
        Check(File.ReadAllBytes(history).SequenceEqual(before),"failed commit preserves encrypted original");
        library.Remember("recovered");Check(new PersonalLibrary(folder).History[0]=="recovered","save recovers after file lock released");
        Directory.CreateDirectory("docs/evidence/v0810");File.WriteAllText("docs/evidence/v0810/integrity-checks.json",JsonSerializer.Serialize(new{Passed=passed,ActualExternalClientInvoked=false,Sample=merged},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"INTEGRITY PASSED {passed}");
    }
}

