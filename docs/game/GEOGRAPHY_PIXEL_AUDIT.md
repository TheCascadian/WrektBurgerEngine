# Audit: provinces that gain and lose pixels

Question: what does it take for a province border to move through the middle of today's smallest map unit, so provinces really gain or lose pixels?

Status of each statement: **[checked]** = read in the code this session; **[measured]** = counted from the test project files; **[estimate]** = my arithmetic, not measured; **[open]** = not checked.

## 1. Verdict

Not possible today, by design. A province is a set of whole locations ("atoms"). Everything the geography layer can do (split, merge, redraw, the new border shift) regroups whole locations. No border can ever run through the inside of one.

Getting real pixel-level shapes is a medium-to-large change in four places: the map data, the renderer, the save format, and what "who holds a pixel" means in the simulation. None of it is blocked by anything fundamental. The biggest decision is the last one (section 5).

## 2. How it works now

- **Location** is "one color of provinces.png" and the smallest map unit [checked: `Model/MapModel.cs:48-53`].
- **Pixel to location** is one `int[] PixelLocation` for the whole map, built once at load and not changed afterward [checked: `Map/MapData.cs`, `Map/MapLoader.cs:101-126`; `CountryLabels.cs:85` relies on it being immutable].
- **Province** (`GeoProvince`) is an immutable atom list with a lifetime. A change retires it and creates new ones [checked: `Geography/GeoModel.cs:19-37`].
- **Conservation** is checked by atom sets: children must cover exactly the parents' atoms, no overlap, no gap [checked: `GeoRegistry.cs:296-313`, `GeoValidator.cs`].
- **Province owner/controller** are derived: the holder of most of its land, from the locations, once a month [checked: `GeoDynamics.Sync`/`Majority`].
- **Rendering**: the data texture stores the location id per pixel. A lookup table maps location to province index for Provinces mode, and the shader draws borders where that index changes between neighboring pixels [checked: `MapRenderer.cs:255-290`, `UpdateLut`, `Shaders.cs`].
- **Saving**: the `geography` save section writes the list of dynamic operations, and a load replays them [checked: `GeoLoader.cs:173-180`, `GeoRegistry.ResetRuntime`].

Size of the problem [measured]:
- provinces.png is 16384 x 6848 = 112 million pixels.
- `locations.txt` has 20,032 locations, so about 5,600 pixels each on average [estimate: includes water].
- The start layout has about 3,400 provinces, so roughly 6 locations per province.

Atoms are therefore roughly 75 x 75 pixels. That is why a front-line "shift" looks like a chunk jumping, not a border sliding.

## 3. Where "atom = whole location" is baked in

| Area | What assumes it | Breaks if shapes go sub-location? |
|---|---|---|
| Province model | `GeoProvince.Atoms` is a string array of location keys | Yes. Needs a pixel-level definition |
| Conservation | set equality of atom keys (registry apply + validator) | Yes. Must become pixel/area conservation |
| Lineage / properties | extensive values (population) split by atom area share | Needs area from pixels, not whole atoms |
| Sync (owner, controller) | majority holder over atoms | Works, but "holder of a fractional location" is undefined (see 5) |
| Lookup table in the renderer | one province index per location | Yes. A location can hold several provinces |
| Shader border lines | compares province index of neighbor pixels read through the location id | Yes. Needs a per-pixel province source |
| Geobase / geodebug overlay | same lookup table (block 3) | Yes, same reason |
| Save/load | operations as atom lists | Yes. Pixel shapes need a representation |
| Validator | connectivity and corner checks use location adjacency | Yes. Needs pixel adjacency |
| Adjacency (`BorderPixels`, `Neighbors`) | between locations only | Not required for provinces; only if the sim should see province borders |
| Armies, supply, pops, war, occupation | all keyed to `Location` [open: not audited line by line] | Not affected unless locations themselves are split |
| UI picking (`World.cs:112`) | pixel to location | Picking a province by pixel needs the new source [open: Hud/inspect code not audited] |

## 4. Design options

**A. Split locations at runtime (make atoms smaller).** Cut a location into two real locations when a front crosses it. Everything keyed to `Location` (armies, buildings, pops, saves, scripts, adjacency, labels) must handle new locations appearing. Highest blast radius. Not recommended.

**B. A separate per-pixel province layer (recommended).** Locations stay the permanent sim unit. A new layer stores, for each pixel, which province owns it. It starts as "the province that owns this pixel's location" and only diverges where the geography moves a border. Province shape is then independent of location shape.
- Memory: a full ushort map would be 224 MB [estimate: 112M x 2 B]. The current data texture is already about 448 MB of RGBA [estimate]. Prefer storing only changed pixels, as per-location patches or runs, since most of the world will not change in a given decade.
- Rendering: an extra override texture, or tiles for changed areas only, sampled in Provinces mode. The lookup-table path stays as the fallback for unchanged pixels.

**C. Derive shapes from the sim instead of storing them (recommended on top of B).** Province shape becomes a deterministic function of sim state and a few seeds per province: for example a weighted nearest-seed partition inside the area its holder controls, recomputed monthly only for provinces touched by a change. Benefits: nothing extra to save (a load recomputes), no drift, and the shape moves smoothly as the front or the population moves. Cost: the function must be fully deterministic and cheap enough to run on 3,000+ provinces.

**D. Coarse cells (for example 8 x 8 blocks).** 1.75 million cells [estimate]. Cheap, but borders look stair-stepped unless the shader smooths them. Possible as a stepping stone.

## 5. The central decision (needs the project owner)

The simulation only knows who holds a whole location. A front that cuts through a location has no meaning to armies, supply, or occupation yet. Two ways out:

1. **Cosmetic-precise.** Provinces get precise, moving shapes, while the sim keeps holding whole locations. A province's pixels near a front are decided by geometry (distance to the holder's land, population centers), and the rules still run on locations.
2. **Sim-precise.** Control itself becomes fractional or per-pixel. That is a much bigger change to war, supply and occupation, and is not needed for what was asked.

My recommendation is option 1 with design B + C.

## 6. Work needed (recommended path)

1. Pixel province layer and its accessors: provinceAt(pixel), area of a province, pixel adjacency between provinces. Initial value derived from locations, so nothing changes visually at start.
2. Redefine conservation in pixels (the sum of children equals the sum of parents, no pixel in two provinces), keeping the atom path for provinces from data files.
3. Shape function (section 4, option C) plus the monthly tick that calls it for changed provinces only.
4. Renderer: per-pixel province texture (or tiles), update on change, shader border lines read it. Overlay and debug view follow.
5. Save: either nothing (shapes recomputed on load) or compressed patches. Replay must give the identical result.
6. Validator, `geolist`, `geodebug`, `geobase`, docs.
7. Tests: shape determinism, conservation in pixels, save/load equals the original, and a visual comparison at the real 16384 x 6848 size.

## 7. Risks

- **Performance**: recomputing shapes over millions of pixels each month. Mitigation: only touched provinces, bounded region, parallel.
- **Determinism**: floating point or iteration order must not change the result between runs and machines.
- **VRAM**: an extra full-size texture is heavy on small GPUs. Mitigation: patches or tiles.
- **Thin slivers and holes**: pixel partitions can produce detached islands inside a province. Needs a cleanup pass and a minimum-size rule.
- **Consistency with the sim**: province owner must still make sense when a province covers part of a location. Needs the rule from section 5.
- **Existing data**: provinces from data files (history/geography) stay atom-based and must keep validating.

## 8. Not checked

- Hud inspect and selection code paths that read provinces.
- Studio map editor use of provinces.
- The older test that fails ("save made before the layer existed loads with it, world unchanged"), cause unknown.
- Verified on Linux only.

## 9. Current state (for reference)

The engine already moves whole locations between neighboring provinces (`border shift`, `GeoDynamics.TryShift`), alongside front-line splits, growth splits and merges. That is the most it can do with atoms of about 75 x 75 pixels. Merges only rejoin existing pieces.
