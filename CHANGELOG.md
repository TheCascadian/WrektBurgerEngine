# Changelog

## 2026-10-14
- Added opt-in historical geography: stable location atoms, dated provinces with split, merge and redraw, lineage, temporal properties, validation, runtime save section `x_geography`, `burger geo` tools and self-tests. See docs/game/GEOGRAPHY.md.
- Optional normal map: `map/normal.png` (tangent space, x east, y north, z up; name set by `normalmap` in `default.map`) adds fine hill shading. A map without one renders exactly as before. The earth1930 and isle bases now include one, made by `tools/earth/make_normal.py`.
- Map shader: bare rock on steep slopes, snow above a snow line that drops towards the poles, sky and sun light with a warm sun, and a soft distance haze on the flat map. Terrain mesh is now 3072 vertices wide by default (`mesh_width`), cut into chunks that are skipped when out of view.
- World500BCE: exaggerated heightmap, `height_scale = 7`, normal map, new seamless 2048 terrain textures.
- Terrain textures: the atlas cell follows the largest texture, capped at 1024 (never scaled lower), with a wrapped border so tile edges and mipmaps stay clean. Terrain types blend over a wider neighborhood with noise, so edges are no longer pixel stairs.

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
- A name that is too big (letters over about 12% of the window height, gone at 24%) or mostly off screen (under 30% of its curve visible, full at 65%) fades out. It no longer stops growing at a fixed size.
