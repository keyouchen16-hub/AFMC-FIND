using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Afmc.Find.App;

// Optional native decoder. Never installed globally or included in every application update.
public static class PlayerRuntime
{
    public const string Version = "3.0.24";
    public const string PackageHash = "3DA3F37EE2B2B10E5C788D830A3B513658C36521EA1B0F966ED86EB69F41EF3C";
    public const string PackageUrl = "https://api.nuget.org/v3-flatcontainer/videolan.libvlc.windows/3.0.24/videolan.libvlc.windows.3.0.24.nupkg";
    private static readonly SemaphoreSlim Gate = new(1);
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AFMC", "Find-player", "libvlc-" + Version + "-x64");

    public static async Task<string> EnsureAsync(IProgress<string>? progress, CancellationToken ct, string? packageFile = null, string? destination = null)
    {
        await Gate.WaitAsync(ct);
        var target = Path.GetFullPath(destination ?? DefaultDirectory);
        var stage = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (await ValidAsync(target, ct)) return target;
            Directory.CreateDirectory(stage);
            var package = Path.Combine(stage, "runtime.nupkg");
            if (packageFile is not null) File.Copy(packageFile, package);
            else
            {
                progress?.Report("首次准备播放器组件 · 约 138 MB，仅需下载一次，可随时停止");
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                using var response = await http.GetAsync(PackageUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = File.Create(package);
                var buffer = new byte[131072]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer, ct)) > 0)
                {
                    total += count;
                    if (total > 160_000_000) throw new InvalidDataException("播放器组件包超出预期大小。");
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    progress?.Report($"首次下载播放组件 · {total / 1048576d:0.0} / 138.5 MB");
                }
            }
            progress?.Report("校验并准备播放组件…");
            await using (var file = File.OpenRead(package))
                if (Convert.ToHexString(await SHA256.HashDataAsync(file, ct)) != PackageHash)
                    throw new InvalidDataException("播放组件校验失败，已拒绝加载；请重试下载。");
            var files = new Dictionary<string, string>();
            using (var zip = ZipFile.OpenRead(package))
            {
                long expanded = 0;
                foreach (var entry in zip.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!entry.FullName.StartsWith("build/x64/", StringComparison.Ordinal) || entry.Name.Length == 0) continue;
                    var relative = entry.FullName[10..];
                    // Headers and the remote-control Lua interface are not required by this player.
                    if (relative.StartsWith("include/") || relative.StartsWith("lua/")) continue;
                    expanded += entry.Length;
                    if (expanded > 600_000_000) throw new InvalidDataException("播放组件解压大小异常。");
                    var path = SafeChild(stage, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    entry.ExtractToFile(path);
                    await using var extracted = File.OpenRead(path);
                    files[relative] = Convert.ToHexString(await SHA256.HashDataAsync(extracted, ct));
                }
            }
            if (!files.ContainsKey("libvlc.dll") || !files.ContainsKey("libvlccore.dll")) throw new InvalidDataException("播放组件不完整。");
            File.Delete(package);
            await File.WriteAllTextAsync(Path.Combine(stage, "manifest.json"), JsonSerializer.Serialize(files), ct);
            ct.ThrowIfCancellationRequested();
            // Only this exact version's owned directory is replaced; user data is elsewhere.
            if (Directory.Exists(target)) Directory.Move(target, target + ".invalid-" + Guid.NewGuid().ToString("N"));
            Directory.Move(stage, target);
            return target;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); Gate.Release(); }
    }

    public static string SafeChild(string parent, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':')) throw new InvalidDataException("不安全的文件路径。");
        var root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("不安全的文件路径。");
        return path;
    }

    private static async Task<bool> ValidAsync(string path, CancellationToken ct)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(Path.Combine(path, "manifest.json"), ct));
            if (manifest is null || !manifest.ContainsKey("libvlc.dll") || !manifest.ContainsKey("libvlccore.dll")) return false;
            foreach (var (relative, hash) in manifest)
            {
                await using var input = File.OpenRead(SafeChild(path, relative));
                if (Convert.ToHexString(await SHA256.HashDataAsync(input, ct)) != hash) return false;
            }
            return true;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
}
