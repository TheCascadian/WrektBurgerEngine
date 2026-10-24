using System.Numerics;
using System.Collections.Immutable;
using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Simulation;
using Raylib_cs;

namespace Burger.Runtime;

public sealed partial class Hud
{
    /// <summary>Area of a panel on the left, next to the left menu bar.</summary>
    private Rectangle LeftRect(float height)
    {
        float x = (G.World.Player != null && !Ui.EditorActive ? LeftMenuW + 10 * S : 8 * S);
        float top = SubNavH;
        return new(x, TopH + 8 * S + top, Gui.LeftPanelWidth * S, Math.Min(height, Raylib.GetScreenHeight() - TopH - 20 * S - top));
    }

    private string CountryLink(Country? c) => c?.Name ?? "-";

    private static string Abbrev(string stat)
    {
        var p = Localisation.GetOrPretty(stat);
        return p.Length <= 3 ? p : p[..3];
    }

    private readonly HashSet<string> _locOpen = new();

    /// <summary>Good is green, bad is red, nothing is dim (the colours of the tooltips).</summary>
    private static Color DeltaColor(double delta, double eps = 0.005) => delta > eps ? Gui.Good : delta < -eps ? Gui.Bad : Gui.TextDim;

    /// <summary>A collapsible section row: chevron, title and a one-line summary when closed. Returns whether it is open.</summary>
    /// <summary>Draws the building's own glyph (bld_ prefix keeps shared tooltip icons untouched); locked uses the greyed variant.</summary>
    private static bool BuildingIcon(string key, Rectangle dst, bool locked)
        => (locked && Gui.Icon($"bld_{key}_locked", dst)) || Gui.Icon($"bld_{key}", dst, locked ? new Color(150, 150, 150, 255) : null);

    public void UnfoldLocation(string key) => _locOpen.Add(key);

    private bool Collapse(string key, string title, string summary, Color summaryCol, float x, float y, float w)
    {
        bool open = _locOpen.Contains(key);
        var row = new Rectangle(x, y, w, 24 * S);
        bool hover = Gui.Hover(row);
        if (hover) Raylib.DrawRectangleRec(row, new Color(255, 255, 255, 14));
        float cx = x + 8 * S, cy = y + 12 * S, cs = 4 * S;
        if (open) Raylib.DrawTriangle(new Vector2(cx - cs, cy - cs / 2), new Vector2(cx, cy + cs / 2 + 1), new Vector2(cx + cs, cy - cs / 2), Gui.Gold);
        else Raylib.DrawTriangle(new Vector2(cx - cs / 2, cy - cs), new Vector2(cx - cs / 2, cy + cs), new Vector2(cx + cs / 2 + 1, cy), Gui.Gold);
        Gui.Label(title, x + 20 * S, y + 2 * S, 15, Gui.Gold, FontKind.Header);
        if (!open && summary.Length > 0)
        {
            float tw = Gui.Measure(title, 15, FontKind.Header).X;
            string sm = Gui.Fit(summary, w - tw - 36 * S, 13);
            Gui.Label(sm, x + w - Gui.Measure(sm, 13).X - 6 * S, y + 4 * S, 13, summaryCol);
        }
        if (Gui.Clicked(row)) { if (open) _locOpen.Remove(key); else _locOpen.Add(key); }
        return open;
    }

    private void DrawLocationPanel()
    {
        var l = Ui.SelectedLocation;
        if (l == null) { Ui.Left = LeftPanel.None; return; }
        var locationSnapshot = _app.Snapshot.Locations.FirstOrDefault(x => x.Index == l.Index);
        var r = FitRect("loc");
        bool unknown = locationSnapshot.Index == l.Index ? !locationSnapshot.IsCharted : !G.Exploration.IsCharted(l);
        Gui.Panel(r, unknown ? "Terra incognita" : l.DisplayName);
        if (Gui.CloseButton(r)) { CloseLeft(); return; }
        float x = r.X + 12 * S, y = r.Y + 36 * S, lh = 22 * S, w = r.Width - 24 * S;
        if (unknown)
        {
            Gui.Wrapped(l.IsWater ? "Uncharted waters. No ship of ours has sailed here." : "A land unexplored by your people. Nothing is known of its soil, its peoples or its rulers.", x, y, w, 15, Gui.Text);
            y += Gui.WrappedHeight(l.IsWater ? "Uncharted waters. No ship of ours has sailed here." : "A land unexplored by your people. Nothing is known of its soil, its peoples or its rulers.", w, 15) + 12 * S;
            Gui.Wrapped("Send a fleet along the coast or march an army to its edge to chart it.", x, y, w, 14, Gui.TextDim);
            return;
        }
        void Row(string k, string v, Color? col = null, string? tip = null)
        {
            Gui.Label(k, x, y, 15, Gui.TextDim);
            Gui.Label(Gui.Fit(v, w - 110 * S, 15), x + 100 * S, y, 15, col ?? Gui.Text);
            if (tip != null && Gui.Hover(new Rectangle(x, y, w, lh))) Gui.Tooltip(tip);
            y += lh;
        }
        // the panel is cut into cells: small label over a value, several to a row
        const float CellH = 38;
        float gap = 8 * S;
        Rectangle CellAt(int i, int n) { float cw = (w - gap * (n - 1)) / n; return new Rectangle(x + i * (cw + gap), y, cw, CellH * S); }
        void Cell(int i, int n, string k, string v, Color? col = null, string? tip = null, Country? flag = null, string? delta = null, Color? deltaCol = null)
        {
            var c = CellAt(i, n);
            Raylib.DrawRectangleRounded(c, 0.12f, 4, new Color(255, 255, 255, 10));
            Gui.Label(k, c.X + 8 * S, c.Y + 3 * S, 12, Gui.TextDim);
            float tx = c.X + 8 * S;
            if (flag != null) tx += FlagChip(flag, tx, c.Y + 19 * S, 14 * S) + 2 * S;
            string txt = Gui.Fit(v, c.Width - (tx - c.X) - 6 * S - (delta != null ? 44 * S : 0), 15);
            Gui.Label(txt, tx, c.Y + 18 * S, 15, col ?? Gui.Text, FontKind.Bold);
            if (delta != null) Gui.Label(delta, tx + Gui.Measure(txt, 15, FontKind.Bold).X + 6 * S, c.Y + 20 * S, 13, deltaCol ?? Gui.TextDim);
            if (Gui.Hover(c)) Gui.Tooltip(tip ?? $"{k}\n{v}");
        }
        void CellsEnd() => y += (CellH + 6) * S;

        var st = l.State;
        var stateSnapshot = locationSnapshot.StateKey is { } stateKey
            ? _app.Snapshot.States.FirstOrDefault(s => s.Key == stateKey)
            : default;
        string terr = locationSnapshot.TerrainName + (locationSnapshot.IsCoastal ? " (coastal)" : "");
        if (locationSnapshot.IsWater) { Cell(0, 2, "Terrain", terr); Cell(1, 2, "Type", locationSnapshot.IsLake ? "Lake" : "Sea"); CellsEnd(); FitEnd("loc", r, y); return; }
        Cell(0, 2, "Terrain", terr);
        Cell(1, 2, locationSnapshot.ContinentIsOcean ? "Ocean" : "Continent", locationSnapshot.ContinentName ?? "-");
        CellsEnd();
        if (locationSnapshot.StateName != null)
        {
            Cell(0, 1, "State", locationSnapshot.StateName + (locationSnapshot.StateLocationCount > 1 ? $"  ·  {locationSnapshot.StateLocationCount} locations" : ""));
            CellsEnd();
        }
        bool split = locationSnapshot.ControllerTag != locationSnapshot.OwnerTag;
        Cell(0, split ? 2 : 1, "Owner", locationSnapshot.OwnerName ?? "-", null, null, l.Owner);
        if (split) Cell(1, 2, "Controller", locationSnapshot.ControllerName ?? "-", Gui.Bad, null, l.Controller);
        CellsEnd();
        if (st != null && l.Owner != null) y += DrawStateRulers(st, x, y, w);
        if (st != null)
        {
            // tax and manpower against the average state of the owner: above is green, below is red
            var peers = _app.Snapshot.States.Where(s => s.OwnerTag == locationSnapshot.OwnerTag && s.Key != stateSnapshot.Key).ToList();
            double avgTax = peers.Count > 0 ? peers.Average(s => s.Tax) : stateSnapshot.Tax;
            double avgMan = peers.Count > 0 ? peers.Average(s => s.Manpower) : stateSnapshot.Manpower;
            void Stat2(int i, string k, double v, double avg, string text)
            {
                double rel = avg > 0 ? v / avg - 1 : 0;
                bool show = peers.Count > 0 && Math.Abs(rel) >= 0.005;
                Cell(i, 2, k, text, null, $"{k}\n{text} a month\nOwner's average state: {avg:0.##}" + (peers.Count > 0 ? $"\n{(rel >= 0 ? "+" : "")}{rel:P0} against the average" : ""),
                    null, show ? $"{(rel >= 0 ? "+" : "")}{rel:P0}" : null, DeltaColor(rel));
            }
            Stat2(0, "Tax", stateSnapshot.Tax, avgTax, $"{stateSnapshot.Tax:0.##}");
            Stat2(1, "Manpower", stateSnapshot.Manpower, avgMan, Num(stateSnapshot.Manpower));
            CellsEnd();
            y += 2 * S;
            if (G.Development.Enabled) y = DrawStateDevelopment(st, x, y, w);
            if (G.Population.Enabled)
            {
                var P = G.Population;
                var sp = P.Of(st);
                double statePopulation = stateSnapshot.Key == st.Key ? stateSnapshot.PopulationTotal : sp.Total;
                double stateGrowth = stateSnapshot.Key == st.Key ? stateSnapshot.PopulationGrowth : sp.LastGrowth;
                string sum = $"{People(statePopulation)}  {(stateGrowth >= 0 ? "+" : "-")}{People(Math.Abs(stateGrowth))}/mo";
                bool open = Collapse("people", "People", sum, DeltaColor(stateGrowth, 0.5), x, y, w);
                y += 26 * S;
                if (open) y = DrawStatePopulation(st, x, y, w, false) + 4 * S;
            }
            if (st.Owner != null && G.Politics.PopsEnabled && G.Population.HasPops(st))
            {
                var stMoods = stateSnapshot.Key == st.Key ? stateSnapshot.Moods : System.Collections.Immutable.ImmutableArray<RenderPopMood>.Empty;
                if (stMoods.Length == 0) stMoods = G.Politics.PopGroupRows(st.Owner, new[] { st }).Select(g => new RenderPopMood(g.Name, g.Size, g.Reaction, g.Opinion, g.Objective, g.Grievance, g.Radicalism, g.PreferredParty)).ToImmutableArray();
                double stAvg = stMoods.Length > 0 ? stMoods.Sum(g => g.Opinion * g.Size) / stMoods.Sum(g => g.Size) : 0;
                bool open = Collapse("politics_here", "Politics", stMoods.Length > 0 ? $"opinion {stAvg:+0;-0;0}" : "", stAvg >= 10 ? Gui.Good : stAvg > -10 ? Gui.TextDim : Gui.Bad, x, y, w);
                y += 26 * S;
                if (open) y = DrawStatePolitics(l, st, x, y, w) + 4 * S;
            }
            if (stateSnapshot.Buildings.Length > 0)
            {
                int levels = stateSnapshot.Buildings.Sum(b => b.Level);
                bool open = Collapse("buildings", "Buildings", $"{levels} levels", Gui.TextDim, x, y, w);
                y += 26 * S;
                if (open)
                {
                    float tile = 46 * S, ih = 26 * S, cx = x + 4 * S, rowY = y;
                    foreach (var b in stateSnapshot.Buildings)
                    {
                        if (cx + tile > x + w && cx > x + 4 * S) { cx = x + 4 * S; rowY += 36 * S; }
                        string name = G.Data.BuildingTypes.TryGetValue(b.Key, out var bt) ? bt.Name : Localisation.GetOrPretty(b.Key);
                        double dmg = b.Damaged;
                        var cell = new Rectangle(cx, rowY, tile - 4 * S, 32 * S);
                        var ir = new Rectangle(cx, rowY + 3 * S, ih, ih);
                        if (!BuildingIcon(b.Key, ir, false))
                        {
                            Raylib.DrawRectangleRounded(ir, 0.25f, 4, new Color(70, 78, 92, 255));
                            string ini = name.Length > 0 ? name[..1].ToUpperInvariant() : "?";
                            var m = Gui.Measure(ini, 15);
                            Gui.Label(ini, ir.X + (ir.Width - m.X) / 2, ir.Y + (ir.Height - m.Y) / 2, 15, Gui.TextDim);
                        }
                        Gui.Label(b.Level.ToString(), cx + ih + 3 * S, rowY + 7 * S, 15, dmg >= 0.05 ? Gui.Bad : Gui.Text);
                        if (dmg >= 0.05) Raylib.DrawRectangleLinesEx(ir, 1, Gui.Bad);
                        if (Gui.Hover(cell))
                            Gui.Tooltip($"{name}\nLevel {b.Level}" + (dmg >= 0.05 ? $"\n{dmg:0.#} levels bombed" : ""));
                        cx += tile + 8 * S;
                    }
                    y = rowY + 36 * S + 4 * S;
                }
            }
            // everything else behind one fold
            var rivers = locationSnapshot.RiverBorders;
            var mods = locationSnapshot.Modifiers.Length + stateSnapshot.Modifiers.Length;
            bool details = Collapse("details", "Details", mods > 0 ? $"{mods} modifier(s)" : "", Gui.TextDim, x, y, w);
            y += 26 * S;
            if (details)
            {
                void Flags(string k, IEnumerable<Country> list, string tip)
                {
                    var arr = list.ToList();
                    Gui.Label(k, x, y, 15, Gui.TextDim);
                    if (arr.Count == 0) { Gui.Label("-", x + 100 * S, y, 15); y += lh; return; }
                    y += FlagRow(arr, x + 100 * S, y, w - 100 * S, new Color(0, 0, 0, 200), _ => tip) + 4 * S;
                }
                Flags("Cores", st.Cores, "Core: people here count as part of this nation");
                Flags("Claims", st.Claims, "Claim: may be taken by a peace deal");
                if (rivers > 0) Row("Rivers", $"{rivers} border(s) with a river");
                if (locationSnapshot.Fort > 0) Row("Fort", locationSnapshot.Fort.ToString());
                y += DrawModifierList(locationSnapshot.Modifiers, x, y, w);
                y += DrawModifierList(stateSnapshot.Modifiers, x, y, w);
                y += 4 * S;
            }
        }
        double siegeProgress = locationSnapshot.Index == l.Index ? locationSnapshot.SiegeProgress : l.SiegeProgress;
        string? siegerTag = locationSnapshot.Index == l.Index ? locationSnapshot.SiegerTag : l.Sieger?.Tag;
        if (siegeProgress > 0) Row("Siege", $"{siegeProgress:0} h by {siegerTag}", Gui.Bad);
        if (l.Owner != null && Gui.Button(new Rectangle(x, y, 170 * S, 28 * S), "Open " + l.Owner.Name, true, "Open the owner's country panel"))
        {
            Ui.SelectedCountry = l.Owner;
            Ui.Left = LeftPanel.Country;
            return;
        }
        y += 36 * S;

        if (locationSnapshot.DivisionCount > 0)
        {
            Gui.Section("Units here", x, y, w);
            y += 30 * S;
            var divisionsHere = _app.Snapshot.Divisions.Where(d => d.LocationKey == locationSnapshot.Key).ToList();
            foreach (var g in divisionsHere.GroupBy(d => d.OwnerTag, StringComparer.OrdinalIgnoreCase))
            {
                var owner = G.World.Countries.FirstOrDefault(c => c.Tag.Equals(g.Key, StringComparison.OrdinalIgnoreCase));
                float fx = owner != null ? FlagChip(owner, x, y + 1 * S, 16 * S) : 0;
                var ownerSnapshot = _app.Snapshot.Countries.FirstOrDefault(c => c.Tag.Equals(g.Key, StringComparison.OrdinalIgnoreCase));
                string ownerName = ownerSnapshot.Tag != null ? ownerSnapshot.Name : g.Key;
                Gui.Label($"{g.Count()} division(s) · {ownerName}", x + fx, y, 15);
                y += lh;
            }
        }

        var player = G.World.Player;
        bool recruit = player != null && Has(FeatureSet.Military) && l.Owner == player && l.Controller == player;
        FitEnd("loc", r, y + (recruit ? 40 * S : 0));
        if (recruit && Gui.Button(new Rectangle(x, y + 6 * S, w, 28 * S), "Recruit divisions...", true, "Open the recruit menu (R) - pick a template, then click your locations", false, 15))
            Ui.Left = LeftPanel.Recruit;
    }

    // ===============================================================================================
    // Country / diplomacy panel
    // ===============================================================================================
    private void DrawCountryPanel()
    {
        var c = Ui.SelectedCountry;
        if (c == null) { Ui.Left = LeftPanel.None; return; }
        var countrySnapshot = _app.Snapshot.Countries.FirstOrDefault(x => x.Tag.Equals(c.Tag, StringComparison.OrdinalIgnoreCase));
        var r = LeftRect(Raylib.GetScreenHeight() - TopH - 20 * S);
        string snapshotCountryName = countrySnapshot.Name ?? "";
        string snapshotCountryTag = countrySnapshot.Tag ?? "";
        Gui.Panel(r, snapshotCountryName.Length > 0 ? snapshotCountryName : c.Name);
        if (Gui.CloseButton(r)) { CloseLeft(); return; }
        float x = r.X + 12 * S, y = r.Y + 36 * S, lh = 21 * S;
        Gui.Flag(c, new Rectangle(x, y, 84 * S, 56 * S));
        float tx = x + 94 * S;
        Gui.Label(snapshotCountryTag.Length > 0 ? snapshotCountryTag : c.Tag, tx, y, 15, Gui.TextDim, FontKind.Bold);
        string govLine = (countrySnapshot.Government.Length > 0 ? Localisation.GetOrPretty(countrySnapshot.Government) : "") + (countrySnapshot.PrimaryContinentName is { } pcont ? (countrySnapshot.Government.Length > 0 ? "  ·  " : "") + pcont : "");
        if (govLine.Length > 0) Gui.Label(Gui.Fit(govLine, r.X + r.Width - tx - 12 * S, 15), tx, y + 18 * S, 15);
        float hx = tx;
        hx += IconStat("province", countrySnapshot.OwnedStateCount.ToString(), $"{countrySnapshot.OwnedStateCount} states", hx, y + 36 * S);
        hx += IconStat("army", countrySnapshot.DivisionCount.ToString(), "Divisions", hx, y + 36 * S);
        if (Gui.Hover(new Rectangle(x, y, 84 * S, 56 * S))) Gui.Tooltip($"{c.Name} ({c.Tag})");
        y += 64 * S;
        y = DrawCountryTabs(c, x, y, r.Width - 24 * S);
        if (ScRelationsOn && _countryTab == (G.Politics.Enabled ? 3 : 2)) { DrawCountryRelations(c, r, x, y, r.Width - 24 * S); return; }
        if (_countryTab == 1) { DrawCountryPeople(c, r, x, y, r.Width - 24 * S); return; }
        if (_countryTab == 2) { DrawCountryFactions(c, r, x, y, r.Width - 24 * S); return; }

        if (countrySnapshot.Ruler is { } rulerSnapshot)
        {
            Gui.Label($"Ruler: {rulerSnapshot.Name} ({rulerSnapshot.Age})", x, y, 16);
            y += lh;
            Gui.Label(string.Join("  ", rulerSnapshot.Stats.Select(s => $"{Abbrev(s.Key)} {s.Value:0}")), x, y, 14, Gui.TextDim);
            y += lh;
            if (rulerSnapshot.Traits.Length > 0)
            {
                Gui.Label(string.Join(", ", rulerSnapshot.Traits), x, y, 14, Gui.TextDim);
                y += lh;
            }
            if (countrySnapshot.Heir is { } heirSnapshot) { Gui.Label($"Heir: {heirSnapshot.Name} ({heirSnapshot.Age})", x, y, 15, Gui.TextDim); y += lh; }
        }

        foreach (var resource in countrySnapshot.Resources)
        {
            Gui.Label($"{Localisation.Get(resource.Name)}: {resource.Value:0.#}", x, y, 15, Gui.TextDim);
            y += lh;
        }
        y += DrawModifierList(countrySnapshot.Modifiers, x, y, r.Width - 24 * S);
        var player = G.World.Player;
        var R = G.World.Relations;
        y += 4 * S;
        if (player != null && player != c && Has(FeatureSet.Diplomacy))
        {
            var relationSnapshot = _app.Snapshot.DiplomacyRelations.FirstOrDefault(relation =>
                relation.FromTag.Equals(player.Tag, StringComparison.OrdinalIgnoreCase)
                && relation.ToTag.Equals(c.Tag, StringComparison.OrdinalIgnoreCase));
            bool hasRelationSnapshot = relationSnapshot.FromTag != null;
            double theirs = hasRelationSnapshot ? relationSnapshot.Opinion : G.Diplomacy.Opinion(c, player);
            double ours = hasRelationSnapshot ? relationSnapshot.ReverseOpinion : G.Diplomacy.Opinion(player, c);
            var opR = new Rectangle(x, y, r.Width - 24 * S, lh);
            Gui.Label($"Opinion of us: {theirs:+0;-0;0}   Our opinion: {ours:+0;-0;0}", x, y, 15, theirs >= 0 ? Gui.Good : Gui.Bad);
            if (Gui.Hover(opR))
            {
                var parts = hasRelationSnapshot
                    ? new List<string> { $"Base: {relationSnapshot.BaseOpinion:+0;-0;0}" }
                    : new List<string> { $"Base: {G.Rules.Diplomacy.BaseOpinion(G, c, player):+0;-0;0}" };
                void Part(string name, double v) { if (Math.Abs(v) >= 0.5) parts.Add($"{name}: {v:+0;-0;0}"); }
                Part("Ideology", hasRelationSnapshot ? relationSnapshot.IdeologyOpinion : G.Politics.IdeologyOpinion(c, player));
                Part("Religion", hasRelationSnapshot ? relationSnapshot.ReligionOpinion : G.Population.ReligionOpinion(c, player));
                Part("Our colonies", hasRelationSnapshot ? relationSnapshot.ColonialOpinion : G.Population.ColonialOpinion(c, player));
                if (hasRelationSnapshot) parts.AddRange(relationSnapshot.OpinionModifiers.Select(m => $"{m.Name}: {m.Value:+0;-0;0}"));
                else if (R.Opinions.TryGetValue((c, player), out var list)) parts.AddRange(list.Select(m => $"{m.Name}: {m.Value:+0;-0;0}"));
                Gui.Tooltip(string.Join("\n", parts));
            }
            y += lh;
            var rel = new List<string>();
            if (hasRelationSnapshot && relationSnapshot.AtWar || !hasRelationSnapshot && G.World.AtWar(player, c)) rel.Add("AT WAR");
            if (hasRelationSnapshot && relationSnapshot.Allied || !hasRelationSnapshot && R.AreAllied(player, c)) rel.Add("Allied");
            if (hasRelationSnapshot && relationSnapshot.FromIsOverlord || !hasRelationSnapshot && R.OverlordOf(c) == player) rel.Add("Our subject");
            if (hasRelationSnapshot && relationSnapshot.ToIsOverlord || !hasRelationSnapshot && R.OverlordOf(player) == c) rel.Add("Our overlord");
            if (hasRelationSnapshot && relationSnapshot.FromHasAccess || !hasRelationSnapshot && R.HasAccess(player, c)) rel.Add("We have access");
            if (hasRelationSnapshot && relationSnapshot.ToHasAccess || !hasRelationSnapshot && R.HasAccess(c, player)) rel.Add("They have access");
            if (hasRelationSnapshot && relationSnapshot.FromGuarantees || !hasRelationSnapshot && R.IsGuaranteeing(player, c)) rel.Add("We guarantee them");
            if (hasRelationSnapshot && relationSnapshot.ToGuarantees || !hasRelationSnapshot && R.IsGuaranteeing(c, player)) rel.Add("They guarantee us");
            if (hasRelationSnapshot && relationSnapshot.HasTruce || !hasRelationSnapshot && R.HasTruce(player, c, G.World.Date)) rel.Add("Truce");
            if (hasRelationSnapshot && relationSnapshot.HasPact || !hasRelationSnapshot && R.HasPact(player, c, G.World.Date)) rel.Add("Non-aggression pact");
            Gui.Label(Gui.Fit(rel.Count == 0 ? "No relations" : string.Join(", ", rel), r.Width - 24 * S, 15), x, y, 15, rel.Contains("AT WAR") ? Gui.Bad : Gui.Text);
            y += lh;
            // subjects: liberty desire; anyone: war exhaustion and the casus belli we have
            if (R.OverlordOf(c) != null || R.OverlordOf(player) == c)
            {
                var sub = R.OverlordOf(c) != null ? c : player;
                double ld = hasRelationSnapshot ? relationSnapshot.LibertyDesire : G.Diplomacy.LibertyDesire(sub);
                var lr = new Rectangle(x, y, r.Width - 24 * S, lh);
                Gui.Label($"Liberty desire ({sub.Tag}): {ld:0}%", x, y, 15, ld >= 50 ? Gui.Bad : Gui.Text);
                if (Gui.Hover(lr)) Gui.Tooltip("How much the subject wants to be free (0-100): its strength and size compared to the overlord, its opinion of it, a different ideology or culture, the overlord losing a war and the tribute.\nAt 50 it no longer joins its overlord's wars and may fight a War of Independence.");
                y += lh;
            }
            double warExhaustion = hasRelationSnapshot ? relationSnapshot.WarExhaustion : G.Diplomacy.WarExhaustion(c);
            if (warExhaustion > 0.05) { Gui.Label($"War exhaustion: {warExhaustion:0.0} / {G.Diplomacy.MaxWarExhaustion:0}", x, y, 15, Gui.TextDim); y += lh; }
            var cbs = G.Diplomacy.ValidCasusBelli(player, c).Where(cb => cb.WarGoal != WarGoalType.Conquest).Select(cb => Localisation.Get(cb.Name)).ToList();
            if (cbs.Count > 0) { Gui.Label(Gui.Fit("Our casus belli: " + string.Join(", ", cbs), r.Width - 24 * S, 14), x, y, 14, Gui.Gold); y += lh; }
        }
        else if (player == c && Has(FeatureSet.Diplomacy))
        {
            if (Has(FeatureSet.Economy))
            {
                var playerSnapshot = _app.Snapshot.Player;
                double gold = playerSnapshot?.Gold ?? c.Gold;
                double manpower = playerSnapshot?.Manpower ?? c.Manpower;
                Gui.Label($"Gold {gold:0}   Manpower {manpower:0}", x, y, 15);
                y += lh;
            }
            Gui.Label($"Diplomatic relations: {countrySnapshot.DiplomaticRelationsUsed}/{countrySnapshot.DiplomaticRelationsMax}", x, y, 15, Gui.TextDim);
            y += lh;
            var allies = R.AlliesOf(c).ToList();
            if (allies.Count > 0) { Gui.Label("Allies", x, y + 1 * S, 14, AllyBlue); y += FlagRow(allies, x + 104 * S, y, r.Width - 24 * S - 104 * S, AllyBlue); }
            var subjects = R.SubjectsOf(c).ToList();
            if (subjects.Count > 0) { Gui.Label(Gui.Fit("Subjects: " + string.Join(", ", subjects.Select(a => $"{a.Tag} ({G.Diplomacy.LibertyDesire(a):0}% liberty)")), r.Width - 24 * S, 15), x, y, 15); y += lh; }
            var selfRelation = _app.Snapshot.DiplomacyRelations.FirstOrDefault(r => r.FromTag.Equals(c.Tag, StringComparison.OrdinalIgnoreCase) && r.ToTag.Equals(c.Tag, StringComparison.OrdinalIgnoreCase));
            if (selfRelation.WarExhaustion > 0.05) { Gui.Label($"War exhaustion: {selfRelation.WarExhaustion:0.0} / {G.Diplomacy.MaxWarExhaustion:0}", x, y, 15, selfRelation.WarExhaustion >= 10 ? Gui.Bad : Gui.TextDim); y += lh; }
        }

        if (G.Politics.Enabled) y = DrawCountryPolitics(c, x, y + 6 * S, r.Width - 24 * S);
        if (Has(FeatureSet.Diplomacy)) y = DrawCountryRelations(c, x, y + 6 * S, r.Width - 24 * S);
        if (player != null && player != c && Has(FeatureSet.LendLease) && Has(FeatureSet.Military)) y = DrawSupportSummary(player, c, x, y, r.Width - 24 * S);
        if (player == null || player == c || !Has(FeatureSet.Diplomacy)) return;
        y += 8 * S;
        Gui.Section("Diplomatic actions", x, y, r.Width - 24 * S);
        y += 26 * S;
        if (G.World.AtWar(player, c))
        {
            var war = G.World.WarBetween(player, c)!;
            if (Gui.Button(new Rectangle(x, y, r.Width - 24 * S, 28 * S), "Negotiate peace", true, $"War score: {war.ScoreFor(player):0}"))
                OpenPeace(war, war.EnemyLeaderOf(player));
            y += 32 * S;
        }
        var actions = G.Diplomacy.ActionsFor(ActionTarget.Country).Where(a => a.Menu && G.Diplomacy.IsVisible(a, player, c)).ToList();
        float listH = r.Y + r.Height - y - 8 * S;
        float rowH = 30 * S;
        float top = Gui.BeginScroll("diplo", new Rectangle(x, y, r.Width - 20 * S, listH), actions.Count * rowH);
        float ry = top;
        foreach (var a in actions)
        {
            var reqs = G.Diplomacy.Requirements(a, player, c);
            bool can = reqs.All(q => q.Ok);
            string tip = Localisation.Get(a.Desc);
            if (reqs.Count > 0) tip += (tip.Length > 0 ? "\n\n" : "") + string.Join("\n", reqs.Select(q => (q.Ok ? "[OK] " : "[X] ") + q.Text));
            if (a.RequiresAcceptance)
            {
                double acc = G.Diplomacy.Acceptance(a, player, c);
                tip += $"\n\nThey would {(acc > 0 ? "ACCEPT" : "DECLINE")} (score {acc:0})";
            }
            var effects = G.Script.Describe(a.OnAccept, G.Diplomacy.ActionContext(a, player, c));
            if (effects.Count > 0) tip += "\n\nEffects:\n" + string.Join("\n", effects);
            if (a.Dialog != null) tip += "\n\nOpens a window to choose the details.";
            if (Gui.Button(new Rectangle(x, ry, r.Width - 30 * S, rowH - 4 * S), Localisation.Get(a.Name) + (a.Dialog != null ? "..." : ""), can, tip, false, 16))
            {
                if (a.Dialog != null) OpenSupportDialog(a.Dialog, c);
                else
                {
                    string result = G.Diplomacy.Use(a, player, c);
                    Ui.SetStatus($"{Localisation.Get(a.Name)}: {result}");
                }
            }
            ry += rowH;
        }
        Gui.EndScroll();
    }

    /// <summary>The country's own relations (also shown on the map: allies blue, enemies red). Returns the new y.</summary>
    private float DrawCountryRelations(Country c, float x, float y, float w)
    {
        var countrySnapshot = _app.Snapshot.Countries.FirstOrDefault(q => q.Tag.Equals(c.Tag, StringComparison.OrdinalIgnoreCase));
        var lines = new[]
        {
            (1, "At war with", Gui.Bad), (2, "Allies", new Color(110, 160, 245, 255)),
            (3, "Subjects", new Color(140, 200, 235, 255)), (4, "Overlord", new Color(140, 200, 235, 255)),
            (5, "Guarantees", Gui.Text), (6, "Guaranteed by", Gui.Text), (7, "Access through", Gui.TextDim)
        };
        if (countrySnapshot.Relations.IsDefaultOrEmpty)
        {
            Gui.Label($"{c.Name} has no allies, subjects or wars.", x, y, 14, Gui.TextDim);
            return y + 22 * S;
        }
        Gui.Section("Relations", x, y, w);
        y += 26 * S;
        foreach (var (kind, label, col) in lines)
        {
            var relations = countrySnapshot.Relations.Where(q => q.Kind == kind).ToList();
            var list = relations.Select(q => G.World.Countries.FirstOrDefault(country => country.Tag.Equals(q.Tag, StringComparison.OrdinalIgnoreCase))).Where(q => q != null).Cast<Country>().ToList();
            if (list.Count == 0) continue;
            Gui.Label(label, x, y + 1 * S, 13, col);
            y += FlagRow(list, x + 104 * S, y, w - 104 * S, col, o =>
            {
                var relation = relations.FirstOrDefault(q => q.Tag.Equals(o.Tag, StringComparison.OrdinalIgnoreCase));
                return label is "Subjects" or "Overlord" ? $"{relation.LibertyDesire:0}% liberty desire" : null;
            }) + 2 * S;
        }
        if (Map.Mode == MapMode.Diplomacy && Map.DiplomacyFocus == c)
        {
            Gui.Label("Map: green = " + c.Name + ", blue = allies, red = enemies, light blue = subjects", x, y, 12, Gui.TextDim);
            y += 18 * S;
        }
        return y + 4 * S;
    }

    /// <summary>A short line about our volunteers and lend-lease with this country (the actions open the windows).</summary>
    private float DrawSupportSummary(Country player, Country c, float x, float y, float w)
    {
        var sup = G.Support;
        int abroad = sup.VolunteersOf(player).Count(d => d.Owner == c);
        var deals = sup.Deals.Where(d => d.From == player && d.To == c).ToList();
        if (abroad == 0 && deals.Count == 0) return y;
        if (abroad > 0)
        {
            Gui.Label($"Our volunteers here: {abroad} division(s)", x, y + 3 * S, 14);
            if (Gui.Button(new Rectangle(x + w - 90 * S, y, 90 * S, 22 * S), "Recall", true, "Bring them home", false, 13))
                foreach (var d in sup.VolunteersOf(player).Where(d => d.Owner == c).ToList()) sup.Recall(d);
            y += 26 * S;
        }
        if (deals.Count > 0)
        {
            Gui.Label(Gui.Fit("Lend-lease: " + string.Join(", ", deals.Select(d => $"{d.Equipment.Name} {d.Share:P0}/month")), w, 14), x, y, 14, Gui.TextDim);
            y += 22 * S;
        }
        return y + 4 * S;
    }
}
