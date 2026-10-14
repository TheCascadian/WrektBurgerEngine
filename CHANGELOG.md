# Changelog

## 2026-10-14
- Added opt-in historical geography: stable location atoms, dated provinces with split, merge and redraw, lineage, temporal properties, validation, runtime save section `x_geography`, `burger geo` tools and self-tests. See docs/game/GEOGRAPHY.md.

## World500BCE scenario
- Added `projects/WrektMap`: 965 states, 10 countries, 113 cultures, 59 religions, 149.2M starting people (design estimates). `World500Test` unchanged. See `docs/game/WORLD500BCE.md`.

## World500BCE: cut map

- `map/default.map` keys `lat_north` / `lat_south` (degrees, default 90 / -90) say which latitudes the images cover; spherical areas use them. A map that is not the whole planet never offers the globe.
- `game.txt` key `globe_mode = no` hides the globe toggle.
- New project `projects/World500BCE`: the map cut off at 60.47 S (no Antarctica), flat only.

## Steadier country labels

- A name is only refitted when its own land, owner or text changes; any other map change leaves it exactly where it was.
- A refit that keeps the old curve inside the land and the letters nearly as big is skipped (a province changing hands elsewhere no longer nudges the name).
- A refit that does move a name glides to the new curve over 0.7 s instead of jumping.
- Letters follow unsnapped, smoothly blended directions, so they no longer step or flip angle at every corner while the camera moves.
