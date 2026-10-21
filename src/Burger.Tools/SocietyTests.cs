using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Population;
using Burger.Core.Save;
using Burger.Core.Society;

namespace Burger.Tools;

public static partial class Program
{
    /// <summary>
    /// The culture, government, succession and dynasty features: traditions and innovations reach the country, cultures that share a state become
    /// one, reforms cost and wait, ideas unlock, corruption costs income, laws pick heirs, minors get regents, gavelkind divides the realm,
    /// dynasties earn and spend prestige, households grow and characters fall ill; and all of it survives a save.
    /// </summary>
    private static void SocietyFeatureTests(string project)
    {
        Game Fresh()
        {
            var g = Game.Load(GameDir(project), compileScripts: true, seed: 21);
            g.StartNewGame(null);
            g.Ai.Enabled = false;
            return g;
        }
        var g = Fresh();
        var F = g.Features;

        // families at the start of the game
        {
            var rulers = g.World.Countries.Where(c => c.Exists && c.Ruler != null).ToList();
            Check(rulers.All(c => c.Ruler!.Spouse is { Alive: true } sp && sp.Female != c.Ruler.Female), "families: every ruler starts with a spouse");
            Check(rulers.Count(c => c.Ruler!.Children.Count >= 1) > rulers.Count * 0.7, $"families: most rulers start with children ({rulers.Count(c => c.Ruler!.Children.Count >= 1)} of {rulers.Count})");
            var any = rulers.First(c => c.Ruler!.Children.Count > 0).Ruler!;
            Check(any.Children.All(k => k.Father != null && k.Mother != null && (k.Father == any || k.Mother == any) && k.Family.Length > 0 && k.Age(g.World.Date) >= 0 && k.Age(g.World.Date) <= any.Age(g.World.Date) - 16), "families: children have two parents and were born after the parents came of age");
            Check(rulers.Any(c => c.Ruler!.Father != null), "families: some rulers have living parents");
        }

        // ------------------------------------------------------------------------------------- ideologies and assemblies
        if (g.Politics.Enabled && g.Politics.AllIdeologies.Any(i => i.HasUnlock))
        {
            var P = g.Politics;
            var late = P.AllIdeologies.First(i => i.HasUnlock && i.UnlockYear is > -300);
            Check(P.Ideologies.All(i => !i.HasUnlock) && !P.Ideologies.Contains(late), $"ideologies: only those of the start are in play ({P.Ideologies.Count} of {P.AllIdeologies.Count}); '{late.Name}' waits");
            Check(g.World.Countries.Where(c => c.Exists).All(c => !P.Of(c).Popularity.ContainsKey(late.Key)), "ideologies: no country follows a locked ideology");
            var gm = Fresh();
            gm.Politics.Unlock(late.Key);
            Check(!gm.Politics.Ideologies.Contains(gm.Politics.IdeologyByKey[late.Key]), "ideologies: unlocking takes effect at the next month");
            for (int d = 0; d < 40; d++) gm.Tick(); // an hour each: move over a month change
            while (gm.World.Date.Day != 1) gm.Tick();
            gm.Tick();
            var lateDef = gm.Politics.IdeologyByKey[late.Key];
            var live = gm.World.Countries.Where(c => c.Exists).ToList();
            Check(gm.Politics.Ideologies.Contains(lateDef) && live.All(c => gm.Politics.Of(c).Popularity.GetValueOrDefault(late.Key) > 0 && Math.Abs(gm.Politics.Of(c).Popularity.Values.Sum() - 1) < 1e-6), $"ideologies: '{late.Name}' appears in every country once unlocked");
            string unlockSave = Path.Combine(Path.GetTempPath(), "burger_ideology.sav");
            SaveGame.Save(gm, unlockSave);
            var g3 = Game.Load(GameDir(project), compileScripts: true, seed: 21);
            SaveGame.Load(g3, unlockSave);
            Check(g3.Politics.IsUnlocked(late.Key) && g3.Politics.Ideologies.Any(i => i.Key == late.Key), "ideologies: a save keeps what was unlocked");
            var sparta = g.World.GetCountry("SPA");
            var persia = g.World.GetCountry("ACH");
            if (sparta != null && persia != null)
                Check(P.HasAssembly(sparta) && !P.HasAssembly(persia), "politics: a council state has an assembly and an absolute monarchy has none");
            if (sparta != null)
            {
                var seatsOf = P.Of(sparta).Seats.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();
                var chamber = P.Ideology(P.Of(sparta).Ruling)!.Chamber;
                Check(P.Of(sparta).TotalSeats == 30 && seatsOf.Count > 0 && seatsOf.All(k => k == P.Of(sparta).Ruling || chamber.Contains(k)), $"politics: a council of elders seats only nobles and priests ({string.Join(", ", seatsOf)} in {P.Of(sparta).TotalSeats} seats)");
            }
        }

        // ------------------------------------------------------------------------------------- laws by age
        if (g.Politics.LawsEnabled && g.Politics.LawGroups.Any(x => x.HasUnlock))
        {
            var P = g.Politics;
            var c0 = g.World.Countries.First(c => c.Exists);
            var press = P.LawGroups.First(x => x.Key == "press");
            Check(!P.IsGroupVisible(c0, press) && P.LawGroups.Where(x => P.IsGroupVisible(c0, x)).All(x => !x.HasUnlock), $"laws: only laws of the age are offered ({P.LawGroups.Count(x => P.IsGroupVisible(c0, x))} of {P.LawGroups.Count} groups)");
            var army = P.Laws["standing_army"];
            Check(!P.IsLawVisible(c0, army) && P.CannotEnact(c0, army) != null, "laws: a law of a later age cannot be enacted");
            var gl = Fresh();
            gl.Politics.Unlock("press");
            gl.Politics.Unlock("standing_army");
            for (int i = 0; i < 40; i++) gl.Tick();
            while (gl.World.Date.Day != 1) gl.Tick();
            gl.Tick();
            var c1 = gl.World.Countries.First(c => c.Exists);
            Check(gl.Politics.IsGroupVisible(c1, gl.Politics.LawGroups.First(x => x.Key == "press")) && gl.Politics.IsLawUnlocked(gl.Politics.Laws["standing_army"]), "laws: a group and a law can be unlocked by script");
        }

        // ------------------------------------------------------------------------------------- culture
        if (g.CultureLife.Enabled)
        {
            var C = g.CultureLife;
            Check(g.Population.CultureByKey.Keys.All(k => C.Of(k).Traditions.Count == 3), "culture: every culture has three traditions");
            var country = g.World.Countries.First(c => c.Exists && g.Population.Of(c).Culture != null && C.Shares(c).GetValueOrDefault(g.Population.Of(c).Culture!) > 0.5);
            string ck = C.PrimaryCulture(country)!;
            var tr = C.TraditionsOf(ck).First();
            string tkey = tr.Modifier.Keys.First();
            Check(g.Modifiers.Sum(country, tkey) >= tr.Modifier[tkey] * 0.5 - 1e-9, $"culture: a tradition changes {tkey} of the country ({tr.Name})");

            // two cultures in one state for fifty years become a new one
            var state = g.World.States.First(s => s.Owner != null && g.Population.HasPops(s) && s.Owner != country);
            var sp = g.Population.Of(state);
            string a = sp.Pops[0].Culture;
            string b = g.Population.CultureByKey.Keys.First(k => k != a && g.Population.CultureByKey[k].Group != g.Population.CultureByKey[a].Group);
            sp.Pops.Clear();
            sp.Pops.Add(new Pop { Culture = a, Religion = null, Type = null, Size = 6000 });
            sp.Pops.Add(new Pop { Culture = b, Religion = null, Type = null, Size = 4000 });
            int months = g.Rules.Society.EvolutionMonths(g);
            for (int m = 0; m < months - 1; m++) C.MonthlyTick();
            Check(!sp.Pops.Any(p => C.Of(p.Culture).IsHybrid), $"culture: no new culture before {months / 12} years of mixing");
            C.MonthlyTick();
            var hybrid = sp.Pops.Select(p => p.Culture).Distinct().ToList();
            Check(hybrid.Count == 1 && C.Of(hybrid[0]).IsHybrid && g.Population.CultureByKey.ContainsKey(hybrid[0]), $"culture: after {months / 12} years the state's people share a new culture ({hybrid.FirstOrDefault()})");
            var hs = C.Of(hybrid[0]);
            Check(hs.Traditions.Count >= 3 && hs.Traditions.Values.All(v => v <= 1.0001) && hs.Traditions.Values.Any(v => Math.Abs(v - 0.5) < 1e-6 || Math.Abs(v - 1) < 1e-6), "culture: the new culture holds both parents' traditions at half weight");
            string hybridKey = hybrid[0];

            // ---------------------------------------------------------------------------------- save
            string tmp = Path.Combine(Path.GetTempPath(), "burger_society.sav");
            SaveGame.Save(g, tmp);
            var g2 = Game.Load(GameDir(project), compileScripts: true, seed: 21);
            SaveGame.Load(g2, tmp);
            Check(g2.Population.CultureByKey.ContainsKey(hybridKey) && g2.CultureLife.Of(hybridKey).IsHybrid && g2.CultureLife.Of(hybridKey).Traditions.Count == C.Of(hybridKey).Traditions.Count, "culture: a save keeps new cultures and their traditions");
        }

        // ------------------------------------------------------------------------------------- government
        if (g.Reforms.Enabled)
        {
            var R = g.Reforms;
            var c = g.World.Countries.First(x => x.Exists && !x.IsPlayer && x.OwnedStates.Count > 1);
            var power = g.Politics.PowerResource!;
            g.Resources.Set(c, power, 1000);
            R.Of(c).Reforms.Clear();
            R.Of(c).LastChangeDay = int.MinValue / 2;
            var first = R.Reforms["scribal_registry"];
            var second = R.Reforms["provincial_governors"];
            Check(R.CannotAdopt(c, second) != null, "government: a reform needs the reforms it requires");
            double ppBefore = g.Resources.Get(c, power), incBefore = g.Modifiers.Sum(c, "income");
            Check(R.Adopt(c, first) == null && R.Has(c, first.Key), "government: a reform is adopted");
            Check(Math.Abs(ppBefore - g.Resources.Get(c, power) - g.Rules.Society.ReformCost(g, first)) < 1e-6, $"government: it costs {g.Rules.Society.ReformCost(g, first):0} political power");
            Check(g.Modifiers.Sum(c, "income") > incBefore && R.CentralisationSteps(c) >= 1, "government: its modifiers reach the country without code");
            Check(R.CannotAdopt(c, second)?.Contains("recently") == true, "government: a year must pass before the next change");
            R.Of(c).LastChangeDay -= 400;
            Check(R.Adopt(c, second) == null, "government: after a year the next reform can follow");
            R.Of(c).LastChangeDay -= 400;
            var auto = R.Reforms["local_autonomy"];
            Check(R.Adopt(c, auto) == null && !R.Has(c, first.Key) && !R.Has(c, second.Key) && R.Has(c, auto.Key), "government: a reform can remove others");
            R.Of(c).Reforms.Clear();
            foreach (var k in new[] { "scribal_registry", "levy_system", "market_charters", "provincial_governors", "standing_guard" }) R.Force(c, k, true);
            R.Of(c).LastChangeDay = int.MinValue / 2;
            Check(R.Of(c).Reforms.Count == 5 && R.CannotAdopt(c, R.Reforms["tax_farming"])?.Contains("at most") == true, "government: a country holds five reforms at most");

            // ideas
            c.Vars.Set(GovernmentSystem.IdeaPointsKey, 175);
            int unlocked = R.Unlocked(c);
            R.MonthlyTick();
            Check(R.Unlocked(c) >= unlocked + 3, $"government: 50 idea points unlock a national idea ({unlocked} -> {R.Unlocked(c)})");
            var set = R.SetOf(c)!;
            int pick = set.Ideas.FindIndex(i => i.Modifier.Count > 0 && !i.Modifier.ContainsKey("income") && !i.Modifier.ContainsKey("stability_target"));
            R.Of(c).IdeasUnlocked = pick;
            c.Vars.Set(GovernmentSystem.IdeaPointsKey, 0);
            R.MonthlyTick();
            string ik = set.Ideas[pick].Modifier.Keys.First();
            double iv0 = g.Modifiers.Sum(c, ik);
            c.Vars.Set(GovernmentSystem.IdeaPointsKey, 55);
            R.MonthlyTick();
            Check(R.Unlocked(c) == pick + 1 && Math.Abs(g.Modifiers.Sum(c, ik) - iv0 - set.Ideas[pick].Modifier[ik]) < 1e-9, $"government: the idea '{set.Ideas[pick].Name}' gives its modifier ({ik} {set.Ideas[pick].Modifier[ik]:+0.##;-0.##}; was {iv0:0.####}, now {g.Modifiers.Sum(c, ik):0.####}, unlocked {R.Unlocked(c)} of pick {pick})");

            // corruption
            R.SetCorruption(c, 5);
            double low = g.Modifiers.Sum(c, "income");
            R.SetCorruption(c, 45);
            R.MonthlyTick(); // drops the cached sums
            double high = g.Modifiers.Sum(c, "income");
            Check(low - high > 0.15, $"government: corruption costs income ({low:0.###} -> {high:0.###})");
            R.SetCorruption(c, 60);
            R.Of(c).LastCorruptionEventDay = int.MinValue / 2;
            R.MonthlyTick();
            Check(R.Of(c).LastCorruptionEventDay > int.MinValue / 4, "government: at 50 corruption or more an event is raised");

            // distance: a capital far from most of the people raises corruption, centralisation lowers it
            var far = g.World.Countries.Where(x => x.Exists && x.OwnedStates.Count > 4).OrderByDescending(R.MillionsBeyondRange).First();
            Check(g.Rules.Society.CorruptionChange(g, far, 3, 0) > 0 && g.Rules.Society.CorruptionChange(g, far, 0, 2) < 0, "government: distance raises corruption and centralisation lowers it");

            string tmp = Path.Combine(Path.GetTempPath(), "burger_society_gov.sav");
            SaveGame.Save(g, tmp);
            var g2 = Game.Load(GameDir(project), compileScripts: true, seed: 21);
            SaveGame.Load(g2, tmp);
            var c2 = g2.World.GetCountry(c.Tag)!;
            Check(g2.Reforms.Of(c2).Reforms.SequenceEqual(R.Of(c).Reforms) && g2.Reforms.Unlocked(c2) == R.Unlocked(c) && Math.Abs(g2.Reforms.Corruption(c2) - R.Corruption(c)) < 1e-6, "government: a save keeps reforms, ideas and corruption");
        }

        // ------------------------------------------------------------------------------------- succession
        if (g.Succession.Enabled)
        {
            var S = g.Succession;
            (Country, Character, Character, Character, Character) Family(Game gm)
            {
                var c = gm.World.Countries.First(x => x.Exists && x.OwnedStates.Count >= 4 && x.Ruler != null && !x.IsPlayer);
                var ruler = c.Ruler!;
                ruler.Children.Clear();
                foreach (var ch in gm.World.Characters.Where(ch => ch.Country == c && ch != ruler).ToList()) ch.Country = null;
                Character K(bool f, int age) { var k = gm.Characters.Generate(c, f, age, ruler.Family); k.Father = ruler; ruler.Children.Add(k); return k; }
                return (c, ruler, K(false, 22), K(false, 12), K(true, 25));
            }
            void SetLaw(Game gm, Country c, string key)
            {
                if (gm.Politics.LawsEnabled && gm.Politics.Laws.TryGetValue(key, out var law)) gm.Politics.Enact(c, law, free: true);
                else c.Definition.Add("succession_law", key);
            }

            // primogeniture: the eldest son, then the eldest daughter
            {
                var gm = Fresh();
                var (c, ruler, elder, younger, daughter) = Family(gm);
                SetLaw(gm, c, "primogeniture");
                var line = gm.Succession.Line(c, ruler).Take(3).ToList();
                Check(line.SequenceEqual(new[] { elder, younger, daughter }), "succession: primogeniture - eldest son, younger son, then the daughter");
                gm.Characters.Kill(ruler);
                Check(c.Ruler == elder && c.Regent == null, "succession: the eldest son rules and an adult needs no regent");
            }
            // ultimogeniture: the youngest, a child with a regent
            {
                var gm = Fresh();
                var (c, ruler, elder, younger, daughter) = Family(gm);
                var baby = gm.Characters.Generate(c, false, 5, ruler.Family);
                baby.Father = ruler; ruler.Children.Add(baby);
                SetLaw(gm, c, "ultimogeniture");
                Check(gm.Succession.Line(c, ruler).First() == baby, "succession: ultimogeniture - the youngest son first");
                gm.Characters.Kill(ruler);
                Check(c.Ruler == baby && c.Regent != null && c.Regent != baby && c.Governing == c.Regent && gm.Succession.InRegency(c), "succession: a child ruler gets a regent who governs");
                double stab = gm.Modifiers.Sum(c, "stability_target");
                baby.BirthDate = gm.World.Date.AddYears(-16);
                gm.Succession.MonthlyTick();
                Check(c.Regent == null && gm.Modifiers.Sum(c, "stability_target") > stab, "succession: the regency ends when the ruler is of age and its stability penalty goes");
            }
            // elective: the council votes
            {
                var gm = Fresh();
                var (c, ruler, elder, younger, daughter) = Family(gm);
                SetLaw(gm, c, "elective");
                var votes = gm.Succession.Vote(c, ruler);
                Check(votes.Count > 0 && votes.Sum(v => v.Votes) == gm.Succession.Electors(c, ruler).Count && gm.Succession.Line(c, ruler).First() == votes[0].Candidate, $"succession: the elective crown goes to the candidate with most votes ({votes.Sum(v => v.Votes)} electors)");
                gm.Characters.Kill(ruler);
                Check(c.Ruler == votes[0].Candidate, "succession: the elected candidate becomes ruler");
            }
            // gavelkind: the realm is shared
            {
                var gm = Fresh();
                var (c, ruler, elder, younger, daughter) = Family(gm);
                younger.BirthDate = gm.World.Date.AddYears(-20);
                SetLaw(gm, c, "gavelkind");
                int countries = gm.World.Countries.Count(x => x.Exists), states = c.OwnedStates.Count;
                Check(gm.Succession.LawOf(c).Key == "gavelkind", $"succession: gavelkind is in force (law {gm.Succession.LawOf(c).Key}, {states} states)");
                gm.Characters.Kill(ruler);
                Check(c.Ruler == elder && gm.World.Countries.Count(x => x.Exists) == countries + 1 && gm.Succession.Partitions == 1, "succession: gavelkind makes a country for the other son");
                var realm = gm.World.Countries.FirstOrDefault(x => x.Ruler == younger);
                if (realm == null) { Check(false, $"succession: the younger son rules a share (ruler now {c.Ruler?.DisplayName}, partitions {gm.Succession.Partitions}, younger in {younger.Country?.Tag})"); }
                else
                Check(gm.World.Relations.OverlordOf(realm) == c && realm.OwnedStates.Count + c.OwnedStates.Count == states && realm.OwnedStates.Count >= 1 && c.Capital != null && c.OwnedStates.Any(s => s.Locations.Contains(c.Capital)), "succession: the younger son rules a share as a subject and the heir keeps the capital");
            }
            // the law changes only after quiet years
            if (g.Politics.LawsEnabled && g.Politics.LawGroups.FirstOrDefault(x => x.Key == "succession") is { } grp)
            {
                var gm = Fresh();
                var (c, ruler, _, _, _) = Family(gm);
                var law = gm.Politics.Laws["elective"];
                gm.Resources.Set(c, gm.Politics.PowerResource!, 500);
                Check(gm.Politics.Enact(c, law) == null && gm.Succession.LawOf(c).Key == "elective", "succession: the law is changed in the laws menu for 50 political power");
                gm.Characters.Kill(ruler);
                var back = gm.Politics.Laws["primogeniture"];
                gm.Politics.Of(c).LawChangedDay[grp.Key] = int.MinValue / 2;
                Check(gm.Politics.CannotEnact(c, back) != null, "succession: a law cannot change right after a succession");
            }
            Check(g.Succession.Laws.Count >= 4, "succession: four laws are defined");
        }

        // ------------------------------------------------------------------------------------- dynasties
        if (g.Dynasties.Enabled)
        {
            var gm = Fresh();
            var D = gm.Dynasties;
            var c = gm.World.Countries.First(x => x.Exists && x.Ruler != null && !x.IsPlayer);
            var ruler = c.Ruler!;
            double earned = D.StateOf(ruler.Family).Prestige;
            var heir = c.Heir ?? gm.Characters.Generate(c, false, 25, ruler.Family);
            heir.Family = ruler.Family;
            heir.Female = false;
            heir.BirthDate = gm.World.Date.AddYears(-30);
            c.Heir = heir;
            gm.Characters.Kill(ruler);
            Check(D.StateOf(c.Ruler!.Family).Prestige > earned, "dynasties: a new ruler adds prestige to the line");

            // households and illness over many years
            if (gm.Politics.LawsEnabled && gm.Politics.Laws.TryGetValue("concubinage", out var conLaw)) gm.Politics.Enact(c, conLaw, free: true);
            for (int y = 0; y < 150; y++) D.YearlyTick();
            Check(D.BastardsBorn > 0, $"dynasties: rulers father bastards ({D.BastardsBorn} in 150 years)");
            var bastard = gm.World.Characters.FirstOrDefault(ch => D.IsBastard(ch));
            Check(bastard == null || (D.Claim(bastard) == 0.5 && bastard.Father != null), "dynasties: a bastard carries half the claim");
            Check(D.ConcubinesTaken > 0 == (gm.Politics.LawsEnabled), $"dynasties: concubines only under the concubinage law ({D.ConcubinesTaken} taken)");
            Check(D.Illnesses > 30, $"dynasties: characters fall ill ({D.Illnesses} in 150 years)");
            var ill = gm.World.Characters.Where(ch => ch.Alive && D.IsSick(ch)).ToList();
            foreach (var ch in ill) ch.Vars.Set("sick_until", gm.World.Date.TotalDays);
            D.MonthlyTick();
            Check(ill.All(ch => !D.IsSick(ch)) && D.IllnessDeaths > 0 && D.IllnessDeaths < ill.Count, $"dynasties: an illness ends with recovery or death ({D.IllnessDeaths} of {ill.Count} died)");

            string tmp = Path.Combine(Path.GetTempPath(), "burger_society_dyn.sav");
            SaveGame.Save(gm, tmp);
            var g2 = Game.Load(GameDir(project), compileScripts: true, seed: 21);
            SaveGame.Load(g2, tmp);
            Check(Math.Abs(g2.Dynasties.StateOf(c.Ruler!.Family).Prestige - D.StateOf(c.Ruler.Family).Prestige) < 1e-6
                  && g2.World.Characters.Count(ch => ch.Alive && g2.Dynasties.IsSick(ch)) == gm.World.Characters.Count(ch => ch.Alive && D.IsSick(ch)), "dynasties: a save keeps prestige and illness");
        }
    }
}
