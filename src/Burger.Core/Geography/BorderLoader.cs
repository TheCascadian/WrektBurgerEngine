using System.Text;
using Burger.Core.Script;

namespace Burger.Core.Geography;

/// <summary>Reads <c>map/areas.txt</c>, <c>history/borders/*.txt</c> and the source ledger. Every problem becomes an error with file and line.</summary>
public static class BorderLoader
{
    private static readonly HashSet<string> BlockKeys = new(StringComparer.OrdinalIgnoreCase) { "add", "remove", "control", "source", "note" };

    /// <summary>Loads a project's game folder. Later files override earlier ones (areas by name); claims keep file order.</summary>
    public static BorderSet LoadFolder(string gameRoot, string? ledgerPath = null)
    {
        var set = new BorderSet();
        LoadLedger(set, ledgerPath);
        foreach (var f in Files(Path.Combine(gameRoot, "map"), "areas*.txt", SearchOption.TopDirectoryOnly))
            LoadAreas(set, ScriptParser.ParseFile(f, Path.GetRelativePath(gameRoot, f)));
        foreach (var f in Files(Path.Combine(gameRoot, "history", "borders"), "*.txt", SearchOption.AllDirectories))
            LoadClaims(set, ScriptParser.ParseFile(f, Path.GetRelativePath(gameRoot, f)), Path.GetFileNameWithoutExtension(f));
        return set;
    }

    private static IEnumerable<string> Files(string dir, string pattern, SearchOption opt)
        => Directory.Exists(dir) ? Directory.GetFiles(dir, pattern, opt).OrderBy(f => f, StringComparer.OrdinalIgnoreCase) : Enumerable.Empty<string>();

    /// <summary>Each top-level <c>name = { atom atom ... }</c> is an area. A later definition of the same name replaces the earlier one.</summary>
    public static void LoadAreas(BorderSet set, Node root)
    {
        foreach (var n in root.All)
        {
            if (n.Children == null) { set.Error($"expected an area block, found '{n.Key}'", n.Location); continue; }
            var atoms = n.Items();
            if (atoms.Count == 0) { set.Error($"area '{n.Key}' has no atoms", n.Location); continue; }
            if (!set.Areas.ContainsKey(n.Key)) set.AreaOrder.Add(n.Key);
            set.Areas[n.Key] = atoms.Distinct(StringComparer.Ordinal).OrderBy(a => a, StringComparer.Ordinal).ToArray();
            set.AreaWhere[n.Key] = n.Location;
        }
    }

    /// <summary>Parses one polity file. The top-level block must be named after the file (<c>ATH.txt</c> holds <c>ATH = { ... }</c>).</summary>
    public static void LoadClaims(BorderSet set, Node root, string tag)
    {
        foreach (var top in root.All)
        {
            if (top.Children == null) { set.Error($"expected a polity block, found '{top.Key}'", top.Location); continue; }
            if (!string.Equals(top.Key, tag, StringComparison.OrdinalIgnoreCase))
            { set.Error($"block '{top.Key}' does not match file name '{tag}'", top.Location); continue; }
            foreach (var d in top.All)
            {
                if (!HistoricalCalendar.TryParse(d.Key, out int day, out var err)) { set.Error(err ?? $"bad date '{d.Key}'", d.Location); continue; }
                if (d.Children == null) { set.Error($"date '{d.Key}' needs a block", d.Location); continue; }
                var c = new BorderClaim { Tag = top.Key.ToUpperInvariant(), Day = day, Where = d.Location };
                bool ok = true;
                foreach (var k in d.All)
                {
                    string key = k.Key.ToLowerInvariant();
                    if (!BlockKeys.Contains(key)) { set.Error($"unknown key '{k.Key}' (expected add, remove, control, source, note)", k.Location); ok = false; continue; }
                    switch (key)
                    {
                        case "add":
                        case "remove":
                            foreach (var a in k.Items())
                            {
                                if (!set.Areas.ContainsKey(a)) { set.Error($"unknown area '{a}'", k.Location); ok = false; }
                                else (key == "add" ? c.Add : c.Remove).Add(a);
                            }
                            break;
                        case "control":
                            c.Control = (k.Value ?? "").ToLowerInvariant();
                            if (!BorderControl.IsValid(c.Control)) { set.Error($"unknown control '{k.Value}' (expected {string.Join(", ", BorderControl.Levels)})", k.Location); ok = false; }
                            break;
                        case "source": c.Source = k.Value ?? ""; break;
                        case "note": c.Note = k.Value ?? ""; break;
                    }
                }
                if (c.Source.Length == 0) { set.Error("claim needs a source (a ledger id, or 'generated')", d.Location); ok = false; }
                else if (CheckSource(set, c.Source) is { } why) { set.Error(why, d.Location); ok = false; }
                if (c.Add.Count == 0 && c.Remove.Count == 0) { set.Error("claim has neither add nor remove", d.Location); ok = false; }
                if (c.Add.Count > 0 && c.Control.Length == 0) { set.Error("claim with add needs a control level", d.Location); ok = false; }
                if (ok) set.Claims.Add(c);
            }
        }
    }

    private static string? CheckSource(BorderSet set, string source)
    {
        if (string.Equals(source, BorderSet.Generated, StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var d in set.Denied)
            if (source.Contains(d, StringComparison.OrdinalIgnoreCase)) return $"source '{source}' is on the project denylist";
        return set.ApprovedSources.Contains(source) ? null : $"source '{source}' is not APPROVED in the source ledger";
    }

    /// <summary>Reads ids whose status is exactly APPROVED, plus the denylist beside the ledger. With no ledger only <c>generated</c> is accepted.</summary>
    public static void LoadLedger(BorderSet set, string? path)
    {
        if (path == null || !File.Exists(path)) return;
        var rows = Csv(File.ReadAllText(path));
        if (rows.Count == 0) return;
        int idCol = rows[0].IndexOf("id"), stCol = rows[0].IndexOf("status");
        if (idCol < 0 || stCol < 0) { set.Error("ledger needs id and status columns", path); return; }
        foreach (var r in rows.Skip(1))
            if (r.Count > Math.Max(idCol, stCol) && r[stCol].Trim() == "APPROVED") set.ApprovedSources.Add(r[idCol].Trim());
        var deny = Path.Combine(Path.GetDirectoryName(path)!, "denylist.txt");
        if (File.Exists(deny))
            set.Denied.AddRange(File.ReadAllLines(deny).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')));
    }

    /// <summary>Minimal RFC 4180 reader: quoted fields may hold commas, quotes (doubled) and newlines.</summary>
    public static List<List<string>> Csv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                if (ch == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; } else quoted = false; }
                else sb.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { row.Add(sb.ToString()); sb.Clear(); }
            else if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(sb.ToString()); sb.Clear();
                if (row.Count > 1 || row[0].Length > 0) rows.Add(row);
                row = new List<string>();
            }
            else sb.Append(ch);
        }
        if (sb.Length > 0 || row.Count > 0) { row.Add(sb.ToString()); rows.Add(row); }
        return rows;
    }
}
