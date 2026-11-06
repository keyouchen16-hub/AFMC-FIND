using System.IO;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Net.Http;
using Afmc.Find.Core;
internal static class MediaChecks
{
 static int passed;
 static void Check(bool v,string n){if(!v)throw new Exception(n);passed++;Console.WriteLine("PASS "+n);}
 sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> f):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>Task.FromResult(f(r));}
 public static async Task Run(bool live)
 {
  var folder="docs/evidence/v074/";var dm=File.ReadAllBytes(folder+"dmhy.xml");var tv=File.ReadAllBytes(folder+"tvmaze.json");var ez=File.ReadAllBytes(folder+"eztv.json");
  var rows=MediaSources.Dmhy(dm);Check(rows.Count>0&&rows.All(r=>Links.MagnetHash(r.Magnet)==r.Hash),"DMHY real captured hashes normalized");
  Check(rows.All(r=>r.Size is null&&r.Seeders is null),"DMHY placeholder enclosure size not resource size");
  Check(rows.All(r=>r.DetailsUrl?.StartsWith("https://share.dmhy.org/")==true),"DMHY details use same-site HTTPS");
  var hostile=Encoding.UTF8.GetString(dm).Replace("http://share.dmhy.org/topics/view/","https://evil.invalid/topics/view/");Check(MediaSources.Dmhy(Encoding.UTF8.GetBytes(hostile)).All(r=>r.DetailsUrl is null),"DMHY foreign details discarded");
  Check(MediaSources.Dmhy(Encoding.UTF8.GetBytes("<rss><channel><title>動漫花園</title></channel></rss>")).Count==0,"DMHY valid empty channel");
  try{MediaSources.Dmhy(Encoding.UTF8.GetBytes("<html/>"));throw new Exception("invalid feed");}catch(SourceException){Check(true,"DMHY challenge is failure");}
  var ids=MediaSources.ShowIds(tv,"Breaking Bad S01E01 1080p");Check(ids.Contains("0903747"),"show identifier resolved after season/resolution removal");
  Check(MediaSources.ShowIds(tv,"Breaking Brad").Count==0,"similar show title not silently accepted");
  Check(MediaSources.NameQuery("Breaking 1080p Bad")=="breaking bad","quality token removal normalizes internal whitespace");
  var results=MediaSources.Eztv(ez,"0903747","Breaking Bad",out var raw);
  Check(raw>0&&results.All(r=>!r.Title.Contains("Breaking Brad")),"EZTV wrongly indexed title rejected");
  Check(raw==77&&results.Count==0,"captured Breaking Bad index contains no qualifying results; negative regression");
  var sherlock=File.ReadAllBytes(folder+"eztv-sherlock.json");
  var sherlockRows=MediaSources.Eztv(sherlock,"1475582","Sherlock",out _);
  Check(sherlockRows.Count==14&&sherlockRows.All(r=>Links.MagnetHash(r.Magnet)==r.Hash),"EZTV captured Sherlock valid matches");
  Check(MediaSources.Eztv(sherlock,"1475582","Sherlock S04E03 720p",out _).Count>0,"EZTV exact season episode and resolution retained");
  try{MediaSources.Eztv(Encoding.UTF8.GetBytes("[]"),"1475582","Sherlock",out _);throw new Exception("invalid JSON root");}catch(SourceException){Check(true,"EZTV invalid JSON shape fails");}
  Check(MediaSources.Eztv(ez,"9999999","Breaking Bad",out _).Count==0,"EZTV wrong IMDb rejected");
  Check(MediaSources.Eztv(ez,"0903747","Breaking Bad S99E99",out _).Count==0,"EZTV explicit episode constraint retained");
  var source=SiteSources.Catalog.Single(s=>s.Kind=="Dmhy");var calls=0;var reports=new List<SourceReport>();
  using(var service=new SearchService(new Handler(r=>{calls++;Check(r.RequestUri!.Query.Contains("keyword="),"DMHY query parameter sent");return new(HttpStatusCode.OK){Content=new ByteArrayContent(dm)};})))
  {await service.SearchAsync("naruto",[source],reports.Add,CancellationToken.None);await service.SearchAsync("naruto",[source],_=>{},CancellationToken.None);Check(calls==1,"DMHY single RSS request and repeat cache");}
  var eztvSource=SiteSources.Catalog.Single(s=>s.Kind=="Eztv");
  var mapping=Encoding.UTF8.GetBytes("[{\"show\":{\"name\":\"Sherlock\",\"externals\":{\"imdb\":\"tt1475582\"}}}]");
  using var pageDoc=JsonDocument.Parse(sherlock);
  var fullPage=Encoding.UTF8.GetBytes("{\"torrents\":["+string.Join(",",Enumerable.Repeat(pageDoc.RootElement.GetProperty("torrents")[0].GetRawText(),100))+"]}");
  var pageRequests=0;reports.Clear();
  using(var service=new SearchService(new Handler(r=>{if(r.RequestUri!.Host=="api.tvmaze.com")return new(HttpStatusCode.OK){Content=new ByteArrayContent(mapping)};pageRequests++;return new(HttpStatusCode.OK){Content=new ByteArrayContent(r.RequestUri.Query.EndsWith("page=1")?fullPage:sherlock)};})))
  {await service.SearchAsync("Sherlock",[eztvSource],reports.Add,CancellationToken.None);Check(pageRequests==2&&reports.Single().Results.Count==14,"EZTV pagination follows raw count and deduplicates hashes");}
  reports.Clear();
  using(var service=new SearchService(new Handler(r=>r.RequestUri!.Host=="api.tvmaze.com"?new(HttpStatusCode.OK){Content=new ByteArrayContent(mapping)}:r.RequestUri.Query.EndsWith("page=1")?new(HttpStatusCode.OK){Content=new ByteArrayContent(fullPage)}:new(HttpStatusCode.ServiceUnavailable))))
  {await service.SearchAsync("Sherlock",[eztvSource],reports.Add,CancellationToken.None);Check(reports.Single().Error is not null&&reports.Single().Results.Count==1,"EZTV later HTTP failure preserves prior results and reports error");}
  var evidence=new List<object>();
  if(live)foreach(var pair in new[]{("Dmhy","naruto"),("Dmhy","海贼王"),("Eztv","Sherlock")})
  {using var service=new SearchService();reports.Clear();await service.SearchAsync(pair.Item2,SiteSources.Catalog.Where(s=>s.Kind==pair.Item1),reports.Add,CancellationToken.None);var report=reports.Single();evidence.Add(new{Kind=pair.Item1,Query=pair.Item2,Count=report.Results.Count,report.Error,report.ElapsedMilliseconds,ValidMagnets=report.Results.All(r=>Links.MagnetHash(r.Magnet)is not null)});File.WriteAllText(folder+"media-live.json",JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));Check(report.Error is null&&report.Results.Count>0,pair.Item1+" live "+pair.Item2);}
  if(live)File.WriteAllText(folder+"media-live.json",JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"MEDIA PASSED {passed}");
 }
}
