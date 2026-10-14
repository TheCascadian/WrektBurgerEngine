# Pop - Politics - Puppets cohesion plan

Goal: the three systems should behave as one pipeline, and the glue should live in the engine (reusable by any scenario), not in
copy-pasted Earth1930 backends.

## 1. Findings (from the code)

**A. One-way, ad hoc links.** Population feeds politics (pop moods, votes, parliament seats, stability via `stability_target`) and
feeds diplomacy (opinion, cores, liberty desire). Diplomacy feeds politics through exactly one call, `FollowOverlord`.
Nothing in `Politics/*.cs` knows about subjects, and nothing in `Population/*.cs` does either.

**B. Puppet government is reset monthly.** `MonthlySubjects` calls `FollowOverlord` every month, so a puppet's election,
coup or revolution is reverted unless it is at war with its overlord. Movements and interest groups in a puppet fight this reset.

**C. Scenario backends are forks, not overrides.** Earth1930 `backend/*.cs` are full copies of the base rule classes.
Differing lines against the base: AI 402, Politics 368, Commodities 244, AiProfiles 209, Supply 149, Population 116, Diplomacy 83.
Every bug fix has to be made twice (this already bit us: CoupChance/CivilWarChance/StabilitySources/AiGivesIn had to be kept in sync).

**D. Repeated relationship idioms** (each written out by hand):
- "overlord or subject of each other": 9 copies (now 1, see section 4).
- `Politics.Enabled` 46x, `Population.Enabled` 26x, `Politics.Of(c).Ruling` 32x, `Relations.OverlordOf` 26x,
  `TopOverlord` 12x, `HoldsHomelandOf` 9x, "culture of country" lookups with null/empty handling in at least 9 places.
- `SubjectsOf(x).Sum(strength * 0.5)` in 5 places (AI profiles, demilitarization), the "subject counts half" weight hard-coded each time.

**E. Feature gating is per call site.** `Enabled` checks are scattered, so adding a feature (a new subject effect) means
finding every guard.

**F. Inactive paths.** Earth1930 has no `common/pop_types`, so the class/promotion paths are untested there.

## 2. Target architecture

A thin **Realm** layer between the three systems. Nothing here is Earth1930 specific.

| Piece | Responsibility | Lives in |
|---|---|---|
| `Relations` queries | pure graph questions: `AreOverlordAndSubject`, `TopOverlord`, `BlocOf(c)` (country + subjects), `IsIndependent` | `Diplomacy/Relations.cs` |
| `CountryProfile` accessor | one place for "ruling ideology, primary culture, religion, home states" with feature gates folded in (returns null/empty, never throws) | `Model/` or `Common/` |
| `SubjectEffects` rule | the single virtual rule class that says what a subject type means for each system (see below) | `Diplomacy/DiplomacyRules.cs` (new virtuals) |
| Domain events | `GovernmentChanged`, `StateTransferred`, `SubjectMade`, `SubjectReleased` raised by their owner, consumed by others | `Game` event hub |

**Domain events replace direct calls.** `MakeSubject` would raise `SubjectMade`; Politics subscribes and decides what a puppet's
government should be. Today diplomacy reaches into politics. After the change each system owns its own reaction.

## 3. Work packages (in order)

1. **Query helpers** (small, safe). `AreOverlordAndSubject` done. Next: `Relations.BlocOf`, `TopOverlord` moved from
   `DiplomacySystem` to `Relations`, `Country.IsSubject` extension, `SubjectWeight(type)` define (`subject_strength_share`, default 0.5)
   replacing the five hard-coded `* 0.5`.
2. **`CountryProfile`**. Wrap `Politics.Of(c).Ruling`, `Population.Of(c).Culture` with the enable checks. Replace the ~40 call sites
   in diplomacy, AI and rules. No behaviour change; tests prove it.
3. **Politics learns about subjects** (behaviour change; needs tuning):
   - new rule `PoliticsRules.SubjectGovernmentPolicy(game, subject, overlord, type)` returning `Imposed | Preferred | Free`.
     Client State = Imposed, Tributary = Preferred (nudge only), Dominion/Colony = Free. Today everything is Imposed.
   - `FollowOverlord` becomes the `Imposed` branch only, and is skipped while the subject has an active civil war or a movement
     above the strike threshold.
   - `MovementGrievanceSources` gets "Foreign rule" (subject, scaled by liberty desire) and "Puppet regime" (imposed ideology differs
     from the pops' leaning).
   - Interest groups: `MovementInterestGroups` adds an `independence` group for subjects with high liberty desire; `GroupSatisfaction`
     for it is `1 - LibertyDesire/100`.
4. **Population learns about subjects**:
   - `OutputFactors` option `subject_manpower_share` so overlords get manpower, not only gold (type field `manpower_share`, default 0).
   - `IndependenceProgress` multiplier for separatist pops inside a subject (they ask the overlord, not the subject, for release);
     liberty desire gains a term from the share of unaccepted cultures.
   - `HoldsHomelandOf` reads the same culture accessor as `IsAccepted`, so "same people" is defined once.
5. **Collapse the forks.** For each Earth1930 backend file, turn the copy into a subclass that overrides only the methods that
   differ (diff against base first). Priority by saved lines: Commodities (244), AI (402), Politics (368), AiProfiles (209).
   A test per file: rules output on a sample game is identical before and after.
6. **Event hub**. Introduce `GameEvents` with the four events above; port `FollowOverlord`, `Annex`, `RefreshName` and the pop cache
   invalidation to subscribers. Removes the remaining direct cross-system calls.
7. **Tests** (extend `PoliticalDepthTests.cs`): puppet revolt not reverted, Free subject keeps its election result, Imposed
   subject follows overlord, subject grievance includes foreign rule, tribute and manpower share, release on high liberty,
   save/load round trip of the new fields. Add a `SampleGame`-style fixture with `pop_types` so the class path is exercised.

## 4. Done

- **Package 1.** `Relations.AreOverlordAndSubject`, `IsSubject`, `TopOverlord`, `BlocOf`; `DiplomacySystem.SubjectStrength` (define
  `subject_strength_share`, 0.5) replaces the five hard-coded `* 0.5`.
- **Package 2.** `Game.RulingOf`, `Game.CultureOf`, `Game.SameRuling` replace the gated lookups in diplomacy rules, AI profiles and the AI backend.
  (A full `CountryProfile` type was not needed; three accessors cover every repeated site.)
- **Package 3.** Subject types have `government = imposed | preferred | free`. New rules `PoliticsRules.SubjectGovernment` (a puppet in civil
  war or with an organised, mobilised movement is no longer forced; define `subject_revolt_organization`, 50) and `SubjectGrievanceSources`
  ("Foreign rule" from liberty desire, define `grievance_foreign` 20; "Puppet regime", define `grievance_puppet` 8). Subjects with liberty
  desire 20+ also bring an `independence` interest group. Earth1930: tributary = preferred, dominion = free, the rest imposed.
- **Package 5.** Correction to section 1C: the line counts there included whole systems. Comparing method bodies, nearly every
  override in the Earth1930 backends was identical to the engine default (Commodities 5 of 5, Supply 7 of 7, Diplomacy 30 of 30,
  Population 42 of 42, Politics 13 of 20, AiProfiles 15 of 15, Warfare 4 of 5, Vision 4 of 4). Those 120 identical overrides were
  deleted; the forks are now subclasses that override only what differs (Politics 7 methods, Warfare 1).
- **Package 7.** Section 10 of `PoliticalDepthTests` covers free/imposed/preferred, puppet-regime grievance, the independence group.
- **Package 4.** `SubjectType.ManpowerShare` (`manpower_share`, default 0; Earth1930: client state 0.10, colony 0.20) sends part of a subject's
  monthly manpower gain to the overlord. Liberty desire gains `(1 - accepted share) * liberty_unaccepted` (10) from people the overlord does
  not accept (`PopulationSystem.AcceptedShareFor`). Independence movements inside a subject run at `subject_independence_factor` (0.8).
  `Game.SamePeople(a, b)` is the one definition of "same people" and `HoldsHomelandOf` uses it.
- **Package 6.** `Game.Hub` (`GameEvents`): `SubjectMade`, `SubjectMonthly`, `SubjectReleased`, `GovernmentChanged`, `StateTransferred`.
  Diplomacy raises them (`Release(subject)` is the one way to free a subject, also used by the `release_subject` effect); politics
  subscribes `FollowOverlord` (moved out of diplomacy), `Game` refreshes country names, population drops its majority cache.
- **AI.cs.** The Earth1930 AI was a stale copy of `AiSystem` (older, shared-RNG signatures), so it is now an empty `GameAi` subclass plus
  the setup that sets `AiSystem.Parallelism = 1`. The AI now draws from the per-country random streams like the engine default.
- **Tests.** Section 11 of `PoliticalDepthTests`: events fire, manpower share, "same people". Still missing: a `pop_types` fixture for the class paths.

## 5. Risks

- Package 3 changes how puppets behave; run `validate` and compare counts before and after, accepting noise (the RNG sequence moves).
- Collapsing forks can silently change behaviour if the base drifted from the fork; the per-file identical-output test guards it.
- Event hub ordering: Politics and Diplomacy both react to `SubjectMade`; define the order explicitly (diplomacy first).

## 6. Reusability checklist

- No tag, ideology key or culture name hard-coded in `src/`.
- Every number is a `defines` entry with a default.
- Every behaviour is a virtual on a rules class, so a scenario overrides one method, not a file.
- Every new field is an optional save block so old saves load.
