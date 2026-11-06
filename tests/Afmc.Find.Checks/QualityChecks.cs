using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Afmc.Find.App;
using Afmc.Find.Core;
internal static class QualityChecks
{
    private static int passed;
    private static void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);passed++;Console.WriteLine("PASS "+name);}
    private static double Baseline(string query,SearchResult row)
    {
        string Normalize(string s)=>Regex.Replace(s.Normalize(NormalizationForm.FormKC).ToLowerInvariant(),@"[^\p{L}\p{N}]+"," ").Trim();
        var q=Normalize(query);var t=Normalize(row.Title);var tokens=q.Split(' ',StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
        var coverage=tokens.Count(x=>Regex.IsMatch(x,@"[\u3400-\u9fff]")?t.Contains(x):(" "+t+" ").Contains(" "+x+" "))/(double)tokens.Length;
        var score=70*coverage*coverage+(q==t?5:0)+Math.Min(20,Math.Log10(1d+Math.Max(0,row.Seeders??0))*5);
        foreach(Match m in Regex.Matches(query,@"(?<!\d)(?:(?:19|20)\d{2}|\d{3,4}p|\d{1,4}(?:\.\d{1,4})+|[248]k)(?!\w)",RegexOptions.IgnoreCase))
            if(!Regex.IsMatch(row.Title,@"(?<![\p{L}\p{N}])"+Regex.Escape(m.Value)+@"(?![\p{L}\p{N}])",RegexOptions.IgnoreCase))score-=100;
        return score;
    }
    public static async Task Run()
    {
        var now=DateTimeOffset.Parse("2026-11-04T00:00:00Z");
        using var cases=JsonDocument.Parse(File.ReadAllText("tests/Afmc.Find.Checks/Fixtures/search-quality.json"));
        var evidence=new List<object>();int oldHits=0,newHits=0;
        foreach(var item in cases.RootElement.EnumerateArray())
        {
            var query=item.GetProperty("query").GetString()!;
            var good=new SearchResult{Title=item.GetProperty("good").GetString()!,Source="Fixture",Category=item.GetProperty("category").GetString()!,Seeders=1};
            var bad=new SearchResult{Title=item.GetProperty("bad").GetString()!,Source="Fixture",Category=good.Category,Seeders=1000000};
            var rows=new[]{bad,good}.Concat(Enumerable.Range(0,8).Select(i=>new SearchResult{Title="Unrelated fixture "+i,Source="Fixture",Seeders=1000000})).ToArray();
            var before=rows.OrderByDescending(r=>Baseline(query,r)).ToArray();var after=rows.OrderByDescending(r=>ResultPresentation.Score(query,r,now)).ToArray();
            if(before[0]==good)oldHits++;if(after[0]==good)newHits++;
            evidence.Add(new{Query=query,BeforeRank=Array.IndexOf(before,good)+1,AfterRank=Array.IndexOf(after,good)+1,Top10=after.Select(r=>r.Title)});
            Check(after[0]==good,"quality benchmark: "+query);
        }
        Directory.CreateDirectory("docs/evidence/v0810");File.WriteAllText("docs/evidence/v0810/search-quality.json",JsonSerializer.Serialize(new {SyntheticControlledFixture=true,Cases=evidence,BeforeTop1=oldHits,AfterTop1=newHits},new JsonSerializerOptions{WriteIndented=true}));
        var bridge=new SearchResult{Title="特洛伊 · Troy 2004 1080p",Source="Fixture",Category="电影"};
        var english=bridge with {Title="Troy 2004 2160p"};var other=bridge with {Title="Troy 2024 1080p"};
        var aliases=SearchQuality.Aliases("特洛伊 2004",[bridge,english,other]);
        Check(aliases.Contains("troy 2004"),"bilingual explicit title provides alias");
        Check(aliases.Max(q=>ResultPresentation.Score(q,english,now))>ResultPresentation.Score("特洛伊 2004",english,now),"alias improves English-release match");
        Check(SearchQuality.WorkKeys(bridge).Intersect(SearchQuality.WorkKeys(english)).Any()&&!SearchQuality.WorkKeys(bridge).Intersect(SearchQuality.WorkKeys(other)).Any(),"work identity separates years");
        Check(!SearchQuality.WorkKeys(bridge with {Title="Troy S01E01 2024"}).Intersect(SearchQuality.WorkKeys(bridge with {Title="Troy S01E02 2024"})).Any(),"work identity separates episodes");
        Check(SearchQuality.WorkKeys(bridge with {Title="Troy 1080p"}).Count==0,"missing year is not guessed into a work group");
        Check(!new ResultFilter(MinBytes:1).Matches(bridge)&&!new ResultFilter(SeededOnly:true).Matches(bridge),"filters exclude unknown size/seed values when constrained");
        var folder=Path.Combine(Path.GetTempPath(),"afmc-quality-"+Guid.NewGuid().ToString("N"));var library=new PersonalLibrary(folder);
        for(var i=0;i<55;i++)library.Remember("query "+i);library.Remember("query 54");
        Check(library.History.Count==50&&library.History[0]=="query 54","history bounded and newest-first without duplicates");
        library.Toggle(bridge);var reloaded=new PersonalLibrary(folder);
        Check(reloaded.IsFavorite(bridge)&&reloaded.History.Count==50,"history and favorites survive reload");
        Check(!File.ReadAllText(Path.Combine(folder,"history.dat")).Contains("query")&&!File.ReadAllText(Path.Combine(folder,"favorites.dat")).Contains("Troy"),"personal records encrypted for Windows user");
        reloaded.ClearHistory();Check(new PersonalLibrary(folder).History.Count==0&&new PersonalLibrary(folder).Favorites.Count==1,"clearing history preserves favorites");
        new Preferences{RememberHistory=false}.Save(folder);Check(!Preferences.Load(folder).RememberHistory,"history-off preference persists");
        var secret="private-query-and-api-key";var diag=DiagnosticReport.Build([new(secret,[bridge],secret){SourceKind=secret,FailureCode=secret}]);
        Check(!diag.Contains(secret)&&!diag.Contains("Troy")&&!diag.Contains(ProductInfo.FeedbackEmail),"diagnostics whitelist excludes query/title/email/key/error text");
        var config=new SourceConfig{Id="health",Name="health",Endpoint="https://fixture.invalid/api"};var calls=0;bool healthy=false;
        using(var service=new SearchService(new Handler(_=>{calls++;return healthy?new(HttpStatusCode.OK){Content=new StringContent("<rss><channel/></rss>")}:new(HttpStatusCode.BadGateway);}),()=>now))
        {
            var reports=new List<SourceReport>();
            await service.SearchAsync("x",[config],reports.Add,CancellationToken.None);await service.SearchAsync("x",[config],reports.Add,CancellationToken.None);await service.SearchAsync("x",[config],reports.Add,CancellationToken.None);
            Check(calls==2&&reports[^1].CooldownSkipped&&reports[^1].RetryAfter>now,"repeated failures enter cooldown without another HTTP request");
            now=now.AddMinutes(2);healthy=true;await service.SearchAsync("x",[config],reports.Add,CancellationToken.None);
            Check(calls==3&&reports[^1].Error is null&&reports[^1].StatusText=="无匹配结果","cooldown expires and genuine empty response is distinguished");
            Check(reports.All(r=>r.SourceId==config.Id&&r.ElapsedMilliseconds>=0),"source reports carry stable identity and elapsed time");
        }
        calls=0;using(var service=new SearchService(new Handler(_=>{calls++;return new(HttpStatusCode.TooManyRequests);}),()=>now))
        {
            var reports=new List<SourceReport>();await service.SearchAsync("x",[config],reports.Add,CancellationToken.None);await service.SearchAsync("x",[config],reports.Add,CancellationToken.None);
            Check(calls==1&&reports[^1].CooldownSkipped&&reports[^1].FailureCode=="RATE_LIMIT","rate-limit response triggers immediate cooldown");
        }
        Console.WriteLine($"QUALITY PASSED {passed}; benchmark top1 {oldHits}->{newHits}/{cases.RootElement.GetArrayLength()}");
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(send(request));}
}

