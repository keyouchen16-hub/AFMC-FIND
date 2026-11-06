using Afmc.Find.Core;

namespace Afmc.Find.App;

// A single short-lived, already decoded session. Ownership is transferred once
// to the player, or released on expiry, a new search, replacement, or app exit.
public sealed class PreparedPlaybackCache
{
    private readonly object gate = new();
    private (SearchResult Row, PlaybackWinner Winner, DateTimeOffset Expires)? entry;
    private Task cleaning = Task.CompletedTask;
    public bool HasPending { get { lock(gate) return entry is not null || !cleaning.IsCompleted; } }
    public static string? Key(SearchResult row) => Links.NormalizeHash(row.Hash) ?? Links.MagnetHash(row.Magnet) ?? row.TorrentUrl ?? (row.ArchiveId is {} id ? "archive:"+id : null);

    public Task StoreAsync(SearchResult row, PlaybackWinner winner, DateTimeOffset now)
    {
        lock(gate) {
            if(Key(row) is not {} key || key != winner.ResourceKey) throw new ArgumentException("Prepared resource identity mismatch");
            if(entry is {} old) cleaning = CleanAfterAsync(cleaning,old.Winner.Session);
            entry = (row,winner,now.AddMinutes(2));
            return cleaning;
        }
    }
    public PlaybackWinner? Take(SearchResult row, DateTimeOffset now)
    {
        lock(gate) {
            if(entry is not {} value || now >= value.Expires || Key(row) != value.Winner.ResourceKey) return null;
            entry = null;
            return value.Winner;
        }
    }
    public async Task<SearchResult?> ClearAsync(DateTimeOffset? expiredAt = null)
    {
        SearchResult? row = null; Task task;
        lock(gate) {
            if(entry is {} value && (expiredAt is null || expiredAt >= value.Expires)) {
                entry = null; row = value.Row;
                cleaning = CleanAfterAsync(cleaning,value.Winner.Session);
            }
            task = cleaning;
        }
        await task;
        return row;
    }
    private static async Task CleanAfterAsync(Task previous, TorrentPlayback session)
    {
        await previous;
        try { await session.DisposeAsync(); }
        catch(Exception e) when(e is not OutOfMemoryException) {
            System.Diagnostics.Trace.TraceWarning("Prepared playback cleanup will be retried by cache recovery.");
        }
    }
}
