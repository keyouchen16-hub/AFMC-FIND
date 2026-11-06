using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Afmc.Find.Core;

internal static class PirateBayChecks
{
    static int passed;
    static void Check(bool valid, string name) { if (!valid) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(f(request)); }
    static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
    public static async Task Run(bool live)
    {
        const string folder = "docs/evidence/v075/";
        var fixture = File.ReadAllBytes(folder + "apibay-ubuntu.json");
        var rows = PirateBaySource.Parse(fixture);
        Check(rows.Count > 0 && rows.All(r => Links.MagnetHash(r.Magnet) == r.Hash), "APIBay captured hashes produce valid magnets");
        Check(rows.All(r => r.DetailsUrl!.StartsWith("https://thepiratebay.org/description.php?id=")), "APIBay details point to official website");
        var ubuntu = rows.Single(r => r.Hash == Links.NormalizeHash("2C6B6858D61DA9543D4231A71DB4B1C9264B0685"));
        Check(ubuntu.Size == 3654957056 && ubuntu.Seeders == 34 && ubuntu.Published?.ToUnixTimeSeconds() == 1652877231, "APIBay metadata fields retain captured values");
        Check(rows.Any(r => r.Category == "软件") && rows.Any(r => r.Category == "书籍"), "APIBay software and ebook category mapping");
        Check(rows.All(r => !r.Title.Contains("&amp;")), "APIBay title entities decoded");
        Check(PirateBaySource.Parse(File.ReadAllBytes(folder + "apibay-empty.json")).Count == 0, "APIBay exact empty sentinel is not a result");
        Check(PirateBaySource.Parse(Bytes("[]")).Count == 0, "APIBay empty array accepted");
        foreach (var invalid in new[] { "{}", "[{}]", "[1]", "[{\"id\":1,\"name\":\"bad\",\"info_hash\":\"0000000000000000000000000000000000000000\"}]" })
        { try { PirateBaySource.Parse(Bytes(invalid)); throw new Exception("invalid response accepted"); } catch (SourceException) { Check(true, "APIBay malformed or invalid-hash response rejected"); } }
        using var doc = JsonDocument.Parse(fixture);
        var first = doc.RootElement[0].GetRawText();
        var unknown = first.Replace("\"seeders\":\"34\"", "\"seeders\":\"-1\"").Replace("\"size\":\"3654957056\"", "\"size\":null").Replace("\"added\":\"1652877231\"", "\"added\":\"999999999999999\"");
        var missing = PirateBaySource.Parse(Bytes("[" + unknown + "]")).Single();
        Check(missing.Seeders is null && missing.Size is null && missing.Published is null, "APIBay invalid numbers remain unknown");
        var mirror = rows[0] with { Source = "TPB Proxy", Observations = [] };
        var merged = ResultMerger.Merge([rows[0], mirror]);
        Check(merged.Count == 1 && merged[0].Source.Contains("海盗湾") && merged[0].Source.Contains("TPB Proxy"), "APIBay and TPB mirror same hash deduplicates with provenance");
        var source = SiteSources.Catalog.Single(s => s.Kind == "PirateBay");
        var saved = SiteSources.IncludeCatalog([source with { Enabled = false }]);
        Check(saved.Count(s => s.Id == source.Id) == 1 && !saved.Single(s => s.Id == source.Id).Enabled, "catalog preserves disabled direct-source choice");
        var requests = new List<Uri>(); var reports = new List<SourceReport>();
        using (var service = new SearchService(new Handler(r => { requests.Add(r.RequestUri!); return new(HttpStatusCode.OK) { Content = new ByteArrayContent(fixture) }; })))
        {
            await service.SearchAsync("Ubuntu & 中文", [source], reports.Add, CancellationToken.None);
            await service.SearchAsync("Ubuntu & 中文", [source], _ => { }, CancellationToken.None);
            Check(requests.Count == 1 && requests[0].AbsoluteUri.Contains("%26") && requests[0].Query.EndsWith("&cat=0"), "APIBay encoded single batch and repeat cache");
            Check(reports.Single().Error is null && reports.Single().Results.Count == rows.Count, "APIBay dispatch returns parsed results");
        }
        reports.Clear();
        using (var service = new SearchService(new Handler(_ => new(HttpStatusCode.Forbidden))))
        { await service.SearchAsync("ubuntu", [source], reports.Add, CancellationToken.None); Check(reports.Single().FailureCode == "ACCESS" && reports.Single().Results.Count == 0, "APIBay failure remains visible"); }
        if (live)
        {
            var evidence = new List<object>();
            using var service = new SearchService();
            foreach (var query in new[] { "ubuntu", "Sherlock", "afmcnoresult827364xyz" })
            {
                reports.Clear(); await service.SearchAsync(query, [source], reports.Add, CancellationToken.None);
                var report = reports.Single();
                evidence.Add(new { Query = query, Count = report.Results.Count, report.Error, report.ElapsedMilliseconds, ValidMagnets = report.Results.All(r => Links.MagnetHash(r.Magnet) == r.Hash) });
                File.WriteAllText(folder + "piratebay-live.json", JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
                Check(report.Error is null && (query.StartsWith("afmcnoresult") ? report.Results.Count == 0 : report.Results.Count > 0), "APIBay live " + query);
            }
        }
        Console.WriteLine($"PIRATEBAY PASSED {passed}");
    }
}
