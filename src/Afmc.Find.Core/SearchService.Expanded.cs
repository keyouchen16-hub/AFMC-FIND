namespace Afmc.Find.Core;

public sealed partial class SearchService
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string,(DateTimeOffset Time,ExpandedSources.CsvPage Page)> csvCache=new();
    private readonly SemaphoreSlim academicGate=new(1);
    private IReadOnlyList<ExpandedSources.AcademicEntry>? academicDatabase;
    private DateTimeOffset academicFetched;
    private async Task<IReadOnlyList<SearchResult>> CsvAsync(string query,CancellationToken ct,int maxPages)
    {
        long? cursor=null;var visited=new HashSet<long>();var collected=new List<SearchResult>();
        try
        {
            for(var page=0;page<maxPages;page++)
            {
                var uri=ExpandedSources.CsvUri(query,cursor);ExpandedSources.CsvPage parsed;
                if(csvCache.TryGetValue(uri.AbsoluteUri,out var cached)&&DateTimeOffset.UtcNow-cached.Time<TimeSpan.FromMinutes(5))parsed=cached.Page;
                else
                {
                    parsed=ExpandedSources.ParseCsv(await GetBytesAsync(uri,4*1024*1024,ct).ConfigureAwait(false));
                    if(csvCache.Count>=96)foreach(var old in csvCache.OrderBy(x=>x.Value.Time).Take(24))csvCache.TryRemove(old.Key,out _);
                    csvCache[uri.AbsoluteUri]=(DateTimeOffset.UtcNow,parsed);
                }
                var before=collected.Count;collected=ResultMerger.Merge(collected.Concat(parsed.Results));
                if(parsed.Results.Count==0||before==collected.Count||parsed.Next is not {} next||!visited.Add(next))break;
                cursor=next;
            }
            return collected;
        }
        catch(Exception e) when(e is not OutOfMemoryException && collected.Count>0)
        {throw new PartialSourceException(collected,"Torrents.csv 后续页请求失败："+Links.Error(e));}
    }
    private async Task<IReadOnlyList<SearchResult>> ExpandedAsync(string kind,string query,CancellationToken ct,int page=1)
    {
        if(kind=="Academic")
        {
            await academicGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if(academicDatabase is null||DateTimeOffset.UtcNow-academicFetched>=TimeSpan.FromHours(24))
                {
                    var data=await GetBytesAsync(ExpandedSources.QueryUri(kind,query),8*1024*1024,ct).ConfigureAwait(false);
                    academicDatabase=ExpandedSources.ParseAcademic(data);academicFetched=DateTimeOffset.UtcNow;
                }
                return ExpandedSources.SearchAcademic(academicDatabase,query);
            }
            finally{academicGate.Release();}
        }
        var uri=ExpandedSources.QueryUri(kind,query,page);
        if(publicCache.TryGetValue(uri.AbsoluteUri,out var cached)&&DateTimeOffset.UtcNow-cached.Time<TimeSpan.FromMinutes(5))return cached.Results;
        var bytes=await GetBytesAsync(uri,4*1024*1024,ct).ConfigureAwait(false);
        var results=kind=="LimeTorrents"?ExpandedSources.ParseLime(bytes):ExpandedSources.ParseTokyo(bytes);
        if(publicCache.Count>=96)foreach(var old in publicCache.OrderBy(x=>x.Value.Time).Take(24))publicCache.TryRemove(old.Key,out _);
        publicCache[uri.AbsoluteUri]=(DateTimeOffset.UtcNow,results);
        return results;
    }
}
