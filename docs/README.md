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
- [Politics panel icons](game/POLITICS_PANEL_ICONS.md): icon set for the politics panels.

## [plans/](plans/): plans (not yet or only partly implemented)

- [Decisions, agency and identity](plans/DECISIONS_PLAN.md): Ledger, Doctrine, weighty decisions, reaction engine, interviews, betrayal, chronicle.
- [1930 historical AI](plans/HISTORICAL_AI_PLAN.md): Earth1930 AI project plan and rules.
- [Comprehensive cohesion](plans/COHESION_PLAN.md): making puppets, subjects and politics work with every other system.

## [audits/](audits/): audits and measurements

Findings about the current state, with evidence. Screenshots live beside the audit that cites them.

- [UI modding audit](audits/UI_MODDING_AUDIT.md): UI modding surface, boundaries and verification status.
- [Rendering performance](audits/RENDERING_PERFORMANCE.md): terrain, rendering and present/VSync investigation.
- [UX performance](audits/UX_PERFORMANCE.md): frame-time interpretation and UX validation.

## Tooling

- [flowchart/](flowchart/): interactive system flowcharts (`index.html`, `v2`-`v4`); `serve.sh` serves them locally.

## Rules

- Game docs describe what exists; plans describe what is intended; audits describe what was measured. Move a plan to `game/` once it ships.
- Keep generated references in sync with `burger docs`.
- When upstream changes, compare the canonical files against `upstream/main` before editing them locally.

## Logs

- [Dev log 2026-10-14](DEVLOG-2026-10-14.md): globe, wrap, clamp, clip-plane fix.
- [Sources](SOURCES/README.md): saved data-source and license pages, grouped by subject.
