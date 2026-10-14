# BurgerEngine Rebuild Report: Stay the Same / Keep / Change / Remove

This is the single, merged report for the ground-up rebuild. It combines the first rebuild document and the verified addendum, then adds the findings from reading the remaining system groups.

Rules for reading it:
- **SAME** means do not change it. Other things depend on it, and old content must keep working.
- **KEEP** means carry the idea over. The code may be rewritten or ported.
- **CHANGE** means keep the purpose and redo the design.
- **REMOVE** means leave it behind.
- **VERIFIED** means the file was read line by line. **UNREAD** means it was not. Section 9 lists exactly which is which. At the end of this pass nothing is unread except the generated script reference.
- Data keys that are spelled the British way (`organisation`, `labour`, `centre`, `colour`, `neighbour`, `defence`, `armoured`) are data contracts. They are quoted as they exist. The rebuild may add American aliases, but must never silently rename them.

---

## READ THIS FIRST (plain version)

Sections 0 to 10 below are the full technical record. They are long and written for someone who is going to code the rebuild. This part is the same story in plain words.

**What the engine is.** You make a game out of text files and pictures. The engine reads them, runs the world, and draws it. The drawing part is kept separate from the world part. That split is GOOD. NEVER break it.

**What stays EXACTLY the same (do not touch):**
- The text file format and how scripts read.
- The folder layout of a game.
- The map picture format.
- Save files.
- The C# mod hooks.
- The model conventions (1.80 m human, joint names, `.glb` files).
- Every file name and key that old games and mods already use.
If any of these change, old games and mods stop working.

**What we KEEP (good ideas):**
- The world never draws anything.
- Everything is data first, C# only when needed.
- The event bus, so systems react to each other without calling each other.
- Resources, memory, script values, particles, flags, relations: these are the cleanest parts. Copy how they are built.
- The editor ideas (it edits text without wrecking your comments).

**What we CHANGE (same job, better build):**
1. The giant `Game` object that knows about everything. Split it into a small core plus plug-in modules.
2. Systems that reach into each other (politics, population, diplomacy, navy, air). Use events instead.
3. Hardcoded game rules inside C# (building names, stat names, ideology names, magic numbers). Move them to data files.
4. The model system. Four layers: skeleton, parts, assembly, style. Tanks and soldiers stop being special cases.
5. Random numbers. Every system gets its own seeded stream, so the same seed ALWAYS gives the same game.
6. Copied scenario code. One source of truth. Scenarios only override what differs.
7. The HUD. Menus and panels come from data, and the HUD only reads the world, it never changes it directly.
8. Tests. Make a real test project instead of hiding tests inside the `validate` command.
9. Debug switches. One logging system with named channels instead of a pile of environment variables.

**What we REMOVE:**
- The old giant constructor and the `if feature is on` checks everywhere.
- Copied backend code and the script that made the copies.
- Old compatibility code for old saves and old mods (this is a fresh start).
- Old test projects, big image files in docs, extra flowchart versions.

**Spelling.** American English in everything new. Old data keys with British spelling (like `organisation`) stay readable through ONE alias table. Nothing gets silently renamed.

**What to do first (order):**
1. Script language and patcher.
2. Core kernel (clock, random streams, events, logging, saves).
3. Data loading.
4. Map and world.
5. Script engine and validator.
6. Modules: military, diplomacy, economy, characters, AI.
7. Saves, console, command line.
8. Renderer and HUD.
9. Model system.
10. Mod layer and export.
11. Editor.
12. Everything else (politics, population, production, navy, air, tech).

**How we know it worked.** Old games load with no new errors. Same seed gives the same game. Saves reload identical. Editing a file keeps your comments. Old mods and old `.glb` models still work. Flags come out byte-for-byte the same.

**The one decision left.** American aliases for British data keys. I set the default to ON, with the British keys still accepted. Say so if you want it different.

---

## 0. The idea in one paragraph

The engine is a Paradox-style grand-strategy engine. All game content is text files in a game folder. A pure simulation library (`Burger.Core`) never draws anything. A renderer (`Burger.Game`), an editor (`Burger.Studio`) and a command line (`Burger.Tools`) sit on top of it. The architecture is good. What is wrong is mostly inside it: one giant `Game` object that knows every system, systems that call each other directly, hardcoded special cases (tanks, soldiers, building keys, stat keys, ideology names), very large files, and copied code in scenario backends. The rebuild keeps the architecture and the file formats, and rewrites the insides in smaller, cleaner pieces.

The same shape shows up in nearly every simulation system: a virtual `*Rules` class (swappable through `Game.Rules.X`), a sealed `*System` class that is gated by a feature, and a registered `SaveSection`. That shape is worth keeping. What changes is how the pieces find each other.

---

## 1. STAY EXACTLY THE SAME (the contracts)

If these change, every mod, map and script stops working and the project stops being this engine.

### 1.1 The script file format
- The Paradox-style text syntax: `key = value`, `key = { ... }`, bare list items `{ GER FRA }`, anonymous blocks, `#` comments, quoted strings, `\n` / `\t` / `\"` escapes.
- Operators: `=`, `==`, `<`, `>`, `<=`, `>=`, `!=`, `?=`. `?=` behaves like `=` ("if the scope exists").
- Typed blocks: `rgb { }`, `hsv { }`, `hsv360 { }`, `list { }`.
- File-local constants (`@name = 10`, used as `@name`). Constants are not part of the data.
- Percent values (`50%` is 0.5) and the booleans yes/no/true/false/1/0/on/off.
- Keys are case-insensitive everywhere.
- The parser never throws. It logs `file:line` and keeps going.
- Every node remembers its file, line and character span.
- Writer rules: tabs for indent, lists of 12 or fewer bare items on one line, UTF-8 without a BOM.

### 1.2 Comment-preserving editing
- `ScriptPatch` edits one statement in place and leaves every other character untouched. This includes the `#index` positional path for repeated keys.
- It must never make a broken file worse. If the file is broken, it appends.
- The editor and the feature installer depend on this.

### 1.3 The game folder layout
```
game/game.txt           name, default tag, icon, speeds
game/features.txt       which features are installed
common/*.txt            definitions (defines, terrain, units, buildings, modifiers, politics, ...)
history/*               starting state (units, fleets, air, diplomacy, politics)
events/                 events
localisation/           text
map/                    provinces.png, locations.txt, default.map, states, adjacencies, heightmap, terrain, trees, rivers
gfx/                    art; gfx/models/*.glb for models
scripts/*.cs            moddable C#, compiled when the game starts
backend/*.cs            locked C# (next to game/), compiled into the exported EXE
saves/  logs/error.log
```
- Files in a folder load sorted, case-insensitive. A later definition of the same key wins and logs a warning.
- Single-file and folder forms both work for each content type (`ParseFileOrFolder`).
- Folder and file names confirmed by reading the systems (all are contracts): `common/focuses`, `common/decisions`, `common/resources`, `common/script_values`, `common/particles`, `common/ai_profiles`, `common/on_actions`, `history/fleets`, `history/air`, `map/air_regions`.
- `localisation` keeps its spelling. Add an American alias if wanted.

### 1.4 The map input format
- `provinces.png`: one unique color per location. Alpha 0 means no location.
- `locations.txt`: `HEX = name`, or either block form (`FF0000 = { name = ... terrain = ... }` and `berlin = { color = FF0000 }`).
- Location types: land, sea (ocean), lake, impassable (wasteland).
- Other images may have different sizes. They are resampled to the province map size.
- `terrain.png` matches colors to `common/terrain.txt`. An unknown color falls back to the nearest terrain, with a warning.
- Rivers: any non-background pixel counts, and Paradox pink counts as background.
- Unnamed colors are auto-created as `loc_<hex>`, with a warning.
- Limits: 255 terrain types, 65,000 locations.
- A coastal location is a land location next to non-lake water.

### 1.5 The rules for stats and defines
- Unknown numeric keys on a definition become free-form stats or modifiers. A mod can add a stat without touching engine code.
- `stat_aggregation` (sum, average, min, max) decides how a division template combines unit stats.
- Every tunable lives in `common/defines.txt`, has a default in code, and has help text in the `Known` table. The editor shows that help.
- The `Definition` node stays attached to every loaded definition.

### 1.6 Scope and script semantics
- Scopes: `ROOT`, `THIS`, `PREV`, `FROM`, `player`, `scope:name`, `c:/country:/tag:`, `s:/state:`, `l:/location:/p:/province:`, `char:/character:`, `generic:`, `war:`, bare 3-letter tags, registered links (`ruler`, `capital`, `owner`), and dot paths (`ROOT.capital.owner`).
- Value forms: `var:name`, `global_var:name`, `modifier:key`, and scoped values (`ROOT.gold`).
- Triggers: `and or not nor nand always custom_tooltip limit`. Iterator prefixes: `any_ all_ every_ random_ ordered_`. Iterator options: `limit count percent max order_by weight`.
- Effects: `if / else_if / else`, `random`, `random_list` (entries written as `50 = { }`, with `trigger` and `modifier` blocks), `hidden_effect`.
- Weight blocks: `base`, `factor`, `add`, `days`, `months`, `years`, `modifier = { factor add <triggers> }`.
- `any_` with `count` or `percent` is a count or percent test.
- Scripts register triggers, effects, values, iterators and links by name, so C# mods can extend the language.
- The validator checks the same rules statically and reports `file:line`.
- Unknown triggers and effects log once per location. Effect exceptions are caught and logged. They never crash the game.
- **Script value formulas (CK3 style):** `value, add, subtract, multiply, divide, modulo, min, max, round, floor, ceiling, abs, if / else_if / else`, `every_*` iterators, and scope switches. Recursion depth limit 40. Quirk to keep: inside a formula `min` acts as a floor and `max` acts as a ceiling (they clamp). Document it, do not "fix" it.
- **Engine formulas that scripts may override (exact names, contracts):** `country_monthly_income`, `country_monthly_expenses`, `country_monthly_manpower`, `max_diplomatic_relations`, `character_yearly_death_chance`, `character_yearly_birth_chance`, `division_combat_power`, `division_org_recovery`, `location_war_value`, `base_opinion`.
- **Auto-registered names for resources:** each custom resource `<key>` creates a value `<key>` and effects `add_<key>` and `set_<key>`.

### 1.7 The C# mod contract
- A `GameScript` base class with the hooks `OnLoad`, `OnGameStart`, `OnSaveLoaded`, the four ticks, and the event hooks (`OnEventFired`, `OnWarDeclared`, `OnPeace`, `OnBattleEnded`, `OnLocationControllerChanged`, `OnStateOwnerChanged`, `OnCharacterDied`, `OnDiplomaticAction`, `OnDivisionDestroyed`, `OnCountryEliminated`). Lower priority runs first.
- Scripts are compiled at load with Roslyn. Compile errors are logged with `file:line`.
- Every script call goes through a guard. A throwing script logs once and never takes the game down.
- `backend/` is the locked layer and `scripts/` is the moddable layer. Both use the same API. Exported games compile `backend/` into the EXE and still compile `scripts/` at runtime.
- Swappable rule objects: `Game.Rules.*` (Diplomacy, Economy, Characters, Warfare, Vision, Production, Supply, Navy, Air, Support, Politics, Population, Tech, Commodities, Focus, AiProfile), plus `Game.Designer.Rules`.
- A replaceable `Game.Ai`, and a combat-model registry (`Game.Combat.Register`). Built-in model ids: `hoi4`, `eu4`, `imperator`, chosen with `combat_model` in `common/defines.txt`.
- Combat-factor sources: `Warfare.CombatFactorSources` is a list of functions that multiply a division's combat power each hour. Navy and Air plug into it. Keep the contract.
- Named script values can replace engine formulas (see 1.6).

### 1.8 Feature packaging
- `features.txt` lists installed features. The engine runs only the systems of installed features.
- A feature package has `feature.txt` (name, category, description, requires, conflicts, group, defines, generate, order) plus the files it adds, mirroring the project folder.
- Install copies files and never overwrites existing ones. Uninstall deletes unchanged files and moves changed ones to `removed_features/<id>/`, so no work is lost.
- Custom feature ids are allowed. `has_feature` works on them.
- A game with no `features.txt` gets every built-in feature (old-project behavior; see section 5 for the new-project change).
- Presets (a list of features and defines) and bases (starting maps) stay as concepts.

### 1.9 Time
- Hourly tick drives daily, monthly and yearly ticks. Order is hourly, daily, monthly, yearly, gated by feature.
- `GameClock`: speeds from `game_speeds`, a per-frame time budget (14 ms, 30 ms at top speed), and a backlog cap of 48 hours. The game never spirals.
- Game date and hour as the simulation clock. `GameDate` in script form.

### 1.10 Determinism
- Seeded main RNG, and one deterministic RNG stream per country for the AI (`CountryRng`, wrapped by `AiRng`).
- A parallel AI tick must give identical results to a serial one.
- Anything random in the simulation must use these streams, never an unseeded source.
- (Section 4.2 lists the places that break this today and must be fixed, not copied.)

### 1.11 Saves
- Saves are readable text in the same script format. They store only what changes during play. Definitions are re-read from the game folder on load.
- A `meta` block (version, date, hour, player, next ids, globals, fired-once events, cooldowns), then countries, states, locations (only the "interesting" ones), characters, armies, divisions, wars, generic scopes, scope hierarchy, relations, modifiers, scheduled events.
- Script and system blocks go through a save-section registry (`SaveSection { Name, Write, Read, Reset }`, optional `Early`), so a mod can own a block.
- Ids (`next_ids`) are saved so references stay valid.

### 1.12 Model conventions
- glTF space: meters, +Y up, facing +Z, feet at the origin. A human is 1.80 m tall.
- Joint names: `root, hips, spine, chest, neck, head`, and `.L` / `.R` of `shoulder, upper_arm, forearm, hand, fingers, thumb, thigh, shin, foot, toe`.
- Maximum 64 joints per rig, 4 joints per vertex.
- `gfx/models/name.glb` is a model. `name.anything.glb` is clips only. A clip's speed is a custom property in m/s. Binary glTF 2.0, no textures.
- Keys are case-insensitive. A unit's look is deterministic. Core never draws. A version counter marks a changed model set. One draw call per figure.

### 1.13 Tools, output formats and external contracts
- The error log with `file:line` (`logs/error.log`, F2 in game).
- The in-game console with registered commands.
- `burger validate`, `burger generate-locations`, and the figure contact sheets.
- Export to a single EXE with an editable `game/` folder.
- Scorecard file format (windows plus probability bands) and the output line `SCORE|name|value`. External tooling reads both.
- Theme names: color names, texture names and layout metric names (`top_bar_height`, `left_menu_width`, `left_panel_width`, `right_panel_width`), with the 20 to 1000 validation.

### 1.14 Rules whose numbers define the game feel
These are behaviors, not just code. Port them with the same results.
- **Elections:** D'Hondt seat allocation with a threshold, and the coalition hook `Rules.WillGovernWith`.
- **Opinion:** base + relation modifiers + ideology + religion + colonial. Base opinion includes +25 allied, +10 overlord/subject, +15 guarantor, -100 at war, -10 truce, and the `same_culture_opinion` define.
- **War:** war score, peace terms (TransferState, Subjugate, Annex, Gold) and truces.
- **Events:** the chance of a mean-time-to-happen (MTTH) event firing per day is `1 - 0.5^(30/mtth)`. `FormatText` placeholders `[ROOT]` and `[scope:x]`, and `.GetName` accepted for Paradox compatibility.
- **Event and on_action keys:** `on_monthly_pulse`, `on_decision_taken`, `on_focus_completed`, plus `country_event`, `character_event` and the old `event` with `type = character`.
- **Focus trees:** `prerequisite` means any of, `requires` means all of, plus `mutually_exclusive`.
- **Memory:** exponential decay per year, with `has_memory` and `add_memory` as script names.
- **Combat factor math:** Division combat power is multiplied by the product of all combat-factor sources, with a floor of 0.05.
- **Models:** see 1.12.

---

## 2. KEEP (good ideas to carry over)

### 2.1 Architecture
- **Simulation with no drawing** (Core) separate from rendering, editor and CLI. This is the most important rule.
- **Everything as data**, with C# as the escape hatch.
- **Event bus** (`GameEvents`). Systems react to each other without calling each other. The memory system's `Recorded` event is a good example, and it is already prepared for a future "reaction engine".
- **Modifier providers.** Each system exposes `ModifierValue(scope, key)` with a key filter and dirty-flag caches. Politics and population plug in this way.
- **Profiling wrapper** (`P(name, action)`): per-call stats, p99 and worst days. Keep from day one.
- **Scope hierarchy** and generic scopes (typed scopes made from data).
- **Per-node lookup caching** in the script engine.
- **Map change counters** (`MapVersion`, `VisibilityVersion`). The renderer redraws only when they change. The AI border cache uses `MapVersion` the same way.
- **Notifications and pending proposals** as a core concept.
- **The "historical bias" setting** for the AI. It scales focus AI weight and profile history weight.
- **Names follow state.** A country's name, color and flag follow its government and subject status (`RefreshName`).
- **Combat-factor source list** (Navy and Air multiply land combat through it).
- **Region data with automatic fill** (air regions cluster leftover states by `air_region_states` / `air_region_km`).

### 2.2 The best-designed data-driven systems (copy these patterns)
- **Resources** (`Resources.cs`): name, start, min, max, monthly script value, top-bar flag, color, scope (country or character). Auto-registers a value and `add_` / `set_` effects. This is the model for every future custom number.
- **Memory** (`MemorySystem.cs`): subject-keyed (Type, Key) records of acts with weight and yearly decay. 19 built-in kinds, `DefineKind` for scripts, a cap of 200 per actor, forget below 0.05.
- **Script values** (`ScriptValues.cs`): see 1.6.
- **Particles** (`ParticleSystem.cs`): data-driven definitions, a request queue capped at 256 drained by the renderer, usable from clip events, the console and the `spawn_particles` effect. Not tied to figures.
- **AI profiles** (`AiProfiles.cs`): traits, rivals, friends, want, timing, awaken triggers, ideology hostility, research and a `parent` for inheritance.
- **Decisions** (`Decisions.cs`): potential, allow, effect, ai_will_do, cost, cooldown, repeatable. Taken flag `decision_taken:`.
- **Focuses** (`Focuses.cs`): trees with lint (date checks, war effects), a `focus_speed` modifier, AI weight scaled by historical bias.
- **Statistics** (`Statistics.cs`): per-country monthly metrics, C# mods can `Add` metrics, old samples are thinned by doubling the interval past 480 samples, civil-war rebels count toward their origin country.
- **Division designer** (`Designer.cs`): custom templates owned by a country, stored in a save section, with a rules class (`MaxBattalions`, `CannotSaveReason`, `CanUse`). Updating a template keeps each division's share of strength and organization.
- **Relations** (`Relations.cs`): one small, clean store for opinions, alliances, access, guarantees, subjects, truces and pacts, with pair-keying by country index. It has no game rules in it. Keep this shape.
- **Vision** (`Vision.cs`): fog of war as a swappable rules class with a clear breadth-first spread. Easy to keep.
- **Procedural flags** (`FlagGenerator`, `FlagIdentity`): pure managed code that returns RGBA bytes. The same identity always gives the same flag. A design grammar builds 8 candidates, each scored by an evaluator, and the best survives. Predecessors pass palette, family and motifs on. It is testable without a window.
- **Combat model interface** (`ICombatModel`): the engine owns battle creation, retreat, destruction and ending. A model only changes `Strength` and `Org`.

### 2.3 Content systems worth keeping as installable features
Military, the three combat models, production, supply (range and hubs), division designer, navy, air, battle plans, lend-lease, politics (parties, factions, parliament, laws, cabinet, unrest, civil wars), population (pops, culture, religion, social classes, cores and claims, colonization, independence), buildings, fog of war, diplomacy (with war goals and demilitarization), characters and interactions, economy, commodities, technology, focus tree, decisions, events and on_actions, memory, particles, statistics, AI and AI profiles, procedural flags.

### 2.4 Editor ideas
Viewport, Inspector, Content Browser, Code, Console, Project Settings, Outliner. Comment-preserving edits. Panel patching with `override`, `extend`, `disable`. Feature install screen, with presets and bases. Specs and templates for new content.

### 2.5 HUD ideas
- The immediate-mode `Gui` (`Block`, `Hover`, `Clicked`, `Scroll`, `Tooltip`). It reads top to bottom. Keep it for the game HUD.
- The 9-slice skin system with fallbacks to drawn colors when an image is missing.
- Custom panels with stable ids and override / extend / disable operations.
- Left-menu entries gated by `Has(FeatureSet.X)`.
- Custom map modes (`MapModeDef`).

### 2.6 Process ideas
- Docs split: game docs describe what exists, plans describe intent, audits describe measurements.
- The Earth 1930 build script as proof that a real map can be generated.
- Multi-seed headless runs with the scorecard recorder.
- README-driven project layout and sample projects as test cases.

---

## 3. CHANGE (keep the purpose, redo the design)

### 3.1 The god object
`Game` builds every system in one constructor, wires them by hand, and holds the rules, profiling, clock and features.
- Split it into a small **kernel** (clock, RNG service, event bus, profiler, script engine, save registry, logger, path service) and **modules** that register themselves.
- A module declares: feature id, requirements, definition loaders, ticks, script registrations, save section, rule slot, menu entry and UI hooks.
- Features become real plug-ins, not `if (Features.Has(...))` checks.
- Tick order becomes explicit data (module priority).
- Example of a direct dependency to remove: `VisionRules.Compute` reads `FeatureSet.Navy` and `game.Navy.Fleets` itself. The Navy module should add its own vision source instead.

### 3.2 Loading
`DataLoader` repeats one pattern per definition type in a fixed order.
- Replace it with **registered definition types**. Each type states its folder, parse function and dependencies. The loader sorts them.
- Keep the current load order as the default, because content depends on it. Keep the `file:line` messages.
- Remove the hardcoded known-key lists. A definition type states which keys are stats.
- The same registry feeds the editor forms (section 3.13) and the docs (section 3.12).

### 3.3 Rules objects
`GameRules` is a long list of concrete types, so adding a system means editing it. Make it a **registry of named rule slots**. Each module declares a slot and a default implementation. Scripts replace by name.

### 3.4 Coupling between systems (Politics, Population, Diplomacy, Economy, Production, Commodities, Tech, Navy, Air, AI)
Verified by reading: these systems call each other directly and know each other's details.
- Politics reaches into Population, Diplomacy, Resources, AI profiles and Characters.
- Diplomacy reaches into Population, Politics, AI profiles, Production, Navy and Warfare (`DiplomacyRules` reads naval strength, military strength, regime preferences).
- Navy depends on Production, Tech, Economy, AI profiles and Warfare. It also changes `war.BattleScore` directly.
- Air writes `State.Damaged` directly.
- `AiSystem.BuildDemands` is about 190 lines of knowledge about Population, Politics and Diplomacy.
- Warfare reaches into Navy, Diplomacy, Mods and the event hub; Support reaches into Warfare and Diplomacy.
- Renderer, labels and overlay read Core state directly (no view-model layer).
- The project's own COHESION_PLAN names one example: diplomacy reaches into politics through one call (`FollowOverlord`), run every month, which reverts any puppet's own election, coup or revolution.

Fix:
- A thin **Realm layer**: relationship queries (`BlocOf`, `TopOverlord`, `AreOverlordAndSubject`), a `CountryProfile`, and `SubjectEffects`.
- **Domain events** (`SubjectMade`, `GovernmentChanged`, `StateTransferred`, `WarDeclared`...) so each system owns its reaction.
- A subject type declares its effects in data (`subject_strength_share`, whether the subject keeps its own government).

### 3.5 Hardcoded rule content in C# (verified)
Move these to data or defines. The C# keeps only the machinery.

| Where | Hardcoded today | Move to |
|---|---|---|
| Production, Navy, Air, Supply | building keys `civilian_factory`, `military_factory`, `dockyard`, `infrastructure`, `railway`, `supply_hub`, `naval_base` | Building tags in data (`tag = factory`) |
| Warfare, Navy, Air | stat keys `soft_attack`, `hard_attack`, `defense`, `breakthrough`, `hardness`, `armor`, `piercing`, `combat_width`, `speed`, `heavy_attack`, `light_attack`, `transport`, `air_attack`, `air_defense`, `agility`, `ground_attack`, `strategic_attack` | A stat registry with roles (`attack_soft`, `attack_hard`, ...) so a mod can rename or add stats |
| Navy | categories `capital`, `screen`; 0.35 hit chance; 0.85 screen preference; 7-day battle cap; 0.15 convoy losses | Defines and ship-class tags |
| Air | bombing target list; 0.02 reinforce; 10-plane minimum | Defines and building tags |
| Characters, Tech, Production | base year 1930; tech start-lag 60/25/10; "6 children" fertility cut 0.3; death at age 100; stat range 2-8 clamped to 0-20; 1000-unit stock heuristic | Defines |
| Focuses | progress counted as +30 per month | Define |
| AI profiles | ideology strings `fascist`, `communist`, `democratic` mapped to archetypes; ideological hostility numbers (for example fascist vs communist -20); action keys in switch statements (`declare_war`, `offer_alliance`, `guarantee_independence`, `offer_non_aggression_pact`, `break_alliance`, `demand_state`, `demand_subjugation`, `fabricate_claim`, `improve_relations`, `insult`) | Ideology properties in data; an action registry where each diplomatic action declares its AI behavior |
| Diplomacy rules | 0.75 join chance, 0.6 and 0.5 ally weights, 365/730 day thresholds | Defines |
| AI system | subject type `SubjectTypes.Keys.First()` | A named default subject type in defines |
| Politics | `democratic` key, ideology-stance substring checks, pop-type substring matches, `stability` resource key, movement interest groups | An ideology property, a pop-type tag, a named stability resource in defines |
| Population | auto cultures `<tag>_culture` and `natives` made in C# | Real cultures in data files |
| Combat | the `0.4 + 0.6 * strength` curve, `0.1` / `0.4` blocked-hit splits, `x10` scale in HOI4 model; 3-day phases and 0.01 morale drain in EU4 model | Defines per combat model |
| Flags | traditions (`nordic`, `slavic`, `islamic`, ...) and political families (`socialist`, `communist`, ...) registered in a static constructor | Data files under `common/flags`, one per tradition and family |
| Statistics | metric list in the constructor; Summable and Cumulative key sets | Data definitions |
| Feature packages | starting-install steps (`StartingArmies`, `StartingFleets`) with ship counts | Data files shipped by the feature |
| Warfare, Support | literal thresholds in combat, retreat, occupation and volunteer logic; the 0.75/0.4 supply triangle | Defines per combat model and supply model |
| Flags | bit layout of the flag LUT, size and complexity thresholds | Constants in one flag-format spec |
| HUD, map, camera | label thresholds, zoom limits, map-mode constants, HUD alert thresholds, fixed font codepoints | A UI theme and defaults file |
| Figures | body, tank and soldier size constants | Assembly data (3.6) |
| Figures | `IsLightArmour`, key `light_tank`, uniforms and ancestries | Data (see 3.6) |

Also fix:
- `TechSystem.MonthlyTick` has a nonsense expression: `Math.Min(1.0, 1.0 + (1.0 - n) * 0.0)`. It always equals 1. Decide what was intended, test it, and remove it.
- `IndependenceProgress` in Population multiplies about 9 factors. Split into named, individually tunable terms, each with a define.
- `DecisionS.Take` contains Demilitarization-specific code and a debug hook. Move it to an `on_decision_taken` handler in data.

### 3.6 The model system (Figures)
The biggest redesign. Replace "Figures" with four layers:
1. **Skeleton**: joints, sockets, clip library.
2. **Part**: a mesh fragment bound to a slot, with named color channels.
3. **Assembly**: skeleton + slot map + gait table + scale + formation + LOD.
4. **Style**: palette plus slot swaps, chosen by context and priority.

Specific fixes:
- The unit-to-model link moves into **data** (a `model` field on unit types and templates).
- Tank and soldier special cases (`LightTank.Drive/Aim`, `TankMapScale`) become gait tables and clip names in the Assembly.
- Uniforms and ancestries move from C# records into data files.
- Colors become palette channels instead of being baked into vertices.
- Remove the `base@ancestry` string-key trick. A resolver takes a context and returns a cached result keyed by hash and global version.
- Procedural parts and `.glb` parts both sit behind one `PartSource` interface.
- Keep everything in 1.12.
- Later: textures and PBR, and one figure per soldier instead of one per stack.

### 3.7 Determinism fixes (verified)
These systems use the shared `Game.Rng`, which is unsafe when the AI runs in parallel and makes results depend on call order:
- Characters, Events (AI choice, MTTH), Decisions (AI take), Focuses, Navy rules, Air rules.
- AI `Diplomacy()` and the `Peace` flow.
- All three combat models (`game.Rng` in `Hoi4CombatModel`, `Eu4CombatModel`, `ImperatorCombatModel`).

- Politics: movement incidents, crackdown success, civil-war and coup chance, election noise, `AiLaws`, AI movement choice, `Candidates`, `GovernmentCollapsed`, `Splinter`.
- Support: the AI volunteer draw (`< 0.4`) uses the shared RNG.
- Population: garrison defection on independence, AI gold buildings (`0.5`), AI production builds (`0.25` and the building pick), `AiProductionBuild`.

Fix: one **RNG service with named streams** (`ai:<tag>`, `combat`, `events`, `characters`, `navy`, `air`), each seeded from the main seed. No system touches a global RNG. `PrefersRegimeChange` already shows the other good pattern: a hash of war id and country id gives a deterministic yes/no with no RNG at all. Use it where a draw is not really needed.

### 3.8 Backend copies
Scenario backends (Earth1930) are full forks. Measured by COHESION_PLAN as differing lines: AI 402, Politics 368, Commodities 244, AI profiles 209, Supply 149, Population 116, Diplomacy 83. Bug fixes had to be made twice and some drifted. Make **one source of truth** (the feature package). Scenarios subclass and override only what differs. Tune with defines first, code second. Generated copies are never committed twice (`tools/assets/backend_gen.py` goes away).

### 3.9 HUD
- Layout, left-menu items, hotkeys, map modes and `MenuIcon` are written in C#. The project's own `UI_MODDING_AUDIT.md` confirms the built-in HUD was never migrated to data.
- `Hud` is a partial class over 22 files (about 7.3k lines). `Hud.Politics.cs` alone is 1,422 lines.
- Define the menu, panels and map modes as data (one definition type per panel with sections). The C# HUD reads the registry.
- Each module registers its own menu entry (id, label, icon, hotkey, visibility trigger, badge count).
- Key bindings go in a rebindable table.
- Give the HUD a read-only **view model** per system, so panels cannot mutate state. Today it calls `G.Production`, `G.Warfare`, `G.Navy` directly and uses string checks such as `StatIcon("gold")` and `"stability"`.
- Icon and label come from the resource or panel definition.
- Keep gameplay-bound code (selection, orders, drag) in C#.

### 3.10 Tools CLI
- `Program.cs` is 2,185 lines. `Validate` is about 1,550 lines and mixes the headless run, scorecard recorder, profiler, war diagnostics and every self-test (patching, flags, figures, scope hierarchy, parties, factions, interface, memory, save/load).
- One command per file. Commands today: new, validate, selftest, run, export, generate-locations, docs, figure, list, features, presets.
- `validate` keeps load plus headless run only.
- Self-tests move into a real test project (section 3.14).

### 3.11 Game launcher and Studio
- The game finds its folder from the exe path or working directory (`FindGameRoot`) and writes `crash.log` next to the exe. One **path service** (game root, user data, logs).
- The game `Program.cs` has about 25 CLI flags including six `--figure-*` render modes. Move the figure modes to a separate tool.
- Studio's `Engine.cs` shells out to `dotnet burger.dll` and the game exe, and finds the engine by looking for `src/Burger.Game/Burger.Game.csproj`. Replace with an in-process tool API. It breaks in an installed build.
- Studio fonts use hardcoded Windows and DejaVu paths. Use a font service with a fallback list.
- Studio state files sit next to the exe. Use per-user config folders.
- Studio `Specs.cs` is a hand-written form table that duplicates the loader's key knowledge. Generate forms from registered definition types, with help text from the definitions.

### 3.12 Documentation
`MODDING.md` is 84 KB in one file. Split by feature. Generate the trigger, effect, value and define reference from the registrations so it cannot drift.

### 3.13 Debug switches
About 17 raw environment variables, read ad hoc with `Environment.GetEnvironmentVariable`: `BURGER_` plus `PURSUIT_LOG`, `FOCUS_LOG`, `AMB_LOG`, `WARSCORE`, `WAR`, `WARCAUSE`, `PEACE`, `LABEL_DEBUG`, `GOV`, `FACT`, `DIPLO`, `DECISION_LOG`, `ALLY`, `AI_LOG`, `AI_DEBUG`, `AI_CHECK` and others. Replace with one **logging facility with named channels** (`ai`, `war`, `peace`, `gov`, `diplo`, `focus`, `ally`...) turned on from the command line or a config file. Example of why: `Relations.SetAlliance` builds two `StackTrace` objects inline whenever `BURGER_ALLY_LOG` is set, inside the data store itself.

### 3.14 Testing
Tests are mostly commands (`PoliticalDepthTests`, `Scorecard`, `Studio.SelfTest`). Make a proper test project: parser round-trip, patch tests (comments survive), determinism tests (serial AI equals parallel AI, and a repeat-run test for the combat models), save/load round-trip, validator tests on sample games, and one test per cache (border, reachability, path scratch, node lookup), each with a named owner and invalidation rule.

### 3.15 Big files
Split along natural lines with no behavior change: CLI `Program.cs`, `Hud.Politics.cs` (1,422), `WarfareSystem.cs` (1,106: movement, fronts, battles, occupation), `DiplomacySystem.WarGoals.cs` (922), `PoliticsSystem.cs` (857), `PopulationSystem.cs` (729), `Gui.cs` (953), `MapRenderer.cs` (797), `App.cs` (762), `CountryLabels.cs` (746). Many are partial classes, which hides size more than it removes it.

### 3.16 Language and naming
Mixed British and American spelling in docs, identifiers and messages. Approximate counts in the repo: `organisation` 395, `labour` 311, `centre` 284, `colour` 239, `neighbour` 102, `armoured` 589, `defence` 14.
- Examples that exist as data contracts: `armoured_division`, `max_organisation`, `faction_eng_labour_left`, `liberalisation_drift`, `neighbour_drift`. Code names with the same issue: `NeighbourStates`, `DecolonisationPressure`, `Localisation`, `DecolonisationPressure`.
- New code, docs and messages use American English.
- For a new project: use American keys (`organization`, `labor`, `center`, `color`, `neighbor`, `defense`, `armored`).
- To keep old content loading: **one alias table in the loader**, not scattered fallbacks.
- Pick one spelling per public name before the first release. Renaming later breaks mods.
- A malformed doc comment exists in Population (two `<summary>` tags before class `SeparationPart`, so the class doc was lost). Fix while porting.

---

## 4. REMOVE (leave behind)

### 4.1 Code
- The monolithic `Game` constructor and its hand-written wiring.
- `if (Features.Has(...))` checks spread through many files.
- Hardcoded unit-key checks in the renderer (`light_tank`, `IsLightArmour`).
- Country-specific data in C# (uniforms, ancestry mixes, per-country soldier outfits).
- The `base@ancestry` variant-key trick and baked per-vertex colors.
- Duplicate backend code copies and the script that makes them.
- Legacy overloads kept for old mods, such as the old-signature `ManageMilitary(Country)` and the AI overloads that draw from the shared RNG. Drop them (this is a rebuild).
- Legacy save paths: memory save version 1 (loaded as country-only), and the self-test that strips `x_memory` to fake an old save. A new project has no old saves.
- Legacy loader fallbacks, such as theme keys `panel_texture`, `button_texture`, `top_bar_texture` that duplicate `textures = { ... }`.
- Auto-generated cultures in C#.
- The `figure-*` options in the game `Program.cs`, and the duplicated `--figure-gear` line in the help text.
- The raw `BURGER_*` environment reads.
- Swallowing `catch` around metric reads in Statistics. Log instead.
- Inline debug code inside data stores and rules (`Relations.SetAlliance`, `Decisions.Take`).
- The "no `features.txt` means every feature" fallback in new projects (keep only for importing old projects).

### 4.2 Repo content
- Old projects (`projects/Test`, `Test2`, `GameTest`, `GifPortraitDemo`) as shipped content. Keep only the sample and one real map as fixtures.
- Large binaries from docs (the 7 MB `contact_sheet.png`, `ARMY_ICONS.png`). Generate on demand.
- Flowchart HTML versions v1 to v4. Keep only the current one.
- The untested Linux export path, unless it is verified. Say so in the docs instead.

---

## 5. Group-by-group findings (what reading the code showed)

Each group lists what the code does, then what it means for the rebuild.

### 5.1 Politics (PoliticsSystem, PoliticsRules: read in full)
- Elections use D'Hondt with a threshold and `Rules.WillGovernWith`.
- `PoliticsRules` is virtual and heavily tuned by defines.
- Registers dynamic values `popularity_<ideology>` and `seats_<ideology>`.
- Calls into Population, Diplomacy, Resources, AI profiles and Characters.
- Hardcodes: `democratic`, ideology-stance substring checks, pop-type substring matches, `stability`.
- **Rebuild:** SAME for D'Hondt and the coalition hook. CHANGE the cross-calls (3.4) and hardcoded keys (3.5).

**Politics partials (read in full this pass; only the tail of Pops was seen via saved output, now also read):**
- **Unrest** (Victoria 3 style movements): each has ideology, demand (Legalize or Power), grievance, organization, progress, heat, crackdowns, concessions and interest groups. Group keys `freedom`, `security`, `army`, `independence`, their texts, the incident switch and the stage names are hardcoded, English-only strings. Tuned by many defines, but literals remain (0.5/0.25 AI choice, 0.25 satisfaction bump, 70 ultimatum progress, heat/progress increments). `StartCivilWar` is one very large function: it picks the rebel region, builds a new country (tag from `FreeTag()`, mixed colors, copied laws and popularity), splits gold, manpower and stockpile, moves divisions, declares a forced war and calls Diplomacy (`TransferState`, `DeclareWar`, `EndWar`, `Annex`) and Warfare (`StopDivision`, `AssignToArmy`, `Teleport`, `DestroyDivision`) directly. The `start_civil_war` effect checks `FeatureSet.Military` and `FeatureSet.Diplomacy` itself and falls back to a coup. `AddStability` looks up the hardcoded resource key `stability`. Spelling: `StateNeighbours`, "legalise", "organised", "Sympathisers" in code and strings.
- **Parties:** parties are `GenericScope` of kind `party` (children `party_faction`, `party_branch`). Seats come from D'Hondt (`election_threshold` 0.05, `election_noise` 0.03 drawn from the shared RNG). Two parallel models live side by side: ideology-keyed legacy fields (`Ruling`, `Government`, `Popularity`, `Seats`, `PartyNames`, `set_party_name`, `DistributeLegacySeats`) and party-keyed fields (`PartyPopularity`, `PartySeats`, `RulingOrganization`, `GovernmentOrganizations`). **Rebuild: party-first, ideology is only an attribute of a party.** Drop the legacy half.
- **Factions:** `weight`, `likes`, `dislikes`, `post`, `hardline`, `tolerance`, and dynamic `loyalty`, `grievance`, `placated_day`. Influence is weight x (0.5 + loyalty/100). Liked or disliked laws surcharge `EnactCost`. Literals: 0.15 loyalty rate, +-10/+-5 cabinet nudges, 0.4/1.4 collapse factors, weight clamp 0.25 to 8. Membership is stored as character flags `org_<party>`, `faction_<faction>` and legacy `party_<ideology>`: state kept in flag-name prefixes. **Rebuild: real membership fields.** `Splinter` and `GovernmentCollapsed` use the shared RNG.
- **Laws and cabinet:** `politics_resource` define names the political-power resource, and an absent resource means free (a good pattern to copy; compare the hardcoded `stability`). `Enact` re-derives seats, bans, government and organizations in one long function and fires `on_law_changed` plus `Hub.RaisePartyBanned`. `Candidates` and `AiLaws` use the shared RNG. The `_aiCalled` flag exists only so an old backend AI still runs politics: legacy, remove.
- **Parliament:** seat-level (party, constituency, pop group, holder). **Keep `SeatRng`**: `Random(Hash(seed, tag, seat.Id, epoch, draws, salt))` with FNV-1a is the model deterministic pattern. Candidate stats 2-8, age 25-70, 25% female, pool 24 are mostly defines; stat range and trait count are literals. Holders become real `Character`s. Rename `Seat_Seat`.
- **Pops (mood layer):** `PopMood` per (state, pop key `culture|religion|type`). Reaction names `Approve/Comply/Disapprove/Protest/Radicalise` are hardcoded strings compared by string in `Summarize`; thresholds -10/25/45/55 are literals. Objective names (`autonomy`, `religious_rights`, `labour_rights`, `property`, `stability`) are hardcoded. `PopSummary` (backing, underground, grievance, stronghold per ideology) is recounted monthly and not saved; moods are saved. Reads Population directly (`HasPops`, `Of`, `HomeStates`, `TotalOf`).
- **Script registration:** `PoliticsSystem.Script.cs` registers triggers, effects and values even when the feature is off (so scripts still validate), plus console commands `politics`, `popularity`, `ideology`, `election`, `law`, `civilwar`. Several effects are ideology-keyed legacy forms (`set_ruling_party`, `add_popularity`, `set_party_name`) beside party-keyed ones (`set_named_party_name`, `add_party_support`). Per-ideology values `popularity_<id>` and `seats_<id>` are added on load.
- **Models:** `IdeologyDef` (Elections, Extremist, ColorBlend, WeakDemocracyDrift, PeacePuppets, Coalition, Opinion, Modifier, Government), `GovernmentDef`, `LawGroup`/`LawDef`, `CabinetPosition`, `CountryPolitics`, `Movement`, `InterestGroup`, `CivilWar`, `Seat`, `CountryParliament`, `PoolCandidate`. `IdeologyDef.PeacePuppets` and `Extremist` show ideology flags already carry rule behavior: good, data-driven.
- **Rebuild consequence:** civil war, revolt and coup should be a domain-event module (Realm layer) that listens to events and issues commands, not inline calls into Warfare and Diplomacy.

### 5.2 Population (PopulationSystem, PopulationRules: read in full)
- Monthly: grow, assimilate, convert, promote, migrate, then `ApplyOutput`.
- Rules are virtual and nearly every number is a define.
- Rule groups: growth, capacity, assimilation, conversion, migration, cultural ties, acceptance (`culture_group_accepted`), output factors, cores, colonization (max colonies, cost, range, upkeep, native attack, settlers, base output, AI score), independence (separatist culture and region, minimum share, suppression, decolonization pressure, colonial opinion, garrison defection, independence divisions, war outcome, AI response), starting pops, stability penalty.
- `SeparatistRegion` is the largest and most complex algorithm.
- Auto cultures `<tag>_culture` and `natives` are created in code.
- **Rebuild:** KEEP the rule split. CHANGE the 9-factor `IndependenceProgress`. REMOVE auto cultures.


**Population partials (read in full this pass):**
- **Independence:** `IndependenceMovement` (culture, progress, suppressed) and `IndependenceWar`. Rules are virtual and good (`SeparatistCulture`, `SeparatistRegion`, `IndependenceProgress`, `IndependenceWarOutcome`, `AiIndependenceResponse`, `DecolonisationPressure`). Problems: `DeclareIndependence` is a second, separate copy of the civil-war flow in Politics (new country, garrison defection, militia, forced war, war flag `independence_war`, annex on defeat). Two nearly identical "rebel state" modules exist and must become one. It draws the shared RNG for garrison defection. The militia template is chosen by `Key.Contains("militia")` then `"infantry"`: a hardcoded string match on content keys. Tags come from a second `FreeIndependenceTag()` (I00-Q99), different from Politics' `FreeTag()` (R00-Z99): two tag allocators, which must be one. `SuppressIndependence` hardcodes the resource key `stability`, and the currency choice is `PowerResource` else the literal `"gold"`. `CreateIndependentCountry` copies popularity and picks the leading ideology, so Population writes into Politics. Notification text and wars are English strings in code.
- **Cores and claims:** monthly core gain and loss, yearly irredentism (January only, hardcoded `Month != 1`), colonization, accepted cultures, gold buildings. All numbers come from virtual rules (`CanCore`, `CoreLossYears`, `ClaimsNeighbour`, `MaxColonies`, `ColonyProgress`, `NativeAttack`, ...), which is the right shape. Hardcodes: AI reserve `100` gold, `0.5` and `0.25` build chances, `* 3` colonize gold margin, currency literal `"gold"`, `PopTypes.OrderByDescending(Share).First()` as the default class. Gold buildings are a parallel build system to Production, switched on only when Production is off: a feature fork inside one file. AI draws the shared RNG (`AiBuild`, `AiProductionBuild`).
- **Legacy hook:** `_aiCalled` and `AiFallback()` run the AI part when a backend `AI.cs` predates the feature. Same legacy pattern as in Politics: remove, the AI calls systems through a registry.
- **Script:** triggers `culture`, `culture_group`, `religion`, `religion_group`, `has_accepted_culture`, `same_religion`, `same_religion_group`, `same_culture`, `has_fellow_believers_in`, `is_colonizing`; values `population`, `accepted_share`, `faithful_share`, `num_of_colonies`; effects `add_pops`, `set_culture`, `add_accepted_culture`, `remove_accepted_culture`, `set_state_religion`, `convert_pops`, `assimilate_pops`, `add_religious_claims`. The scope switch `Country / State / Location-with-State` is repeated in each trigger by hand; a scope-adapter helper ("majority culture of any scope") replaces that.
- **Model:** `CultureDef/Group`, `ReligionDef/Group`, `PopTypeDef` (Share, Growth, Modifier, PromoteTo/Rate), `Pop` (culture, religion?, type?, size), `StatePopulation`, `CountryPopulation`, `GoldProject`. Optional religion and class are `null` when the feature is off; every consumer null-checks. Rebuild: pop key as a small struct, not strings joined with `|`.
- **Rebuild:** SAME for the rule set shape and the data model. CHANGE: merge the two rebel-state flows into one module, one tag allocator, one RNG stream, resources by data key.

### 5.3 Diplomacy (DiplomacySystem, DiplomacyRules, Relations: read in full)
- `DiplomacySystem`: data-driven `ActionDef`, war score, peace terms, truces, `CheckEliminated`.
- `DiplomacyRules`: reasons for `CannotDeclareWar`, great-power war reach, separate peace, `ContestStrength`, `AllowsAnnex`, define-gated total war and capitulation, `WillJoinWar`, `PeaceTermCost`, `PrefersPuppets`, `PrefersRegimeChange` (hash-based, no RNG), `WantsDemilitarized`, `PrefersLiberation`, `WillGrantIndependence`, `GivesLandToKin`, `AiAcceptsPeace`, `PeaceWillingness`, `MonthlyWarExhaustion`, `LibertyDesire`, `NavalStrength`, `SeaReachKm`, `AcceptanceModifier`, `MilitaryStrength` (cached per day).
- `Relations` is a clean store (section 2.2).
- **Rebuild:** SAME for opinion composition and peace terms. KEEP `Relations`. CHANGE the literals and cross-module reaches.


**Diplomacy partials (read in full this pass):**
- **WarGoals** is the biggest cross-system file. It holds war goals and casus belli, ticking war score, war exhaustion, liberty desire, subjects, release-a-people planning, stranded land, enclaves, capitulation, non-aggression pacts, new peace terms and script registration. It should be at least five modules (War, Peace terms, Subjects, Territory/Cores, Pacts).
- **Peace logic is world-geometry heavy:** `PlanRelease`, `DissolveHoldings`, `SettleStranded`, `LinkedHome`, `EnclaveHolders` do graph walks over states and score receivers with literals (+10 per neighbor, +15 same culture, +30 winner, opinion/10, rank x 1000, +1000 overlord, +100 patron). Those weights are hardcoded and should be rules.
- **Hardcoded keys:** `stability` (Humiliate), `stability_target`, `manpower`, `political_power_gain` (the exhaustion modifier key set), `"civil_war"`, `"total"`, `"cap:" + tag` war flags, `SubjectTypes.Values.First()` as the default subject type, literal opinion values (`50`, `-60`, `75`), `0.6` ally weight, ticking-score step 1 and a 365-day grace, `held*2 >= total`. Some are defines (`max_ticking_war_score`, `humiliate_stability`, `humiliate_power`, `break_pact_opinion`, `holdings_collapse`, `liberty_desire_rebel`, `subject_strength_share`).
- **Politics, Population and Warfare calls:** `Population.ReleaseCulture`, `MajorityCulture`, `Components`, `NeighbourStates`, `Politics.SetRulingParty`, `AddPopularity`, `IdeologyOpinion`, `PowerResource`, `Warfare.DestroyDivision`, `EvacuateTrespassers`. Diplomacy cannot be built without them.
- **Good patterns to keep:** per-day caching with explicit invalidation (`_cbCache`, `InvalidateCasusBelli`), a plan/apply split for release (`PlanRelease` then `ApplyNewTerm`, with a `Problem` string for the UI), and `GameEvents` raises (`RaiseSubjectMade`, `RaiseSubjectReleased`, `RaiseSubjectMonthly`, `RaisePactBroken`). Subjects are driven by hub events, not by direct calls: the model for all cross-system traffic.
- **Demilitarization** is a clean self-contained feature (own `SaveSection`, history file `history/demilitarized`, events `on_state_remilitarized`); it still calls `Warfare.EvacuateTrespassers` directly and `Population.NeighbourStates`. `how` is a free string (`"decision"`, `"war"`, `"treaty"`): make it an enum.
- **Save:** goals, ticking score, exhaustion and men lost are written per war block; statistics like `CasusBelliUsed`, `PeaceDeals` are not saved. `MonthlyWars` mutates `war.Vars` flags as persistent state.
- **Rebuild:** SAME for the casus belli data, peace-term types, subject types and liberty-desire rule. CHANGE: split the file, move geometry weights to rules, enum-ify flags (`civil_war`, `total`, `cap:`).

### 5.4 Characters, Production, Commodities, Economy, Supply, Technology (read in full)
- Same Rules + System pattern, coupled by direct calls.
- Script-value override hooks exist: `country_monthly_income`, `country_monthly_expenses`, `country_monthly_manpower`, `character_yearly_death_chance`, `character_yearly_birth_chance`.
- Characters use the main `_game.Rng`, not per-country streams.
- Hardcoded numbers and keys: see 3.5.
- **Rebuild:** CHANGE per 3.5 and 3.7.

### 5.5 Warfare (all files read in full, including WarfareSystem and Support)
- `WarfareRules`: `CanEnter` (not passable, demilitarized zones, allies, overlord/subject, access, wars), `MoveHours` (speed stat, `speed` modifier, terrain `MovementCost`, river x1.25, strait x2, retreat x0.75), `OrgRecovery` (script value `division_org_recovery` or define), `CombatFactor` (supply `0.5 + 0.5 * supply`, plus the source list), `DivisionPower`.
- `ICombatModel` and `CombatRegistry`. An unknown id logs once and falls back to `hoi4`.
- `CombatMath` shared helpers: general multiplier, country multiplier (modifier plus `division_combat_power` plus average combat factor), frontline picked by highest organization up to a combat width, `Damage` (also tracks `MenLost` per owner), progress bar.
- HOI4 model uses hardness, armor vs piercing, defense vs breakthrough. EU4 model has daily dice and alternating fire and shock phases. Imperator model has a d6 and martial difference.
- Weaknesses: shared `game.Rng` (3.7), hardcoded stat names and literals (3.5), and the three models share many ideas but no shared "damage step" abstraction.
- `DesignerRules` and `DesignerSystem`: see 2.2. Uses a save section marked `Early`, so templates load before divisions.
- `VisionRules`: see 2.2 and 3.1.
- **Rebuild:** SAME for the combat-factor source list and the model interface. CHANGE the RNG and literals. Split `WarfareSystem` (see below).

**WarfareSystem (1106 lines, read in full)**
- One class holds eight jobs: division and army management; pathfinding (`FindPath`/`FindPathCore`, a pooled A* with generation-stamped arrays, a per-country `Reachable` region cache keyed by day and war count, `LandComponent`); control and occupation (`SetController`, `OccupierFor`, `Occupation`); battles (`StartOrJoinBattle`, `ProcessBattle`, `Retreat`, `EndBattle`, `EndInvalidBattles`); demilitarized-zone routing (`RouteOutOfDemilitarized`, `PathOutOfDemilitarized`, `EvacuateTrespassers`, `NearestOwnLocation`); fronts and battle plans (`UpdateFront`, `BorderPairs`, `PeaceBorderPairs`, `HoldFallback`, `AssignPosts`); offensive AI (`Offensive`); queries. **Rebuild:** split into Movement/Pathing, Control, Battle, Fronts, and OffensiveAi modules.
- Direct couplings: Navy (`CanUseSea`, `SeaStepHours`, `CanCarry`, `Embarked++`), Diplomacy (`RecordLosses`), mod hooks (`OnDivisionDestroyed`, `OnLocationControllerChanged`, `OnBattleEnded`), the Hub (`RaiseLocationOccupied`), script events (`on_location_occupied`, `on_battle_won`, `on_battle_lost`), and the combat model (`Current(game)`, `Tick`, `OnBattleStart`, wrapped in try/catch that logs `combat-ex:<id>`). These become domain events in the rebuild.
- `CanEnter` hardcodes the demilitarized check on top of `Rules.CanEnter`.
- Hardcoded literals: battle score `delta = 1 + 4*loserLoss/(winnerLoss+loserLoss+1)`; org break threshold 0.01; destroyed at `Strength <= 0.01`; retreat score +10 own, -100 attacker location, +0.01 x distance; A* heuristic 1.5 land / 0.3 sea, expansion cap 30000, river x1.25, strait x2, embark x2, minimum terrain cost 0.1, default speed 4; siege `min(3, besiegers)` per hour and `hoursNeeded*(1+Fort)`; `PathRetryDay` +3 days; offensive score river x1.3 and objective factors 0.4+0.6 or x3; cache clear size 256; English-only army names ("{n}. {t.Name}", "{Ordinal} Army") and notification text. All go to defines and the string table.
- No RNG in `WarfareSystem` itself (the shared RNG is in the combat models). `AssignPosts` is deterministic (atan2 order plus largest remainder).
- KEEP: generation-stamped scratch arrays for A*, region reachability cache, front hash gating, stable post assignment, per-hour border-pair cache, profiler hooks.

**Support (258 lines, read in full)**
- `SupportRules` (virtual): `MaxVolunteers` (define `volunteer_share` 0.15), `CannotSendVolunteers`, `CannotLendLease`, `VolunteerOpinion` (define 20), `LendLeaseOpinion` (define 15). Refusal reasons are English strings.
- `SupportSystem` is the `lend_lease` feature. Its `SaveSection` "support" saves deals only; `_shipped` is not saved.
- Volunteers: the division `Owner` is changed to the receiver and `VolunteerFrom` is set. It is recalled daily when the host is at peace or at war with the home country. `Recall` destroys it if home has no capital.
- Lend-lease: a monthly share of the stockpile; needs the Production feature. `SendOnce` is a one-time shipment.
- The AI draws the shared RNG (`< 0.4`). Literals: category "army", stockpile > 200, share 0.1, opinion >= 20, >= 4 divisions.
- Calls Warfare (`StopDivision`, `AssignToArmy`, `Teleport`, `DestroyDivision`, `NearestOwnLocation`) and Diplomacy (`AddOpinion`) directly.
- **Rebuild:** keep the rules shape; give the AI a named RNG stream; save `_shipped`; replace direct calls with commands on the Realm layer.

### 5.6 Navy and Air (read in full)
- `NavyRules`: IsPort, Damage, PickTarget, RetreatBelow, RepairPerDay, TransportCapacity, SeaStepHours, InvasionFactor, ConvoyLosses.
- Navy hooks `Warfare.CombatFactorSources`. Naval battle changes `war.BattleScore` directly. Hourly tick: move, start battles, tick battles, convoys. Fleets come from `history/fleets`.
- AI logic (`AiMonthly`, `AiDaily`, and Air's `AiWeekly`) lives inside the system class and is called from `AiSystem`.
- `AirRules`: missions Superiority, CloseSupport, Bombing. Registers `SuperiorityFactor`. Air regions from `map/air_regions`, with automatic clustering.
- **Rebuild:** KEEP the source-list hook. CHANGE: move AI out of the systems into the AI module, replace direct writes with events, move literals to data (3.5).

### 5.7 Events, Decisions, Focuses (read in full)
- Events: `country_event`, `character_event`, scheduled events with delay, `on_actions` with effects, events and weighted random events, a player event queue with pause and timeout auto-resolve.
- Decisions: data-driven (`DecisionDef`). AI takes decisions at a rate set by the `ai_decision_speed` define.
- Focuses: HOI4-style trees. `FocusRules`: Days (`focus_speed` modifier), AiWeight (scaled by `HistoricalBias`), AiStartChance. Progress is stored in `c.Vars` flags (`focus_done:`, `focus_current:`).
- **Rebuild:** SAME for MTTH and tree semantics (1.14). CHANGE the RNG (3.7) and the +30 literal.

### 5.8 Resources, Statistics, Script values, Memory, Particles (read in full)
- See 2.2. These are the cleanest systems in the engine.
- Changes: Statistics metrics become data. Memory drops the version 1 loader.

### 5.9 AI (AiProfiles, AiSystem: read in full)
- `AiProfiles`: see 2.2. `AiProfileRules` has about 14 virtual hooks (GenericArchetype, HistoryWeight, ActionFactor, Readiness, ResourceMotive, Affinity, Restraint, Pursuit, JoinFactor, Foes, CommonCause, IsAwake, Courtship, Ambition, Motive). Personalities are cached 30 days. Comments refer to real history (Germany/Netherlands, Japan 1941, Czechoslovakia), so the AI is history-flavored.
- `AiSystem`: parallel per-country work (static `Parallelism`, `CountryRng`, `AiRng`). The daily tick alternates military and fleets by `(day + index)` parity. Air runs weekly. Monthly order: Recruit, Air, HelpFriends, Production, Navy, Decisions, Politics, Population, Diplomacy, Peace.
- Legacy overloads draw from the shared RNG. `Diplomacy()` and Peace still use the shared `Game.Rng`.
- **Rebuild:** SAME for the monthly order and per-country RNG. CHANGE: action registry (3.5), no legacy overloads, `BuildDemands` split by domain, RNG fix (3.7).
- Per the HISTORICAL_AI_PLAN: build the **scorecard first**, then tune the AI against it. Keep the "scorecard-first" approach.

### 5.10 Procedural flags (all seven files read in full)
- `Render(FlagIdentity)` returns 96 x 64 RGBA with 3 x 3 supersampling. Eight candidates per flag, the last is a plain safe fallback (`tricolour` for nations, `bicolour` for parties). A predecessor chain is followed at most 3 deep. Up to 6 sibling flags are used to avoid lookalikes.
- Seed is `Hash(Kind + ":" + Key)`, mixed with the candidate index.
- Family choice: weight × era factor × tradition weight × political-family weight. 75% chance of inheriting the parent's family.
- `FlagIdentity` is a record: key, color, kind, tradition, family, year, accents, predecessor, motifs, distinct.
- Family names (`tricolour`, `bicolour`, `cross`, `saltire`, `canton`, `chevron`, `geometric`, `heraldic`, `emblematic`) and emblem names (`rose`, `cog`, `star`, `hammer_sickle`, `flame`, ...) are used as keys, so treat them as data contracts.
- **Rebuild:** KEEP the generator and its determinism. SAME for the seed rule (flags must not change between versions). Move traditions and political families to data. 
- **`FlagFamilies` (436):** a `FamilyCfg` record per family (weights, edge, priority, charge limits, `MaxColors`, `NeedsCharge`, `PriLo/PriHi`, `EdgeLo/EdgeHi`, `MaxCharge`). `Build` is a string switch to static C# methods with literal probabilities, so adding a family needs C#. `EraFactor` has hardcoded year thresholds 1789, 1850, 1920. `FlagPalette` order: inherited, accents, political family, tradition, random pools; redmean distance thresholds 110/130/140; contrast score with a tincture rule. The family names "tricolour" and "bicolour" are British spellings quoted as existing data keys; keep them as contracts or alias them to American spellings on load.
- **`FlagPlan` (224):** records choices per slot `Family.slot`; hint reuse probability `Keep = 0.65` gives family resemblance across predecessors; weighted emblem picking (x3 family tag, x4 preferred, x2 tradition, x8 motifs).
- **`FlagEmblems` (216):** `EmblemDef` record (Tags, MinRadius, MinYear/MaxYear, Weight). About 25 emblems registered in a static constructor with year gates in code (`hammer_sickle` 1917, arrows 1931). `Register` is the only extension point and needs C#.
- **`FlagKit` (190):** canvas `A = 1.5`; FNV-1a `Hash`; SplitMix64 `Mix`; a private `Rng` class (a second RNG implementation, deterministic); HSL/RGB math; shape predicates (`Hit`, `Rect`, `Disc`, `Union`, `Minus`, `Poly`, `SymPoly`, `Lens`, `Star`, `Place`); layered `Flag` with `At`, `ChargeAt`, `Under`.
- **`FlagEvaluator` (148):** scores a 48 x 32 raster: palette merge (distance 40), primary-color share, edge density, low-contrast edges, charge area/border cut/illegibility, detail loss at half size, mass balance, and a 12 x 8 thumbnail compared against siblings (penalty under 0.08 distance). Rejections: too many colors, edges do not read, missing or tiny emblem, emblem cut by the edge, low contrast, identity color barely shows. Reason strings are English and internal. `FlagTraditions` lives in `FlagIdentity.cs`.
- **Rebuild verdict:** the architecture is clean and deterministic, so keep it. Move families, emblem registry, era tables, and evaluator thresholds to data. Keep the primitives, the plan/hint mechanism, and the evaluator. Unify the private `Rng` with the kernel's stream type without changing output (flags must stay byte-stable; add a golden-hash test per key).

### 5.11 HUD, GUI, rendering and app (all Burger.Game files read)
- **Read in full:** `Hud.cs`, `Hud.Mechanics.cs`, `Hud.Politics`, `Hud.Population`, `Hud.Characters`, `Hud.Panels`, `Hud.Observer`, `Hud.Wars`, `Hud.Designer`, `Hud.Military`, `Hud.Production`, `Hud.PopMood`, `Hud.Air`, `Hud.Support`, `Hud.Navy`, `Hud.Editor`, `Hud.Menus`, `Hud.Focus`, `Hud.Technology`, `Hud.Console`, `Hud.Popups`. Partly: `Interface.cs` (200), `Hud.Menu.cs` (200), `Gui.cs` (330 of 953).
- **Immediate-mode UI.** `Gui.*` helpers plus a scale factor `S`. Pixel literals are everywhere (`29 * S`, `Color(29,31,37,255)`, card colors `(36,38,44)`, `(40,52,66)` repeated in every panel). Panel height is either a parallel function (`PoliticsPanelHeight`, `PartiesPanelBodyHeight`, `LawGroupHeight`, `MovementsPanelBodyHeight`, `CabinetPanelBodyHeight`, `EffectsPanelBodyHeight`, `PeopleBodyHeight`) or measured with `FitRect(key)`/`FitEnd(key, r, h)` (previous-frame height, `_fitH`). The parallel functions duplicate layout math despite a comment claiming they cannot drift. Several panels also hand-compute `contentH` before `BeginScroll`. A few write `_fitH["prod0"] = 2000` to force a tall frame.
- **No view-model layer.** The HUD reads Core directly (`G.Politics`, `G.Population`, `G.Diplomacy`, `G.Warfare`, `G.Support`, `G.Production`, `G.Navy`, `G.Air`, `G.Focus`, `G.Tech`, `G.Designer`, `G.Events`, `G.World`) and sometimes mutates Core state itself:
  - Army `Clear` button sets `army.FrontLimit.Clear()`, `army.Objective = null`, `army.Fallback.Clear()` in the HUD.
  - Production `+`/`-` do `line.Factories++/--` and the construction queue is reordered by swapping list entries in the HUD (`c.Construction[i-1]`).
  - The map editor mutates `State`, `Location.State`, `Controller`, `Cores`, `Tax`, `Manpower`, adds to `World.States`/`StateByKey`, calls `RefreshOwnership()` and `G.RaiseMapChanged()` directly.
  - Fleet `Port` button sets `f.Retreating = false` after `RetreatHome`.
  These bypass the command pattern and would be unsafe under a replay or networked model.
- **Business logic and thresholds live in the HUD.**
  - Politics: notes thresholds `LowStabilityNote=35`, `FactionNoteCohesion=55`, `ElectionNoteDays=120`, `BannedSupportNote=0.10`, movement 25/60; loyalty colors 35/55; `Heat` 60/35; stat tile >=8 good, <=3 bad; character health <40/<70, loyalty <25/>=80, age >=70.
  - Parliament seat layout (`rows = clamp(round(sqrt(n/2.6)), 3, 14)`), majority `n/2+1`, "government seats".
  - Peace panel: `BuildOffer`, `StateReceiver`, war-score verdict (|score| < 5 undecided), `StalemateMonths` (still >= 6/12), exhaustion red past half; "release peoples" picks the majority culture of states.
  - Pop mood: opinion tiles (>=10 good, > -10 neutral), unrest <0.1/<0.3, underground <0.02, `PeoplePageSize = 7`, `ReactionColor` by string `"Approve"/"Comply"/"Disapprove"`, `ObjectiveText`/`ObjectiveTip` switch on objective keys (`autonomy`, `religious_rights`, `labour_rights`, `property`, `stability`) with the rules explained in English prose in the HUD ("Unhappy under governments of the right").
  - Military: army strength <0.5 red, supply <0.5 red; fleet/air rows.
  - Production: efficiency stripe >=0.75 good, >=0.3 gold; per-day output formula `Factories * production_per_factory * Efficiency / Cost` re-derived in the HUD (a second copy of the Core formula; it can drift).
  - Designer: stat list `max_strength`, `max_organisation`, `soft_attack`, `hard_attack`, `defense`, `breakthrough`, `piercing`, `speed`, `combat_width`, `supply_use`; `supply_use` is the only "lower is better" stat (hardcoded); grid is 5 columns; slot math in the HUD.
  - Support: volunteer/lend-lease share presets `{0, .05, .1, .25, .5}`, `+100/+100/+1k` amount steps, `-100`.
  - Declare-war dialog hides casus belli by `reason.StartsWith("Casus belli")` (matching a localized-looking English string to decide visibility).
- **Name-guessing by substring.** `UnitColor`/`UnitIcon` guess from `UnitType.Category` (`air`, `fight`, `bomb`, `nav`, `ship`, `sea`, `sub`, `art`, `support`, `gun`), `EquipGroup` guesses from equipment key (`infantry`, `support`, `artillery`, `anti_`, `tank`, `motor`, `truck`) and category, `CategoryColor` has a fixed table (`factory`, `infrastructure`, `military`, `naval`, `civil`, `air`, `capital`, `screen`, `transport`), `PoliticsSourceIcon`/`PoliticsModifierIcon` substring-match source or key, `ModifierText` special-cases `_drift` and `political_power_gain`. `TechTip` shows `kv.Key.Replace('_', ' ')`. Data keys not covered fall to a default icon silently.
- **English is hardcoded.** Nearly every label, tooltip, role name and status string (including long explanatory tooltips and status messages like "Click an air region in range for ...") is a string literal in C#. Only names that come from data pass through `Localisation.Get`/`GetOrPretty`. `Fmt`/`Num`/`People` number formatting is English-only. Plural handling is ad hoc ("division(s)").
- **Command pattern worth keeping.** Core methods return an error string or null (`P.Enact`, `Appoint`, `Placate`, `Favour`, `Sideline`, `Invite`, `Dismiss`, `Concede`, `Suppress`, `AcceptCulture`, `Colonize`, `GoldBuild`, `G.Diplomacy.Use`, `SendPeaceOffer`, `A.Assign`, `A.Rebase`, `A.BuildWing`, `N.BuyShip`, `sup.SendOnce`, `sup.SetLendLease`, `T.Start`, `F.Start`, `G.Designer.Create/Update/Delete`). `Cannot*` methods feed disabled-button tooltips (`CannotEnact`, `CannotPlacate`, `CannotBuildReason`, `CannotBuyReason`, `CannotResearch`, `CannotStart`, `CannotSendVolunteers`, `CannotLendLease`, `CannotSaveReason`, `CannotDeclareWarReason`). Requirement checklists use `[OK]`/`[X]` (`F.Requirements`, `TechTip`, `ExplainTrigger`). `G.Script.Describe(effects, ctx)` previews what an action would do (`DrawProposals`). `G.Events.DescribeOption`/`IsOptionAvailable`/`OptionName` do the same for event options. `ActionButton` is read-only in observer mode. Keep all of this as the UI-to-Core contract.
- **Shared widget set (keep, port to the new toolkit).** `Panel`, `CloseButton`, `Section`, `Button(rect, label, enabled, tooltip, selected, size)`, `ConfirmButton` (click twice), `TextField`, `BeginScroll/EndScroll`, `Bar`, `Card`, `Stripe`, `Tabs`, `FilterChips`, `Collapse`, `Pie`, `ShareBar`, `Chip`, `MeterRow`, `StatRow(label, value, key, was, lowerBetter)` (shows change against saved), `IconStat`, `FlagChip`, `FlagRow`, `Wrapped`, `WrappedHeight`, `Fit` (ellipsis), `Tooltip` with inline markup (`[icon:production][state:value]`, `[state:warning]`), `Block(rect)` (consume mouse). `Gui.Block` is called per widget so overlapping panels do not click through.
- **Map interaction is a tool state machine in `Ui`.** `Ui.Tool` (`PlanTool.Front/Arrow/Fallback/PickFront`), `Ui.PlanArmy`, `Ui.OrderingWing`, `Ui.WingMission`, `Ui.SelectedFleet/Army/Divisions`, `Ui.BuildType`, `Ui.ChoosingGeneralFor`, `Ui.RecruitTemplate`. Map clicks route to `PlanMapClick`, `WingMapOrder`, `EditorClick`. Right-click or Esc cancels. This is hidden global UI state with several mutually exclusive tools and no single "current tool" owner; stale tool state is cleared ad hoc (`_fitH`, `Ui.Tool = None` in many places).
- **Per-panel details worth carrying over.**
  - Designer: templates left, slot grid (drag, click, right-click) or +/- list in the middle (both edit the same `_design` list), live stats right with change vs saved template, `MaxBattalions`, `CanUse`, `Preview`, `CannotSaveReason`, `Create/Update/Delete/CanEdit` (custom vs from-files), "Train" opens the recruit menu with the template. Stock availability `AvailableShare` of required equipment.
  - Military: armies with generals, front enemy (country at war or neighbor), mode Hold/Offensive/Fallback, battle plan tools only for the selected army (front limit, objective arrow, fallback line), `FrontTargetOf` rules, generals chosen by `IsGeneral` then `GeneralStat`.
  - Production: lines, construction (project queue with priority via swap, up to `max_factories_per_project` factories), stockpile with shortage. Naval equipment uses dockyards (`UsesDockyards`).
  - Air: wings, missions Superiority/CAS/Bombing (`CanFly`, range from base), rebase, new wings vs production feature (two code paths: `production` on = stockpile, off = orders with gold/manpower/days), air bases by capacity.
  - Navy: fleets, transport capacity, ship building (same two paths).
  - Focus: tree laid out by `X`/`Y` grid with elbow connectors, mutual exclusion dashes, `Prerequisite` (any) vs `Requires` (all), `Days(G, player, f)` from `G.Rules.Focus`, reward via `DescribeReward`.
  - Technology: slots, categories, `rules.Slots/MonthlyPoints/Cost`, `Year`/`Order` sorting, `Unlocks` list, `Modifier` dictionary.
  - Events/proposals: a queue (`G.Events.PlayerQueue`, `G.PendingProposals`), one at a time, event first. Event picture via `Assets.Texture(pe.Def.Picture)`.
  - Console and error log: console input handles `` ` ``, history, `save` and `observe_ui` special cased before `G.Commands.Execute`, log lines capped at 500, error panel truncates at 180 chars. The console reads raw keys itself instead of using the shared `TextField`.
  - Pause menu: a hardcoded list (Resume, Save/Load, Error log, Settings, Main menu, Quit). Settings: speed-5 days per second (text field), historical vs ahistorical AI, AI on/off, saved through `UserSettings`. Save menu caches headers (`SaveGame.Peek`) because reading them each frame was slow.
  - Map editor (F10): paints locations into states, creates a state per location, changes owner/cores/tax/manpower, `StateWriter.Save` writes back to `map/states` keeping comments. It duplicates the Studio's job inside the game and changes Core data live (see no view-model above).
- **Rebuild implication.**
  - Add a view-model/query layer: reasons (`Cannot*`), explanations (`Explain*`/`Requirements`) and attention-note providers with data-driven thresholds, so the HUD only formats.
  - Route all mutation through commands (queue reorder, army plan clear, production line change, editor ops), so replay/undo/multiplayer are possible.
  - Use measured or retained layout instead of the duplicated height math. Put card/stripe/state colors and sizes in the theme file.
  - Move every string and icon choice to localization and data (category/icon/color tables, objective text, unit stat "lower is better" flag, stat lists per panel).
  - One tool-mode owner for map clicks instead of scattered `Ui.*` fields.
  - Remove the HUD copy of the production-per-day formula; ask the Core.
  - Keep the command/Cannot/Explain contract, observer read-only mode, the generic widgets, and the two-path (feature on/off) UI only if the feature split stays.
- **`Gui.cs` (all 953 lines now read).**
  - Themeable through `Skin(name, rect, tint)`: every widget tries a theme image first (`panel_header`, `close_button`, `close_button_hover`, `button`, `button_hover`, `button_selected`, `button_disabled`, `text_field`, `text_field_focused`, `bar`, `bar_fill`, `card`, `card_selected`, `scrollbar`, `scroll_thumb`, `section_line`, `top_bar`, `tooltip`), then a nine-patch, then a procedural gradient. Keep this fallback chain; it is why mods can reskin the UI.
  - **Tooltip tone and icon are guessed from English words.** `InferTooltipTone` scans for "loss", "shortage", "retreat", "revolt", "cannot", "unable", "war exhaustion" (bad), "missing", "requires", "needs", "low supply" (warning), "gained", "increase", "grow", "available", "accepted", "core state", "efficient" (good), else any digit is "value". `TooltipSubject` maps ~100 English terms to icon names (terrain names, fleet/navy, air, strength, manpower, supply, war score, population, gold, factory, stockpile, production, technology, characters, politics, decisions, recruit). Explicit `[state:good|bad|warning|info|value]` and `[icon:x]` tags exist and always win, but most call sites use plain text, so colors and icons change when a string is reworded or translated. Rebuild: tooltips come from structured rows `{text, tone, icon}` produced by the Core, never inferred from prose.
  - Delta coloring: any signed number in a tooltip (`+5`, `-2.5%`) is recolored by regex (`SignedNumber`), "drastic" at |50| absolute or |25| percent. The sign must not follow a letter, digit or dot so `1930-1936` is not a delta. Hardcoded palette (`DeltaPositive` etc.) and thresholds (`DrasticAbsoluteDelta = 50`, `DrasticPercentDelta = 25`). This heuristic also colors any number that merely has a sign, whatever it means; a "lower is better" number shows green when it rises.
  - `StatRow` takes a `lowerBetter` flag; the Designer passes `k is "supply_use"`. `_iconAliases` maps stat keys to icon files (`max_strength`->`strength`, `soft_attack`->`artillery`, `hard_attack`->`battle`, `defense`->`fortification`, `breakthrough`->`army`, `piercing`->`ammunition`, `speed`->`road`, `combat_width`->`border`, `supply_use`->`supply`, politics tab names). Icons are `gfx/interface/tooltips/<name>.png` loaded on demand through `Assets.Texture`.
  - Input model: static state (`_focusedField`, `_dragSlider`, `_confirming`, `_scroll` dictionary by string id, `_clips` stack, `_wheelUsed`, `LeftClickConsumed`). Ids are strings, so two panels using the same id would share scroll state. `Block(rect)` registers "mouse over UI" for the frame, so panels drawn later cover earlier ones. `TextField` ignores IME/paste/selection/cursor movement (append and backspace only). `Slider` exists but was not seen in any panel read so far.
  - `Gui.Scale = Gui.ScaleFor(screenHeight)`; `Hud.S` mirrors it.
- **`MapRenderer` (read in full).**
  - Map data is two textures plus a per-location lookup texture: `_mapDataTex` (per pixel: location index low/high byte, terrain, river flag or coast distance), `_lutTex` (3 blocks of `256 x blockRows`: block 0 = map-mode color, block 1 = owner id (16 bit) + state id (16 bit), block 2 = stripe color + flag bits), `_atlasTex` (terrain tile atlas, `TileSize = 512`). The fragment shader draws borders, stripes, hover, selection, fog, state highlight and demilitarized hatching from those. Changing ownership only re-uploads the small LUT. Keep this design.
  - Flag bits in the LUT (hardcoded contract between C# and the shader): 1 water, 2 occupied, 4 selected, 8 hovered, 16 lake, 32 not visible, 64 highlighted state, 128 demilitarized. The flag computation is duplicated in `UpdateLut` and `UpdateLutFlags` (the second one omits the 128 demilitarized bit, so hover on a demilitarized location clears its hatching until the next full rebuild).
  - Map modes are a C# enum (`Political, Terrain, States, Diplomacy, Locations, Supply, Air, Ideology, Population, Culture, Religion`) with color logic inline in `UpdateLut`; a data-driven `CustomMode` (`MapModeDef`) overrides it through `Interface.MapModeColor`. Hardcoded color ramps and constants: population log scale `(log10(per) - 3) / 3.5`, supply `v / 6.0` and cutoff `0.05`, diplomacy colors (self green, war red, allied blue, overlord/subject light blue, truce grey, opinion tint), air superiority ramp, `HashColor` golden-ratio hues. Rebuild: all built-in modes become `MapModeDef` entries in data (value expression + color ramp), no C# enum.
  - Hidden `DrawHoverInfo` and the LUT update call Core systems (`Politics.RulingIdeology`, `Population.MajorityCulture`, `Supply.SupplyAt`, `Air.RegionOf`, `Diplomacy.Opinion`) from the renderer, so the renderer depends on Core rules (no map-color provider interface).
  - Procedural parts are render-only and not part of the sim: tree placement with `new Random(42)` (60000 cap), noise terrain tiles with `new Random(1234)`, coast-distance flood fill (`CoastRange = 24`). They are deterministic per map, but nothing guards that they stay that way. Keep them out of simulation hashes.
  - Heightfield: a CPU height field for picking and a render mesh; `HeightAt` uses the same two-triangle split as raylib's `GenMeshHeightmap`, and `Pick` walks cells with an exact ray/triangle test (cell traversal) so picking matches the visible surface. The code comments describe past bugs (half-cell shift, diagonal mismatch, sea plane covering coast). Keep the exact picker and add a regression test that checks pick(WorldPos(p)) == location(p).
  - `StrategicZoom = max(140, 0.33 * max(world))` switches off fog and unit counters; `FogShading` is a static field "kept for the whole session". Many camera-height constants (tint 0.55-0.95, border scale 0.6-4, `camH < 120` borders, `camH < 40` trees) are inline.
- **`App.cs` (read in full).**
  - Screens: Menu, Playing, Error. Loading (`LoadGame`) rebuilds everything (`Game.Load(root, compileScripts: true)`) and "Reload files" does the same, so a reload equals a restart (no hot reload of data). `_pending` runs a reload or load between frames. Load errors become a screen with the log; save errors are caught and returned as strings.
  - Frame order: keys -> camera update -> pick -> draw world -> overlay (labels, orders, units, fleets, air bases) -> map mouse -> hover info -> HUD -> `EndFrame`. The simulation advances AFTER drawing (`game.Clock.Update(dt)`), wrapped in try/catch that pauses the game and logs "Simulation error" (a failing system pauses the whole game but leaves state half updated; there is no rollback). Frame `dt` is capped at 0.1 s.
  - `Camera` is a damped controller (`SmoothFactor 0.15`), frame-rate independent; zoom anchors to the cursor using the logical (not damped) camera; middle-drag pans in screen space. `IsMoving` and `PickFast` are leftovers (`PickFast` just calls the exact `Pick`; the comment about a cheap path is stale).
  - Automation hooks: `--open` (panels by name with sub-tab), `--exec` commands, `--screenshot` + frame number, `--perf` (fps, ms/frame, sim ms/frame, days/s, per-system profile with p99 and worst days, path counts), `--speed`, `--zoom`, `--map-mode`, `--observe`, `--tag`, `--load-save`. Keep these; they are the only UI test harness. The `open` switch is a hardcoded list of panel names in `App`.
  - Commands registered in `App` itself (`soldier_gag`, `particle`) reach into render objects from game commands.
  - The menu duplicates save-list code with the Hud (two caches: `_saveListCache`/`SaveLabel` in `App`, `_saveCache` in `Hud`). `DrawHoverInfo` holds per-tool hover text and `Cannot*` reasons (production build, recruit, air assign/rebase, front pick), plus the hardcoded "a division uses about 0.3 per day" supply text.
  - Saves: `SavesDir` = sibling `saves` of the game folder; names `TAG_YYYY-MM-DD_yyyyMMdd-HHmmss`; `quicksave`; `*.sav`; `SaveGame.Peek` reads a header. The window title and icon come from `game.Data.Info`.
  - Fixed text in the main menu (`"Choose a country (or click it on the map):"`, `"Observe"`, `"Reload files"`) and hardcoded panel sizes (`420 * s`, `listH = ... - 250 * s`).
- **`CameraController` (read in full).** Logical target/distance damped to a rendered pose, tilt grows with zoom (`back = Distance * (0.75 - 0.55 t)`), pulls toward the map center when zoomed out (`pull = t^2`), fixed 40 degree FOV, `MinDistance = 5`, `MaxDistance = 1.05 * max(world)`. Keys: WASD/arrows pan, Q/E and PageUp/PageDown zoom, Shift = x2.5. Pure view code, keep.
- **`CountryLabels` (read in full).**
  - Paradox-style curved names. The Core snapshot (`CaptureInput`) copies owner, name, capital and fixed geometry into immutable records; a background `Task` finds the contiguous blocks per owner (BFS over land neighbors), merges blocks of one country separated by a narrow sea (`gap = max(6, width * 0.0025)`), and fits a line per block group. Candidates: arc (least-squares quadratic), spine (smoothed bin midpoints), ridge (Viterbi chain through land stretches per bin) at several angles (+-60 degrees in 15 steps plus the principal axis). Scores weigh font size, steepness, coverage, share on land and bend. The result is published when the version still matches.
  - Rebuild triggers: `game.MapChanged`, plus `PollChanges` every 15 frames which calls `_game.RefreshNames()` and hashes every country's index, name, capital and every location's owner. Debounce 0.4 s, minimum interval 1 s, max wait 1.5 s. A comment states that not every name-affecting change raises `MapChanged`, so the render layer polls. This is the "no domain events" smell again: rebuild needs a `NamesChanged`/`BordersChanged` event.
  - Hardcoded: label wanted when it holds the capital or has `locs >= 2 && px >= 400`; names are `ToUpperInvariant`; at most 24 points; label font cap 140 px; `minScreenFont = 3`; alpha 150-245; glyph advance cache per size; per-letter `DrawTextPro` with four shadow passes (about 5 draws per glyph). A `BURGER_LABEL_DEBUG=TAG` environment variable writes `label_debug.txt` in the current directory.
  - It reads `c.Name` of the live country (so titles, party and ideology names show), and `Task.Run` worker uses only snapshots, so it is thread safe. Keep the design (snapshot, worker, publish by version). Rebuild: the pure layout becomes a Core-free function over a `MapGeometry` snapshot and is unit tested with fixed inputs; per-letter drawing becomes a glyph-batch or SDF text.
- **`MapOverlay` (read in full).**
  - Draws labels, battles, division counters (one per `(location, owner)`), fleets, air-base badges, front lines, battle-plan arrows, fallback lines and selected-division paths. Each draw method also does hit testing and selection (`Gui.Hover`/`Gui.Clicked` on rectangles) and mutates `UiState` (`SelectedDivisions`, `SelectedArmy`, `SelectedFleet`). Presentation, input and command logic are in one method. Rebuild: a draw list (`Counter { rect, owner, divisions }`), and a separate input pass that hit-tests the draw list.
  - `w.Divisions.GroupBy(...)` runs every frame (also in `MapFigures`): allocations per frame. Rebuild: per-location stack index kept by the Core.
  - Fog of war: `owner != player && !IsVisible(loc)` hides stacks; supply warning triangle at `supply < 0.75` (red below `0.4`); tooltips list up to 8 divisions; colors, sizes (`50 x 22`), zoom scale `clamp(60 / camY, 0.65, 1.15)` are inline. Labels: country names when `camY > 18`, hovered province name when `camY < 30`.
  - English tooltip text is built inline (`"Battle of ..."`, `"Attacker: ..."`, `"Combat model: ..."`, fleet statuses "In battle/Retreating/Moving/In port/At sea"). `_ = ppu;` is a leftover unused variable. `DrawUnits` clears `Counters` twice.
  - Core types used directly: `Burger.Core.Warfare.CombatMath.RiverCrossing`, `_game.Combat.Current(_game).Name`, `_game.Navy.Fleets`, `_game.Air.WingsOf/PlanesAt/Capacity`, `AirSystem.MissionName`.
- **`MapFigures`, `MapParticles`, `FigureRenderer` (read in full).**
  - One 3D soldier (or light tank) per stack the player can see, up to `MaxFigures = 400`, with `MinPixels = 12` on-screen culling and `HeightFor(cam) = clamp(camY * 0.08, 0.3, 2)`. Animator state per stack (`_stands`, keyed by the first division, purged every 600 frames), eased position (`1 - exp(-6 dt)`), eased yaw (7 rad/s max), ground lean from `GroundNormal`, gait speed matched to travel speed (walk/run/idle/fire/aim clips), and combat rate x1.8. Clip events (muzzle flash) drive `MapParticles.Emit`.
  - Hardcoded unit kinds: a figure is a tank only when the template contains a unit of type key `LightTank.Key`; everything else is a soldier; `Uniforms.ClipFor(owner.Tag, ...)` picks a per-country clip. This is the same "unit class from a single key" problem as in Warfare: the figure model must come from the model definition (see 3.1), not from `IsLightArmour`. `StandingGag` is a static console test hook.
  - `MapParticles`: pool of 3000 billboards with a fixed RNG seed (`new Random(7)`); effects come from `common/particles.txt` (`ParticleDef`: count, direction forward/up/out, spread, speed, life, drag, gravity, size, color ramp, additive), plus requests from console and scripts (`_game.Particles.TryTake`). Two passes (alpha then additive). Render-only, not part of the simulation. Keep. Particles are not saved and not seeded per game, so they do not break determinism.
  - `FigureRenderer`: GPU skinning (4 joints per vertex carried in tangent + the two texcoord sets; `bones[192]` as 3 rows per joint; `Rig.MaxJoints`), vertex limit `ushort.MaxValue`, half-flat shading, depth "pull" so slopes do not clip feet. Includes an offline toolchain inside the runtime class: contact sheet, gear sheet, people sheet, soldier sheet, turntable PNG frames and cached `.gif` rendering through `ffmpeg` with SHA1 signatures (`GifLook = "turntable-3"`). These run from `Game.exe --figure-*` flags. Keep the sheets and GIF cache but move them to a `Burger.Tools` command (they only need a hidden window plus the renderer), so the game binary does not carry `ffmpeg` export code.
- **`Media` (read in full).** Animated UI images: `.gif` through `LoadImageAnim` (frame delays parsed from the GIF graphic-control extension by hand) and video (`.mp4 .webm .mov .mkv .avi .m4v`) through external `ffmpeg`/`ffprobe` processes on the PATH, decoded on a background thread to raw RGBA at most 256 px on the long side, 5-30 fps, paced by a stopwatch, looped by restarting the process. Audio is extracted once to a temp `.wav` under `burgerengine_media` (cache key is `string.GetHashCode()` of path + length + time; this hash is randomized per process on .NET, so the cache is never reused across runs). Clips only decode while drawn (`Use` marks a frame). Missing tools degrade to a one-time warning. Rebuild: keep the idea (visible-only decode, graceful degradation), fix the cache key with a stable hash, and make the `ffmpeg` dependency optional and documented.
- **`Assets` (read in full).** Fonts (theme font, then `gfx/fonts`, then exe `fonts`, then system fonts, then raylib default; body 36 px, bold 36 px, header 72 px; fixed codepoint set Latin-1 + a few punctuation, arrows and the euro sign), a texture cache keyed by relative path (case-insensitive; missing files cached as empty), country flags keyed by `FlagVersion`, party flags keyed by scope string, generated flags from `FlagGenerator` (seeded by tag), `NoiseTile` (procedural terrain tile, value noise seeded per terrain). The fixed codepoint range means no CJK, Cyrillic or Greek: another localization blocker. System font paths are hardcoded (Windows names, one DejaVu path).
- **`Shaders` (read in full).** One terrain shader (GLSL 330): per-fragment lookup of the 4 nearest map pixels to get smooth borders (`edgePx`), province/state/country/coast lines, water (waves, fresnel, specular, foam), terrain atlas blend, hill shading, map-mode tint, DMZ cross-hatch, occupation stripes, fog of war, highlight/hover/selected. The flag bit constants are repeated in a comment and in C#. Keep as the base shader. Rebuild: generate the flag constants into both sides from one table.
- **`Viewport`, `UiState`, `UserSettings`, `RenderPerf` (read in full).**
  - `Viewport`: lets the Studio embed the map in a panel (origin, size), exact world-to-screen and ray helpers, and `TryProjectSnapped` rejecting points behind the eye (a documented bug fix). Keep.
  - `UiState`: enums `LeftPanel`, `RightPanel`, `PlanTool`, and `PeaceDraft` (a peace deal under construction: states, subjugate/annex/gold/concede/free subjects/regime change/humiliate/demilitarize/release cultures). `UiState` is one flat bag of selections, so adding a panel means editing the enum, the `Hud` switch and `App`'s `--open` list. Rebuild: panels register by id (the data `PanelDef` registry already does).
  - `UserSettings`: `settings.cfg` next to the exe, `key = double` only (speed 5 limit, historical bias 0.75 default, ahistorical, ai_disabled, portrait_sound). Writes into `AppContext.BaseDirectory` (fails silently on read-only installs). `Apply` pushes settings into the game (`Clock.DaysPerSecond[5]`, `HistoricalBias`, `Ai.Enabled`). Rebuild: per-user config dir, typed settings with a schema, and settings are never part of a save (so replays stay deterministic).
  - `RenderPerf`: ring buffer of 600 frames per stage (input/pick, map, labels, orders, units, fleets, air, hover, ui, EndDrawing), allocation and GC counters, p50/p95/p99 report. Keep.
- **`Hud.Menu.cs` 195-402 and `Interface.cs` 195-459 (now read).**
  - Menu icons are procedural shapes drawn with raylib primitives per `MenuIcon` (navy, air, population, politics, decisions). Recruit panel: lists `Data.Templates` filtered by comparing the reason string to `"This template belongs to another country"` (a string compare as a rule check). It calls `G.Rules.Economy.CannotRecruitReason` per template per frame, and `Economy.Recruit`, `CancelRecruitment`, `Production.AvailableShare/Stock`. Hardcoded stat list in the tooltip (`max_strength`, `max_organisation`, `soft_attack`, `hard_attack`, `defense`, `breakthrough`) and row/button sizes. World panel sorts every country by owned location count every frame.
  - `InterfaceRegistry`: data panels (`panel = { id title button_text hotkey icon width potential ... }`) with elements `text`, `header`, `separator`, `button { allow effect }`, `bar { value min max color }`, `list { iterator limit max_rows children }`. Modding operations on an existing panel: `override` (replace elements), `extend` (append elements), `disable` (remove), mutually exclusive, id required for them; panel width validated as an integer 200-1000 (default 420). `map_mode = { id name value min max low_color high_color }` colors locations by a script value; `ColorFn` is the C# alternative. Theme block loads into `ThemeDef`. Keep all of this; it is the right UI extension model. Gaps: only text/header/separator/button/bar/list (no image, tabs, tooltips on list rows, icons per row, conditional sections other than `potential`), and `Potential`/`Allow` are evaluated per frame per element with no caching. Map-mode values are evaluated per location on each rebuild.
- **Game project UNREAD list after this batch:** nothing from the original list. The first 195 lines of `Hud.Menu.cs` and `Interface.cs` were read in earlier batches. Tool/test/benchmark behavior of these files is still not verified by running.

### 5.12 Studio (all 20 source files read in full)
- **Shape:** one `sealed partial class Studio` split over 11 files (`Studio`, `.Content`, `.Inspector`, `.Features`, `.Console`, `.World`, `.Shell`, `.Code`, `.Outliner`, `.SelfTest`), plus `Ui`, `Catalog`, `Program`, and `Project/` (`ProjectStore`, `ProjectIndex`, `Specs`, `Templates`, `Engine`). About 5,500 lines. The class is the god object of the editor (same smell as 3.1): selection, modals, dock layout, world, console, features and file browser all share private fields.
- **`ProjectStore` is the best idea in the editor and must be kept.** All file access goes through one cache of texts with dirty flags, LF normalization with remembered CRLF/BOM per file (round-trips the user's bytes), `Changed(rel, source)` and `DirtyChanged` events, `Edit(rel, text => text)`, `Revert`, `ReloadAll`, `Save`/`SaveAll`, and `FilesOf(fileOrFolder)` which mirrors the engine's "file or folder" source rule (`common/traits` = `common/traits.txt` + `common/traits/*.txt`) and also lists files that exist only in memory. A form edit and the text editor edit the same string. Pick-up of outside changes: only when the file is not dirty and the disk time differs.
- **Edits are text patches, not model writes.** Every field edit goes through `ScriptPatch.SetValue/Set/Remove/AppendStatement/ReplaceSource` on the cached text, so comments and formatting survive (contract 1.2). Blocks are addressed by `#index` paths (`BlockRef.Path`), not ids, which is why repeated keys such as events work; `Catalog.Refresh` re-finds a block by (file, id) and falls back to (file, path). Keep this addressing scheme.
- **`BlockSpec` / `FieldSpec` (`Specs.cs`, 628 lines) is the editor's schema layer.** One static spec per content kind: `Title`, `Source` (file or folder), `DefaultFile`, `Container` (e.g. `ship_types`), `IdKey`, `NameKey`, `StatementKeys`, `NewTypes`, `ValidateId`, `Help`, `Template`, `Fields`, `ExtraFields` (traits add one numeric field per character stat from `character_stats`), `ScriptFirst`. `FieldKind` = Text, Number, Bool, Color, Word, List, Choice, Country, Location, Character, Image. The 28 kinds are listed in `Catalog.Kinds`. Verified list: Countries, Characters, Resources, Modifiers, Decisions, ScriptValues, Interface, Events, DiplomaticActions, CharacterInteractions, CasusBelli, SubjectTypes, Traits, Terrain, UnitTypes, DivisionTemplates, Equipment, ShipTypes, PlaneTypes, Buildings, OnActions, Ideologies, Governments, LawGroups, CabinetPosts, CultureGroups, ReligionGroups, PopTypes.
  - **Problem: this schema is a hand-written copy of what the engine already knows.** Field names, choice lists (`category = fighter|cas|bomber`, `screen|capital|transport`, `army|naval|air`, trait categories, modifier scopes) and help text are duplicated from Core loaders and docs. Several drift risks are visible: the Modifiers form lists exactly 10 modifier keys (`income upkeep manpower tax attack defense org_recovery speed opinion relations`) while the engine reads open-ended `modifier:<key>`; the Ideology template contains `opinion = { {id} = 10 }` where `{id}` is replaced by the new id (so a new ideology gets a self-opinion, which is intentional but undocumented); `Specs.OnActions.Help` hardcodes the list of 16 on-action names. **Rebuild: generate `BlockSpec` from a schema each module registers (the same registration that feeds the validator and `burger docs`), so there is one source of truth for fields, choices, help and templates.**
  - `ValidateId` is a single `IdRule`; countries use `Country.IsValidTag` (3 characters, letters and digits). Event ids are `namespace.N` with `NextEventId` taking max+1 per namespace.
  - `FeatureOf(kind)` maps a kind to the feature that gates it (units/divisions to `Military`, characters/traits to `Characters`, diplomacy kinds to `Diplomacy`, equipment to `Production`, ships to `Navy`, planes to `Air`, ideologies/governments to `Politics`, laws, cabinet, cultures to `Population`, religions to `Religion`, pop types to `SocialClasses`); `Buildings` are available when any of Production/Navy/Air/SupplyRange/SupplyHubs is installed. **This table is a fourth place where "feature to content kind" is encoded** (with `FeatureSet`, `FeaturePackages` and the Core loaders). It must come from the module registration in the rebuild.
- **`ProjectIndex`** feeds every drop-down (tags, locations, characters, traits, character stats, terrains, division templates, modifiers, events, states) by re-parsing the cached files after any change (`_valid = false` on every `Changed`). Whole-index rebuild per edit is fine for small projects but re-parses localisation, locations and every events file on each keystroke commit; cache per source file in the rebuild. It parses its own copy of the location-file format (`HEX = name` or `name = HEX`) and the default stat list `martial finesse charisma zeal`, a third copy of data the loader owns. `SetText(key, text)` writes into the file that already defines the key, else `localisation/editor.txt`.
- **Content Browser** (`Studio.Content.cs`): Game and Backend roots (`BackendRoot = "../backend"`; "players can change these files" versus "compiled into the EXE"), tree plus tile grid, search across all files (capped at 300), tile size slider, thumbnails for images (`Textures.Get(path, 128)`), rename (F2), duplicate (`name_copy`, `name_copy2`...), delete to the OS recycle bin (`Microsoft.VisualBasic.FileIO`), create menus per kind with feature gating, context menu "add to this file" per kind. Windows-only calls remain: `explorer.exe /select,`, `Microsoft.VisualBasic.FileIO`, `Engine.OpenInExplorer`; the strings say "Windows Explorer", "Windows Recycle Bin" and "Drag files from Windows". These must be abstracted behind a small `IShell` for the rebuild (the user's own machine is Linux).
  - **Create flow:** `DoCreateBlockAsset(kind, id, type, file)` picks the main file (`TargetFilesOf`: default file, else the file holding the most blocks of the kind; files that hold only other statement kinds are skipped), fills the kind's template (`{id}`), special-cases the map-mode template inline, appends with `ScriptPatch.AppendStatement` (or `Set` under the container), saves immediately only when the file was not already dirty ("never saves other unsaved edits of that file"). `DoDuplicateBlock` rewrites the id key (or the statement key) on the copy.
  - `CreateCSharp(template)` renames classes with a hardcoded list of 17 template class names (`MyScript`, `MyCombat`, `MyDiplomacyRules`, ... `MyReactions`) by regex so two scripts do not clash; the list is a manual duplicate of the template file.
- **Inspector** (`Studio.Inspector.cs`): a generic form from `FieldSpec` (bool tri-state combo "(not set)/yes/no", color picker that writes on release, filtered combos for country/location/character/choice, list with `+` popup, image field with Browse and drop-a-PNG, number/text/word), per-field text buffers keyed to the selected object, the entry's full script in a collapsible editor with Apply (parse check; must be exactly one block), a "world context" panel (a country's owned states and characters, a character's age, spouse and children) read straight from the live `Game`, plus location, state (name, owner, tax, manpower, category, locations, cores and claims checklists, paint and delete) and file inspectors. State edits write through `StateWriter`.
  - Numbers are validated only loosely (`Number` fields accept any string unless it contains whitespace and is not a double); Choice and Word accept anything typed.
- **World viewport** (`Studio.World.cs`): `Game.Load(gameDir, compileScripts: false, seed: 1)` into a `MapRenderer` + `CameraController` + `MapOverlay` rendered to a `RenderTexture2D` shown in ImGui (`rlImGui.ImageRenderTexture`); `Viewport.Origin/Width/Height` are set for the shared HUD code and `Gui.Scale = 1f` for labels. Tools: Select and Paint state (drag locations into the selected state; writes are batched in `_pendingStates` and flushed on mouse release/selection change). Map edits are applied to the live world first, then written to the store with `WorldEditSource` so they do not mark the world stale (`_worldOwnFiles`). `SyncCountries` copies names, colors and flags from unsaved country files into the running world without a reload. `Load save...` opens any `.sav` of the project into the editor world through `SaveGame.Load` (so the save format doubles as an editor input, keep it stable). Map modes: 5 built-ins (`Political, Terrain, States, Diplomacy, Locations`) plus the data-driven ones.
  - **Studio mutates Core objects directly** (`loc.State = target`, `s.Cores.Add`, `world.Countries.Add`, `CountryByTag[tag] = c`, `RefreshOwnership`, `RaiseMapChanged`) and has a placeholder-country hack in `CountryOf` that appends a `Country` with `Index = Countries.Count` and gray color to the live world for tags that only exist in unsaved files. This bypasses any invariants a rebuilt `World` would enforce (index stability, tag allocators in 5.x convergence). **Rebuild: a `WorldEditor` API in Core (move location, create state, set owner, create country) that the game, Studio and tools share, with undo.**
  - Delete state clears `l.State`/`l.Controller` on its locations and leaves them stateless (warning shown in the outliner: "N land locations have no state").
- **Features window and wizard** (`Studio.Features.cs`): reads `FeaturePackages.All()`, `Presets()`, `Bases()` (maps); category grouping; `Group` makes alternatives radio buttons (combat models, supply modes); `Requires` auto-selects dependencies and removal removes dependents (`RemoveWithDependents`); `Conflicts` uninstalls the alternative; "Add" and "Remove" confirm with the list of files; a "Use" button sets `combat_model` in defines; custom feature ids are added with no files (checked with `has_feature`); a project without `features.txt` is treated as "all built-in features" and offered "Convert to features". New project calls the `burger new` tool as a child process (`Engine.RunToolAsync`) with `--base`, `--preset`, `--features`. **The wizard and the CLI are two front-ends over the same Core package API, which is correct; the only issue is that Studio shells out to the CLI to create a project instead of calling the Core function.**
- **Console** (`Studio.Console.cs`): Problems (from `Check`, click to open file:line), Log (last 500, warnings only toggle), Output (Play/Export/tool stdout). **Project Settings** has four tabs: Game (`game.txt` name/version/author/description/icon/default country, combat model combo, "Taking land" 3 presets for `occupation_hours` 0/24/72, five game speeds), Rules (every `Defines.Known` row with default and help, custom keys appended), Formulas, Texts (all localisation keys, capped at 1500 rows).
  - **Formulas tab:** `ScriptValueSystem.Formulas` lists the 10 engine formulas replaceable by a same-named script value (`country_monthly_income`, `country_monthly_expenses`, `country_monthly_manpower`, `max_diplomatic_relations`, `character_yearly_death_chance`, `character_yearly_birth_chance`, `division_combat_power`, `division_org_recovery`, `location_war_value`, `base_opinion`), each with a starter template stored in a hardcoded dictionary in the Studio (`FormulaTemplates`). Replacements go to `common/script_values/engine_formulas.txt`; "Default" deletes the block. **This is the right, no-C# override mechanism and should become the primary override path for every rule that is one number.** Gap: only 10 hooks; the rules in 3.5 that are bare constants (flag thresholds, supply 0.75/0.4, AI draw) are not reachable this way.
  - Game speeds: five numbers with `0` meaning "as fast as possible". Default list `0.5 1.5 4 10 0` exists in Studio and in the defines (a duplicate default).
- **Code editor** (`Studio.Code.cs`): tabs, line-number gutter drawn by hand over an ImGui multiline input (the input is sized to its text and scrolled inside a child so the gutter and go-to-line can work), caret reported through an input callback, "Insert template" combo, "Script words" side panel (triggers, effects, values, iterators, scopes from the live `ScriptRegistry` of the last Check), "Check syntax" (parser errors), "Compile C#" (`ModManager.CompileCheck`, errors are clickable `file.cs:line:`). No syntax highlighting, no completion, no find, no undo beyond ImGui's, no multi-caret; for long C# the empty-state tip points to `scripts/Scripts.csproj` in an external IDE. **For the rebuild: do not grow this editor; give the engine a language server story (LSP for the script format, since the registry is the schema) and open files in the user's editor.**
- **Outliner** (`Studio.Outliner.cs`): World (countries with color swatch and owned states, states, characters), Mechanics, Content, Politics (only if any of its 4 kinds are installed), Population, Military and Terrain; search filter; `Singular(kind)` is a hand-written title switch ("Casus belli", "Windows & map modes" ...), another copy of naming data.
- **Shell** (`Studio.Shell.cs`): dock layout built through `igDockBuilder*` P/Invoke into `cimgui` (viewport and code top, inspector/outliner/settings right 26%, content browser and console bottom 30%); main menu (File, Project, View, Help), modal dialogs (prompt, prompt with choice, message, confirm; queued), project hub with Projects list, recent projects and the new-project wizard. Hotkeys: F5 Play, F7 Check, Ctrl+S Save all. The dock builder P/Invoke depends on the native `cimgui` exposing internal symbols (a build-time fragility).
- **Self-test** (`--selftest`, 157 lines): creates one asset of every kind, edits fields, duplicates, deletes, paints a state, installs backend C#, removes and re-adds `combat_eu4`, installs six military features, loads the game with C# compiled, asserts default backend classes (`GameWarfareRules`, `GameVisionRules`, `GameEconomyRules`, `GameDiplomacyRules`, `GameCharacterRules`, `GameAi`, `GameNavyRules`, `GameAirRules`, `GameSupportRules`), starts a new game as `TST`, ticks 60 days, and requires zero errors; writes `studio_selftest.txt`. This is a real end-to-end test of the editing pipeline and is the model for acceptance test 7.x "edit then load then simulate". It is not part of any `ctest`-style runner that I found; it runs only by hand.
- **`Ui.cs`**: gold/dim/red/green/yellow/blue constants, `T` (unformatted text, to avoid `%` format bugs), `Tip` with delay and stationary hover, `Help (?)`, `ComboFilter` (type to search or enter a new value; capped at 400 items), `TabItem` through `igBeginTabItem` unsafe call to allow programmatic selection, theme (gold accent on charcoal), file icons drawn with the draw list (Folder, TXT, C#, DOC, MAP, FILE; no icon font). `IconFor` maps extension to icon kind (`.txt` Script, `.cs`, images `png jpg jpeg bmp tga gif`, `md csv json yml`, `.map`). The Studio theme is separate from the in-game theme system (two theming systems).
- **`Program.cs`** (Studio): flags `--screenshot`, `--frames`, `--select`, `--open`, `--browse`, `--settings`, `--features`, `--selftest`, `--windowed`, `--size WxH`, project path; a crash writes `logs/studio_crash.log`. The screenshot flags are the editor's test hooks (good idea: keep).
- **Collected rebuild items from Studio:**
  1. One `BlockSpec` source of truth generated from module registration (fields, choices, help, templates, feature gate, naming).
  2. A Core `WorldEditor` API with undo, used by Studio and tools; no direct field mutation, no placeholder countries.
  3. `IShell` for explorer, recycle bin and "Windows" wording.
  4. Split the god class by panel with an explicit `EditorContext`.
  5. Create projects by calling Core, not by spawning the CLI.
  6. Per-file caching in `ProjectIndex`; stop reparsing everything per commit.
  7. Typed validation in the Inspector (numbers, choices, ids) using the same validator as `burger validate`.
  8. Make `--selftest` a registered test.
  9. Hardcoded English UI strings throughout; a localization pass is needed if the editor is ever localized.

### 5.13 Tools CLI (read in full)
- `Program.cs` (2,185 lines, partial class), `Figures.cs`, `Scorecard.cs`, `Templates.cs`, `PoliticalDepthTests.cs`. See 3.10.
- **Commands**: `new` (`--blank`, `--base isle|tiny`, `--preset`, `--features`; rejects names with spaces or invalid filename characters; copies the base `game/`, sets the name with `ScriptPatch.SetValue`, writes the backend README, installs features, writes an IntelliSense-only `Scripts.csproj`; with no flags it copies `projects/SampleGame`), `presets`, `list`, `features <project> [add|remove ids]`, `validate`, `run`, `export`, `generate-locations`, `docs`, `figure`.
- **`features`**: removal checks dependents first and removes dependents before requirements. A project with no `features.txt` means "every built-in system runs" and is saved as `FeatureSet.BuiltIn` on the first change. Keep this rule.
- **`export`**: validates first, then `dotnet publish` single-file self-contained (`win-x64` by default). Backend `.cs` files are compiled into the exe through `-p:BackendDir`. Copies `game/`, writes the player README.
- **`docs`**: generates `docs/game/SCRIPT_REFERENCE.md` from the registry. Keep: the registry is the single source of the reference.
- **`run`**: launches the exe on Windows, else `dotnet run`.
- **`ValidateSeeds`**: one process per seed, run in parallel, parses `SCORE|name|0/1/NA` text lines from stdout (hardcoded `char 124`). Replace the text protocol with a structured result file (JSON), but keep the `SCORE|` line as a compatibility output (section 7).
- **`PrintScorecard`**: reads `scorecard.txt` next to the game dir; verdicts ok/LOW/HIGH/n/a; weighted score plus "PRIORITY 2-5: n of m within band".
- **`PrintFeatureStats`**: about 15 per-feature diagnostic blocks that hardcode Earth1930 tags (GER, USA, SOV, JAP, ITA, FRA, ENG). Scenario leakage into the engine CLI. Move to scenario data (a `diagnostics` list in the project) or drop.
- **`Scorecard.cs` (`ScorecardRecorder : GameScript`)**: records wars at declaration (a war can end between monthly samples), samples monthly (pairs at war, rulers, alliances, civil wars), snapshots yearly on 1 January (owners, rulers, factories, alive, subjects, demilitarized states, techs), measures war outcomes after 24 months. Line kinds: war, war_result, war_holds, ruling, ruling_not, ruling_ever, exists, gone, owns, ideology_land, owns_country, alliance, alliance_at, independent, civil_war, countries, wars, world_war, tech, industry_rank, growth, remilitarized. Fields: `expect = lo-hi` (one number means plus or minus 0.2), `weight`, `priority` 1-5, `from`, `to`, `year`, `share`. Hardcoded: the great-power list, the `WarTable` big set, the 1930-1950 default window, and the `BURGER_WARSCORE_LOG` dump. Move all of these to the scorecard file.
- **Self-tests inside `Validate`** (the de facto test suite; port every one):
  - Hierarchy: `ScopeHierarchySelfTest` (generic scopes, cycles, reparenting, deterministic DFS, traversal-snapshot stability).
  - Parties and factions: `PartyScopeSelfTest`, `FactionSelfTest` (influence, law surcharge, grievance, placate, cabinet, splintering, AI negotiating).
  - Saves: round trips, a legacy-format save with the scope sections stripped, and a loop over every combat model.
  - Feature suites: `MilitaryFeatureTests`, `WarGoalTests`, `ReleaseTests`, `PoliticsFeatureTests`, `PoliticalDepthTests`, `PopulationFeatureTests`, `HistoricalFeatureTests`.
  - `PatchSelfTest` (comment-preserving edits, `#index` paths), `InterfaceSelfTest` (panel override, extend, disable), `FlagSelfTest` (determinism, diversity, successor behavior).
- **`PoliticalDepthTests`**: movement rising, organisation, grievance, crackdown odds, hardline and liberal factions, graded vote loss and collapse chance, persistence with old-save compatibility, interest groups and violence, puppet government control and the independence group, cohesion events and manpower share, and a 120-month boundedness run. Written against the rules API (`CoupChance`, `CivilWarChance`, `CanRevolt`, `CrackdownOdds`, `MovementProgress`, `MovementIncidentChance`, `GroupSatisfaction`, `SubjectGovernment`). Those method names are therefore part of the contract.
- **`Figures.cs` (`figure list|export|test`)**: acceptance tests for the model system. Body 1.80 m, 1,200-3,000 triangles, closed shells, volume 50-120 L, mirrored rig, weights summing to 1 per vertex. Gait: no foot slide above 4 mm in walk and run, feet on the ground. Clips loop without a jump. `.glb` round trip of mesh, rig and clips. Mod loading from `gfx/models` (`name.glb` is a model, `name.anything.glb` gives clips). Tank: 4.6-5.4 m long, 2.3-2.8 m wide, 2.0-2.4 m tall, 5 named joints plus per-side wheels and cleats, no track slip. Soldier: kit pieces skinned to body joints, uniform outfits, fire styles with muzzle events, both hands on the rifle, rifle variants, people variants sharing clips and rig, gear never more than 1.2 m from the body.
- **`Templates.cs`**: `WriteScriptsProject` (14 `Using`s), `PlayerReadme` (still says `game/localisation/`; a data-key contract, keep the path), and a 24-bit `Bmp` writer.
- **Problems**:
  - One huge static test class with a static `_failedChecks`; `Check()` prints [pass]/[FAIL]. There is no .NET test project (`COMMANDS.md` says so). Rebuild as a real test project (xUnit or NUnit), one test per `Check`, run through the CLI as well.
  - Heavy scenario coupling in tests (`RAJ`, `AAA`, `Earth1930`, `punjabi`, `communist`, `democratic`). Build small fixture games instead.
  - Diagnostics are scattered environment variables (`BURGER_AI_LOG`, `BURGER_DIPLO_LOG`, `BURGER_PEACE_LOG`, `BURGER_FACT_LOG`, `BURGER_WARCAUSE_LOG`, `BURGER_DECISION_LOG`, `BURGER_PURSUIT_LOG`, `BURGER_WAR_LOG`, `BURGER_WARSCORE_LOG`, `BURGER_AMB_LOG`, `BURGER_ALLY_LOG`, `BURGER_FOCUS_LOG`). Replace with the logger's named channels (build step 2) and keep the variables as aliases.
  - Profiling: `validate --profile` prints `max_day_ms`, `p99_day_ms`, `worst_days`. Keep.

### 5.14 Game entry
- `Program.cs` read in full. About 25 flags, `FindGameRoot`, `crash.log` next to the exe, six `--figure-*` modes. See 3.11.

### 5.15 Planning docs and reference docs (read in full)
- **COHESION_PLAN**: findings A-F and the Realm layer with domain events. Packages 1-7 done except the `pop_types` fixture. About 120 identical Earth1930 overrides were deleted; the AI backend is now an empty `GameAi` subclass setting `Parallelism = 1`. Lesson for the rebuild: a project backend overrides only what differs.
- **DECISIONS_PLAN**: Phase 1 (Memory) implemented. Phases 2-8 are plans only (Signals and Reactions, Major Decision syntax, Doctrine, Interview/Encounter, Betrayal, Chronicle, playthrough seeds). Decisions taken: doctrine inspectable but obfuscated, pause plus choice window, heavier voice for catastrophes, AI-vs-AI reactions. Design the event bus and memory so these phases can attach later.
- **HISTORICAL_AI_PLAN**: phases 0-4 (scorecard, historical start data, events, AI profiles and goals with `AiGoal`, threat model, `ai-report`, then balance). Scorecard and AI profiles are implemented. Four open decisions: the railroad dial (`historical_focus = strict|guided|free`), the scope of 1930, real names, the time horizon.
- **MODDING.md** (read lines 180-1035): the reference for the script language, the 17 hooks (`OnLoad` through `OnCountryEliminated`), the 10 replaceable formulas, the modifier keys the engine reads (`income upkeep manpower tax attack defense org_recovery speed opinion relations`), Defines, Export, and partial UI panel mods. Sections 4, 4b, 4b-2, 4c and 4d describe every system's data format and rules overrides; they were checked against the code read in 5.1-5.8 and are accurate except that Earth1930 `backend/Politics.cs` is said to need syncing with the engine (now subclassed, per COHESION_PLAN).
- **COMMANDS.md**: launch scripts (`burger.sh`, `BurgerEngine.sh`, `BurgerStudio.sh`, `WBE.sh`), `tools/zip.sh`, upstream patch automation (`tools/burger.sh`, `tools/git/burger.sh`, cherry-pick with `-X theirs`), `tools/check-rebase-push-main.sh`, Python map and asset tools under `tools/earth` and `tools/assets`, `compress-ui-bg`, output locations.
- **MODDING.md lines 1-180** (map, countries, characters): map images (`provinces.png` unique color per location, heightmap, terrain, trees, rivers), `map/locations.txt` (`HEX = name` or a block with `type = land|sea|lake|impassable`), states, `common/terrain.txt` (extra numeric keys stored for scripts), countries (3-character tags, `country_names` and `country_colors` that follow ideology and subject status, `set_country_name`/`set_country_color`/`set_flag_image` and their `reset_` pairs), characters with animated portraits (still, GIF, video via ffmpeg) and conditional `portraits` blocks. All are data contracts; keep the formats.
- **SCOPES.md**: generic scopes (`common/scopes`, `generic:<id>` and `scope_id:`), `kind`, `data` (numeric values copied to variables), `vars`, `flags`, parent as an organizational link only (no ownership or authority), forward references allowed, `scope_child`/`scope_descendant` iterators (depth-first, stable insertion order), `create_scope`, `set_scope_parent`, `clear_scope_parent`, cycle rejection, and the old `child`/`is_child_of` keeping the character-family meaning. Section 7 documents party factions (`weight`, `likes`, `dislikes`, `hardline`, `tolerance`, `post`; negotiate, favour, sideline, coalition invite, splintering, AI). Keep all of it; the self-tests in 5.13 enforce it.
- **TOOLTIPS.md**: tooltip rows take `[icon:<subject>]` and `[state:neutral|good|bad|warning|info|value]` tags (old `[+] [-] [!] [i] [=]` still map); unmarked rows are inferred from English text, which is the localization problem in section 8. Replace inference with explicit tags from the view-model layer. Icons map through `tooltip_icons` in `interface/theme.txt`; state colors through `colors`.
- **POLITICS_PANEL_ICONS.md**: eight overridable politics icon keys, and an optional `icon` per law that makes the choice button icon-only with the name moved to the tooltip.
- **RENDERING_PERFORMANCE.md** (in `docs/audits`): the Earth map's 1024x576 height mesh was the software-render bottleneck; a 256x144 render mesh built from the smoothed full-resolution heights gave 3.2 to 9.1 FPS, while `_heights` stays full resolution for `HeightAt`, world positions and picking. **Rebuild rule: render mesh resolution and height data resolution are separate.** `--perf` records 10-second windows (600-frame ring, p50/p95/p99, allocations, GC pauses, per-stage CPU times). No GPU timing is available from Raylib-cs.
- **UX_PERFORMANCE.md**: in the real-GPU log time sits in `EndDrawing` (flush, swap, VSync), not in the map or simulation; the stage is labeled `EndDrawing/flush+swap`. UI changes: wider action rail with 12 px labels, a permanent date with a PAUSE/LIVE badge, map-mode tooltips that explain built-in and custom ranges. Studio `--selftest` exceeded 90 seconds in that environment and was not reported as passing; the rebuild's Studio self-test must be fast.
- **README.md**: product pitch, quick start, controls (WASD, wheel, Space, 1-5, Tab, F1-F10, backtick console), folder layout, the feature table (military, combat_*, production, supply_*, division_designer, navy, air, battle_plans, lend_lease, politics, laws, cabinet, civil_wars, population, religion, social_classes, cores_claims, colonization, independence, buildings_*, fog_of_war, diplomacy, characters, character_interactions, economy, ai), game code (`game/scripts`, moddable) versus backend code (`backend/`, compiled into the exe), the Earth 1930 map (6144x3456, about 5300 land locations, 950 sea zones, 158 lakes, 1450 states, 74 countries, built by `tools/earth/make_earth.py`). The README feature table is out of date against `features/` (ai_profiles, technology, commodities, demilitarization, focus, memory are missing from it): generate it from the feature packages.
- **Docs drift**: the plans partly describe implemented work as future work; mark status at the top of each in the new repo.

---

## 6. Build order for the rebuild

1. **Script language**: parser, nodes, writer, patcher, with round-trip and comment tests (1.1, 1.2).
2. **Kernel**: clock, RNG service with named streams, event bus, profiler, logger with channels and `file:line`, save-section registry, path service.
3. **Data loading** with registered definition types, defines with help text, localisation.
4. **Map loader** and world model (locations, states, countries, characters, generic scopes).
5. **Script engine** and validator. Register core triggers, effects, values, iterators and links. Port script values, resources and memory here (they are the cleanest and the others use them).
6. **Module system** and feature packages. Port military first, then diplomacy (start with `Relations`), economy, characters, AI.
7. **Saves**, then the console and the CLI (`validate`, `generate-locations`).
8. **Renderer** (map, labels, overlays) and the HUD (registry-driven menu and view models).
9. **Model system** (four layers), then replace soldiers and tanks. Procedural flags can come with this step.
10. **Mod layer** (`scripts/`, `backend/`, export).
11. **Editor**.
12. Remaining features: politics, population, production, navy, air, technology, and the rest.

Check after each step: load the Earth 1930 game and a small sample, run the validator, save and load, and compare a seeded run against the old engine's run where the rules are unchanged.

---

## 7. Acceptance tests ("the rebuild is the same engine")

- Every file in a current `game/` folder loads with no new errors.
- `burger validate` gives the same results on the sample games.
- The same seed gives the same game, with the AI serial or parallel, and with combat repeated.
- A save made by the new engine reloads to an identical state.
- A comment-heavy data file edited by the editor keeps every comment.
- A C# mod written for the old `GameScript` API compiles unchanged.
- An old `.glb` in `gfx/models` still loads and animates.
- The same flag identity gives the same flag bytes as before.
- All 10 overridable script-value formulas still replace their engine formulas.
- The scorecard output line `SCORE|name|value` is unchanged.

---

## 8. Debt that is easy to miss
- Many docs and messages carry British spellings. Fix in new text only. Do not mass-rename data keys.
- Hand-made caches (border, reachability, path scratch, node lookup, personality cache of 30 days, military strength cache per day) each need an owner and an invalidation rule.
- Several `Rules` classes hold literals that look like tuning but are not in defines. Run a "literal sweep" (search for number literals in `*Rules.cs`) as the first task of each module port.
- `Media` caches by an unstable `GetHashCode` key; use a real key (path plus mtime).
- Studio calls Windows-only APIs in places; wrap behind a platform service.
- Tools hardcode Earth1930 tags (`PrintFeatureStats`, `Scorecard`, self-tests); move to data.
- Localization: tooltips infer meaning from English keywords, font codepoints are fixed, and many English strings are hardcoded (including the Studio's). Plan a string table before UI porting.
- Static state: AI `Parallelism` is static, and `FlagTraditions` registers into static dictionaries behind `lock`. Make both instance state of the game, so two games can run in one process (tests).

---

## 9. Reading status (honest)

| Group | Status |
|---|---|
| Script language, trigger/effect engine, validator, `Game.cs`, data loaders and defines, feature and mod system, modifiers, model system | Read in full (earlier passes) |
| Politics: `PoliticsSystem`, `PoliticsRules` | Read in full |
| Politics partials: Unrest, Parties, Factions, Factions.Actions, Laws, Parliament, Pops, Script, `PoliticsModel` (which holds the parliament models too) | Read in full |
| Population: `PopulationSystem`, `PopulationRules` | Read in full |
| Population: `PopulationModel`, Independence, Cores, Script | Read in full |
| Diplomacy: `DiplomacySystem`, `DiplomacyRules`, `Relations` | Read in full |
| Diplomacy partials: WarGoals, Demilitarization | Read in full |
| Characters, Production, Commodities, Economy, Supply, Technology | Read in full |
| Navy, Air, Events, Decisions, Focuses, Resources, Statistics, ScriptValues, Memory, Particles | Read in full |
| AI: `AiProfiles`, `AiSystem` | Read in full |
| Warfare: `WarfareRules`, `CombatModels`, `Designer`, `Vision` | Read in full |
| Warfare: `WarfareSystem` (1106), `Support` (258) | Read in full |
| Flags: `FlagGenerator`, `FlagIdentity` | Read in full |
| Flags: `FlagFamilies`, `FlagPlan`, `FlagEmblems`, `FlagKit`, `FlagEvaluator` | Read in full |
| Game: `Hud.cs`, `Program.cs`, `Hud.Mechanics.cs`, `Interface.cs` (first 200), `Hud.Menu.cs` (first 200), `Gui.cs` (first 330) | Read |
| Game: the other 19 `Hud.*` panel files (Politics, Population, Characters, Panels, Observer, Wars, Designer, Military, Production, PopMood, Air, Support, Navy, Editor, Menus, Focus, Technology, Console, Popups) | Read in full |
| Game: `Gui.cs` (all), `MapRenderer`, `App.cs`, `CameraController` | Read in full |
| Game: `CountryLabels`, `MapOverlay`, `MapFigures`, `MapParticles`, `FigureRenderer`, `Media`, `Assets`, `Shaders`, `UiState`, `Viewport`, `UserSettings`, `RenderPerf`, rest of `Interface.cs` and `Hud.Menu.cs` | Read in full |
| Studio: all 20 source files (`Studio.*` partials, `Ui`, `Catalog`, `Program`, `Project/` `ProjectStore`, `ProjectIndex`, `Specs`, `Templates`, `Engine`) | Read in full |
| Tools: `Program.cs`, `Figures`, `Scorecard`, `PoliticalDepthTests`, `Templates` | Read in full |
| Docs: UI audit, `COMMANDS.md`, the three plans, `MODDING.md` lines 180-1035 | Read in full |
| Docs: `MODDING.md` lines 1-180, `SCOPES.md`, `TOOLTIPS.md`, `POLITICS_PANEL_ICONS.md`, `RENDERING_PERFORMANCE.md`, `UX_PERFORMANCE.md`, `README.md`, head of `SCRIPT_REFERENCE.md` | Read in full (`SCRIPT_REFERENCE.md` is generated by `burger docs`; only its head was read) |

Every source group and every doc is now read. The one exception is the body of the generated `SCRIPT_REFERENCE.md`, which `burger docs` rebuilds from the registry.

---

## 10. Open points (resolve before freezing the lists)

1. ~~Politics, Population and Diplomacy partials~~ now verified (5.1-5.3). They confirm a Realm/domain-event layer, and that civil war (Politics) and independence war (Population) are the same flow that must be one module.
2. ~~`WarfareSystem.cs`~~ read (5.5).
3. ~~Flags internals~~ read (5.10).
4. ~~HUD panel files~~ read (5.11).
5. ~~Studio panels and Tools commands~~ read (5.12-5.13). Every command and panel above survives; `PrintFeatureStats` is the one candidate to drop.
6. Items that were here about legacy mod overloads, the `TechSystem` expression, the Statistics `catch` and shared-RNG use in Diplomacy and Peace are dropped. The rebuild already covers them: legacy overloads are removed (3.1), the `TechSystem` expression is replaced (3.5), and named RNG streams make the thread question moot (3.7).
7. **American aliases** (the one real decision left): default is to ship American-spelled aliases for British data keys, on, with the British keys still accepted (section 3.16).
