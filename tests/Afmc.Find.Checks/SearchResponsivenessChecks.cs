using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Afmc.Find.Core;

internal static class SearchResponsivenessChecks
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);passed++;Console.WriteLine("PASS "+name);}
    sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>send(r,ct);}
    static SourceConfig Source(string name,string? host=null,bool browser=false)=>new(){Id=name,Name=name,Endpoint=$"https://{host??name}.invalid/api",DiscoveryFormat=browser?"BrowserSearch":""};
    static HttpResponseMessage Rss(int count=1)=>new(HttpStatusCode.OK){Content=new StringContent("<rss><channel>"+string.Join("",Enumerable.Range(1,count).Select(i=>$"<item><title>Example {i}</title><enclosure url='magnet:?xt=urn:btih:{i:x40}' type='application/x-bittorrent'/></item>"))+"</channel></rss>")};
    public static async Task Run()
    {
        var now=DateTimeOffset.UtcNow;
        var ranking=new SearchRanking(now);var health=new PlaybackHealth();
        using var cases=JsonDocument.Parse(File.ReadAllText("tests/Afmc.Find.Checks/Fixtures/search-quality.json"));
        foreach(var item in cases.RootElement.EnumerateArray())
        {
            var q=item.GetProperty("query").GetString()!;
            var good=new SearchResult{Title=item.GetProperty("good").GetString()!,Source="fixture",Category=item.GetProperty("category").GetString()!,Hash=new string('1',40),Seeders=1};
            var wrong=good with {Title=item.GetProperty("bad").GetString()!,Hash=new string('2',40),Seeders=1000000};
            health.Record(wrong,PlaybackEvidence.Playable,now);
            Check(SearchRanking.Order([wrong,good],r=>ranking.Evaluate([q],r),health,now).First()==good,"matching name and conditions outrank playable wrong result: "+q);
        }
        foreach(var q in new[]{"少林足球","蜘蛛侠","机器学习","Beethoven","Blender","Pride and Prejudice"})
        {
            var good=new SearchResult{Title=q+" 2020",Source="fixture",Hash=new string('3',40)};
            var unrelated=good with {Title="Completely unrelated popular 1080p",Hash=new string('4',40),Seeders=10000000};
            health.Record(unrelated,PlaybackEvidence.Playable,now);
            Check(health.Order([unrelated,good],r=>SearchQuality.Score(q,r,now),now).First()==unrelated,"old playback-first defect reproduced: "+q);
            Check(ranking.Evaluate([q],unrelated).Tier==3 && SearchRanking.Order([unrelated,good],r=>ranking.Evaluate([q],r),health,now).First()==good,"unrelated title excluded regardless of popularity/playback: "+q);
        }
        var bridge=new SearchResult{Title="少林足球 · Shaolin Soccer 2001 1080p",Source="fixture",Category="电影"};
        var english=bridge with {Title="Shaolin Soccer 2001 720p"};
        var aliases=SearchQuality.Aliases("少林足球",[bridge,english]);
        Check(ranking.Evaluate(aliases,english).Tier==0,"explicit bilingual alias retains English version");
        var exact=english with {Hash=new string('5',40)};var alternative=english with {Hash=new string('6',40)};
        health.Record(alternative,PlaybackEvidence.Playable,now);
        Check(SearchRanking.Order([exact,alternative],r=>ranking.Evaluate(aliases,r),health,now).First()==alternative,"within matching names real playback remains preferred");
        Check(SearchQuality.MatchTier("RE","freedom 2024")==3,"short Latin query uses word boundaries");
        Check(SearchQuality.MatchTier("2001","2001")==0 && SearchQuality.MatchTier("2001","2024")==3,"number-only queries remain searchable");
        Check(SearchQuality.MatchTier("","anything")==0,"favorites without query remain visible");
        Check(SearchQuality.Explain("少林足球",english).StartsWith("名称未匹配"),"zero overlap is not labeled partial name match");

        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fast=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using(var service=new SearchService(new Handler(async(r,ct)=>{if(r.RequestUri!.Host.StartsWith("slow"))await release.Task.WaitAsync(ct);return Rss();})))
        {
            var task=service.SearchAsync("Example",[Source("slow1"),Source("slow2"),Source("slow3"),Source("fast")],r=>{if(r.SourceId=="fast")fast.TrySetResult();},CancellationToken.None,1);
            try {await fast.Task.WaitAsync(TimeSpan.FromSeconds(3));Check(!task.IsCompleted,"fast fourth source returns while three slow sources remain pending");}
            finally {release.TrySetResult();await task;}
        }
        release=new(TaskCreationOptions.RunContinuationsAsynchronously);fast=new(TaskCreationOptions.RunContinuationsAsynchronously);
        using(var service=new SearchService(new Handler(async(r,ct)=>{if(r.RequestUri!.Host.StartsWith("browser"))await release.Task.WaitAsync(ct);return Rss();})))
        {
            var task=service.SearchAsync("Example",[Source("browser1",browser:true),Source("browser2",browser:true),Source("fast")],r=>{if(r.SourceId=="fast")fast.TrySetResult();},CancellationToken.None,1);
            try{await fast.Task.WaitAsync(TimeSpan.FromSeconds(3));Check(!task.IsCompleted,"dynamic sources cannot occupy ordinary-source lane");}
            finally{release.TrySetResult();await task;}
        }
        int active=0,peak=0;
        using(var service=new SearchService(new Handler(async(r,ct)=>{var n=Interlocked.Increment(ref active);int old;do{old=peak;}while(n>old&&Interlocked.CompareExchange(ref peak,n,old)!=old);try{await Task.Delay(30,ct);return Rss();}finally{Interlocked.Decrement(ref active);}})))
        {await service.SearchAsync("Example",Enumerable.Range(0,8).Select(i=>Source("source"+i,"same-host")),_=>{},CancellationToken.None,1);Check(peak==2,"same host concurrency bounded to two");}
        release=new(TaskCreationOptions.RunContinuationsAsynchronously);var first=new TaskCompletionSource<SourceReport>(TaskCreationOptions.RunContinuationsAsynchronously);var final=new ConcurrentBag<SourceReport>();int calls=0;
        using(var service=new SearchService(new Handler(async(r,ct)=>{if(Interlocked.Increment(ref calls)==1)return Rss(100);await release.Task.WaitAsync(ct);return new(HttpStatusCode.BadGateway);})))
        {
            var task=service.SearchAsync("Example",[Source("paged")],final.Add,CancellationToken.None,3,r=>first.TrySetResult(r));
            try{var partial=await first.Task.WaitAsync(TimeSpan.FromSeconds(3));Check(!task.IsCompleted && !partial.IsComplete && partial.Results.Count==100,"first page arrives before later pages finish");}
            finally{release.TrySetResult();await task;}
            Check(final.Single().IsComplete&&final.Single().Error is not null&&final.Single().Results.Count==100,"later-page failure preserves early results and final error");
        }
        using(var service=new SearchService(new Handler(async(r,ct)=>{await Task.Delay(Timeout.Infinite,ct);return Rss();})))
        using(var cancel=new CancellationTokenSource(100))
        {bool canceled=false;try{await service.SearchAsync("Example",Enumerable.Range(0,15).Select(i=>Source("cancel"+i)),_=>{},cancel.Token);}catch(OperationCanceledException){canceled=true;}Check(canceled,"cancel releases running and queued searches");}
        Console.WriteLine($"SEARCH RESPONSIVENESS PASSED {passed}");
    }
    public static async Task Live()
    {
        Directory.CreateDirectory("docs/evidence/v0814");
        using var service=new SearchService();
        var sources=SiteSources.Catalog.Where(s=>s.Enabled&&s.Kind!="Website").Prepend(SourceConfig.Archive()).ToArray();
        var evidence=new List<object>();
        foreach(var query in new[]{"少林足球","Blender","Pride and Prejudice"})
        {
            var reports=new ConcurrentBag<SourceReport>();var clock=Stopwatch.StartNew();long first=0;var canceled=false;
            using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(100));
            try{await service.SearchAsync(query,sources,r=>{reports.Add(r);if(r.Results.Any(x=>SearchQuality.MatchTier(query,x.Title)==0))Interlocked.CompareExchange(ref first,clock.ElapsedMilliseconds,0);},cancel.Token,1);}catch(OperationCanceledException){canceled=true;}
            var raw=ResultMerger.Merge(reports.SelectMany(r=>r.Results));var now=DateTimeOffset.UtcNow;var rank=new SearchRanking(now);var aliases=SearchQuality.Aliases(query,raw);
            var ranked=SearchRanking.Order(raw.Where(r=>rank.Evaluate(aliases,r).Tier<3),r=>rank.Evaluate(aliases,r),new PlaybackHealth(),now).ToArray();
            var entry=new{query,Captured=now,ElapsedMs=clock.ElapsedMilliseconds,FirstNameMatchMs=first==0?(long?)null:first,SourceCount=sources.Length,Completed=reports.Count,Cancelled=canceled,Raw=raw.Count,Matching=ranked.Length,Aliases=aliases,Top=ranked.Take(10).Select(r=>new{r.Title,r.Source,Tier=rank.Evaluate(aliases,r).Tier}),Sources=reports.Select(r=>new{r.Name,r.ElapsedMilliseconds,Count=r.Results.Count,r.FailureCode})};
            evidence.Add(entry);File.WriteAllText("docs/evidence/v0814/live-search.json",JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"LIVE {query}: {ranked.Length}/{raw.Count} name-matched; first={first}ms total={clock.ElapsedMilliseconds}ms; top={ranked.FirstOrDefault()?.Title}");
        }
    }
}
