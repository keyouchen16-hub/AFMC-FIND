namespace Afmc.Find.Core;

/// <summary>Query-scoped lexical ranking cache; playback evidence remains live and is never cached here.</summary>
public sealed class SearchRanking
{
    public sealed record Match(int Tier, double Score, double NameScore, string Query, string Explanation);
    private readonly Dictionary<(string Query,string Title,string Category,long? Seeds,DateTimeOffset? Published),Match> cache=[];
    private readonly DateTimeOffset now;
    public SearchRanking(DateTimeOffset now) => this.now=now;
    public Match Evaluate(IEnumerable<string> aliases, SearchResult row)
    {
        Match? best=null;
        foreach(var query in aliases)
        {
            var key=(query,row.Title,row.Category,row.Seeders,row.Published);
            if(!cache.TryGetValue(key,out var value))
            {
                value=new(SearchQuality.MatchTier(query,row.Title),SearchQuality.Score(query,row,now),
                    SearchQuality.Score(query,row with {Seeders=null,Published=null},now),query,SearchQuality.Explain(query,row with {MetadataConflict=false}));
                // Bounded to this search, with a defensive cap for very large custom feeds.
                if(cache.Count>=20000)cache.Clear();
                cache[key]=value;
            }
            if(best is null||value.Tier<best.Tier||value.Tier==best.Tier&&value.Score>best.Score)best=value;
        }
        return best??new(0,0,0,"","");
    }
    public static IOrderedEnumerable<SearchResult> Order(IEnumerable<SearchResult> rows,Func<SearchResult,Match> match,PlaybackHealth health,DateTimeOffset now) =>
        rows.OrderBy(r=>match(r).Tier).ThenBy(r=>health.Tier(r,now)).ThenByDescending(r=>match(r).Score).ThenBy(r=>r.Title,StringComparer.OrdinalIgnoreCase);
}
