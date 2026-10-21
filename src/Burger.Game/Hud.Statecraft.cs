using System.Numerics;
using Burger.Core;
using Burger.Core.Characters;
using Burger.Core.Diplomacy;
using Burger.Core.Model;
using Burger.Core.Warfare;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>
/// The sections the statecraft features add to existing screens: great powers and leagues (World panel), relations (country panel), court (Politics panel),
/// raising troops and army groups (Army panel), titles, marriage and intrigue (character sheet), banners and the peace conference window.
/// </summary>
public sealed partial class Hud
{
    private int _worldTab, _armyTab;
    private float _worldScH = 600;
    private float _scH = 800;
    private double _scTax = 0.2, _scLevy = 0.2;
    private ArmyGroup? _scMarshalFor;
    private readonly HashSet<int> _scConferencesClosed = new();

    private void ScResult(string? why, string ok = "")
    {
        if (why != null) G.Notify(why, NotificationKind.Bad);
        else if (ok.Length > 0) G.Notify(ok, NotificationKind.Good);
    }

    private IEnumerable<Character> ScCourt(Country c)
        => G.World.CharactersOf(c).Where(ch => ch.Alive && ch.Age(G.World.Date) >= G.Data.Defines.AdultAge);

    private bool ScButton(Rectangle box, float width, string text, bool enabled, string? tip, float offset = 0)
        => Gui.Button(new Rectangle(box.X + box.Width - width - 6 * S - offset, box.Y + (box.Height - 24 * S) / 2, width, 24 * S), text, enabled, tip, false, 13);

    // ===============================================================================================
    // Powers: the rank list, spheres, crises and leagues
    // ===============================================================================================
    private float ScPowers(Country? p, float x, float y, float w)
    {
        var gp = G.GreatPowers;
        Gui.Section("Great powers", x, y, w);
        y += 26 * S;
        double max = Math.Max(1, gp.Ranking.Count > 0 ? gp.Ranking[0].Score : 1);
        foreach (var e in gp.Ranking.Take(14))
        {
            var box = new Rectangle(x, y, w, 36 * S);
            bool hover = Gui.Hover(box);
            Card(box, e.Country == p ? new Color(60, 52, 34, 255) : hover ? new Color(50, 50, 56, 255) : new Color(36, 38, 44, 255), e.Country == p);
            Gui.Label($"{e.Rank}", box.X + 6 * S, box.Y + 9 * S, 15, e.Great ? Gui.Gold : Gui.TextDim, FontKind.Bold);
            Gui.Flag(e.Country, new Rectangle(box.X + 30 * S, box.Y + 5 * S, 38 * S, 26 * S));
            Gui.Label(Gui.Fit(e.Country.Name, w * 0.34f, 15, FontKind.Bold), box.X + 76 * S, box.Y + 2 * S, 15, e.Great ? Gui.Gold : Gui.Text, FontKind.Bold);
            var sphere = gp.SphereOf(e.Country);
            string sub = e.Great ? "Great power" : sphere != null ? $"Sphere of {sphere.Name}" : "";
            Gui.Label(Gui.Fit(sub, w * 0.4f, 12), box.X + 76 * S, box.Y + 19 * S, 12, Gui.TextDim);
            float bx = box.X + w * 0.58f;
            Gui.Bar(new Rectangle(bx, box.Y + 14 * S, w * 0.25f, 7 * S), (float)(e.Score / max), e.Great ? Gui.Gold : new Color(120, 124, 136, 255));
            string arrow = e.Rank < e.PreviousRank ? "▲" : e.Rank > e.PreviousRank ? "▼" : "";
            Gui.Label($"{e.Score:0}", bx + w * 0.26f, box.Y + 9 * S, 13, Gui.Text, FontKind.Bold);
            if (arrow.Length > 0) Gui.Label(arrow, box.X + box.Width - 18 * S, box.Y + 9 * S, 12, e.Rank < e.PreviousRank ? Gui.Good : Gui.Bad);
            if (hover)
            {
                Gui.Tooltip($"{e.Country.Name}: rank {e.Rank}, score {e.Score:0}\nIndustry (40%) {e.Industry:0.#}\nArmy (30%) {e.Army:0.#}\nNavy and air (20%) {e.Fleet:0.#}\nPeople (10%) {e.People:0.#}" +
                            (e.Great ? "\nOne of the great powers" : ""));
                if (Gui.Clicked(box)) { Ui.SelectedCountry = e.Country; Ui.Left = LeftPanel.Country; }
            }
            y += 40 * S;
        }

        // spheres of influence (the actions - campaign, draw into the sphere, release - are in the action list of each country)
        y += 4 * S;
        Gui.Section("Spheres of influence", x, y, w);
        y += 26 * S;
        if (p != null)
        {
            var own = gp.SphereOf(p);
            if (gp.IsGreat(p)) y = ScText($"You are a great power: {gp.Campaigns(p).Count()} of {gp.MaxCampaigns(p)} campaigns. Open a country to campaign for influence there.", x, y, w, 13, Gui.Text);
            else y = ScText(own != null ? $"You are in the sphere of influence of {own.Name}." : "You are not in anyone's sphere.", x, y, w, 13, own != null ? Gui.Gold : Gui.TextDim);
        }
        foreach (var e in gp.Ranking.Where(e => e.Great))
        {
            var power = e.Country;
            var camp = gp.Campaigns(power).ToList();
            var members = gp.SphereMembers(power).ToList();
            if (camp.Count == 0 && members.Count == 0) continue;
            var box = new Rectangle(x, y, w, 34 * S + (members.Count > 0 ? 20 * S : 0) + camp.Count * 22 * S);
            Card(box, new Color(36, 38, 44, 255));
            Gui.Flag(power, new Rectangle(box.X + 6 * S, box.Y + 5 * S, 32 * S, 22 * S));
            Gui.Label(Gui.Fit(power.Name, w - 60 * S, 14, FontKind.Bold), box.X + 46 * S, box.Y + 7 * S, 14, Gui.Gold, FontKind.Bold);
            float ry = box.Y + 32 * S;
            if (members.Count > 0) { Gui.Label(Gui.Fit("Sphere: " + string.Join(", ", members.Select(m => m.Name)), w - 16 * S, 12), box.X + 8 * S, ry, 12, Gui.Text); ry += 20 * S; }
            foreach (var t in camp)
            {
                double inf = gp.Influence(power, t);
                Gui.Label(Gui.Fit($"Campaign in {t.Name}", w * 0.45f, 12), box.X + 8 * S, ry, 12, Gui.TextDim);
                Gui.Bar(new Rectangle(box.X + w * 0.5f, ry + 4 * S, w * 0.4f, 8 * S), (float)(inf / 100), inf > 50 ? Gui.Gold : new Color(120, 160, 220, 255));
                Gui.Label($"{inf:0}", box.X + w * 0.5f + w * 0.41f, ry, 12, Gui.Text);
                ry += 22 * S;
            }
            y += box.Height + 4 * S;
        }
        foreach (var cr in gp.Crises)
        {
            var box = new Rectangle(x, y, w, 40 * S);
            Card(box, new Color(70, 34, 30, 255));
            Gui.Label(Gui.Fit($"Crisis over {cr.Target.Name}", w - 16 * S, 15, FontKind.Bold), box.X + 8 * S, box.Y + 3 * S, 15, Gui.Bad, FontKind.Bold);
            Gui.Label(Gui.Fit("Rivals: " + string.Join(", ", cr.Powers.Select(c => c.Name)), w - 16 * S, 13), box.X + 8 * S, box.Y + 21 * S, 13, Gui.Text);
            if (Gui.Hover(box)) Gui.Tooltip("Two great powers both have more than 50 influence here. It ends in a war, a deal or time.");
            y += 44 * S;
        }
        return y;
    }

    /// <summary>The leagues: members, who leads, the vote on the selected country and the sanctions in force. Joining, leaving and proposing sanctions are actions of the country panel.</summary>
    private float ScLeagues(Country? p, float x, float y, float w)
    {
        var gp = G.GreatPowers;
        var target = Ui.SelectedCountry;
        foreach (var b in gp.Bodies.Values)
        {
            var members = gp.MembersOf(b.Key).Where(c => c.Exists).OrderByDescending(gp.Score).ToList();
            var box = new Rectangle(x, y, w, 76 * S);
            Card(box, new Color(36, 38, 44, 255));
            Gui.Label(Gui.Fit(b.Name, w - 130 * S, 16, FontKind.Bold), box.X + 8 * S, box.Y + 4 * S, 16, Gui.Gold, FontKind.Bold);
            if (p != null && gp.IsMember(b.Key, p)) Gui.Label("Member", box.X + box.Width - 70 * S, box.Y + 6 * S, 13, Gui.Good, FontKind.Bold);
            var leader = gp.LeaderOf(b.Key);
            Gui.Label(Gui.Fit($"{members.Count} members" + (leader != null ? $", led by {leader.Name}" : ""), w - 16 * S, 13), box.X + 8 * S, box.Y + 26 * S, 13, Gui.TextDim);
            float fx = box.X + 8 * S;
            foreach (var m in members.Take(14))
            {
                var fr = new Rectangle(fx, box.Y + 48 * S, 26 * S, 18 * S);
                Gui.Flag(m, fr);
                if (m == leader) Raylib.DrawRectangleLinesEx(fr, 2, Gui.Gold);
                if (Gui.Hover(fr)) Gui.Tooltip($"{m.Name}{(m == leader ? " (leads the league)" : "")}" + (target != null && target != m && !members.Contains(target) ? $"\nVotes {(gp.VotesYes(m, target) ? "yes" : "no")} on sanctions against {target.Name}" : ""));
                fx += 28 * S;
            }
            y += 80 * S;
        }
        foreach (var s in gp.Sanctions)
        {
            string body = gp.Bodies.TryGetValue(s.Body, out var bd) ? bd.Name : s.Body;
            Gui.Label(Gui.Fit($"{body} sanctions {s.Target.Name} until {new GameDate(s.ExpireDay).ToDisplay()}", w, 13), x, y, 13, Gui.Bad);
            y += 20 * S;
        }
        foreach (var line in gp.VoteLog.TakeLast(4))
        {
            Gui.Label(Gui.Fit(line, w, 12), x, y, 12, Gui.TextDim);
            y += 18 * S;
        }
        return y;
    }

    // ===============================================================================================
    // Country panel, "Relations" tab: envoys, reputation, rivals, spheres, factions, sanctions
    // ===============================================================================================
    /// <summary>Opens one of the statecraft sections (command line --open sc:NAME[:TAG], for screenshots).</summary>
    public void OpenStatecraft(string what, Country? c)
    {
        var p = G.World.Player;
        switch (what)
        {
            case "powers": _worldTab = 1; Ui.Right = RightPanel.World; break;
            case "leagues": _worldTab = 2; Ui.Right = RightPanel.World; break;
            case "relations": c ??= p; if (c != null) { _countryTabTag = c.Tag; _countryTab = G.Politics.Enabled ? 3 : 2; Ui.SelectedCountry = c; Ui.Left = LeftPanel.Country; } break;
            case "court": PoliticsTab = 9; Ui.Left = LeftPanel.Politics; break;
            case "groups": _armyTab = 1; Ui.Left = LeftPanel.Military; break;
            case "raising": _armyTab = 2; Ui.Left = LeftPanel.Military; break;
            case "sheet": { var ch = (c ?? p)?.Ruler; if (ch != null) OpenSheet(ch); break; }
        }
    }

    private bool ScRelationsOn => G.GreatPowers.Enabled || G.Reputation.Enabled || G.Envoys.Enabled || G.Factions.Enabled || G.Marriages.Enabled;

    private float ScText(string text, float x, float y, float w, float size, Color? col = null) => y + Gui.Wrapped(text, x, y, w, size, col ?? Gui.TextDim) + 4 * S;

    private bool ScAct(ref float y, float x, float w, string text, bool enabled, string? tip)
    {
        bool b = Gui.Button(new Rectangle(x, y, w, 28 * S), Gui.Fit(text, w - 12 * S, 14), enabled, tip, false, 14);
        y += 34 * S;
        return b;
    }

    private void DrawCountryRelations(Country c, Rectangle r, float x, float y, float w)
    {
        var area = new Rectangle(x - 2 * S, y, w + 4 * S, r.Y + r.Height - y - 8 * S);
        float top = Gui.BeginScroll("country_relations", area, _scH);
        float end = ScRelations(G.World.Player, c, x, top, w - 10 * S);
        _scH = end - top + 12 * S;
        Gui.EndScroll();
    }

    /// <summary>
    /// What the statecraft systems know about a country (information only: the actions - rivals, influence, spheres, leagues, sanctions, faction goals - are
    /// diplomatic actions in the country's action list; marriages are arranged from a character's sheet).
    /// </summary>
    private float ScRelations(Country? p, Country c, float x, float y, float w)
    {
        var gp = G.GreatPowers;
        if (gp.Enabled && gp.EntryOf(c) is { } e)
        {
            Gui.Section("Power", x, y, w);
            y += 26 * S;
            var owner = gp.SphereOf(c);
            y = ScText($"Rank {e.Rank} of {gp.Ranking.Count}, score {e.Score:0} (industry {e.Industry:0.#}, army {e.Army:0.#}, navy and air {e.Fleet:0.#}, people {e.People:0.#}). " + (e.Great ? "A great power." : owner != null ? $"In the sphere of influence of {owner.Name}." : "Independent."), x, y, w, 13, Gui.Text);
            var sphere = gp.SphereMembers(c).ToList();
            if (sphere.Count > 0) y = ScText("Sphere: " + string.Join(", ", sphere.Select(m => m.Name)), x, y, w, 13, Gui.Text);
            foreach (var t in gp.Campaigns(c).ToList())
                y = ScText($"Campaigning in {t.Name}: influence {gp.Influence(c, t):0} of 50", x, y, w, 13, Gui.Gold);
            foreach (var cr in gp.Crises.Where(k => k.Target == c))
                y = ScText($"Crisis over {c.Name}: " + string.Join(", ", cr.Powers.Select(k => k.Name)), x, y, w, 13, Gui.Bad);
            var leagues = gp.BodiesOf(c).Select(b => b.Name).ToList();
            if (leagues.Count > 0) y = ScText("Leagues: " + string.Join(", ", leagues), x, y, w, 13, Gui.Text);
            foreach (var s in gp.Sanctions.Where(s => s.Target == c))
                y = ScText($"Under sanctions of the {(gp.Bodies.TryGetValue(s.Body, out var bd) ? bd.Name : s.Body)} until {new GameDate(s.ExpireDay).ToDisplay()}", x, y, w, 13, Gui.Bad);
        }
        if (G.Reputation.Enabled)
        {
            var R = G.Reputation;
            Gui.Section("Reputation", x, y, w);
            y += 26 * S;
            double ae = R.Expansion(c);
            Gui.Label($"Aggressive expansion {ae:0.#}", x, y, 15, ae >= 20 ? Gui.Bad : ae >= 10 ? Gui.Gold : Gui.Text, FontKind.Bold);
            var bar = new Rectangle(x + w * 0.46f, y + 5 * S, w * 0.52f, 10 * S);
            Gui.Bar(bar, (float)(ae / 40), ae >= 20 ? Gui.Bad : ae >= 10 ? Gui.Gold : Gui.Good);
            foreach (var mark in new[] { 10, 20 })
                Raylib.DrawLineEx(new Vector2(bar.X + bar.Width * mark / 40f, bar.Y - 2), new Vector2(bar.X + bar.Width * mark / 40f, bar.Y + bar.Height + 2), 1.5f, Color.White);
            if (Gui.Hover(new Rectangle(x, y, w, 20 * S))) Gui.Tooltip("Every state taken adds its development (half with a core or claim), fading 0.1 a month.\nAt 10: neighbours dislike them by 2 a point and fabricating claims costs 50% more.\nAt 20: three countries within reach that dislike them form a coalition.");
            y += 26 * S;
            if (R.CoalitionAgainst(c) is { } coal)
                y = ScText($"A coalition stands against {c.Name}: {string.Join(", ", coal.Members.Select(m => m.Name))}", x, y, w, 13, Gui.Bad);
            var rivals = R.RivalsOf(c);
            y = ScText(rivals.Count == 0 ? "No rivals." : $"Rivals ({rivals.Count} of {R.RivalSlots}): " + string.Join(", ", rivals.Select(m => m.Name)), x, y, w, 13, Gui.Text);
        }
        if (G.Factions.Enabled)
        {
            var F = G.Factions;
            var members = F.FactionOf(c);
            Gui.Section("Alliance faction", x, y, w);
            y += 26 * S;
            if (members.Count < 2) y = ScText("No allies to form a faction with.", x, y, w, 13);
            else
            {
                var leader = F.LeaderOf(c);
                float fx = x;
                foreach (var m in members)
                {
                    var fr = new Rectangle(fx, y, 34 * S, 24 * S);
                    Gui.Flag(m, fr);
                    if (m == leader) Raylib.DrawRectangleLinesEx(fr, 2, Gui.Gold);
                    if (Gui.Hover(fr)) Gui.Tooltip($"{m.Name}{(m == leader ? " leads the faction" : "")}");
                    fx += 38 * S;
                }
                y += 30 * S;
                if (F.Goals.TryGetValue(leader, out var goal))
                    y = ScText($"Goal: war on {goal.Target.Name} - {F.DaysLeft(goal)} days to join. Members that do not join lose 25 opinion with the leader and may leave.", x, y, w, 13, p != null && F.Supports(p, goal) ? Gui.Good : Gui.Gold);
                else y = ScText($"{leader.Name} leads the faction and sets its goals.", x, y, w, 13);
            }
        }
        return y;
    }

    // ===============================================================================================
    // Politics panel, "Court" tab: titles to create, vassals, governors (granting is on the character sheets)
    // ===============================================================================================
    private void DrawCourtTab(Country p, Rectangle r, float x, float y, float w)
    {
        var area = new Rectangle(x - 2 * S, y, w + 4 * S, r.Y + r.Height - y - 8 * S);
        float top = Gui.BeginScroll("politics_court", area, _scH);
        float end = ScCourtTab(p, x, top, w - 10 * S);
        _scH = end - top + 12 * S;
        Gui.EndScroll();
    }

    private float ScCourtTab(Country p, float x, float y, float w)
    {
        var T = G.Titles;
        string power = G.Politics.Enabled ? G.Politics.PowerName : "gold";
        Gui.Section("Titles", x, y, w);
        y += 26 * S;
        var held = T.TitlesOfTier(TitleTier.Empire).Concat(T.TitlesOfTier(TitleTier.Kingdom)).Concat(T.TitlesOfTier(TitleTier.Duchy))
            .Where(t => t.Created && t.Capital?.Owner == p).Take(8).ToList();
        foreach (var t in held)
        {
            Gui.Label(Gui.Fit($"{t.Tier}: {t.Name}", w, 14), x, y, 14, Gui.Gold);
            y += 20 * S;
        }
        var creatable = T.TitlesOfTier(TitleTier.Duchy).Concat(T.TitlesOfTier(TitleTier.Kingdom)).Concat(T.TitlesOfTier(TitleTier.Empire))
            .Where(t => !t.Created && t.Children.Count > 0 && t.StatesBelow().Any(s => s.Owner == p)).OrderBy(t => t.Tier == TitleTier.Duchy ? 0 : 1).ThenByDescending(t => t.StatesBelow().Count(s => s.Owner == p)).Take(6).ToList();
        foreach (var t in creatable)
        {
            string? why = T.CannotCreate(p, t);
            var box = new Rectangle(x, y, w, 30 * S);
            Card(box, new Color(36, 38, 44, 255));
            Gui.Label(Gui.Fit($"{t.Tier} of {t.Name}", w - 110 * S, 14), box.X + 8 * S, box.Y + 6 * S, 14, Gui.Text);
            var title = t;
            if (ScButton(box, 90 * S, $"Create {T.CreateCost:0}", why == null, why ?? $"Create the title for {T.CreateCost:0} {power}: de jure claims on its land cost less"))
                Confirm($"Create the {t.Tier.ToString().ToLowerInvariant()} of {t.Name} for {T.CreateCost:0} {power}?", () => ScResult(T.Create(p, title)));
            y += 34 * S;
        }
        if (held.Count == 0 && creatable.Count == 0) y = ScText("No title to hold or create yet.", x, y, w, 13);

        y += 6 * S;
        y = ScCouncil(p, x, y, w);

        Gui.Section("Vassals", x, y, w);
        y += 26 * S;
        y = ScText($"Open a character's sheet to grant them a county ({T.GrantCost:0} {power}). The vassal keeps all but their tax and levy share (10-30%); below -50 opinion they plot.", x, y, w, 13);
        foreach (var (vassal, states) in T.VassalsOf(p))
        {
            var box = new Rectangle(x, y, w, 44 * S);
            Card(box, new Color(36, 38, 44, 255));
            Gui.Label(Gui.Fit(vassal.DisplayName, w - 16 * S, 15, FontKind.Bold), box.X + 8 * S, box.Y + 3 * S, 15, Gui.Text, FontKind.Bold);
            double op = G.Intrigue.Enabled ? G.Intrigue.OpinionOfRuler(vassal) : vassal.Loyalty - 50;
            Gui.Label(Gui.Fit($"{states.Count} counties, tax {T.TaxShare(vassal):P0}, levy {T.LevyShare(vassal):P0}, opinion {op:+0;-0;0}", w - 16 * S, 12), box.X + 8 * S, box.Y + 24 * S, 12, op < -50 ? Gui.Bad : Gui.TextDim);
            if (Gui.Hover(box)) { Gui.Tooltip($"{vassal.DisplayName}: loyalty {vassal.Loyalty:0}. Click to open their sheet."); if (Gui.Clicked(box)) OpenSheet(vassal); }
            y += 48 * S;
        }
        if (T.VassalsOf(p).Count == 0) y = ScText("No vassals.", x, y, w, 13);

        y += 6 * S;
        y = ScGovernors(p, x, y, w);
        return y;
    }

    // ===============================================================================================
    // Troops: mobilisation, levies, mercenaries, call to arms, the legion
    // ===============================================================================================
    private float ScTroops(Country? p, float x, float y, float w)
    {
        if (p == null) { Gui.Label("Observing: no country to act for.", x, y, 14, Gui.TextDim); return y + 24 * S; }
        var R = G.Raising;
        Gui.Section("Manpower", x, y, w);
        y += 26 * S;
        y += Gui.Wrapped($"Recruitable share of the people {R.RecruitableShare(p):P1} ({Num(R.ManpowerCap(p))} men at most). Set by the military service law.", x, y, w, 13, Gui.TextDim) + 6 * S;

        Gui.Section("Mobilisation", x, y, w);
        y += 26 * S;
        if (R.IsMobilised(p))
        {
            Gui.Label($"Mobilised: {R.MobilisedDaysLeft(p)} days left", x, y, 15, Gui.Gold, FontKind.Bold);
            Gui.Bar(new Rectangle(x + w * 0.5f, y + 5 * S, w * 0.5f, 9 * S), 1f - R.MobilisedDaysLeft(p) / 90f, Gui.Gold);
            y += 24 * S;
        }
        else if (R.RestDaysLeft(p) > 0) { Gui.Label($"The people rest: {R.RestDaysLeft(p)} days before the next mobilisation", x, y, 14, Gui.TextDim); y += 22 * S; }
        string? mwhy = R.CannotMobilise(p);
        double gold = R.MobilisationCost(p);
        if (Gui.Button(new Rectangle(x, y, w, 30 * S), $"Mobilise {Num(R.MobilisationMen(p))} men", mwhy == null, mwhy ?? $"+2% of the people under arms for 90 days, then 90 days of rest.\n-5 stability, {gold:0} gold.", false, 14))
            Confirm($"Mobilise {Num(R.MobilisationMen(p))} men for 90 days? It costs {gold:0} gold and 5 stability, and the people rest for 90 days afterwards.", () => ScResult(R.Mobilise(p), "The realm mobilises."));
        y += 38 * S;

        Gui.Section("Levies", x, y, w);
        y += 26 * S;
        if (R.HasLevies(p))
        {
            Gui.Label("Levies stand. They are disbanded 30 days after the peace.", x, y, 14, Gui.Gold);
            y += 22 * S;
            if (Gui.Button(new Rectangle(x, y, w, 28 * S), "Disband the levies now", true, "Send them home; the tax comes back", false, 14)) R.DisbandLevies(p);
            y += 36 * S;
        }
        else
        {
            string? lwhy = R.CannotRaiseLevies(p);
            if (Gui.Button(new Rectangle(x, y, w, 30 * S), $"Raise levies ({Num(R.LevyMen(p))} men)", lwhy == null, lwhy ?? "In a war: up to 1.5% of the people, free, at 70% strength and 30 organisation. Tax -10% while they stand.", false, 14))
                ScResult(R.RaiseLevies(p), "The levies are raised.");
            y += 38 * S;
        }

        Gui.Section("Mercenaries", x, y, w);
        y += 26 * S;
        foreach (var co in R.Companies.Where(c => c.Owner == p).ToList())
        {
            var box = new Rectangle(x, y, w, 32 * S);
            Card(box, new Color(36, 38, 44, 255));
            Gui.Label(Gui.Fit($"{co.Def.Name}: {co.Def.Divisions} divisions", w - 100 * S, 14), box.X + 8 * S, box.Y + 7 * S, 14, Gui.Text);
            var company = co;
            if (ScButton(box, 76 * S, "Dismiss", true, "Send them away; no more pay")) R.Dismiss(company);
            y += 36 * S;
        }
        Gui.Label($"Upkeep {R.MercenaryUpkeep(p):0} gold a month. {R.Companies.Count(c => c.Owner == p)} of {R.MaxCompanies} companies.", x, y, 13, Gui.TextDim);
        y += 22 * S;
        foreach (var def in R.Mercenaries.Values)
        {
            var box = new Rectangle(x, y, w, 40 * S);
            Card(box, new Color(36, 38, 44, 255));
            Gui.Label(Gui.Fit(def.Name, w - 130 * S, 14, FontKind.Bold), box.X + 8 * S, box.Y + 2 * S, 14, Gui.Text, FontKind.Bold);
            Gui.Label(Gui.Fit($"{def.Divisions} divisions, {def.Divisions * def.Cost:0} gold a month, {def.Quality:P0} quality", w - 130 * S, 12), box.X + 8 * S, box.Y + 21 * S, 12, Gui.TextDim);
            string? why = R.CannotHire(p, def);
            var d = def;
            if (ScButton(box, 60 * S, "Hire", why == null, why ?? def.Desc)) ScResult(R.Hire(p, d), $"The {d.Name} are hired.");
            y += 44 * S;
        }

        Gui.Section("Subjects and legion", x, y, w);
        y += 26 * S;
        string? cwhy = R.CannotCallToArms(p);
        if (Gui.Button(new Rectangle(x, y, w, 30 * S), "Call the subjects to arms", cwhy == null, cwhy ?? "Each subject sends up to a quarter of its divisions for the war; its liberty desire is +5 while they are away.", false, 14))
        {
            int sent = R.CallToArms(p);
            G.Notify($"{sent} divisions answer the call.", NotificationKind.Good);
        }
        y += 36 * S;
        bool legion = R.HasLegion(p);
        if (Gui.Button(new Rectangle(x, y, w, 30 * S), legion ? "Foreign legion: recruiting (click to stop)" : "Foreign legion: off (click to start)", true, $"Foreigners join a division at a time: 0.1% of the people a month.{(legion ? $" Pool {R.LegionPool(p):0}." : "")}", legion, 14))
            R.SetLegion(p, !legion);
        y += 38 * S;
        return y;
    }

    // ===============================================================================================
    // Armies: army groups
    // ===============================================================================================
    private float ScArmies(Country? p, float x, float y, float w)
    {
        if (p == null) { Gui.Label("Observing: no country to act for.", x, y, 14, Gui.TextDim); return y + 24 * S; }
        var O = G.ArmyOrg;
        Gui.Section("Army groups", x, y, w);
        y += 26 * S;
        y += Gui.Wrapped("A group gives orders and a theatre to all its armies at once; a field marshal adds 0.5% of their martial skill to every army in it. Armies with a second or third arm (infantry, cavalry, support) fight up to 12% better.", x, y, w, 13, Gui.TextDim) + 6 * S;
        if (Gui.Button(new Rectangle(x, y, w, 28 * S), "New army group", true, "A group of armies under a field marshal", false, 14)) O.CreateGroup(p);
        y += 36 * S;
        var enemies = G.World.WarsOf(p).SelectMany(war => war.EnemiesOf(p)).Distinct().Where(e => e.Exists).ToList();
        var selectedArmy = Ui.SelectedArmy ?? Ui.SelectedDivisions.Select(d => d.Army).FirstOrDefault(a => a != null);
        foreach (var grp in O.GroupsOf(p).ToList())
        {
            float h = 72 * S + grp.Armies.Count * 34 * S + 90 * S + (grp.Enemy != null || enemies.Count > 0 ? 0 : 0);
            var box = new Rectangle(x, y, w, h);
            Card(box, new Color(36, 38, 44, 255));
            float gx = box.X + 8 * S, gy = box.Y + 4 * S, gw = box.Width - 16 * S;
            Gui.Label(Gui.Fit(grp.Name, gw - 100 * S, 16, FontKind.Bold), gx, gy, 16, Gui.Gold, FontKind.Bold);
            var g2 = grp;
            if (Gui.Button(new Rectangle(gx + gw - 88 * S, gy, 88 * S, 24 * S), "Disband", true, "Dissolve the group; the armies stay", false, 12)) { O.DisbandGroup(g2); continue; }
            gy += 28 * S;
            string marshal = grp.Marshal != null ? $"{grp.Marshal.DisplayName}: +{O.MarshalBonus(grp):P1}" : "No field marshal";
            Gui.Label(Gui.Fit("Field marshal: " + marshal, gw - 110 * S, 14), gx, gy + 3 * S, 14, grp.Marshal != null ? Gui.Text : Gui.TextDim);
            if (Gui.Button(new Rectangle(gx + gw - 100 * S, gy, 100 * S, 24 * S), _scMarshalFor == grp ? "Cancel" : "Appoint", true, "Pick a field marshal from the court", _scMarshalFor == grp, 12)) _scMarshalFor = _scMarshalFor == grp ? null : grp;
            gy += 30 * S;
            foreach (var a in grp.Armies.ToList())
            {
                Gui.Label(Gui.Fit($"{a.Name}: {a.Divisions.Count} divisions", gw - 220 * S, 13), gx + 8 * S, gy + 4 * S, 13, Gui.Text);
                double bonus = O.CombinedArmsBonus(a);
                Gui.Label($"Combined arms {bonus:+0%;-0%;+0%}", gx + gw - 210 * S, gy + 4 * S, 12, bonus > 0 ? Gui.Good : Gui.TextDim);
                var rowR = new Rectangle(gx + 8 * S, gy, gw - 90 * S, 26 * S);
                if (Gui.Hover(rowR)) { Gui.Tooltip(O.DescribeBonus(a)); if (Gui.Clicked(rowR)) Ui.SelectedArmy = a; }
                var army = a;
                if (Gui.Button(new Rectangle(gx + gw - 76 * S, gy, 76 * S, 24 * S), "Remove", true, "Take the army out of the group", false, 12)) O.Remove(g2, army);
                gy += 30 * S;
            }
            if (selectedArmy != null && O.CannotAdd(grp, selectedArmy) is var addWhy)
                if (Gui.Button(new Rectangle(gx, gy, gw, 26 * S), $"Add {selectedArmy.Name}", addWhy == null, addWhy ?? "Add the selected army", false, 13)) O.Add(g2, selectedArmy);
            gy += 32 * S;
            // orders
            Gui.Label("Orders:", gx, gy + 4 * S, 13, Gui.TextDim);
            float ox = gx + 54 * S;
            foreach (var mode in new[] { ArmyMode.Hold, ArmyMode.Offensive, ArmyMode.Fallback })
            {
                string ml = mode.ToString();
                float bw = Gui.Measure(ml, 12).X + 18 * S;
                if (Gui.Button(new Rectangle(ox, gy, bw, 24 * S), ml, true, $"Every army of the group: {ml}", grp.Mode == mode, 12)) O.SetOrders(g2, grp.Enemy, mode);
                ox += bw + 3 * S;
            }
            gy += 28 * S;
            Gui.Label(Gui.Fit($"Front: {(grp.Enemy != null ? grp.Enemy.Name : "none")}   Theatre: {(grp.Theatre.Count > 0 ? $"{grp.Theatre.Count} locations" : "the whole border")}", gw, 13), gx, gy + 2 * S, 13, Gui.Text);
            gy += 24 * S;
            float ex = gx;
            foreach (var en in enemies.Take(5))
            {
                float bw = Gui.Measure(en.Name, 12).X + 18 * S;
                if (ex + bw > gx + gw) break;
                var foe = en;
                if (Gui.Button(new Rectangle(ex, gy, bw, 24 * S), en.Name, true, $"Front against {en.Name}", grp.Enemy == en, 12)) O.SetOrders(g2, foe, grp.Mode);
                ex += bw + 3 * S;
            }
            if (Ui.SelectedLocation is { } sl)
            {
                string tl = g2.Theatre.Contains(sl) ? "Drop location" : "Add location";
                float bw = Gui.Measure(tl, 12).X + 18 * S;
                if (Gui.Button(new Rectangle(gx + gw - bw - 54 * S, gy, bw, 24 * S), tl, true, "Add or remove the selected location to the theatre", false, 12))
                {
                    var set = g2.Theatre.ToHashSet();
                    if (!set.Remove(sl)) set.Add(sl);
                    O.SetTheatre(g2, set);
                }
            }
            if (Gui.Button(new Rectangle(gx + gw - 48 * S, gy, 48 * S, 24 * S), "All", true, "The whole border", false, 12)) O.SetTheatre(g2, Array.Empty<Location>());
            y += h + 6 * S;
            if (_scMarshalFor == grp)
            {
                foreach (var ch in ScCourt(p).Where(c => O.CannotSetMarshal(grp, c) == null).OrderByDescending(c => c.GetStat(G.Data.Defines.GeneralStat)).Take(6))
                {
                    var cb = new Rectangle(x + 14 * S, y, w - 14 * S, 28 * S);
                    Card(cb, new Color(30, 32, 38, 255));
                    Gui.Label(Gui.Fit($"{ch.DisplayName}, martial {ch.GetStat(G.Data.Defines.GeneralStat):0}", cb.Width - 100 * S, 13), cb.X + 8 * S, cb.Y + 6 * S, 13, Gui.Text);
                    var who = ch;
                    if (ScButton(cb, 76 * S, "Appoint", true, $"{ch.DisplayName} commands the group")) { ScResult(O.SetMarshal(g2, who)); _scMarshalFor = null; }
                    y += 32 * S;
                }
            }
        }
        var loose = G.World.ArmiesOf(p).Where(a => O.GroupOf(a) == null).ToList();
        if (loose.Count > 0)
        {
            Gui.Section("Armies outside a group", x, y, w);
            y += 26 * S;
            var groups = O.GroupsOf(p).ToList();
            foreach (var a in loose)
            {
                var box = new Rectangle(x, y, w, 32 * S);
                Card(box, Ui.SelectedArmy == a ? new Color(60, 52, 34, 255) : new Color(36, 38, 44, 255), Ui.SelectedArmy == a);
                Gui.Label(Gui.Fit($"{a.Name}: {a.Divisions.Count} divisions", w - 40 * S - groups.Count * 34 * S, 13), box.X + 8 * S, box.Y + 7 * S, 13, Gui.Text);
                double bonus = O.CombinedArmsBonus(a);
                if (Gui.Hover(box)) { Gui.Tooltip(O.DescribeBonus(a) + (groups.Count > 0 ? "\nNumbers add it to that army group" : "")); if (Gui.Clicked(new Rectangle(box.X, box.Y, box.Width - groups.Count * 34 * S, box.Height))) Ui.SelectedArmy = a; }
                for (int i = 0; i < groups.Count; i++)
                {
                    var g3 = groups[i];
                    var army = a;
                    if (Gui.Button(new Rectangle(box.X + box.Width - (groups.Count - i) * 34 * S, box.Y + 4 * S, 30 * S, 24 * S), (i + 1).ToString(), O.CannotAdd(g3, a) == null, $"Add to the {g3.Name}", false, 12)) O.Add(g3, army);
                }
                y += 36 * S;
            }
        }
        return y;
    }

    private float DrawSchemeRow(Scheme s, float x, float y, float w, bool cancelable)
    {
        var I = G.Intrigue;
        var box = new Rectangle(x, y, w, 46 * S);
        Card(box, new Color(36, 38, 44, 255));
        Gui.Label(Gui.Fit($"{s.Def.Name}: {s.Owner.DisplayName} against {s.Target.DisplayName}", w - 130 * S, 13, FontKind.Bold), box.X + 8 * S, box.Y + 3 * S, 13, Gui.Text, FontKind.Bold);
        Gui.Label($"{s.Progress:0}%", box.X + 8 * S, box.Y + 25 * S, 12, Gui.TextDim);
        Gui.Bar(new Rectangle(box.X + 70 * S, box.Y + 29 * S, box.Width - 190 * S, 8 * S), (float)Math.Clamp(s.Progress / 100, 0, 1), new Color(120, 160, 220, 255));
        double disc = I.DiscoveryOverall(s);
        DrawDiscoveryRing(new Vector2(box.X + box.Width - 90 * S, box.Y + 23 * S), 11 * S, disc);
        if (Gui.Hover(new Rectangle(box.X + box.Width - 104 * S, box.Y + 9 * S, 28 * S, 28 * S)))
            Gui.Tooltip($"Chance to be found out before it is done: {disc:P0}");
        if (cancelable)
        {
            var sc = s;
            if (Gui.Button(new Rectangle(box.X + box.Width - 68 * S, box.Y + 10 * S, 62 * S, 24 * S), "Cancel", true, "Drop the scheme", false, 12)) I.Cancel(sc);
        }
        return y + 50 * S;
    }

    /// <summary>A ring that fills with the chance of being found out.</summary>
    private static void DrawDiscoveryRing(Vector2 c, float r, double fraction)
    {
        Raylib.DrawRing(c, r * 0.62f, r, 0, 360, 36, new Color(0, 0, 0, 170));
        var col = fraction > 0.5 ? new Color(226, 70, 56, 255) : fraction > 0.25 ? new Color(236, 188, 70, 255) : new Color(110, 200, 120, 255);
        Raylib.DrawRing(c, r * 0.62f, r, -90, -90 + 360 * (float)Math.Clamp(fraction, 0, 1), 36, col);
    }

    // ===============================================================================================
    // Character sheet: stress, titles and offices, royal marriage, intrigue
    // ===============================================================================================
    private State? _scGrantFor;
    private bool _scGovFor;

    /// <summary>The stress bar over a portrait.</summary>
    private void DrawIntrigueOverlay(Character ch, Rectangle pr)
    {
        if (!G.Intrigue.Enabled) return;
        double stress = G.Intrigue.Stress(ch);
        if (stress <= 0) return;
        var bar = new Rectangle(pr.X + 3 * S, pr.Y + pr.Height - 9 * S, pr.Width - 6 * S, 6 * S);
        Raylib.DrawRectangleRec(new Rectangle(bar.X - 1, bar.Y - 1, bar.Width + 2, bar.Height + 2), new Color(0, 0, 0, 200));
        Gui.Bar(bar, (float)(stress / 100), stress > 66 ? Gui.Bad : stress > 33 ? Gui.Gold : new Color(120, 170, 220, 255));
        if (Gui.Hover(pr)) Gui.Tooltip($"Stress {stress:0} of 100. Schemes add to it; at the top a character breaks down.");
    }

    /// <summary>The titles a character holds, their offices, and what the player can give them.</summary>
    private float DrawTitlesSection(Character ch, float x, float y, float w)
    {
        if (!G.Titles.Enabled || ch.Country == null || !ch.Alive) return y;
        var T = G.Titles;
        var country = ch.Country;
        var player = G.World.Player;
        bool mine = country == player;
        string power = G.Politics.Enabled ? G.Politics.PowerName : "gold";
        var held = T.TitlesHeldBy(ch).ToList();
        var governs = T.Governors.Where(g => g.Governor == ch && g.Country == country).ToList();
        var posts = G.Politics.Positions.Where(q => q.Council && G.Politics.MinisterOf(country, q) == ch).ToList();
        bool canGive = mine && !ch.IsRuler && country.Regent != ch && ch.Age(G.World.Date) >= G.Data.Defines.AdultAge;
        if (held.Count == 0 && governs.Count == 0 && posts.Count == 0 && !canGive) return y;
        Gui.Section("Titles and offices", x, y, w);
        y += 26 * S;
        if (mine && held.Count > 0 && (G.Succession.Enabled || G.Dynasties.Enabled))
        {
            if (Gui.Button(new Rectangle(x, y, w, 26 * S), "Succession and heirs", true, "Succession law, heirs and the dynasty of the country", false, 13)) { Ui.SelectedCountry = country; Ui.Left = LeftPanel.Politics; PoliticsTab = 8; }
            y += 30 * S;
        }
        foreach (var t in held.Where(t => t.Tier != TitleTier.County))
        {
            Gui.Label(Gui.Fit($"{t.Tier}: {t.Name}", w, 14), x, y, 14, Gui.Gold, FontKind.Bold);
            y += 20 * S;
        }
        var counties = held.Where(t => t.Tier == TitleTier.County && t.State != null).Select(t => t.State!).ToList();
        if (counties.Count > 0)
        {
            bool vassal = !ch.IsRuler;
            if (vassal) y = ScText($"Vassal: keeps all but {T.TaxShare(ch):P0} of the tax and {T.LevyShare(ch):P0} of the levies of {counties.Count} counties. Opinion of the ruler {(G.Intrigue.Enabled ? G.Intrigue.OpinionOfRuler(ch) : ch.Loyalty - 50):+0;-0;0}.", x, y, w, 12);
            foreach (var st in counties.Take(vassal ? 20 : 3))
            {
                Gui.Label(Gui.Fit($"County: {st.DisplayName}", w - 100 * S, 13), x, y + 3 * S, 13, Gui.Text);
                if (vassal && mine)
                {
                    string? why = T.CannotRevoke(country, st);
                    var state = st;
                    if (Gui.Button(new Rectangle(x + w - 90 * S, y, 90 * S, 22 * S), "Revoke", why == null, why ?? $"Take it back for {T.RevokeCost:0} {power}; their loyalty drops", false, 12))
                        Confirm($"Take {st.DisplayName} back from {ch.DisplayName} for {T.RevokeCost:0} {power}?", () => ScResult(T.Revoke(country, state)));
                }
                y += 26 * S;
            }
            if (ch.IsRuler && counties.Count > 3) { Gui.Label($"and {counties.Count - 3} more counties", x, y, 12, Gui.TextDim); y += 18 * S; }
        }
        foreach (var g in governs)
        {
            Gui.Label(Gui.Fit($"Governor of {g.Duchy.Name}", w - 100 * S, 13), x, y + 3 * S, 13, Gui.Text);
            if (mine && Gui.Button(new Rectangle(x + w - 90 * S, y, 90 * S, 22 * S), "Dismiss", true, "Dismiss the governor", false, 12)) T.DismissGovernor(country, g.Duchy);
            y += 26 * S;
        }
        foreach (var post in posts)
        {
            Gui.Label(Gui.Fit($"Council: {post.Name}", w, 13), x, y, 13, Gui.Text);
            y += 20 * S;
        }
        if (canGive)
        {
            var direct = T.DirectStates(country).Where(s => s != country.Capital?.State).OrderByDescending(s => s.Locations.Count).Take(8).ToList();
            if (direct.Count > 0)
            {
                if (Gui.Button(new Rectangle(x, y, w, 26 * S), _scGrantFor != null ? "Cancel granting" : $"Grant a county ({T.GrantCost:0} {power})", true, "Make them a vassal of a county you hold directly", _scGrantFor != null, 13))
                    _scGrantFor = _scGrantFor != null ? null : direct[0];
                y += 30 * S;
                if (_scGrantFor != null)
                {
                    float fx = x;
                    foreach (var st in direct)
                    {
                        string label = Gui.Fit(st.DisplayName, 120 * S, 12);
                        float bw = Gui.Measure(label, 12).X + 16 * S;
                        if (fx + bw > x + w && fx > x) { fx = x; y += 28 * S; }
                        if (Gui.Button(new Rectangle(fx, y, bw, 24 * S), label, true, $"Pick {st.DisplayName}", _scGrantFor == st, 12)) _scGrantFor = st;
                        fx += bw + 4 * S;
                    }
                    y += 30 * S;
                    Gui.Label($"Tax share {_scTax:P0}", x, y, 13, Gui.Text);
                    Gui.Slider("sc_tax", new Rectangle(x + 120 * S, y + 4 * S, w - 130 * S, 14 * S), ref _scTax, 0.1, 0.3);
                    y += 24 * S;
                    Gui.Label($"Levy share {_scLevy:P0}", x, y, 13, Gui.Text);
                    Gui.Slider("sc_levy", new Rectangle(x + 120 * S, y + 4 * S, w - 130 * S, 14 * S), ref _scLevy, 0.1, 0.3);
                    y += 26 * S;
                    string? why = T.CannotGrant(country, _scGrantFor, ch);
                    var gs = _scGrantFor;
                    if (Gui.Button(new Rectangle(x, y, w, 28 * S), $"Give {gs.DisplayName} to {ch.DisplayName}", why == null || why.StartsWith("Needs"), why ?? "Grant the county", false, 13))
                        Confirm($"Give {gs.DisplayName} to {ch.DisplayName} for {T.GrantCost:0} {power}? They keep all but {_scTax:P0} of the tax and {_scLevy:P0} of the levies.", () => { ScResult(T.Grant(country, gs, ch, _scTax, _scLevy)); _scGrantFor = null; });
                    y += 34 * S;
                }
            }
            var regions = T.RegionsOf(country).Where(d => T.GovernorOf(country, d)?.Governor != ch).Take(10).ToList();
            if (regions.Count > 0)
            {
                if (Gui.Button(new Rectangle(x, y, w, 26 * S), _scGovFor ? "Cancel" : $"Appoint governor ({T.GovernorCost:0} {power})", true, "One governor for each region: tax +1% a point of finesse", _scGovFor, 13)) _scGovFor = !_scGovFor;
                y += 30 * S;
                if (_scGovFor)
                {
                    float fx = x;
                    foreach (var duchy in regions)
                    {
                        string? why = T.CannotAppointGovernor(country, duchy, ch);
                        string label = Gui.Fit(duchy.Name, 130 * S, 12);
                        float bw = Gui.Measure(label, 12).X + 16 * S;
                        if (fx + bw > x + w && fx > x) { fx = x; y += 28 * S; }
                        var d = duchy;
                        if (Gui.Button(new Rectangle(fx, y, bw, 24 * S), label, why == null, why ?? $"{ch.DisplayName} governs {duchy.Name}", false, 12))
                            Confirm($"Appoint {ch.DisplayName} governor of {duchy.Name} for {T.GovernorCost:0} {power}?", () => { ScResult(T.AppointGovernor(country, d, ch)); _scGovFor = false; });
                        fx += bw + 4 * S;
                    }
                    y += 30 * S;
                }
            }
        }
        return y + 4 * S;
    }

    /// <summary>Royal marriage: arranged from the sheet of a character of another house.</summary>
    private float DrawMarriageSection(Character ch, float x, float y, float w)
    {
        if (!G.Marriages.Enabled || ch.Country == null || !ch.Alive) return y;
        var player = G.World.Player;
        if (player == null || ch.Country == player && !G.Marriages.Of(player).Any()) return y;
        var M = G.Marriages;
        Gui.Section("Royal marriage", x, y, w);
        y += 26 * S;
        if (ch.Country == player)
        {
            foreach (var m in M.Of(player)) y = ScText($"Married into the house of {m.Other(player).Name} since {new GameDate(m.Day).ToDisplay()}.", x, y, w, 13, Gui.Text);
            return y;
        }
        var existing = M.Between(player, ch.Country);
        if (existing != null) return ScText($"The ruling houses of {player.Name} and {ch.Country.Name} are joined (since {new GameDate(existing.Day).ToDisplay()}).", x, y, w, 13, Gui.Text);
        var action = G.Data.DiplomaticActions.GetValueOrDefault("royal_marriage");
        if (action == null) return ScText("Royal marriages are not defined in this game.", x, y, w, 13);
        var target = ch.Country;
        var reqs = G.Diplomacy.Requirements(action, player, target);
        string tip = Localisation.Get(action.Desc) + (reqs.Count > 0 ? "\n\n" + string.Join("\n", reqs.Select(q => (q.Ok ? "[OK] " : "[X] ") + q.Text)) : "");
        if (ScAct(ref y, x, w, $"Propose a marriage with {ch.DisplayName}'s house", reqs.All(q => q.Ok), tip))
            Ui.SetStatus($"{Localisation.Get(action.Name)}: {G.Diplomacy.Use(action, player, target)}");
        return y;
    }

    /// <summary>The schemes of a character, the schemes against them, plots, and the buttons to start one.</summary>
    private float DrawIntrigueSection(Character ch, float x, float y, float w)
    {
        if (!G.Intrigue.Enabled) return y;
        var I = G.Intrigue;
        var player = G.World.Player;
        var ruler = player?.Ruler;
        bool isOurRuler = ruler != null && ch == ruler;
        // our own schemes are listed on the sheet of our ruler; any other character shows the schemes they run for us, and those aimed at them
        var own = I.Schemes.Where(s => s.Owner.Country == player && (isOurRuler || s.Owner == ch)).ToList();
        var against = I.Schemes.Where(s => s.Target == ch && s.Owner.Country == player && !own.Contains(s)).ToList();
        var plots = isOurRuler && player != null ? I.Plots.Where(p => p.Country == player).ToList() : I.PlotsInvolving(ch).ToList();
        bool canStart = ruler != null && ch.Alive && ch != ruler;
        if (own.Count == 0 && against.Count == 0 && plots.Count == 0 && !canStart) return y;
        Gui.Section("Intrigue", x, y, w);
        y += 26 * S;
        foreach (var s in own) y = DrawSchemeRow(s, x, y, w, true);
        foreach (var s in against) y = DrawSchemeRow(s, x, y, w, true);
        foreach (var pl in plots)
        {
            string text = isOurRuler ? (pl.Goal == "independence" ? $"{pl.Leader.DisplayName} plots to break away ({pl.Size} members)" : $"{pl.Leader.DisplayName} plots against you ({pl.Size} members)")
                : pl.Leader == ch ? $"Leads a plot ({pl.Size} members)" : $"Member of a plot led by {pl.Leader.DisplayName}";
            Gui.Label(Gui.Fit(text, w - 60 * S, 13), x, y, 13, Gui.Bad);
            if (isOurRuler && Gui.Button(new Rectangle(x + w - 56 * S, y - 2 * S, 56 * S, 22 * S), "Sheet", true, "Open the leader's sheet", false, 12)) OpenSheet(pl.Leader);
            y += 24 * S;
            if (isOurRuler) { Gui.Bar(new Rectangle(x, y - 6 * S, w, 6 * S), (float)Math.Clamp(pl.Progress / 100, 0, 1), Gui.Bad); y += 6 * S; }
        }
        if (canStart)
        {
            float bx = x;
            foreach (var def in I.Defs.Values)
            {
                string? why = I.CannotStart(ruler!, def, ch);
                float bw = Gui.Measure(def.Name, 13, FontKind.Bold).X + 22 * S;
                if (bx + bw > x + w && bx > x) { bx = x; y += 30 * S; }
                var d = def;
                var owner = ruler!;
                if (Gui.Button(new Rectangle(bx, y, bw, 26 * S), def.Name, why == null, why ?? $"{def.Desc}\n{def.Months:0} months. Stress +{def.Stress:0}. It may be found out.", false, 13))
                    ScResult(I.Start(owner, d, ch), $"{d.Name} against {ch.DisplayName} begins.");
                bx += bw + 4 * S;
            }
            y += 34 * S;
        }
        return y + 4 * S;
    }

    private Title? _scGovPick;

    /// <summary>A candidate: portrait, name (opens the sheet), a line of facts and a button.</summary>
    private float ScCandidateRow(Character ch, string info, string button, string? why, string tip, Action click, float x, float y, float w)
    {
        var box = new Rectangle(x, y, w, 36 * S);
        Card(box, new Color(30, 32, 38, 255));
        var pr = new Rectangle(box.X + 3 * S, box.Y + 3 * S, 30 * S, 30 * S);
        DrawPortrait(ch, pr, false, false);
        var nameR = new Rectangle(box.X + 38 * S, box.Y, w - 130 * S, box.Height);
        Gui.Label(Gui.Fit(ch.DisplayName, nameR.Width, 13, FontKind.Bold), nameR.X, box.Y + 2 * S, 13, Gui.Text, FontKind.Bold);
        Gui.Label(Gui.Fit(info, nameR.Width, 12), nameR.X, box.Y + 19 * S, 12, Gui.TextDim);
        if (Gui.Hover(nameR)) { Gui.Tooltip($"{ch.DisplayName}: click to open the sheet\n" + string.Join("  ", G.Data.Defines.CharacterStats.Select(st => $"{st} {ch.GetStat(st):0}"))); if (Gui.Clicked(nameR)) OpenSheet(ch); }
        if (ScButton(box, 80 * S, button, why == null, why ?? tip)) click();
        return y + 40 * S;
    }

    private float ScCouncil(Country p, float x, float y, float w)
    {
        var P = G.Politics;
        Gui.Section("Council", x, y, w);
        y += 26 * S;
        y = ScText($"Five posts held by any adult of the country, whatever their party. Each minister adds {2}% of their skill to the post's effect; appointing costs {P.PowerName}.", x, y, w, 13);
        foreach (var post in P.Positions.Where(q => q.Council).ToList())
        {
            var m = P.MinisterOf(p, post);
            var head = new Rectangle(x, y, w, 26 * S);
            Gui.Label(Gui.Fit($"{post.Name} ({Localisation.GetOrPretty(post.Stat)})", w - 110 * S, 14, FontKind.Bold), x, y + 3 * S, 14, Gui.Gold, FontKind.Bold);
            if (Gui.Hover(new Rectangle(x, y, w - 110 * S, 26 * S)))
                Gui.Tooltip($"{post.Name}\nGood ministers have high {Localisation.GetOrPretty(post.Stat)}.\nPer point: {string.Join(", ", post.PerPoint.Select(kv => ModifierText(kv.Key, kv.Value)))}");
            if (Gui.Button(new Rectangle(x + w - 100 * S, y, 100 * S, 24 * S), m == null ? "Appoint" : "Replace", true, $"Choose a councillor ({post.Cost:0} {P.PowerName})", _cabinetPick == post.Key, 13))
                _cabinetPick = _cabinetPick == post.Key ? null : post.Key;
            y += 30 * S;
            if (m != null) y += CharRow(m, x, y, w, $"{Localisation.GetOrPretty(post.Stat)} {m.GetStat(post.Stat):0}") + 2 * S;
            else { Gui.Label("Vacant", x + 8 * S, y, 13, Gui.TextDim); y += 22 * S; }
            if (_cabinetPick == post.Key)
                foreach (var cand in P.Candidates(p, post).Take(6).ToList())
                {
                    var pos = post; var who = cand;
                    string? why = P.CannotAppoint(p, post, cand);
                    y = ScCandidateRow(cand, $"{Localisation.GetOrPretty(post.Stat)} {cand.GetStat(post.Stat):0}, age {cand.Age(G.World.Date)}", "Appoint", why, $"Appoint for {post.Cost:0} {P.PowerName}",
                        () => Confirm($"Appoint {who.DisplayName} to {pos.Name} for {pos.Cost:0} {P.PowerName}?", () => { var err = P.Appoint(p, pos, who); if (err != null) Ui.SetStatus(err); else _cabinetPick = null; }), x + 12 * S, y, w - 12 * S);
                }
            y += 4 * S;
        }
        return y;
    }

    private float ScGovernors(Country p, float x, float y, float w)
    {
        var T = G.Titles;
        string power = G.Politics.Enabled ? G.Politics.PowerName : "gold";
        Gui.Section("Governors", x, y, w);
        y += 26 * S;
        y = ScText($"One governor for each region: tax +1% for each point of their finesse; -1 loyalty a year after 5 years in office. Appointing costs {T.GovernorCost:0} {power}. Governors are chosen for you at the start of the game.", x, y, w, 13);
        foreach (var duchy in T.RegionsOf(p))
        {
            var gov = T.GovernorOf(p, duchy);
            Gui.Label(Gui.Fit(duchy.Name, w - 130 * S, 14, FontKind.Bold), x, y + 3 * S, 14, Gui.Gold, FontKind.Bold);
            if (Gui.Button(new Rectangle(x + w - 120 * S, y, 120 * S, 24 * S), gov == null ? "Fill vacancy" : "Replace", true, $"Choose a governor for {duchy.Name} ({T.GovernorCost:0} {power})", _scGovPick == duchy, 13))
                _scGovPick = _scGovPick == duchy ? null : duchy;
            y += 30 * S;
            if (gov != null) y += CharRow(gov.Governor, x, y, w, $"finesse {gov.Governor.GetStat("finesse"):0}, since {new GameDate(gov.Since).ToDisplay()}") + 2 * S;
            else { Gui.Label("Vacant", x + 8 * S, y, 13, Gui.Bad); y += 22 * S; }
            if (_scGovPick == duchy)
            {
                var cands = ScCourt(p).Where(c => !c.IsRuler).OrderByDescending(c => c.GetStat("finesse")).Take(8).ToList();
                foreach (var cand in cands)
                {
                    var d = duchy; var who = cand;
                    string? why = T.CannotAppointGovernor(p, duchy, cand);
                    y = ScCandidateRow(cand, $"finesse {cand.GetStat("finesse"):0}, age {cand.Age(G.World.Date)}", "Appoint", why, $"{cand.DisplayName} governs {duchy.Name}",
                        () => Confirm($"Appoint {who.DisplayName} governor of {d.Name} for {T.GovernorCost:0} {power}?", () => { ScResult(T.AppointGovernor(p, d, who)); _scGovPick = null; }), x + 12 * S, y, w - 12 * S);
                }
                if (cands.Count == 0) { Gui.Label("No one at court can govern.", x + 12 * S, y, 13, Gui.TextDim); y += 22 * S; }
            }
            y += 4 * S;
        }
        return y;
    }

    /// <summary>The vassal and the governor who rule a state, as rows that open their sheets (location panel).</summary>
    private float DrawStateRulers(State st, float x, float y, float w)
    {
        if (!G.Titles.Enabled || st.Owner == null) return 0;
        float used = 0;
        var T = G.Titles;
        var vassal = T.VassalOf(st);
        var gov = T.DuchyOf(st) is { } duchy ? T.GovernorOf(st.Owner, duchy) : null;
        if (vassal != null) { Gui.Label("VASSAL (HOLDS THE COUNTY)", x, y + used, 11, Gui.TextDim, FontKind.Bold); used += 14 * S; used += CharRow(vassal, x, y + used, w, $"tax {T.TaxShare(vassal):P0}, levy {T.LevyShare(vassal):P0}"); }
        if (gov != null) { Gui.Label($"GOVERNOR OF {gov.Duchy.Name.ToUpperInvariant()}", x, y + used, 11, Gui.TextDim, FontKind.Bold); used += 14 * S; used += CharRow(gov.Governor, x, y + used, w, $"finesse {gov.Governor.GetStat("finesse"):0}"); }
        else if (st.Owner == G.World.Player && T.DuchyOf(st) != null) { Gui.Label("No governor in this region.", x, y + used, 13, Gui.TextDim); used += 20 * S; }
        return used + (used > 0 ? 6 * S : 0);
    }

    // ===============================================================================================
    // Banners under the top bar and the peace conference window
    // ===============================================================================================
    private void DrawStatecraftBanners()
    {
        var p = G.World.Player;
        if (p == null || Ui.EditorActive) return;
        var lines = new List<(string Text, Color Tint, string Tip, int Tab)>();
        if (G.Raising.Enabled && G.Raising.IsMobilised(p))
            lines.Add(($"Mobilised: {G.Raising.MobilisedDaysLeft(p)} days left", Gui.Gold, "The realm is under arms. -5 stability was paid; afterwards the people rest 90 days.", 3));
        if (G.Raising.Enabled && G.Raising.HasLevies(p))
            lines.Add(("Levies stand: tax -10%", Gui.Gold, "Disbanded 30 days after the peace.", 3));
        if (G.Reputation.Enabled && G.Reputation.CoalitionAgainst(p) is { } coal)
            lines.Add(($"Coalition against you: {coal.Members.Count} countries", Gui.Bad, string.Join(", ", coal.Members.Select(c => c.Name)) + "\nIt dissolves when your aggressive expansion falls below 10.", 1));
        if (G.Factions.Enabled && G.Factions.FactionOf(p).Count > 1 && G.Factions.Goals.TryGetValue(G.Factions.LeaderOf(p), out var goal) && G.Factions.DaysLeft(goal) > 0)
            lines.Add(($"Faction goal: war on {goal.Target.Name} ({G.Factions.DaysLeft(goal)} days)", G.Factions.Supports(p, goal) ? Gui.Good : Gui.Gold, "Members that do not join lose 25 opinion with the leader.", 1));
        if (G.GreatPowers.Enabled)
            foreach (var cr in G.GreatPowers.Crises.Where(c => c.Powers.Contains(p) || c.Target == p))
                lines.Add(($"Crisis over {cr.Target.Name}", Gui.Bad, "Two great powers have more than 50 influence there.", 0));
        if (G.Conferences.Enabled)
            foreach (var c in G.Conferences.Active.Where(c => c.Leader == p || c.Loser == p || c.Turns.Any(t => t.Winner == p)))
                lines.Add(($"Peace conference: {G.Conferences.DaysToNext(c)} days to the next turn", Gui.Gold, "Click to see the conference.", -1));
        if (lines.Count == 0) return;
        int sw = Raylib.GetScreenWidth();
        float bw = 360 * S, bh = 24 * S, y = TopH + 6 * S;
        // the left panels start at the left edge, the right ones at the right: the banners sit between them
        float x = (sw - bw) / 2;
        foreach (var l in lines.Take(5))
        {
            var r = new Rectangle(x, y, bw, bh);
            Raylib.DrawRectangleRounded(r, 0.3f, 6, new Color(18, 18, 22, 220));
            Raylib.DrawRectangleRoundedLinesEx(r, 0.3f, 6, 1.5f, l.Tint);
            Gui.Label(Gui.Fit(l.Text, bw - 16 * S, 13, FontKind.Bold), r.X + 8 * S, r.Y + 3 * S, 13, l.Tint, FontKind.Bold);
            Gui.Block(r);
            if (Gui.Hover(r))
            {
                Gui.Tooltip(l.Tip);
                if (Gui.Clicked(r))
                {
                    switch (l.Tab)
                    {
                        case < 0: _scConferencesClosed.Clear(); break;
                        case 0: _worldTab = 1; Ui.Right = RightPanel.World; break;
                        case 3: _armyTab = 2; Ui.Left = LeftPanel.Military; break;
                        default: _countryTabTag = p.Tag; _countryTab = G.Politics.Enabled ? 3 : 2; Ui.SelectedCountry = p; Ui.Left = LeftPanel.Country; break;
                    }
                }
            }
            y += bh + 3 * S;
        }
    }

    private void DrawConferenceWindow()
    {
        var p = G.World.Player;
        if (p == null || !G.Conferences.Enabled || Ui.EditorActive) return;
        var conf = G.Conferences.Active.FirstOrDefault(c => !_scConferencesClosed.Contains(c.War.Id) && (c.Leader == p || c.Loser == p || c.Turns.Any(t => t.Winner == p)));
        if (conf == null) return;
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        float w = 520 * S, h = 150 * S + Math.Max(1, conf.Turns.Count) * 40 * S;
        var r = new Rectangle((sw - w) / 2, TopH + 120 * S, w, h);
        Gui.Panel(r, "Peace conference");
        Gui.Block(r);
        if (Gui.CloseButton(r)) { _scConferencesClosed.Add(conf.War.Id); return; }
        float x = r.X + 14 * S, y = r.Y + Gui.HeaderHeight + 8 * S, cw = r.Width - 28 * S;
        Gui.Flag(conf.Leader, new Rectangle(x, y, 40 * S, 27 * S));
        Gui.Label($"{conf.Leader.Name} against {conf.Loser.Name}", x + 50 * S, y + 3 * S, 17, Gui.Gold, FontKind.Bold);
        y += 34 * S;
        y += Gui.Wrapped("The war is under an armistice. Each victor takes a turn of 3 days to put its demands, the biggest winner first, with its share of the war score.", x, y, cw, 13, Gui.TextDim) + 6 * S;
        var current = G.Conferences.Current(conf);
        foreach (var t in conf.Turns)
        {
            var box = new Rectangle(x, y, cw, 36 * S);
            Card(box, t == current ? new Color(60, 52, 34, 255) : new Color(36, 38, 44, 255), t == current);
            Gui.Flag(t.Winner, new Rectangle(box.X + 6 * S, box.Y + 5 * S, 38 * S, 26 * S));
            Gui.Label(Gui.Fit(t.Winner.Name, cw * 0.3f, 15, FontKind.Bold), box.X + 52 * S, box.Y + 2 * S, 15, t.Winner == p ? Gui.Gold : Gui.Text, FontKind.Bold);
            Gui.Label($"{t.Points:0} points", box.X + 52 * S, box.Y + 20 * S, 12, Gui.TextDim);
            string status = t.Done ? (t.Result.Length > 0 ? t.Result : "Done") : t == current ? (G.Conferences.DaysToNext(conf) > 0 ? $"in {G.Conferences.DaysToNext(conf)} days" : "now") : "waiting";
            Gui.Label(Gui.Fit(status, cw * 0.45f, 13), box.X + cw * 0.5f, box.Y + 10 * S, 13, t.Done ? Gui.Good : t == current ? Gui.Gold : Gui.TextDim);
            y += 40 * S;
        }
    }
}
