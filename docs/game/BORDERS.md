# Border claims

Who held which land on which date, written as plain text with a source for every line. You edit text files; the engine compiles them. Nothing here changes gameplay yet: the compiled result only feeds the historical province layer (see [GEOGRAPHY.md](GEOGRAPHY.md)) and the Studio **Time** map mode.

## Files

| File | You edit it? | What it holds |
|---|---|---|
| `map/areas.txt` (and `map/areas*.txt`) | yes | Named groups of locations. `attica = { land_00012 land_00013 }`. Areas must not overlap. A later file replaces an earlier area of the same name. |
| `history/borders/<TAG>.txt` | yes | The claims of one polity. The top block must be named after the file. |
| `history/geography/_generated_borders.txt` | **no** | Written by `burger borders build`. One province per area, with `owner`, `control` and `claim_source` tracks. |

## Claim file

```
ATH = {
	-700 = { add = { attica } control = core source = pleiades note = "Attic towns" }
	-480 = { remove = { attica } control = unknown source = generated }
}
```

- A date key is any date the engine reads: `-700` (astronomical), `500BCE.1.1`, `1CE`.
- Keys inside a date block: `add`, `remove` (lists of area names), `control`, `source`, `note`. Anything else is an error. `source` is required. `control` is required when you `add`.
- Control levels: `core`, `direct`, `tributary`, `claimed`, `contested`, `unknown`.
- `source` is an `id` from `tools/map500bce/provenance/sources.csv` whose status is exactly `APPROVED`, or the word `generated`. `generated` means a terrain-driven placeholder, not attested history. It is labeled that way in build output and in the viewer. Candidates (`PENDING OWNER`), `UNVERIFIED` and `REJECTED` rows are refused, as is anything on `denylist.txt`.
- Facts may come from a source; the geometry (the areas) is ours. Do not copy another dataset's lines.

Every mistake is a hard error naming the file and line: unknown key, bad date, unknown area, missing or unapproved source, unknown control level.

## Replay

Claims run by date. On one date every `remove` runs before any `add`, so a handover on the same day works whatever the file order is. Inside one group, file order (sorted by name) then line order decides. A later `add` wins, but adding an area another polity still holds is reported as an **overlap** error: remove it from the old holder first.

## Commands

```
burger borders areas <project> [--claims] [--force]   make map/areas.txt from map/states (cut into land-connected pieces); --claims also writes placeholder claim files
burger borders build <project> [--year Y] [--ledger file]
burger borders test
```

`build` checks everything, writes `_generated_borders.txt` (same input gives the same bytes), and prints coverage per claim date: the share of land area (spherical area of each location) that is **attested** (owned, real source), **generated** (owned, `generated`) or **unknown** (unowned, or in no area). With `--year` it prints that date only and also writes a state snapshot to `map/states_snapshots/`, which the engine does not load. If any error is found nothing is written. The source ledger is `--ledger`, else `<project>/provenance/sources.csv`, else the repo's `tools/map500bce/provenance/sources.csv`; with no ledger only `generated` is accepted.

`burger geo <project> --date <date>` then validates the generated provinces (contiguity, no land and water mixed).

## Viewing

In Burger Studio pick the **Time** map mode. A year box and slider (500 BCE to 2020 CE; the box takes any year) choose the date. Land is colored by owner on that date, unclaimed land is gray, and land held with control other than `core` or `direct` is hatched. Hover or click a location to see its area, polity, control and source.

## World500BCE pilot

Its 969 states became 1,219 areas (a state with parts that do not touch by land is cut into `name_1`, `name_2`). Each owner got a claim file dated 500 BCE with `control = unknown source = generated`: the ownership comes from the project's existing states, not from a cited source. None of it is attested history.
