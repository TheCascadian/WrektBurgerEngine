using System.Numerics;
using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Multiplayer;
using Burger.Core.Politics;
using Burger.Core.Society;
using Burger.Core.Simulation;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>
/// Society pages shared by the People and Politics panels: culture (traditions), government (reforms, national ideas, corruption),
/// succession (law, line, regent) and dynasty (prestige, family tree).
/// </summary>
public sealed partial class Hud
{
    private readonly Dictionary<string, float> _tabH = new();

    /// <summary>Draws one of the long society pages (reforms, succession) in a scrolling area under the Politics tabs.</summary>
    private void DrawScrolledPage(string id, Rectangle r, float x, float y, float w, Func<float, float, float> draw)
    {
        var area = new Rectangle(x - 2 * S, y, w + 4 * S, r.Bottom() - y - 8 * S);
        float top = Gui.BeginScroll(id, area, _tabH.GetValueOrDefault(id, 600));
        float end = draw(top, w - 10 * S);
        _tabH[id] = end - top + 12 * S;
        Gui.EndScroll();
    }

    /// <summary>Height the reforms block under the laws needed last frame (0 when it is not shown).</summary>
    private float ReformsHeightIn(Country c) => G.World.Player == c && G.Reforms.Enabled ? _tabH.GetValueOrDefault("laws_reforms", 900 * S) : 0;

    /// <summary>The Succession tab of Politics: the law, the line of heirs, regents, and the dynasty with its family.</summary>
    private void DrawSuccessionPage(Country p, Rectangle r, float x, float y, float w)
        => DrawScrolledPage("pol_succession", r, x, y, w, (top, cw) =>
        {
            float cy = top;
            if (G.Succession.Enabled) cy = DrawSuccessionContent(p, x, cy, cw);
            if (G.Dynasties.Enabled) cy = DrawDynastyContent(p, x, cy + 8 * S, cw);
            return cy;
        });

    // ------------------------------------------------------------------------------------------------ shared pieces
    /// <summary>The Overview of a country without an assembly: who rules, how stable it is, and the currents among its people.</summary>
    private void DrawRulerOverview(Country c, Rectangle r, float x, float y, float w)
    {
        var P = G.Politics;
        var p = P.Of(c);
        var ruling = P.Ideology(p.Ruling);
        Gui.Label($"{ruling?.Name ?? "Unknown"} rule", x, y, 18, Gui.Gold, FontKind.Header);
        Gui.Label("No parliament or parties: the ruler decides, the people and the great houses are watched below.", x, y + 24 * S, 12, Gui.TextDim);
        y += 44 * S;
        double stability = G.Rules.Politics.StabilityOf(G, c), power = P.PowerOf(c);
        double support = p.GovernmentOrganizations.Where(k => !PoliticsSystem.IsMinorityParty(k)).Sum(k => p.PartyPopularity.GetValueOrDefault(k));
        var tiles = new (string Label, string Value, Color Tone, float Fill, string Tip)[]
        {
            ("STABILITY", $"{stability:0}", stability >= 60 ? Gui.Good : stability >= 40 ? Gui.Gold : Gui.Bad, (float)(stability / 100), $"Stability {stability:0}/100\nLow stability feeds radicals and political movements."),
            (P.PowerName.ToUpperInvariant(), double.IsFinite(power) ? power.ToString("0") : "-", Gui.Gold, -1, "Spent on laws, reforms, ministers and crackdowns."),
            ("SUPPORT", $"{support:P0}", support >= 0.5 ? Gui.Good : Gui.Text, (float)support, $"The ruling current holds {support:P1} of the people."),
        };
        float gap = 4 * S, tw = (w - 2 * gap) / tiles.Length, th = OverviewPulseHeight * S;
        for (int i = 0; i < tiles.Length; i++)
        {
            var tile = new Rectangle(x + i * (tw + gap), y, tw, th);
            Gui.Card(tile, new Color(29, 31, 37, 255));
            Gui.LabelCentered(tiles[i].Value, new Rectangle(tile.X, tile.Y + 1 * S, tile.Width, 21 * S), 16, tiles[i].Tone, FontKind.Bold);
            Gui.LabelCentered(Gui.Fit(tiles[i].Label, tile.Width - 8 * S, 14, FontKind.Bold), new Rectangle(tile.X, tile.Y + 20 * S, tile.Width, 18 * S), 14, Gui.TextDim, FontKind.Bold);
            if (tiles[i].Fill >= 0) Gui.Bar(new Rectangle(tile.X + 8 * S, tile.Bottom() - 6 * S, tile.Width - 16 * S, 3 * S), tiles[i].Fill, tiles[i].Tone);
            if (Gui.Hover(tile)) Gui.Tooltip(tiles[i].Tip);
        }
        y += th + 10 * S;
        Gui.Section("Ruler", x, y, w);
        y += 24 * S;
        if (c.Ruler != null) y += CharRow(c.Ruler, x, y, w, c.Regent != null ? "Ruler (child)" : "Ruler", true);
        if (c.Regent != null) y += CharRow(c.Regent, x + 14 * S, y, w - 14 * S, "Regent");
        if (c.Heir != null && c.Heir != c.Ruler) y += CharRow(c.Heir, x + 14 * S, y, w - 14 * S, "Heir");
        y += 6 * S;
        Gui.Section("Currents among the people", x, y, w);
        y += 24 * S;
        foreach (var ide in P.Ideologies.OrderByDescending(i => p.Popularity.GetValueOrDefault(i.Key)).Take(6))
        {
            double v = p.Popularity.GetValueOrDefault(ide.Key);
            bool rules = ide.Key.Equals(p.Ruling, StringComparison.OrdinalIgnoreCase);
            Gui.Label(Gui.Fit(ide.Name + (rules ? " (rules)" : ""), w - 60 * S, 13), x, y, 13, rules ? Gui.Gold : Gui.Text);
            string pct = $"{v:P0}";
            Gui.Label(pct, x + w - Gui.Measure(pct, 13).X, y, 13, Gui.TextDim);
            Gui.Bar(new Rectangle(x, y + 18 * S, w, 5 * S), (float)Math.Clamp(v, 0, 1), C(ide.Color));
            y += 28 * S;
        }
    }

    private static Color HashTint(string key)
    {
        int h = 17;
        foreach (char ch in key) h = h * 31 + ch;
        var rgb = Rgb.FromHsv((Math.Abs(h) % 1000) / 1000.0, 0.45, 0.78);
        return new Color(rgb.R, rgb.G, rgb.B, (byte)255);
    }

    /// <summary>A square icon tile: the icon named by key if the game has one, else a tinted tile with the first letters.</summary>
    private void Glyph(string key, string label, Rectangle r, bool lit)
    {
        var tint = HashTint(key);
        if (!lit) tint = new Color((byte)(tint.R / 3), (byte)(tint.G / 3), (byte)(tint.B / 3), (byte)255);
        Raylib.DrawRectangleRounded(r, 0.18f, 4, tint);
        Raylib.DrawRectangleLinesEx(r, 1, new Color(0, 0, 0, 110));
        if (Gui.Icon(key, new Rectangle(r.X + 4 * S, r.Y + 4 * S, r.Width - 8 * S, r.Height - 8 * S), lit ? null : new Color(120, 120, 120, 255))) return;
        string ini = new string(label.Split(' ', '-').Where(p => p.Length > 0).Take(2).Select(p => char.ToUpperInvariant(p[0])).ToArray());
        var m = Gui.Measure(ini, 20, FontKind.Bold);
        Gui.Label(ini, r.X + (r.Width - m.X) / 2, r.Y + (r.Height - m.Y) / 2, 20, lit ? Color.White : Gui.TextDim, FontKind.Bold);
    }

    /// <summary>The red cross on the corner of a portrait of someone who is ill (dynasties feature). Returns whether it was drawn.</summary>
    private bool SickMark(Character ch, Rectangle pr)
    {
        if (!G.Dynasties.Enabled || !G.Dynasties.IsSick(ch)) return false;
        float rad = Math.Clamp(pr.Width * 0.2f, 5 * S, 9 * S);
        var c = new Vector2(pr.X + pr.Width - rad * 0.6f, pr.Y + rad * 0.6f);
        Raylib.DrawCircleV(c, rad, new Color(190, 50, 50, 255));
        Raylib.DrawRectangleRec(new Rectangle(c.X - rad * 0.2f, c.Y - rad * 0.55f, rad * 0.4f, rad * 1.1f), Color.White);
        Raylib.DrawRectangleRec(new Rectangle(c.X - rad * 0.55f, c.Y - rad * 0.2f, rad * 1.1f, rad * 0.4f), Color.White);
        return true;
    }

    private static string Mods(Dictionary<string, double> m, double weight = 1) => SocietyData.Describe(m, weight).Replace(", ", "\n");

    private int SnapshotAge(Character ch, GameDate now)
    {
        var snapshot = _app.Snapshot.Characters.FirstOrDefault(q => q.Id == ch.Id);
        return snapshot.Id == ch.Id && snapshot.Name != null ? snapshot.Age : ch.Age(now);
    }

    private string PowerText(Country p, double cost)
        => G.Politics.Enabled ? $"{cost:0} {G.Politics.PowerName.ToLowerInvariant()}" : "free";

    /// <summary>A character row: portrait, name, age, standing and an illness marker. Returns the height used.</summary>
    private float CharRow(Character ch, float x, float y, float w, string extra = "", bool highlight = false)
    {
        float h = 42 * S;
        var box = new Rectangle(x, y, w, h);
        if (Gui.Clicked(box)) OpenSheet(ch);
        Card(box, highlight ? new Color(54, 50, 36, 255) : new Color(36, 38, 46, 255), highlight);
        var pr = new Rectangle(x + 4 * S, y + 3 * S, 36 * S, 36 * S);
        DrawPortrait(ch, pr, false, false);
        PortraitFrame(pr, ch.Female ? new Color(200, 150, 180, 255) : new Color(130, 160, 200, 255));
        var dyn = G.Dynasties;
        var snapshot = _app.Snapshot.Characters.FirstOrDefault(q => q.Id == ch.Id);
        bool hasSnapshot = snapshot.Id == ch.Id && snapshot.Name != null;
        string displayName = hasSnapshot ? snapshot.Name ?? ch.DisplayName : ch.DisplayName;
        int age = hasSnapshot ? snapshot.Age : ch.Age(G.World.Date);
        bool sick = SickMark(ch, pr);
        Gui.Label(Gui.Fit(displayName, w - 56 * S, 14, FontKind.Bold), x + 46 * S, y + 3 * S, 14, ch.Alive ? Gui.Text : Gui.TextDim, FontKind.Bold);
        string sub = $"{(ch.Female ? "F" : "M")} {age}" + (extra.Length > 0 ? "  ·  " + extra : "");
        Gui.Label(Gui.Fit(sub, w - 56 * S, 12), x + 46 * S, y + 22 * S, 12, Gui.TextDim);
        if (Gui.Hover(box))
        {
            var tip = new List<string> { displayName, $"Age {age}" };
            if (dyn.Enabled) tip.Add("Claim: " + dyn.ClaimLabel(ch));
            if (sick && dyn.DiseaseOf(ch) is { } d) tip.Add($"Ill: {d.Name} (until {new GameDate(dyn.SickUntil(ch)).ToDisplay()})");
            if (hasSnapshot)
                tip.Add(string.Join("  ", snapshot.Stats.Select(s => $"{s.Key} {s.Value:0}")));
            else
                tip.Add(string.Join("  ", G.Data.Defines.CharacterStats.Select(s => $"{s} {ch.GetStat(s):0}")));
            if (hasSnapshot && snapshot.Traits.Length > 0) tip.Add(string.Join(", ", snapshot.Traits));
            else if (ch.Traits.Count > 0) tip.Add(string.Join(", ", ch.Traits.Select(t => t.Name)));
            Gui.Tooltip(string.Join("\n", tip));
        }
        return h + 3 * S;
    }

    // ------------------------------------------------------------------------------------------------ culture
    private float DrawCultureContent(Country p, float x, float y, float w)
    {
        var C = G.CultureLife;
        string? ck = C.PrimaryCulture(p);
        if (ck == null || !G.Population.CultureByKey.TryGetValue(ck, out var def)) { Gui.Label("Your country has no culture.", x, y, 14, Gui.TextDim); return y + 24 * S; }
        var st = C.Of(ck);
        Raylib.DrawRectangleRounded(new Rectangle(x, y + 3 * S, 16 * S, 16 * S), 0.3f, 4, C_(def.Color));
        Gui.Label(def.Name, x + 24 * S, y, 18, Gui.Gold, FontKind.Header);
        Gui.Label($"{def.Group.Name}" + (st.IsHybrid ? $"  ·  grew from {G.Population.Culture(st.ParentA)?.Name} and {G.Population.Culture(st.ParentB)?.Name}" : ""), x + 24 * S, y + 22 * S, 12, Gui.TextDim);
        y += 44 * S;
        Gui.Section("Traditions", x, y, w);
        y += 24 * S;
        var traditions = C.TraditionsOf(ck).ToList();
        float tile = Math.Min(86 * S, (w - 12 * S) / 3);
        for (int i = 0; i < traditions.Count; i++)
        {
            var t = traditions[i];
            double weight = st.Traditions.GetValueOrDefault(t.Key, 1);
            var tr = new Rectangle(x + i * (tile + 6 * S), y, tile, tile + 28 * S);
            Card(tr, new Color(34, 38, 46, 255));
            Glyph(string.IsNullOrEmpty(t.Icon) ? t.Key : t.Icon, t.Name, new Rectangle(tr.X + (tile - 40 * S) / 2, tr.Y + 6 * S, 40 * S, 40 * S), true);
            Gui.Label(Gui.Fit(t.Name, tile - 6 * S, 12, FontKind.Bold), tr.X + 4 * S, tr.Y + 50 * S, 12, Gui.Text, FontKind.Bold);
            if (weight < 0.999) Gui.Label($"{weight:P0}", tr.X + tile - 34 * S, tr.Y + 4 * S, 12, Gui.Gold);
            if (Gui.Hover(tr)) Gui.Tooltip($"{t.Name}" + (weight < 0.999 ? $" ({weight:P0} of it, inherited)" : "") + $"\n{t.Desc}\n{Mods(t.Modifier, weight)}");
        }
        y += tile + 36 * S;
        var all = C.ModifiersOf(ck);
        if (all.Count > 0)
        {
            string line = "Gives: " + SocietyData.Describe(all);
            y += Gui.Wrapped(line, x, y, w, 12, Gui.TextDim) + 6 * S;
        }

        // cultures mixing in the player's states, and cultures that grew out of others
        var mixing = p.OwnedStates.Select(s => (State: s, Mix: C.MixingIn(s))).Where(t => t.Mix != null).OrderByDescending(t => t.Mix!.Value.Months).Take(6).ToList();
        if (mixing.Count > 0)
        {
            Gui.Section("Cultures mixing", x, y, w);
            y += 24 * S;
            int limit = (int)(G.Data.Defines.Get("culture_evolution_years", 50) * 12);
            foreach (var (s, mix) in mixing)
            {
                var (a, b, months) = mix!.Value;
                string text = $"{s.DisplayName}: {G.Population.Culture(a)?.Name} + {G.Population.Culture(b)?.Name}";
                Gui.Label(Gui.Fit(text, w - 70 * S, 13), x, y, 13, Gui.Text);
                string yrs = $"{months / 12}/{limit / 12} yr";
                Gui.Label(yrs, x + w - Gui.Measure(yrs, 13).X, y, 13, Gui.TextDim);
                Gui.Bar(new Rectangle(x, y + 18 * S, w, 5 * S), Math.Clamp(months / (float)limit, 0, 1), new Color(180, 130, 200, 255));
                y += 28 * S;
            }
        }
        if (C.Evolved.Count > 0)
        {
            Gui.Section("New cultures", x, y, w);
            y += 24 * S;
            foreach (var k in C.Evolved.TakeLast(6).Reverse())
                if (G.Population.CultureByKey.TryGetValue(k, out var cd))
                {
                    Raylib.DrawRectangleRounded(new Rectangle(x, y + 2 * S, 12 * S, 12 * S), 0.3f, 4, C_(cd.Color));
                    Gui.Label(cd.Name, x + 20 * S, y, 13, Gui.Text);
                    y += 20 * S;
                }
        }
        return y;
    }

    private static Color C_(Rgb c) => new(c.R, c.G, c.B, (byte)255);

    // ------------------------------------------------------------------------------------------------ government
    private float DrawGovernmentContent(Country p, float x, float y, float w)
    {
        var R = G.Reforms;
        var rules = G.Rules.Society;
        var gs = R.Of(p);
        int cd = R.CooldownLeft(p);
        Gui.Label("Government", x, y, 18, Gui.Gold, FontKind.Header);
        string st = $"{gs.Reforms.Count}/{rules.MaxReforms(G)} reforms" + (cd > 0 ? $"  ·  next change in {cd} days" : "  ·  can change now");
        Gui.Label(st, x, y + 24 * S, 13, Gui.TextDim);
        if (G.Politics.Enabled)
        {
            string pp = $"{G.Politics.PowerOf(p):0} {G.Politics.PowerName.ToLowerInvariant()}";
            Gui.Label(pp, x + w - Gui.Measure(pp, 13).X, y + 24 * S, 13, Gui.Gold);
        }
        y += 48 * S;

        // corruption meter
        double corr = R.Corruption(p);
        Gui.Label("Corruption", x, y, 14, Gui.Text, FontKind.Bold);
        string cv = $"{corr:0.0} / 100";
        Gui.Label(cv, x + w - Gui.Measure(cv, 14, FontKind.Bold).X, y, 14, corr >= rules.CorruptionEventAt(G) ? Gui.Bad : Gui.Text, FontKind.Bold);
        var bar = new Rectangle(x, y + 22 * S, w, 12 * S);
        Gui.Bar(bar, (float)(corr / 100), corr >= 50 ? new Color(210, 70, 60, 255) : corr >= 25 ? new Color(220, 170, 70, 255) : new Color(110, 180, 110, 255));
        Raylib.DrawLineEx(new Vector2(bar.X + bar.Width * 0.5f, bar.Y), new Vector2(bar.X + bar.Width * 0.5f, bar.Y + bar.Height), 1, new Color(255, 255, 255, 120));
        double beyond = R.MillionsBeyondRange(p), steps = R.CentralisationSteps(p);
        double change = rules.CorruptionChange(G, p, beyond, steps);
        if (Gui.Hover(new Rectangle(x, y, w, 38 * S)))
            Gui.Tooltip($"Corruption {corr:0.0}: tax income {-corr * rules.CorruptionIncomePerPoint(G):P1}\nPeople beyond {rules.CorruptionRangeKm(G):0} km of the capital: {beyond:0.##}M (+{G.Data.Defines.Get("corruption_per_million", 0.05) * beyond:0.00} a month)\nCentralisation steps: {steps:0.#} ({-G.Data.Defines.Get("corruption_per_step", 0.2) * steps:0.00} a month)\nChange now: {change:+0.00;-0.00} a month\nAt 50 or more, events appear.");
        y += 38 * S;
        string cl = $"{change:+0.00;-0.00} a month  ·  centralisation {steps:0.#}  ·  tax {-corr * rules.CorruptionIncomePerPoint(G):P1}";
        Gui.Label(cl, x, y, 12, Gui.TextDim);
        y += 22 * S;

        // reform tree: tiers as rows, tiles side by side
        Gui.Section("Reforms", x, y, w);
        y += 24 * S;
        var avail = R.AvailableReforms(p);
        foreach (var tier in avail.GroupBy(r => r.Tier).OrderBy(g => g.Key))
        {
            int perRow = 3;
            float gap = 6 * S, tw = (w - gap * (perRow - 1)) / perRow, th = 86 * S;
            int col = 0;
            foreach (var rf in tier)
            {
                if (col == perRow) { col = 0; y += th + gap; }
                var tile = new Rectangle(x + col * (tw + gap), y, tw, th);
                bool held = gs.Reforms.Contains(rf.Key, StringComparer.OrdinalIgnoreCase);
                string? why = held ? R.CannotDrop(p, rf) : R.CannotAdopt(p, rf);
                Card(tile, held ? new Color(40, 62, 46, 255) : new Color(36, 38, 46, 255), held);
                Stripe(tile, held ? Gui.Good : why == null ? Gui.Gold : new Color(90, 96, 108, 255));
                Gui.Label(Gui.Fit(rf.Name, tw - 14 * S, 13, FontKind.Bold), tile.X + 8 * S, tile.Y + 3 * S, 13, held ? Gui.Good : Gui.Text, FontKind.Bold);
                float ty = tile.Y + 20 * S;
                foreach (var kv in rf.Modifier.Take(2))
                {
                    Gui.Label(Gui.Fit($"{Localisation.GetOrPretty(kv.Key)} {kv.Value:+0.##;-0.##}", tw - 14 * S, 11), tile.X + 8 * S, ty, 11, kv.Value >= 0 == !IsBadKey(kv.Key) ? Gui.Good : Gui.Bad);
                    ty += 14 * S;
                }
                double cost = rules.ReformCost(G, rf);
                var btn = new Rectangle(tile.X + 6 * S, tile.Bottom() - 26 * S, tw - 12 * S, 22 * S);
                string tip = $"{rf.Name}\n{rf.Desc}\n{Mods(rf.Modifier)}" + (rf.Requires.Count > 0 ? "\nNeeds: " + string.Join(", ", rf.Requires.Select(q => (R.Has(p, q) ? "[OK] " : "[X] ") + (R.Reforms.TryGetValue(q, out var qd) ? qd.Name : q))) : "")
                             + (rf.Removes.Count > 0 ? "\nReplaces: " + string.Join(", ", rf.Removes.Select(q => R.Reforms.TryGetValue(q, out var qd) ? qd.Name : q)) : "")
                             + $"\nCost: {PowerText(p, cost)}" + (why != null ? "\n" + why : "");
                if (Gui.Button(btn, held ? "Drop" : $"Adopt ({cost:0})", why == null, tip, false, 12))
                {
                    var reform = rf; bool drop = held;
                    Confirm($"{(drop ? "Drop" : "Adopt")} {reform.Name} for {PowerText(p, cost)}? Reforms can't change again for {rules.ReformCooldownDays(G) / 365.0:0.#} year(s).", () =>
                    {
                        if (!Send(GameCommand.Reform, reform.Key, drop ? "0" : "1")) Ui.SetStatus((drop ? R.Drop(p, reform) : R.Adopt(p, reform)) ?? (drop ? $"{reform.Name} dropped" : $"{reform.Name} adopted"));
                    });
                }
                else if (Gui.Hover(tile)) Gui.Tooltip(tip);
                col++;
            }
            y += th + gap;
        }

        // national ideas: a column of slots that light up
        var set = R.SetOf(p);
        int slots = rules.IdeaSlots(G), have = R.Unlocked(p);
        Gui.Section("National ideas" + (set != null ? " - " + set.Name : ""), x, y, w);
        y += 24 * S;
        double pts = R.IdeaPoints(p), need = rules.IdeaCost(G);
        string ptsText = have >= slots || set != null && have >= set.Ideas.Count ? "All ideas unlocked" : $"{pts:0.0} / {need:0} idea points  (+{rules.IdeaPointsPerMonth(G, p):0.00} a month)";
        Gui.Label(ptsText, x, y, 12, Gui.TextDim);
        Gui.Bar(new Rectangle(x, y + 17 * S, w, 5 * S), Math.Clamp((float)(pts / need), 0, 1), new Color(150, 190, 230, 255));
        y += 28 * S;
        for (int i = 0; i < slots; i++)
        {
            var idea = set != null && i < set.Ideas.Count ? set.Ideas[i] : null;
            bool lit = idea != null && i < have;
            var box = new Rectangle(x, y, w, 36 * S);
            Card(box, lit ? new Color(54, 52, 36, 255) : new Color(30, 32, 38, 255), lit);
            Raylib.DrawCircleV(new Vector2(x + 16 * S, y + 18 * S), 9 * S, lit ? new Color(236, 196, 90, 255) : new Color(54, 58, 68, 255));
            Gui.Label((i + 1).ToString(), x + 16 * S - Gui.Measure((i + 1).ToString(), 12, FontKind.Bold).X / 2, y + 11 * S, 12, lit ? new Color(40, 30, 10, 255) : Gui.TextDim, FontKind.Bold);
            if (idea != null)
            {
                Gui.Label(Gui.Fit(lit ? idea.Name : "Locked", w - 44 * S, 14, FontKind.Bold), x + 34 * S, y + 2 * S, 14, lit ? Gui.Gold : Gui.TextDim, FontKind.Bold);
                Gui.Label(Gui.Fit(SocietyData.Describe(idea.Modifier), w - 44 * S, 12), x + 34 * S, y + 20 * S, 12, lit ? Gui.Text : new Color(110, 116, 128, 255));
                if (Gui.Hover(box)) Gui.Tooltip(lit ? $"{idea.Name}\n{idea.Desc}\n{Mods(idea.Modifier)}" : $"Unlocks at {(i + 1) * need:0} idea points in total.\n{idea.Name}: {SocietyData.Describe(idea.Modifier)}");
            }
            else Gui.Label("-", x + 34 * S, y + 9 * S, 14, Gui.TextDim);
            y += 40 * S;
        }
        return y;
    }

    /// <summary>Modifier keys where a larger number is worse for the country.</summary>
    private static bool IsBadKey(string key) => key.ToLowerInvariant() is "upkeep" or "corruption_gain" or "development_cost" or "war_exhaustion_gain" or "liberty_desire";

    // ------------------------------------------------------------------------------------------------ succession
    private float DrawSuccessionContent(Country p, float x, float y, float w)
    {
        var S_ = G.Succession;
        var law = S_.LawOf(p);
        Gui.Label("Succession", x, y, 18, Gui.Gold, FontKind.Header);
        Gui.Label($"Law: {law.Name}", x, y + 24 * S, 14, Gui.Text);
        y += 48 * S;

        // laws (laws feature): choose another
        var group = G.Politics.LawsEnabled ? G.Politics.LawGroups.FirstOrDefault(g => g.Key == SuccessionSystem.LawGroupKey) : null;
        if (group != null)
        {
            Gui.Section("Succession law", x, y, w);
            y += 24 * S;
            var cur = G.Politics.LawOf(p, group);
            float gap = 4 * S, bw = (w - gap) / 2, bh = 28 * S;
            int col = 0;
            foreach (var l in group.Laws.Where(l => G.Politics.IsLawVisible(p, l)))
            {
                if (col == 2) { col = 0; y += bh + gap; }
                bool isCur = cur == l;
                string? why = isCur ? null : G.Politics.CannotEnact(p, l);
                var def = S_.Laws.GetValueOrDefault(l.Key);
                string tip = $"{l.Name}\n{l.Desc}\n" + (def != null ? DescribeOrder(def) + "\n" : "") + (l.Modifier.Count > 0 ? Mods(l.Modifier) + "\n" : "") + (isCur ? "In force" : why ?? $"Cost: {PowerText(p, G.Politics.EnactCost(p, l))}");
                if (Gui.Button(new Rectangle(x + col * (bw + gap), y, bw, bh), isCur ? l.Name : $"{l.Name} ({G.Politics.EnactCost(p, l):0})", why == null || isCur, tip, isCur, 12) && !isCur)
                { var newLaw = l; Confirm($"Change the succession law to {newLaw.Name} for {G.Politics.EnactCost(p, newLaw):0} {G.Politics.PowerName}?", () => Ui.SetStatus(G.Politics.Enact(p, newLaw) ?? $"Succession law: {newLaw.Name}")); }
                col++;
            }
            y += bh + 6 * S;
            double quiet = S_.YearsSinceSuccession(p);
            Gui.Label(quiet < 90 ? $"Last succession {quiet:0.#} years ago; the law can change after {G.Rules.Society.LawChangeQuietYears(G)} years without one." : "The law can be changed.", x, y, 12, Gui.TextDim);
            y += 22 * S;
        }
        else
        {
            y += Gui.Wrapped(law.Desc, x, y, w, 12, Gui.TextDim) + 6 * S;
        }

        // regency
        if (p.Regent is { } regent && p.Ruler is { } child)
        {
            var banner = new Rectangle(x, y, w, 64 * S);
            Raylib.DrawRectangleRec(banner, new Color(70, 52, 30, 255));
            Raylib.DrawRectangleLinesEx(banner, 1, Gui.Gold);
            var pr = new Rectangle(x + 6 * S, y + 6 * S, 52 * S, 52 * S);
            DrawPortrait(regent, pr, false, false);
            PortraitFrame(pr, Gui.Gold);
            Gui.Label("Regency", x + 66 * S, y + 4 * S, 15, Gui.Gold, FontKind.Header);
            Gui.Label(Gui.Fit($"{regent.DisplayName} rules for {child.DisplayName}", w - 74 * S, 13), x + 66 * S, y + 24 * S, 13, Gui.Text);
            Gui.Label($"{S_.RegencyEndsIn(p)} years to go  ·  stability {G.Rules.Society.RegencyStability(G):+0;-0}", x + 66 * S, y + 42 * S, 12, Gui.TextDim);
            if (Gui.Hover(banner)) Gui.Tooltip($"{child.DisplayName} is {SnapshotAge(child, G.World.Date)} and cannot rule yet. {regent.DisplayName} governs with their own abilities until {child.DisplayName} is {G.Rules.Society.RegencyAge(G)}.");
            y += 72 * S;
        }

        // the line
        Gui.Section("Line of succession", x, y, w);
        y += 24 * S;
        if (p.Ruler != null)
        {
            y += CharRow(p.Ruler, x, y, w, "Ruler" + (p.Regent != null ? " (child)" : ""), true);
        }
        int shown = 0;
        var votes = law.Elective ? S_.Vote(p, p.Ruler) : null;
        foreach (var heir in S_.Line(p, p.Ruler).Take(8))
        {
            string extra = shown == 0 ? "Heir" : $"#{shown + 1}";
            if (G.Dynasties.Enabled && G.Dynasties.Claim(heir) < 1) extra += "  ·  " + G.Dynasties.ClaimLabel(heir);
            if (votes != null && votes.FirstOrDefault(v => v.Candidate == heir) is { Candidate: not null } vt) extra += $"  ·  {vt.Votes} votes";
            y += CharRow(heir, x, y, w, extra, shown == 0);
            shown++;
        }
        if (shown == 0) { Gui.Label("Nobody is in line: a new ruler would be found.", x, y, 13, Gui.TextDim); y += 22 * S; }
        if (law.Elective)
        {
            Gui.Section("Electors", x, y, w);
            y += 24 * S;
            foreach (var e in S_.Electors(p, p.Ruler)) y += CharRow(e, x, y, w, $"prestige {G.Dynasties.Prestige(e):0}");
        }
        return y;
    }

    private string DescribeOrder(SuccessionLawDef d)
        => d.Elective ? "A council votes (prestige + opinion)." : "Order: " + string.Join(", ", d.Order.Select(s => s.Replace('_', ' '))) + (d.Split ? ". The realm is divided among the sons." : "");

    // ------------------------------------------------------------------------------------------------ dynasty
    private float DrawDynastyContent(Country p, float x, float y, float w)
    {
        var D = G.Dynasties;
        if (p.Ruler is not { Family.Length: > 0 } ruler)
        {
            Gui.Label("Your country has no ruling family.", x, y, 14, Gui.TextDim);
            return y + 24 * S;
        }
        var ds = D.StateOf(ruler.Family);
        Gui.Label($"House {ruler.Family}", x, y, 18, Gui.Gold, FontKind.Header);
        Gui.Label($"{D.Thrones(ruler.Family)} throne(s)  ·  {D.Members(ruler.Family).Count()} living members", x, y + 24 * S, 12, Gui.TextDim);
        y += 44 * S;
        Gui.Label($"{ds.Prestige:0} prestige", x, y, 16, Gui.Text, FontKind.Bold);
        var rt = new Rectangle(x, y, w, 22 * S);
        var rules = G.Rules.Society;
        if (Gui.Hover(rt)) Gui.Tooltip($"A dynasty earns {rules.PrestigePerRuler(G):0} prestige for each new ruler, {rules.PrestigePerThroneYear(G):0} a year for each throne it holds and {rules.PrestigePerEvent(G):0} for notable events (a child born to the ruler).\nPrestige counts when a council elects the ruler.");
        y += 28 * S;

        // household law
        var hh = G.Politics.LawsEnabled ? G.Politics.LawGroups.FirstOrDefault(g => g.Key == "household") : null;
        if (hh != null)
        {
            Gui.Section("Household", x, y, w);
            y += 24 * S;
            var cur = G.Politics.LawOf(p, hh);
            float gap = 4 * S, bw = (w - gap) / 2;
            int col = 0;
            foreach (var l in hh.Laws)
            {
                bool isCur = cur == l;
                string? why = isCur ? null : G.Politics.CannotEnact(p, l);
                if (Gui.Button(new Rectangle(x + col * (bw + gap), y, bw, 28 * S), isCur ? l.Name : $"{l.Name} ({G.Politics.EnactCost(p, l):0})", why == null || isCur, $"{l.Name}\n{l.Desc}\n" + (isCur ? "In force" : why ?? $"Cost: {PowerText(p, G.Politics.EnactCost(p, l))}"), isCur, 12) && !isCur)
                { var newLaw = l; Confirm($"Change the household law to {newLaw.Name} for {G.Politics.EnactCost(p, newLaw):0} {G.Politics.PowerName}?", () => Ui.SetStatus(G.Politics.Enact(p, newLaw) ?? $"Household law: {newLaw.Name}")); }
                col++;
            }
            y += 36 * S;
        }

        // family tree of the ruler
        Gui.Section("Family", x, y, w);
        y += 24 * S;
        y += CharRow(ruler, x, y, w, "Ruler", true);
        if (ruler.Spouse is { Alive: true } spouse) y += CharRow(spouse, x + 14 * S, y, w - 14 * S, "Spouse");
        foreach (var con in D.Concubines(ruler)) y += CharRow(con, x + 14 * S, y, w - 14 * S, "Concubine");
        foreach (var kid in ruler.Children.Where(k => k.Alive).OrderByDescending(k => D.Claim(k)).ThenByDescending(k => SnapshotAge(k, G.World.Date)).Take(10))
        {
            string extra = kid == p.Heir ? "Heir" : D.Claim(kid) < 1 ? D.ClaimLabel(kid) : "Child";
            y += CharRow(kid, x + 28 * S, y, w - 28 * S, extra, kid == p.Heir);
        }

        // the houses: where the family lives
        var houses = D.Houses(ruler.Family);
        if (houses.Count > 1)
        {
            Gui.Section("Houses", x, y, w);
            y += 24 * S;
            foreach (var (country, members) in houses)
            {
                var row = new Rectangle(x, y, w, 24 * S);
                Card(row, new Color(36, 38, 46, 255));
                Raylib.DrawRectangleRec(new Rectangle(x, y, 5 * S, row.Height), country != null ? C(country.Color) : Gui.TextDim);
                Gui.Label(Gui.Fit(country?.Name ?? "Abroad", w - 90 * S, 13, FontKind.Bold), x + 12 * S, y + 3 * S, 13, Gui.Text, FontKind.Bold);
                string n = $"{members.Count} member(s)";
                Gui.Label(n, x + w - Gui.Measure(n, 12).X - 6 * S, y + 5 * S, 12, Gui.TextDim);
                if (Gui.Hover(row)) Gui.Tooltip(string.Join("\n", members.Take(12).Select(m => $"{m.DisplayName}, {SnapshotAge(m, G.World.Date)}" + (m.IsRuler ? " (ruler)" : ""))));
                y += 28 * S;
            }
        }
        return y;
    }
}
