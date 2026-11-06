using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Afmc.Find.App;

internal static class RoutingCacheChecks
{
    public static void Run()
    {
        var type = typeof(TorrentPlayback).Assembly.GetType("Afmc.Find.App.LibtorrentSession")!;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var saved = type.GetField("routingSaved", flags)!;
        var remember = type.GetMethod("RememberRouting", flags)!;
        var snapshot = type.GetMethod("RoutingSnapshot", flags)!;
        string[] Get() => (string[])snapshot.Invoke(null, null)!;
        void Put(object nodes)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { routing = nodes }));
            remember.Invoke(null, [doc.RootElement]);
        }
        void Check(bool value) { if (!value) throw new Exception("Routing cache check failed"); }
        var valid = Enumerable.Range(1, 12).Select(i => $"08080808{i:X4}").ToArray();
        Put(valid); Check(Get().Length == 12);
        Get()[0] = "mutated"; Check(Get()[0] == valid[0]);
        Put(Enumerable.Repeat(valid[0], 257).ToArray()); Check(Get().Length == 12);
        Put(new[] { "invalid", new string('a', 4096) }); Check(Get().Length == 12);
        Put(new[] { valid[0] }); Check(Get().Length == 12);
        saved.SetValue(null, Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * 1801d));
        Check(Get().Length == 0);
        Parallel.For(0, 100, _ => { Put(valid); Check(Get().Length == 12); });
        Check(Get().Length == 12);
        var peerPut = type.GetMethod("RememberPeers", flags)!;
        var peerGet = type.GetMethod("PeerNodes", flags)!;
        var peerStore = (System.Collections.IDictionary)type.GetField("resourcePeers", flags)!.GetValue(null)!;
        string[] GetPeers(string? key) => (string[])peerGet.Invoke(null, [key])!;
        void PutPeers(string? key, string[] values)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { peersReusable = values }));
            peerPut.Invoke(null, [key, doc.RootElement]);
        }
        PutPeers("first-magnet", valid);
        Check(GetPeers("first-magnet").Length == 12 && GetPeers("other-magnet").Length == 0);
        PutPeers(null, valid); Check(GetPeers(null).Length == 0);
        PutPeers("too-many", Enumerable.Repeat(valid[0], 65).ToArray()); Check(GetPeers("too-many").Length == 0);
        var entry = peerStore["first-magnet"]!;
        entry.GetType().GetProperty("Saved")!.SetValue(entry, Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * 301d));
        Check(GetPeers("first-magnet").Length == 0);
        for (var i = 0; i < 20; i++) PutPeers($"resource{i}", valid);
        Check(peerStore.Count == 16 && GetPeers("resource0").Length == 0 && GetPeers("resource19").Length == 12);
        Console.WriteLine("ROUTING CACHE PASSED: expiry, defensive copy, invalid/oversized rejection, minimum nodes, concurrent reads/writes");
        Console.WriteLine("PEER CACHE PASSED: resource isolation, no file inputs, 5-minute expiry, 64 endpoints, 16 resources");
    }
}
