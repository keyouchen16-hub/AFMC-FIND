using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Afmc.Find.Core;

namespace Afmc.Find.App;

public sealed class SettingsStore
{
    public string DirectoryPath { get; }
    public SettingsStore(string? path = null) => DirectoryPath = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AFMC", "Find");
    private string FilePath => Path.Combine(DirectoryPath, "sources.json");
    public List<SourceConfig> Load()
    {
        if (!File.Exists(FilePath)) return SiteSources.IncludeCatalog([SourceConfig.Archive()]);
        var items = JsonSerializer.Deserialize<List<SourceConfig>>(File.ReadAllText(FilePath)) ?? throw new InvalidDataException("配置无效");
        return SiteSources.IncludeCatalog(items.Select(s => s with { ApiKey = s.ApiKey.Length == 0 ? "" : Secret.Unprotect(s.ApiKey) }));
    }
    public void Save(IEnumerable<SourceConfig> sources)
    {
        Directory.CreateDirectory(DirectoryPath);
        var saved = sources.Select(s => s with { ApiKey = s.ApiKey.Length == 0 ? "" : Secret.Protect(s.ApiKey) }).ToList();
        var temp = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak"); else File.Move(temp, FilePath);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public static class Secret
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    public static string Protect(string value) => Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(value), true));
    public static string Unprotect(string value) => Encoding.UTF8.GetString(Transform(Convert.FromBase64String(value), false));
    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var ok = protect ? CryptProtectData(ref input, "AFMC FIND", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output) : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new System.Security.Cryptography.CryptographicException("无法读取当前 Windows 用户的受保护配置。");
            var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally { Marshal.FreeHGlobal(input.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data); Array.Clear(bytes); }
    }
}
