# Timeline Game Design (skeleton)

Status: draft. Fill one paragraph per item before any new mechanic is coded. Nothing here is decided unless marked DECIDED.

## 0. Decisions so far
- DECIDED: the timeline game (500 BCE to 2020 CE horizon) replaces the 1930 game. Old scenarios live in the local-only `.ARCHIVE/`.
- DECIDED: no Paradox references in names, docs or content. Script syntax and folder layout are kept.
- DECIDED: the player starts in 500 BCE (ancient era first).
- DECIDED: one ruleset spans the whole timeline. Era Packs are overrides that enable specific mechanics at particular times; they are not separate games.
- DECIDED: the 500 BCE to 2020 CE span is maintained regardless of how realistic it is. Scope pressure is handled by cutting mechanics, not by cutting the span.
- DECIDED: Dino is likely not involved.
- DECIDED: revenue will be split between burgerkinghotline (BKH) and wrekt, and possibly one other developer. OPEN: the split terms (the notes say they should be agreed beforehand, based on effort and costs) and who the possible third developer is.
- DECIDED: American English keys are used outright. British English ships as a localization language that overrides only the words that actually differ. No alias table.
- DECIDED: SampleGame stays as the template and test fixture.
- DECIDED: the tick structure is kept exactly (section 2).
- DECIDED: the combat models are renamed to engine-exclusive ids: BH4, BE4, BI1. Each maps in the team's minds to its old counterpart but no Paradox game or product is named in code, data or docs.
- DECIDED: git history is NOT rewritten. Earth1930 stays in the earlier commits.
- DECIDED: all other business decisions are deferred (see section 7).
- DECIDED: history-following is strict by default. Guided is an optional setting. History-following can be disabled only by an explicit, very clearly labeled setting chosen per playthrough, stored in that playthrough's save, so it never carries over to other playthroughs.
- DECIDED: every country in every shipped scenario, and explicitly in the starting scenario, is modeled properly with as much content as is feasible within the project goal.
- DECIDED: real names, real people, real locations.
- DECIDED: the full timeline plus a sandbox extension beyond it.
- DECIDED: all recommendations in section 9 are approved.
- DECIDED: the GPL-3.0 historical basemap data is not used; its content is recreated safely (section 9.5).
- DECIDED: tools, validation, self-tests and focus lint are scenario agnostic and take a single scenario per run.
- DECIDED: the game is very unlikely ever to have live in-game generative AI content. Any generated content is created before release and shipped; nothing is generated mid-game or after release.

## 1. Pillars
One sentence each: what the player does, what they fight, what makes a session memorable.

## 2. Time and scale
- DECIDED: keep the current tick structure exactly: hourly tick drives daily, monthly and yearly ticks, in that order, gated by feature. GameClock speeds, the per-frame time budget and the 48-hour backlog cap are unchanged.
- DECIDED: ms per tick is minimized. Every optimization that does not harm the rest of the engine or the game is taken. There is no location-count or character-count budget; the rule is "make it performant". Work is profiled with the existing `P(name, action)` wrapper and `--perf`, and each change is judged by measured p99 and worst-day ms.
- Optimizations allowed without changing tick structure: skipping work when its feature is not installed, dirty-flag caches, per-scope early-outs, allocation-free hot paths, parallel AI with identical results to serial.
- DECIDED requirement: the date is consistent and easy to understand, and era-appropriate for each unique era.
- RECOMMENDED (not yet approved): one internal day counter that never changes, and a per-era display calendar chosen by the Era Pack. See section 9.

## 3. Mechanics (build order)
For each: purpose, data it owns, events it emits and listens to, what it must never call directly, test that proves it.
1. Characters and dynasties
2. Settlements and population (rural to city upgrade: gold plus population)
3. Realm layer: subjects, vassals, tribute, courts
4. Centralization dial
   - Centralized: faster research and economy, weaker subject and culture management.
   - Decentralized: tribute income, risk of falling behind technologically.
5. Economy and research
6. Military (supports the above; does not lead)
7. AI (scorecard first, per the historical AI plan)

## 4. Collapse (hardest open problem)
Source: `docs/recs/EMPIRE_COLLAPSE_MODEL.md` and its flowchart. Collapse changes the player's challenge, not just their strength.

Loop: inputs (war, succession, inequality, repression) -> pressure (trust falls in regions, elites, army) -> warning (who is angry, why, what is at risk) -> action (reform, bargain, share power, crack down) -> player chooses a cost -> concede (state survives changed) or refuse/fail (breakaway blocs shaped by past choices) -> conflict (fight, negotiate, seek allies) -> state holds (rebuild with new laws, debts, grudges) or breaks (play on as rump, breakaway, claimant) -> new goal (reunite, survive, reform, retaliate) -> crisis leaves marks (loyalty, legitimacy, diplomacy, memory) -> back into pressure.

- DECIDED: the player controls one political seat, not the whole former empire. Before the split, the imperial government. After, the faction or successor they led into the crisis (usually the rump; sometimes a breakaway, claimant or coalition). The transfer is stated before it happens ("If this faction wins, you will continue as X").
- DECIDED: other fragments are simulated rival actors with leaders, goals and negotiable interests. The player influences them by diplomacy, concessions, espionage or war, not direct control.
- DECIDED: recovery is a new campaign objective, in five steps: stabilize, legitimize, rebuild, resolve the breakup, live with the settlement. Several endings count as success; reunification is one path.
- DECIDED: warnings are visible stages (grievance, organization, ultimatum, revolt) that show who, what they want, why now and how close they are. No hidden collapse roll.
- Design test: after every warning, the player has at least one response that changes who leaves, what they demand, or what the successor becomes.
- Prototype: the merged rebel-state module (civil war plus independence war), built on the movements stages already in Politics, and on Memory for "crisis leaves marks".
- OPEN: which causes are in the first prototype, and the numeric model behind "trust" per region, elite and army.

## 5. Eras and content
Era Packs are overrides on the single ruleset, built on the feature packaging system (`feature.txt`, requires, conflicts, install/uninstall). A pack can switch a mechanic on or off, or override defines, from a given date. Per era: map, cultures and religions, tech list, event set. All content original; log AI-assisted content for Steam disclosure.

## 6. Kernel requirements derived from the design
Named RNG streams, event bus, registered definition types, save registry, test project. The tick structure is not changed. See .ARCHIVE/docs/REBUILD_REPORT.md section 6.

## 7. Release and legal
- Business decisions other than the ones above are DEFERRED, including revenue split terms, the possible third developer, and Steam submission details.
- Steam AI disclosure (DECIDED approach: describe it as accurately as possible). As reported by secondary sources (verify against Valve's Steamworks pages before submitting):
  - Pre-generated AI content (art, sound, code, text shipped in the game) must be disclosed, plus a promise that nothing is illegal or infringing and the game matches its marketing.
  - Live-generated AI content adds a guardrails description. Live in-game generation is not planned (see section 0), so that part should not apply.
  - The disclosure is shown publicly on the store page.
  - Reportedly, behind-the-scenes efficiency tools (such as code-completion assistants) are not reportable. That is unverified; do not rely on it. Disclose by category instead.
  - Non-disclosure risks removal, and Valve cannot verify use, so the accurate record is the developer's own.
- Provenance log: keep a file listing each content category (code, art/textures, models, UI icons, text/localization, design docs) with how it was made (AI-generated, AI-assisted then hand-edited, hand-made). Update it as content is added, so the Steam answers come from the log and not from memory.
- Content originality checklist: no Paradox names, no copied focus or tech trees, no content resembling Paradox files.
- Revenue: split between BKH and wrekt, and possibly one other developer. Terms are open and should be agreed before release.
- Publishing: DECIDED, BKH will self-publish.

## 8. Risks
Scope (maintained span means mechanics get cut instead), collapse design, AI-content perception, performance on low-end hardware (hourly tick stays, so per-tick cost is the lever).

## 9. Recommendations awaiting approval
Nothing here is decided. Each is a recommendation from Claude.

### 9.1 Date
- Internal: one signed day counter from a fixed epoch, never changed by era, so saves, ticks and scripts stay consistent.
- Display: each Era Pack names a calendar format (for example year-number style and month names). The player sees one style per era, not a mix.

### 9.2 Collapse prototype causes (first four)
1. Succession crisis (uncertain or contested heir).
2. Unpaid or defeated army.
3. Regional neglect (tax burden without services, distance from the capital).
4. Rival power centers (an elite or governor with their own forces).
Later: cultural or religious exclusion, repression as a cause (in the prototype repression is a player action, not a cause).

### 9.3 Numeric model
Three kinds of bloc: Region, Elite, Army. Each has trust 0-100, reusing the existing movement fields (grievance, organization, progress, heat). All numbers are defines.
- Monthly: trust += drivers - pull toward the bloc's baseline.
- Region drivers: tax burden minus services, distance times centralization, culture or religion mismatch, recent war losses, remembered grievances.
- Elite drivers: share of offices versus their weight, heir legitimacy, their own force strength.
- Army drivers: months of unpaid wages, victories and defeats, commander loyalty, share of the budget.
- Stages: grievance (trust below 50), organization (below 35 and organization rising), ultimatum (below 20), revolt (ultimatum expires unanswered).
- Capacity to act = bloc strength x organization. A weak bloc can be angry without being dangerous.
- Era Packs change the driver weights (for example army pay matters more once standing armies exist).

### 9.4 Era Packs (candidate list, needs review)
| Era | Rough span | Mechanics to enable |
|---|---|---|
| Antiquity | 500 BCE-200 CE | city-states, tribute, levies, settlement upgrades, early courts |
| Late Antiquity | 200-700 | migrations, world-religion spread, collapse of large empires |
| Medieval | 700-1300 | feudal vassals, centralization dial at full weight |
| Early Modern | 1300-1650 | gunpowder, printing, banking and trade |
| Industrial | 1650-1900 | industry, standing armies, parliaments, nationalism |
| Modern | 1900-2020 | mechanized war, mass ideology, modern state |

### 9.5 500 BCE map (approved; GPL-3.0 confirmed by the owner)
- Coastlines, rivers and elevation are mostly reusable from the existing generator inputs (Natural Earth, public domain, plus AWS elevation tiles), with manual fixes where the coast has moved.
- The political layer (who held what in 500 BCE) needs a source. aourednik/historical-basemaps has such maps but is reported to be GPL-3.0 (unverified); using it in a shipped game may impose GPL terms, so do not use it until licensing is confirmed. Alternative: draw the political layer ourselves from public-domain or citable references.
- Tools should be rewritten from the 1930 generator to a timeline-neutral generator.

## 10. Open items
- Pillars (section 1) and per-mechanic paragraphs (section 3) are still empty.
- Approval or changes for section 9.
- Whether the "I DO NOT KNOW" items are to be decided by Claude's recommendation: which 1930-era engine values to rework (base year 1930, tech start-lag, flag era thresholds 1789/1850/1920), and whether the planning week and kernel work overlap.
- When the Earth1930-specific self-tests and the focus-lint check in `Program.cs` get replaced.
- The four decisions in HISTORICAL_AI_PLAN (railroad dial, scope of 1930, real names and people, time horizon): all four were written for the 1930 game.
- Deferred business: split terms, third developer, Steam wording check, content provenance.

## 11. Recommendations awaiting approval (second round)

### 11.1 Safe recreation of the GPL-3.0 map data
Not legal advice; a lawyer should review before release.
- Do not copy, convert, trace, resample or "clean up" the GPL files, and do not keep them in the repo or on build machines used for the shipped data. A derived work could carry GPL terms into the game.
- Facts (who held what, where a city stood) are not copyrightable. The risk is in the specific geometry and the selection and arrangement in that dataset.
- Build from independently licensed sources, each logged with license and URL before use: Natural Earth (public domain) for coastlines and rivers, AWS elevation tiles for relief, and primary or scholarly sources for who held what. Check each license directly; do not assume.
- Draw the political boundaries ourselves from those sources, as new polygons on our own grid (the game's province map), not by following another dataset's lines.
- Keep a source ledger: for each country and region in the scenario, the references used and the date. This is the evidence that the data is independent.
- Compare the finished map against the GPL data only as an audit, to catch accidental near-copies, never as an input.

### 11.2 The 1930-era engine values
Move each to a define or era-pack data, with the 1930 value removed from code:
- Base year 1930: becomes the scenario start date.
- Tech start-lag 60/25/10: becomes a per-scenario define, with a sensible default per era.
- Flag era thresholds 1789, 1850, 1920: replace with era boundaries read from the Era Pack list. The seed rule stays unchanged so existing flag output does not change.
- Flag families and emblems with year gates (for example hammer_sickle 1917) go to data, as the report already says.

### 11.3 Planning week and kernel work
Run them at the same time. The kernel steps in the rebuild report (script language, kernel, data loading) do not depend on the game design, so coding them in the planning week costs no design time. The first mechanic is not coded until the section 3 paragraphs are written.

### 11.4 Strict-by-default history
- A per-playthrough setting: strict (default), guided, or off. Off requires an explicit, clearly labeled choice at new-game time and is shown in the save and the UI.
- The setting lives in the save's meta block, not in user settings, so it cannot leak across playthroughs.
