# Sites: procedural location models

Large locations get a 3D settlement built from kit pieces. First kit: `latin` (Rome = city tier, Latium = town tier).

![town](img/site-latin-town.png)
![city](img/site-latin-city.png)
![Rome in game](img/site-rome-ingame.png)

## Pipeline

1. `tools/assets/models/site_<kit>.py` (uses `gear_lib.py`) writes `out/site_<piece>_<kit>.glb`. Metres, +Y up, facing +Z, all vertices on the root joint. Copy to `projects/World500BCE/game/gfx/models/`.
2. `common/sites/*_kit.txt` defines kits and tiers. `common/sites/*_sites.txt` lists sites (`name`, `location`, `weight`).
3. `SiteSystem` (Core/Sites): score = `Development.Urbanization(state) x weight`, rounded to 0.01. Kit = the kit whose `culture_groups` holds the state's dominant pop culture group. Tier = highest `min_score` reached. Layout is deterministic (seed = FNV hash of site key), baked into one `FigureModel`. Rebuilt monthly only if kit, tier, score step or `Figures.Version` changed.
4. `MapSites` (Game) draws baked models at `MetresToWorld = 0.05`, only when camera height <= `ShowBelow = 90`, not in globe mode, not on non-visible locations.

## Layer kinds (tier `layers`)

- `place`: one piece at fixed x, z, yaw.
- `ring`: wall segments plus gate pieces on a circle. Gate angles feed `street`.
- `street`: paths from center to each gate.
- `scatter`: weighted pieces, no overlap, count = `count + count_per_score x score`, between inner and outer radius, face center / random / radial.

Yaw 0 = +Z, turning towards +X. Footprint radius = max horizontal reach x 0.8. Pieces follow terrain per placement.

## Commands

Preview sheets (offscreen, no game load):
`dotnet run --project src/Burger.Game -c Release -- --game projects/World500BCE/game --site-sheets OUTDIR [--site-kit KEY] [--site-score S]`

In-game screenshot (`--lookat` takes `lon,lat` or a location key):
`dotnet run --project src/Burger.Game -c Release -- --game projects/World500BCE/game --observe --size 1600x900 --lookat land_04924 --zoom 12 --days 1 --screenshot out.png --frames 30 --log-dir DIR`

Startup log lists every site: `Site 'key': loc at map pixel x,y (state ...)`.

## Add a kit

1. Copy `site_latin.py`, change pieces, budgets, colors. Run it, copy glbs.
2. Add `common/sites/NN_<kit>_kit.txt` with `culture_groups` and tiers.
3. Run `--site-sheets`, view PNGs, tune.
4. Add sites, screenshot in game, copy PNGs to `docs/game/img/`.

## Add a site

Add entry to `01_sites.txt`: `key = { name = "X" location = land_NNNNN weight = W }`. Needs the location's state to have a kit for its culture group.

## OPEN

- Latin kit design (hut shapes, wall style, temple layout) is not sourced against archaeology. Unverified.
- Location point sits on the coast for Rome: part of the ring lies over water. Placement offset not implemented.
- `ShowBelow`, `MetresToWorld` untuned. No instancing: one baked mesh per site.
- Globe mode shows no sites.
