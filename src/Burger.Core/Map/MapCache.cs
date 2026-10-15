using Burger.Core.Data;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Burger.Core.Map;

/// <summary>
/// Disk cache for the slow part of loading a map: decoding the big images and turning them into per-pixel arrays.
/// An entry is stored under <c>game/cache/map/</c> with a key that is a SHA-256 over the content of every file and setting the entry
/// was made from, so a changed map can never read an old entry; a missing, unreadable or mismatching entry is simply built again.
/// Set the environment variable <c>BURGER_NO_MAP_CACHE</c> to turn it off.
/// </summary>
public static class MapCache
{
    public static readonly bool Enabled = Environment.GetEnvironmentVariable("BURGER_NO_MAP_CACHE") == null;
    /// <summary>Bump when the way an entry is made changes (the loader's own code is not part of the key).</summary>
    private const string Format = "burger-map-cache-1";
    private const uint Magic = 0x31434D42; // "BMC1"

    public static string Dir(GameData data) => Path.Combine(data.Root, "cache", "map");

    /// <summary>The key for an entry: file parts ("file:PATH") count by content, every other part as text.</summary>
    public static string Key(params string[] parts)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(ReadOnlySpan<byte> b) => sha.AppendData(b);
        Add(Encoding.UTF8.GetBytes(Format));
        foreach (var p in parts)
        {
            if (p.StartsWith("file:", StringComparison.Ordinal))
            {
                string path = p[5..];
                Add(Encoding.UTF8.GetBytes(File.Exists(path) ? "F" : "M"));
                if (File.Exists(path))
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
                    {
                        var buf = new byte[1 << 20];
                        int n;
                        while ((n = fs.Read(buf, 0, buf.Length)) > 0) sha.AppendData(buf.AsSpan(0, n));
                    }
            }
            else Add(Encoding.UTF8.GetBytes(p));
            Add(new byte[] { 0 });
        }
        return Convert.ToHexString(sha.GetHashAndReset());
    }

    public static T[]? Load<T>(string dir, string name, string key, out int[] meta) where T : unmanaged
    {
        meta = Array.Empty<int>();
        if (!Enabled) return null;
        try
        {
            string path = Path.Combine(dir, name + ".bin");
            if (!File.Exists(path)) return null;
            var file = File.ReadAllBytes(path);
            int pos = 0;
            if (BitConverter.ToUInt32(file, pos) != Magic) return null; pos += 4;
            if (Encoding.ASCII.GetString(file, pos, 64) != key) return null; pos += 64;
            int metaCount = BitConverter.ToInt32(file, pos); pos += 4;
            if (metaCount < 0 || metaCount > 1_000_000) return null;
            meta = new int[metaCount];
            Buffer.BlockCopy(file, pos, meta, 0, metaCount * 4); pos += metaCount * 4;
            int size = BitConverter.ToInt32(file, pos); pos += 4;
            long count = BitConverter.ToInt64(file, pos); pos += 8;
            byte mode = file[pos++];
            if (size != Marshal.SizeOf<T>() || count < 0 || count > int.MaxValue) return null;
            var result = new T[count];
            if (mode == 0)
            {
                if (file.Length - pos != count * size) return null;
                file.AsSpan(pos).CopyTo(MemoryMarshal.AsBytes(result.AsSpan()));
                return result;
            }
            var eq = EqualityComparer<T>.Default;
            int at = 0;
            Span<T> one = stackalloc T[1];
            while (at < count)
            {
                long run = 0; int shift = 0;
                while (true) { byte b = file[pos++]; run |= (long)(b & 0x7F) << shift; if ((b & 0x80) == 0) break; shift += 7; }
                if (run <= 0 || at + run > count) return null;
                file.AsSpan(pos, size).CopyTo(MemoryMarshal.AsBytes(one)); pos += size;
                result.AsSpan(at, (int)run).Fill(one[0]);
                at += (int)run;
            }
            return pos == file.Length ? result : null;
        }
        catch (Exception ex)
        {
            Log.Warn("Map cache entry '" + name + "' ignored: " + ex.Message);
            return null;
        }
    }

    public static void Save<T>(string dir, string name, string key, int[] meta, T[] data) where T : unmanaged
    {
        if (!Enabled) return;
        try
        {
            Directory.CreateDirectory(dir);
            int size = Marshal.SizeOf<T>();
            // run-length encode; flat maps shrink a lot, noisy ones are stored raw
            var rle = new MemoryStream();
            var eq = EqualityComparer<T>.Default;
            long limit = (long)data.Length * size;
            bool useRle = true;
            Span<T> one = stackalloc T[1];
            for (int i = 0; i < data.Length;)
            {
                T v = data[i];
                int j = i + 1;
                while (j < data.Length && eq.Equals(data[j], v)) j++;
                long run = j - i;
                while (run >= 0x80) { rle.WriteByte((byte)(run | 0x80)); run >>= 7; }
                rle.WriteByte((byte)run);
                one[0] = v;
                rle.Write(MemoryMarshal.AsBytes(one));
                if (rle.Length >= limit) { useRle = false; break; }
                i = j;
            }
            string tmp = Path.Combine(dir, name + ".tmp"), final = Path.Combine(dir, name + ".bin");
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(Magic);
                bw.Write(Encoding.ASCII.GetBytes(key));
                bw.Write(meta.Length);
                foreach (var m in meta) bw.Write(m);
                bw.Write(size);
                bw.Write((long)data.Length);
                bw.Write((byte)(useRle ? 1 : 0));
                if (useRle) rle.WriteTo(fs); else bw.Write(MemoryMarshal.AsBytes(data.AsSpan()));
            }
            File.Move(tmp, final, true);
        }
        catch (Exception ex)
        {
            Log.Warn("Map cache entry '" + name + "' could not be saved: " + ex.Message);
        }
    }
}
