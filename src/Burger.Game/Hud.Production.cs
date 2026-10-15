using Burger.Core.Multiplayer;
using System.Numerics;
using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Modding;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>Production (factories, lines, construction, stockpile) and the division designer.</summary>
public sealed partial class Hud
{
    private bool _addingLine;
    private ProductionLine? _changingLine;

    private string Fmt(double v) => v >= 10000 ? $"{v / 1000:0.#}k" : v >= 100 ? $"{v:0}" : $"{v:0.#}";

    private bool Tabs(float x, float y, float w, string[] names, ref int current)
    {
        bool changed = false;
        float tw = (w - (names.Length - 1) * 4 * S) / names.Length;
        for (int i = 0; i < names.Length; i++)
            if (Gui.Button(new Rectangle(x + i * (tw + 4 * S), y, tw, 26 * S), names[i], true, null, current == i, 15)) { if (current != i) changed = true; current = i; }
        return changed;
    }

    private void DrawProductionPanel()
    {
        var player = G.World.Player;
        if (player == null || !Has(FeatureSet.Production)) { CloseLeft(); return; }
        var r = FitRect("prod" + Ui.ProductionTab);
        r.Width = 440 * S;
        Gui.Panel(r, "Production");
        if (Gui.CloseButton(r)) { CloseLeft(); return; }
        float x = r.X + 12 * S, y = r.Y + Gui.HeaderHeight + 10 * S, w = r.Width - 24 * S;
        int tab = Ui.ProductionTab;
        if (Tabs(x, y, w, new[] { "Production", "Construction", "Stockpile" }, ref tab)) { _addingLine = false; _changingLine = null; }
        Ui.ProductionTab = tab;
        if (tab != 1) Ui.BuildType = null;
        y += 36 * S;
        switch (tab)
        {
            case 0: DrawProductionLines(player, r, x, y, w); break;
            case 1: DrawConstruction(player, r, x, y, w); break;
            default: DrawStockpile(player, r, x, y, w); break;
        }
    }

    private static string EquipGroup(EquipmentType e)
    {
        string cat = e.Category.ToLowerInvariant(), k = e.Key.ToLowerInvariant();
        if (cat == "air") return "Air";
        if (cat == "naval") return "Naval";
        if (k.Contains("infantry") || k.Contains("support")) return "Infantry";
        if (k.Contains("artillery") || k.Contains("anti_")) return "Guns";
        if (k.Contains("tank") || k.Contains("motor") || k.Contains("truck")) return "Vehicles";
        return "Other";
    }

    private static int GroupOrder(string g) => g switch { "Infantry" => 0, "Guns" => 1, "Vehicles" => 2, "Air" => 3, "Naval" => 4, _ => 5 };

    private void DrawProductionLines(Country c, Rectangle r, float x, float y, float w)
    {
        var P = G.Production;
        int mil = P.MilitaryFactories(c), dock = P.Dockyards(c);
        Gui.Label($"Military factories {mil - P.FreeFactories(c, false)}/{mil} in use", x, y, 14);
        if (dock > 0) { string dt = $"Dockyards {dock - P.FreeFactories(c, true)}/{dock}"; Gui.Label(dt, x + w - Gui.Measure(dt, 14).X, y, 14, Gui.TextDim); }
        y += 24 * S;
        if (_addingLine || _changingLine != null)
        {
            _fitH["prod0"] = 2000;
            Gui.Section(_changingLine != null ? "Produce instead" : "New production line", x, y, w);
            y += 26 * S;
            var groups = G.Data.Equipment.Values.GroupBy(EquipGroup).OrderBy(g => GroupOrder(g.Key)).Select(g => (g.Key, g.Count())).ToList();
            y += FilterChips("equip", groups, x, y, w, out string grp);
            var items = G.Data.Equipment.Values.Where(e => EquipGroup(e) == grp).OrderBy(e => e.Cost).ToList();
            float pneed = items.Count * 32 * S;
            FitEnd("prod0", r, y + pneed + 46 * S);
            float top = Gui.BeginScroll("equip", new Rectangle(x, y, w, Math.Min(pneed, r.Y + r.Height - y - 50 * S)), pneed);
            float ey = top;
            foreach (var e in items)
            {
                string tip = $"{e.Name}\nCost: {Num(e.Cost, 1)} per unit" + (P.UsesDockyards(e) ? "\nBuilt in dockyards" : "");
                bool pick = Gui.Button(new Rectangle(x, ey, w - 10 * S, 28 * S), "", true, tip, false, 15);
                Gui.Label(Gui.Fit(e.Name, w - 120 * S, 15), x + 8 * S, ey + 5 * S, 15);
                string stk = Num(P.Stock(c, e.Key));
                Gui.Label(stk, x + w - 18 * S - Gui.Measure(stk, 13).X, ey + 6 * S, 13, P.Stock(c, e.Key) > 0 ? Gui.Text : Gui.TextDim);
                if (pick)
                {
                    if (_changingLine != null) P.ChangeLine(_changingLine, e);
                    else if (!Send(GameCommand.QueueProduction, e.Key, Math.Min(1, P.FreeFactories(c, P.UsesDockyards(e))).ToString())) P.AddLine(c, e, Math.Min(1, P.FreeFactories(c, P.UsesDockyards(e))));
                    _addingLine = false;
                    _changingLine = null;
                }
                ey += 32 * S;
            }
            Gui.EndScroll();
            if (Gui.Button(new Rectangle(x, r.Y + r.Height - 42 * S, w, 30 * S), "Cancel", true, null, false, 15)) { _addingLine = false; _changingLine = null; }
            return;
        }
        float rowH = 58 * S;
        float need = Math.Max(24 * S, c.ProductionLines.Count * (rowH + 4 * S));
        FitEnd("prod0", r, y + need + 36 * S);
        float listTop = Gui.BeginScroll("lines", new Rectangle(x, y, w, Math.Min(need, Raylib.GetScreenHeight() * 0.6f)), need);
        float ly = listTop;
        foreach (var line in c.ProductionLines.ToList())
        {
            var box = new Rectangle(x, ly, w - 8 * S, rowH);
            Card(box, new Color(36, 38, 44, 255));
            bool naval = P.UsesDockyards(line.Equipment);
            Stripe(box, line.Efficiency >= 0.75 ? Gui.Good : line.Efficiency >= 0.3 ? Gui.Gold : new Color(90, 96, 108, 255));
            // controls: big - n + and x on the first row
            float bs = 30 * S, bx = box.X + box.Width - 4 * bs - 18 * S, by = box.Y + 4 * S;
            Gui.Label(Gui.Fit(line.Equipment.Name, bx - box.X - 18 * S, 16, FontKind.Bold), box.X + 10 * S, box.Y + 8 * S, 16, Gui.Gold, FontKind.Bold);
            if (Gui.Button(new Rectangle(bx, by, bs, bs), "-", line.Factories > 0, "One factory less", false, 18)) line.Factories--;
            string fc = line.Factories.ToString();
            Gui.Label(fc, bx + bs + 3 * S + (bs - 6 * S - Gui.Measure(fc, 15, FontKind.Bold).X) / 2, by + 6 * S, 15, Gui.Text, FontKind.Bold);
            if (Gui.Hover(new Rectangle(bx + bs, by, bs, bs))) Gui.Tooltip($"{line.Factories} {(naval ? "dockyards" : "factories")} on this line");
            if (Gui.Button(new Rectangle(bx + 2 * bs + 6 * S, by, bs, bs), "+", P.FreeFactories(c, naval) > 0, "One factory more (if free)", false, 18)) line.Factories++;
            if (Gui.Button(new Rectangle(bx + 3 * bs + 12 * S, by, bs, bs), "x", true, "Stop this line", false, 15)) P.RemoveLine(c, line);
            // second row: efficiency, output, change
            double perDay = line.Factories * G.Data.Defines.Get("production_per_factory", 4.5) * line.Efficiency / line.Equipment.Cost;
            var eff = new Rectangle(box.X + 10 * S, box.Y + 42 * S, 90 * S, 8 * S);
            Gui.Bar(eff, (float)line.Efficiency, new Color(90, 170, 220, 255));
            if (Gui.Hover(eff)) Gui.Tooltip($"Efficiency {line.Efficiency:P0}\nIt grows every day the line keeps producing the same thing.");
            Gui.Label($"{line.Efficiency:P0}  {perDay:0.##}/day  stock {Fmt(P.Stock(c, line.Equipment.Key))}", eff.X + eff.Width + 8 * S, box.Y + 37 * S, 12, Gui.TextDim);
            if (Gui.Button(new Rectangle(box.X + box.Width - 74 * S, box.Y + 36 * S, 66 * S, 18 * S), "Change", true, "Produce something else (efficiency drops)", false, 11)) _changingLine = line;
            ly += rowH + 4 * S;
        }
        Gui.EndScroll();
        if (c.ProductionLines.Count == 0) Gui.Label("No production lines. Add one below.", x, y, 14, Gui.TextDim);
        if (Gui.Button(new Rectangle(x, r.Y + r.Height - 42 * S, w, 30 * S), "+ Add production line", G.Data.Equipment.Count > 0, null, false, 15)) _addingLine = true;
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static Color CategoryColor(string cat) => cat.ToLowerInvariant() switch
    {
        "factory" => new Color(220, 170, 70, 255), "infrastructure" => new Color(120, 170, 220, 255),
        "military" => new Color(210, 90, 80, 255), "naval" => new Color(70, 150, 200, 255),
        "civil" => new Color(110, 190, 120, 255), "air" => new Color(170, 140, 220, 255),
        "capital" => new Color(210, 90, 80, 255), "screen" => new Color(120, 170, 220, 255),
        "transport" => new Color(110, 190, 120, 255), _ => new Color(130, 134, 144, 255),
    };

    private void DrawConstruction(Country c, Rectangle r, float x, float y, float w)
    {
        var P = G.Production;
        int civ = P.CivilianFactories(c), free = P.CivilianForConstruction(c);
        Gui.Label($"Civilian factories: {civ}  ({free} for construction)", x, y, 15);
        if (Gui.Hover(new Rectangle(x, y, w, 20 * S)))
            Gui.Tooltip($"{civ - free} civilian factories make consumer goods.\nEach building gets up to {G.Data.Defines.Get("max_factories_per_project", 15):0} factories, top of the queue first.");
        y += 26 * S;
        Gui.Section("Build", x, y, w);
        y += 26 * S;
        var types = G.Data.BuildingTypes.Values.ToList();
        var cats = types.GroupBy(b => b.Category).Select(g => (Cap(g.Key), g.Count())).ToList();
        y += FilterChips("build", cats, x, y, w, out string bcat);
        var shown = types.Where(b => Cap(b.Category) == bcat).ToList();
        float bw = (w - 6 * S) / 2, bh = 42 * S;
        for (int i = 0; i < shown.Count; i++)
        {
            var b = shown[i];
            var br = new Rectangle(x + (i % 2) * (bw + 6 * S), y + (i / 2) * (bh + 4 * S), bw, bh);
            string tip = $"{b.Name}\nCost: {Num(b.Cost)}\nMax level: {b.MaxLevel}" + (b.Description.Length > 0 ? "\n" + Localisation.Get(b.Description) : "") + "\nPick it, then click your states on the map.";
            bool on = Ui.BuildType == b;
            if (Gui.Button(br, "", true, tip, on, 14)) Ui.BuildType = on ? null : b;
            Stripe(br, CategoryColor(b.Category));
            bool locked = !G.World.States.Any(s => s.Owner == c && s.Buildings.GetValueOrDefault(b.Key) < b.MaxLevel);
            float tx = br.X + 9 * S;
            if (BuildingIcon(b.Key, new Rectangle(tx, br.Y + 7 * S, 28 * S, 28 * S), locked)) tx += 34 * S;
            Gui.Label(Gui.Fit(b.Name, br.X + br.Width - tx - 6 * S, 14, FontKind.Bold), tx, br.Y + 4 * S, 14, locked ? Gui.TextDim : on ? Color.White : Gui.Text, FontKind.Bold);
            Gui.Label($"{Num(b.Cost)} · max {b.MaxLevel}", tx, br.Y + 22 * S, 12, Gui.TextDim);
        }
        y += (shown.Count + 1) / 2 * (bh + 4 * S) + 6 * S;
        if (Ui.BuildType != null) { y += Gui.Wrapped($"Click one of your states to build a {Ui.BuildType.Name}. Right-click or Esc to stop.", x, y, w, 14, Gui.Gold) + 6 * S; }
        Gui.Section($"Queue ({c.Construction.Count})", x, y, w);
        y += 26 * S;
        float rowH = 46 * S;
        float need = Math.Max(20 * S, c.Construction.Count * (rowH + 4 * S));
        FitEnd("prod1", r, y + need);
        float top = Gui.BeginScroll("construction", new Rectangle(x, y, w, Math.Min(need, r.Y + r.Height - y - 10 * S)), need);
        float qy = top;
        for (int i = 0; i < c.Construction.Count; i++)
        {
            var p = c.Construction[i];
            var box = new Rectangle(x, qy, w - 8 * S, rowH);
            Card(box, new Color(36, 38, 44, 255));
            Gui.Label(Gui.Fit($"{p.Building.Name} - {p.State.DisplayName}", box.Width - 100 * S, 15), box.X + 8 * S, box.Y + 4 * S, 15);
            Gui.Bar(new Rectangle(box.X + 8 * S, box.Y + 28 * S, box.Width - 110 * S, 9 * S), (float)(p.Progress / p.Building.Cost), Gui.Gold);
            Gui.Label($"{p.Factories} fact.", box.X + box.Width - 96 * S, box.Y + 24 * S, 13, Gui.TextDim);
            if (i > 0 && Gui.Button(new Rectangle(box.X + box.Width - 58 * S, box.Y + 4 * S, 24 * S, 20 * S), "^", true, "Move up", false, 13))
                (c.Construction[i - 1], c.Construction[i]) = (c.Construction[i], c.Construction[i - 1]);
            if (Gui.Button(new Rectangle(box.X + box.Width - 30 * S, box.Y + 4 * S, 24 * S, 20 * S), "x", true, "Cancel", false, 13)) { c.Construction.RemoveAt(i); Gui.EndScroll(); return; }
            qy += rowH + 4 * S;
        }
        Gui.EndScroll();
    }

    private void DrawStockpile(Country c, Rectangle r, float x, float y, float w)
    {
        var shortage = G.Production.Shortage(c);
        var produced = c.ProductionLines.GroupBy(l => l.Equipment.Key).ToDictionary(g => g.Key, g => g.Sum(l => l.LastOutput), StringComparer.OrdinalIgnoreCase);
        float top = Gui.BeginScroll("stockpile", new Rectangle(x, y, w, r.Y + r.Height - y - 10 * S), G.Data.Equipment.Count * 46 * S);
        float sy = top;
        foreach (var e in G.Data.Equipment.Values)
        {
            var box = new Rectangle(x, sy, w - 8 * S, 42 * S);
            Card(box, new Color(36, 38, 44, 255));
            Gui.Label(e.Name, box.X + 8 * S, box.Y + 4 * S, 15, Gui.Text, FontKind.Bold);
            double need = shortage.GetValueOrDefault(e.Key);
            Gui.Label($"+{produced.GetValueOrDefault(e.Key):0.#}/day" + (need > 0 ? $"   missing {Fmt(need)}" : ""), box.X + 8 * S, box.Y + 23 * S, 13, need > 0 ? Gui.Bad : Gui.TextDim);
            string stock = Fmt(G.Production.Stock(c, e.Key));
            var m = Gui.Measure(stock, 18, FontKind.Bold);
            Gui.Label(stock, box.X + box.Width - m.X - 10 * S, box.Y + 11 * S, 18, Gui.Gold, FontKind.Bold);
            if (Gui.Hover(box)) Gui.Tooltip($"[icon:production][state:value] {e.Name}: {G.Production.Stock(c, e.Key):0} in stock\n" +
                            $"[state:warning] Missing for divisions and training: {need:0}");
            sy += 46 * S;
        }
        Gui.EndScroll();
    }

}
