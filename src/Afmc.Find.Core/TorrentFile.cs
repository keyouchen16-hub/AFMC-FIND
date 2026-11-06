using System.Security.Cryptography;
using System.Text;

namespace Afmc.Find.Core;

public sealed record TorrentInfo(string Name, string Hash, long Size);

// Bounded, strict bencode reader. No filesystem paths from a torrent are ever used by this app.
public static class TorrentFile
{
    public const int MaxBytes = 16 * 1024 * 1024;
    public static TorrentInfo Validate(byte[] data)
    {
        if (data.Length == 0 || data.Length > MaxBytes) throw new SourceException("种子文件为空或超过 16 MB。");
        var reader = new Reader(data);
        if (reader.Read(0) is not Dictionary<string, object> root || reader.Position != data.Length || !root.TryGetValue("info", out var infoObject) || infoObject is not Dictionary<string, object> info)
            throw new SourceException("返回内容不是有效的种子文件。");
        if (!info.TryGetValue("name", out var nameBytes) || nameBytes is not byte[] nameData) throw new SourceException("种子缺少名称。");
        var name = Text(nameData); CheckSegment(name);
        if (!info.TryGetValue("piece length", out var pieceLength) || pieceLength is not long p || p <= 0) throw new SourceException("种子分块信息无效。");
        if (!info.TryGetValue("pieces", out var pieces) || pieces is not byte[] hashes || hashes.Length % 20 != 0) throw new SourceException("暂不支持纯 v2 种子文件；可使用来源提供的 v2 磁力链接。");
        long size = 0;
        if (info.TryGetValue("length", out var length) && length is long single && single >= 0) size = single;
        else if (info.TryGetValue("files", out var filesValue) && filesValue is List<object> files && files.Count > 0)
        {
            foreach (var file in files)
            {
                if (file is not Dictionary<string, object> f || !f.TryGetValue("length", out var l) || l is not long n || n < 0 || !f.TryGetValue("path", out var pathValue) || pathValue is not List<object> path || path.Count == 0) throw new SourceException("种子文件列表无效。");
                foreach (var part in path) { if (part is not byte[] b) throw new SourceException("种子路径无效。"); CheckSegment(Text(b)); }
                if (long.MaxValue - size < n) throw new SourceException("种子长度溢出。"); size += n;
            }
        }
        else throw new SourceException("种子缺少有效文件长度。");
        if (size / p + (size % p == 0 ? 0 : 1) != hashes.Length / 20) throw new SourceException("种子长度与分块数量不匹配。");
        return new(name, Convert.ToHexString(SHA1.HashData(data.AsSpan(reader.InfoStart, reader.InfoEnd - reader.InfoStart))).ToLowerInvariant(), size);
    }
    private static string Text(byte[] data) { try { return new UTF8Encoding(false, true).GetString(data); } catch (DecoderFallbackException) { throw new SourceException("种子名称编码无效。"); } }
    private static void CheckSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." || value.IndexOfAny(['/', '\\', ':', '\0']) >= 0 || value.Any(char.IsControl)) throw new SourceException("种子包含不安全的文件路径。");
    }
    private sealed class Reader(byte[] bytes)
    {
        public int Position { get; private set; }
        public int InfoStart { get; private set; }
        public int InfoEnd { get; private set; }
        private int nodes;
        public object Read(int depth)
        {
            if (++nodes > 200000 || depth > 48 || Position >= bytes.Length) throw Invalid();
            byte c = bytes[Position];
            if (c is >= (byte)'0' and <= (byte)'9')
            {
                int start = Position; long length = 0;
                while (Position < bytes.Length && bytes[Position] != ':')
                {
                    var digit = bytes[Position++] - '0'; if (digit < 0 || digit > 9 || length > MaxBytes) throw Invalid(); length = length * 10 + digit;
                }
                if (Position >= bytes.Length || (Position - start > 1 && bytes[start] == '0') || length > bytes.Length - Position - 1) throw Invalid();
                Position++; var value = bytes.AsSpan(Position, (int)length).ToArray(); Position += (int)length; return value;
            }
            Position++;
            if (c == 'i')
            {
                int start = Position; while (Position < bytes.Length && bytes[Position] != 'e') Position++;
                if (Position >= bytes.Length) throw Invalid();
                var value = Encoding.ASCII.GetString(bytes, start, Position++ - start);
                if (!long.TryParse(value, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var n) || value != n.ToString(System.Globalization.CultureInfo.InvariantCulture)) throw Invalid(); return n;
            }
            if (c == 'l')
            {
                var list = new List<object>(); while (Position < bytes.Length && bytes[Position] != 'e') list.Add(Read(depth + 1)); End(); return list;
            }
            if (c == 'd')
            {
                var map = new Dictionary<string, object>();
                while (Position < bytes.Length && bytes[Position] != 'e')
                {
                    if (Read(depth + 1) is not byte[] keyBytes) throw Invalid();
                    var key = Encoding.Latin1.GetString(keyBytes); var start = Position; var value = Read(depth + 1);
                    if (!map.TryAdd(key, value)) throw Invalid();
                    if (depth == 0 && key == "info") { InfoStart = start; InfoEnd = Position; }
                }
                End(); return map;
            }
            throw Invalid();
        }
        private void End() { if (Position >= bytes.Length || bytes[Position++] != 'e') throw Invalid(); }
        private static SourceException Invalid() => new("返回内容不是有效的种子文件（结构异常）。");
    }
}
