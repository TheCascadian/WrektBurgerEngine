using Burger.Core.Development;
using Burger.Core.Model;
using Burger.Core.Multiplayer;
using Raylib_cs;

namespace Burger.Runtime;

public sealed partial class Hud
{
    /// <summary>Development, literacy, urbanization and institutions of a state, for the location panel. Returns the new y.</summary>
    private float DrawStateDevelopment(State st, float x, float y, float w)
    {
        var D = G.Development;
        float lh = 20 * S;
        int level = D.Level(st);
        var player = G.World.Player;
        string why = player != null ? D.CannotDevelop(player, st) ?? "" : "Choose a country first";
        double cost = player != null ? D.Cost(player, st) : 0;
        string cur = player != null ? D.Currency(player)?.Name.ToLowerInvariant() ?? "gold" : "";

        Gui.Label("Development", x, y, 15, Gui.TextDim);
        var bar = new Rectangle(x + 100 * S, y + 5 * S, 90 * S, 10 * S);
        Raylib.DrawRectangleRec(bar, new Color(30, 34, 42, 255));
        float fill = Math.Clamp(level / 20f, 0, 1);
        Raylib.DrawRectangleRec(new Rectangle(bar.X, bar.Y, bar.Width * fill, bar.Height), new Color(220, 180, 70, 255));
        Raylib.DrawRectangleLinesEx(bar, 1, new Color(90, 96, 110, 255));
        Gui.Label(level.ToString(), bar.X + bar.Width + 8 * S, y, 15, Gui.Text, FontKind.Bold);
        if (player != null && st.Owner == player)
        {
            var btn = new Rectangle(x + w - 96 * S, y - 2 * S, 90 * S, lh);
            string tip = why.Length == 0 ? $"Raise development to {level + 1}\nCost {cost:0} {cur}" : why;
            if (Gui.Button(btn, $"+1  ({cost:0})", why.Length == 0, tip + $"\nEach level gained: +{G.Rules.Development.TaxPerLevel(G):P0} tax, +{G.Rules.Development.ManpowerPerLevel(G):P0} manpower, +{G.Rules.Development.RoomPerLevel(G):0} rooms", false, 13))
            {
                if (Mp != null) Mp.Submit(GameCommand.Develop, st.Key);
                else D.Develop(player, st);
            }
        }
        if (Gui.Hover(new Rectangle(x, y, 190 * S, lh)))
            Gui.Tooltip($"Development {level} (started at {D.StartLevel(st)}, {D.Gained(st)} gained)\nEach level gained adds {G.Rules.Development.TaxPerLevel(G):P0} tax, {G.Rules.Development.ManpowerPerLevel(G):P0} manpower and {G.Rules.Development.RoomPerLevel(G):0} rooms (building slots).");
        y += lh;
        Gui.Label("Literacy", x, y, 15, Gui.TextDim);
        Gui.Label($"{D.Literacy(st):P1}", x + 100 * S, y, 15, Gui.Text, FontKind.Bold);
        if (Gui.Hover(new Rectangle(x, y, w, lh))) Gui.Tooltip("Share of people who can read. Rises 0.2% a year where schools exist (scribes, priests and nobles), faster with some institutions.");
        y += lh;
        Gui.Label("Urbanization", x, y, 15, Gui.TextDim);
        Gui.Label($"{D.Urbanization(st):P1}", x + 100 * S, y, 15, Gui.Text, FontKind.Bold);
        if (Gui.Hover(new Rectangle(x, y, w, lh))) Gui.Tooltip("Share of people in towns. Rises 0.1% a year where markets exist (craft and trade people), faster with some institutions.");
        y += lh;
        var have = D.Institutions.Where(i => D.HasAppeared(i)).ToList();
        if (have.Count > 0)
        {
            Gui.Label("Institutions", x, y, 15, Gui.TextDim);
            var adopted = have.Where(i => D.Adopted(i, st)).Select(i => i.Name).ToList();
            string text = adopted.Count > 0 ? string.Join(", ", adopted) : "none";
            Gui.Label(Gui.Fit(text, w - 100 * S, 14), x + 100 * S, y + 1 * S, 14, adopted.Count > 0 ? Gui.Good : Gui.TextDim);
            if (Gui.Hover(new Rectangle(x, y, w, lh)))
                Gui.Tooltip(string.Join("\n", have.Select(i => $"{i.Name}: {D.Presence(i, st):P0}" + (D.Adopted(i, st) ? " (adopted)" : ""))));
            y += lh;
        }
        return y + 4 * S;
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
