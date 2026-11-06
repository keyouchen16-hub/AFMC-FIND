using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Afmc.Find.Core;

public sealed partial class SearchService : IDisposable
{
    private readonly HttpClient http;
    private readonly Lazy<WebsiteDiscovery> websiteDiscovery=new(()=>new WebsiteDiscovery());
    public Func<SourceConfig, CancellationToken, Task<SourceConfig>>? SourceResolver { get; set; }
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string,(DateTimeOffset Time,IReadOnlyList<SearchResult> Results)> publicCache=new();
    private readonly SemaphoreSlim ciliSearchGate=new(1);
    private DateTimeOffset nextCiliSearch;
    private readonly Func<DateTimeOffset> now;
    private sealed record Health(int Failures,DateTimeOffset? RetryAfter,string Code);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string,Health> sourceHealth=new();
    public SearchService(HttpMessageHandler? handler = null,Func<DateTimeOffset>? clock=null)
    {
        now=clock??(()=>DateTimeOffset.UtcNow);
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(35) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; AFMC-FIND/0.4.0)");
    }
    public async Task<byte[]> GetBytesAsync(Uri uri, int maxBytes, CancellationToken ct, bool redirects = false)
    {
        for (var hop = 0; hop < 6; hop++)
        {
            Links.WebUri(uri.AbsoluteUri);
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                if (!redirects || response.Headers.Location is not { } location) throw new SourceException("服务发生重定向，请检查配置的 API 地址。");
                var next = new Uri(uri, location);
                if (next.Scheme != "https" && !next.IsLoopback) throw new SourceException("已拒绝不安全的下载重定向。");
                uri = next; continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > maxBytes) throw new SourceException("响应过大，已停止读取。");
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var output = new MemoryStream(); var buffer = new byte[16384];
            while (true)
            {
                var n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false); if (n == 0) break;
                if (output.Length + n > maxBytes) throw new SourceException("响应过大，已停止读取。");
                output.Write(buffer, 0, n);
            }
            return output.ToArray();
        }
        throw new SourceException("重定向次数过多。");
    }
    public async Task<SearchSummary> SearchAsync(string query, IEnumerable<SourceConfig> sources, Action<SourceReport> report, CancellationToken ct, int maxPages = 3, Action<SourceReport>? progress = null)
    {
        query = query.Trim();
        if(maxPages is < 1 or > 3) throw new SourceException("每个来源每次最多查询 3 页。");
        if (query.Length is < 1 or > 200 || query.Any(char.IsControl)) throw new SourceException("请输入 1–200 个字符的关键词。");
        using var gate = new SemaphoreSlim(6);
        using var browserGate = new SemaphoreSlim(1);
        var enabled=sources.Where(s=>s.Enabled&&s.Kind!="Website").ToArray();
        string Host(SourceConfig source)=>Uri.TryCreate(source.Endpoint,UriKind.Absolute,out var uri)?uri.Host:source.Kind;
        var hostGates=enabled.Select(Host).Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(h=>h,_=>new SemaphoreSlim(2),StringComparer.OrdinalIgnoreCase);
        int success = 0, failed = 0;
        var jobs = enabled.Select(async s =>
        {
            var lane=s.DiscoveryFormat=="BrowserSearch"?browserGate:gate;
            var hostGate=hostGates[Host(s)];
            await hostGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await lane.WaitAsync(ct).ConfigureAwait(false);
                try {
                var timer=System.Diagnostics.Stopwatch.StartNew();
                var healthKey=s.Id+"|"+s.Kind+"|"+s.Endpoint;
                void Publish(SourceReport value,string code="")
                {
                    Health? health=null;
                    if(value.Error is null)sourceHealth.TryRemove(healthKey,out _);
                    else health=sourceHealth.AddOrUpdate(healthKey,_=>NewHealth(1,code),(_,old)=>NewHealth(old.Failures+1,code));
                    report(value with {SourceId=s.Id,SourceKind=s.Kind,ElapsedMilliseconds=timer.ElapsedMilliseconds,FailureCode=code,RetryAfter=health?.RetryAfter});
                }
                if(sourceHealth.TryGetValue(healthKey,out var prior)&&prior.RetryAfter is {} until&&until>now())
                {
                    Interlocked.Increment(ref failed);
                    report(new(s.Name,[], $"来源冷却中，约 {Math.Ceiling((until-now()).TotalSeconds)} 秒后可重试") {SourceId=s.Id,SourceKind=s.Kind,FailureCode=prior.Code,RetryAfter=until,CooldownSkipped=true});
                    return;
                }
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(s.DiscoveryFormat=="BrowserSearch"?105:45));
                var collected = new List<SearchResult>();
                try
                {
                    if (s.EngineIndexerId.Length > 0)
                        s = SourceResolver is not null ? await SourceResolver(s, timeout.Token).ConfigureAwait(false) : throw new SourceException("内置搜索引擎尚未启动。");
                    for(var page=1;page<=maxPages;page++)
                    {
                        var results = s.Kind switch {
                            "AutoWeb" => await websiteDiscovery.Value.SearchAsync(s,query,timeout.Token).ConfigureAwait(false),
                            "Foss" => await FossAsync(query,timeout.Token).ConfigureAwait(false),
                            "PirateBay" => await PirateBayAsync(query,timeout.Token).ConfigureAwait(false),
                            "Dmhy" or "Eztv" => await MediaAsync(s.Kind,query,timeout.Token,maxPages).ConfigureAwait(false),
                            "LinuxTracker" or "Debian" => await ReliableAsync(s.Kind,query,timeout.Token,page).ConfigureAwait(false),
                            "Archive" => await ArchiveAsync(query, s, timeout.Token,page).ConfigureAwait(false),
                            "Zhongzilou" => await ZhongzilouAsync(query, timeout.Token,page).ConfigureAwait(false),
                            "Tpb" => await TpbAsync(query, timeout.Token,page).ConfigureAwait(false),
                            "CiliXiong" => await CiliXiongAsync(query,timeout.Token).ConfigureAwait(false),
                            "Knaben" or "BitSearch" or "Nyaa" => await GlobalAsync(s.Kind,query,timeout.Token,page).ConfigureAwait(false),
                            "TorrentsCsv" => await CsvAsync(query,timeout.Token,maxPages).ConfigureAwait(false),
                            "LimeTorrents" or "TokyoTosho" or "Academic" => await ExpandedAsync(s.Kind,query,timeout.Token,page).ConfigureAwait(false),
                            _ => await TorznabAsync(query, s, timeout.Token,page).ConfigureAwait(false)
                        };
                        ct.ThrowIfCancellationRequested();
                        var before=collected.Count;
                        collected=ResultMerger.Merge(collected.Concat(results.Select(r=>PublicTorrentDiscovery.Mark(r,s.Kind)))).DistinctBy(r=>Links.MagnetHash(r.Magnet) ?? Links.NormalizeHash(r.Hash) ?? r.TorrentUrl ?? r.ShareUrl ?? r.DetailsUrl ?? r.Title).ToList();
                        if(results.Count==0 || collected.Count==before || s.Kind is "AutoWeb" or "Nyaa" or "CiliXiong" or "TorrentsCsv" or "TokyoTosho" or "Academic" or "Debian" or "Dmhy" or "Eztv" or "PirateBay" or "Foss") break;
                        var pageSize=s.Kind switch {"LinuxTracker"=>25,"Archive"=>50,"Zhongzilou"=>15,"Tpb"=>30,"LimeTorrents"=>40,_=>100};
                        if(results.Count<pageSize)break;
                        if(page<maxPages)progress?.Invoke(new(s.Name,collected.ToArray()){SourceId=s.Id,SourceKind=s.Kind,ElapsedMilliseconds=timer.ElapsedMilliseconds,IsComplete=false});
                    }
                    ct.ThrowIfCancellationRequested(); Interlocked.Increment(ref success); Publish(new(s.Name, collected.Select(r=>r with {MatchReason=SiteSources.MatchReason(query,r.Title)}).ToList()));
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    ct.ThrowIfCancellationRequested();
                    if(e is PartialSourceException partial)collected=ResultMerger.Merge(collected.Concat(partial.Results));
                    Interlocked.Increment(ref failed); Publish(new(s.Name, collected.Select(r=>r with{MatchReason=SiteSources.MatchReason(query,r.Title)}).ToList(), (collected.Count>0?"已保留部分结果；后续请求失败：":"")+(e is PartialSourceException?e.Message:Links.Error(e))),FailureCode(e));
                }
                } finally {lane.Release();}
            }
            finally { hostGate.Release(); }
        }).ToArray();
        try {await Task.WhenAll(jobs).ConfigureAwait(false);ct.ThrowIfCancellationRequested();}
        finally {foreach(var hostGate in hostGates.Values)hostGate.Dispose();}
        return new(success, failed);
    }
    private Health NewHealth(int failures,string code)=>new(failures,code=="RATE_LIMIT"?now().AddMinutes(2):failures>=2?now().AddSeconds(Math.Min(300,30*failures)):null,code);
    private static string FailureCode(Exception e)=>e switch
    {
        HttpRequestException {StatusCode:HttpStatusCode.TooManyRequests}=>"RATE_LIMIT",
        HttpRequestException {StatusCode:HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden}=>"ACCESS",
        HttpRequestException=>"NETWORK",OperationCanceledException=>"TIMEOUT",
        System.Xml.XmlException or System.Text.Json.JsonException or SourceException=>"RESPONSE",
        _=>"UNKNOWN"
    };
    private static Uri TorznabUri(SourceConfig s, string mode, string? query = null, int page = 1)
    {
        var endpoint = Links.Endpoint(s.Endpoint);
        return new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "?t=" + mode + "&apikey=" + Uri.EscapeDataString(s.ApiKey) + (query is null ? "" : "&q=" + Uri.EscapeDataString(query) + "&limit=100&offset="+((page-1)*100)));
    }
    public async Task<string> TestSourceAsync(SourceConfig source, CancellationToken ct)
    {
        if(source.Kind=="AutoWeb")return $"自动来源请求成功，验证返回 {(await websiteDiscovery.Value.SearchAsync(source,source.DiscoveryQuery,ct)).Count} 条匹配结果";
        if (source.EngineIndexerId.Length > 0)
            source = SourceResolver is not null ? await SourceResolver(source, ct).ConfigureAwait(false) : throw new SourceException("内置搜索引擎尚未启动。");
        if(source.Kind == "Website") return source.Description;
        if(source.Kind == "Foss") return $"连接成功，真实测试返回 {(await FossAsync("Blender",ct)).Count} 条";
        if(source.Kind == "PirateBay") return $"连接成功，真实测试返回 {(await PirateBayAsync("ubuntu",ct)).Count} 条";
        if(source.Kind is "Dmhy" or "Eztv")return $"连接成功，真实测试返回 {(await MediaAsync(source.Kind,source.Kind=="Dmhy"?"naruto":"Sherlock",ct,1)).Count} 条";
        if(ReliableSources.Supports(source.Kind))return $"连接成功，真实测试返回 {(await ReliableAsync(source.Kind,source.Kind=="Debian"?"debian":"ubuntu",ct)).Count} 条";
        if(ExpandedSources.Supports(source.Kind))
        {
            var results=source.Kind=="TorrentsCsv"?await CsvAsync("ubuntu",ct,1):await ExpandedAsync(source.Kind,source.Kind switch {"Academic"=>"mnist","TokyoTosho"=>"naruto",_=>"ubuntu"},ct);
            return $"连接成功，真实测试返回 {results.Count} 条";
        }
        if(source.Kind == "CiliXiong") return $"连接成功，测试返回 {(await CiliXiongAsync("特洛伊",ct)).Count} 个磁力版本";
        if(GlobalSources.Supports(source.Kind)) return $"连接成功，测试返回 {(await GlobalAsync(source.Kind,source.Kind=="Nyaa"?"blender":"ubuntu",ct)).Count} 条";
        if(source.Kind == "Tpb") return $"连接成功，真实测试返回 {(await TpbAsync("ubuntu",ct)).Count} 条";
        if(source.Kind == "Zhongzilou") return $"连接成功，真实测试返回 {(await ZhongzilouAsync("ubuntu",ct)).Count} 条";
        if (source.Kind == "Archive") { var r = await ArchiveAsync("ubuntu", source, ct); return $"连接成功，测试查询返回 {r.Count} 条（最多 50 条）"; }
        var bytes = await GetBytesAsync(TorznabUri(source, "caps"), 2 * 1024 * 1024, ct);
        var doc = Xml(bytes); CheckApiError(doc);
        var mode = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "search" && x.Attribute("available")?.Value == "yes");
        if (mode is null || !(mode.Attribute("supportedParams")?.Value.Split(',').Select(p => p.Trim()).Contains("q") ?? false)) throw new SourceException("该来源没有声明支持关键词搜索（t=search / q）。");
        return "连接成功，支持关键词搜索";
    }
    private async Task<IReadOnlyList<SearchResult>> CiliXiongAsync(string query,CancellationToken ct)
    {
        if(publicCache.TryGetValue("cilixiong:"+query,out var cached)&&DateTimeOffset.UtcNow-cached.Time<TimeSpan.FromMinutes(5))return cached.Results;
        await ciliSearchGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var delay=nextCiliSearch-DateTimeOffset.UtcNow;
            if(delay>TimeSpan.Zero)await Task.Delay(delay,ct).ConfigureAwait(false);
            return await CiliXiongFetchAsync(query,ct).ConfigureAwait(false);
        }
        finally{ciliSearchGate.Release();}
    }
    private async Task<IReadOnlyList<SearchResult>> CiliXiongFetchAsync(string query,CancellationToken ct)
    {
        var key="cilixiong:"+query;
        if(publicCache.TryGetValue(key,out var cached)&&DateTimeOffset.UtcNow-cached.Time<TimeSpan.FromMinutes(5))return cached.Results;
        using var request=new HttpRequestMessage(HttpMethod.Post,"https://www.cilixiong.cc/e/search/index.php")
        {Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"classid","1,2"},{"show","title"},{"tempid","1"},{"keyboard",query}})};
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);
        nextCiliSearch=DateTimeOffset.UtcNow.AddSeconds(5.5);
        byte[] bytes;
        if(response.StatusCode is HttpStatusCode.Found or HttpStatusCode.SeeOther)
        {
            var location=response.Headers.Location??throw new SourceException("磁力熊缺少搜索结果地址。");
            var target=new Uri(request.RequestUri!,location);
            if(target.Scheme!="https"||target.Host!="www.cilixiong.cc"||!target.AbsolutePath.StartsWith("/e/search/result/"))throw new SourceException("磁力熊搜索跳转到未识别的页面。");
            bytes=await GetBytesAsync(target,2*1024*1024,ct).ConfigureAwait(false);
        }
        else
        {
            response.EnsureSuccessStatusCode();
            await using var stream=await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var buffer=new MemoryStream();var chunk=new byte[8192];int count;
            while((count=await stream.ReadAsync(chunk,ct).ConfigureAwait(false))>0){if(buffer.Length+count>2*1024*1024)throw new SourceException("磁力熊响应过大。");buffer.Write(chunk,0,count);}
            bytes=buffer.ToArray();
        }
        var links=CiliXiong.SearchLinks(System.Text.Encoding.UTF8.GetString(bytes));
        var results=new System.Collections.Concurrent.ConcurrentBag<SearchResult>();
        var errors=new System.Collections.Concurrent.ConcurrentBag<string>();
        using var detailGate=new SemaphoreSlim(2);
        await Task.WhenAll(links.Select(async uri=>
        {
            var entered=false;
            try
            {
                await detailGate.WaitAsync(ct).ConfigureAwait(false);entered=true;
                var html=System.Text.Encoding.UTF8.GetString(await GetBytesAsync(uri,2*1024*1024,ct).ConfigureAwait(false));
                foreach(var item in CiliXiong.ParseDetail(html,uri))results.Add(item);
            }
            catch(Exception e) when(e is not OutOfMemoryException){errors.Add(Links.Error(e));}
            finally{if(entered)detailGate.Release();}
        })).ConfigureAwait(false);
        var merged=ResultMerger.Merge(results).OrderBy(r=>r.Title,StringComparer.Ordinal).ToList();
        if(!errors.IsEmpty)throw new PartialSourceException(merged,"磁力熊部分详情未能读取："+errors.First());
        if(publicCache.Count>=96)foreach(var entry in publicCache.OrderBy(x=>x.Value.Time).Take(24))publicCache.TryRemove(entry.Key,out _);
        publicCache[key]=(DateTimeOffset.UtcNow,merged);
        return merged;
    }
    private async Task<IReadOnlyList<SearchResult>> GlobalAsync(string kind,string query,CancellationToken ct,int page=1)
    {
        var uri=GlobalSources.QueryUri(kind,query,page);
        var key=uri.AbsoluteUri;
        if(publicCache.TryGetValue(key,out var cached)&&DateTimeOffset.UtcNow-cached.Time<TimeSpan.FromMinutes(5))return cached.Results;
        var bytes=await GetBytesAsync(uri,4*1024*1024,ct).ConfigureAwait(false);
        var result=GlobalSources.Parse(kind,bytes);
        if(publicCache.Count>=96)foreach(var entry in publicCache.OrderBy(x=>x.Value.Time).Take(24))publicCache.TryRemove(entry.Key,out _);
        publicCache[key]=(DateTimeOffset.UtcNow,result);
        return result;
    }
    private async Task<IReadOnlyList<SearchResult>> ZhongzilouAsync(string query,CancellationToken ct,int page=1)
    {
        var bytes=await GetBytesAsync(new Uri("https://m.zhongzilou.com/list/"+Uri.EscapeDataString(query)+"/"+page),4*1024*1024,ct).ConfigureAwait(false);
        return SiteSources.ParseZhongzilou(System.Text.Encoding.UTF8.GetString(bytes));
    }
    private async Task<IReadOnlyList<SearchResult>> TpbAsync(string query,CancellationToken ct,int page=1)
    {
        var bytes=await GetBytesAsync(new Uri("https://thepiratebay.party/search/"+Uri.EscapeDataString(query)+"/"+page+"/99/0"),4*1024*1024,ct,true).ConfigureAwait(false);
        return SiteSources.ParseTpb(System.Text.Encoding.UTF8.GetString(bytes));
    }
    public async Task<string> VerifySiteMagnetAsync(SearchResult result,CancellationToken ct)
    {
        if(result.DetailsUrl is null || new Uri(result.DetailsUrl).Host != "m.zhongzilou.com") return result.Magnet ?? throw new SourceException("缺少磁力链接。");
        var bytes=await GetBytesAsync(Links.WebUri(result.DetailsUrl),4*1024*1024,ct);
        var doc=new HtmlAgilityPack.HtmlDocument(); doc.LoadHtml(System.Text.Encoding.UTF8.GetString(bytes));
        var node=doc.DocumentNode.SelectSingleNode("//textarea[@id='copytext']");
        var value=WebUtility.HtmlDecode(node?.InnerText ?? doc.DocumentNode.SelectSingleNode("//a[starts-with(@href,'magnet:')]")?.GetAttributeValue("href","") ?? "").Trim();
        if(Links.MagnetHash(value) is not { } hash || hash != Links.MagnetHash(result.Magnet)) throw new SourceException("详情页磁力链接缺失或哈希不一致，已停止交接。");
        return value;
    }
    private async Task<IReadOnlyList<SearchResult>> TorznabAsync(string query, SourceConfig source, CancellationToken ct,int page=1)
    {
        var bytes = await GetBytesAsync(TorznabUri(source, "search", query,page), 8 * 1024 * 1024, ct).ConfigureAwait(false);
        return ParseTorznab(bytes, source.Name);
    }
    public static XDocument Xml(byte[] bytes)
    {
        using var reader = XmlReader.Create(new MemoryStream(bytes), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 });
        return XDocument.Load(reader);
    }
    private static void CheckApiError(XDocument doc)
    {
        if (doc.Root?.Name.LocalName != "error") return;
        var code = doc.Root.Attribute("code")?.Value;
        throw new SourceException(code is "100" or "101" or "102" ? "认证失败，请检查 API Key" : "搜索源报告查询错误，请检查该源的配置。");
    }
    public static IReadOnlyList<SearchResult> ParseTorznab(byte[] bytes, string source)
    {
        var doc = Xml(bytes); CheckApiError(doc);
        if (doc.Root?.Name.LocalName != "rss") throw new SourceException("不是有效的 Torznab RSS 响应。");
        var results = new List<SearchResult>();
        foreach (var item in doc.Descendants().Where(x => x.Name.LocalName == "item").Take(500))
        {
            string? Child(string key) => item.Elements().FirstOrDefault(x => x.Name.LocalName == key)?.Value;
            string? Attr(string key) => item.Elements().FirstOrDefault(x => x.Name.LocalName == "attr" && x.Attribute("name")?.Value == key)?.Attribute("value")?.Value;
            static string? Web(string? url) { try { return url is null ? null : Links.WebUri(url).AbsoluteUri; } catch (SourceException) { return null; } }
            static long? Number(string? s) => long.TryParse(s, out var n) && n >= 0 ? n : null;
            var title = Child("title"); if (string.IsNullOrWhiteSpace(title)) continue;
            var enclosure = item.Elements().FirstOrDefault(x => x.Name.LocalName == "enclosure");
            var cover=ResultPresentation.SafeCover(Attr("coverurl")) ?? ResultPresentation.SafeCover(Attr("poster"))
                ?? ResultPresentation.SafeCover(item.Elements().FirstOrDefault(x=>x.Name.LocalName=="thumbnail")?.Attribute("url")?.Value);
            var link = Child("link");
            var magnet = new[] { Attr("magneturl"), enclosure?.Attribute("url")?.Value, link }.FirstOrDefault(x => Links.MagnetHash(x) is not null);
            var torrent = enclosure?.Attribute("type")?.Value == "application/x-bittorrent" ? Web(enclosure.Attribute("url")?.Value) : null;
            // A generic <link> is often a details page. Do not pass it as a torrent.
            var details = Web(Child("comments")) ?? (torrent is null ? Web(link) : null);
            if (magnet is null && torrent is null) continue;
            results.Add(new() { Title = title.Length > 1000 ? title[..1000] : title, Source = source, Magnet = magnet, TorrentUrl = torrent,
                DetailsUrl = details, CoverUrl=cover, CoverSource=cover is null?null:source, Hash = Links.NormalizeHash(Attr("infohash")) ?? Links.MagnetHash(magnet), Size = Number(Child("size") ?? enclosure?.Attribute("length")?.Value),
                Seeders = Number(Attr("seeders")), Category = Child("category") ?? "未知", Published = DateTimeOffset.TryParse(Child("pubDate"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : null });
        }
        return results;
    }
    private async Task<IReadOnlyList<SearchResult>> ArchiveAsync(string query, SourceConfig source, CancellationToken ct,int page=1)
    {
        // Quote each token so input remains plain keywords rather than an Archive query expression.
        var tokens = Regex.Matches(query, @"[^\s""\\]+").Select(m => "\"" + m.Value + "\"").ToArray();
        if (tokens.Length == 0) return [];
        var expression = "title:(" + string.Join(" AND ", tokens) + ") AND format:\"Archive BitTorrent\"";
        var uri = new Uri("https://archive.org/advancedsearch.php?q=" + Uri.EscapeDataString(expression) + "&fl[]=identifier&fl[]=title&fl[]=mediatype&fl[]=publicdate&rows=50&page="+page+"&output=json&sort[]=identifier+asc");
        var bytes = await GetBytesAsync(uri, 4 * 1024 * 1024, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(bytes);
        if (!doc.RootElement.TryGetProperty("response", out var response) || !response.TryGetProperty("docs", out var docs)) throw new SourceException("公开源返回格式异常。");
        var result = new List<SearchResult>();
        foreach (var item in docs.EnumerateArray())
        {
            var id = item.GetProperty("identifier").GetString(); if (string.IsNullOrWhiteSpace(id)) continue;
            string? Field(string key) => item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            result.Add(new() { Title = Field("title") ?? id, Source = source.Name, ArchiveId = id, DetailsUrl = "https://archive.org/details/" + Uri.EscapeDataString(id), Category = Field("mediatype") ?? "未知", Published = DateTimeOffset.TryParse(Field("publicdate"), out var dt) ? dt : null });
        }
        return result;
    }
    public async Task<string> ResolveTorrentAsync(SearchResult result, CancellationToken ct)
    {
        if (result.TorrentUrl is not null) return Links.WebUri(result.TorrentUrl).AbsoluteUri;
        if (result.ArchiveId is not { } id) throw new SourceException("此结果没有可用的种子文件。");
        var bytes = await GetBytesAsync(new Uri("https://archive.org/metadata/" + Uri.EscapeDataString(id)), 8 * 1024 * 1024, ct);
        using var json = JsonDocument.Parse(bytes);
        if (json.RootElement.TryGetProperty("files", out var files)) foreach (var f in files.EnumerateArray())
        {
            if (!f.TryGetProperty("format", out var format) || format.GetString() != "Archive BitTorrent") continue;
            var name = f.GetProperty("name").GetString();
            if (name?.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) == true)
                return "https://archive.org/download/" + Uri.EscapeDataString(id) + "/" + Uri.EscapeDataString(name);
        }
        throw new SourceException("该条目的种子文件已不可用，请查看来源页面。");
    }
    public void Dispose(){http.Dispose();if(websiteDiscovery.IsValueCreated)websiteDiscovery.Value.Dispose();}
}

