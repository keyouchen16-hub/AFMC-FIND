using System.Globalization;

namespace Afmc.Find.Core;

public sealed record SourceConfig
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "新搜索源";
    public string Kind { get; init; } = "Torznab";
    public string Endpoint { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public string Description { get; init; } = "";
    public string EngineIndexerId { get; init; } = "";
    public string DiscoveryTemplate { get; init; } = "";
    public string DiscoveryFormat { get; init; } = "";
    public string DiscoveryQuery { get; init; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string StatusLabel { get; init; } = "";
    public override string ToString() => $"{(Kind == "Website" ? "[仅网页入口]" : Enabled ? "[参与搜索]" : "[已停用]")} {Name}  {Description}";
    public static SourceConfig Archive() => new() { Id = "internet-archive", Name = "Internet Archive", Kind = "Archive", Endpoint = "https://archive.org" };
}

public sealed record SearchResult
{
    public required string Title { get; init; }
    public required string Source { get; init; }
    public string? Magnet { get; init; }
    public bool PublicDiscoveryAllowed { get; init; }
    public string? TorrentUrl { get; init; }
    public string? ShareUrl { get; init; }
    public string ResourceType => CloudShares.Provider(ShareUrl) is {} provider ? provider + " · 网盘" : "BT 资源";
    public string? DetailsUrl { get; init; }
    public string? ArchiveId { get; init; }
    public string? CoverUrl { get; init; }
    public string? CoverSource { get; init; }
    public bool CoverInferred { get; init; }
    public string? ThumbnailUrl => CoverUrl ?? (ArchiveId is null ? null : "https://archive.org/services/img/"+Uri.EscapeDataString(ArchiveId));
    public string ThumbnailLabel => ThumbnailUrl is null ? "暂无封面" : CoverInferred ? "同片名 / 年份匹配海报 · "+CoverSource : "来源条目图片";
    public string MatchReason { get; init; } = "";
    // Transient presentation only: never persist a probe status or a collapsed group as resource data.
    [System.Text.Json.Serialization.JsonIgnore] public string PlaybackLabel { get; init; } = "未验证";
    [System.Text.Json.Serialization.JsonIgnore] public string VersionLabel { get; init; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string ResultName => ResourceDisplay.Name(this);
    public string SourceAndSpecs => Source+(SearchQuality.TechnicalSummary(this) is {Length:>0} spec?" · "+spec:"");
    public string? Hash { get; init; }
    public long? Size { get; init; }
    public long? Seeders { get; init; }
    public string Category { get; init; } = "未知";
    public DateTimeOffset? Published { get; init; }
    public IReadOnlyList<ResourceObservation> Observations { get; init; } = [];
    public string MetadataSource { get; init; } = "";
    public bool MetadataConflict { get; init; }
    public string MetadataNote { get; init; } = "";
    public string SizeText => Size is not { } n ? "未知" : n >= 1073741824 ? $"{n / 1073741824d:0.##} GB" : n >= 1048576 ? $"{n / 1048576d:0.##} MB" : $"{n:N0} B";
    public string SeedsText => Seeders?.ToString("N0", CultureInfo.CurrentCulture) ?? "未知";
    public string PublishedText => Published?.ToLocalTime().ToString("yyyy-MM-dd") ?? "未知";
}

public sealed record SourceReport(string Name, IReadOnlyList<SearchResult> Results, string? Error = null)
{
    public bool IsComplete {get;init;}=true;
    public string SourceId {get;init;}="";
    public string SourceKind {get;init;}="";
    public long ElapsedMilliseconds {get;init;}
    public string FailureCode {get;init;}="";
    public DateTimeOffset? RetryAfter {get;init;}
    public bool CooldownSkipped {get;init;}
    public string StatusText=>!IsComplete?"已返回部分结果 · 继续检索":Error is null?(Results.Count==0?"无匹配结果":"成功"):(CooldownSkipped?"暂缓请求":Results.Count>0?"部分成功":"失败");
    public string Summary=>$"{Name} · {StatusText} · {Results.Count} 条 · {ElapsedMilliseconds/1000d:0.0}s"+(Error is null?"":" · "+Error);
}
public sealed record SearchSummary(int Succeeded, int Failed);
public sealed class SourceException(string message) : Exception(message);

public static class ResultMerger
{
    // Select one complete source record deterministically. Keep its title, size,
    // detail page and artwork together rather than mixing unrelated field owners.
    private static int Completeness(SearchResult row) =>
        (row.Size is > 0 ? 4 : 0) + (!string.IsNullOrWhiteSpace(row.Category) && row.Category != "未知" ? 1 : 0) +
        SearchQuality.TechnicalSummary(row).Split(" · ", StringSplitOptions.RemoveEmptyEntries).Length * 3 +
        (System.Text.RegularExpressions.Regex.IsMatch(row.Title, @"(?<!\d)(?:19|20)\d{2}(?!\d)") ? 3 : 0);

    public static List<SearchResult> Merge(IEnumerable<SearchResult> input)
    {
        var output = new List<SearchResult>();
        var groups = new Dictionary<string,List<SearchResult>>(StringComparer.OrdinalIgnoreCase);
        var positions = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        var raw=input.SelectMany(r=>r.Observations.Count>0?r.Observations.Select(o=>o.ToResult()):[r]);
        foreach (var r in raw.OrderByDescending(Completeness)
            .ThenByDescending(r => Math.Min(r.Title.Length, 240))
            .ThenBy(r => r.Source, StringComparer.Ordinal)
            .ThenBy(r => r.Title, StringComparer.Ordinal)
            .ThenBy(r => r.DetailsUrl, StringComparer.Ordinal)
            .ThenBy(r=>r.Size).ThenBy(r=>r.Published).ThenByDescending(r=>r.Seeders)
            .ThenBy(r=>r.CoverUrl,StringComparer.Ordinal).ThenBy(r=>r.ArchiveId,StringComparer.Ordinal)
            .ThenBy(r=>r.Magnet,StringComparer.Ordinal).ThenBy(r=>r.TorrentUrl,StringComparer.Ordinal))
        {
            var key = Links.NormalizeHash(r.Hash) ?? Links.MagnetHash(r.Magnet);
            if (key is null)
            {
                output.Add(r); continue;
            }
            if(!groups.TryGetValue(key,out var group)){groups[key]=group=[];positions[key]=output.Count;output.Add(r);}
            group.Add(r);
        }
        foreach(var (hash,group) in groups)
        {
            var observations=group.Select(ResourceObservation.From).Distinct().ToArray();
            var primary=group[0];var size=group.FirstOrDefault(r=>r.Size is not null);
            var date=group.FirstOrDefault(r=>r.Published is not null);
            var seeds=group.Where(r=>r.Seeders is not null).OrderByDescending(r=>r.Seeders).FirstOrDefault();
            var sizes=group.Where(r=>r.Size is not null).Select(r=>r.Size!.Value).Distinct().ToArray();
            var sizeConflict=sizes.Length>1 && sizes.Max()-sizes.Min()>Math.Max(1048576d,sizes.Max()*0.01);
            var years=group.Select(r=>System.Text.RegularExpressions.Regex.Match(r.Title,@"(?<!\d)(?:19|20)\d{2}(?!\d)").Value).Where(s=>s.Length>0).Distinct().ToArray();
            var conflict=sizeConflict||years.Length>1;
            var note=new List<string> {$"标题 / 页面依据：{primary.Source}"};
            if(size is not null)note.Add($"大小依据：{size.Source}");
            if(date is not null)note.Add($"发布时间依据：{date.Source}（来源发布记录，非作品上映日期）");
            if(seeds is not null)note.Add($"做种依据：{seeds.Source}（各来源报告的最大值，未实时验证）");
            if(sizeConflict)note.Add("大小存在来源差异，请核对下载软件中的文件列表。");
            if(years.Length>1)note.Add("标题年份存在来源差异，暂不推断海报或作品分组。");
            if(group.Where(r=>r.Published is not null).Select(r=>r.Published!.Value.UtcDateTime.Date).Distinct().Count()>1)note.Add("各来源发布时间不同；详细记录列于下方。");
            output[positions[hash]]=primary with {Hash=hash,Source=string.Join(" · ",group.SelectMany(r=>r.Source.Split(" · ")).Distinct().Order(StringComparer.Ordinal)),
                Magnet=primary.Magnet??group.FirstOrDefault(r=>r.Magnet is not null)?.Magnet,
                TorrentUrl=primary.TorrentUrl??group.FirstOrDefault(r=>r.TorrentUrl is not null)?.TorrentUrl,
                Size=size?.Size,Published=date?.Published,Seeders=seeds?.Seeders,
                Observations=observations,MetadataSource=primary.Source,MetadataConflict=conflict,MetadataNote=string.Join("\n",note)};
        }
        return output;
    }
}
