using System.Globalization;
using System.Text;
using Burger.Core.Save;
using Burger.Core.Script;

namespace Burger.Core.Multiplayer;

/// <summary>
/// Reads a save that came from another machine (the host's resync). The host is treated as hostile:
/// the text is parsed with hard limits (depth, tokens, entries, string length), then checked against the schema before
/// any game state is touched: only known top-level blocks, keys and values made of plain printable characters, finite numbers,
/// tag-shaped country keys. There is no type information in a save and nothing is deserialized by name; every value is read by the
/// loader into a field of a type it already knows, and unknown ids resolve to nothing.
/// </summary>
public static class SaveGuard
{
    /// <summary>Top-level blocks written by <see cref="SaveGame.Save"/>. Sections of systems and mods are added as <c>x_name</c> from the game itself.</summary>
    private static readonly HashSet<string> BaseBlocks = new(StringComparer.Ordinal)
    {
        "meta", "countries", "states", "locations", "characters", "armies", "divisions", "wars", "generic_scopes", "scope_hierarchy",
        "relations", "modifiers", "scheduled_events",
    };

    public const int MaxKeyLength = 128;

    /// <summary>Warn level: a save above it is still loaded, but the player is told (set from measured late-game saves, see docs/multiplayer-security.md).</summary>
    public static long WarnNodes = 1_000_000;
    public static long WarnBytes = 64L << 20;

    /// <summary>Parses and validates. Throws <see cref="InvalidDataException"/> with the reason when the save is refused.</summary>
    public static Node Read(Game game, byte[] utf8) => Read(game, utf8, out _);

    /// <summary>As <see cref="Read(Game, byte[])"/>; <paramref name="warning"/> is set when the save is accepted but above the warn level.</summary>
    public static Node Read(Game game, byte[] utf8, out string? warning)
    {
        warning = null;
        if (utf8.LongLength > Wire.MaxSaveBytes) throw new InvalidDataException("save too large");
        string text;
        try { text = new UTF8Encoding(false, true).GetString(utf8); } // invalid UTF-8 is an error, not a replacement character
        catch (DecoderFallbackException) { throw new InvalidDataException("save is not valid UTF-8"); }
        var root = ReadText(game, text);
        long nodes = Count(root);
        if (nodes > WarnNodes || utf8.LongLength > WarnBytes)
            warning = $"the host's save is very large ({utf8.LongLength / (1 << 20)} MB, {nodes} entries; refused above {Wire.MaxSaveBytes >> 20} MB or {ScriptParser.ParseLimits.Save.MaxNodes} entries)";
        return root;
    }

    private static long Count(Node n) { long c = 1; if (n.Children != null) foreach (var k in n.Children) c += Count(k); return c; }

    public static Node ReadText(Game game, string text)
    {
        Node root;
        try { root = ScriptParser.ParseStrict(text, "save", ScriptParser.ParseLimits.Save); }
        catch (InvalidDataException) { throw; }
        catch (Exception e) { throw new InvalidDataException("unreadable save: " + e.GetType().Name); }
        Validate(game, root);
        return root;
    }

    public static void Validate(Game game, Node root)
    {
        var allowed = new HashSet<string>(BaseBlocks, StringComparer.Ordinal);
        foreach (var sec in game.SaveSections.All) allowed.Add("x_" + sec.Name);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var top in root.All)
        {
            if (!top.IsBlock) throw new InvalidDataException($"unexpected value '{Short(top.Key)}' at the top level");
            if (!allowed.Contains(top.Key)) throw new InvalidDataException($"unknown block '{Short(top.Key)}'");
            if (!seen.Add(top.Key)) throw new InvalidDataException($"block '{top.Key}' twice");
        }
        if (!seen.Contains("meta")) throw new InvalidDataException("no meta block");
        var meta = root.Get("meta")!;
        if (meta.GetString("version") != "1") throw new InvalidDataException("unknown save version");
        if (meta.GetString("game") is { } g && !string.Equals(g, game.Data.Info.Name, StringComparison.Ordinal)) throw new InvalidDataException("save of another game");
        // country keys name countries: tag-shaped only
        foreach (var n in root.Get("countries")?.All ?? Enumerable.Empty<Node>())
            if (!n.IsBlock || Sanitize.Tag(n.Key) != n.Key) throw new InvalidDataException("bad country key");
        foreach (var top in root.All) Walk(top, 1);
    }

    private static void Walk(Node n, int depth)
    {
        if (n.Key.Length > MaxKeyLength) throw new InvalidDataException("key too long");
        CheckText(n.Key);
        if (n.Value != null)
        {
            CheckText(n.Value);
            // numbers must be finite: NaN or infinity would poison every later sum
            if (Node.TryParseDouble(n.Value, out double d) && !double.IsFinite(d)) throw new InvalidDataException("non-finite number");
            string low = n.Value;
            if (low.Length is >= 3 and <= 9 && (low.Equals("nan", StringComparison.OrdinalIgnoreCase) || low.Contains("infinity", StringComparison.OrdinalIgnoreCase) || low.Equals("-inf", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("non-finite number");
        }
        if (n.Children != null) foreach (var c in n.Children) Walk(c, depth + 1);
    }

    private static void CheckText(string s)
    {
        foreach (char c in s)
            if (char.IsControl(c) && c is not ('\n' or '\t')) throw new InvalidDataException("control character");
    }

    private static string Short(string s) => Sanitize.Text(s.Length > 40 ? s[..40] : s, 40);
}
