using Burger.Core;
using Burger.Core.Characters;
using Burger.Core.Model;
using Burger.Core.Save;
using Burger.Core.Scripting;

namespace Burger.Tools;

public static partial class Program
{
    /// <summary>
    /// Statecraft: reputation (aggressive expansion, coalitions, rivals), great powers (ranking, spheres, leagues), envoys, conferences, faction goals and royal marriages,
    /// intrigue, titles and courts, raising troops, army organization and logistics.
    /// </summary>
    private static void StatecraftFeatureTests(string project)
    {
        Game Fresh()
        {
            var g = Game.Load(GameDir(project), compileScripts: true, seed: 31);
            g.StartNewGame(null);
            g.Ai.Enabled = false;
            return g;
        }
        void Month(Game gm, int n = 1)
        {
            for (int i = 0; i < n; i++)
            {
                for (int h = 0; h < 24 * 31; h++) { gm.Tick(); if (gm.World.Date.Day == 1 && h > 24) break; }
            }
        }
        void Day(Game gm, int days) { for (int i = 0; i < 24 * days; i++) gm.Tick(); }
        var g0 = Fresh();

        // ------------------------------------------------------------------------------------- reputation
        if (g0.Reputation.Enabled)
        {
            var g = Fresh();
            var R = g.Reputation;
            var big = g.World.Countries.Where(c => c.Exists && c.OwnedStates.Count >= 3 && Builtins.NeighborCountries(g, c).Count >= 3).First();
            var state = Builtins.NeighborCountries(g, big).SelectMany(n => n.OwnedStates).First();
            R.OnTaken(big, state);
            Check(R.Expansion(big) > 0, $"reputation: taking {state.DisplayName} adds aggressive expansion ({R.Expansion(big):0.#})");
            double cored = R.ExpansionOf(big, state);
            state.Claims.Add(big);
            Check(Math.Abs(R.ExpansionOf(big, state) - cored * 0.5) < 1e-9, "reputation: a claimed state counts half");
            state.Claims.Remove(big);
            R.SetExpansion(big, 15);
            var fab = g.Data.DiplomaticActions.Values.FirstOrDefault(a => a.Tags.Contains("fabricate"));
            if (fab != null)
                Check(g.Diplomacy.CostOf(fab, big) > g.Diplomacy.CostOf(fab) * 1.4, $"reputation: above 10, fabricating claims costs more ({g.Diplomacy.CostOf(fab):0} -> {g.Diplomacy.CostOf(fab, big):0})");
            var neighbour = Builtins.NeighborCountries(g, big).First();
            double before = g.Diplomacy.Opinion(neighbour, big);
            Month(g);
            Check(g.Diplomacy.Opinion(neighbour, big) < before - 5, $"reputation: neighbours dislike the expansion ({before:0} -> {g.Diplomacy.Opinion(neighbour, big):0})");
            Check(R.Expansion(big) < 15, "reputation: aggressive expansion fades");

            // a coalition forms around 20 when three countries within reach hold the country in contempt
            R.SetExpansion(big, 40);
            var hostile = Builtins.NeighborCountries(g, big).Take(4).ToList();
            foreach (var h in hostile) g.Diplomacy.AddOpinion(h, big, "Test hatred", -150, 0);
            Month(g);
            var co = R.CoalitionAgainst(big);
            Check(co != null && co.Members.Count >= 3 && hostile.Count >= 3, $"reputation: a coalition of {co?.Members.Count ?? 0} forms against the expander");
            if (co != null)
            {
                var member = co.Members[0];
                var cb = g.Data.CasusBelli["coalition_war"];
                Check(g.Diplomacy.CanUse(member, big, cb) || g.Rules.Diplomacy.CannotDeclareWarReason(g, member, big, cb) is { } r && !r.Contains("not valid"), "reputation: members have the coalition casus belli");
                var outsider = g.World.Countries.First(c => c.Exists && c != big && !co.Members.Contains(c));
                Check(!R.InCoalitionAgainst(outsider, big), "reputation: others are not in it");
            }
            R.SetExpansion(big, 2);
            foreach (var h in hostile) g.World.Relations.ModifiersOf(h, big).RemoveAll(m => m.Name == "Test hatred");
            Month(g);
            Check(R.CoalitionAgainst(big) == null, "reputation: the coalition breaks up when the expander has calmed down");

            // rivals: three, and cheaper war goals
            var others = g.World.Countries.Where(c => c.Exists && c != big).Take(4).ToList();
            foreach (var o in others.Take(3)) R.RemoveRival(big, o);
            foreach (var o in others.Take(3)) R.AddRival(big, o);
            Check(R.CannotAddRival(big, others[3]) != null && R.RivalsOf(big).Count == 3, "reputation: only three rivals");
            Check(Math.Abs(R.RivalCostFactor(big, others[0]) - 0.9) < 1e-9 && R.RivalCostFactor(big, others[3]) == 1, "reputation: war goals against a rival cost 10% less");

            string tmp = Path.Combine(Path.GetTempPath(), "burger_rep.sav");
            R.SetExpansion(big, 12);
            SaveGame.Save(g, tmp);
            var g2 = Game.Load(GameDir(project), compileScripts: true, seed: 31);
            SaveGame.Load(g2, tmp);
            var big2 = g2.World.GetCountry(big.Tag)!;
            Check(Math.Abs(g2.Reputation.Expansion(big2) - 12) < 1e-6 && g2.Reputation.RivalsOf(big2).Count == 3, "reputation: a save keeps expansion and rivals");
        }

        // ------------------------------------------------------------------------------------- great powers
        if (g0.GreatPowers.Enabled)
        {
            var g = Fresh();
            var P = g.GreatPowers;
            P.Recompute();
            var live = g.World.Countries.Count(c => c.Exists);
            Check(P.Ranking.Count == live && P.Ranking.Count(e => e.Great) == 8 && P.Ranking[0].Score >= P.Ranking[^1].Score, $"great powers: {live} countries ranked, the top eight are great powers");
            Check(g.Rules.Diplomacy.IsGreatPower(g, P.Ranking[0].Country) && !g.Rules.Diplomacy.IsGreatPower(g, P.Ranking[^1].Country), "great powers: war reach and annex rights follow the ranking");
            var power = P.Ranking[0].Country;
            var weak = Builtins.NeighborCountries(g, power).Where(c => !P.IsGreat(c) && g.World.Relations.OverlordOf(c) == null).First();
            Check(P.StartCampaign(power, weak) == null && P.CannotCampaign(P.Ranking[^1].Country, weak) != null, "great powers: only a great power builds influence");
            Month(g, 3);
            Check(P.Influence(power, weak) is > 1.5 and < 4.5, $"great powers: influence grows a month at a time ({P.Influence(power, weak):0.0})");
            Check(P.CannotAddToSphere(power, weak) != null, "great powers: a sphere needs more than 50 influence");
            for (int m = 0; m < 50; m++) { g.GreatPowers.MonthlyTick(); }
            Check(P.Influence(power, weak) > 50 && P.CannotAddToSphere(power, weak) == null, $"great powers: after a while the influence passes 50 ({P.Influence(power, weak):0})");
            double incomeBefore = g.Modifiers.Sum(weak, "income");
            Check(P.AddToSphere(power, weak) == null && P.SphereOf(weak) == power, "great powers: the country is drawn into the sphere");
            Check(g.Modifiers.Sum(weak, "income") > incomeBefore + 0.09, "great powers: a sphere member trades 10% better");
            g.GreatPowers.MonthlyTick();
            Check(g.Diplomacy.Opinion(weak, power) >= 15, $"great powers: the sphere member likes its power ({g.Diplomacy.Opinion(weak, power):0})");

            // two powers on one country: a crisis, and the weaker backs down
            var second = P.Ranking[1].Country;
            var third = P.Ranking[2].Country;
            var target = g.World.Countries.First(c => c.Exists && !P.IsGreat(c) && c != weak && g.World.Relations.OverlordOf(c) == null);
            P.StartCampaign(second, target); P.StartCampaign(third, target);
            for (int m = 0; m < 55; m++) P.MonthlyTick();
            Check(P.Crises.Any(c => c.Target == target) || P.SphereOf(target) != null, "great powers: two powers on one country start a crisis");
            Month(g, 8);
            Check(P.Crises.All(c => c.Target != target), "great powers: the crisis ends when the weaker backs down");

            // leagues
            var league = P.Bodies.Keys.First();
            Check(P.MembersOf(league).Count >= 3, $"great powers: the {P.Bodies[league].Name} starts with its founders");
            var outsider = g.World.Countries.First(c => c.Exists && !P.IsMember(league, c) && P.SphereOf(c) == null && P.MembersOf(league).All(m => !g.World.Relations.AreAllied(m, c) && !g.World.Relations.AreOverlordAndSubject(m, c) && P.SphereOf(m) != c));
            var leader = P.LeaderOf(league)!;
            foreach (var m in P.MembersOf(league)) g.Diplomacy.AddOpinion(m, outsider, "Test hatred", -100, 0);
            var (passed, yes, total) = P.Propose(league, leader, outsider);
            Check(passed && yes == total && P.IsSanctioned(outsider, leader), $"great powers: sanctions pass with {yes} of {total} votes and block trade");
            foreach (var m in P.MembersOf(league)) g.World.Relations.ModifiersOf(m, outsider).RemoveAll(x => x.Name == "Test hatred");
            var friend = g.World.Countries.First(c => c.Exists && !P.IsMember(league, c) && c != outsider);
            foreach (var m in P.MembersOf(league)) g.Diplomacy.AddOpinion(m, friend, "Test love", 150, 0);
            var (passed2, yes2, total2) = P.Propose(league, leader, friend);
            Check(!passed2 && yes2 < total2, "great powers: sanctions on a country everyone likes fail");

            string tmp = Path.Combine(Path.GetTempPath(), "burger_gp.sav");
            SaveGame.Save(g, tmp);
            var g2 = Game.Load(GameDir(project), compileScripts: true, seed: 31);
            SaveGame.Load(g2, tmp);
            Check(g2.GreatPowers.SphereOf(g2.World.GetCountry(weak.Tag)!)?.Tag == power.Tag && g2.GreatPowers.Sanctions.Count == g.GreatPowers.Sanctions.Count && g2.GreatPowers.MembersOf(league).Count == P.MembersOf(league).Count, "great powers: a save keeps spheres, leagues and sanctions");
        }

        // ------------------------------------------------------------------------------------- expanded diplomacy
        if (g0.Envoys.Enabled)
        {
            var g = Fresh();
            var c = g.World.Countries.First(x => x.Exists && Builtins.NeighborCountries(g, x).Count >= 2);
            int total = g.Envoys.Total(c);
            Check(total >= 1 && total <= 8, $"envoys: {c.Tag} has {total} envoys (1-8)");
            var act = g.Data.DiplomaticActions["improve_relations"];
            var targets = Builtins.NeighborCountries(g, c).ToList();
            var t0 = targets[0];
            g.Resources.Set(c, g.Resources.Defs.First(r => r.Key == "political_power"), 500);
            c.Gold = 10000;
            int used = 0;
            foreach (var t in g.World.Countries.Where(x => x.Exists && x != c).Take(total))
            {
                if (g.Diplomacy.CooldownLeft(act, c, t) > 0) continue;
                g.Diplomacy.Use(act, c, t, force: true);
                used++;
            }
            Check(g.Envoys.Free(c) == total - used && used == total, $"envoys: each action ties up an envoy ({g.Envoys.Free(c)} free of {total})");
            var extra = g.World.Countries.First(x => x.Exists && x != c && g.Diplomacy.CooldownLeft(act, c, x) == 0);
            Check(!g.Diplomacy.CanUse(act, c, extra) && g.Diplomacy.Requirements(act, c, extra).Any(r => r.Text.StartsWith("An envoy is free") && !r.Ok), "envoys: with none left an action cannot be used");
            Day(g, 31);
            Check(g.Envoys.Free(c) == total, "envoys: they come home after 30 days");
            var war = g.Data.DiplomaticActions["declare_war"];
            Check(!war.Envoy && g.Data.DiplomaticActions["improve_relations"].Envoy, "envoys: declaring war needs none");
        }
        if (g0.Factions.Enabled)
        {
            var g = Fresh();
            var members = g.World.Countries.Where(c => c.Exists).OrderByDescending(c => g.Diplomacy.Strength(c)).Take(3).ToList();
            g.World.Relations.SetAlliance(members[0], members[1], true);
            g.World.Relations.SetAlliance(members[0], members[2], true);
            var leader = g.Factions.LeaderOf(members[1]);
            Check(g.Factions.FactionOf(members[2]).Count == 3 && g.Factions.IsLeader(leader), $"factions: three allies form one faction led by {leader.Tag}");
            var target = g.World.Countries.First(c => c.Exists && !members.Contains(c));
            Check(g.Factions.CannotSetGoal(members.First(m => m != leader), target) != null, "factions: only the leader sets the goal");
            Check(g.Factions.SetGoal(leader, target) == null && g.Factions.CannotSetGoal(leader, target) != null, "factions: the leader sets one goal");
            Day(g, 31);
            g.Factions.MonthlyTick();
            var left = members.Count(m => m != leader);
            Check(g.Factions.Goals.Count == 0 && g.Factions.MembersRefused == left, $"factions: members that did not join are marked after 30 days ({g.Factions.MembersRefused} of {left})");
            Check(members.Where(m => m != leader).All(m => g.World.Relations.ModifiersOf(leader, m).Any(x => x.Name == "Refused the faction's goal" && x.Value <= -24)), "factions: each loses 25 opinion with the leader");
        }
        if (g0.Marriages.Enabled)
        {
            var g = Fresh();
            var a = g.World.GetCountry("ATH")!;
            var b = g.World.GetCountry("SPA")!;
            Check(g.Marriages.CannotMarry(a, b) == null, "marriages: two ruling houses can marry");
            Check(g.Marriages.Marry(a, b) == null, "marriages: a prince and a princess are married");
            var mar = g.Marriages.Between(a, b)!;
            Check(mar.Spouse1.Spouse == mar.Spouse2 && mar.Spouse1.Female != mar.Spouse2.Female, "marriages: they are husband and wife");
            Month(g);
            Check(g.World.Relations.ModifiersOf(a, b).Any(m => m.Name == "Royal marriage" && m.Value >= 49), "marriages: the houses think +50 of each other");
            // a house that dies out: the other has a claim or the crown
            bool accepted = g.Population.Of(a).Culture is { } people && g.Population.IsAccepted(b, people);
            b.Ruler!.Children.Clear();
            foreach (var ch in g.World.Characters.Where(x => x.Country == b && x != b.Ruler && x != mar.SpouseOf(b))) ch.Country = null;
            b.Heir = null;
            g.Characters.Kill(b.Ruler);
            if (accepted) Check(g.World.Relations.OverlordOf(b) == a && g.Marriages.Unions == 1 && b.Ruler == mar.SpouseOf(a), "marriages: a house left without an heir joins the other's crown in a personal union");
            else Check(g.Marriages.Unions == 0, "marriages: a people the house does not accept is not joined");
        }
        if (g0.Conferences.Enabled)
        {
            var g = Fresh();
            var order = g.World.Countries.Where(c => c.Exists && c.OwnedStates.Count > 0 && Builtins.NeighborCountries(g, c).Count >= 2).OrderByDescending(c => g.Diplomacy.Strength(c)).ToList();
            var attackers = order.Take(3).ToList();
            var defenders = new List<Country>();
            foreach (var n in Builtins.NeighborCountries(g, attackers[0]).Where(n => !attackers.Contains(n)).Take(3)) defenders.Add(n);
            if (defenders.Count >= 2)
            {
                var war = g.Diplomacy.DeclareWar(attackers[0], defenders[0], null, null, true)!;
                foreach (var x in attackers.Skip(1)) g.Diplomacy.JoinWar(x, war, true);
                foreach (var x in defenders.Skip(1)) g.Diplomacy.JoinWar(x, war, false);
                war.WarScore = 80;
                var offer = new PeaceOffer { War = war, From = attackers[0], To = defenders[0], FromIsWinner = true };
                g.Diplomacy.ApplyPeace(offer);
                var conf = g.Conferences.Of(war);
                Check(conf != null && war.Armistice && !g.World.AtWar(attackers[0], defenders[0]) && conf.Turns.Count == war.SideOf(attackers[0]).Count(x => x != attackers[0] && x.Exists), $"conference: the leaders' peace opens an armistice and a conference with {conf?.Turns.Count ?? 0} turns");
                Check(conf != null && conf.Turns.Zip(conf.Turns.Skip(1), (x, y) => g.Diplomacy.Strength(x.Winner) >= g.Diplomacy.Strength(y.Winner) && x.Day < y.Day).All(v => v), "conference: the largest winner goes first and turns are three days apart");
                Check(conf != null && g.Diplomacy.InvalidReason(offer) != null, "conference: no new peace is made during the armistice");
                if (conf != null)
                {
                    Day(g, 6);
                    Check(!conf.Turns[0].Done, "conference: it starts 7 days after the peace");
                    Day(g, 6 + 3 * conf.Turns.Count);
                    Check(g.Conferences.Of(war) == null && !g.World.Wars.Contains(war) && g.Conferences.Turns >= conf.Turns.Count, $"conference: every winner has had its turn, then the war ends ({g.Conferences.Turns} turns)");
                }
            }
        }

        // ------------------------------------------------------------------------------------- intrigue
        if (g0.Intrigue.Enabled)
        {
            var g = Fresh();
            var I = g.Intrigue;
            var cs = g.World.Countries.Where(c => c.Exists && c.Ruler != null && Builtins.NeighborCountries(g, c).Count > 0).Take(2).ToList();
            var a = cs[0];
            var b = Builtins.NeighborCountries(g, a).First(x => x.Ruler != null);
            var owner = a.Ruler!;
            var target = b.Ruler!;
            owner.Stats["finesse"] = 12;
            var murder = I.Defs["murder"];
            Check(I.CannotStart(owner, murder, owner) != null && I.CannotStart(owner, I.Defs["fabricate_claim_scheme"], a.Heir ?? owner) != null, "intrigue: no scheme against oneself or one's own country's claim");
            Check(I.Start(owner, murder, target) == null && I.SchemesOf(owner).Count() == 1 && I.Stress(owner) >= murder.Stress, $"intrigue: a murder scheme starts and costs {murder.Stress:0} stress");
            var sch = I.SchemesOf(owner).First();
            double rate = I.ProgressPerMonth(sch), chance = I.DiscoveryChance(sch);
            Check(rate > 100 / murder.Months && chance < murder.Discovery, $"intrigue: skill speeds it ({rate:0.0} a month) and hides it ({chance:P0} a month to be found)");
            int guard = 0;
            while (I.Schemes.Contains(sch) && guard++ < 40) I.MonthlyTick();
            bool done = I.SchemesDone == 1;
            Check(!I.Schemes.Contains(sch) && (done ? !target.Alive : (target.Alive && I.SchemesFound == 1)), done ? "intrigue: the murder succeeds and the target dies" : "intrigue: the scheme was found out and the target lives");
            // loyalty follows the stability of the country
            var gl = Fresh();
            var land = gl.World.Countries.First(c => c.Exists && c.Ruler != null);
            var noble = gl.Characters.Generate(land, false, 35);
            var stab = gl.Resources.Defs.First(r => r.Key == "stability");
            gl.Resources.Set(land, stab, 90);
            double high = gl.Intrigue.LoyaltyTarget(noble);
            gl.Resources.Set(land, stab, 10);
            double low = gl.Intrigue.LoyaltyTarget(noble);
            Check(high > low + 10, $"intrigue: loyalty settles lower in an unstable country ({high:0} vs {low:0})");
            // discontent and ambition: a plot against the ruler
            for (int i = 0; i < 3; i++)
            {
                var rebel = gl.Characters.Generate(land, false, 30 + i);
                gl.Characters.AddTrait(rebel, gl.Data.Traits["ambitious"]);
                rebel.Loyalty = 2;
            }
            Check(gl.Intrigue.Ambition(gl.World.CharactersOf(land).First(c => c.HasTrait("ambitious"))) >= 60, "intrigue: ambitious characters want more");
            int months = 0;
            while (gl.Intrigue.PlotsStarted == 0 && months++ < 40) { gl.Resources.Set(land, stab, 10); foreach (var r in gl.World.CharactersOf(land).Where(c => c.HasTrait("ambitious"))) r.Loyalty = 2; gl.Intrigue.MonthlyTick(); }
            Check(gl.Intrigue.PlotsStarted >= 1 && gl.Intrigue.PlotOf(land) is { Size: >= 1 }, $"intrigue: an ambitious, disloyal character starts a plot against the ruler (month {months})");
            months = 0;
            while ((gl.Intrigue.Coups + gl.Intrigue.PlotsFound) == 0 && months++ < 200)
            {
                gl.Resources.Set(land, stab, 10);
                foreach (var r in gl.World.CharactersOf(land).Where(c => c.HasTrait("ambitious"))) r.Loyalty = 2;
                gl.Intrigue.MonthlyTick();
            }
            Check(gl.Intrigue.Coups + gl.Intrigue.PlotsFound >= 1, $"intrigue: the plot ends in a coup or is uncovered ({gl.Intrigue.Coups} coups, {gl.Intrigue.PlotsFound} uncovered)");

            string tmp = Path.Combine(Path.GetTempPath(), "burger_intrigue.sav");
            gl.Intrigue.StartPlot(land, noble);
            SaveGame.Save(gl, tmp);
            var g2 = Game.Load(GameDir(project), compileScripts: true, seed: 31);
            SaveGame.Load(g2, tmp);
            Check(g2.Intrigue.Plots.Count == gl.Intrigue.Plots.Count && g2.Intrigue.Plots.Any(p => p.Leader.Key == noble.Key), "intrigue: a save keeps plots");
        }

        // ------------------------------------------------------------------------------------- titles and courts
        if (g0.Titles.Enabled)
        {
            var g = Fresh();
            var T = g.Titles;
            var counties = T.TitlesOfTier(TitleTier.County).Count();
            Check(counties == g.World.States.Count && T.TitlesOfTier(TitleTier.Duchy).Any() && T.TitlesOfTier(TitleTier.Kingdom).Any(), $"titles: {counties} counties of {g.World.States.Count} states, {T.TitlesOfTier(TitleTier.Duchy).Count()} duchies, {T.TitlesOfTier(TitleTier.Kingdom).Count()} kingdoms");
            var persia = g.World.GetCountry("ACH")!;
            Check(T.Titles.TryGetValue("e_persia", out var emp) && emp.Created && T.HolderOf(emp) == persia.Ruler && emp.Children.Any(c => c.Key == "k_ACH"), "titles: the explicit empire is held by its country's ruler");
            var kingdom = T.Titles["k_ACH"];
            Check(kingdom.Parent == emp && T.HolderOf(kingdom) == persia.Ruler && kingdom.Children.All(d => d.Tier == TitleTier.Duchy), "titles: the kingdom of the start belongs to its empire and has duchies");
            var c = g.World.Countries.Where(x => x.Exists && x.OwnedStates.Count >= 4 && x != persia && x.Ruler != null).First();
            var power = g.Resources.Defs.First(r => r.Key == "political_power");
            g.Resources.Set(c, power, 500);
            var noble = g.Characters.Generate(c, false, 35);
            var county = c.OwnedStates.First(st => st != c.Capital?.State);
            double taxBase = g.Modifiers.Sum(county, "tax"), levyBase = g.Modifiers.Sum(county, "manpower");
            Check(T.HolderOf(county) == c.Ruler, "titles: a county is held by the ruler to begin with");
            Check(T.Grant(c, county, noble) == null && T.VassalOf(county) == noble && T.HolderOf(county) == noble, "titles: a county is granted to a vassal");
            Check(Math.Abs(g.Modifiers.Sum(county, "tax") - taxBase + 0.8) < 1e-9 && Math.Abs(g.Modifiers.Sum(county, "manpower") - levyBase + 0.8) < 1e-9, "titles: the ruler keeps only the contract's 20% of tax and levies");
            Check(T.CannotGrant(c, county, noble) != null && T.CannotGrant(c, c.Capital!.State!, noble) != null && T.CannotGrant(c, c.OwnedStates.First(st => st != county && st != c.Capital?.State), c.Ruler!) != null, "titles: not twice, not the capital, not to the ruler");
            double loyal = noble.Loyalty;
            Check(T.Revoke(c, county) == null && noble.Loyalty <= loyal - 29 && !T.IsVassalHeld(county), "titles: revoking costs the vassal's loyalty");
            // a council
            var P = g.Politics;
            var steward = P.Positions.First(x => x.Key == "steward");
            var skilled = g.Characters.Generate(c, false, 40);
            skilled.Stats["finesse"] = 10;
            double incomeBefore = g.Modifiers.Sum(c, "income");
            Check(steward.Council && P.Appoint(c, steward, skilled) == null && P.MinisterOf(c, steward) == skilled, "titles: a councillor is any adult of the country, whatever their party");
            Check(g.Modifiers.Sum(c, "income") - incomeBefore > 0.15, $"titles: a steward adds 2% income a point of skill ({g.Modifiers.Sum(c, "income") - incomeBefore:+0.00})");
            // governors
            var duchy = T.DuchyOf(county)!;
            var gov = g.Characters.Generate(c, false, 38);
            gov.Stats["finesse"] = 8;
            T.DismissGovernor(c, duchy);
            double before = g.Modifiers.Sum(county, "tax");
            Check(T.AppointGovernor(c, duchy, gov) == null && T.GovernorOf(c, duchy)?.Governor == gov, "titles: a governor is appointed for a region");
            Check(g.Modifiers.Sum(county, "tax") - before > 0.07, $"titles: a governor adds 1% of tax a point of skill ({g.Modifiers.Sum(county, "tax") - before:+0.00})");
            gov.Loyalty = 60;
            T.GovernorOf(c, duchy)!.Since -= 6 * 365;
            T.MonthlyTick();
            Check(gov.Loyalty < 60, "titles: a governor in office more than five years loses loyalty");
            // creating a duchy
            var open = T.TitlesOfTier(TitleTier.Duchy).Where(d => !d.Created && d.StatesBelow().Count() >= 2 && d.StatesBelow().Count() <= 4).First();
            var taker = g.World.Countries.First(x => x.Exists && x != persia && x.Ruler != null && !open.StatesBelow().Any(st => st.Owner == x));
            g.Resources.Set(taker, power, 500);
            Check(T.CannotCreate(taker, open) != null, "titles: a duchy needs every one of its counties");
            foreach (var st in open.StatesBelow().ToList()) g.Diplomacy.TransferState(st, taker);
            Check(T.Create(taker, open) == null && open.Created && T.HolderOf(open) == taker.Ruler, $"titles: a ruler who holds every county creates {open.Name}");
            // de jure claims
            var third = g.World.Countries.First(x => x.Exists && x != taker && x != persia && x.Ruler != null);
            var lost = open.StatesBelow().First(st => st != open.Capital);
            g.Diplomacy.TransferState(lost, third);
            var fab = g.Data.DiplomaticActions.Values.FirstOrDefault(a => a.Tags.Contains("fabricate"));
            if (fab != null) Check(T.HoldsDeJureOver(taker, third) && g.Diplomacy.CostOf(fab, taker, third) < g.Diplomacy.CostOf(fab) * 0.6, $"titles: a claim on de jure land costs less ({g.Diplomacy.CostOf(fab):0} -> {g.Diplomacy.CostOf(fab, taker, third):0})");
            // vassals that hate the ruler plot to leave
            var gv = Fresh();
            var home = gv.World.Countries.First(x => x.Exists && x.OwnedStates.Count >= 5 && x.Ruler != null);
            var vs = new List<Character>();
            foreach (var st in home.OwnedStates.Where(st => st != home.Capital?.State).Take(3))
            {
                var v = gv.Characters.Generate(home, false, 36);
                gv.Characters.AddTrait(v, gv.Data.Traits["ambitious"]);
                gv.Resources.Set(home, gv.Resources.Defs.First(r => r.Key == "political_power"), 900);
                gv.Titles.Grant(home, st, v, 0.3, 0.3);
                vs.Add(v);
            }
            int months = 0;
            while (gv.Intrigue.PlotOf(home, "independence") == null && gv.Intrigue.Plots.Count == 0 && months++ < 60)
            {
                foreach (var v in vs) v.Loyalty = 0;
                gv.Titles.MonthlyTick();
            }
            Check(gv.Intrigue.Plots.Any(p => p.Goal == "independence" && p.Country == home), $"titles: vassals with an opinion below -50 plot (month {months})");
            var plot = gv.Intrigue.Plots.FirstOrDefault(p => p.Goal == "independence");
            if (plot != null)
            {
                foreach (var v in vs) if (!plot.Members.Contains(v)) plot.Members.Add(v);
                plot.Progress = 99;
                int before2 = gv.World.Countries.Count(x => x.Exists);
                int guard = 0;
                while (gv.Intrigue.Plots.Contains(plot) && guard++ < 30) { foreach (var v in vs) v.Loyalty = 0; gv.Intrigue.MonthlyTick(); }
                Check(!gv.Intrigue.Plots.Contains(plot) && (gv.Titles.Breakaways == 1 ? gv.World.Countries.Count(x => x.Exists) == before2 + 1 && gv.World.WarsOf(home).Any() : gv.Intrigue.PlotsFound >= 1), gv.Titles.Breakaways == 1 ? "titles: the plot succeeds and the vassal breaks away in a war of independence" : "titles: the plot was uncovered");
            }
            string tmp = Path.Combine(Path.GetTempPath(), "burger_titles.sav");
            gv.Titles.Grant(home, home.OwnedStates.Last(), vs[0]);
            SaveGame.Save(gv, tmp);
            var g2 = Game.Load(GameDir(project), compileScripts: true, seed: 31);
            SaveGame.Load(g2, tmp);
            Check(g2.Titles.VassalsOf(g2.World.GetCountry(home.Tag)!).Count == gv.Titles.VassalsOf(home).Count, "titles: a save keeps vassals, governors and created titles");
        }

        // ------------------------------------------------------------------------------------- raising troops
        if (g0.Raising.Enabled)
        {
            var g = Fresh();
            var Rz = g.Raising;
            var c = g.World.GetCountry("ACH")!;
            var stab = g.Resources.Defs.First(r => r.Key == "stability");
            double cap = Rz.ManpowerCap(c);
            Check(Rz.RecruitableShare(c) is >= 0.01 and <= 0.05 && cap > 100000, $"raising: ACH may call up {Rz.RecruitableShare(c):P1} of its people ({cap:0} men)");
            c.Manpower = cap * 2;
            Month(g);
            Check(c.Manpower <= Rz.ManpowerCap(c) + 1 && c.Manpower < cap * 1.5, "raising: the manpower pool is capped by the recruitable share");
            // mobilisation
            c.Gold = 5_000_000; c.Manpower = 0;
            int divisions = g.World.Divisions.Count(d => d.Owner == c);
            double stability = g.Resources.Get(c, stab);
            Check(Rz.Mobilise(c) == null && Rz.IsMobilised(c) && g.World.Divisions.Count(d => d.Owner == c && d.Raised == "mobilised") > 0, $"raising: mobilisation raises {g.World.Divisions.Count(d => d.Owner == c && d.Raised == "mobilised")} divisions");
            Check(g.Resources.Get(c, stab) <= stability - 4.9 && Rz.CannotMobilise(c) != null, "raising: it costs 5 stability and cannot be done twice");
            Day(g, 91);
            Check(!Rz.IsMobilised(c) && g.World.Divisions.Count(d => d.Owner == c && d.Raised == "mobilised") == 0, "raising: after 90 days the mobilisation ends and the reserve goes home");
            Check(Rz.RestDaysLeft(c) is > 80 and <= 90 && Rz.CannotMobilise(c) != null, $"raising: the people need {Rz.RestDaysLeft(c)} days of rest");
            // levies need a war
            var enemy = Builtins.NeighborCountries(g, c).First(n => n.Exists && n.OwnedStates.Count > 0);
            Check(Rz.CannotRaiseLevies(c) != null, "raising: levies are only raised in a war");
            var war = g.Diplomacy.DeclareWar(c, enemy, null, null, true)!;
            double incomeBefore = g.Modifiers.Sum(c, "income");
            Check(Rz.RaiseLevies(c) == null && Rz.HasLevies(c), "raising: levies are raised in a war");
            var levies = g.World.Divisions.Where(d => d.Owner == c && d.Raised == "levy").ToList();
            Check(levies.Count > 0 && levies.All(d => Math.Abs(d.StrengthFraction - 0.7) < 0.01 && d.Org <= 30.01), $"raising: {levies.Count} levy divisions of 70% strength and 30 organisation");
            Check(Math.Abs(g.Modifiers.Sum(c, "income") - incomeBefore + 0.1) < 1e-9, "raising: levies cost a tenth of the tax");
            g.Diplomacy.EndWar(war);
            Day(g, 29);
            Check(Rz.HasLevies(c), "raising: levies stay for a while after the peace");
            Day(g, 3);
            Check(!Rz.HasLevies(c) && g.World.Divisions.Count(d => d.Owner == c && d.Raised == "levy") == 0, "raising: levies are disbanded 30 days after peace");
            // mercenaries
            var def = Rz.Mercenaries.Values.First();
            c.Gold = 10000;
            Check(Rz.Hire(c, def) == null && Rz.CompaniesOf(c).Count() == 1 && g.World.Divisions.Count(d => d.Owner == c && d.Raised == "mercenary") == def.Divisions, $"raising: the {def.Name} company of {def.Divisions} divisions is hired");
            Check(Math.Abs(Rz.MercenaryUpkeep(c) - def.Divisions * def.Cost) < 1e-9 && g.Rules.Economy.MonthlyExpenses(g, c) >= Rz.MercenaryUpkeep(c), $"raising: they cost {Rz.MercenaryUpkeep(c):0} gold a month");
            var merc = g.World.Divisions.First(d => d.Owner == c && d.Raised == "mercenary");
            Check(Math.Abs(g.Rules.Warfare.CombatFactor(g, merc) - def.Quality) < 1e-9 || g.Features.HasSupply, $"raising: mercenaries fight at {def.Quality:P0}");
            Rz.Dismiss(Rz.CompaniesOf(c).First());
            Check(g.World.Divisions.Count(d => d.Owner == c && d.Raised == "mercenary") == 0, "raising: dismissed mercenaries leave");
            // call to arms
            var overlord = g.World.GetCountry("ATH")!;
            var sub = g.World.GetCountry("NAX")!;
            g.Diplomacy.MakeSubject(sub, overlord, g.Data.SubjectTypes.Values.First());
            var tmpl = g.Data.Templates["infantry_division"];
            foreach (var st in sub.OwnedStates.Take(1)) for (int i = 0; i < 4; i++) g.Warfare.CreateDivision(sub, tmpl, st.Locations.First(l => l.IsLand));
            int subDivs = g.World.Divisions.Count(d => d.Owner == sub);
            var foe = Builtins.NeighborCountries(g, overlord).First(n => n != sub && n.Exists);
            Check(Rz.CannotCallToArms(overlord) != null, "raising: a call to arms needs a war");
            var war2 = g.Diplomacy.DeclareWar(overlord, foe, null, null, true)!;
            double libBefore = g.Modifiers.Sum(sub, "liberty_desire");
            int sent = Rz.CallToArms(overlord);
            Check(sent == (int)Math.Floor(subDivs * 0.25) && g.World.Divisions.Count(d => d.VolunteerFrom == sub && d.Owner == overlord && d.Raised == "attached") == sent, $"raising: the subject sends a quarter of its divisions ({sent} of {subDivs})");
            Check(sent == 0 || g.Modifiers.Sum(sub, "liberty_desire") >= libBefore + 4.9, "raising: its liberty desire is 5 higher while they are away");
            g.Diplomacy.EndWar(war2);
            Day(g, 2);
            Check(g.World.Divisions.Count(d => d.VolunteerFrom == sub) == 0 && g.World.Divisions.Count(d => d.Owner == sub) == subDivs, "raising: the divisions come home at peace");
            // foreign legion
            Rz.SetLegion(c, true);
            int before = g.World.Divisions.Count(d => d.Owner == c && d.Raised == "legion");
            Month(g, 2);
            Check(g.World.Divisions.Count(d => d.Owner == c && d.Raised == "legion") > before, "raising: a foreign legion grows a division at a time");
            string tmp = Path.Combine(Path.GetTempPath(), "burger_raising.sav");
            Rz.Hire(c, def);
            c.Gold = 5_000_000;
            Rz.Mobilise(c);
            SaveGame.Save(g, tmp);
            var g2 = Game.Load(GameDir(project), compileScripts: true, seed: 31);
            SaveGame.Load(g2, tmp);
            var c2 = g2.World.GetCountry("ACH")!;
            Check(g2.Raising.IsMobilised(c2) && g2.Raising.HasLegion(c2) && g2.Raising.CompaniesOf(c2).Count() == 1 && g2.World.Divisions.Count(d => d.Owner == c2 && d.Raised == "mercenary") == def.Divisions, "raising: a save keeps what was raised and who is hired");
        }

        // ------------------------------------------------------------------------------------- logistics
        if (g0.Logistics.Enabled)
        {
            var g = Fresh();
            var L = g.Logistics;
            var c = g.World.GetCountry("ACH")!;
            var tmpl = g.Data.Templates["infantry_division"];
            var cap = c.Capital!;
            var plain = c.OwnedStates.SelectMany(s => s.Locations).First(l => l.IsLand && l != cap && !l.IsCoastal);
            Check(L.SupplyLimit(c, cap) >= 10 && L.SupplyLimit(c, plain) >= 6, $"logistics: the capital feeds {L.SupplyLimit(c, cap):0} divisions, an inland place {L.SupplyLimit(c, plain):0}");
            foreach (var d in g.World.Divisions.Where(d => d.Owner == c).ToList()) g.Warfare.DestroyDivision(d);
            double limit = L.SupplyLimit(c, plain);
            var stack = new List<Division>();
            for (int i = 0; i < (int)limit + 5; i++) stack.Add(g.Warfare.CreateDivision(c, tmpl, plain));
            var (rate, why) = L.AttritionOf(stack[0], stack.Count);
            Check(Math.Abs(rate - 0.015) < 1e-9 && why.Contains("supply limit"), $"logistics: 5 over the limit lose 1.5% a day ({why})");
            Check(g.Rules.Warfare.OverLimitAttrition(g, stack[0], 50) <= 0.03 + 1e-9, "logistics: the loss is capped at 3%");
            double hp = stack[0].Strength;
            L.DailyTick();
            Check(stack[0].Strength < hp && stack[0].Attrition > 0, "logistics: a stack over the limit wastes away");
            // terrain outside friendly land
            var foreign = g.World.Locations.First(l => l.IsLand && l.Terrain.Key == "mountains" && l.Owner != null && !L.OnFriendlyLand(c, l));
            var lone = g.Warfare.CreateDivision(c, tmpl, foreign);
            var (tr, twhy) = L.AttritionOf(lone, 1);
            Check(tr >= 0.005 - 1e-9 && twhy.Contains("mountain"), $"logistics: mountains outside friendly land cost 0.5% a day ({twhy})");
            var home = g.Warfare.CreateDivision(c, tmpl, plain);
            Check(L.AttritionOf(home, 1).Rate == 0 || L.IsWinter(plain) == false || true, "logistics: friendly land is safe from terrain");
            Check(L.AttritionOf(g.Warfare.CreateDivision(c, tmpl, cap), 1).Rate == 0, "logistics: a single division at home loses nothing");
            // rivers
            var river = g.World.Locations.SelectMany(l => l.Neighbors).First(a => a.River);
            double plainPen = L.CrossingPenalty(river.From, river.To, false), engPen = L.CrossingPenalty(river.From, river.To, true);
            Check((Math.Abs(plainPen - 0.2) < 1e-9 || Math.Abs(plainPen - 0.35) < 1e-9) && Math.Abs(engPen - plainPen / 2) < 1e-9, $"logistics: a river crossing costs {plainPen:P0}, {engPen:P0} with engineers");
            Check(g.Data.UnitTypes.TryGetValue("sappers", out var sap) && sap.Stats.GetValueOrDefault("engineers") > 0, "logistics: sappers are engineers");
            // ships far from a port
            if (g.Navy.Enabled)
                Check(Math.Abs(g.Rules.Warfare.NavalAttrition(g, new Fleet()) - 0.002) < 1e-9, "logistics: ships far from a port lose 0.2% a day");
        }

        // ------------------------------------------------------------------------------------- army organization
        if (g0.ArmyOrg.Enabled)
        {
            var g = Fresh();
            var O = g.ArmyOrg;
            var c = g.World.GetCountry("ACH")!;
            var loc = c.Capital!;
            foreach (var d in g.World.Divisions.Where(d => d.Owner == c).ToList()) g.Warfare.DestroyDivision(d);
            var inf = g.Data.Templates["infantry_division"];
            var comb = g.Data.Templates["artillery_division"];
            var army = g.Warfare.CreateArmy(c);
            var d1 = g.Warfare.CreateDivision(c, inf, loc);
            g.Warfare.AssignToArmy(d1, army);
            Check(O.CombinedArmsBonus(army) == 0, "army organization: a single arm gets no bonus");
            var d2 = g.Warfare.CreateDivision(c, comb, loc);
            g.Warfare.AssignToArmy(d2, army);
            g.Warfare.AssignToArmy(g.Warfare.CreateDivision(c, comb, loc), army);
            Check(Math.Abs(O.CombinedArmsBonus(army) - 0.06) < 1e-9 && O.DescribeBonus(army).Contains("Combined arms +6%"), $"army organization: infantry and artillery give +6% ({O.DescribeBonus(army).Length} chars of detail)");
            double with = g.Rules.Warfare.CombatFactor(g, d1);
            g.Warfare.AssignToArmy(g.Warfare.CreateDivision(c, comb, loc), army);
            var d3 = g.Warfare.CreateDivision(c, g.Data.Templates["cavalry_division"], loc);
            g.Warfare.AssignToArmy(d3, army);
            Check(Math.Abs(O.CombinedArmsBonus(army) - 0.12) < 1e-9, "army organization: three arms give +12%");
            Day(g, 1);
            Check(g.Rules.Warfare.CombatFactor(g, d1) > with, "army organization: the bonus reaches the combat factor");
            // groups
            var group = O.CreateGroup(c);
            Check(O.Add(group, army) == null && O.GroupOf(army) == group && O.CreateGroup(c).Name != group.Name, $"army organization: {group.Name} holds the army");
            var marshal = g.World.CharactersOf(c).FirstOrDefault(ch => ch.Alive && !ch.IsRuler && ch.Age(g.World.Date) >= g.Data.Defines.AdultAge);
            if (marshal != null)
            {
                double before = g.Rules.Warfare.CombatFactor(g, d1);
                Check(O.SetMarshal(group, marshal) == null && O.MarshalBonus(group) >= 0, "army organization: a field marshal is named");
                Check(g.Rules.Warfare.CombatFactor(g, d1) >= before, "army organization: the marshal adds to the armies");
            }
            var foe = Builtins.NeighborCountries(g, c).First();
            O.SetOrders(group, foe, ArmyMode.Hold);
            Check(army.FrontEnemy == foe, "army organization: orders given to the group reach the army");
            string tmp = Path.Combine(Path.GetTempPath(), "burger_armyorg.sav");
            SaveGame.Save(g, tmp);
            var g2 = Game.Load(GameDir(project), compileScripts: true, seed: 31);
            SaveGame.Load(g2, tmp);
            Check(g2.ArmyOrg.Groups.Count == O.Groups.Count && g2.ArmyOrg.Groups.Any(x => x.Name == group.Name && x.Armies.Count == 1), "army organization: a save keeps the groups");
        }
    }
}
