using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Afmc.Find.Core;

internal static class LiveBaseline
{
    public static readonly string[] Queries = [
        "特洛伊", "特洛伊 2004", "Troy 2004 1080p", "Troy 2004 2160p", "流浪地球", "流浪地球 2019", "The Wandering Earth 2019", "Interstellar 2014", "Dune 2021", "Dune 2024",
        "Breaking Bad S01E01", "Breaking Bad S02", "权力的游戏 第一季", "Game of Thrones S01", "The Last of Us S01E01", "Sherlock S01", "三体", "Three Body 2023",
        "Naruto", "Naruto 1080p", "One Piece", "One Piece 1080p", "葬送的芙莉莲", "Frieren 1080p", "Spirited Away 2001", "千与千寻 2001",
        "Ubuntu", "Ubuntu 24.04", "Ubuntu 22.04", "Debian 12", "Linux Mint", "Blender 4.2", "LibreOffice", "GIMP",
        "Beethoven FLAC", "Mozart FLAC", "Bach", "周杰伦", "Taylor Swift FLAC", "Daft Punk FLAC",
        "Pride and Prejudice EPUB", "Alice in Wonderland EPUB", "Python EPUB", "机器学习", "三国演义", "Sherlock Holmes EPUB",
        "MNIST", "CIFAR", "ImageNet", "Wikipedia"
    ];
    public sealed record Snapshot(string Query, DateTimeOffset Captured, long ElapsedMs, long? FirstResultMs, SourceReport[] Sources);
    public static async Task Run()
    {
        var folder="docs/evidence/v071/baseline";Directory.CreateDirectory(folder);
        using var service=new SearchService();
        var sources=SiteSources.Catalog.Where(s=>s.Enabled&&s.Kind!="Website").Prepend(SourceConfig.Archive()).ToArray();
        for(var i=0;i<Queries.Length;i++)
        {
            var path=Path.Combine(folder,$"{i+1:00}.json");if(File.Exists(path)){Console.WriteLine($"BASELINE reuse {i+1:00}");continue;}
            var reports=new ConcurrentBag<SourceReport>();long first=0;var clock=Stopwatch.StartNew();
            using var cancel=new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await service.SearchAsync(Queries[i],sources,r=>{reports.Add(r);if(r.Results.Count>0)Interlocked.CompareExchange(ref first,clock.ElapsedMilliseconds,0);},cancel.Token,maxPages:1);
            var snapshot=new Snapshot(Queries[i],DateTimeOffset.UtcNow,clock.ElapsedMilliseconds,first==0?null:first,reports.OrderBy(r=>r.SourceId).ToArray());
            File.WriteAllText(path,JsonSerializer.Serialize(snapshot,new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"BASELINE {i+1:00}/50 {Queries[i]}: {reports.Sum(r=>r.Results.Count)} raw, {reports.Count(r=>r.Error!=null)} failed, {clock.ElapsedMilliseconds} ms");
            await Task.Delay(700);
        }
        Console.WriteLine("BASELINE captured 50 real queries; bounded to one page per source; relevance requires separate review.");
    }
    private static List<SearchResult> OldMerge(IEnumerable<SearchResult> rows)
    {
        var output=new List<SearchResult>();var hashes=new Dictionary<string,int>();
        foreach(var row in rows)
        {
            var key=Links.NormalizeHash(row.Hash)??Links.MagnetHash(row.Magnet);
            if(key is null||!hashes.TryGetValue(key,out var index)){if(key is not null)hashes[key]=output.Count;output.Add(row);continue;}
            var old=output[index];output[index]=old with{Source=string.Join(" · ",old.Source.Split(" · ").Append(row.Source).Distinct()),
                Magnet=old.Magnet??row.Magnet,TorrentUrl=old.TorrentUrl??row.TorrentUrl,CoverUrl=old.CoverUrl??row.CoverUrl,
                CoverSource=old.CoverUrl is not null?old.CoverSource:row.CoverSource,Published=old.Published??row.Published,
                ArchiveId=old.ArchiveId??row.ArchiveId,Seeders=old.Seeders is null?row.Seeders:row.Seeders is null?old.Seeders:Math.Max(old.Seeders.Value,row.Seeders.Value)};
        }
        return output;
    }
    public static void Review()
    {
        var entries=new List<object>();
        for(var i=0;i<Queries.Length;i++)
        {
            var snapshot=JsonSerializer.Deserialize<Snapshot>(File.ReadAllText($"docs/evidence/v071/baseline/{i+1:00}.json"))!;
            var raw=snapshot.Sources.SelectMany(s=>s.Results).ToArray();
            object[] Rank(List<SearchResult> rows,bool old=false)
            {
                var aliases=SearchQuality.Aliases(snapshot.Query,rows);
                return ResultPresentation.WithCovers(rows).OrderByDescending(r=>aliases.Select(q=>old?OldScore(q,r,snapshot.Captured):SearchQuality.Score(q,r,snapshot.Captured)).Max()).ThenBy(r=>r.Title,StringComparer.OrdinalIgnoreCase).Take(5)
                    .Select(r=>(object)new{r.Title,r.Hash,r.Source,r.Size,r.Category,r.MetadataConflict,r.ThumbnailUrl,r.CoverInferred,
                        ConstraintsPresent=SearchQuality.MatchesConstraints(snapshot.Query,r.Title)}).ToArray();
            }
            var merged=ResultMerger.Merge(raw);
            entries.Add(new{Id=i+1,snapshot.Query,snapshot.Captured,snapshot.ElapsedMs,snapshot.FirstResultMs,
                Success=snapshot.Sources.Count(s=>s.Error is null),Failed=snapshot.Sources.Count(s=>s.Error is not null),Total=merged.Count,
                Before=Rank(OldMerge(raw),true),After=Rank(merged)});
        }
        File.WriteAllText("docs/evidence/v071/baseline-review.json",JsonSerializer.Serialize(new{Method="Same captured source records in fixed source order. Before reconstructs 0.7.0 merge; not a second live run. Relevance requires explicit reviewer labels.",Queries=entries},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("BASELINE replayed 50 snapshots; review file ready.");
    }
    private static double OldScore(string query,SearchResult row,DateTimeOffset now)
    {
        var q=SearchQuality.Canonical(query);var title=SearchQuality.Canonical(row.Title);var tokens=q.Split(' ',StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
        bool Contains(string token)=>System.Text.RegularExpressions.Regex.IsMatch(token,@"[\u3400-\u9fff]")?title.Contains(token,StringComparison.Ordinal):(" "+title+" ").Contains(" "+token+" ",StringComparison.Ordinal);
        if(tokens.Length==0)return 0;
        var coverage=tokens.Count(Contains)/(double)tokens.Length;
        var score=70*coverage*coverage+(q==title?5:0)+Math.Min(20,Math.Log10(1d+Math.Max(0,row.Seeders??0))*5);
        if(row.Published is {} date&&date<=now)score+=5/(1+(now-date).TotalDays/365);
        var constraints=System.Text.RegularExpressions.Regex.Matches(q,@"\b(?:(?:19|20)\d{2}|\d{3,4}p|s\d{2,3}|e\d{2,3})\b").Select(m=>m.Value)
            .Concat(System.Text.RegularExpressions.Regex.Matches(query,@"(?<!\d)\d{1,4}(?:\.\d{1,4})+(?!\d)").Select(m=>SearchQuality.Canonical(m.Value)))
            .Concat(new[]{"国语","粤语","中字","双语"}.Where(q.Contains));
        return score-constraints.Count(t=>!Contains(t))*100;
    }
}
