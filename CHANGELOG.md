# Changelog

## 2026-10-15
- Map cache: loading a map now keeps its slow products (province, terrain, height, tree and river arrays) in `<game>/cache/map/`. Each entry is keyed by a SHA-256 of the content of its source files and settings, so any change to a map file rebuilds just that entry. A game load of World500BCE drops from about 6.6 s to 2.5 s. Delete the folder or set `BURGER_NO_MAP_CACHE` to bypass it; it is git-ignored.
- Faster start (World500BCE, warm: about 9.4 s to about 2.6 s). More cache entries in `<game>/cache/`: map geometry, border scan, coast distance, terrain atlas, and the compiled C# backend and scripts (`cache/scripts/`, keyed by source text and engine build). The renderer's CPU work (atlas, rivers, terrain blend, map-data image) starts on worker threads as soon as the map is loaded, while the rest of the game still loads (`Game.MapLoaded`, `MapRenderer.Prewarm`). The log shows "Map renderer built in N ms".
- `--monitor N` (or `BURGER_MONITOR=N`) opens the game full screen on monitor N (0 = first); for testing on the second monitor.
- Main Menu (from the pause menu) no longer rebuilds the GPU map: the renderer is kept when the map size and locations match. "Reload files" and Retry still rebuild everything.
- Starting the game with a project folder (not its `game/` subfolder) now finds `game/` on its own. The load-error panel layout was fixed.
- Determinism: the shared `Game.Rng` is gone. `Game.Rngs` (`RngService`) gives every system its own named, seeded stream (`combat`, `events`, `characters`, `navy`, `politics`, `population`, `decisions`, `focus`, `tech`, `diplomacy`, `script`, `ai:TAG`). Inside a country AI tick all systems draw from that country's stream, so serial and parallel runs match. Stream positions are saved in `x_rng`. Seeded games give different worlds than before, but the same seed now always gives the same world. Mods: use `game.Rngs.<Name>` instead of `game.Rng`. New `burger validate-par <project>` checks serial == parallel (`projects/World500BCE_PTest/scorecard.txt`).
- Soldier and tank figures now stand on a soft circular shadow that lies on the slope under their feet. `FigureShadows` picks the shadow per model (`FigureShadows.Set(modelKey, shadow)`); implement `IFigureShadow` (it gets the pose, placement, up vector and camera) for a per-model or cast shadow. `FigureShadows.Enabled = false` turns them off; `BlobShadow` has `Size`, `Strength`, `Lift` and `Length`.
- Save and load are now exact: a game saved at any day and loaded into a fresh game plays on identical to the uninterrupted game. Fixed: saves rounded numbers to 4 decimals (now round-trip `"R"`), party popularity was re-normalized and rebuilt on load, the political modifier and wealth caches went stale (now rebuilt hourly and daily, under a lock), vacant parliament seats were not saved, and the game seed was not restored from the save (it seeds the parliament draws). `WarfareSystem` front hashes no longer use `HashCode.Combine` (random per process).
- `burger validate-par` now also plays a fourth world, `reload`: it saves mid-run, loads into a fresh game and plays on; scorecard lines `serial_eq_reload_*` check serial == reload. `World500BCE_PTest` is busier (imperator combat, laws, civil wars, independence, AI profiles, navy, country politics and diplomacy history): 104 of 104 lines within band over 8 seeds and 3650 days.
- Dynamic provinces: a base game system (`GeoDynamics`) that ticks monthly with the simulation on any project with a geography layer. Provinces split along fronts when a war occupies part of them, split in two balanced halves when their estimated population passes `split_population`, and merge back when the front disappears or the people dwindle. Changes are normal runtime operations (lineage, conservation, saved with the game) and never touch provinces that have authored events still to come. Tune or turn off in the geography `settings` block (`dynamic`, `split_population`, `merge_ceiling`, `min_age_days`, `front_min_age_days`, `max_changes_per_month`).
- Console: `geodebug` (highlights dynamic provinces), `geolist` (what changed), `geowar [n|off]` (test occupation), `geoskip [months]` (run the sim forward and report changes). Tool: `burger geo simulate <project> --years N [--war] [--split-population P]` runs headless and checks determinism, validity and save/load.
- Console: `geobase [on|off]` overlays the start-layout province borders (magenta) above the current province shapes.

## 2026-10-14
- Added opt-in historical geography: stable location atoms, dated provinces with split, merge and redraw, lineage, temporal properties, validation, runtime save section `x_geography`, `burger geo` tools and self-tests. See docs/game/GEOGRAPHY.md.
- Optional normal map: `map/normal.png` (tangent space, x east, y north, z up; name set by `normalmap` in `default.map`) adds fine hill shading. A map without one renders exactly as before. The earth1930 and isle bases now include one, made by `tools/earth/make_normal.py`.
- Map shader: bare rock on steep slopes, snow above a snow line that drops towards the poles, sky and sun light with a warm sun, and a soft distance haze on the flat map. Terrain mesh is now 3072 vertices wide by default (`mesh_width`), cut into chunks that are skipped when out of view.
- World500BCE: exaggerated heightmap, `height_scale = 7`, normal map, new seamless 2048 terrain textures.
- Terrain textures: the atlas cell follows the largest texture, capped at 1024 (never scaled lower), with a wrapped border so tile edges and mipmaps stay clean. Terrain types blend over a wider neighborhood with noise, so edges are no longer pixel stairs.
- Far-view speedup: zoomed out the terrain draws from a quarter-resolution mesh, the terrain blend collapses to one mip-mapped texture lookup, water closeness reads one texel, and MSAA switches off (it stays on up close). The continuous benchmark went from 56.7 to 146.5 fps average.
- Near-view speedup: the terrain blend kernel is baked once into two half-resolution weight textures (up to 8 terrain types; larger sets keep the old per-pixel loop), and the water normal uses an exact one-pass slope instead of three evaluations. Benchmark average 146.5 to 185.6 fps.
- Water is now part of the terrain mesh and shader instead of a flat tile (ported crossing-swell water shader): the open ocean is unpredictable (warped swells), lakes are completely still, coasts are calm and muted with a smooth blend from the open sea. Sea-level vertices lift with the waves; shallows show a sandy bed. Rivers read a half-resolution river texture. `--lookat lon,lat` (with `--zoom`) aims the camera for screenshots.

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
