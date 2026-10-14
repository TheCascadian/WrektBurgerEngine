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

## 1. Pillars
One sentence each: what the player does, what they fight, what makes a session memorable.

## 2. Time and scale
- DECIDED: keep the current tick structure exactly: hourly tick drives daily, monthly and yearly ticks, in that order, gated by feature. GameClock speeds, the per-frame time budget and the 48-hour backlog cap are unchanged.
- DECIDED: ms per tick is minimized. Every optimization that does not harm the rest of the engine or the game is taken. There is no location-count or character-count budget; the rule is "make it performant". Work is profiled with the existing `P(name, action)` wrapper and `--perf`, and each change is judged by measured p99 and worst-day ms.
- Optimizations allowed without changing tick structure: skipping work when its feature is not installed, dirty-flag caches, per-scope early-outs, allocation-free hot paths, parallel AI with identical results to serial.
- OPEN: date model across 2,500 years (calendar, BCE dates, era boundaries).

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
Named RNG streams, event bus, registered definition types, save registry, test project. The tick structure is not changed. See REBUILD_REPORT.md section 6.

## 7. Release and legal
- Steam AI disclosure (DECIDED approach: describe it as accurately as possible). As reported by secondary sources (verify against Valve's Steamworks pages before submitting):
  - Pre-generated AI content (art, sound, code, text shipped in the game) must be disclosed, plus a promise that nothing is illegal or infringing and the game matches its marketing.
  - Live-generated AI content adds a guardrails description. This game does not generate content live, so that part should not apply; confirm that stays true.
  - The disclosure is shown publicly on the store page.
  - Reportedly, behind-the-scenes efficiency tools (such as code-completion assistants) are not reportable. That is unverified; do not rely on it. Disclose by category instead.
  - Non-disclosure risks removal, and Valve cannot verify use, so the accurate record is the developer's own.
- Provenance log: keep a file listing each content category (code, art/textures, models, UI icons, text/localization, design docs) with how it was made (AI-generated, AI-assisted then hand-edited, hand-made). Update it as content is added, so the Steam answers come from the log and not from memory.
- Content originality checklist: no Paradox names, no copied focus or tech trees, no content resembling Paradox files.
- Revenue: split between BKH and wrekt, and possibly one other developer. Terms are open and should be agreed before release.
- Publishing: the notes record BKH wanting to self-publish (developer and publisher) and keep control of the money. Not confirmed as final.

## 8. Risks
Scope (maintained span means mechanics get cut instead), collapse design, AI-content perception, performance on low-end hardware (hourly tick stays, so per-tick cost is the lever).
