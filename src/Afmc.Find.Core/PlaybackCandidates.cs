namespace Afmc.Find.Core;

public static class PlaybackCandidates
{
    public static IReadOnlyList<SearchResult> Select(SearchResult selected, IEnumerable<SearchResult> rows, string query)
    {
        var keys = SearchQuality.WorkKeys(selected);
        // Only movies with an explicit year: series/packs and ambiguous identities need manual selection.
        bool Movie(SearchResult r) => SearchQuality.WorkKeys(r).Count > 0 &&
            !System.Text.RegularExpressions.Regex.IsMatch(SearchQuality.Canonical(r.Title),
                @"\bs\d{2,3}\b|\be\d{2,3}\b|collection|complete|合集|全集|全季|打包");
        bool Playable(SearchResult r) => r.ShareUrl is null && (r.Magnet is not null || r.TorrentUrl is not null || r.ArchiveId is not null);
        if (!Movie(selected)) return [selected];
        var alternatives = rows.Where(r => Playable(r) && Movie(r) &&
            SearchQuality.WorkKeys(r).Intersect(keys, StringComparer.Ordinal).Any() &&
            SearchQuality.MatchesConstraints(query, r.Title) &&
            new[] { "国语", "粤语", "中字", "双语" }.All(tag => !selected.Title.Contains(tag) || r.Title.Contains(tag)))
            .Where(r => new[] { "director s cut", "extended", "unrated", "导演剪辑", "加长版" }
                .All(tag => SearchQuality.Canonical(selected.Title).Contains(tag) == SearchQuality.Canonical(r.Title).Contains(tag)))
            .OrderByDescending(r => r.Seeders ?? -1).ThenBy(r => r.Size ?? long.MaxValue);
        return new[] { selected }.Concat(alternatives)
            .DistinctBy(r => Links.NormalizeHash(r.Hash) ?? Links.MagnetHash(r.Magnet) ?? r.TorrentUrl ?? r.ArchiveId ?? r.Title)
            .Take(3).ToArray();
    }
}
