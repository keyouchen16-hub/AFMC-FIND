using System.Text.RegularExpressions;

namespace Afmc.Find.Core;

public sealed record ResourceGroup(SearchResult Representative, IReadOnlyList<SearchResult> Versions);

/// <summary>
/// Conservative presentation groups, not deduplication or permission to substitute
/// playback targets. Original records and their rank within each group are retained.
/// </summary>
public static class ResourceGroups
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    private sealed class Builder(SearchResult first, HashSet<string> commonKeys)
    {
        public readonly List<SearchResult> Rows = [first];
        public readonly HashSet<string> CommonKeys = commonKeys;
    }

    public static IReadOnlyList<ResourceGroup> Build(IEnumerable<SearchResult> ranked)
    {
        ArgumentNullException.ThrowIfNull(ranked);
        var groups = new List<Builder>();
        var partitions = new Dictionary<string, List<Builder>>(StringComparer.Ordinal);
        foreach (var row in ranked)
        {
            var identity = Identity(row);
            if (identity is null)
            {
                groups.Add(new Builder(row, []));
                continue;
            }

            var (partition, keys) = identity.Value;
            if (!partitions.TryGetValue(partition, out var candidates))
                partitions.Add(partition, candidates = []);

            // A bilingual row must not glue two established, differently named
            // groups together. Preserve it as a separate row when ambiguous.
            var matches = candidates.Where(g => g.CommonKeys.Overlaps(keys)).Take(2).ToArray();
            if (matches.Length == 1)
            {
                matches[0].Rows.Add(row);
                // Keep only identity shared by EVERY member. Union would allow
                // A -> (A/B) -> B -> (B/C) to merge unrelated works transitively.
                matches[0].CommonKeys.IntersectWith(keys);
            }
            else
            {
                var group = new Builder(row, keys);
                groups.Add(group);
                // An ambiguous bridge remains a singleton and is not a new
                // identity anchor for subsequently received rows.
                if (matches.Length == 0) candidates.Add(group);
            }
        }

        return groups.Select(g => new ResourceGroup(g.Rows[0], g.Rows.AsReadOnly())).ToArray();
    }

    private static (string Partition, HashSet<string> Keys)? Identity(SearchResult row)
    {
        if (row.MetadataConflict || row.ShareUrl is not null || row.Title.Length is 0 or > 1024)
            return null;

        var category = SearchQuality.Category(row);
        if (category is not ("影视" or "动漫")) return null;
        if (Has(row.Category, @"\btv\b|series|电视剧|電視劇|剧集|劇集")) return null;

        var title = SearchQuality.Canonical(row.Title);
        if (Has(title, @"\bs\d{1,3}\b|\be\d{1,3}\b|\d{1,2}x\d{1,3}\b|\b(?:part|pt|vol|volume|disc|disk|cd)\s*\d+\b|\b(?:seasons?|episodes?|series|collections?|complete|boxsets?|box sets?|trilog[yi]|antholog[yi])\b|合集|全集|全季|打包|全套|完结|完結|第[零一二三四五六七八九十百\d]+[季集话話部]|全[零一二三四五六七八九十百\d]+[集话話]|剧场版合集"))
            return null;
        if (category == "动漫")
        {
            if (Has(row.Title, @"[\[【]\d{1,3}(?:\s*-\s*\d{1,3})?[\]】]")) return null;
            // Anime releases also use "Title (2025) - 01" without an episode
            // keyword. WorkKeys stops at the year, so these must stay separate.
            // The final boundary prevents resolutions like "- 720p" matching.
            if (Has(row.Title, @"(?<!\d)(?:19|20)\d{2}(?!\d).*?[-–—]\s*\d{1,3}(?:v\d+)?(?![\p{L}\p{N}])")) return null;
        }

        var keys = SearchQuality.WorkKeys(row).ToHashSet(StringComparer.Ordinal);
        if (keys.Count == 0 || keys.Count > 2) return null;
        if (keys.Count == 2)
        {
            // The existing key parser also accepts '/', often used for packs.
            // Only an explicit Chinese/Latin alias pair is accepted as a bridge.
            var names = keys.Select(k => k[(k.LastIndexOf(':') + 1)..]).ToArray();
            if (names.Count(n => Has(n, @"[\u3400-\u9fff]")) != 1 ||
                names.Count(n => !Has(n, @"[\u3400-\u9fff]") && Has(n, @"[a-z]")) != 1)
                return null;
        }

        var edition = new List<string>();
        foreach (var (name, pattern) in new (string, string)[]
        {
            ("director", @"\bdirector(?: s|s)? cut\b|导演剪辑|導演剪輯"),
            ("extended", @"\bextended\b|加长版|加長版|加长剪辑|加長剪輯"),
            ("unrated", @"\bunrated\b|未分级|未分級"),
            ("uncut", @"\buncut\b|未删减|未刪減"),
            ("remastered", @"\bremastered\b|修复版|修復版|重制版|重製版"),
            ("theatrical", @"\btheatrical\b|院线版|院線版|剧场版|劇場版"),
            ("final", @"\bfinal cut\b|最终剪辑|最終剪輯"),
            ("special", @"\bspecial edition\b|特别版|特別版")
        })
            if (Has(title, pattern)) edition.Add(name);

        return (category + "|" + string.Join(',', edition), keys);
    }

    private static bool Has(string text, string pattern) =>
        Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
}
