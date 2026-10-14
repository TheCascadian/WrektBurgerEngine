# Historical geography

An opt-in layer that gives the world a province history from 500 BCE onward. It is inert unless a project has `history/geography/*.txt`.

## Model
- **Location** is the permanent atom. Its key is the stable id. Locations never change identity.
- **GeoProvince** is a playable province: an immutable set of atoms with a lifetime `[From, To)`. Atoms of a province never change; history is made by retiring provinces and creating new ones.
- Reorganizations: **split** (1 to n), **merge** (n to 1), **redraw** (n to m). Retired ids are never reused. Lineage is kept as parents and children.
- Political data (owner, region, name, any property) lives in dated timelines keyed by province id, apart from the physical atoms.

## Dates
The canonical date is `GameDate.TotalDays` (days since 1.1.1) with astronomical years: year 0 is 1 BCE, 500 BCE is -499. Text with a `BCE` or `CE` suffix rejects year 0; plain numbers are astronomical. `HistoricalCalendar.Forever` and `Beginning` are the open ends.

## Property carry-over
| Kind | Rule |
| --- | --- |
| extensive | split by area weight (integers by largest remainder), totals conserved; merges sum |
| intensive | area-weighted average |
| categorical | plurality by area, ties broken ordinally |
| set | union |

An explicit value on the effective day wins. `share` is allowed only on a single-parent split.

## Files
`history/geography/*.txt` hold provinces, operations (`split`, `merge`, `redraw`) and tracks, replayed deterministically by date and then file order. Validation rejects overlapping lifetimes, id reuse, missing atoms, unconnected provinces and corner-only borders (fewer than 2 shared pixels). Strait links only join an `archipelago` province.

## Saves
Operations made at runtime are saved in the `x_geography` section. The section is registered only when geography exists, so old saves load unchanged and saves from geography projects load in older builds (the section is ignored).

## Commands
- `burger geo` - report on a project.
- `burger geo test` - self-test and scale test.
- `geo <location>` in the console - province, lineage and owner today.

## Not included
No renderer, editor or map-mode integration, and no historical dataset. Gameplay still runs on Locations and States.
