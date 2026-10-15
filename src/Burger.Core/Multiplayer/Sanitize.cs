using System.Globalization;
using System.Text;

namespace Burger.Core.Multiplayer;

/// <summary>
/// Cleans every string that came from another machine before the game stores, shows or uses it. Names and chat are shown to other
/// players, tags and ids become lookups, paths touch the disk. Nothing here trusts its input.
/// </summary>
public static class Sanitize
{
    public const int MaxName = 24;
    public const int MaxChat = 200;
    public const int MaxToast = 200;

    private static bool Bad(char c) =>
        char.IsControl(c) || c is '\u200B' or '\u200C' or '\u200D' or '\u200E' or '\u200F' or '\u2028' or '\u2029' or '\uFEFF'
        || (c >= '\u202A' && c <= '\u202E') || (c >= '\u2066' && c <= '\u2069'); // zero width and bidi overrides can fake text

    /// <summary>Printable text without control, zero-width or bidi characters, whitespace collapsed, at most <paramref name="max"/> characters.</summary>
    public static string Text(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(Math.Min(s.Length, max));
        bool space = false;
        foreach (char c in s)
        {
            if (Bad(c)) continue;
            if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
            if (space) { if (sb.Length >= max) break; sb.Append(' '); space = false; }
            if (sb.Length >= max) break;
            sb.Append(c);
        }
        string r = sb.ToString().Trim();
        // never leave half a surrogate pair at the cut
        if (r.Length > 0 && char.IsHighSurrogate(r[^1])) r = r[..^1];
        return r;
    }

    public static string PlayerName(string? s)
    {
        string n = Text(s, MaxName);
        return n.Length == 0 ? "Player" : n;
    }

    public static string Chat(string? s) => Text(s, MaxChat);

    /// <summary>A country tag or an id: letters, digits, underscore, 1..32 characters. Anything else gives "".</summary>
    public static string Tag(string? s)
    {
        if (string.IsNullOrEmpty(s) || s.Length > 32) return "";
        foreach (char c in s) if (!(c is >= 'a' and <= 'z' || c is >= 'A' and <= 'Z' || c is >= '0' and <= '9' || c == '_')) return "";
        return s;
    }

    /// <summary>A project or mod name: like a tag but also '.', '-' and a space; 1..64 characters, no leading dot.</summary>
    public static string Name(string? s)
    {
        if (string.IsNullOrEmpty(s) || s.Length > 64 || s[0] == '.') return "";
        foreach (char c in s) if (!(c is >= 'a' and <= 'z' || c is >= 'A' and <= 'Z' || c is >= '0' and <= '9' || c is '_' or '-' or '.' or ' ')) return "";
        return s;
    }

    public static bool Int(string? s, long min, long max, out long value)
    {
        value = 0;
        if (string.IsNullOrEmpty(s) || s.Length > 20) return false;
        return long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
    }

    /// <summary>
    /// Resolves a relative path below <paramref name="root"/>. Refuses absolute paths, drive letters and streams (':'), backslashes,
    /// '..', empty or dotted segments, control characters, over-long paths and anything that leaves the root through a link.
    /// Returns null when the path is not acceptable.
    /// </summary>
    public static string? RelativePath(string root, string? rel)
    {
        if (string.IsNullOrEmpty(rel) || rel.Length > 200) return null;
        if (rel[0] == '/' || rel.Contains('\\') || rel.Contains(':') || rel.Any(char.IsControl)) return null;
        foreach (var seg in rel.Split('/'))
            if (seg.Length == 0 || seg.StartsWith('.') || seg.EndsWith('.') || seg.EndsWith(' ') || seg.StartsWith('~')) return null;
        string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string full = Path.GetFullPath(Path.Combine(rootFull, rel));
        if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return null;
        // links inside the root must not lead out of it
        try
        {
            string cur = rootFull;
            foreach (var seg in rel.Split('/'))
            {
                cur = Path.Combine(cur, seg);
                var info = new FileInfo(cur);
                if (info.Exists || Directory.Exists(cur))
                {
                    var target = info.ResolveLinkTarget(true);
                    if (target != null) return null;
                }
            }
        }
        catch { return null; }
        return full;
    }
}
