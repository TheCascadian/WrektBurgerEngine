using System.Numerics;
using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Politics;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>What the people think: the People sub-tab of the Politics overview and the Politics fold of the location panel.</summary>
public sealed partial class Hud
{
    private int _peopleSort;       // 0 size, 1 opinion, 2 radicalism
    private int _peoplePage;
    /// <summary>Seats sub-tabs (location panel and Politics overview) are switched off for now; set true to bring them back.</summary>
    internal const bool SeatsTabs = false;
    private int _stateMoodSub;     // 0 groups, 1 seats
    private const int PeoplePageSize = 7;

    private static Color ReactionColor(string reaction) => reaction switch
    {
        "Approve" => Gui.Good, "Comply" => Gui.Text, "Disapprove" => Gui.Gold, _ => Gui.Bad,
    };

    private static string ObjectiveText(string objective) => objective switch
    {
        "autonomy" => "Autonomy", "religious_rights" => "Religious rights", "labour_rights" => "Labour rights",
        "property" => "Property", "stability" => "Order", _ => Localisation.GetOrPretty(objective),
    };

    private static string ObjectiveTip(string objective) => objective switch
    {
        "autonomy" => "Wants self-rule: their culture is not accepted here, or the land is not their core. Content when a party of their culture sits in parliament.",
        "religious_rights" => "Follows a different faith from the state religion. Content when a party of their faith holds seats.",
        "labour_rights" => "Workers and peasants. Unhappy under governments of the right.",
        "property" => "Landowners and the wealthy. Unhappy under governments of the left.",
        _ => "Wants a stable, orderly country.",
    };

    /// <summary>A bar centred on zero: red to the left for -100, green to the right for +100.</summary>
    private void OpinionBar(Rectangle r, double opinion)
    {
        Raylib.DrawRectangleRec(r, new Color(14, 15, 19, 255));
        float mid = r.X + r.Width / 2;
        float len = (float)(Math.Clamp(opinion, -100, 100) / 100) * r.Width / 2;
        var col = opinion >= 0 ? Gui.Good : Gui.Bad;
        Raylib.DrawRectangleRec(len >= 0 ? new Rectangle(mid, r.Y, len, r.Height) : new Rectangle(mid + len, r.Y, -len, r.Height), col);
        Raylib.DrawRectangleRec(new Rectangle(mid - 0.5f, r.Y - 1, 1, r.Height + 2), new Color(255, 255, 255, 120));
    }

    private string PopGroupTip(Country c, PoliticsSystem.PopGroupRow row)
    {
        var P = G.Politics;
        string tip = $"{row.Name} · {People(row.Size)} in {row.States} state(s)\nWants: {ObjectiveText(row.Objective)}. {ObjectiveTip(row.Objective)}\n"
            + $"Opinion {row.Opinion:+0;-0;0} · grievance {row.Grievance:0} · radicalism {row.Radicalism:0}";
        if (row.PreferredParty.Length > 0)
            tip += $"\nLeans to: {P.PartyDisplayName(c, row.PreferredParty)}" + (P.IsPartyBanned(c, row.PreferredParty) ? " (banned: they back it in secret)" : "");
        if (row.Worst.Count > 0)
            tip += "\nLowest opinion: " + string.Join(", ", row.Worst.Select(w => $"{w.State.DisplayName} ({w.Opinion:+0;-0;0})"));
        return tip;
    }

    // ------------------------------------------------------------------------------------- People sub-tab
    private float PeopleBodyHeight() => 58 * S + 24 * S + 28 * S + PeoplePageSize * 36 * S + 34 * S + 24 * S + 3 * 20 * S;

    private void DrawPeopleTab(Country c, Rectangle r, float x, float y, float w)
    {
        var P = G.Politics;
        if (!P.PopsEnabled) { Gui.Label("The population feature is not active.", x + 2 * S, y, 14, Gui.TextDim); return; }
        var sum = P.PopSummaryOf(c);
        double stab = P.PopOpinionStability(c);
        double underground = sum.Underground.Values.Sum();
        var tiles = new (string Label, string Value, Color Tone, float Fill, string Tip)[]
        {
            ("OPINION", $"{sum.AverageOpinion:+0;-0;0}", sum.AverageOpinion >= 10 ? Gui.Good : sum.AverageOpinion > -10 ? Gui.Text : Gui.Bad, -1,
                $"Average opinion of the government, -100 to +100, weighted by people.\nAdds {stab:+0.0;-0.0;0} to stability."),
            ("UNREST", $"{sum.UnrestShare:P0}", sum.UnrestShare < 0.1 ? Gui.Good : sum.UnrestShare < 0.3 ? Gui.Gold : Gui.Bad, (float)sum.UnrestShare,
                "Share of people who protest or are radicalising."),
            ("UNDERGROUND", $"{underground:P0}", underground < 0.02 ? Gui.Text : Gui.Gold, (float)Math.Min(1, underground * 4),
                "People secretly backing banned parties. It feeds those movements' support even though the parties cannot stand."),
        };
        float gap = 4 * S, tileW = (w - 2 * gap) / tiles.Length, tileH = 46 * S;
        for (int i = 0; i < tiles.Length; i++)
        {
            var tile = new Rectangle(x + i * (tileW + gap), y, tileW, tileH);
            Gui.Card(tile, new Color(29, 31, 37, 255));
            Gui.LabelCentered(tiles[i].Value, new Rectangle(tile.X, tile.Y + 1 * S, tile.Width, 21 * S), 16, tiles[i].Tone, FontKind.Bold);
            Gui.LabelCentered(tiles[i].Label, new Rectangle(tile.X, tile.Y + 20 * S, tile.Width, 18 * S), 14, Gui.TextDim, FontKind.Bold);
            if (tiles[i].Fill >= 0) Gui.Bar(new Rectangle(tile.X + 8 * S, tile.Bottom() - 6 * S, tile.Width - 16 * S, 3 * S), tiles[i].Fill, tiles[i].Tone);
            if (Gui.Hover(tile)) Gui.Tooltip(tiles[i].Tip);
        }
        y += tileH + 12 * S;

        var rows = P.PopGroupRows(c);
        rows = _peopleSort switch
        {
            1 => rows.OrderBy(q => q.Opinion).ToList(),
            2 => rows.OrderByDescending(q => q.Radicalism).ToList(),
            _ => rows,
        };
        PoliticsSection("Pop groups", "ui_pop_groups", x, y, w);
        y += 24 * S;
        float sw = (w - 8 * S) / 3;
        string[] sortNames = { "By size", "By opinion", "By radicalism" };
        for (int i = 0; i < 3; i++)
            if (Gui.Button(new Rectangle(x + i * (sw + 4 * S), y, sw, 24 * S), sortNames[i], true, null, _peopleSort == i, 13)) { _peopleSort = i; _peoplePage = 0; }
        y += 28 * S;
        if (rows.Count == 0) { Gui.Label("No pop data yet. Moods appear after the first month.", x + 2 * S, y, 14, Gui.TextDim); return; }
        int pages = (rows.Count + PeoplePageSize - 1) / PeoplePageSize;
        _peoplePage = Math.Clamp(_peoplePage, 0, pages - 1);
        float rowH = 36 * S;
        foreach (var row in rows.Skip(_peoplePage * PeoplePageSize).Take(PeoplePageSize))
        {
            var rect = new Rectangle(x, y, w, rowH - 3 * S);
            Gui.Card(rect, new Color(29, 31, 37, 255));
            Gui.Label(Gui.Fit(row.Name, w * 0.46f, 14, FontKind.Bold), rect.X + 6 * S, rect.Y + 2 * S, 14, Gui.Text, FontKind.Bold);
            string right = $"{People(row.Size)} · {row.Reaction}";
            Gui.Label(Gui.Fit(right, w * 0.45f, 13), (rect.X + rect.Width) - Math.Min(w * 0.45f, Gui.Measure(right, 13).X) - 6 * S, rect.Y + 3 * S, 13, ReactionColor(row.Reaction));
            OpinionBar(new Rectangle(rect.X + 6 * S, rect.Bottom() - 9 * S, w * 0.4f, 5 * S), row.Opinion);
            Gui.Label(Gui.Fit(ObjectiveText(row.Objective), w * 0.45f, 12), rect.X + 6 * S + w * 0.4f + 8 * S, rect.Bottom() - 16 * S, 12, Gui.TextDim);
            if (Gui.Hover(rect)) Gui.Tooltip(PopGroupTip(c, row));
            y += rowH;
        }
        y += 2 * S;
        if (pages > 1)
        {
            if (Gui.Button(new Rectangle(x, y, 80 * S, 26 * S), "Prev", _peoplePage > 0, null, false, 14)) _peoplePage--;
            if (Gui.Button(new Rectangle(x + w - 80 * S, y, 80 * S, 26 * S), "Next", _peoplePage < pages - 1, null, false, 14)) _peoplePage++;
            Gui.LabelCentered($"{_peoplePage + 1}/{pages}", new Rectangle(x + 80 * S, y, w - 160 * S, 26 * S), 14, Gui.TextDim);
        }
        y += 32 * S;

        // what the people's lean does to the vote
        var p = P.Of(c);
        var keys = p.PartySeats.Where(kv => kv.Value > 0 && !PoliticsSystem.IsMinorityParty(kv.Key)).Select(kv => kv.Key).ToList();
        var mult = P.PopVoteMultipliers(c, keys);
        PoliticsSection("Vote shift from pop lean", "ui_vote", x, y, w);
        y += 24 * S;
        foreach (var kv in mult.OrderByDescending(m => m.Value).Take(3))
        {
            double d = kv.Value - 1;
            Gui.Label(Gui.Fit(P.PartyDisplayName(c, kv.Key), w * 0.6f, 13), x + 2 * S, y, 13, Gui.Text);
            string t = $"{d:+0%;-0%;0%}";
            Gui.Label(t, x + w - Gui.Measure(t, 13).X - 4 * S, y, 13, DeltaColor(d));
            if (Gui.Hover(new Rectangle(x, y, w, 20 * S))) Gui.Tooltip("How much the people's culture, religion, class and mood move this party's vote at the next election, before it is renormalised.");
            y += 20 * S;
        }
    }

    // ------------------------------------------------------------------------------------- location panel fold
    /// <summary>Politics of the selected province's state: its pop groups' moods and the seats it sends to parliament.</summary>
    private float DrawStatePolitics(Location l, State st, float x, float y, float w)
    {
        var owner = st.Owner;
        var P = G.Politics;
        if (owner == null || !P.PopsEnabled || !G.Population.HasPops(st)) return y;
        var moods = P.MoodsOf(st);
        if (moods == null || moods.Count == 0) { Gui.Label("Moods appear after the first month.", x, y, 13, Gui.TextDim); return y + 20 * S; }

        if (!SeatsTabs) _stateMoodSub = 0;
        float sw = (w - 4 * S) / 2;
        for (int i = 0; i < 2 && SeatsTabs; i++)
            if (Gui.Button(new Rectangle(x + i * (sw + 4 * S), y, sw, 24 * S), i == 0 ? "Groups" : "Seats", true,
                    i == 0 ? $"How the people of {st.DisplayName} (with {l.DisplayName}) see the government." : $"The seats {st.DisplayName} sends to parliament.", _stateMoodSub == i, 13))
                _stateMoodSub = i;
        if (SeatsTabs) y += 30 * S;

        if (_stateMoodSub == 1)
        {
            var seats = P.ParliamentOf(owner).Seats.Where(s => s.State == st).OrderBy(s => s.Party, StringComparer.OrdinalIgnoreCase).ToList();
            if (seats.Count == 0) { Gui.Label("This state sends no seats.", x, y, 13, Gui.TextDim); return y + 20 * S; }
            var op = P.Of(owner);
            foreach (var seat in seats.Take(8))
            {
                var rect = new Rectangle(x, y, w, 20 * S);
                bool gov = op.GovernmentOrganizations.Contains(seat.Party, StringComparer.OrdinalIgnoreCase);
                Raylib.DrawCircleV(new Vector2(x + 7 * S, y + 10 * S), 4 * S, C(P.PartyColor(seat.Party)));
                Gui.Label(Gui.Fit(seat.HolderName ?? "Unnamed", w * 0.5f, 13), x + 18 * S, y + 1 * S, 13, gov ? Gui.Good : Gui.Text);
                string pn = Gui.Fit(P.PartyDisplayName(owner, seat.Party), w * 0.4f, 12);
                Gui.Label(pn, x + w - Gui.Measure(pn, 12).X - 4 * S, y + 3 * S, 12, Gui.TextDim);
                if (Gui.Hover(rect)) Gui.Tooltip(P.SeatText(owner, seat) + $"\n{(gov ? "Government" : "Opposition")}");
                y += 20 * S;
            }
            if (seats.Count > 8) { Gui.Label($"+{seats.Count - 8} more", x, y, 12, Gui.TextDim); y += 18 * S; }
            return y + 2 * S;
        }

        var groups = P.PopGroupRows(owner, new[] { st });
        double avg = groups.Count > 0 ? groups.Sum(g => g.Opinion * g.Size) / groups.Sum(g => g.Size) : 0;
        Gui.Label($"Opinion here {avg:+0;-0;0}", x, y, 14, avg >= 10 ? Gui.Good : avg > -10 ? Gui.Text : Gui.Bad, FontKind.Bold);
        OpinionBar(new Rectangle(x + 120 * S, y + 7 * S, w - 124 * S, 6 * S), avg);
        y += 22 * S;
        foreach (var g in groups.Take(5))
        {
            var rect = new Rectangle(x, y, w, 34 * S);
            Gui.Label(Gui.Fit(g.Name, w * 0.6f, 13, FontKind.Bold), x, y, 13, Gui.Text, FontKind.Bold);
            string rt = $"{g.Reaction} ({g.Opinion:+0;-0;0})";
            Gui.Label(rt, x + w - Gui.Measure(rt, 13).X, y, 13, ReactionColor(g.Reaction));
            OpinionBar(new Rectangle(x, y + 19 * S, w * 0.4f, 5 * S), g.Opinion);
            Gui.Label(Gui.Fit(ObjectiveText(g.Objective), w * 0.55f, 12), x + w * 0.4f + 8 * S, y + 16 * S, 12, Gui.TextDim);
            if (Gui.Hover(rect)) Gui.Tooltip(PopGroupTip(owner, g));
            y += 34 * S;
        }
        return y + 2 * S;
    }
}
