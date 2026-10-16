# World500BCE scenario

`projects/WrektMap` is the REAL 500 BCE scenario, copied from `World500Test` (which stays unchanged).

- 965 states: clusters of land locations per people and landmass. Names are placeholders. Some land is left without a state; an owner is not required.
- 58 countries: 20 original (ACH, MAC, CAR, NUB, SAB, OLM, CHV, IND, ZHO and the Greek city-states ATH, SPA, COR, ARG, THB, THS, CRT, NAX, CRO, SYR, PNT) plus 38 added (see docs/game/countries-500bce.md). Other states are unowned (tribal peoples). `default_country = ACH`.
- 113 cultures, 59 religions, 7 social classes. Starting people: 149.2M.
- Features: `population`, `religion`, `social_classes`, `cores_claims`.

## Limits
- Totals and densities are my own design estimates in people per km2, not sourced census data.
- Region shapes are my own lon/lat geometry. Reference maps (see `docs/SOURCES/maps-500bce/`) supplied facts only; no geometry was copied.
- Land outside a painted region takes the nearest region's people at 60% density, within 25 degrees.
- One country stands for each large power; the Greek poleis are one country each (see docs/game/countries-500bce.md).
- The political map and Reddit screenshots are not stored; the licenses of several references are unverified.

## The `World500BCE` project: no south pole

A second project cut from the same map. The images are cropped to the top 6848 rows (down to 60.47 S), so Antarctica and the sea around it no longer exist; the 42 `ice_` and 119 southern sea locations are removed. `map/default.map` has `lat_north = 90` and `lat_south = -60.47` so area math stays correct for the shorter map, and `game.txt` has `globe_mode = no`. A map that does not cover the whole planet cannot use the globe, so the engine switches the globe off for it anyway.

## Development, institutions and tech groups

Feature `development` (in `features.txt`; research needs `technology`). Systems live in `src/Burger.Core/Development/DevelopmentSystem.cs`.

- **Development level** per state (1 to 30). The start level is `1 + round(tax*2)`. Spending political power (command `develop`, multiplayer-safe) adds a level. Cost: `development_cost_base` (50) + `development_cost_per_level` (10) x level, scaled by the state modifier `development_cost`. Only levels gained above the start level give bonuses: tax `development_tax_per_level` (.05), manpower `development_manpower_per_level` (.03), production room `development_room_per_level` (2). The AI spends political power above a 100 reserve. Define `development_resource` to change the currency (OPEN: hooks for the other spec resources are not wired).
- **Literacy and urbanization** per state: start from clergy, noble and artisan pop shares, grow monthly, and are boosted by institutions. Schools (clergy + half nobles at least 2.5%) and markets (artisans at least 6%) are derived from pop shares, since 500BCE has no such buildings.
- **Institutions** (`common/institutions/*.txt`): appear on a date in an origin state, then spread to neighbors (adjacent states and across up to 2 sea provinces) at `spread` per month, gated by `requires` (literacy, urbanization, development). Monthly gain = rate x best neighbor presence x receptivity (0.1 to 1, from urbanization or development level), so towns and cores adopt fast, tribal and steppe lands slowly. A state adopts at presence 1. Adopted institutions give the listed state modifiers and cut the cost of techs that name them with `institution`. Shipped set: alphabetic_writing, iron_working, coinage, philosophy, imperial_roads. OPEN: origins, dates and spread rates are estimates.
- **Tech groups** (`common/tech_groups/*.txt`): three neighbors of the same group that know a tech make it 10% cheaper. `history/technologies.txt` gives each country a `lag` in years.
- **Techs** (`common/technologies/*.txt`): 23 techs, -2000 to -400, costs 120 to 560. OPEN: balance against research points is unchecked.
- **Map modes**: Development (slate to gold) and Institution (one entry per appeared institution; Tab cycles). The location panel shows the development bar, the +1 button, literacy, urbanization and institutions.
