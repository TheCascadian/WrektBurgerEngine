# BurgerEngine documentation

This directory follows the documentation layout of
[`BurgerKingHotline/BurgerEngine`](https://github.com/BurgerKingHotline/BurgerEngine/tree/main/docs).
The three canonical engine documents retain their upstream names so they remain easy to compare and merge:

## Engine documentation

- [Modding guide](MODDING.md) — project layout, data files, scripting, C# extensions, UI and export.
- [Script reference](SCRIPT_REFERENCE.md) — generated triggers, effects, values, scopes and iterators.
- [1930 historical AI plan](PLAN_1930_HISTORICAL_AI.md) — the Earth1930 project plan and rules.

## WrektBurgerEngine additions

These documents describe functionality, audits and measurements specific to this repository. They do not replace the
upstream engine documents.

- [Command reference](COMMANDS.md) — build, launch, validate, profile and repository workflows.
- [Scopes guide](SCOPES.md) — generic scopes, hierarchy, data, variables and script usage.
- [UI modding audit](UI_MODDING_AUDIT.md) — current UI modding surface, boundaries and verification status.
- [Rendering performance](rendering-performance.md) — terrain, rendering and present/VSync investigation.
- [UX performance follow-up](ux-performance-followup.md) — frame-time interpretation and UX validation.

## Documentation rules

- Put engine-wide user and modding behavior in the canonical documents above.
- Put repository-specific investigations, benchmark results and audit status in the Wrekt additions.
- Keep generated references synchronized with `burger docs`.
- Link screenshots from the relevant audit or performance report; keep large image evidence under `docs/img/`.
- When upstream changes, compare the canonical files against `upstream/main` before editing them locally.
