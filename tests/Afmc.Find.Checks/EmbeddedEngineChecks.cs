using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Afmc.Find.App;
using Afmc.Find.Core;

internal static class EmbeddedEngineChecks
{
    public static async Task Run()
    {
        var folder=Path.GetFullPath(".tools/engine-test-"+Guid.NewGuid().ToString("N"));
        var runtime=Path.GetFullPath(".tools/jackett/runtime/Jackett");
        int passed=0;int? pid=null;int count=0;SourceConfig? saved=null;
        void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
        try
        {
            using(var engine=new EmbeddedEngine(folder,runtime))
            {
                using var cancel=new CancellationTokenSource(TimeSpan.FromMinutes(3));
                var catalog=await engine.CatalogAsync(cancel.Token);pid=engine.ProcessId;count=catalog.Count;
                Check(count>100 && pid is not null,"real bundled engine returns source catalog");
                Console.WriteLine("CATALOG "+count);
                var source=catalog.Single(x=>x.Id=="internetarchive");
                var fields=await engine.ConfigurationAsync(source.Id,cancel.Token);
                Check(fields.Count>0,"real configuration schema");
                var values=new JsonArray(fields.OfType<JsonObject>().Where(f=>f["type"]?.GetValue<string>() is not ("displayinfo" or "displayimage")).Select(f=>(JsonNode)new JsonObject{["id"]=f["id"]!.DeepClone(),["value"]=f["value"] is JsonValue v&&v.TryGetValue<string>(out var str)?str:f["value"]?.ToJsonString(),["values"]=f["values"]?.DeepClone()}).ToArray());
                await engine.ConfigureAsync(source.Id,values,cancel.Token);
                Check((await engine.CatalogAsync(cancel.Token)).Single(s=>s.Id==source.Id).Configured,"real source configuration persisted");
                await engine.TestAsync(source.Id,cancel.Token);Check(true,"real source test passes");
                saved=source.ToConfig();
                var store=new SettingsStore(folder);store.Save([saved]);
                var persisted=JsonSerializer.Deserialize<SourceConfig[]>(File.ReadAllText(Path.Combine(folder,"sources.json")))!.Single();
                Check(persisted.ApiKey==""&&persisted.Endpoint==source.Site&&persisted.EngineIndexerId==source.Id,"managed source stores no engine key or loopback endpoint");
                using var search=new SearchService{SourceResolver=engine.ResolveAsync};
                var reports=new List<SourceReport>();await search.SearchAsync("ubuntu",[saved],r=>reports.Add(r),cancel.Token,1);
                Check(reports.Count==1&&reports[0].Error is null&&reports[0].Results.Count>0,"real managed engine search returns downloadable results");
                Console.WriteLine("RESULTS "+reports[0].Results.Count);
                var resolved=await engine.ResolveAsync(saved,cancel.Token);
                using var anonymous=new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler{UseProxy=false,AllowAutoRedirect=false});
                var root=new Uri(new Uri(resolved.Endpoint).GetLeftPart(UriPartial.Authority));
                using var unauthorized=await anonymous.GetAsync(new Uri(root,"/api/v2.0/indexers"));
                Check(!unauthorized.IsSuccessStatusCode,"management API rejects anonymous access");
                using var conflict=new EmbeddedEngine(folder,runtime);
                try{await conflict.EnsureStartedAsync(cancel.Token);throw new Exception("duplicate engine accepted");}catch(SourceException){Check(true,"second engine cannot own same profile");}
            }
            Check(pid is {} id&&!Alive(id),"disposing owner stops engine child");
            using(var restarted=new EmbeddedEngine(folder,runtime))
            {
                var catalog=await restarted.CatalogAsync(CancellationToken.None);
                Check(catalog.Single(s=>s.Id==saved!.EngineIndexerId).Configured,"source survives engine restart");
                using var search=new SearchService{SourceResolver=restarted.ResolveAsync};var reports=new List<SourceReport>();
                await search.SearchAsync("ubuntu",[saved!],r=>reports.Add(r),CancellationToken.None,1);
                Check(reports.Count==1&&reports[0].Error is null&&reports[0].Results.Count>0,"search works after restart without manual address or key");
            }
            Directory.CreateDirectory("docs/evidence/v0810");File.WriteAllText("docs/evidence/v0810/engine-live.json",JsonSerializer.Serialize(new{Passed=passed,Catalog=count,Engine="0.24.3009",Source="internetarchive",Query="ubuntu",Restart=true,ChildStopped=true}));
            Console.WriteLine("ENGINE LIVE PASSED "+passed);
        }
        catch{Console.Error.WriteLine("ENGINE TEST FAILED; isolated profile retained, no personal profile modified.");throw;}
    }
    private static bool Alive(int id){try{using var p=Process.GetProcessById(id);return !p.HasExited;}catch(ArgumentException){return false;}}
}
