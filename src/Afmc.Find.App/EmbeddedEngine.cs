using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Afmc.Find.Core;

namespace Afmc.Find.App;

public sealed record EngineSource(string Id,string Name,string Site,string Type,string Language,bool Configured)
{
    public string Description{get;init;}="";
    public string[] Categories{get;init;}=[];
    public int AccessOrder=>Type=="public"?0:Type=="semi-private"||Type=="semi-public"?1:2;
    public string AccessLabel=>Type=="public"?"公开":Type is "semi-private" or "semi-public"?"部分功能需账号":"需账号";
    public string CategoryLabel=>Categories.Length==0?"未标明":Categories.Length>4?"综合资源 · "+string.Join(" · ",Categories.Take(3)):string.Join(" · ",Categories);
    public string Label => AccessLabel+" · "+Language+(Configured?" · 已配置":"");
    public static string[] Classify(JsonArray? caps)
    {
        var categories=new HashSet<string>();
        foreach(var cap in caps?.OfType<JsonObject>()??Enumerable.Empty<JsonObject>())
        {
            var value=cap["ID"]?.ToString()??cap["id"]?.ToString()??"";
            if(!int.TryParse(value,out var id))continue;
            if(id==5070){categories.Add("动漫");continue;}
            switch(id/1000){case 1:categories.Add("游戏");break;case 2:categories.Add("电影");break;case 3:categories.Add("音乐");break;case 4:categories.Add(id is 4050 or 4070?"游戏":"软件");break;case 5:categories.Add("剧集/视频");break;case 7:categories.Add("书籍");break;case 8:categories.Add("其他");break;}
        }
        return categories.OrderBy(x=>x,StringComparer.Ordinal).ToArray();
    }
    public static IEnumerable<EngineSource> Filter(IEnumerable<EngineSource> sources,string query,int access,string category)
    {
        if(Uri.TryCreate(query,UriKind.Absolute,out var uri)&&uri.Scheme is "https" or "http")query=uri.Host;
        return sources.Where(x=>(access==0||access==1&&x.Type=="public"||access==2&&x.Type!="public")&&(category=="全部资源"||x.Categories.Contains(category)||category=="未标明"&&x.Categories.Length==0)&&(query.Length==0||x.Name.Contains(query,StringComparison.OrdinalIgnoreCase)||x.Site.Contains(query,StringComparison.OrdinalIgnoreCase)||x.Id.Contains(query,StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x=>x.AccessOrder).ThenBy(x=>x.Name,StringComparer.OrdinalIgnoreCase);
    }
    public SourceConfig ToConfig()=>new(){Id="engine-"+Id,Name=Name,Kind="Torznab",Endpoint=Site,EngineIndexerId=Id,Description="内置引擎 · "+Type};
}

public sealed class EmbeddedEngine : IDisposable
{
    private readonly string folder,executable;
    private readonly SemaphoreSlim gate=new(1);
    private readonly CancellationTokenSource lifetime=new();
    private HttpClient? client;
    private Process? child;
    private EngineJob? job;
    private FileStream? ownership;
    private string apiKey="",password="";
    private Uri? origin;
    private bool disposed;
    public int? ProcessId=>child is {HasExited:false}?child.Id:null;
    public string DataDirectory=>folder;
    public EmbeddedEngine(string profile,string? engineDirectory=null)
    {
        folder=Path.Combine(profile,"SearchEngine");
        executable=Path.Combine(engineDirectory??Path.Combine(AppContext.BaseDirectory,"components","Jackett"),"JackettConsole.exe");
    }
    public static string ValidId(string id)=>Regex.IsMatch(id,"^[a-zA-Z0-9_-]{1,100}$")?id:throw new SourceException("来源标识无效。");
    private static void PrivateDirectory(string path)
    {
        var directory=Directory.CreateDirectory(path);
        var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);
        var inheritance=InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit;
        acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,FileSystemRights.FullControl,inheritance,PropagationFlags.None,AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),FileSystemRights.FullControl,inheritance,PropagationFlags.None,AccessControlType.Allow));
        directory.SetAccessControl(acl);
    }
    public async Task EnsureStartedAsync(CancellationToken ct)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,lifetime.Token);
        await gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            if(child is {HasExited:false}&&client is not null)return;
            StopChild();
            if(!File.Exists(executable))throw new SourceException("安装包缺少内置搜索组件，请重新安装完整版本。");
            // Upstream automatically migrates this directory even with --DataFolder.
            if(Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Jackett")))
                throw new SourceException("检测到外部旧版 Jackett 配置，为保护原数据暂未启动内置引擎；请先处理旧配置迁移。");
            PrivateDirectory(folder);
            try{ownership=new FileStream(Path.Combine(folder,"owner.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException){throw new SourceException("另一个 AFMC FIND 正在使用搜索引擎，请先退出重复窗口。");}
            var secretFile=Path.Combine(folder,"engine.secret");
            if(File.Exists(secretFile))apiKey=Secret.Unprotect(File.ReadAllText(secretFile));
            else{apiKey=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();File.WriteAllText(secretFile,Secret.Protect(apiKey));}
            password=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var configPath=Path.Combine(folder,"ServerConfig.json");
            var config=File.Exists(configPath)?JsonNode.Parse(File.ReadAllText(configPath))!.AsObject():new JsonObject();
            int port=config["Port"]?.GetValue<int>()??0;
            TcpListener listener;
            try{listener=new TcpListener(IPAddress.Loopback,port);listener.Start();}
            catch(SocketException){listener=new TcpListener(IPAddress.Loopback,0);listener.Start();}
            port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
            config["Port"]=port;config["LocalBindAddress"]="127.0.0.1";config["AllowExternal"]=false;config["AllowCORS"]=false;
            config["UpdateDisabled"]=true;config["APIKey"]=apiKey;config["BasePathOverride"]="";config["BaseUrlOverride"]="";
            var probe=new Uri("https://www.example.com/");
            var proxy=HttpClient.DefaultProxy.GetProxy(probe);
            if(proxy is not null&&proxy!=probe&&proxy.UserInfo.Length==0&&proxy.Scheme is "http" or "socks5")
            {config["ProxyType"]=proxy.Scheme=="http"?0:2;config["ProxyUrl"]=proxy.Host;config["ProxyPort"]=proxy.Port;}
            else{config["ProxyType"]=-1;config["ProxyUrl"]="";}
            config["ProxyUsername"]="";config["ProxyPassword"]="";
            config["AdminPassword"]=Convert.ToHexString(SHA512.HashData(Encoding.Unicode.GetBytes(password+apiKey))).ToLowerInvariant();
            var temp=configPath+".new";File.WriteAllText(temp,config.ToJsonString());File.Move(temp,configPath,true);
            origin=new Uri($"http://127.0.0.1:{port}/");
            client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false,CookieContainer=new CookieContainer()}){BaseAddress=origin,Timeout=TimeSpan.FromSeconds(90)};
            var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(executable)!,RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add("--DataFolder");start.ArgumentList.Add(folder);start.ArgumentList.Add("--NoUpdates");start.ArgumentList.Add("--NoRestart");
            job=new EngineJob();child=Process.Start(start)??throw new SourceException("内置引擎启动失败。");
            try{job.Assign(child);}catch{child.Kill(true);throw;}
            child.OutputDataReceived+=(_,_)=>{};child.ErrorDataReceived+=(_,_)=>{};child.BeginOutputReadLine();child.BeginErrorReadLine();
            using var startup=CancellationTokenSource.CreateLinkedTokenSource(linked.Token);startup.CancelAfter(TimeSpan.FromSeconds(35));
            while(true)
            {
                startup.Token.ThrowIfCancellationRequested();
                if(child.HasExited)throw new SourceException("内置引擎未能启动，请检查组件与配置目录。");
                try{using var response=await client.GetAsync("health",startup.Token).ConfigureAwait(false);if(response.IsSuccessStatusCode)break;}
                catch(HttpRequestException){}
                await Task.Delay(200,startup.Token).ConfigureAwait(false);
            }
            using var login=await client.PostAsync("UI/Dashboard",new FormUrlEncodedContent(new Dictionary<string,string>{{"password",password}}),startup.Token).ConfigureAwait(false);
            if(login.StatusCode!=HttpStatusCode.Redirect)throw new SourceException("内置引擎认证失败。");
            // Verify the session rather than treating a redirect as proof of login.
            using var verify=await client.GetAsync("api/v2.0/indexers?configured=true",startup.Token).ConfigureAwait(false);
            if(!verify.IsSuccessStatusCode)throw new SourceException("内置引擎会话验证失败。");
        }
        catch{StopChild();throw;}
        finally{gate.Release();}
    }
    private async Task<JsonNode?> RequestAsync(HttpMethod method,string path,JsonNode? body,CancellationToken ct)
    {
        await EnsureStartedAsync(ct).ConfigureAwait(false);
        using var request=new HttpRequestMessage(method,path);
        if(body is not null)request.Content=new StringContent(body.ToJsonString(),Encoding.UTF8,"application/json");
        using var response=await client!.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);
        if(!response.IsSuccessStatusCode)throw new SourceException(response.StatusCode switch{HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden=>"来源认证失败，请检查该站点的凭据。",HttpStatusCode.TooManyRequests=>"来源限流，请稍后重试。",_=>"来源配置或测试失败，请核对网址、必填项及网络；该来源尚未接通。"});
        if(response.StatusCode==HttpStatusCode.NoContent)return null;
        using var stream=await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);using var memory=new MemoryStream();var buffer=new byte[16384];int count;
        while((count=await stream.ReadAsync(buffer,ct).ConfigureAwait(false))>0){if(memory.Length+count>8*1024*1024)throw new SourceException("引擎响应过大。");memory.Write(buffer,0,count);}
        return memory.Length==0?null:JsonNode.Parse(memory.ToArray());
    }
    public async Task<IReadOnlyList<EngineSource>> CatalogAsync(CancellationToken ct)
    {
        var data=await RequestAsync(HttpMethod.Get,"api/v2.0/indexers?configured=false",null,ct).ConfigureAwait(false);
        if(data is not JsonArray array)throw new SourceException("引擎来源目录格式无效。");
        return array.OfType<JsonObject>().Select(x=>new EngineSource(ValidId(x["id"]!.GetValue<string>()),x["name"]!.GetValue<string>(),x["site_link"]?.GetValue<string>()??"",x["type"]?.GetValue<string>()??"",x["language"]?.GetValue<string>()??"",x["configured"]?.GetValue<bool>()??false){Description=x["description"]?.GetValue<string>()??"",Categories=EngineSource.Classify(x["caps"] as JsonArray)}).OrderBy(x=>x.AccessOrder).ThenBy(x=>x.Name,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public async Task<JsonArray> ConfigurationAsync(string id,CancellationToken ct)=>(await RequestAsync(HttpMethod.Get,"api/v2.0/indexers/"+ValidId(id)+"/config",null,ct).ConfigureAwait(false)) as JsonArray??throw new SourceException("来源配置格式无效。");
    public async Task ConfigureAsync(string id,JsonArray values,CancellationToken ct)=>await RequestAsync(HttpMethod.Post,"api/v2.0/indexers/"+ValidId(id)+"/config",values,ct).ConfigureAwait(false);
    public async Task TestAsync(string id,CancellationToken ct)=>await RequestAsync(HttpMethod.Post,"api/v2.0/indexers/"+ValidId(id)+"/test",null,ct).ConfigureAwait(false);
    public async Task<SourceConfig> ResolveAsync(SourceConfig source,CancellationToken ct)
    {
        if(source.EngineIndexerId.Length==0)return source;
        var id=ValidId(source.EngineIndexerId);await EnsureStartedAsync(ct).ConfigureAwait(false);
        return source with{Endpoint=new Uri(origin!,"api/v2.0/indexers/"+id+"/results/torznab/api").AbsoluteUri,ApiKey=apiKey};
    }
    private void StopChild()
    {
        if(child is {HasExited:false}&&client is not null)
        {
            try
            {
                using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var response=client.PostAsync("api/v2.0/server/shutdown",null,stop.Token).GetAwaiter().GetResult();
                if(response.IsSuccessStatusCode)child.WaitForExit(2000);
            }
            catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or ObjectDisposedException){}
        }
        job?.Dispose();job=null;
        if(child is not null){try{if(!child.HasExited){child.Kill(true);child.WaitForExit(3000);}}catch(InvalidOperationException){}child.Dispose();child=null;}
        client?.Dispose();client=null;ownership?.Dispose();ownership=null;
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;lifetime.Cancel();
        gate.Wait();try{StopChild();}finally{gate.Release();}
    }
}
