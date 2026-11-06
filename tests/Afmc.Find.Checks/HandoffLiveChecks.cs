using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Afmc.Find.App;
using Afmc.Find.Core;

internal static class HandoffLiveChecks
{
    public static async Task Run()
    {
        var root=Path.GetFullPath("docs/evidence/v071/handoff-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(root);
        var exe=Path.GetFullPath(".tools/qbt-validation/client/qbittorrent.exe");
        if(!File.Exists(exe))throw new InvalidOperationException("Verified isolated qBittorrent binary missing.");
        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(45)};
        var listing=await http.GetStringAsync("https://releases.ubuntu.com/24.04/");
        var filename=Regex.Match(listing,"href=\"(ubuntu-[^\"]*desktop[^\"]*\\.iso\\.torrent)\"").Groups[1].Value;
        if(filename.Length==0)throw new InvalidOperationException("Official Ubuntu torrent link not found.");
        var url="https://releases.ubuntu.com/24.04/"+filename;
        var bytes=await http.GetByteArrayAsync(url);var info=TorrentFile.Validate(bytes);
        File.WriteAllBytes(Path.Combine(root,"official-ubuntu.torrent"),bytes);
        using var service=new SearchService();var launcher=new DownloadLauncher(service,root);
        var row=new SearchResult{Title=info.Name,Source="Ubuntu official",Hash=info.Hash,TorrentUrl=url};
        var prepared=await launcher.PrepareAsync(row,CancellationToken.None);
        var magnet=await launcher.PrepareAsync(row with{Magnet="magnet:?xt=urn:btih:"+info.Hash+"&dn="+Uri.EscapeDataString(info.Name)},CancellationToken.None);
        var evidence=new List<object>();
        foreach(var entry in new[]{(Kind:"torrent",Value:prepared),(Kind:"magnet",Value:magnet)})
        {
            var profile=Path.GetFullPath(Path.Combine(".tools/qbt-validation/profiles",Path.GetFileName(root),entry.Kind));var config=Path.Combine(profile,"qBittorrent","config");Directory.CreateDirectory(config);
            var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();var port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
            var password=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
            var salt=System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
            var key=System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(password,salt,100000,System.Security.Cryptography.HashAlgorithmName.SHA512,64);
            var passwordHash=Convert.ToBase64String(salt)+":"+Convert.ToBase64String(key);
            File.WriteAllText(Path.Combine(config,"qBittorrent.ini"),$"[LegalNotice]\nAccepted=true\n[Preferences]\nGeneral\\CloseToTray=false\nGeneral\\MinimizeToTray=false\nGeneral\\CheckForUpdates=false\nConnection\\UPnP=false\n[WebUI]\nEnabled=true\nAddress=127.0.0.1\nPort={port}\nUsername=afmc-test\nPassword_PBKDF2=@ByteArray({passwordHash})\nLocalHostAuth=true\n[Network]\nPortForwardingEnabled=false\n[BitTorrent]\nSession\\DHTEnabled=false\nSession\\PeXEnabled=false\nSession\\LSDEnabled=false\nSession\\PortForwardingEnabled=false\nSession\\InterfaceAddress=127.0.0.1\n");
            Process? client=null;
            try
            {
                DownloadLauncher.OpenPrepared(entry.Value,root,start=>
                {
                    if(start.FileName!=entry.Value)throw new InvalidOperationException("Dispatch target changed.");
                    var launch=new ProcessStartInfo(exe){UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden};
                    foreach(var arg in new[]{"--profile="+profile,"--confirm-legal-notice","--no-splash","--add-stopped=true","--skip-dialog=true","--save-path="+Path.Combine(profile,"payload"),start.FileName})launch.ArgumentList.Add(arg);
                    client=Process.Start(launch)??throw new InvalidOperationException("Client failed to start.");
                });
                if(!client!.WaitForInputIdle(15000))throw new InvalidOperationException("Client not ready.");
                await Task.Delay(4000);client.Refresh();
                if(client.HasExited)throw new InvalidOperationException("Client exited before acceptance verification.");
                using var api=new HttpClient{BaseAddress=new Uri($"http://127.0.0.1:{port}/"),Timeout=TimeSpan.FromSeconds(10)};
                api.DefaultRequestHeaders.Referrer=api.BaseAddress;
                using var login=await api.PostAsync("api/v2/auth/login",new FormUrlEncodedContent(new Dictionary<string,string>{{"username","afmc-test"},{"password",password}}));
                // 5.3 returns HTTP status rather than the older literal "Ok." body.
                login.EnsureSuccessStatusCode();
                using var taskJson=JsonDocument.Parse(await api.GetStringAsync("api/v2/torrents/info?hashes="+info.Hash));
                var task=taskJson.RootElement.EnumerateArray().Single();var state=task.GetProperty("state").GetString();
                if(task.GetProperty("hash").GetString()!=info.Hash||state is not ("stoppedDL" or "stoppedUP" or "pausedDL" or "pausedUP"))throw new InvalidOperationException("Client receipt hash/state mismatch.");
                using var shutdown=await api.PostAsync("api/v2/app/shutdown",new StringContent(""));shutdown.EnsureSuccessStatusCode();
                if(!client.WaitForExit(15000))throw new InvalidOperationException("Isolated client did not close normally.");
                var resume=Directory.GetFiles(profile,info.Hash+".fastresume",SearchOption.AllDirectories).Single();
                var text=Encoding.Latin1.GetString(File.ReadAllBytes(resume));
                var stopped=text.Contains("6:pausedi1e",StringComparison.Ordinal)||text.Contains("7:stoppedi1e",StringComparison.Ordinal);
                if(!stopped)throw new InvalidOperationException("Client task was not persisted as stopped.");
                var payload=Path.Combine(profile,"payload");
                var downloadedBytes=Directory.Exists(payload)?Directory.GetFiles(payload,"*",SearchOption.AllDirectories).Sum(p=>new FileInfo(p).Length):0;
                if(downloadedBytes!=0)throw new InvalidOperationException("Unexpected payload bytes.");
                evidence.Add(new{entry.Kind,InfoHash=info.Hash,Client=FileVersionInfo.GetVersionInfo(exe).ProductVersion,ResumePath=resume,Stopped=stopped,PayloadBytes=downloadedBytes,
                    Boundary="Real qBittorrent receives the application-prepared target via an explicit isolated test dispatcher. Windows default association is unchanged; this is not a Quark/cloud acceptance test."});
                Console.WriteLine($"PASS real client received {entry.Kind}; hash={info.Hash}; stopped; payload=0");
            }
            finally {if(client is not null&&!client.HasExited){client.Kill();client.WaitForExit(5000);}client?.Dispose();}
        }
        File.WriteAllText("docs/evidence/v071/handoff-live.json",JsonSerializer.Serialize(new{OfficialTorrent=url,ClientExecutable=exe,DefaultAssociationModified=false,Checks=evidence},new JsonSerializerOptions{WriteIndented=true}));
    }
}
