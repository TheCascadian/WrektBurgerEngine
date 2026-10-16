# Specialization theory: maximalist stress test

Question: does merging game and engine (dropping the open script layer) buy 1.5x or more? Earlier result on PTest (11 countries): no, ceiling 1.28x.
This test scales the same 500 BCE map to N countries (`tools/make_maxi.py --countries N`, every state owned, armies, fleets, restless politics, characters, pops) and looks for the case where specialization would pay.

Method: `burger validate <project> --days 365 --seed 1 --parallelism 1 --tick-stats`. Ceiling = 1 / (1 - interpreter-self share) from a dotnet-trace profile (script engine, script nodes, script context, modifier sums). It is an upper bound: builtins called from scripts still cost in a specialized engine.
FPS model: speed 3 (4 days a second, 1.6 ticks a frame), 14 ms sim budget a frame, frame = max(16.7 ms, sim ms). Simulation only, no rendering.

## Finding 1: the scaling problem is generic, not openness
At N=100 and N=200 the dominant cost was `PopulationSystem.ColonyCount`, recounted on every call (80446 of 80446) because its "never counted" sentinel was `-1` and dates before 1 CE are negative. Fixed with `int.MinValue`. Pure algorithm bug, a specialized engine would have paid it too.

| N countries | sim 365 d before | sim 365 d after | avg FPS (model) | max tick |
|---|---|---|---|---|
| 11 (PTest) | - | 2.9 ms/day | 59.6 | 320 ms |
| 25 | 3.9 s | 3.4 s | 59.1 | 637 ms |
| 50 | 5.8 s | 4.1 s | 58.7 | 1046 ms |
| 100 | 13.3 s | 5.6 s | 57.7 | 1438 ms |
| 200 | 43.6 s | 9.5 s | 55.8 | 2198 ms |

PTest 3650 days: 5.57 s to 4.25 s (1.31x), digest `5ee3dfe46e5bb31f` unchanged.

## Finding 2: standard maximalist play, openness ceiling
Maxi_100 after the fix: interpreter-self 8.7 percent of tick time, ceiling 1.095x. Maxi_200 before the fix: 4 percent. Far below 1.5x.
Verdict for standard maximalist gameplay: THEORY FAILS. Specialization buys at most about 1.1x. The wins are algorithmic (N-squared pairwise opinion, `ModifierValue` cache, monthly spike ticks).

## Finding 3: 60 FPS
No N holds a 60 FPS average at speed 3, not even PTest (59.6). The mean is fine up to about N=100 (15 ms a day); the blocker is spike ticks (monthly and yearly work in one atomic tick, 0.3 s at N=11, 1.4 s at N=100, 2.2 s at N=200). Maximalist N under the 57.7 FPS model: about 100 countries. Reaching 60 needs the monthly work spread over days (a recommendation, generic and compatible with the open engine).

## Finding 4: worst case where the theory holds
Case name: SCRIPT-DOMINATED MONTHLY PULSE.
Conditions: 100 countries, 6000 interpreted country events (each a three-branch OR trigger with `any_neighbor_country` and a three-modifier `mean_time_to_happen`), evaluated every month for every country (about 7.3 million trigger evaluations a year). Project `Maxi_100S` (6000 events, generated in the experiment, not committed).
Result: sim 19.0 s against 5.4 s without the mod; interpreter-self 66.8 percent; ceiling 3.0x. Inside the 2.5-5x band, so the theory holds for THAT case only.
Scale check: 1200 events costs +1.7 s (ceiling about 1.3x). The 2.5x line needs on the order of 5000 interpreted events at 100 countries, far beyond any shipped content (PTest has two event files).
It is also not a reason to merge: the same win comes from indexing and caching event triggers (skip events whose cheap precondition fails) inside the open engine, or from the existing C# script path for mods that need it.

## Call
- Standard play: theory fails (1.1x ceiling, under the 1.5x bar). Keep the open engine.
- Worst case holds (3.0x) only for a script-dominated mod. Fix that case with a trigger index or compiled triggers, not by removing the openness layer; merging would break modding and future extensibility for a gain nobody hits.
- Do next, all generic: split monthly and yearly ticks across days, reduce `PoliticsSystem.ModifierValue` and pairwise `Opinion` cost, add precondition indexing for events.
