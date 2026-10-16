# World500BCE scenario

`projects/WrektMap` is the REAL 500 BCE scenario, copied from `World500Test` (which stays unchanged).

- 965 states: clusters of land locations per people and landmass. Names are placeholders. Some land is left without a state; an owner is not required.
- 20 countries (ACH, MAC, CAR, NUB, SAB, OLM, CHV, IND, ZHO and the Greek city-states ATH, SPA, COR, ARG, THB, THS, CRT, NAX, CRO, SYR, PNT). Other states are unowned (tribal peoples). `default_country = ACH`.
- 113 cultures, 59 religions, 7 social classes. Starting people: 149.2M.
- Features: `population`, `religion`, `social_classes`, `cores_claims`.

## Limits
- Totals and densities are my own design estimates in people per km2, not sourced census data.
- Region shapes are my own lon/lat geometry. Reference maps (see `docs/SOURCES/maps-500bce/`) supplied facts only; no geometry was copied.
- Land outside a painted region takes the nearest region's people at 60% density, within 25 degrees.
- One country stands for each large power; the Greek poleis are one country each (see docs/countries-500bce.md).
- The political map and Reddit screenshots are not stored; the licenses of several references are unverified.

## The `World500BCE` project: no south pole

A second project cut from the same map. The images are cropped to the top 6848 rows (down to 60.47 S), so Antarctica and the sea around it no longer exist; the 42 `ice_` and 119 southern sea locations are removed. `map/default.map` has `lat_north = 90` and `lat_south = -60.47` so area math stays correct for the shorter map, and `game.txt` has `globe_mode = no`. A map that does not cover the whole planet cannot use the globe, so the engine switches the globe off for it anyway.
