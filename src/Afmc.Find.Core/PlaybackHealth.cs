namespace Afmc.Find.Core;

public enum PlaybackEvidence { Playable, DataVerified, TimedOut, PlaybackFailed, DataReceived }
public sealed class PlaybackHealth
{
    public static SearchResult[] Candidates(IEnumerable<SearchResult> ranked, int limit = 12)
    {
        var groups = new List<(HashSet<string> Keys, List<SearchResult> Rows)>();
        foreach(var row in ranked.Take(120)) {
            // Scheduling diversity only, never permission to substitute one movie for another.
            var title = System.Text.RegularExpressions.Regex.Replace(row.Title,@"\[[^\]]*\]|【[^】]*】"," ").Trim();
            var keys = SearchQuality.WorkKeys(row with {Title=title, MetadataConflict=false});
            var index = groups.FindIndex(g=>keys.Any(g.Keys.Contains));
            if(index<0) groups.Add((keys.ToHashSet(), [row]));
            else { groups[index].Keys.UnionWith(keys); groups[index].Rows.Add(row); }
        }
        foreach(var group in groups) group.Rows.Sort((a,b)=> {
            var seeds=(b.Seeders??-1).CompareTo(a.Seeders??-1);
            return seeds!=0?seeds:(a.Size??long.MaxValue).CompareTo(b.Size??long.MaxValue);
        });
        return Enumerable.Range(0,limit).SelectMany(i=>groups.Where(g=>g.Rows.Count>i).Select(g=>g.Rows[i])).Take(limit).ToArray();
    }
    private readonly Dictionary<string, (PlaybackEvidence Evidence, DateTimeOffset Time)> entries = [];
    private static string? Key(SearchResult row) => Links.NormalizeHash(row.Hash) ?? Links.MagnetHash(row.Magnet) ?? row.TorrentUrl ?? (row.ArchiveId is {} id ? "archive:"+id : null);
    public int Tier(SearchResult row, DateTimeOffset now)
    {
        if (Key(row) is not {} key || !entries.TryGetValue(key, out var entry) || now-entry.Time > TimeSpan.FromMinutes(30)) return 2;
        return entry.Evidence switch { PlaybackEvidence.Playable => 0, PlaybackEvidence.DataVerified => 1, _ => 3 };
    }
    public IOrderedEnumerable<SearchResult> Order(IEnumerable<SearchResult> rows, Func<SearchResult,double> relevance, DateTimeOffset now) =>
        rows.OrderBy(r => Tier(r,now)).ThenByDescending(relevance).ThenBy(r => r.Title,StringComparer.OrdinalIgnoreCase);
    public void Record(SearchResult row, PlaybackEvidence evidence, DateTimeOffset now)
    {
        if (Key(row) is {} key) entries[key] = (evidence, now);
    }
    public void Forget(SearchResult row) { if(Key(row) is {} key) entries.Remove(key); }
    public (string Label, double Bonus) Get(SearchResult row, DateTimeOffset now)
    {
        if (Key(row) is not {} key || !entries.TryGetValue(key, out var entry) || now-entry.Time > TimeSpan.FromMinutes(30))
            return ("未验证播放", 0);
        var (label, bonus) = entry.Evidence switch {
            PlaybackEvidence.Playable => ("已验证可播", 15d),
            PlaybackEvidence.DataVerified => ("已验证有数据 · 播放未确认", 4d),
            PlaybackEvidence.DataReceived => ("收到数据 · 首片未完成校验，尚不能起播", -6d),
            PlaybackEvidence.TimedOut => ("本次连接超时 · 下载能力未知", -8d),
            _ => ("本次试播失败 · 下载能力未知", -5d)
        };
        return ($"{label}（{entry.Time.ToLocalTime():HH:mm}检测）", bonus);
    }
}
