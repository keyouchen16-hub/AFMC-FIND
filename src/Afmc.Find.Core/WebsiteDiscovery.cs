using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Afmc.Find.Core;

public sealed record DiscoveryResult(SourceConfig? Source,IReadOnlyList<SearchResult> Preview,IReadOnlyList<string> Steps,string Message);
public sealed record DiscoveryCandidate(string Template,string Format,string Evidence);

// Read-only, bounded discovery of explicitly advertised public search interfaces.
// This does not use the Jackett catalog or guess credentials/endpoints.
public sealed partial class WebsiteDiscovery:IDisposable
{
    private readonly HttpClient http;
    private readonly Func<string,CancellationToken,Task<IPAddress[]>> dns;
    private readonly bool browserEnabled;
    private const string Term="{searchTerms}";
    private static readonly string[] QueryNames=["q","query","keyword","keywords","search","s","wd","search_query","term"];
    public WebsiteDiscovery(HttpMessageHandler? handler=null,Func<string,CancellationToken,Task<IPAddress[]>>? resolver=null,bool? enableBrowser=null)
    {
        http=new HttpClient(handler??PublicTransport()){Timeout=TimeSpan.FromSeconds(12)};
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; AFMC-FIND/0.7.11; public-search-discovery)");
        dns=resolver??((host,ct)=>Dns.GetHostAddressesAsync(host,ct));
        browserEnabled=enableBrowser??(handler is null && resolver is null);
    }
    private static HttpMessageHandler PublicTransport()
    {
        var handler=new SocketsHttpHandler{AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.All,UseCookies=false,ConnectTimeout=TimeSpan.FromSeconds(10)};
        // HTTPS CONNECT callbacks can carry the proxy URI rather than the original target.
        // Capture only endpoints selected by the existing system proxy configuration.
        var proxyEndpoints=new[]{new Uri("https://example.com/"),new Uri("http://example.com/")}
            .Where(uri=>!HttpClient.DefaultProxy.IsBypassed(uri))
            .Select(uri=>(Original:uri,Proxy:HttpClient.DefaultProxy.GetProxy(uri)))
            .Where(p=>p.Proxy is not null&&p.Proxy!=p.Original).Select(p=>p.Proxy!).ToArray();
        handler.ConnectCallback=async(context,ct)=>
        {
            var trustedProxy=proxyEndpoints.Any(proxy=>proxy.Host.Equals(context.DnsEndPoint.Host,StringComparison.OrdinalIgnoreCase)&&proxy.Port==context.DnsEndPoint.Port);
            var addresses=await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host,ct).ConfigureAwait(false);
            if(!trustedProxy&&addresses.Any(ip=>!PublicAddress(ip)))throw new SourceException("连接目标变为非公开地址，已停止。");
            foreach(var address in addresses)
            {
                var socket=new Socket(address.AddressFamily,SocketType.Stream,ProtocolType.Tcp);
                try{await socket.ConnectAsync(new IPEndPoint(address,context.DnsEndPoint.Port),ct).ConfigureAwait(false);return new NetworkStream(socket,true);}
                catch(SocketException){socket.Dispose();}catch{socket.Dispose();throw;}
            }
            throw new HttpRequestException("无法连接公开网站。");
        };
        return handler;
    }
    public static bool PublicAddress(IPAddress address)
    {
        if(address.IsIPv4MappedToIPv6)address=address.MapToIPv4();
        if(IPAddress.IsLoopback(address))return false;
        var b=address.GetAddressBytes();
        if(b.Length==4)return b[0] is not (0 or 10 or 127) && b[0]<224 && !(b[0]==169&&b[1]==254) && !(b[0]==172&&b[1]>=16&&b[1]<=31) && !(b[0]==192&&b[1]==168) && !(b[0]==100&&b[1]>=64&&b[1]<=127) && !(b[0]==198&&b[1] is 18 or 19);
        return !address.Equals(IPAddress.IPv6None)&&!address.Equals(IPAddress.IPv6Any)&&!address.IsIPv6LinkLocal&&!address.IsIPv6Multicast&&!address.IsIPv6SiteLocal&&(b[0]&0xfe)!=0xfc;
    }
    public static Uri PublicUrl(string text)
    {
        var uri=Links.WebUri(text.Trim());
        if(!uri.IsDefaultPort||uri.Fragment.Length>0||uri.Host.Equals("localhost",StringComparison.OrdinalIgnoreCase)||!uri.Host.Contains('.')||uri.Host.EndsWith(".local",StringComparison.OrdinalIgnoreCase)||uri.Host.EndsWith(".internal",StringComparison.OrdinalIgnoreCase))throw new SourceException("自动探测仅支持公开网站的标准 HTTP/HTTPS 地址。");
        if(IPAddress.TryParse(uri.Host,out var ip)&&!PublicAddress(ip))throw new SourceException("自动探测不能访问本机或内网地址。");
        if(Regex.IsMatch(uri.Query,@"(?:[?&])(?:api_?key|token|access_token|password|passkey|auth)=",RegexOptions.IgnoreCase))throw new SourceException("请粘贴不含密码或 Key 的公开网址；自动探测不会提取私有凭据。");
        return uri;
    }
    private sealed class Budget { public int Requests; public long Bytes; public readonly Dictionary<string,Page> Pages=[]; }
    private sealed record Page(Uri Url,byte[] Bytes,string Type)
    {public string Text=>Encoding.UTF8.GetString(Bytes);}
    private async Task<Page> Fetch(Uri url,Budget budget,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var original=url.AbsoluteUri;if(budget.Pages.TryGetValue(original,out var cached))return cached;
        for(int redirect=0;redirect<4;redirect++)
        {
            PublicUrl(url.AbsoluteUri);var addresses=await dns(url.DnsSafeHost,ct).ConfigureAwait(false);
            if(addresses.Length==0||addresses.Any(ip=>!PublicAddress(ip)))throw new SourceException("网站解析到了非公开地址，已停止探测。");
            if(++budget.Requests>20)throw new SourceException("已达到本轮探测请求上限。");
            using var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);
            if((int)response.StatusCode is >=300 and <400)
            {var next=response.Headers.Location is {} location?new Uri(url,location):throw new SourceException("网站跳转没有目标地址。");if(url.Scheme=="https"&&next.Scheme!="https")throw new SourceException("网站跳转降低了连接安全性，已停止。");url=next;continue;}
            if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)throw new SourceException("网站要求认证或拦截了自动请求，尚不能直接添加。");
            response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength>4*1024*1024)throw new SourceException("候选响应超过 4 MB 探测上限。");
            using var stream=await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);using var memory=new MemoryStream();var buffer=new byte[16384];int n;
            while((n=await stream.ReadAsync(buffer,ct).ConfigureAwait(false))>0){budget.Bytes+=n;if(memory.Length+n>4*1024*1024||budget.Bytes>20*1024*1024)throw new SourceException("已达到探测数据量上限。");memory.Write(buffer,0,n);}
            var page=new Page(url,memory.ToArray(),response.Content.Headers.ContentType?.MediaType??"");budget.Pages[original]=page;return page;
        }
        throw new SourceException("网站跳转次数过多。");
    }
    private static HtmlDocument Html(string text){var doc=new HtmlDocument();doc.LoadHtml(text);return doc;}
    private static string Clean(string text)=>Regex.Replace(WebUtility.HtmlDecode(text),@"\s+"," ").Trim();
    private static bool Matches(string title,string query)=>query.Split(' ',StringSplitOptions.RemoveEmptyEntries).All(word=>title.Contains(word,StringComparison.OrdinalIgnoreCase));
    private static bool SameOrigin(Uri a,Uri b)=>a.Scheme==b.Scheme&&a.Host.Equals(b.Host,StringComparison.OrdinalIgnoreCase)&&a.Port==b.Port;
    private static string? Local(Uri page,string? link)
    {try{if(string.IsNullOrWhiteSpace(link))return null;var uri=PublicUrl(new Uri(page,WebUtility.HtmlDecode(link)).AbsoluteUri);return SameOrigin(page,uri)?uri.AbsoluteUri:null;}catch(Exception ex)when(ex is SourceException or UriFormatException){return null;}}
    public static IReadOnlyList<DiscoveryCandidate> HtmlCandidates(string text,Uri page)
    {
        var doc=Html(text);var found=new List<DiscoveryCandidate>();
        foreach(var form in doc.DocumentNode.SelectNodes("//form")??Enumerable.Empty<HtmlNode>())
        {
            if(!form.GetAttributeValue("method","get").Equals("get",StringComparison.OrdinalIgnoreCase)||form.SelectSingleNode(".//input[translate(@type,'PASSWORD','password')='password']") is not null)continue;
            var inputs=form.SelectNodes(".//input[@name]")?.ToArray()??[];
            var query=inputs.FirstOrDefault(x=>x.GetAttributeValue("type","").Equals("search",StringComparison.OrdinalIgnoreCase))??inputs.FirstOrDefault(x=>QueryNames.Contains(x.GetAttributeValue("name","").ToLowerInvariant()));
            if(query is null)continue;
            var action=Local(page,form.GetAttributeValue("action",page.AbsoluteUri));if(action is null||Regex.IsMatch(new Uri(action).AbsolutePath,@"/(?:delete|remove|logout|unsubscribe|purchase|checkout)(?:/|\.|$)",RegexOptions.IgnoreCase))continue;
            var fields=inputs.Where(x=>x!=query&&x.GetAttributeValue("type","").Equals("hidden",StringComparison.OrdinalIgnoreCase)).Select(x=>(Name:x.GetAttributeValue("name",""),Value:x.GetAttributeValue("value",""))).Where(x=>x.Name.Length<80&&x.Value.Length<200&&!Regex.IsMatch(x.Name,"key|token|auth|pass|csrf",RegexOptions.IgnoreCase)).ToList();
            fields.Add((query.GetAttributeValue("name","q"),Term));
            var uri=new UriBuilder(action);var existing=uri.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries).Where(p=>!fields.Any(f=>Uri.UnescapeDataString(p.Split('=',2)[0])==f.Name));
            uri.Query=string.Join("&",existing.Concat(fields.Select(p=>Uri.EscapeDataString(p.Name)+"="+(p.Value==Term?Term:Uri.EscapeDataString(p.Value)))));
            found.Add(new(uri.Uri.AbsoluteUri.Replace("%7BsearchTerms%7D",Term,StringComparison.OrdinalIgnoreCase),"Search","网页公开 GET 搜索表单"));
        }
        foreach(var link in doc.DocumentNode.SelectNodes("//link[@href]|//a[@href]")??Enumerable.Empty<HtmlNode>())
        {
            var href=Local(page,link.GetAttributeValue("href",""));if(href is null)continue;
            var type=link.GetAttributeValue("type","");
            if(type.Contains("opensearchdescription",StringComparison.OrdinalIgnoreCase))found.Add(new(href,"OpenSearch","网页声明的 OpenSearch 描述"));
            else if(type.Contains("rss",StringComparison.OrdinalIgnoreCase)||type.Contains("atom",StringComparison.OrdinalIgnoreCase)||Regex.IsMatch(new Uri(href).AbsolutePath,@"(?:rss|feed|torrents)\.(?:xml|rss)$",RegexOptions.IgnoreCase))found.Add(new(href,"Feed","网页公开的订阅地址"));
            else if(Regex.IsMatch(new Uri(href).AbsolutePath,@"(?:openapi|swagger)\.(?:json)$",RegexOptions.IgnoreCase))found.Add(new(href,"OpenApi","网页链接的公开 API 描述"));
            else if(Regex.IsMatch(new Uri(href).AbsolutePath,@"/(?:feeds?|rss|api|developers|docs)/?$",RegexOptions.IgnoreCase))found.Add(new(href,"Index","网站公开的订阅或接口说明页"));
        }
        return found.DistinctBy(x=>x.Template).Take(12).ToArray();
    }
    public static IReadOnlyList<DiscoveryCandidate> OpenSearchCandidates(byte[] bytes,Uri page)
    {
        var doc=SearchService.Xml(bytes);if(doc.Root?.Name.LocalName!="OpenSearchDescription")return [];
        var found=new List<DiscoveryCandidate>();
        foreach(var element in doc.Descendants().Where(e=>e.Name.LocalName=="Url"))
        {
            var template=element.Attribute("template")?.Value;if(template is null||!template.Contains(Term))continue;
            template=Regex.Replace(template,@"\{(?:startPage|startIndex|count|language|inputEncoding|outputEncoding)\??\}",m=>m.Value.Contains("Encoding")?"UTF-8":m.Value.Contains("language")?"en":m.Value.Contains("count")?"50":"1");
            if(Regex.IsMatch(template.Replace(Term,""),@"[{}]"))continue;
            var absolute=Local(page,template.Replace(Term,"AFMCTERM"));if(absolute is not null)found.Add(new(absolute.Replace("AFMCTERM",Term),"Search","OpenSearch 关键词模板"));
        }
        return found;
    }
    public static IReadOnlyList<DiscoveryCandidate> OpenApiCandidates(byte[] bytes,Uri page)
    {
        using var doc=JsonDocument.Parse(bytes);var root=doc.RootElement;var found=new List<DiscoveryCandidate>();
        if(root.ValueKind!=JsonValueKind.Object)return found;
        if(!root.TryGetProperty("paths",out var paths)||paths.ValueKind!=JsonValueKind.Object)return found;
        if(root.TryGetProperty("security",out var security)&&security.ValueKind==JsonValueKind.Array&&security.GetArrayLength()>0)return found;
        var server=page.GetLeftPart(UriPartial.Authority)+"/";
        if(root.TryGetProperty("servers",out var servers)&&servers.ValueKind==JsonValueKind.Array&&servers.GetArrayLength()>0&&servers[0].TryGetProperty("url",out var serverValue))server=Local(page,serverValue.GetString())??server;
        foreach(var path in paths.EnumerateObject())
        {
            if(path.Name.Contains('{')||!path.Value.TryGetProperty("get",out var get)||get.TryGetProperty("security",out var auth)&&auth.ValueKind==JsonValueKind.Array&&auth.GetArrayLength()>0)continue;
            if(!get.TryGetProperty("parameters",out var parameters)||parameters.ValueKind!=JsonValueKind.Array)continue;
            var items=parameters.EnumerateArray().Where(p=>p.ValueKind==JsonValueKind.Object).ToArray();
            var query=items.FirstOrDefault(p=>p.TryGetProperty("in",out var loc)&&loc.GetString()=="query"&&p.TryGetProperty("name",out var name)&&QueryNames.Contains(name.GetString()??""));
            if(query.ValueKind==JsonValueKind.Undefined)continue;
            if(items.Any(p=>p.TryGetProperty("required",out var r)&&r.ValueKind==JsonValueKind.True&&p.GetRawText()!=query.GetRawText()))continue;
            var target=Local(page,server.TrimEnd('/')+"/"+path.Name.TrimStart('/'));if(target is not null)found.Add(new(target+"?"+Uri.EscapeDataString(query.GetProperty("name").GetString()!)+"="+Term,"Search","公开 OpenAPI GET 搜索操作"));
        }
        return found.Take(6).ToArray();
    }
    public async Task<DiscoveryResult> DiscoverAsync(string address,string query,IProgress<string>? progress,CancellationToken ct)
    {
        PublicUrl(address);
        using var staticTime=CancellationTokenSource.CreateLinkedTokenSource(ct);
        staticTime.CancelAfter(TimeSpan.FromSeconds(45));
        DiscoveryResult initial;
        try{initial=await DiscoverStaticAsync(address,query,progress,staticTime.Token).ConfigureAwait(false);}
        catch(OperationCanceledException)when(!ct.IsCancellationRequested){initial=new(null,[],[],"静态识别超时。");}
        if(initial.Source is not null||!browserEnabled)return initial;
        ct.ThrowIfCancellationRequested();
        var steps=initial.Steps.ToList();
        void Step(string value){lock(steps){steps.Add(value);progress?.Report(value);}}
        Step("静态页面未验证成功，正在用隔离浏览器检查动态搜索和下载按钮。");
        try{return await DiscoverBrowserAsync(address,query,steps,Step,ct).ConfigureAwait(false);}
        catch(OperationCanceledException)when(!ct.IsCancellationRequested){return new(null,[],steps,"动态识别达到时间上限；尚未添加，可填写具体作品名缩小探测范围。");}
        catch(Exception ex)when(ex is not OutOfMemoryException){ct.ThrowIfCancellationRequested();return new(null,[],steps,ex is SourceException?ex.Message:ex is TimeoutException?"动态页面加载或操作超时，尚未添加。":"动态页面未完成验证（"+ex.GetType().Name+"）。网站可能要求交互验证、改变了结构或网络连接失败；尚未添加。");}
    }
    private async Task<DiscoveryResult> DiscoverStaticAsync(string address,string query,IProgress<string>? progress,CancellationToken ct)
    {
        var steps=new List<string>();void Step(string s){steps.Add(s);progress?.Report(s);}
        try
        {
            if(query.Length>100||query.Any(char.IsControl))throw new SourceException("验证关键词最多 100 个字符；也可留空自动选取本站样本。");
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(75));ct=timeout.Token;
            var budget=new Budget();var home=await Fetch(PublicUrl(address),budget,ct).ConfigureAwait(false);Step("已读取网站，检查公开搜索入口与订阅声明。");
            var name=Clean(Html(home.Text).DocumentNode.SelectSingleNode("//title")?.InnerText??home.Url.Host);if(name.Length>80)name=name[..80];
            var candidates=HtmlCandidates(home.Text,home.Url).ToList();
            if(candidates.Count==0&&home.Url.AbsolutePath!="/")
            {
                try{var root=await Fetch(new Uri(home.Url.GetLeftPart(UriPartial.Authority)+"/"),budget,ct).ConfigureAwait(false);candidates.AddRange(HtmlCandidates(root.Text,root.Url));Step("已检查同站首页的搜索入口。");}
                catch(Exception ex)when(ex is HttpRequestException or SourceException or TaskCanceledException){ct.ThrowIfCancellationRequested();}
            }
            var automatic=string.IsNullOrWhiteSpace(query);
            IReadOnlyList<string> probeQueries=automatic?await AutoProbe(home,budget,Step,ct).ConfigureAwait(false):[query];
            if(home.Text.TrimStart().StartsWith('<'))
            {try{var root=SearchService.Xml(home.Bytes).Root?.Name.LocalName;if(root is "rss" or "feed")candidates.Insert(0,new(home.Url.AbsoluteUri,"Feed","直接读取公开订阅"));else if(root=="OpenSearchDescription")candidates.AddRange(OpenSearchCandidates(home.Bytes,home.Url));}catch(System.Xml.XmlException){}}
            if(home.Text.TrimStart().StartsWith('{')||home.Text.TrimStart().StartsWith('['))
            {
                try{candidates.AddRange(OpenApiCandidates(home.Bytes,home.Url));}catch(JsonException){}
                foreach(var part in home.Url.Query.TrimStart('?').Split('&')){var pair=part.Split('=',2);if(pair.Length==2&&QueryNames.Contains(pair[0]))candidates.Add(new(home.Url.GetLeftPart(UriPartial.Path)+"?"+pair[0]+"="+Term,"Search","输入地址中的查询参数"));}
            }
            // Read a small number of advertised same-origin scripts for literal GET search URLs.
            var html=Html(home.Text);
            var scripts=html.DocumentNode.SelectNodes("//script")?.Take(4).ToArray()??[];
            foreach(var script in scripts)
            {
                var text=script.InnerText;var src=Local(home.Url,script.GetAttributeValue("src",""));
                if(src is not null&&candidates.Count==0){try{text=(await Fetch(new Uri(src),budget,ct).ConfigureAwait(false)).Text;}catch(Exception ex)when(ex is HttpRequestException or SourceException or TaskCanceledException){ct.ThrowIfCancellationRequested();continue;}}
                foreach(Match m in Regex.Matches(text,"[\"'](?<url>[^\"'\\s]{1,240}[?&](?:q|query|keyword|search)=)[\"']",RegexOptions.IgnoreCase))
                {var url=Local(home.Url,m.Groups["url"].Value);if(url is not null)candidates.Add(new(url+Term,"Search","网页脚本中声明的查询地址"));}
            }
            var visited=new HashSet<string>();
            foreach(var probeQuery in probeQueries)
            {
            query=probeQuery;if(automatic)Step("自动选择本站验证词："+query);
            var queue=new Queue<DiscoveryCandidate>(candidates.DistinctBy(c=>c.Template).Take(12));
            while(queue.Count>0&&budget.Requests<18)
            {
                var candidate=queue.Dequeue();if(!visited.Add(candidate.Template+"|"+query))continue;Step("发现候选："+candidate.Evidence+"，正在验证。");
                try
                {
                    if(candidate.Format is "OpenSearch" or "OpenApi")
                    {var description=await Fetch(new Uri(candidate.Template),budget,ct).ConfigureAwait(false);foreach(var expanded in candidate.Format=="OpenSearch"?OpenSearchCandidates(description.Bytes,description.Url):OpenApiCandidates(description.Bytes,description.Url))queue.Enqueue(expanded);continue;}
                    if(candidate.Format=="Index")
                    {var index=await Fetch(new Uri(candidate.Template),budget,ct).ConfigureAwait(false);foreach(var entry in HtmlCandidates(index.Text,index.Url).Where(c=>c.Format!="Index"))queue.Enqueue(entry);continue;}
                    var source=new SourceConfig{Id="auto-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(candidate.Template))).ToLowerInvariant()[..20],Kind="AutoWeb",Name=name,Endpoint=home.Url.AbsoluteUri,DiscoveryTemplate=candidate.Template,DiscoveryFormat=candidate.Format,DiscoveryQuery=query,Description=candidate.Format=="Feed"?"自动探测 · 仅检索订阅现有条目":"自动探测 · 已验证关键词入口"};
                    var raw=await ReadResults(source,query,budget,ct).ConfigureAwait(false);var matches=raw.Where(r=>Matches(r.Title,query)).Take(100).ToArray();
                    if(matches.Length==0){Step("候选未返回与验证词匹配的可下载条目；未判定为可用。");continue;}
                    if(candidate.Format!="Feed")
                    {
                        var negative=await ReadResults(source,"afmcprobe"+Guid.NewGuid().ToString("N"),budget,ct,query).ConfigureAwait(false);
                        var ids=matches.Select(Identity).ToHashSet();if(negative.Any(r=>ids.Contains(Identity(r)))){Step("对照查询仍返回相同条目，拒绝将推荐内容当作搜索结果。");continue;}
                    }
                    var sample=matches[0];
                    if(sample.Magnet is null&&sample.TorrentUrl is {} torrent){var data=await Fetch(PublicUrl(torrent),budget,ct).ConfigureAwait(false);TorrentFile.Validate(data.Bytes);Step("已校验样本种子元数据。");}
                    Step(candidate.Format=="Feed"?"订阅条目检索验证通过；范围限于当前订阅，不代表全站搜索。":"关键词及无匹配词对照验证通过。");
                    return new(source,matches,steps,$"可添加 · 验证返回 {matches.Length} 条匹配结果。"+(candidate.Format=="Feed"?"仅检索订阅现有条目。":""));
                }
                catch(Exception ex)when(ex is not OutOfMemoryException){ct.ThrowIfCancellationRequested();Step("候选未通过："+Links.Error(ex));}
            }
            }
            return new(null,[],steps,visited.Count==0?"未发现可验证的公开搜索入口。该站可能依赖动态脚本、登录或专门适配；尚未添加。":"发现了候选入口，但未验证出可下载结果。可换一个该站确实存在的关键词再试；尚未添加。");
        }
        catch(Exception ex)when(ex is not OutOfMemoryException){if(ct.IsCancellationRequested)throw;return new(null,[],steps,Links.Error(ex));}
    }
    private static string Identity(SearchResult r)=>r.Hash??r.Magnet??r.TorrentUrl??r.ShareUrl??r.Title;
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SourceConfig source,string query,CancellationToken ct)
    {
        if(source.DiscoveryFormat=="BrowserSearch")return await BrowserSearchAsync(source,query,ct).ConfigureAwait(false);
        if(source.DiscoveryFormat is not ("Search" or "Feed")||source.DiscoveryTemplate.Length==0||source.DiscoveryFormat=="Search"&&!source.DiscoveryTemplate.Contains(Term))throw new SourceException("自动来源配置无效，请重新探测。");
        if(string.IsNullOrWhiteSpace(query)||query.Length>200||query.Any(char.IsControl))throw new SourceException("请输入有效的搜索关键词。");
        return (await ReadResults(source,query,new Budget(),ct).ConfigureAwait(false)).Where(r=>Matches(r.Title,query)).Take(100).ToArray();
    }
    private async Task<IReadOnlyList<SearchResult>> ReadResults(SourceConfig source,string query,Budget budget,CancellationToken ct,string? controlQuery=null)
    {
        var url=PublicUrl(source.DiscoveryTemplate.Replace(Term,Uri.EscapeDataString(query)));var page=await Fetch(url,budget,ct).ConfigureAwait(false);
        if(source.DiscoveryFormat=="Feed"&&SearchService.Xml(page.Bytes).Root?.Name.LocalName is not ("rss" or "feed"))throw new SourceException("订阅地址已不再返回有效 RSS/Atom。");
        var results=ParseResults(page.Bytes,page.Url,source.Name).ToList();
        if(results.Count==0&&(page.Text.Contains("cf-chl-",StringComparison.OrdinalIgnoreCase)||page.Text.Contains("Just a moment",StringComparison.OrdinalIgnoreCase)||Html(page.Text).DocumentNode.SelectSingleNode("//input[@type='password']") is not null))throw new SourceException("候选返回登录或验证页面，未得到搜索结果。");
        if(source.DiscoveryFormat=="Search"&&page.Text.TrimStart().StartsWith('<'))
            results.AddRange(await FollowDetails(page,controlQuery??query,source.Name,budget,ct,controlQuery is not null).ConfigureAwait(false));
        return results.DistinctBy(Identity).Take(500).ToArray();
    }
    public static IReadOnlyList<SearchResult> ParseResults(byte[] bytes,Uri page,string source)
    {
        var text=Encoding.UTF8.GetString(bytes).TrimStart();var rows=new List<SearchResult>();
        string? Web(string? link){try{return string.IsNullOrWhiteSpace(link)?null:PublicUrl(new Uri(page,WebUtility.HtmlDecode(link)).AbsoluteUri).AbsoluteUri;}catch(Exception ex)when(ex is SourceException or UriFormatException){return null;}}
        void Add(string title,string? magnet,string? torrent,string? detail)
        {title=Clean(title);if(title.Length is <1 or >1000)return;var hash=Links.MagnetHash(magnet);torrent=Web(torrent);if(hash is null&&torrent is null)return;rows.Add(new(){Title=title,Source=source,Magnet=hash is null?null:magnet,TorrentUrl=torrent,Hash=hash,DetailsUrl=Web(detail)});}
        if(text.StartsWith('{')||text.StartsWith('['))
        {
            using var doc=JsonDocument.Parse(bytes);int visited=0;
            if(doc.RootElement.ValueKind==JsonValueKind.Object&&doc.RootElement.EnumerateObject().Any(p=>p.Name is "error" or "errors"&&p.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.False)))throw new SourceException("候选接口返回错误信息，未得到搜索结果。");
            void Walk(JsonElement item,int depth)
            {
                if(depth>8||++visited>3000)return;
                if(item.ValueKind==JsonValueKind.Array){foreach(var child in item.EnumerateArray().Take(500))Walk(child,depth+1);return;}
                if(item.ValueKind!=JsonValueKind.Object)return;
                string? Field(params string[] names){foreach(var p in item.EnumerateObject())if(names.Contains(p.Name.ToLowerInvariant())&&p.Value.ValueKind==JsonValueKind.String)return p.Value.GetString();return null;}
                var title=Field("title","name","filename");
                if(title is not null){var magnet=Field("magnet","magneturl","magnet_url");var hash=Links.NormalizeHash(Field("info_hash","infohash","hash"));if(magnet is null&&hash?.StartsWith("btih:")==true)magnet="magnet:?xt=urn:"+hash;Add(title,magnet,Field("torrent_url","torrenturl","torrent"),Field("details_url","details","url","link"));}
                foreach(var p in item.EnumerateObject())if(p.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)Walk(p.Value,depth+1);
            }
            Walk(doc.RootElement,0);return rows;
        }
        if(text.StartsWith('<'))
        {
            try
            {
                var doc=SearchService.Xml(bytes);
                if(doc.Root?.Name.LocalName is "rss" or "feed")
                {
                    foreach(var item in doc.Descendants().Where(x=>x.Name.LocalName is "item" or "entry").Take(2000))
                    {
                        var title=item.Elements().FirstOrDefault(x=>x.Name.LocalName=="title")?.Value??"";string? magnet=null,torrent=null,details=null;
                        foreach(var node in item.Elements())
                        {
                            var value=node.Attribute("url")?.Value??node.Attribute("href")?.Value??(node.Name.LocalName=="attr"?node.Attribute("value")?.Value:node.Value);
                            if(Links.MagnetHash(value)is not null)magnet=value;
                            else if(node.Attribute("type")?.Value=="application/x-bittorrent"||(Web(value)is {} href&&new Uri(href).AbsolutePath.EndsWith(".torrent",StringComparison.OrdinalIgnoreCase)))torrent=value;
                            else if(node.Name.LocalName is "link" or "comments")details=value;
                        }
                        Add(title,magnet,torrent,details);
                    }
                    return rows;
                }
            }catch(System.Xml.XmlException){if(text.Contains("<!DOCTYPE",StringComparison.OrdinalIgnoreCase)&&!text.Contains("<html",StringComparison.OrdinalIgnoreCase))throw;}
        }
        foreach(var row in HtmlDownloads(text,page,source))rows.Add(row);
        return rows.DistinctBy(Identity).ToArray();
    }
    public void Dispose()=>http.Dispose();
}
