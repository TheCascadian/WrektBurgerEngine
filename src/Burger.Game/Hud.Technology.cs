using Burger.Core.Model;
using Burger.Core.Technology;
using Raylib_cs;

namespace Burger.Runtime;

public sealed partial class Hud
{
    private string _techCategory = "";

    /// <summary>Technology: research slots and progress, what can be researched now, and what the country knows by category.</summary>
    private void DrawTechnologyPanel()
    {
        var player = G.World.Player;
        if (player == null) { Ui.Left = LeftPanel.None; return; }
        var r = FitRect("tech");
        Gui.Panel(r, "Technology");
        if (Gui.CloseButton(r)) { CloseLeft(); return; }
        float x = r.X + 10 * S, y = r.Y + 36 * S, w = r.Width - 20 * S;
        var T = G.Tech;
        var rules = G.Rules.Tech;
        var snapshot = _app.Snapshot;
        double points = snapshot.TechnologyPoints;
        int slots = snapshot.TechnologySlotCount;
        Gui.Label($"{points:0.0} points/month · {slots} slot(s) · {snapshot.KnownTechnologyCount}/{snapshot.Technologies.Length} known", x, y, 14, Gui.TextDim);
        y += 26 * S;

        // the research slots
        var current = T.SlotsOf(player).ToList();
        for (int i = 0; i < slots; i++)
        {
            var box = new Rectangle(x, y, w - 8 * S, 44 * S);
            Card(box, new Color(40, 46, 58, 255), i < current.Count);
            if (i < current.Count)
            {
                var slot = current[i];
                var slotSnapshot = snapshot.ResearchSlots.FirstOrDefault(s => s.Key.Equals(slot.Tech.Key, StringComparison.OrdinalIgnoreCase));
                double cost = slotSnapshot.Cost;
                Stripe(box, new Color(110, 170, 230, 255));
                double frac = Math.Clamp(slotSnapshot.Progress / Math.Max(1, cost), 0, 1);
                Gui.Label(slotSnapshot.Name, x + 10 * S, y + 4 * S, 15, Gui.Gold, FontKind.Bold);
                var pb = new Rectangle(x + 10 * S, y + 26 * S, w - 90 * S, 10 * S);
                Gui.Bar(pb, (float)frac, new Color(110, 170, 230, 255));
                string pct = $"{frac:P0}";
                Gui.Label(pct, x + w - 72 * S - Gui.Measure(pct, 12).X, y + 23 * S, 12, Gui.TextDim);
                if (Gui.Hover(pb)) Gui.Tooltip($"{slotSnapshot.Progress:0} of {cost:0} points");
                if (Gui.Button(new Rectangle(x + w - 62 * S, y + 8 * S, 50 * S, 26 * S), "Drop", true, "Stop researching (the progress is lost)", false, 13)) T.Cancel(player, slot);
                if (Gui.Hover(box)) Gui.Tooltip(TechTip(slot.Tech, player));
            }
            else Gui.Label("Free slot - pick a technology below", x + 10 * S, y + 13 * S, 14, Gui.TextDim);
            y += 50 * S;
        }

        // categories
        var cats = snapshot.Technologies.Select(t => t.Category).Distinct().OrderBy(c => c).ToList();
        if (_techCategory.Length == 0 || !cats.Contains(_techCategory)) _techCategory = cats.FirstOrDefault() ?? "";
        float bx = x;
        foreach (var c in cats)
        {
            float bw = Math.Max(54 * S, Gui.Measure(c, 12).X + 14 * S);
            if (bx + bw > x + w && bx > x) { bx = x; y += 28 * S; }
            if (Gui.Button(new Rectangle(bx, y, bw, 24 * S), c, true, $"{snapshot.Technologies.Count(t => t.Category == c && t.Known)} of {snapshot.Technologies.Count(t => t.Category == c)} known", c == _techCategory, 12)) _techCategory = c;
            bx += bw + 3 * S;
        }
        y += 30 * S;

        var list = snapshot.Technologies.Where(t => t.Category == _techCategory).OrderBy(t => t.Year).ThenBy(t => t.Order).ToList();
        float rowH = 40 * S;
        float need = list.Count * (rowH + 4 * S);
        FitEnd("tech", r, y + need);
        float top = Gui.BeginScroll("techs", new Rectangle(x, y, w, Math.Min(need, r.Bottom() - y - 10 * S)), need);
        float dy = top;
        foreach (var t in list)
        {
            var liveTech = T.Defs.GetValueOrDefault(t.Key);
            if (liveTech == null) continue;
            var box = new Rectangle(x, dy, w - 8 * S, rowH);
            bool known = t.Known;
            string? why = known ? "Known" : t.BlockReason;
            bool busy = t.Busy;
            Card(box, known ? new Color(40, 58, 44, 255) : busy ? new Color(40, 50, 70, 255) : new Color(36, 38, 44, 255), busy);
            Stripe(box, known ? Gui.Good : busy ? new Color(110, 170, 230, 255) : why == null ? Gui.Gold : new Color(90, 96, 108, 255));
            Gui.Label(Gui.Fit(t.Name, w - 150 * S, 15, FontKind.Bold), x + 10 * S, dy + 4 * S, 15, known ? Gui.Good : Gui.Text, FontKind.Bold);
            Gui.Label(known ? $"{t.Year:0} · known" : $"{t.Year:0} · {rules.Cost(G, player, liveTech):0} points", x + 10 * S, dy + 22 * S, 12, Gui.TextDim);
            if (Gui.Hover(box)) Gui.Tooltip(TechTip(liveTech, player));
            if (!known && !busy && Gui.Button(new Rectangle(x + w - 90 * S, dy + 6 * S, 76 * S, 26 * S), "Research", why == null, why ?? TechTip(liveTech, player), false, 13))
                Ui.SetStatus(T.Start(player, liveTech) ?? $"Researching {t.Name}");
            dy += rowH + 4 * S;
        }
        Gui.EndScroll();
    }

    private string TechTip(TechDef t, Country c)
    {
        var parts = new List<string> { t.Name + $" ({t.Year:0})" };
        if (t.Desc.Length > 0) parts.Add(t.Desc);
        if (t.Requires.Count > 0) parts.Add("Needs: " + string.Join(", ", t.Requires.Select(q => (G.Tech.Has(c, q) ? "[OK] " : "[X] ") + G.Tech.Defs[q].Name)));
        if (t.Unlocks.Count > 0) parts.Add("Unlocks: " + string.Join(", ", t.Unlocks));
        if (t.Modifier.Count > 0) parts.Add("Gives: " + string.Join(", ", t.Modifier.Select(kv => $"{kv.Key.Replace('_', ' ')} {kv.Value:+0.##;-0.##}")));
        return string.Join("\n", parts);
    }
}

internal static class RectExtensions
{
    public static float Bottom(this Rectangle r) => r.Y + r.Height;
}
