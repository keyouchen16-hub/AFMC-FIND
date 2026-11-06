using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Afmc.Find.Core;

public sealed partial class WebsiteDiscovery
{
    private static readonly SemaphoreSlim BrowserSlot=new(1,1);
    private sealed class Activity
    {
        public readonly ConcurrentDictionary<IRequest,byte> Pending=new();
        public long LastChange=Environment.TickCount64;
    }
    private static readonly ConditionalWeakTable<IPage,Activity> Activities=new();
    private static Activity Track(IPage page)=>Activities.GetValue(page,p=>
    {
        var state=new Activity();
        p.Request+=(_,r)=>{if(r.ResourceType is "document" or "script" or "stylesheet" or "xhr" or "fetch"){state.Pending.TryAdd(r,0);Interlocked.Exchange(ref state.LastChange,Environment.TickCount64);}};
        void Done(object? sender,IRequest r){state.Pending.TryRemove(r,out _);Interlocked.Exchange(ref state.LastChange,Environment.TickCount64);}
        p.RequestFinished+=Done;p.RequestFailed+=Done;return state;
    });
    private const string SearchInput="input[type=search]:visible, input[name=q]:visible, input[name=keyword]:visible, input[name=s]:visible";
    private sealed class BrowserSession : IAsyncDisposable
    {
        private readonly IPlaywright driver;
        private readonly IBrowser browser;
        public IBrowserContext Context {get;}
        private BrowserSession(IPlaywright driver,IBrowser browser,IBrowserContext context){this.driver=driver;this.browser=browser;Context=context;}
        public static async Task<BrowserSession> Create()
        {
            var driver=await Playwright.CreateAsync().ConfigureAwait(false);
            try
            {
                var browser=await driver.Chromium.LaunchAsync(new(){Channel="msedge",Headless=true,Timeout=15000,Args=["--disable-background-networking","--disable-quic"]}).ConfigureAwait(false);
                try
                {
                    var context=await browser.NewContextAsync(new(){AcceptDownloads=false,ServiceWorkers=ServiceWorkerPolicy.Block,ViewportSize=new(){Width=1280,Height=900}}).ConfigureAwait(false);
                    context.SetDefaultTimeout(7000);context.SetDefaultNavigationTimeout(15000);
                    await context.AddInitScriptAsync("window.__afmcOpened=[];window.open=(u)=>{if(u)window.__afmcOpened.push(String(u));return null;};").ConfigureAwait(false);
                    return new(driver,browser,context);
                }
                catch{await browser.CloseAsync().ConfigureAwait(false);throw;}
            }
            catch{driver.Dispose();throw new SourceException("无法启动隔离浏览器。请确认 Microsoft Edge 已安装且未被组织策略禁止；静态搜索源仍可使用。");}
        }
        public async ValueTask DisposeAsync(){try{await browser.CloseAsync().ConfigureAwait(false);}finally{driver.Dispose();}}
    }

    // Every browser request uses the same pinned public-address transport as static discovery.
    // No normal browser profile, API keys, authentication storage, file downloads or private sockets.
    private async Task InstallRoutes(IBrowserContext context,Uri origin,CancellationToken ct,Action<string>? diagnostic)
    {
        int requests=0;long totalBytes=0;
        await context.RouteWebSocketAsync("**/*",async socket=>await socket.CloseAsync().ConfigureAwait(false)).ConfigureAwait(false);
        await context.RouteAsync("**/*",async route=>
        {
            try
            {
                var request=route.Request;
                var url=PublicUrl(request.Url);
                if(request.ResourceType is "image" or "media" or "font" || Interlocked.Increment(ref requests)>250 ||
                    request.Method is not ("GET" or "HEAD" or "POST") ||
                    request.Method=="POST"&&!SameOrigin(url,origin) ||
                    request.IsNavigationRequest&&!SameOrigin(url,origin))
                {await route.AbortAsync().ConfigureAwait(false);return;}
                var addresses=await dns(url.DnsSafeHost,ct).ConfigureAwait(false);
                if(addresses.Length==0||addresses.Any(ip=>!PublicAddress(ip)))throw new SourceException("非公开网络目标。");
                using var message=new HttpRequestMessage(new HttpMethod(request.Method),url);
                if(request.PostDataBuffer is {} data)
                {if(data.Length>128*1024)throw new SourceException("请求数据过大。");message.Content=new ByteArrayContent(data);}
                foreach(var header in await request.AllHeadersAsync().ConfigureAwait(false))
                {
                    if(header.Key is "host" or "content-length" or "accept-encoding" or "connection" or "authorization" or "proxy-authorization")continue;
                    if(!message.Headers.TryAddWithoutValidation(header.Key,header.Value))message.Content?.Headers.TryAddWithoutValidation(header.Key,header.Value);
                }
                using var response=await http.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);
                if(response.Content.Headers.ContentLength>8*1024*1024)throw new SourceException("响应数据过大。");
                using var input=await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);using var output=new MemoryStream();var buffer=new byte[16384];int count;
                while((count=await input.ReadAsync(buffer,ct).ConfigureAwait(false))>0)
                {if(output.Length+count>8*1024*1024||Interlocked.Add(ref totalBytes,count)>40*1024*1024)throw new SourceException("响应达到上限。");output.Write(buffer,0,count);}
                if(request.ResourceType is "fetch" or "xhr")
                {try{using var json=JsonDocument.Parse(output.ToArray());if(json.RootElement.TryGetProperty("data",out var dataNode)&&dataNode.ValueKind==JsonValueKind.Object&&dataNode.TryGetProperty("posts",out var posts))diagnostic?.Invoke("动态搜索响应条目数："+(posts.ValueKind==JsonValueKind.Array?posts.GetArrayLength():-1));}catch(JsonException){}catch(InvalidOperationException){}}
                // Do not replay transport/client-hint negotiation headers into an intercepted response.
                // Preserve document security policies and same-session cookie/redirect semantics.
                var headers=response.Headers.Concat(response.Content.Headers).Where(h=>new[]{"content-type","location","set-cookie","cache-control","content-security-policy","x-content-type-options","x-frame-options","referrer-policy","permissions-policy","access-control-allow-origin","access-control-allow-credentials","access-control-allow-headers","access-control-allow-methods"}.Contains(h.Key.ToLowerInvariant())).ToDictionary(h=>h.Key,h=>string.Join("\n",h.Value),StringComparer.OrdinalIgnoreCase);
                await route.FulfillAsync(new(){Status=(int)response.StatusCode,Headers=headers,BodyBytes=output.ToArray()}).ConfigureAwait(false);
            }
            catch(Exception ex)when(ex is not OutOfMemoryException)
            {diagnostic?.Invoke("动态请求未完成："+route.Request.ResourceType+" · "+ex.GetType().Name);try{await route.AbortAsync().ConfigureAwait(false);}catch(PlaywrightException){}}
        }).ConfigureAwait(false);
    }
    private async Task<T> WithBrowser<T>(string address,Func<IBrowserContext,CancellationToken,Task<T>> action,CancellationToken ct,Action<string>? diagnostic=null)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(100));
        var token=timeout.Token;await BrowserSlot.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await using var session=await BrowserSession.Create().ConfigureAwait(false);
            await InstallRoutes(session.Context,PublicUrl(address),token,diagnostic).ConfigureAwait(false);
            // Closing the isolated context interrupts navigation/click waits as well as HTTP reads.
            using var cancel=token.Register(()=>{_ = CloseContext(session.Context);});
            try{return await action(session.Context,token).ConfigureAwait(false);}
            catch(PlaywrightException){token.ThrowIfCancellationRequested();throw new SourceException("动态页面没有在限定时间内完成搜索或下载入口加载。可重试或填写更准确的验证词；尚未确认可用。");}
        }
        finally{BrowserSlot.Release();}
    }
    private static async Task CloseContext(IBrowserContext context){try{await context.CloseAsync().ConfigureAwait(false);}catch(PlaywrightException){}}
    private static async Task Settle(IPage page,CancellationToken ct)
    {
        var state=Track(page);var start=Environment.TickCount64;
        while(Environment.TickCount64-start<15000)
        {
            await Task.Delay(100,ct).ConfigureAwait(false);
            if(Environment.TickCount64-start>=800&&state.Pending.IsEmpty&&Environment.TickCount64-Interlocked.Read(ref state.LastChange)>=600)return;
        }
        throw new SourceException("动态请求尚未完成，未读取可能过期的结果；请稍后重试。");
    }
    private static async Task<Page> Snapshot(IPage page)
    {
        var uri=new UriBuilder(page.Url){Fragment=""}.Uri;
        return new(uri,Encoding.UTF8.GetBytes(await page.ContentAsync().ConfigureAwait(false)),"text/html");
    }
    private static async Task Navigate(IPage page,string url,CancellationToken ct)
    {
        Track(page);
        try{await page.GotoAsync(url,new(){WaitUntil=WaitUntilState.DOMContentLoaded}).ConfigureAwait(false);}
        catch(TimeoutException){throw new SourceException("动态页面加载超时（已呈现文字 "+(await page.Locator("body").InnerTextAsync(new(){Timeout=1000}).ConfigureAwait(false)).Length+" 字符）。尚未完成验证。");}
        await Settle(page,ct).ConfigureAwait(false);
    }
    private static async Task<string> FindBrowserEntry(IPage page,string address,CancellationToken ct,Action<string>? diagnostic=null)
    {
        await Navigate(page,address,ct).ConfigureAwait(false);
        if(await page.Locator(SearchInput).CountAsync().ConfigureAwait(false)>0)return new UriBuilder(page.Url){Fragment=""}.Uri.AbsoluteUri;
        // Follow only an observed same-site search link, never guessed API endpoints.
        var snapshot=await Snapshot(page).ConfigureAwait(false);
        foreach(var a in Html(snapshot.Text).DocumentNode.SelectNodes("//a[@href]")??Enumerable.Empty<HtmlAgilityPack.HtmlNode>())
        {
            if(!Uri.TryCreate(snapshot.Url,a.GetAttributeValue("href",""),out var target)||!SameOrigin(snapshot.Url,target)||!Regex.IsMatch(target.AbsolutePath,@"/(?:search|sou|s)(?:/|\.|$)",RegexOptions.IgnoreCase))continue;
            var entry=new UriBuilder(target){Fragment=""}.Uri.AbsoluteUri;PublicUrl(entry);
            await Navigate(page,entry,ct).ConfigureAwait(false);
            if(await page.Locator(SearchInput).CountAsync().ConfigureAwait(false)>0)return entry;
            break;
        }
        await Navigate(page,address,ct).ConfigureAwait(false);
        var toggle=page.Locator("a[aria-label='搜索']:visible, button[aria-label='搜索']:visible, a[title='搜索']:visible, button[title='搜索']:visible, button[aria-label='Search']:visible").First;
        if(await toggle.CountAsync().ConfigureAwait(false)>0){await toggle.ClickAsync().ConfigureAwait(false);await Settle(page,ct).ConfigureAwait(false);}
        if(await page.Locator(SearchInput).CountAsync().ConfigureAwait(false)>0)return address;
        throw new SourceException("已读取动态页面，但没有发现可操作的公开搜索框；需专门适配该站，尚未添加。");
    }
    private static async Task<Page> SubmitSearch(IPage page,string entry,string query,CancellationToken ct)
    {
        // A hash-routed search can retain the previous result list on same-document navigation.
        // Start a fresh document for every query, including the negative control.
        await page.GotoAsync("about:blank").ConfigureAwait(false);
        await FindBrowserEntry(page,entry,ct).ConfigureAwait(false);
        var input=page.Locator(SearchInput).First;
        var form=input.Locator("xpath=ancestor::form");
        if(await form.CountAsync().ConfigureAwait(false)>0)
        {
            if(await form.Locator("input[type=password]").CountAsync().ConfigureAwait(false)>0)throw new SourceException("搜索入口包含密码字段，未自动提交。");
            var action=await form.GetAttributeAsync("action").ConfigureAwait(false);
            if(!string.IsNullOrEmpty(action)&&(!Uri.TryCreate(new Uri(page.Url),action,out var target)||!SameOrigin(new Uri(entry),target)||Regex.IsMatch(target.AbsolutePath,@"/(?:delete|remove|logout|purchase|checkout)(?:/|\.|$)",RegexOptions.IgnoreCase)))throw new SourceException("搜索表单目标不是可验证的同站搜索操作。");
        }
        await input.FillAsync(query).ConfigureAwait(false);
        await input.DispatchEventAsync("change").ConfigureAwait(false);
        await Task.Delay(150,ct).ConfigureAwait(false);
        if(await form.CountAsync().ConfigureAwait(false)>0)await form.EvaluateAsync("form => form.requestSubmit()").ConfigureAwait(false);
        else await input.PressAsync("Enter").ConfigureAwait(false);
        await Settle(page,ct).ConfigureAwait(false);
        return await Snapshot(page).ConfigureAwait(false);
    }
    private Task<DiscoveryResult> DiscoverBrowserAsync(string address,string query,List<string> steps,Action<string> step,CancellationToken ct)=>WithBrowser(address,async(context,token)=>
    {
        var page=await context.NewPageAsync().ConfigureAwait(false);
        step("隔离浏览器已启动，正在加载页面并寻找动态搜索入口。");
        var entry=await FindBrowserEntry(page,address,token,step).ConfigureAwait(false);
        var name=Clean(await page.TitleAsync().ConfigureAwait(false));if(name.Length is 0 or >80)name=new Uri(address).Host;
        var automatic=string.IsNullOrWhiteSpace(query);
        string[] queries=[query];
        if(automatic)
        {
            await Navigate(page,address,token).ConfigureAwait(false);
            queries=ContentLinks(await Snapshot(page).ConfigureAwait(false)).Select(l=>ProbeWord(l.Title)).Where(w=>w.Length>=2).Distinct().Take(3).ToArray();
            if(queries.Length==0)throw new SourceException("动态搜索框已找到，但没有合适的自动验证样本。请填写该站的资源名。");
        }
        foreach(var probe in queries)
        {
        query=probe;
        if(automatic)step("动态页面自动选择验证词："+query);
        try
        {
        if(query.Length>100||query.Any(char.IsControl))throw new SourceException("请输入 100 字以内的验证词。");
        step("已找到动态搜索框，正在验证关键词和无匹配词。");
        var positive=await SubmitSearch(page,entry,query,token).ConfigureAwait(false);
        step("动态搜索页可识别的内容链接："+ContentLinks(positive).Count()+"；匹配："+ContentLinks(positive).Count(l=>Matches(l.Title,query))+"。");
        var links=ContentLinks(positive).Where(l=>Matches(l.Title,query)).Take(3).ToArray();
        var direct=ParseResults(positive.Bytes,positive.Url,name).Where(r=>Matches(r.Title,query)).ToArray();
        if(links.Length==0&&direct.Length==0)throw new SourceException("动态搜索没有返回与验证词匹配的条目。请填写该站确有的资源名，尚未添加。");
        var negative=await SubmitSearch(page,entry,"afmcprobe"+Guid.NewGuid().ToString("N")[..6],token).ConfigureAwait(false);
        var positiveLinks=ContentLinks(positive).ToArray();var negativeLinks=ContentLinks(negative).ToArray();
        if(negativeLinks.Any(l=>links.Any(p=>p.Url==l.Url)))
        {
            var positiveMatches=positiveLinks.Count(l=>Matches(l.Title,query));var negativeMatches=negativeLinks.Count(l=>Matches(l.Title,query));
            // Some frontends keep a broad default list after a no-result response. Accept only
            // clear keyword-driven narrowing, never the same list with a locally filtered title.
            var narrowed=positiveLinks.Length>0&&negativeLinks.Length>=positiveLinks.Length*2&&
                positiveMatches/(double)positiveLinks.Length>=0.5&&negativeMatches/(double)negativeLinks.Length<=positiveMatches/(double)positiveLinks.Length/2;
            if(!narrowed)throw new SourceException("无匹配词仍返回相同内容，未将推荐列表当作搜索源。");
            step("本站无结果时保留默认列表；已验证关键词显著缩小列表，搜索结果仍严格按标题匹配。");
        }
        if(ParseResults(negative.Bytes,negative.Url,name).Any(r=>direct.Any(p=>Identity(p)==Identity(r))))throw new SourceException("对照查询重复返回相同下载条目，未判定为可用。");
        step("关键词对照通过，正在检查详情页及动态下载按钮。");
        var rows=direct.ToList();
        foreach(var link in links){rows.AddRange(await BrowserDetail(page,link,name,token,step).ConfigureAwait(false));if(rows.Count>0)break;}
        if(rows.Count==0)throw new SourceException("搜索已验证，但详情页未找到公开磁力、种子或受支持的网盘链接。可能需要登录、验证码或专门适配；尚未添加。");
        var source=new SourceConfig{Id="web-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry))).ToLowerInvariant()[..20],Kind="AutoWeb",Name=name,Endpoint=address,DiscoveryTemplate=entry,DiscoveryFormat="BrowserSearch",DiscoveryQuery=query,Description="自动探测 · 动态网页搜索 · 隔离浏览器"};
        step("动态搜索和下载入口验证通过；每次搜索重新获取链接，不保存临时跳转令牌。");
        return new DiscoveryResult(source,rows.DistinctBy(Identity).ToArray(),steps,$"可添加 · {rows.Count} 条验证结果"+(rows.Any(r=>r.ShareUrl is not null)?"（含网盘，使用浏览器打开；分享有效性以网盘为准）。":"。"));
        }
        catch(SourceException ex)when(automatic){step("此样本未通过："+ex.Message);}
        }
        throw new SourceException("动态入口已发现，但自动样本均未完成下载验证。请填写该站确有的资源名再试；尚未添加。");
    },ct,step);
    private Task<IReadOnlyList<SearchResult>> BrowserSearchAsync(SourceConfig source,string query,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(query)||query.Length>200||query.Any(char.IsControl))throw new SourceException("请输入有效关键词。");
        PublicUrl(source.DiscoveryTemplate);
        if(!SameOrigin(PublicUrl(source.Endpoint),PublicUrl(source.DiscoveryTemplate)))throw new SourceException("动态来源搜索入口必须位于同一网站，请重新识别。");
        return WithBrowser<IReadOnlyList<SearchResult>>(source.Endpoint,async(context,token)=>
        {
            var page=await context.NewPageAsync().ConfigureAwait(false);
            var search=await SubmitSearch(page,source.DiscoveryTemplate,query,token).ConfigureAwait(false);
            var rows=ParseResults(search.Bytes,search.Url,source.Name).Where(r=>Matches(r.Title,query)).ToList();
            foreach(var link in ContentLinks(search).Where(l=>Matches(l.Title,query)).Take(4))rows.AddRange(await BrowserDetail(page,link,source.Name,token).ConfigureAwait(false));
            return rows.DistinctBy(Identity).ToArray();
        },ct);
    }
    private static async Task<IReadOnlyList<SearchResult>> BrowserDetail(IPage page,PageLink link,string source,CancellationToken ct,Action<string>? diagnostic=null)
    {
        await Navigate(page,link.Url,ct).ConfigureAwait(false);
        var initial=await Snapshot(page).ConfigureAwait(false);
        var heading=Clean(Html(initial.Text).DocumentNode.SelectSingleNode("//h1")?.InnerText??"");
        if(heading.Length>0&&!Matches(heading,ProbeWord(link.Title))){diagnostic?.Invoke("详情标题与搜索条目不一致，已跳过。");return [];}
        var poster=Html(initial.Text).DocumentNode.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content","");
        try{poster=string.IsNullOrWhiteSpace(poster)?null:PublicUrl(new Uri(initial.Url,poster).AbsoluteUri).AbsoluteUri;}catch(SourceException){poster=null;}
        var rows=new List<SearchResult>();
        for(int depth=0;depth<3;depth++)
        {
            var snapshot=await Snapshot(page).ConfigureAwait(false);
            rows.AddRange(ParseResults(snapshot.Bytes,snapshot.Url,source));
            if(rows.Count>0)break;
            var buttons=page.Locator("a:visible, button:visible").Filter(new(){HasTextRegex=new Regex(@"^\s*(?:[↓⇩↧]\s*)?(?:下载|立即下载|种子下载|获取下载|Download)(?:\s|\+|\d|$)",RegexOptions.IgnoreCase)});
            var count=Math.Min(await buttons.CountAsync().ConfigureAwait(false),3);
            diagnostic?.Invoke("检查下载入口第 "+(depth+1)+" 层："+count+" 个候选按钮。");
            string? follow=null;
            for(int i=0;i<count;i++)
            {
                var button=buttons.Nth(i);
                var label=await button.InnerTextAsync().ConfigureAwait(false);
                if(Regex.IsMatch(label,"APP|客户端|登录|购买|充值|付费",RegexOptions.IgnoreCase))continue;
                var href=await button.GetAttributeAsync("href").ConfigureAwait(false);
                if(href is not null&&Regex.IsMatch(href,@"/(?:delete|remove|logout|purchase|checkout|login|register)(?:/|\?|\.|$)",RegexOptions.IgnoreCase))continue;
                if(href is not null&&Uri.TryCreate(initial.Url,href,out var target)&&target.Scheme is "http" or "https"&&!SameOrigin(initial.Url,target))continue;
                await page.EvaluateAsync("window.__afmcOpened=[]").ConfigureAwait(false);
                await button.ClickAsync(new(){Timeout=4000}).ConfigureAwait(false);await Settle(page,ct).ConfigureAwait(false);
                var opened=await page.EvaluateAsync<string[]>("window.__afmcOpened||[]").ConfigureAwait(false);
                foreach(var value in opened)
                {
                    if(CloudShares.Provider(value)is not null)rows.Add(new(){Title=link.Title,Source=source,ShareUrl=value});
                    else if(Links.MagnetHash(value)is {} hash)rows.Add(new(){Title=link.Title,Source=source,Magnet=value,Hash=hash});
                    else if(Uri.TryCreate(initial.Url,value,out var next)&&SameOrigin(initial.Url,next)&&Regex.IsMatch(next.AbsolutePath,@"/(?:download|dl|torrent)(?:/|\.|$)",RegexOptions.IgnoreCase))follow=next.AbsoluteUri;
                }
                rows.AddRange(ParseResults(Encoding.UTF8.GetBytes(await page.ContentAsync().ConfigureAwait(false)),new UriBuilder(page.Url){Fragment=""}.Uri,source));
                if(rows.Count>0||follow is not null)break;
            }
            if(rows.Count>0)break;
            if(follow is not null)await Navigate(page,follow,ct).ConfigureAwait(false);
            else if(count==0||page.Url==snapshot.Url.AbsoluteUri)break;
        }
        return rows.Select(r=>r with{Title=r.ShareUrl is not null?link.Title:WithContext(r.Title,link.Title),DetailsUrl=link.Url,CoverUrl=poster,CoverSource=poster is null?null:source}).DistinctBy(Identity).ToArray();
    }
}
