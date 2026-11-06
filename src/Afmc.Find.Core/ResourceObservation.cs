namespace Afmc.Find.Core;

// Flat records retain field ownership across incremental merges and encrypted favorites.
public sealed record ResourceObservation(string Source, string Title, string? Hash, string? Magnet,
    string? TorrentUrl, string? DetailsUrl, long? Size, long? Seeders, DateTimeOffset? Published,
    string Category, string? CoverUrl, string? CoverSource, bool CoverInferred, string? ArchiveId,string? ShareUrl=null,bool PublicDiscoveryAllowed=false)
{
    public static ResourceObservation From(SearchResult r) => new(r.Source,r.Title,r.Hash,r.Magnet,r.TorrentUrl,
        r.DetailsUrl,r.Size,r.Seeders,r.Published,r.Category,r.CoverUrl,r.CoverSource,r.CoverInferred,r.ArchiveId,r.ShareUrl,r.PublicDiscoveryAllowed);
    public SearchResult ToResult() => new() {Source=Source,Title=Title,Hash=Hash,Magnet=Magnet,TorrentUrl=TorrentUrl,
        DetailsUrl=DetailsUrl,Size=Size,Seeders=Seeders,Published=Published,Category=Category,CoverUrl=CoverUrl,
        CoverSource=CoverSource,CoverInferred=CoverInferred,ArchiveId=ArchiveId,ShareUrl=ShareUrl,PublicDiscoveryAllowed=PublicDiscoveryAllowed};
}
