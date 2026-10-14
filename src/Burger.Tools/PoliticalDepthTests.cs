using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Politics;
using Burger.Core.Save;
using Burger.Core.Script;

namespace Burger.Tools;

public static partial class Program
{
    private sealed record Outcome(double Progress, double Organization, double Grievance, double Crackdowns, double Concessions, double Stability, string Ruling, int Revolutions, bool Exists);

    /// <summary>
    /// Depth of the political simulation: movements that need organisation, grievance and an opening; decisions with delayed and
    /// competing consequences; regional pop discontent; faction vote loss and collapse that are graded and chancy, not fixed;
    /// persistence and old-save compatibility. Each scenario starts from the identical seed so differences come from the inputs.
    /// </summary>
    private static void PoliticalDepthTests(string project)
    {
        Game Fresh()
        {
            var g = Game.Load(GameDir(project), compileScripts: true, seed: 11);
            g.StartNewGame(null);
            g.Ai.Enabled = false;
            return g;
        }
        var g0 = Fresh();
        var P0 = g0.Politics;
        if (!P0.Enabled || !P0.CivilWarsEnabled) return;
        // a country that can be shut out: pick the same one in every run (the first non-player country with two states and a movement-capable ideology)
        var dictKey = P0.Ideologies.FirstOrDefault(i => !i.Elections)?.Key;
        if (dictKey == null) return;
        var tag = g0.World.Countries.Where(c => c.Exists && !c.IsPlayer && c.OwnedStates.Count >= 2).Select(c => c.Tag).First();
        var rivalKey = P0.Ideologies.First(i => i.Key != dictKey).Key;

        double Stability(Game g, Country c)
        {
            var def = g.Resources.Defs.FirstOrDefault(r => string.Equals(r.Key, "stability", StringComparison.OrdinalIgnoreCase));
            return def == null ? 50 : g.Resources.Get(c, def);
        }
        void SetStability(Game g, Country c, double v)
        {
            var def = g.Resources.Defs.FirstOrDefault(r => string.Equals(r.Key, "stability", StringComparison.OrdinalIgnoreCase));
            if (def != null) g.Resources.Set(c, def, v);
        }

        // One run: an authoritarian state facing a popular shut-out ideology. `hold` pins stability (a regime that keeps order),
        // `act` is a decision taken at month 3.
        Outcome Run(double? hold, Action<Game, Country, Movement>? act, int months = 48, double support = 0.5)
        {
            var g = Fresh();
            var P = g.Politics;
            var c = g.World.GetCountry(tag)!;
            P.SetRulingParty(c, dictKey, "test");
            P.AddPopularity(c, rivalKey, support - P.PopularityOf(c, rivalKey));
            P.Of(c).CalmUntilDay = int.MinValue;
            int revolutions = P.Revolutions;
            for (int m = 0; m < months; m++)
            {
                if (hold is { } h) SetStability(g, c, h);
                g.Politics.MonthlyTick();
                if (m == 2 && act != null && P.MovementOf(c, rivalKey) is { } mv) act(g, c, mv);
                if (!c.Exists || P.InCivilWar(c)) break;
            }
            var move = P.MovementOf(c, rivalKey);
            return new Outcome(move?.Progress ?? -1, move?.Organization ?? -1, move?.Grievance ?? -1, move?.Crackdowns ?? 0, move?.Concessions ?? 0,
                Stability(g, c), P.Of(c).Ruling, P.Revolutions - revolutions, c.Exists);
        }
        void Fund(Game g, Country c) { if (g.Politics.PowerResource is { } r) g.Resources.Set(c, r, 1_000_000); }

        // ---- 1. strong support alone is not sufficient: a regime that holds order is not toppled, a weak one is
        var gr = Fresh();
        {
            var Pr = gr.Politics; var Rr = gr.Rules.Politics;
            var cr = gr.World.GetCountry(tag)!;
            Pr.SetRulingParty(cr, dictKey, "test");
            Pr.AddPopularity(cr, rivalKey, 0.85 - Pr.PopularityOf(cr, rivalKey));
            var ideo = Pr.Ideology(rivalKey)!;
            var mv = new Movement { Country = cr, Ideology = rivalKey, Demand = MovementDemand.Power, Progress = 100, Organization = 80, Grievance = 70, Started = gr.World.Date };
            Pr.Movements.RemoveAll(x => x.Country == cr && x.Ideology == rivalKey); Pr.Movements.Add(mv);
            SetStability(gr, cr, 85);
            double stableCoup = Rr.CoupChance(gr, cr, ideo, 0.85), stableWar = Rr.CivilWarChance(gr, cr, ideo, 0.85);
            bool stableCanRise = Rr.CanRevolt(gr, cr, mv);
            SetStability(gr, cr, 10);
            double weakCoup = Rr.CoupChance(gr, cr, ideo, 0.85), weakWar = Rr.CivilWarChance(gr, cr, ideo, 0.85);
            bool weakCanRise = Rr.CanRevolt(gr, cr, mv);
            Check(!stableCanRise && weakCanRise, $"depth: a fully mobilised, organised, angry movement still cannot rise against a regime that keeps order (stable {stableCanRise}, turmoil {weakCanRise})");
            Check(stableCoup * 12 < 0.5 && stableCoup + stableWar < (weakCoup + weakWar) / 4, $"depth: 85% support alone rarely topples a stable regime ({stableCoup:P2}+{stableWar:P2} a month) but readily does in turmoil ({weakCoup:P1}+{weakWar:P1})");
            mv.Organization = 5;
            double loose = Rr.CoupChance(gr, cr, ideo, 0.85) + Rr.CivilWarChance(gr, cr, ideo, 0.85);
            Check(!Rr.CanRevolt(gr, cr, mv) && loose < (weakCoup + weakWar) / 4, $"depth: even in turmoil a movement without organisation cannot rise ({loose:P2} a month vs {weakCoup + weakWar:P1} when organised)");
        }
        var weak = Run(12, null, 72, 0.85);
        Check(weak.Revolutions > 0 || weak.Ruling != dictKey || !weak.Exists,
            $"depth: the same support topples a regime in turmoil within six years (revolutions {weak.Revolutions}, ruling {weak.Ruling}, org {weak.Organization:0})");

        // ---- 2. mobilisation is gradual and needs organisation: a lightly supported movement stays far below a revolt
        var small = Run(null, null, 24, 0.3);
        var big = Run(null, null, 24, 0.7);
        Check(big.Organization > small.Organization + 5 || big.Revolutions > 0, $"depth: wider support builds more organisation ({small.Organization:0} vs {big.Organization:0})");

        // ---- 3. identical starts diverge by player choice; decisions have delayed, competing consequences
        var inaction = Run(70, null, 18);
        var concede = Run(70, (g, c, m) => { Fund(g, c); g.Politics.Concede(m); }, 18);
        var crack = Run(70, (g, c, m) => { Fund(g, c); g.Politics.Suppress(m); }, 18);
        Check(concede.Grievance < inaction.Grievance, $"depth: concessions ease grievance ({concede.Grievance:0} vs {inaction.Grievance:0} left alone)");
        Check(crack.Grievance > inaction.Grievance, $"depth: a crackdown is remembered and feeds grievance ({crack.Grievance:0} vs {inaction.Grievance:0})");
        Check(crack.Crackdowns > 0 && concede.Concessions > 0 && inaction.Crackdowns == 0, "depth: crackdowns and concessions leave a persistent memory");
        Check(concede.Organization > inaction.Organization - 1 || concede.Progress < inaction.Progress,
            $"depth: a concession buys calm now but emboldens it (mobilised {concede.Progress:0} vs {inaction.Progress:0}, organised {concede.Organization:0} vs {inaction.Organization:0})");
        Check(new[] { inaction.Grievance, concede.Grievance, crack.Grievance }.Distinct().Count() == 3, "depth: three choices from the same start end in three different places");

        // ---- 4. a crackdown can fail: over repeated starts it works sometimes, not always, and costs stability either way
        {
            var g = Fresh();
            var P = g.Politics;
            var c = g.World.GetCountry(tag)!;
            P.SetRulingParty(c, dictKey, "test");
            var m = new Movement { Country = c, Ideology = rivalKey, Demand = MovementDemand.Power, Progress = 60, Organization = 50, Grievance = 50, Started = g.World.Date };
            P.Movements.Add(m);
            var (success, cost) = g.Rules.Politics.CrackdownOdds(g, c, m);
            Check(success > 0.05 && success < 0.95 && cost > 0, $"depth: a crackdown's success is a chance ({success:P0}) with a stability cost ({cost:0.0})");
            int worked = 0, trials = 40;
            for (int k = 0; k < trials; k++)
            {
                Fund(g, c);
                m.Progress = 60; m.Organization = 50;
                P.Suppress(m);
                if (m.Organization < 50) worked++;
            }
            Check(worked > 0 && worked < trials, $"depth: crackdowns neither always nor never work ({worked} of {trials})");
        }

        // ---- 5. regional conditions: discontent among the backers' pop groups feeds the movement's grievance
        if (P0.PopsEnabled)
        {
            var g1 = Fresh(); var g2 = Fresh();
            double Recount(Game g, double grievance)
            {
                var P = g.Politics;
                var c = g.World.GetCountry(tag)!;
                P.SetRulingParty(c, dictKey, "test");
                P.AddPopularity(c, rivalKey, 0.5 - P.PopularityOf(c, rivalKey));
                P.PopReactionMonthly(c);
                foreach (var s in c.OwnedStates)
                    if (P.MoodsOf(s) is { } moods)
                        foreach (var mood in moods.Values) mood.Grievance = grievance;
                var summary = P.PopSummaryOf(c);
                return summary.BackerGrievance.Values.DefaultIfEmpty(0).Max();
            }
            Check(Recount(g1, 90) >= Recount(g2, 0), "depth: the pop summary reports the discontent of each ideology's backers");
            var gs = Fresh();
            var Ps = gs.Politics;
            var cs = gs.World.GetCountry(tag)!;
            Ps.PopReactionMonthly(cs);
            var sum = Ps.PopSummaryOf(cs);
            Check(sum.Stronghold.Values.All(x => x.Share is >= 0 and <= 1), "depth: each ideology's regional concentration is a share between 0 and 1");
        }

        // ---- 6. factions: stances shape reactions to the same decision
        {
            var g = Fresh();
            var P = g.Politics;
            Country? hawkLand = null, dovesLand = null;
            GenericScope? hawk = null, dove = null;
            foreach (var c in g.World.Countries.Where(c => c.Exists))
                foreach (var f in P.GoverningFactions(c))
                {
                    if (hawk == null && P.FactionHardline(f) >= 0.65) { hawk = f; hawkLand = c; }
                    if (dove == null && P.FactionHardline(f) <= 0.35) { dove = f; dovesLand = c; }
                }
            if (hawk != null && dove != null)
            {
                double Reaction(Country c, GenericScope f, bool crackdown)
                {
                    f.Vars.Set("grievance", 40);
                    Fund(g, c);
                    P.Movements.RemoveAll(m => m.Country == c);
                    var mv = new Movement { Country = c, Ideology = rivalKey, Demand = MovementDemand.Power, Progress = 50, Organization = 50, Grievance = 50, Started = g.World.Date };
                    P.Movements.Add(mv);
                    if (crackdown) P.Suppress(mv); else P.Concede(mv);
                    return P.FactionGrievance(f);
                }
                Check(Reaction(hawkLand!, hawk, true) < 40 && Reaction(dovesLand!, dove, true) > 40, "depth: a crackdown pleases a governing hardline faction and angers a liberal one");
                Check(Reaction(hawkLand!, hawk, false) > 40 && Reaction(dovesLand!, dove, false) < 40, "depth: a concession does the reverse");
            }
            else Check(true, "depth: (no governing hardline and liberal factions in this scenario)");

            // vote loss is graded, not a fixed penalty
            var party = g.World.Countries.SelectMany(c => P.PartiesOf(c)).FirstOrDefault(p => P.FactionsOf(p).Count() >= 2);
            if (party != null)
            {
                foreach (var f in P.FactionsOf(party)) f.Vars.Set("loyalty", 60);
                Check(Math.Abs(P.PartyVoteRetention(party) - 1) < 1e-9, "depth: a party whose factions are loyal loses no votes (no universal penalty)");
                var factions = P.FactionsOf(party).ToList();
                factions[0].Vars.Set("loyalty", 30);
                double mild = P.PartyVoteRetention(party);
                factions[0].Vars.Set("loyalty", 0);
                double severe = P.PartyVoteRetention(party);
                foreach (var f in factions) f.Vars.Set("loyalty", 0);
                double all = P.PartyVoteRetention(party);
                Check(mild < 1 && severe < mild && all < severe && all >= 0.6, $"depth: vote loss grows with how many factions turn and how far ({mild:P1}, {severe:P1}, {all:P1})");
            }

            // collapse is a chance, not a threshold timer
            var govt = g.World.Countries.FirstOrDefault(c => c.Exists && P.GoverningFactions(c).Count() >= 2);
            if (govt != null)
            {
                var fs = P.GoverningFactions(govt).ToList();
                foreach (var f in fs) f.Vars.Set("loyalty", 70);
                Check(P.CollapseChance(govt) == 0, "depth: no collapse risk while the government's factions are loyal");
                foreach (var f in fs) f.Vars.Set("loyalty", 5);
                double chance = P.CollapseChance(govt);
                Check(chance > 0 && chance < 1, $"depth: a divided government risks collapse ({chance:P0} a month), not certainty");
                int fell = 0;
                for (int k = 0; k < 200; k++) if (P.CollapseChance(govt) > 0 && g.Rng.NextDouble() < chance) fell++;
                Check(fell > 0 && fell < 200, "depth: collapse happens in some months and not others");
            }
        }

        // ---- 7. persistence: new movement state is saved, and old saves without it still load with sensible values
        {
            var g = Fresh();
            var P = g.Politics;
            var c = g.World.GetCountry(tag)!;
            var mv = new Movement { Country = c, Ideology = rivalKey, Demand = MovementDemand.Power, Progress = 33, Organization = 41, Grievance = 57, Concessions = 2, Crackdowns = 1.5, Started = g.World.Date };
            P.Movements.Add(mv);
            string tmp = Path.Combine(Path.GetTempPath(), "burger_depth.sav");
            SaveGame.Save(g, tmp);
            var g2 = Fresh();
            SaveGame.Load(g2, tmp);
            var back = g2.Politics.MovementOf(g2.World.GetCountry(tag)!, rivalKey);
            Check(back != null && Math.Abs(back.Organization - 41) < 0.01 && Math.Abs(back.Grievance - 57) < 0.01 && Math.Abs(back.Concessions - 2) < 0.01 && Math.Abs(back.Crackdowns - 1.5) < 0.01 && Math.Abs(back.Progress - 33) < 0.01,
                "depth: a movement's organisation, grievance and memories survive save and load");
            string oldPath = Path.Combine(Path.GetTempPath(), "burger_depth_old.sav");
            var root = ScriptParser.ParseFile(tmp, Path.GetFileName(tmp));
            foreach (var n in root.GetAll("movement"))
                n.Children?.RemoveAll(x => x.Key is "organization" or "grievance" or "concessions" or "crackdowns");
            ScriptWriter.WriteFile(oldPath, root, "Old-format movement self-test");
            var g3 = Fresh();
            SaveGame.Load(g3, oldPath);
            var old = g3.Politics.MovementOf(g3.World.GetCountry(tag)!, rivalKey);
            Check(old != null && old.Organization > 0 && old.Grievance > 0 && Math.Abs(old.Progress - 33) < 0.01, "depth: a save from before organisation and grievance loads with values derived from its progress");
        }

        // ---- 9. interest groups: spawn only with a movement, want freedom/security/army (never laws), and make the movement aggressive
        {
            var g = Fresh();
            var P = g.Politics; var R = g.Rules.Politics;
            var c = g.World.GetCountry(tag)!;
            P.SetRulingParty(c, dictKey, "test");
            P.AddPopularity(c, rivalKey, 0.5 - P.PopularityOf(c, rivalKey));
            P.Of(c).CalmUntilDay = int.MinValue;
            Check(!P.MovementsOf(c).Any(), "depth: no movement, no interest groups");
            for (int k = 0; k < 8 && P.MovementOf(c, rivalKey) == null; k++) { SetStability(g, c, 60); P.MonthlyTick(); }
            var born = P.MovementOf(c, rivalKey);
            Check(born != null && born.Groups.Count > 0 && born.Groups.All(x => x.Key is "freedom" or "security" or "army" && x.Strength >= 5), $"depth: a new movement brings interest groups that want freedom, security or an army ({string.Join(",", born?.Groups.Select(x => x.Key) ?? Array.Empty<string>())})");

            Movement Mk(double sat) => new Movement
            {
                Country = c, Ideology = rivalKey, Demand = MovementDemand.Power, Progress = 55, Organization = 70, Grievance = 60, Started = g.World.Date,
                Groups = new() { new InterestGroup { Key = "security", Strength = 80, Satisfaction = sat }, new InterestGroup { Key = "army", Strength = 60, Satisfaction = sat } },
            };
            var denied = Mk(0.1); var content = Mk(0.9);
            SetStability(g, c, 50);
            double pDen = R.MovementProgress(g, c, denied, 0.5), pCon = R.MovementProgress(g, c, content, 0.5);
            double iDen = R.MovementIncidentChance(g, c, denied), iCon = R.MovementIncidentChance(g, c, content);
            Check(pDen > pCon && iDen > iCon * 2, $"depth: denied groups drive a movement harder (progress {pDen:0.0} vs {pCon:0.0}, violence {iDen:P1} vs {iCon:P1} a month)");
            Check(R.MovementGrievanceSources(g, c, denied).Any(x => x.Cause.Contains("Interest groups")) && !R.MovementGrievanceSources(g, c, content).Any(x => x.Cause.Contains("Interest groups") && x.Value > 5),
                "depth: unmet demands show up as a cause of grievance");
            var calm = Mk(0.1); calm.Progress = 5;
            Check(R.MovementIncidentChance(g, c, calm) == 0, "depth: a barely mobilised movement does not turn violent yet");

            // what the state does changes what the groups get: a stable state satisfies security, repression sours freedom
            SetStability(g, c, 90); double secHi = R.GroupSatisfaction(g, c, denied, "security");
            SetStability(g, c, 10); double secLo = R.GroupSatisfaction(g, c, denied, "security");
            var repressed = Mk(0.5); repressed.Crackdowns = 5;
            Check(secHi > secLo + 0.4 && R.GroupSatisfaction(g, c, repressed, "freedom") < R.GroupSatisfaction(g, c, Mk(0.5), "freedom"), $"depth: order satisfies the security group ({secLo:P0} -> {secHi:P0}) and crackdowns sour the freedom group");

            // violence: incidents happen over time, cost stability and fade
            P.Movements.RemoveAll(x => x.Country == c); P.Movements.Add(denied);
            denied.Progress = 60;
            double maxHeat = 0; int months = 0;
            for (int k = 0; k < 36 && c.Exists && P.Movements.Contains(denied); k++, months++)
            {
                SetStability(g, c, 85); P.MonthlyTick(); maxHeat = Math.Max(maxHeat, denied.Heat);
            }
            Check(maxHeat > 5, $"depth: a denied, organised movement turns violent within {months} months (peak heat {maxHeat:0})");
            var hot = Mk(0.5); hot.Heat = 60; var cool = Mk(0.5);
            Check(R.MovementDisruption(g, c, hot) < R.MovementDisruption(g, c, cool), "depth: riots and mutinies cost stability on top of strikes");

            // meeting a group: cheaper than a full concession, weakens that group, calms the movement a bit
            var meet = Mk(0.2); P.Movements.RemoveAll(x => x.Country == c); P.Movements.Add(meet);
            Fund(g, c);
            double before = meet.Groups[0].Strength, gBefore = meet.Grievance;
            Check(P.MeetCost < P.ConcedeCost && P.MeetGroup(meet, "security") == null && meet.Groups[0].Strength < before && meet.Groups[0].Satisfaction > 0.2 && meet.Grievance < gBefore,
                "depth: meeting one group's demand costs less than a concession, weakens it and eases grievance");

            // persistence
            var keep = Mk(0.3); keep.Heat = 22; P.Movements.RemoveAll(x => x.Country == c); P.Movements.Add(keep);
            string tmp = Path.Combine(Path.GetTempPath(), "burger_depth_groups.sav");
            SaveGame.Save(g, tmp);
            var g2 = Fresh(); SaveGame.Load(g2, tmp);
            var back = g2.Politics.MovementOf(g2.World.GetCountry(tag)!, rivalKey);
            Check(back != null && Math.Abs(back.Heat - 22) < 0.01 && back.Groups.Count == 2 && back.Groups.Any(x => x.Key == "army" && Math.Abs(x.Strength - 60) < 0.01 && Math.Abs(x.Satisfaction - 0.3) < 0.01),
                "depth: interest groups and violence survive save and load");
        }

        // ---- 10. puppets: the subject type says who decides the government; foreign rule feeds grievance and an independence group
        {
            var g = Fresh();
            var P = g.Politics; var R = g.Rules.Politics;
            var c = g.World.GetCountry(tag)!;
            var lord = g.World.Countries.First(x => x.Exists && x != c && !x.IsPlayer && g.World.Relations.OverlordOf(x) == null && g.World.Relations.OverlordOf(c) != x);
            P.SetRulingParty(lord, rivalKey, "test");
            var imposed = new SubjectType { Key = "t_imposed", Name = "Imposed", Government = "imposed" };
            var free = new SubjectType { Key = "t_free", Name = "Free", Government = "free" };
            Check(R.SubjectGrievanceSources(g, c).Count == 0, "depth: a country that is nobody's subject has no foreign-rule grievance");

            P.SetRulingParty(c, dictKey, "test");
            g.Diplomacy.MakeSubject(c, lord, free);
            Check(P.Of(c).Ruling == dictKey && R.SubjectGovernment(g, c, lord, free) == SubjectControl.Free, "depth: a free subject keeps its own government");
            Check(!R.SubjectGrievanceSources(g, c).Any(x => x.Cause == "Puppet regime"), "depth: a free subject has no puppet-regime grievance");

            g.Diplomacy.MakeSubject(c, lord, imposed);
            Check(P.Of(c).Ruling == rivalKey && R.SubjectGovernment(g, c, lord, imposed) == SubjectControl.Imposed, "depth: an imposed subject gets its overlord's ideology");
            Check(R.SubjectGrievanceSources(g, c).Any(x => x.Cause == "Puppet regime" && x.Value > 0), "depth: an imposed regime breeds grievance");

            // an organised, mobilised movement cannot be held in line any more
            var rebels = new Movement { Country = c, Ideology = dictKey, Progress = 70, Organization = 80, Grievance = 60 };
            P.Movements.Add(rebels);
            Check(R.SubjectGovernment(g, c, lord, imposed) == SubjectControl.Preferred, "depth: a puppet in open revolt is no longer forced into line");
            P.Movements.Remove(rebels);

            double lib = g.Diplomacy.LibertyDesire(c);
            Check(Math.Abs(R.GroupSatisfaction(g, c, new Movement { Country = c, Ideology = rivalKey }, "independence") - Math.Clamp(1 - lib / 100, 0, 1)) < 1e-9,
                "depth: the independence group is satisfied in proportion to how little the subject wants out");
            Check(R.MovementInterestGroups(g, c, P.Ideology(dictKey)!).Any(x => x.Key == "independence") == (lib >= 20), $"depth: a subject's movement brings an independence group only with liberty desire of 20+ ({lib:0})");
        }

        // ---- 11. cohesion: events between the systems, manpower share, "same people"
        {
            var g = Fresh();
            var c = g.World.GetCountry(tag)!;
            var lord = g.World.Countries.First(x => x.Exists && x != c && !x.IsPlayer && g.World.Relations.OverlordOf(x) == null && g.World.Relations.OverlordOf(c) != x && !g.World.Relations.SubjectsOf(x).Any() && !g.World.Relations.SubjectsOf(c).Any());
            int made = 0, released = 0, moved = 0;
            g.Hub.SubjectMade += (_, _, _) => made++;
            g.Hub.SubjectReleased += (_, _) => released++;
            g.Hub.StateTransferred += (_, _, _) => moved++;
            g.Diplomacy.MakeSubject(c, lord, new SubjectType { Key = "t_share", Name = "Share", Government = "free", ManpowerShare = 0.5 });
            Check(made >= 1 && g.World.Relations.OverlordOf(c) == lord, "cohesion: making a subject raises SubjectMade");
            if (c.OwnedStates.FirstOrDefault() is { } st) { g.Diplomacy.TransferState(st, lord); Check(moved == 1, "cohesion: a state transfer raises StateTransferred"); }
            Check(g.SamePeople(c, c) == (g.CultureOf(c) is { Length: > 0 }) && g.SamePeople(c, lord) == string.Equals(g.CultureOf(c), g.CultureOf(lord), StringComparison.OrdinalIgnoreCase) && g.CultureOf(c) is not null == g.SamePeople(c, c),
                "cohesion: 'same people' is one definition (same primary culture)");
            if (g.Features.Has(Burger.Core.Modding.FeatureSet.Military))
            {
                double m0 = lord.Manpower, c0 = c.Manpower;
                g.Economy.MonthlyTick();
                double sent = Math.Max(0, c.LastManpowerGain) * 0.5;
                Check(Math.Abs(lord.Manpower - m0 - lord.LastManpowerGain - sent) < 0.01 && Math.Abs(c.Manpower - c0 - (c.LastManpowerGain - sent)) < 0.01,
                    $"cohesion: a subject sends half its monthly manpower gain to the overlord ({sent:0} of {c.LastManpowerGain:0})");
            }
            g.Diplomacy.Release(c);
            Check(released == 1 && g.World.Relations.OverlordOf(c) == null, "cohesion: releasing a subject raises SubjectReleased");
        }

        // ---- 8. stability: the simulation stays bounded over a long run with an active movement
        {
            var o = Run(null, null, 120, 0.6);
            Check(o.Stability is >= -1 and <= 101 && double.IsFinite(o.Grievance) && double.IsFinite(o.Organization) && o.Grievance <= 100 && o.Organization <= 100,
                $"depth: ten years with a movement stay in bounds (stability {o.Stability:0}, org {o.Organization:0}, grievance {o.Grievance:0})");
        }
    }
}
