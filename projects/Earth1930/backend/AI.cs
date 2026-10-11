 // AI: how computer-controlled countries manage armies and fronts, recruit, use diplomacy and make peace.
 // Each part only runs when the game has the matching feature (military, diplomacy).
 //
 // This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
 // It starts as a copy of the engine's default. Edit anything you like; delete the file to use the engine default again.

 /// <summary>How AI countries run their armies, recruit, use diplomacy and make peace.</summary>
 public class GameAi : AiSystem
 {
     public GameAi(Game game) : base(game) { }

     /// <summary>
     /// Builds the best demands the AI can afford with its war score: first what the war is about (war goal states, freeing
     /// a subject, regime change, liberating a people), each state going to the side member that fights for it (faction war
     /// goals), then other occupied land; never more than the war goals allow (max_demands).
     /// </summary>
     public override PeaceOffer BuildDemands(War war, Country winner, Country loser, double score)
     {
         var D = Game.Diplomacy;
         var rules = Game.Rules.Diplomacy;
         var offer = new PeaceOffer { War = war, From = winner, To = loser, FromIsWinner = true };
         var winnerSide = war.SideOf(winner).ToHashSet();
         var loserSide = war.SideOf(loser);
         double budget = Math.Min(score, D.MaxDemands(war, winner));
         double used = 0;
         bool Try(PeaceTerm t)
         {
             var (wn, ls) = (winner, t.State?.Owner ?? loser);
             double cost = rules.PeaceTermCost(Game, war, t, wn, ls);
             if (used + cost > budget) return false;
             offer.Terms.Add(t);
             if (D.InvalidReason(offer) != null) { offer.Terms.Remove(t); return false; }
             used += cost;
             return true;
         }
         var goals = war.Goals.Where(g => winnerSide.Contains(g.Holder)).ToList();
         // freeing subjects of the losing side that fight with us for independence
         foreach (var g in goals.Where(g => g.CasusBelli.WarGoal == WarGoalType.Independence))
             Try(new PeaceTerm { Type = PeaceTermType.Independence, Beneficiary = g.Holder });
         // annexation when everything is held
         var held = loserSide.SelectMany(l => l.OwnedStates)
             .Select(s => (State: s, Held: s.Locations.Count(l => l.Controller != null && winnerSide.Contains(l.Controller)) / (double)Math.Max(1, s.Locations.Count)))
             .Where(x => x.Held >= 0.5).ToList();
         if (loser.OwnedStates.Count > 0 && held.Count(x => x.State.Owner == loser) == loser.OwnedStates.Count)
         {
             var annex = new PeaceTerm { Type = PeaceTermType.Annex, Beneficiary = winner };
             if (Try(annex)) return offer;
         }
         // regime change for ideological wars
         if (Game.Politics.Enabled && goals.Any(g => g.CasusBelli.WarGoal == WarGoalType.Ideology) && Game.Politics.Of(loser).Ruling != Game.Politics.Of(winner).Ruling)
             Try(new PeaceTerm { Type = PeaceTermType.RegimeChange, Beneficiary = winner, Ideology = Game.Politics.Of(winner).Ruling });
         // liberating peoples
         if (Game.Population.Enabled && goals.Any(g => g.CasusBelli.WarGoal == WarGoalType.Liberation))
             foreach (var g in goals.Where(g => g.CasusBelli.WarGoal == WarGoalType.Liberation))
                 foreach (var st in D.GoalStates(g).Where(s => held.Any(h => h.State == s)).Take(3))
                     if (Game.Population.MajorityCulture(st) is { } mc && !offer.Terms.Any(t => t.Type == PeaceTermType.ReleaseCountry && t.Culture == mc))
                         Try(new PeaceTerm { Type = PeaceTermType.ReleaseCountry, State = st, Culture = mc, Beneficiary = winner });
         // democracies (peace_puppets) set up friendly subjects instead of taking foreign land: the defeated country itself
         // becomes a subject when we hold much of its own people's land, other peoples in the land we hold are released as
         // subjects; we take only land of our own (accepted) peoples, cores and claims
         var pop = Game.Population;
         var rel = Game.World.Relations;
         bool puppets = rules.PrefersPuppets(Game, winner) && rel.OverlordOf(winner) == null; // subjects have no subjects
         bool subjugated = false;
         bool OursToTake(State s) => !puppets || !pop.Enabled || s.Cores.Contains(winner) || s.Claims.Contains(winner)
                                     || pop.MajorityCulture(s) is not { } mc || pop.IsAccepted(winner, mc);
         if (puppets && pop.Enabled)
         {
             string loserCulture = pop.Of(loser).Culture ?? "";
             var homeHeld = held.Where(h => h.State.Owner == loser && pop.MajorityCulture(h.State) == loserCulture && !OursToTake(h.State)).ToList();
             int home = loser.OwnedStates.Count(s => pop.MajorityCulture(s) == loserCulture);
             if (rel.OverlordOf(loser) == null && homeHeld.Count > 0 && homeHeld.Count * 3 >= Math.Max(1, home) && !D.HoldsHomelandOf(winner, loser))
                 subjugated = Try(new PeaceTerm { Type = PeaceTermType.Subjugate, Beneficiary = winner, SubjectType = Game.Data.SubjectTypes.Keys.First() });
             if (!subjugated)
                 foreach (var (state, _) in held.OrderByDescending(x => x.Held).ThenByDescending(x => x.State.Tax))
                 {
                     if (OursToTake(state) || pop.MajorityCulture(state) is not { } mc) continue;
                     if (mc == (pop.Of(state.Owner!).Culture ?? "")) continue; // the owner's own people: not split off
                     if (offer.Terms.Any(t => t.Type == PeaceTermType.ReleaseCountry && t.Culture == mc)) continue;
                     if (offer.Terms.Count(t => t.Type == PeaceTermType.ReleaseCountry) >= 3) break;
                     Try(new PeaceTerm { Type = PeaceTermType.ReleaseCountry, State = state, Culture = mc, Beneficiary = winner, AsSubject = true });
                 }
         }
         // land: goal states first (to whoever fights for them), then the rest of the held land. Allies and subjects only get
         // land that touches their own (or land they get in this deal): no far-away pieces for puppets; the rest goes to the winner.
         var gained = new Dictionary<Country, HashSet<State>>();
         bool Touches(Country x, State s)
         {
             gained.TryGetValue(x, out var got);
             foreach (var l in s.Locations)
                 foreach (var a in l.Neighbors)
                     if (a.Kind != AdjacencyKind.Sea && a.To.IsLand && a.To.State is { } ns && ns != s && (ns.Owner == x || got?.Contains(ns) == true))
                         return true;
             return false;
         }
         Country BeneficiaryOf(State s)
         {
             foreach (var g in goals)
                 if (D.GoalStates(g).Contains(s) && (g.Holder == winner || Touches(g.Holder, s))) return g.Holder;
             if (s.Cores.Contains(winner) || s.Claims.Contains(winner)) return winner;
             var claimant = winnerSide.FirstOrDefault(x => x != winner && s.Cores.Contains(x) && Touches(x, s))
                            ?? winnerSide.FirstOrDefault(x => x != winner && s.Claims.Contains(x) && Touches(x, s));
             return claimant ?? winner;
         }
         bool IsGoal(State s) => goals.Any(g => D.GoalStates(g).Contains(s));
         if (!subjugated)
             foreach (var (state, _) in held.OrderByDescending(x => IsGoal(x.State)).ThenByDescending(x => x.Held).ThenByDescending(x => x.State.Tax))
             {
                 if (offer.Terms.Any(t => t.State == state)) continue;
                 // states released as a people go with that people
                 if (offer.Terms.Any(t => t.Type == PeaceTermType.ReleaseCountry && t.State?.Owner == state.Owner && pop.Enabled && pop.MajorityCulture(state) == t.Culture)) continue;
                 // never take a country's last state through cession - that is annexation
                 if (state.Owner!.OwnedStates.Count - offer.Terms.Count(x => x.Type == PeaceTermType.TransferState && x.State?.Owner == state.Owner) <= 1) continue;
                 var to = BeneficiaryOf(state);
                 if (to == winner && !OursToTake(state)) continue;
                 if (Try(new PeaceTerm { Type = PeaceTermType.TransferState, State = state, Beneficiary = to }))
                 {
                     if (!gained.TryGetValue(to, out var set)) gained[to] = set = new HashSet<State>();
                     set.Add(state);
                 }
             }
         if (offer.Terms.Count == 0 && rel.OverlordOf(loser) == null && !D.HoldsHomelandOf(winner, loser))
             Try(new PeaceTerm { Type = PeaceTermType.Subjugate, Beneficiary = winner, SubjectType = Game.Data.SubjectTypes.Keys.First() });
         if (offer.Terms.Count == 0) Try(new PeaceTerm { Type = PeaceTermType.Humiliate, Beneficiary = winner });
         if (offer.Terms.Count == 0 && loser.Gold > 10)
             Try(new PeaceTerm { Type = PeaceTermType.Gold, Beneficiary = winner, Amount = Math.Min(loser.Gold, Math.Max(1, loser.LastIncome) * score / 4) });
         return offer;
     }
 }

 /// <summary>Installs the AI.</summary>
 public sealed class AiSetup : GameScript
 {
     // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
     public override int Priority => -1000;

    public override void OnLoad()
    {
        // Earth1930's custom game-state mutations are not yet safe for parallel country AI.
        // Keep this project deterministic and profileable until those shared collections are isolated.
        AiSystem.Parallelism = 1;
        Game.Ai = new GameAi(Game);
    }
}
