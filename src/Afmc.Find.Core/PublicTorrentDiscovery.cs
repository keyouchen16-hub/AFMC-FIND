namespace Afmc.Find.Core;

// Fixed, bounded discovery hints for built-in public indexes only. No remote
// configuration fetch, and no changes to Torznab/private/manual magnet inputs.
public static class PublicTorrentDiscovery
{
    private static readonly string[] trackers = [
        "udp://open.stealth.si:80/announce", "udp://tracker.opentrackr.org:1337/announce",
        "udp://tracker.torrent.eu.org:451/announce", "udp://open.demonii.com:1337/announce",
        "udp://exodus.desync.com:6969/announce", "udp://open.tracker.cl:1337/announce"];

    public static SearchResult Mark(SearchResult row, string kind)
    {
        var allowed = kind is "PirateBay" or "Tpb" or "CiliXiong" or "Zhongzilou" or "BitSearch"
            or "TorrentsCsv" or "Nyaa" or "Dmhy" or "Eztv" or "TokyoTosho";
        return row with { PublicDiscoveryAllowed = allowed,
            Observations = row.Observations.Select(o=>o with {PublicDiscoveryAllowed=allowed}).ToArray() };
    }

    public static string Complete(string magnet, bool allowed)
    {
        if (!allowed || Links.MagnetHash(magnet) is null) return magnet;
        // Keep original discovery/authentication information byte-for-byte.
        if (magnet[8..].Split('&').Any(pair => {
            var parts=pair.Split('=',2);
            return parts.Length==2 && Uri.UnescapeDataString(parts[0]).Equals("tr",StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(Uri.UnescapeDataString(parts[1]));
        })) return magnet;
        var enriched=magnet+string.Concat(trackers.Select(t=>"&tr="+Uri.EscapeDataString(t)));
        return enriched.Length<=16384 ? enriched : magnet;
    }

    public static bool IsFallbackSet(IEnumerable<string> urls) => trackers.ToHashSet(StringComparer.Ordinal).SetEquals(urls);
}
