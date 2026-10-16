# BurgerEngine documentation

Documents are grouped by purpose. The canonical engine guides keep their upstream names (see
[`BurgerKingHotline/BurgerEngine`](https://github.com/BurgerKingHotline/BurgerEngine/tree/main/docs)) so they stay easy to compare and merge.

## [game/](game/): game documentation

How to make, run and extend a game. Describes behaviour that exists today.

- [Modding guide](game/MODDING.md): project layout, data files, scripting, C# extensions, UI and export.
- [Script reference](game/SCRIPT_REFERENCE.md): generated triggers, effects, values, scopes and iterators (`burger docs`).
- [Scopes guide](game/SCOPES.md): generic scopes, hierarchy, data, variables and script usage.
- [Command reference](game/COMMANDS.md): build, launch, validate, profile and repository workflows.
- [Historical geography](game/GEOGRAPHY.md): dated provinces, splits, merges and lineage from 500 BCE.
- [Tooltips](game/TOOLTIPS.md): tooltip system.
- [Modding API](game/modding-api.md): the C# mod surface.
- [Model prompt](game/llm-model-prompt.md): reusable prompt for building a model file.
- [World 500 BCE](game/WORLD500BCE.md): the shipped scenario.
- [Countries 500 BCE](game/countries-500bce.md): every country, its history, gear and the all-countries list by founding date.

- [Politics panel icons](game/POLITICS_PANEL_ICONS.md): icon set for the politics panels.

## [multiplayer/](multiplayer/): multiplayer and security

- [Multiplayer](multiplayer/multiplayer.md): lockstep sessions, join codes, UI.
- [Multiplayer security](multiplayer/multiplayer-security.md): threat model and mod sandbox.
- [Channel spec](multiplayer/channel-spec.md): TLS and key confirmation (UNREVIEWED).
- [External review plan](multiplayer/external-review-plan.md): what to hand to a reviewer.
- [OS sandbox evaluation](multiplayer/os-sandbox-evaluation.md): separate-process mod sandbox, evaluated.
- [Relay](multiplayer/relay.md) and [Steam](multiplayer/steam.md).

## [plans/](plans/): plans (not yet or only partly implemented)

- [Decisions, agency and identity](plans/DECISIONS_PLAN.md), [1930 historical AI](plans/HISTORICAL_AI_PLAN.md), [Cohesion](plans/COHESION_PLAN.md), [Timeline design](plans/TIMELINE_DESIGN.md), [Empire collapse model](plans/EMPIRE_COLLAPSE_MODEL.md), [500 BCE map sources](plans/MAP_500BCE_SOURCES.md).

## [audits/](audits/): audits and measurements

Findings about the current state, with evidence. Screenshots live beside the audit that cites them.

- [UI modding audit](audits/UI_MODDING_AUDIT.md), [Rendering performance](audits/RENDERING_PERFORMANCE.md), [UX performance](audits/UX_PERFORMANCE.md), [Geography pixel audit](audits/GEOGRAPHY_PIXEL_AUDIT.md), [Specialization stress test](audits/specialization-stress-test.md).

## Other

- [flowchart/](flowchart/): interactive system flowcharts (`index.html`; `v2`-`v4` are older versions); `serve.sh` serves them locally.
- [logs/](logs/): dev logs ([2026-10-14](logs/DEVLOG-2026-10-14.md): globe, wrap, clamp, clip-plane fix).
- [SOURCES/](SOURCES/README.md): saved data-source, license and reference-paper copies, grouped by subject.

## Rules

- Game docs describe what exists; plans describe what is intended; audits describe what was measured. Move a plan to `game/` once it ships.
- Images go in an `img/` folder next to the document that cites them.
- Keep generated references in sync with `burger docs`.
- When upstream changes, compare the canonical files against `upstream/main` before editing them locally.
