# Timeline Game Design (skeleton)

Status: draft. Fill one paragraph per item before any new mechanic is coded. Nothing here is decided unless marked DECIDED.

## 0. Decisions so far
- DECIDED: the timeline game (500 BCE to 2020 CE horizon) replaces the 1930 game. Old scenarios live in the local-only `.ARCHIVE/`.
- DECIDED: no Paradox references in names, docs or content. Script syntax and folder layout are kept.
- OPEN: first playable era (ancient or medieval).
- OPEN: whether the full 500 BCE to 2020 span ships as one ruleset or as era packs.

## 1. Pillars
One sentence each: what the player does, what they fight, what makes a session memorable.

## 2. Time and scale
- Tick granularity (daily or monthly; hourly only if combat needs it).
- Date model across 2,500 years (calendar, BCE dates, era boundaries).
- Low-end hardware budget: target ms per tick, location count, character count.

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
- Goal: collapse is fun for the player holding the empire.
- Candidate: merged rebel-state module (civil war plus independence war) as the prototype.
- Questions: what does the player control during collapse (successor, fragment, court)? What is the recovery path? What signals warn the player?
- Prototype plan and success test.

## 5. Eras and content
Era packs as installable features. Per era: map, cultures and religions, tech list, event set. All content original; log AI-assisted content for Steam disclosure.

## 6. Kernel requirements derived from the design
Named RNG streams, event bus, registered definition types, data-driven tick rates, save registry, test project. See REBUILD_REPORT.md section 6.

## 7. Release and legal
- Steam AI disclosure: what to record and when.
- Content originality checklist.
- Publisher, revenue split and Dino's role: owner decisions, not engineering.

## 8. Risks
Scope, collapse design, AI-content perception, performance on low-end hardware.
