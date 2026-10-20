using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Burger.Core;
using Burger.Core.Model;

namespace Burger.Tools;

public static partial class Program
{
    /// <summary>
    /// <c>burger split-states &lt;project&gt; [--max 12] [--chunk 6]</c>: cuts every state with more than --max land locations into connected chunks of about --chunk locations
    /// (map/states and history/pops are rewritten, the old files are kept as .orig). The first chunk keeps the key; the others get the next free number of the same series, the
    /// name plus a number, the owner and cores, and a share of the tax, manpower and people in proportion to their locations. Buildings go to the chunk with the most coast.
    /// </summary>
    private static int SplitStates(List<string> args)
    {
        string project = ResolveProject(RequireProjectArg(args, "burger split-states <project> [--max 12] [--chunk 6]"));
        int max = 12, chunk = 6;
        string? planPath = null;
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == "--max") max = int.Parse(args[i + 1]);
            if (args[i] == "--chunk") chunk = int.Parse(args[i + 1]);
            if (args[i] == "--plan") planPath = args[i + 1];
        }
        // a plan: state key -> groups of location keys that must stay together (the locations whose owner is the same at every start date); each group is cut into its connected pieces
        var plan = new Dictionary<string, List<List<string>>>();
        if (planPath != null)
            foreach (var line in File.ReadLines(planPath))
            {
                var parts = line.Split('	');
                if (parts.Length < 2) continue;
                plan[parts[0]] = parts.Skip(1).Select(g => g.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList()).ToList();
            }
        Log.EchoToConsole = false;
        var game = Game.Load(GameDir(project), compileScripts: false, seed: 1);
        string statesPath = Path.Combine(GameDir(project), "map", "states", "00_states.txt");
        string popsPath = Path.Combine(GameDir(project), "history", "pops", "00_pops.txt");
        var statesText = File.ReadAllText(statesPath).Replace("\r\n", "\n");
        var popsText = File.ReadAllText(popsPath).Replace("\r\n", "\n");

        var blockRx = new Regex(@"(?<head>^|\n)(?<key>[a-z0-9_]+) = \{\n(?<body>.*?)\n\}(?=\n|$)", RegexOptions.Singleline);
        var used = new HashSet<string>(blockRx.Matches(statesText).Select(m => m.Groups["key"].Value));
        var nextNumber = new Dictionary<string, int>();
        foreach (var k in used)
        {
            var m = Regex.Match(k, @"^(.*)_(\d+)$");
            if (!m.Success) continue;
            string prefix = m.Groups[1].Value; int n = int.Parse(m.Groups[2].Value);
            nextNumber[prefix] = Math.Max(nextNumber.GetValueOrDefault(prefix), n);
        }
        var splitOf = new Dictionary<string, List<(string Key, double Share)>>();
        int stateCount = 0, newCount = 0;
        var sb = new StringBuilder();
        int pos = 0;
        foreach (Match m in blockRx.Matches(statesText))
        {
            sb.Append(statesText, pos, m.Index - pos);
            pos = m.Index + m.Length;
            string key = m.Groups["key"].Value, body = m.Groups["body"].Value;
            stateCount++;
            var locMatch = Regex.Match(body, @"locations = \{([^}]*)\}");
            var keys = locMatch.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var locs = keys.Select(k => game.World.GetLocation(k)).Where(l => l != null && l.IsLand).Cast<Location>().ToList();
            List<List<Location>> chunks;
            if (planPath != null)
            {
                if (!plan.TryGetValue(key, out var groups) || groups.Count < 2) { sb.Append(m.Value); continue; }
                var byKey = locs.ToDictionary(l => l.Key);
                chunks = new List<List<Location>>();
                foreach (var g in groups)
                    chunks.AddRange(Pieces(g.Where(byKey.ContainsKey).Select(k => byKey[k]).ToList()));
                var placed = new HashSet<Location>(chunks.SelectMany(c => c));
                var left = locs.Where(l => !placed.Contains(l)).ToList();
                if (left.Count > 0) chunks.AddRange(Pieces(left));
                if (chunks.Count < 2) { sb.Append(m.Value); continue; }
            }
            else
            {
                if (locs.Count <= max) { sb.Append(m.Value); continue; }
                chunks = Cut(locs, Math.Max(2, (int)Math.Round(locs.Count / (double)chunk)));
            }
            string prefix2 = Regex.Replace(key, @"_\d+$", "");
            string name = Regex.Match(body, @"name = ""([^""]*)""").Groups[1].Value;
            double manpower = double.Parse(Regex.Match(body, @"manpower = ([\d.]+)").Groups[1].Value, CultureInfo.InvariantCulture);
            double tax = double.Parse(Regex.Match(body, @"tax = ([\d.]+)").Groups[1].Value, CultureInfo.InvariantCulture);
            string? owner = Regex.Match(body, @"owner = (\w+)") is { Success: true } o ? o.Groups[1].Value : null;
            string? cores = Regex.Match(body, @"cores = \{[^}]*\}") is { Success: true } c ? c.Value : null;
            string? continent = Regex.Match(body, @"continent = (\w+)") is { Success: true } ct ? ct.Groups[1].Value : null;
            var buildings = Regex.Match(body, @"buildings = \{.*?\n\t\}", RegexOptions.Singleline);
            int coastBest = chunks.Select((ch, i) => (i, n: ch.Count(l => l.IsCoastal))).OrderByDescending(t => t.n).First().i;
            var shares = new List<(string, double)>();
            for (int i = 0; i < chunks.Count; i++)
            {
                string newKey = i == 0 ? key : $"{prefix2}_{nextNumber[prefix2] = nextNumber.GetValueOrDefault(prefix2) + 1}";
                double share = chunks[i].Count / (double)locs.Count;
                shares.Add((newKey, share));
                sb.Append(i == 0 ? m.Groups["head"].Value : "\n").Append(newKey).Append(" = {\n");
                sb.Append($"\tname = \"{(i == 0 ? name : name + " " + (i + 1))}\"\n");
                if (owner != null) sb.Append($"\towner = {owner}\n");
                if (cores != null) sb.Append('\t').Append(cores).Append('\n');
                if (continent != null) sb.Append($"\tcontinent = {continent}\n");
                sb.Append($"\tmanpower = {Math.Max(1, (int)Math.Round(manpower * share))}\n");
                sb.Append($"\ttax = {(tax * share).ToString("0.0#", CultureInfo.InvariantCulture)}\n");
                sb.Append($"\tlocations = {{ {string.Join(" ", chunks[i].Select(l => l.Key))} }}\n");
                if (buildings.Success && i == coastBest) sb.Append('\t').Append(buildings.Value).Append('\n');
                sb.Append('}');
                if (i > 0) newCount++;
            }
            splitOf[key] = shares;
        }
        sb.Append(statesText, pos, statesText.Length - pos);

        // the people follow the land
        var popBlock = new Regex(@"(?<key>^[a-z0-9_]+) = \{\n(?<body>(?:\t[^\n]*\n)+)\}", RegexOptions.Multiline);
        var pb = new StringBuilder();
        int pp = 0;
        foreach (Match m in popBlock.Matches(popsText))
        {
            pb.Append(popsText, pp, m.Index - pp);
            pp = m.Index + m.Length;
            string key = m.Groups["key"].Value;
            if (!splitOf.TryGetValue(key, out var shares)) { pb.Append(m.Value); continue; }
            var lines = m.Groups["body"].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < shares.Count; i++)
            {
                if (i > 0) pb.Append('\n');
                pb.Append(shares[i].Key).Append(" = {\n");
                foreach (var line in lines)
                    pb.Append(Regex.Replace(line, @"size = (\d+)", mm => $"size = {Math.Max(1, (int)Math.Round(double.Parse(mm.Groups[1].Value, CultureInfo.InvariantCulture) * shares[i].Item2))}")).Append('\n');
                pb.Append('}');
            }
        }
        pb.Append(popsText, pp, popsText.Length - pp);

        foreach (var (path, text) in new[] { (statesPath, sb.ToString()), (popsPath, pb.ToString()) })
        {
            if (!File.Exists(path + ".orig")) File.Copy(path, path + ".orig");
            File.WriteAllText(path, text.Replace("\n", "\r\n"));
        }
        Console.WriteLine($"{stateCount} states, {splitOf.Count} cut into {splitOf.Values.Sum(v => v.Count)} chunks (+{newCount} states)");
        return 0;
    }

    /// <summary><c>burger export-state-centres &lt;project&gt; &lt;file.csv&gt;</c>: the middle of every state in degrees (key,lon,lat,locations), for tools/import-history.py.</summary>
    private static int ExportStateCentres(List<string> args)
    {
        string project = ResolveProject(RequireProjectArg(args, "burger export-state-centres <project> <file.csv>"));
        if (args.Count < 1) { Console.WriteLine("usage: burger export-state-centres <project> <file.csv>"); return 2; }
        Log.EchoToConsole = false;
        var game = Game.Load(GameDir(project), compileScripts: false, seed: 1);
        var map = game.World.Map;
        var sb = new StringBuilder("key,lon,lat,locations\n");
        foreach (var s in game.World.States.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            var land = s.Locations.Where(l => l.IsLand).ToList();
            if (land.Count == 0) land = s.Locations.ToList();
            double x = land.Average(l => l.CenterX), y = land.Average(l => l.CenterY);
            double lon = -180.0 + 360.0 * x / map.Width, lat = map.Settings.NorthLat - (map.Settings.NorthLat - map.Settings.SouthLat) * y / map.Height;
            sb.Append(s.Key).Append(',').Append(lon.ToString("0.###", CultureInfo.InvariantCulture)).Append(',').Append(lat.ToString("0.###", CultureInfo.InvariantCulture)).Append(',').Append(land.Count).Append('\n');
        }
        File.WriteAllText(args[0], sb.ToString());
        Console.WriteLine($"{game.World.States.Count} states written to {args[0]}");
        return 0;
    }

    /// <summary>The connected pieces of a set of locations (neighbours inside the set).</summary>
    private static List<List<Location>> Pieces(List<Location> locs)
    {
        var set = new HashSet<Location>(locs);
        var seen = new HashSet<Location>();
        var result = new List<List<Location>>();
        foreach (var l in locs)
        {
            if (!seen.Add(l)) continue;
            var piece = new List<Location> { l };
            var q = new Queue<Location>(); q.Enqueue(l);
            while (q.Count > 0)
                foreach (var a in q.Dequeue().Neighbors)
                    if (set.Contains(a.To) && seen.Add(a.To)) { piece.Add(a.To); q.Enqueue(a.To); }
            result.Add(piece.OrderBy(x => x.Key, StringComparer.Ordinal).ToList());
        }
        return result;
    }

    /// <summary><c>burger export-location-centres &lt;project&gt; &lt;file.csv&gt;</c>: state,location,lon,lat of every land location that belongs to a state, for tools/refine_states.py.</summary>
    private static int ExportLocationCentres(List<string> args)
    {
        string project = ResolveProject(RequireProjectArg(args, "burger export-location-centres <project> <file.csv>"));
        if (args.Count < 1) { Console.WriteLine("usage: burger export-location-centres <project> <file.csv>"); return 2; }
        Log.EchoToConsole = false;
        var game = Game.Load(GameDir(project), compileScripts: false, seed: 1);
        var map = game.World.Map;
        var sb = new StringBuilder("state,location,lon,lat\n");
        int n = 0;
        foreach (var s in game.World.States.OrderBy(s => s.Key, StringComparer.Ordinal))
            foreach (var l in s.Locations.Where(l => l.IsLand))
            {
                double lon = -180.0 + 360.0 * l.CenterX / map.Width, lat = map.Settings.NorthLat - (map.Settings.NorthLat - map.Settings.SouthLat) * l.CenterY / map.Height;
                sb.Append(s.Key).Append(',').Append(l.Key).Append(',').Append(lon.ToString("0.###", CultureInfo.InvariantCulture)).Append(',').Append(lat.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
                n++;
            }
        File.WriteAllText(args[0], sb.ToString());
        Console.WriteLine($"{n} locations written to {args[0]}");
        return 0;
    }

    /// <summary>Cuts connected locations into k connected chunks: seeds far apart, then every location joins the nearest seed over the neighbours inside the state.</summary>
    private static List<List<Location>> Cut(List<Location> locs, int k)
    {
        var set = new HashSet<Location>(locs);
        // components first (a state may be in two pieces): each piece is cut on its own
        var seen = new HashSet<Location>();
        var pieces = new List<List<Location>>();
        foreach (var l in locs)
        {
            if (!seen.Add(l)) continue;
            var piece = new List<Location> { l };
            var q = new Queue<Location>(); q.Enqueue(l);
            while (q.Count > 0)
                foreach (var a in q.Dequeue().Neighbors)
                    if (set.Contains(a.To) && seen.Add(a.To)) { piece.Add(a.To); q.Enqueue(a.To); }
            pieces.Add(piece);
        }
        var result = new List<List<Location>>();
        int total = locs.Count;
        foreach (var piece in pieces)
        {
            int kp = Math.Max(1, (int)Math.Round(k * piece.Count / (double)total));
            if (piece.Count <= 3) kp = 1;
            var seeds = new List<Location>();
            double cx = piece.Average(l => l.CenterX), cy = piece.Average(l => l.CenterY);
            seeds.Add(piece.OrderByDescending(l => Sq(l.CenterX - cx, l.CenterY - cy)).First());
            while (seeds.Count < kp)
                seeds.Add(piece.OrderByDescending(l => seeds.Min(s => Sq(l.CenterX - s.CenterX, l.CenterY - s.CenterY))).First());
            // multi-source breadth first search
            var owner = new Dictionary<Location, int>();
            var queue = new Queue<Location>();
            for (int i = 0; i < seeds.Count; i++) { owner[seeds[i]] = i; queue.Enqueue(seeds[i]); }
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var a in cur.Neighbors.OrderBy(a => a.To.Key, StringComparer.Ordinal))
                    if (set.Contains(a.To) && !owner.ContainsKey(a.To)) { owner[a.To] = owner[cur]; queue.Enqueue(a.To); }
            }
            for (int i = 0; i < seeds.Count; i++)
                result.Add(piece.Where(l => owner[l] == i).OrderBy(l => l.Key, StringComparer.Ordinal).ToList());
        }
        return result;
    }

    private static double Sq(double dx, double dy) => dx * dx + dy * dy;
}
