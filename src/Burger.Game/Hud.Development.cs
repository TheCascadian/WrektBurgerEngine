using Burger.Core.Development;
using Burger.Core.Model;
using Burger.Core.Multiplayer;
using Raylib_cs;

namespace Burger.Runtime;

public sealed partial class Hud
{
    /// <summary>Development level with its raise button, then a "Society" fold (literacy, urbanization, institutions) for the location panel. Returns the new y.</summary>
    private float DrawStateDevelopment(State st, float x, float y, float w)
    {
        var D = G.Development;
        float lh = 24 * S;
        int level = D.Level(st);
        var player = G.World.Player;
        string why = player != null ? D.CannotDevelop(player, st) ?? "" : "Choose a country first";
        double cost = player != null ? D.Cost(player, st) : 0;
        string cur = player != null ? D.Currency(player)?.Name.ToLowerInvariant() ?? "gold" : "";

        // one row: label, a bar of 20 steps, the level, the raise button
        Gui.Label("Development", x, y + 2 * S, 15, Gui.TextDim);
        bool mine = player != null && st.Owner == player;
        float btnW = mine ? 92 * S : 0, barX = x + 100 * S, barW = w - 100 * S - btnW - 40 * S;
        const int steps = 20;
        float seg = (barW - (steps - 1) * 2 * S) / steps;
        for (int i = 0; i < steps; i++)
        {
            var c = i < level ? new Color(220, 180, 70, 255) : new Color(36, 40, 50, 255);
            Raylib.DrawRectangleRec(new Rectangle(barX + i * (seg + 2 * S), y + 6 * S, seg, 12 * S), c);
        }
        Gui.Label(level.ToString(), barX + barW + 8 * S, y + 2 * S, 16, Gui.Text, FontKind.Bold);
        if (mine)
        {
            var btn = new Rectangle(x + w - btnW, y, btnW, lh - 2 * S);
            string tip = why.Length == 0 ? $"Raise development to {level + 1}\nCost {cost:0} {cur}" : why;
            if (Gui.Button(btn, $"+1  ({cost:0})", why.Length == 0, tip + $"\nEach level gained: +{G.Rules.Development.TaxPerLevel(G):P0} tax, +{G.Rules.Development.ManpowerPerLevel(G):P0} manpower, +{G.Rules.Development.RoomPerLevel(G):0} rooms", false, 13))
            {
                if (Mp != null) Mp.Submit(GameCommand.Develop, st.Key);
                else D.Develop(player!, st);
            }
        }
        if (Gui.Hover(new Rectangle(x, y, w - btnW - 4 * S, lh)))
            Gui.Tooltip($"Development {level} (started at {D.StartLevel(st)}, {D.Gained(st)} gained)\nEach level gained adds {G.Rules.Development.TaxPerLevel(G):P0} tax, {G.Rules.Development.ManpowerPerLevel(G):P0} manpower and {G.Rules.Development.RoomPerLevel(G):0} rooms (building slots).");
        y += lh + 2 * S;

        var have = D.Institutions.Where(i => D.HasAppeared(i)).ToList();
        var adopted = have.Where(i => D.Adopted(i, st)).ToList();
        string summary = $"Literacy {D.Literacy(st):P0}  ·  Urban {D.Urbanization(st):P0}" + (have.Count > 0 ? $"  ·  {adopted.Count}/{have.Count} institutions" : "");
        bool open = Collapse("society", "Society", summary, Gui.TextDim, x, y, w);
        y += 26 * S;
        if (!open) return y;

        // two meters side by side, each in its own cell
        void Meter(int i, string k, double v, string tip)
        {
            float gap = 8 * S, cw = (w - gap) / 2;
            var c = new Rectangle(x + i * (cw + gap), y, cw, 38 * S);
            Raylib.DrawRectangleRounded(c, 0.12f, 4, new Color(255, 255, 255, 10));
            Gui.Label(k, c.X + 8 * S, c.Y + 3 * S, 12, Gui.TextDim);
            Gui.Label($"{v:P1}", c.X + c.Width - Gui.Measure($"{v:P1}", 15, FontKind.Bold).X - 8 * S, c.Y + 2 * S, 15, Gui.Text, FontKind.Bold);
            var bar = new Rectangle(c.X + 8 * S, c.Y + 24 * S, c.Width - 16 * S, 8 * S);
            Raylib.DrawRectangleRec(bar, new Color(30, 34, 42, 255));
            Raylib.DrawRectangleRec(new Rectangle(bar.X, bar.Y, bar.Width * (float)Math.Clamp(v, 0, 1), bar.Height), new Color(110, 170, 210, 255));
            if (Gui.Hover(c)) Gui.Tooltip(tip);
        }
        Meter(0, "Literacy", D.Literacy(st), "Share of people who can read. Rises 0.2% a year where schools exist (scribes, priests and nobles), faster with some institutions.");
        Meter(1, "Urbanization", D.Urbanization(st), "Share of people in towns. Rises 0.1% a year where markets exist (craft and trade people), faster with some institutions.");
        y += 44 * S;
        if (have.Count > 0)
        {
            float cx = x;
            foreach (var i in have)
            {
                bool got = D.Adopted(i, st);
                var m = Gui.Measure(i.Name, 13);
                float cw = m.X + 14 * S;
                if (cx + cw > x + w) { cx = x; y += 24 * S; }
                var chip = new Rectangle(cx, y, cw, 20 * S);
                Raylib.DrawRectangleRounded(chip, 0.4f, 4, got ? new Color(40, 90, 60, 255) : new Color(40, 44, 54, 255));
                Gui.Label(i.Name, cx + 7 * S, y + 2 * S, 13, got ? Gui.Text : Gui.TextDim);
                if (Gui.Hover(chip)) Gui.Tooltip($"{i.Name}: {D.Presence(i, st):P0}" + (got ? " (adopted)" : " (not adopted)"));
                cx += cw + 4 * S;
            }
            y += 26 * S;
        }
        return y + 2 * S;
    }

    /// <summary>Cycles the institution the Institutions map mode shows.</summary>
    private void NextInstitution()
    {
        var list = G.Development.Institutions.Where(i => G.Development.HasAppeared(i)).ToList();
        if (list.Count == 0) { Map.Mode = MapMode.Political; return; }
        int at = list.FindIndex(i => i.Key == Map.InstitutionKey);
        Map.InstitutionKey = list[(at + 1) % list.Count].Key;
    }
}
