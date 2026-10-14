# Decisions, Agency and Per-Playthrough Identity — Report and Plan

Status: Phase 1 (Memory) implemented in `src/Burger.Core/Memories`; all other phases are still plans.

## 1. Where we are

What the engine already gives us (and the plan builds on):

- Events with options, flags, `is_triggered_only`, on_actions, and effects like `give_in_to_movement` / `suppress_movement`.
- Political simulation: parties, parliament seats held by real characters, interest groups, subjects and puppets, opinions, truces, guarantees.
- `GameEvents` hub (SubjectMade, GovernmentChanged, StateTransferred...) so systems can react without calling each other.
- Characters with loyalty, health, traits, family.

What is missing, and why choices feel light:

| Problem | Cause |
|---|---|
| Choices are a menu of +/- numbers | Options end in `add_stability -3`; nothing remembers *who* chose *what*. |
| Outcomes are the same across runs | Consequences are immediate and generic; no state that is *yours*. |
| Reactions are unrelated to the action | The AI and population react to the numbers, not to what the player did or showed. |
| Little sense of world | Lore only exists as event text. There is no layer of names, places, dead, and rumours. |
| Betrayal is a stat change | `opinion -50` carries no personal weight. |

## 2. Design pillars

1. **Every action is a statement.** The world remembers it and answers in proportion: lesser, equal, or greater.
2. **Costs are chosen, not charged.** A weighty decision gives up something you value. No option is a free win.
3. **The consequence is yours.** What follows depends on your ruler, party, allies, and history, not a global table.
4. **Lore is a by-product of play.** Short fragments generated from real events, not authored prose.
5. **Say less.** One strong line and a number beat a paragraph.

## 3. Core systems

### 3.1 The Memory (what was done, and to whom)
A record of notable acts for every kind of subject (country, character, party, state, pop group): `betrayed_ally`, `broke_treaty`, `massacred_strikers`, `abandoned_province`, `honoured_guarantee`.
- Each entry: kind, target, date, weight, decay rate (some never fade).
- Anyone can query it in triggers: `has_memory = { kind = betrayed_ally with = FRA }`.
- Drives reputation: not one "trust" number but *what kind of ruler you are*, read by other countries, parties, generals.
- Engine fit: a `MemorySystem` in Core subscribing to `GameEvents`; saved with the game (versioned section).

### 3.2 Doctrine (the identity you build by choosing)
Three or four axes shaped only by decisions, not by picking a menu: e.g. *Order ↔ Mercy*, *Expansion ↔ Restraint*, *Loyalty to people ↔ to the state*.
- **Inspectable, never fully legible.** A Doctrine panel exists, but it shows qualitative bands and named tendencies ("Hardened", "Wavering"), not exact values or formulas. The player sees the direction and the rough strength, and the choices that moved it appear only as vague marks in the Memory. Thresholds, weights and the exact effect of each option stay hidden. Options hint at a shift ("This will make you harder.") without stating the size.
- Thresholds unlock and lock options for the rest of the game, and change how AI, parties, and generals treat you.
- Two players in the same start diverge because choice 12 only exists for the one who made choice 3.

### 3.3 Weighty decisions (Frostpunk grammar)
Authoring rules for a "Major Decision":
- **Two or three options, all with a price.** Prices are named and different in kind: lives, trust, a person, a region, a future option.
- **Irreversible and announced as such.** A visible "this cannot be undone" marker and a short pause (no auto-pick, no timer unless the situation is urgent).
- **Cost shown before, consequence shown later.** Show the immediate price; hide the downstream effect, then reveal it ("Three winters later...").
- **A decision that pays off after others.** Chains, not singles: choices set up the next dilemma.
- **A witness.** Each major decision names who saw it: a character whose loyalty or doctrine changes visibly.
- **Aftermath beat.** A one-line chronicle entry and a persistent mark (flag, character trait, map mark, dead-count) so it never vanishes.

Engine fit: extend event options with `cost = { }` (named, typed), `irreversible = yes`, `witness = <scope>`, `ledger = { ... }`, `doctrine = { axis = +1 }`, `delayed = { months = N event = ... }`. These are additions to existing event syntax, not a new system.

### 3.4 Reaction engine (every action has an answer)
A rule layer that maps *what the player did* to *what the world does*, with a magnitude.
- Each action emits a **Signal** (kind, actor, target, magnitude, visibility).
- Listeners (AI countries, parties, interest groups, generals, pops) translate signals to reactions by their own values.
- Reaction size is chosen from: *lesser* (quiet note, opinion), *equal* (mirror: sanction for sanction), *greater* (overreaction, escalation).
- Reaction type is decided by a roll weighted by the listener's traits and the Memory, so it is not predictable but is explainable afterwards ("Why did this happen?" panel).
- The first signals to build:
  - **Border buildup:** divisions near a border raise *Alarm* in the neighbour per month. Alarm leads to mobilisation, a demand, a guarantee request, an ultimatum, or a pre-emptive strike. The neighbour's own army level and its allies set the chance. The player gets a warning event before the dangerous step.
  - **Treaty broken / ally abandoned:** ledger entry plus emotional event chain (see 3.6).
  - **Repression / concession:** raises or lowers the matching interest group's trust and creates a martyr or a loyal minister.
  - **Economic pressure:** embargoes and aid create dependency; dependants are a lever and a risk.

### 3.5 The Interview (a decision disguised as small talk)
A repeatable *Encounter* type: a non-player character (journalist, envoy, general, bishop, a child) asks questions; answers are not labelled as policy.
- 3–5 questions, each with 2–3 answers; answers write **modifiers on the player's current ruler, party, or faction only** (scope-bound; lost when that scope changes).
- Examples of what answers change: ruler trait (*Candid*, *Evasive*), party cohesion, a general's loyalty, a doctrine axis, a hidden Memory entry, a public figure's opinion that surfaces later.
- Consequences are delayed and hidden: the published piece appears weeks later as a headline event referencing the answers.
- Triggers: a new ruler, a crisis, an election, a war's first anniversary.
- Engine fit: `encounter = { speaker, questions = { ... } }` data block, scoped modifiers (`modifier` on the character/party scope with `until = scope_change`), a `Headline` event scheduled via `Scheduled`.

### 3.6 Betrayal that hurts
For an alliance to be devastating, it must first be personal.
- **Invest first:** allies share small recurring moments (a joint exercise, a named envoy, a letter, a shared war memory). They are stored as Memory entries with a named person on each side.
- **Anchor to a face:** the allied ruler or envoy is a real Character with a portrait and traits. Betrayal removes or turns *that person*.
- **Show the turn from the inside:** a three-beat sequence: a wrong detail (a late message), the proof, the loss (units pulled from your flank, a border opened). One short line each.
- **Persistent scars:** the betrayed side keeps a permanent mark (a flag, a trait, a monument). Pops of that culture remember. Your later alliances are cooler.
- **Reverse works too:** *you* betray, and your own envoy, general, or party answers.

### 3.7 Lore without prose
World-building through fragments the simulation assembles:
- **Chronicle:** a running log of one-liners from real events: "1932. The Ruhr strike. 41 dead." Short, dated, numerical.
- **Epitaphs and names:** battles, cities, and units get names from the events that defined them ("Hill 41", "The Winter Pact"). Characters who die get a single line that cites their last act.
- **Rumours:** short, unreliable lines shown on the map or loading screen, true or false by design.
- **Fragments, not stories:** posters, telegrams, ledger lines, one sentence each. The player fills the gaps.
- **Let war do the talking.** War is already grimdark; the writing does not need to perform it. The register is *plain, exact and understated*: the simulation's own numbers and a single human detail do the work, and the line stops one beat before it explains itself.
  - **Rule 1: the number is real.** Every figure comes from the sim (casualties, grain, divisions, days). Never invent a total for effect.
  - **Rule 2: one concrete thing, not an adjective.** A boot, a ration, a telegram, a name on a list. No "terrible", "bloody", "doomed".
  - **Rule 3: the bureaucracy is the horror.** The state answers in forms, ledgers and communiqués. The gap between the number and the tone is the effect.
  - **Rule 4: cut the last clause.** If the line explains why it is sad, delete that part.
  - **Rule 5: tone follows scale.** Skirmishes get a dry report, battles a roll call. Catastrophe (a city lost, a famine, a collapse, a betrayal) is allowed a heavier voice: longer cadence, the single heaviest image, and a hard stop. It earns this by being rare. Rules 1 to 4 still apply to every tier, with no invented figures, no adjectives, no explaining the sadness.
  - Examples, by event kind:
    - Battle: "Hill 41, third day. 2,300 dead. The count is revised on the fifth."
    - Siege: "Day 118. The bakeries burn the last of the furniture."
    - Famine: "Bread: 140 grams. The clerks issue it in 140-gram portions."
    - Conscription: "Class of 1912 called. The village sends nine. It is told to send more."
    - Armistice: "11:00. The guns stop. Burial parties work until dark."
    - Treaty broken: "Filed under *Expired*."
    - Betrayal: "Your envoy's last cable arrived after the border did."
    - Epitaph: "Gen. Roth. Held the bridge at Aachen. Relieved the same week."
  - Lines pull from templates with slots (`{casualties}`, `{city}`, `{year}`, `{ruler}`, `{ration}`, `{unit}`) chosen by event kind and scale, so they stay specific and rarely repeat. Doctrine can colour word choice (a harder ruler's chronicle is flatter, a merciful one's names more dead), but not add decoration.

### 3.8 AI mode: historical by default, ahistorical optional
- **Historical AI is the default** and the baseline every system above is tuned against (Signals, Memory reactions and the border-alarm chances all use the historical profiles first).
- **Ahistorical AI is an opt-in setting** (Settings > AI behaviour). Nations follow their own interests, not their history. The tradeoff is stated in the UI: less realism, accuracy and plausibility, but more freedom to place the player in any role.
- Reactions stay proportional in both modes. Only the *motives* change: ahistorical nations weigh the Memory, Doctrine and the player's visible actions more, and their scripted history less.
- Scorecard and balance runs always use the historical mode.

## 4. Making playthroughs differ

- **Seeded personality of the world:** each run rolls *background tensions* (a hidden rival, a secret society, a plague year) so the same start diverges before the player acts.
- **Characters with agendas:** ministers and generals want things and act on them; the Memory and Doctrine decide who stays loyal.
- **Scarcity that forces tradeoffs:** a few shared resources (Trust, Fear, Grain) used by many decisions, so you cannot please everyone.
- **Unlockable futures:** end states depend on the Memory and Doctrine (a reputation for mercy opens a federation; for terror, an empire that cannot stop).
- **Per-run summary:** at the end, show *the five decisions that defined you* from the Memory.

## 5. Large-scale, accessible changes

- **Few big levers over many knobs:** group tweaks into stances ("Total mobilisation", "Open the archives") that change dozens of numbers at once, with a one-line summary.
- **Pre-flight preview:** every major action shows "likely reactions" (who will notice and how), without exact numbers.
- **"Why?" panel on every reaction:** the Signal that caused it, so the world feels fair.
- **Fewer, bigger events:** merge small events into rare, heavier ones.

## 6. Phased plan

| Phase | Deliverable | Notes |
|---|---|---|
| 1 | **Memory** + save/load, query triggers, Characters tab section "Remembered acts (character, party, people)" | Foundation for everything else. Fits `GameEvents`. |
| 2 | **Signal / Reaction engine**, with border buildup and treaty-break as first signals | Extend `DiplomacySystem` and the AI. Include the "Why?" panel. |
| 3 | **Major Decision syntax** (`cost`, `irreversible`, `witness`, `delayed`) and three showcase decision chains in Earth1930 | Extend the event parser and the event window UI (weight pause, cost-first layout). |
| 4 | **Doctrine axes** and gating | Needs Phase 3 hooks. |
| 5 | **Interview / Encounter** type with scoped modifiers and delayed headline | Needs Phase 3 and Phase 1. |
| 6 | **Betrayal arc**: ally memory moments, named envoy, three-beat sequence, scars | Needs Memory, Reaction engine, and Characters. |
| 7 | **Chronicle and fragment generator** (templates, slots, rumours) | Independent; can start in parallel with 1. |
| 8 | **Playthrough seeds and ending summary** | Last; tunes the rest. |

All phases stay data-driven and scenario-agnostic (Core), with Earth1930 supplying content only, matching the cohesion plan.

## 7. Risks

- **Too much text:** keep each event body ≤ 2 sentences; reward good fragments, cut the rest.
- **Reactions that feel random:** keep every reaction explainable and traceable to a Signal.
- **Content volume:** an authoring cost for weighty chains. Start with three excellent chains rather than thirty adequate ones.
- **Save compatibility:** the Memory, Doctrine, and Encounter state need versioned save sections (missing sections must load as empty).
- **AI exploitation:** the AI must also use Signals, or the player can game it by reading them.

## 8. Decisions taken

1. **Doctrine:** inspectible but obfuscated. Qualitative bands only; weights, thresholds and exact effects hidden (see 3.2).
2. **Major decisions:** real-time pause plus a choice window. The game pauses, the window shows cost-first options, and there is no auto-pick or timer unless the situation is declared urgent (see 3.3).
3. **Voice:** catastrophe events get a heavier voice; all other tiers keep the understated rules (see 3.7).
4. **Reactions apply AI-vs-AI too.** Signals, the Memory and Doctrine run for every country, so the world answers itself and the player cannot game the system by reading it. Implications: AI memories need saving too, the Reaction engine must stay cheap per month (cap listeners per signal, batch by region), and the player sees only the signals visible to them.
