# WrektBurgerEngine Performance Audit

Branch `500BCE-Multiplayer`, base commit `89c01a8`. The changes are in `performance-improvements.patch` (a plain `git diff` against HEAD plus the new file `src/Burger.Game/GpuTimer.cs`). Nothing is committed.

## Status after merging origin/500BCE-Multiplayer (95b7476)

BurgerKingHotline's "Fix uneven time between days" reworked the same areas independently. Where both changed the same thing, the merge keeps that version, so parts of this report describe code that is no longer in the tree:

* Superseded and removed: worker-thread ticks and the held-frame HUD picture (replaced by `SimulationThread`), the MSAA, relief and tree switches (replaced by `GraphicsQuality`), the figure, unit-marker and lookup-texture caches in `MapFigures`, `MapOverlay` and `MapRenderer` (those files were rewritten), and the exact simulation changes in `TradeNetworkSystem`, `CultureSystem`, `Modifiers`, `CommoditySystem` and `AiSystem` (rewritten there too).
* Kept: `GpuTimer` stage timing and `RenderPerf`, now stamped inside the new frame loop; `BENCH_MOUSE` and hitch logging in `Bench`; the `Defines`, `GreatPowers`, `Relations` and `FeatureSet` speed-ups; the `--deep-digest`, `DAYMS` and `EARLYMONTH` tool output.
* Checked after the merge: Release build with 0 warnings and 0 errors; `validate World500BCE --days 60 --digest 20` gives the same digests as a build of 95b7476 alone; `--bench` at 1280x720 runs and reports GPU stages.

The numbers below were measured before the merge and have not been re-measured.

## Summary

Before this work, frame-time spikes on capable hardware came from managed allocation in the presentation layer, not from the GPU. Roughly 1.2 to 1.7 MB per frame was allocated, which forced gen 2 collections and frames of 140 to 170 ms. In live play, the lookup texture behind the map colors was also rebuilt on about half of all frames, even though nothing it reads had changed. Under the same conditions, the changes below reduce allocation by about 97%, remove every frame over 50 ms in the scripted benchmark, and cut the median CPU render time in live play roughly in half.

The rendering changes are in the presentation layer. A second round, described under [Simulation (World500BCE)](#simulation-world500bce), changes simulation code only where the result is bit-for-bit identical, checked with state digests over a full game year. Tick rate, game rules, save format, modding and map data are untouched. A third round runs single-player ticks on a worker thread, which removes the frame stalls in live play at about the same in-game speed. The one clock change is that worker ticks get a per-call budget that ends when the next frame is due, instead of a fixed 14 ms. Tick order and speeds are unchanged. Every change that skips work is either exactly equivalent (checked texel by texel at runtime, see below) or a user setting whose full-quality default holds on hardware-accelerated drivers.

The largest remaining cost on capable hardware is the terrain fragment shader at strategic zoom, which takes about 10.8 ms of GPU time at 1080p on a GTX 1660 Ti. It is measured and broken down below but left unchanged, because the cheap fixes would change how borders look at that zoom.

## Tested environment

Everything was measured on one machine. No other hardware was tested.

| Item | Value |
|---|---|
| OS | Linux 6.18.55-2-cachyos-lts (X11) |
| CPU | AMD Ryzen 5 7600X, 6 cores / 12 threads |
| RAM | 61 GB |
| GPU | NVIDIA GeForce GTX 1660 Ti, driver 615.71.09 |
| Runtime | .NET SDK 9.0.122 running net8.0 assemblies (`DOTNET_ROLL_FORWARD=Major`) |
| Project | `projects/World500BCE` (16384x8192 map, 59,297 locations, 7.9 M terrain triangles) |

These driver paths ran on this machine:

| Path | How it was selected | Status |
|---|---|---|
| NVIDIA proprietary GL | default | Valid; primary results |
| Mesa zink on NVIDIA Vulkan | `__GLX_VENDOR_LIBRARY_NAME=mesa` | Valid; used as a second driver stack |
| Mesa llvmpipe (software) | `LIBGL_ALWAYS_SOFTWARE=1` | Valid; stands in for "weak hardware" |
| AMD Radeon 610M iGPU (radeonsi, PRIME) | `DRI_PRIME=pci-0000_0f_00_0` | **INVALID.** Rendering ran, but presenting to the NVIDIA X screen produced a black window. These numbers are not used. |

**Not tested at all:** Windows (any GPU), macOS, any AMD or Intel GPU running the game properly, laptop power states, resolutions other than 720p and 1080p, and multi-monitor setups. A win-x64 release was built but never run. The Windows code path of the GPU timer (`opengl32.dll` plus `wglGetProcAddress`) has never been run.

## Instrumentation

All of it is opt-in. The game does no extra work unless `--perf` or `--bench` is given.

* **`RenderPerf`** (rewritten) measures CPU time per presentation stage (`input/pick`, `map`, `labels`, `orders`, `units`, `fleets`, `air`, `hover`, `ui`, `EndDrawing/flush+swap`). It also tracks managed allocation per stage and per frame, GC collections per generation, and GC pause time.
  * Frame time is measured from `Begin` to `End` with `Stopwatch`, instead of trusting raylib's `GetFrameTime`.
  * Percentiles come from a ring buffer. A bench run keeps every frame (capacity 131,072). `--perf` keeps the last 600.
  * Counts, allocation and GC figures cover the whole window, not only the frames still in the ring.
  * `LastFrame()` gives the breakdown of a single frame for hitch logging.
* **`GpuTimer`** (new) records GPU time per stage (`terrain`, `sites/figures`, `labels`, `overlays+ui`) using GL 3.3 timestamp queries.
  * Results are read 6 frames later and only once the driver reports them ready, so the timer never stalls the pipeline.
  * Each stamp flushes raylib's batch so 2D draws are charged to the right stage.
  * Overhead: 128.3 fps with the timer against 127.7 fps without it (`BURGER_NO_GPU_TIMER=1`). That gap is within run-to-run noise.
  * A stage only measures true GPU cost while the frame is GPU-bound.
* **`Bench`** (extended):
  * Frames of 50 ms or longer are logged with their stage breakdown.
  * Stage and GPU windows start at the end of warmup, so they cover the same frames as the leg figures.
  * GPU percentiles are reported per leg.
  * `BENCH_MOUSE=center|sweep|none` fixes where the hover pick looks (see harness issues below).

### Harness issues found and fixed

1. **The hover pick followed the real cursor.** In `--bench`, the hover pick used wherever the physical mouse happened to be, so the result depended on the desk. One zink baseline allocated 70 KB/frame while the identical NVIDIA run allocated 1,160 KB/frame, only because the cursor was off the window in one and over it in the other. `BENCH_MOUSE` now fixes the pick, and the baselines were re-run (`base2`). The default is `center`, a static hover. `sweep` moves the pick around an ellipse every 2 s, crossing provinces about once per frame like fast mouse movement.
2. **The machine slept during one run** (a 2,092 s wall time). Runs are now wrapped in `systemd-inhibit`.
3. **Two sampler bugs in my first scripts:** the peak-RSS sampler read the wrong process, and GC counts were reported cumulatively. Both were fixed before any number in this document was taken.
4. **Run-to-run drift on average fps is about ±10%.** Two runs of the same baseline build gave 124.2 and 112.2 fps. Average fps differences below about 10% are therefore not treated as results here. Percentile tails, allocation and GC figures are far more stable, so the conclusions rest on those.
5. **A user `settings.cfg` changes results.** The release folder contains the user's `settings.cfg` (`lod_terrain = 0` and others), so screenshots and benchmarks there do not match the dev build. All comparisons use the `bin/Release` build with no settings file, unless a run says it writes one.

## Results, ranked by measured impact

Unless stated otherwise, results come from the NVIDIA GTX 1660 Ti at 1920x1080 with vsync off: `World500BCE`, `--bench` (9 legs, 52 s, one continuous camera path covering far, mid and near zoom, pans and a zoom sweep). "base2" is HEAD plus the measurement harness only.

### Overall: baseline against final

| Run | ALL fps | p50 ms | p95 ms | p99 ms | max ms | alloc KB/frame | GC gen0/1/2 | GC pause | frames ≥50 ms | peak RSS |
|---|---|---|---|---|---|---|---|---|---|---|
| base2, static hover | 124.2 | 7.15 | 11.33 | 13.34 | 161.36 | 1301.5 | 133/13/6 | 387.6 ms | 3 | 4102 MB |
| base2, static hover (repeat) | 112.2 | 7.78 | 12.36 | 21.00 | 171.26 | 1191.6 | 120/10/4 | 345.3 ms | 3 | 4212 MB |
| **final**, static hover | 122.5 | 7.32 | 11.96 | 12.41 | **24.51** | **43.6** | 64/7/**0** | **48.4 ms** | **0** | 3255 MB |
| base2, sweeping hover | 115.4 | 7.94 | 13.90 | 17.27 | 167.26 | 1659.9 | 129/14/7 | 571.7 ms | 3 | 4090 MB |
| **final**, sweeping hover | 122.1 | 7.36 | 11.89 | 12.30 | **23.59** | **42.8** | 65/7/1 | **53.5 ms** | **0** | 3261 MB |

Average fps is unchanged within noise. With a static hover, the worst frame drops from 161 to 171 ms down to 24 ms and gen 2 collections stop. With a sweeping hover, p99 falls from 17.3 to 12.3 ms. Peak RSS drops by about 850 MB.

### 1. Cache the figure stacks per snapshot (`MapFigures.StacksFor`)

* **Root cause.** The 3D unit figures regrouped every division into location stacks every frame, using LINQ, dictionaries and lists. The result only changes when a new `RenderSnapshot` is published, and a snapshot is immutable.
* **Fix.** The stacks are cached by snapshot reference.
* **Evidence.** Harness-only build against the build with this change, NVIDIA 1080p, same path:
  * Allocation: 1248.5 → 88.5 KB/frame.
  * GC pause: 350.7 → 49.1 ms; gen 2 collections: 5 → 1.
  * Worst frame: 137.3 → 20.8 ms; frames ≥50 ms: 3 → 0.
  * Peak RSS: 4145 → 3419 MB.
  * The logged hitches were all GC pauses inside the map stage.
* **Correctness.** The cache key is the snapshot reference and the grouping code is unchanged, so the output is the same list.

### 2. Skip rebuilds of the map lookup texture that would write the same texels (`MapRenderer`)

* **Root cause.** The lookup texture holds every location's color, owner/state, flags and terra-incognita data. It was rebuilt in full (59,297 entries, about 2.8 ms and 450 KB of allocation) whenever `Dirty` was set, for two reasons:
  * **Hover.** Every change of the hovered province set `Dirty`. A full rebuild then ran even though the existing patch path updates just the two changed flag texels. With a sweeping hover, that is a rebuild almost every frame.
  * **Live play.** `Game.MapChanged` fires most frames while the simulation runs. During live play the renderer receives fast snapshots (`RenderSnapshotBuilder.BuildFast`), which reuse the previous snapshot's `Locations`, `Countries` and `DiplomacyRelations` arrays. Every one of those rebuilds therefore read identical data and produced an identical texture. A counter build measured 837 rebuilds in 1,800 frames (47%), costing 2.3 s of main-thread time.
* **Fix.** `LutInputsUnchanged` compares everything the snapshot-path build reads; the build reads nothing else:
  * by reference: the immutable `Locations`, `Countries` and `DiplomacyRelations` arrays;
  * `PlayerTag`, `VisibilityVersion`, `ExplorationVersion` and `GeoVersion`;
  * map mode, custom mode, diplomacy focus and the Geo debug inputs.

  When all of these match the last build, the rebuild is skipped and the flag patch path moves the hover and selection highlights. A rebuild always runs in three cases:
  * after a build that read the live game (custom modes, a foreign diplomacy focus, Geo debug), because that path's flags are not reproduced by the patch;
  * while the reveal animation has written texels since the last build;
  * whenever any of the compared inputs changed.
* **Evidence.**
  * Sweeping hover: map stage p50 3.5 → 0.1 ms and map stage allocation 1570 → 0.6 KB/frame.
  * Live play (`--observe --perf --speed 3`, 1080p, two runs per build):

    | Build | render frame p50/p95/p99/max | map stage p50/max | alloc KB/frame | GC pause per 10 s window |
    |---|---|---|---|---|
    | base2 | 6.3/14.4/15.5/18.9 ms; 7.5/15.9/16.3/19.1 ms | 2.6–2.7 / 3.7–6.1 ms | 787.0; 658.2 | 37.9; 28.4 ms |
    | final | 3.7/14.9/16.1/16.7 ms; 3.7/15.5/15.9/16.3 ms | 0.1 / 0.2 ms | 203.0; 209.7 | 16.9; 10.8 ms |

    The overall live frame rate (33 to 38 fps) is set by the simulation, which takes 16 to 22 ms per frame on the main thread at speed 3. That cost is unchanged by design.
* **Correctness check.** A temporary build (`BURGER_LUT_CHECK=1`, not in the patch) rebuilt the texture after every skipped or patched frame and compared all four blocks texel by texel:
  * Patch path, sweeping hover: 3,200 frames checked, 0 mismatched texels.
  * Skip path, sweeping hover: 3,200 frames checked, 0 mismatched texels.
  * Skip path, live play: 1,000 frames checked, 0 mismatched texels.

  These checks ran in Political mode only. The selection patch, other map modes and the reveal animation were not exercised by them (World500BCE has terra incognita off). The reveal case is protected by construction: any reveal write forces the next rebuild.

### 3. Cache unit-marker groups per snapshot (`MapOverlay.DrawUnits`)

* **Root cause.** The same per-frame LINQ grouping as item 1, for the 2D unit markers.
* **Fix.** A cache keyed by snapshot reference, with the same filter and the same group order, plus a reused slot dictionary. Click handling is unchanged.
* **Evidence.** Units stage allocation 48.5 → 2.4 KB/frame; total allocation 88.7 → 43.4 KB/frame; GC gen0 collections over the run 81 → 65.

### 4. MSAA off by default on software renderers, plus user quality switches

* **What changed.**
  * `UserSettings` gains `msaa`, `relief_detail` and `trees`, all on by default. Three switches sit in the settings panel under "Terrain", and they persist in `settings.cfg`.
  * The MSAA setting is read before the window opens, so turning it off also removes the 4x sample buffer on the next start.
  * When GL_RENDERER names llvmpipe, softpipe or SwiftShader, MSAA starts off unless the player has set it. Hardware drivers keep MSAA on.
* **Evidence.** llvmpipe at 1280x720, same path:

  | Run | ALL fps | p50 | p95 | p99 | max | near-static fps |
  |---|---|---|---|---|---|---|
  | base2 | 13.9 | 62.6 | 99.9 | 161.0 | 385.5 | 10.8 |
  | final, `msaa = 1` set | 14.6 | 59.4 | 98.9 | 143.8 | 196.4 | 11.4 |
  | final, default (MSAA auto-off) | 18.2 | 51.2 | 91.8 | 103.5 | 110.7 | 24.6 |
  | final, MSAA buffer removed too (`BURGER_NO_MSAA=1`) | 18.2 | 48.7 | 93.6 | 119.3 | 144.6 | — |

  Near zoom more than doubles (11.4 → 24.6 fps). Far zoom already runs without MSAA (the terrain LOD turns it off there), so it gains nothing.
* **Relief was considered and rejected as an automatic default.** Turning relief off gained nothing measurable on llvmpipe (18.2 fps with relief on against 17.4 fps with it off). On NVIDIA it gained about 0.3 ms of GPU time at mid zoom. It stays a manual switch only.
* **Terrain blending "Low" was slower, not faster, on llvmpipe** (12.2 fps against 14.6 at the default). It was left alone.
* **Anomaly (OPEN).** One earlier llvmpipe run with `msaa = 1` gave 8.8 fps; the repeat gave 14.6 fps. The cause is unknown. The repeat matches base2, so the repeat is the one used.

### 5. Smaller items (not measured individually)

* `UpdateLutFlags` indexes the locations array directly when `Locations[index].Index == index`, instead of a linear `FirstOrDefault` over 59,297 entries. It falls back to the scan otherwise.
* The `hasNormalMap` uniform location is cached instead of looked up.

## Remaining bottlenecks (measured, not changed)

### 1. Terrain fragment shader at strategic zoom (GPU)

At far zoom the terrain pass costs 10.8 ms of GPU time at 1080p on a GTX 1660 Ti, against 6.5 ms at mid and near zoom. It is the largest single cost on capable hardware. Measured with shader variants (`EXP` environment, experiment build only):

| Variant | far-static GPU terrain p50 | far-static fps |
|---|---|---|
| full shader | 10.81 ms | 89.4 |
| far-zoom border neighbor search disabled | 6.60 ms | 144.3 |
| relief shading disabled | 10.63 ms | 89.6 |
| early return after the data fetch | 4.29 ms | 217.3 |
| early return after the 4-texel location gather | 1.80 ms | 271.4 |
| flat color, no texture reads | 0.63 ms | 252.5 |

The far-zoom border block costs about 4.2 ms. It does 4 scattered `locAt` fetches at a stride of about one screen pixel, plus up to 4 lookups per neighbor, and it keeps borders visible when several map pixels share one screen pixel. Removing it or thinning it changes border rendering at strategic zoom, which this audit treats as map accuracy. One option is a "far border detail" quality setting, defaulting to full. Its fidelity cost would need review and it has not been built. Shading after the gather (about 4.3 to 9 ms) is spread across terrain, water and lighting. No single piece stands out.

### 2. Simulation time on the main thread during live play

See [Simulation (World500BCE)](#simulation-world500bce). Single ticks of 200 to 800 ms remain, but in single player they now run on a worker thread and no longer stall frames. See [Ticks on a worker thread](#ticks-on-a-worker-thread).

### 3. Country labels during live play

Labels allocate about 106 to 127 KB/frame while the simulation runs, mostly from label re-derivation on `MapChanged`. This was not changed.

### 4. Multiplayer transport (inspection only, not run)

* `TcpTransport.Send` writes synchronously, under a lock, on the caller's thread, which is the main thread. A stalled peer socket could therefore block rendering.
* `NoDelay` is set.
* The join handshake polls with `Thread.Sleep(5)`; this only matters during the handshake.

The protocol is lockstep and sends commands only. No change was made.

### 5. Zink runs faster than native GL at near zoom (OPEN)

Zink on the same NVIDIA GPU ran 290 fps at near zoom against 140 fps on the proprietary GL driver. The cause is unknown. MSAA resolve handling is a guess, not a finding.

## Simulation (World500BCE)

### Method

* `burger validate World500BCE --days N --stream --tick-stats` records the wall time of every hourly tick. `--profile` adds per-stage maxima. `dotnet-trace` (sampled thread time) gives call-tree breakdowns.
* Correctness: `--digest 30 --deep-digest` writes a state digest every 30 days over 365 days. Every change below gave the same 26 digest lines as HEAD.
* The machine was shared with a browser using 4 to 5 cores during these runs (load average 5 to 15). Single runs therefore swing by up to 1.5x. The comparison below alternates HEAD and the patched build back to back, twice, so both see the same load.

### Where the hitches come from

A tick over about 16 ms is a dropped frame, because `GameClock.Update` runs ticks on the render thread. The slow ticks are almost all in a few scheduled stages:

| When | Stage | HEAD worst case |
|---|---|---|
| day 7, 20h, monthly | settlement of one commodity (`MatchCommodity`) | 250 to 570 ms (January about 1,100 ms) |
| day 7, 07h, monthly | `GreatPowers.MonthlyTick` | about 170 ms |
| days 12 to 16, hours 2/10/20 | other commodity settlements | 60 to 160 ms |
| hour 2, daily | production and construction | about 50 ms |

Most settlement time goes to computing, for every buying country, the trade priority (`TradePriority`, which calls `DiplomacySystem.Opinion`) of every seller country. Route finding after ownership changes and construction input demand come next.

### Changes (all exact)

| Change | Why it is exact |
|---|---|
| Trade routes: the state graph is stored as dense arrays, and one ordered search per origin is kept and resumed across destinations. The graph survives ownership changes unless the topology hash changes. | Same neighbor order and first-edge rule as the old breadth-first search, so the paths and summed distances are the same. Digest match. |
| Land route paths and distances are kept per origin and destination across ownership changes. Only the capacity, which depends on the owners, is recomputed. | The search tree never changes once a state is reached. |
| The BFS timer in `FindRoute` runs only when profiling. | Timing only. |
| `BuildOrder` sorts a struct key array instead of a list with a closure comparer. | Same total order (agreement, priority, country index). |
| `Defines.Get` caches parsed numbers, and `Load` clears the cache. It was a linear, case-insensitive scan plus a parse on every call. | Same lookup and parse; nothing writes `Defines.Raw` outside `Load`. |
| `FeatureSet.Has` caches answers per key instance, and `Set` replaces the cache. | Same answer while the feature set is unchanged. |
| `DiplomacyRules.BaseOpinion` builds the script scope only when a `base_opinion` script value exists. | The dictionary was discarded when there was no script. |
| `GreatPowers.Recompute` takes the maximum of each score table once instead of once per country. | Same expression, evaluated once. |
| Construction input demand groups a country's projects by state once per settled commodity. | Same projects in the same order: same comparer chain, and a stable sort. |

Rejected: caching opinions across commodities. Opinion reads modifiers and the `base_opinion` script value, and mods can override `CommodityRules`, so it cannot be shown to stay the same between two settlements. Parallel priority evaluation was also rejected, because the lazy caches behind `Opinion` are not proven thread-safe.

### Result: HEAD against the patch, 120 days, alternating runs

| | HEAD run 1 | patch run 1 | HEAD run 2 | patch run 2 |
|---|---|---|---|---|
| max tick (ms) | 1137 | 792 | 1296 | 631 |
| p999 tick (ms) | 519 | 268 | 510 | 235 |
| p99 tick (ms) | 90.9 | 77.4 | 96.1 | 71.2 |
| ms per sim day | 225 | 195 | 221 | 183 |
| allocation per sim day (MB) | 64.8 | 43.2 | 64.0 | 45.5 |

In short: worst-case ticks dropped about 40% and the 1-in-1000 tick about 50%. Throughput rose about 15% and allocation fell about 30%. Not measured individually: the construction grouping and the walked-path cache each showed no clear gain against the load noise. They are kept because they remove work and allocation without changing results.

### Ticks on a worker thread

Exact optimization alone could not remove the stalls, so in single player `GameClock.Update` and the render snapshot build now run on a worker thread (`App.StartTick` / `CompleteTick` / `FrameHeld`). The same `Game.Tick` calls run in the same order; only the thread differs.

How it works:

* A normal frame handles input, draws the map and overlays, and draws the live HUD. Then, before the frame is presented, the ticks start on a worker. Nothing on the main thread touches `Game` until they finish.
* The worker's budget ends at the moment the next frame is due (`GameClock.Update` takes an optional budget). At frame start the main thread waits up to 8 ms for ticks that are about to finish. The frame's own CPU work is about 5 ms, so it still presents on time.
* If the ticks are still running after that wait, which happens during long ticks, the frame is held: the camera moves, and the map, labels, units and fleets draw from the last snapshot. The HUD is shown as the picture from the last normal frame.
* A held frame never drops input. On a mouse press or release, a key press, or a scroll over the HUD, the frame waits for the ticks to finish and runs as a normal frame.
* While the clock runs, the HUD is drawn into a render texture (premultiplied alpha) so held frames can show it again.
* Speed 5 keeps its 30 ms budget, the same trade of smoothness for speed it made before.
* The worker path is used only when the clock runs in single player without a benchmark. Paused play, multiplayer and `--bench` run the ticks inline as before.
* Switches: `BURGER_SYNC_TICKS=1` restores every tick on the main thread (for A/B). `BURGER_WORKER_BUDGET_MS` sets a fixed worker budget, and `BURGER_TICK_GRACE_MS` sets the frame-start wait.

Tuning history, all at speed 3:

| worker setup | days/s | frames held | HUD |
|---|---|---|---|
| 14 ms per call, started after present | about 10% below the main-thread path | about 50% | — |
| 40 ms per call | at parity | about 66% | unusable: most frames showed a stale HUD |
| budget to the next frame, 3 ms wait | about 9% below | about 35% | live on most frames |
| budget to the next frame, 8 ms wait (final) | at parity | about 28% | live on most frames |

Result: World500BCE, `--observe --speed 3` (target 4 days/s), 60 fps cap, 1280x720 and 1600x900, hidden window, 100 to 150 s per run after loading. The runs alternate between modes so both see the same machine load. Each figure is the range over nine 10 s PERF windows.

| | worker (final) | main thread |
|---|---|---|
| fps | 58.3 to 58.6 | 34.7 to 47.2 |
| frame interval p95 (ms) | 24.8 to 25.3 | 45 to 71 |
| frame interval max (ms) | 31 to 59 | 222 to 287 |
| frames over 50 ms per 10 s | 0 or 1 | 16 to 31 |
| frames held per 10 s (of about 585) | 118 to 212 | 0 |
| days/s (mean of 9 windows) | 3.80 | 3.84 |

The days/s difference is within run-to-run noise: other main-thread runs gave 3.73 to 3.84. Garbage collection is the same in both modes when counted for the whole process (about 100 gen0 and 35 gen1 per 10 s, 400 to 600 ms of pause). RenderPerf counts only collections inside a frame, so it shows more in worker mode, where ticks overlap frames. PERF now prints a `process GC` line for this reason, plus the held frame count.

The frames over 50 ms that remain in worker mode (about 1 per 10 s, 50 to 60 ms) were not traced. Garbage collection pauses on the main thread are the likely cause, but that is not proven.

Limitations:

* **Held frames.** About 1 frame in 3 still shows the last HUD picture, mostly during the daily and monthly ticks (a 50 ms daily tick spans 3 frames). A click or key press in such a frame waits for the tick. That is up to about 500 ms during the monthly settlement on day 7, so input can stall there.
* **Input waits are untested.** The automated runs have no input, so the wait-on-input path has not been exercised.
* **Frame pacing.** The 8 ms wait shows as about 5% of frames at 25 ms instead of 17 ms.
* **No MSAA on the HUD while running.** The HUD render texture has no sample buffer. HUD shapes were checked visually on one machine, not measured.
* **Ticks are not shortened.** A 500 ms monthly settlement still takes 500 ms.
* **Multiplayer is unchanged.** The session still runs its ticks as before.

What would remove the remaining limitations (OPEN, not built): move the HUD to read snapshots and send its changes as commands. The HUD could then stay live and responsive during ticks of any length.

### Second round: tooltips and tick cost

Tooltips flashed every tick, because held frames showed the cached HUD picture without the tooltip.

* `Gui` keeps the last tooltip (`_lastTooltip`, `EndFrame(drawTooltip)`, `DrawLastTooltip`).
* The cached HUD path draws the tooltip over the texture.
* Held frames redraw the map hover box from the snapshot (`HoverInfo`, `DrawHoverBox`).
* `InputPending` no longer counts camera and speed keys as input, so panning does not force a wait on the tick.

The visual result has NOT been checked live. The automated runs have no cursor.

Exact changes:

| Change | Why it is exact | Measured |
|---|---|---|
| `CultureSystem.ComputeFor` reuses thread-static scratch buffers and a stable key comparison in place of LINQ grouping and sorting. | Same shares in the same order. | Non-stream, 60 days: 10,073 to 9,065 ms (about 10%). |
| `TradeNetworkSystem.BuildOrder` keeps each buyer's seller order arrays between matches, stamped per match, instead of allocating them. | Same keys and sort; arrays are only reused. | Stream, 90 days: 156.40 to 152.29 ms per day (about 3%, near noise). |
| Construction projects by state store a single project without sorting, and store larger lists as sorted arrays. | A one-element sort is the identity. | Not measured alone. |

Correctness:

* 365 days, non-stream, deep digest: 26 of 26 lines match the baseline.
* 120 days, stream, deep digest: 8 of 8 lines match.
* Culture alone: 13 of 13 digest lines match HEAD.

Live check, release build, `--observe --speed 3 --fps 60 --size 1280x720`, hidden window:

| Window | fps | Simulation | Days per second | Frames held |
|---|---|---|---|---|
| first 10 s (loading) | 17.4 | 25.0 ms per frame | 0.7 | 80 |
| next | 58.5 | 18.6 ms per frame | 3.5 | 193 |
| next | 58.3 | 18.8 ms per frame | 3.1 | 205 |

Tick cost that remains (stream, this machine):

* The settlement day (7th, 20h) takes about 641 ms in the first month, which is spent building routes. Later months take about 220 ms.
* The 16th at 02h takes about 160 ms.
* In the live PERF log, `Population.MonthlyTick` peaks at 239 ms and `Commodities.DailyTick` at 195 ms.
* A day costs about 152 ms on one core, so this machine tops out near 6.5 days per second. Speed 4 (10 days per second) cannot be reached on one thread.

Test failures that are not from this patch, because HEAD fails the same way:

* `trade-test` and `society-test` on SampleGame fail; SampleGame has no 500 BCE data. `trade-test` on World500BCE passes.
* `mp-test` on SampleGame prints "[FAIL] no resync before the injected drift" and then hangs.

### Third round: the first half of each month

Stream, 62 days, profile: a day in the first half of a month (days 1 to 16) costs about 265 to 283 ms. A day in the second half costs about 66 to 69 ms. The difference is the monthly work, which is scheduled into days 1 to 16:

| Days 1 to 16, two months | ms |
|---|---|
| Commodity settlement (`Commodities.DailyTick`) | 3,601 |
| `Ai.MonthlyTick` (spread slices) | 1,679 |
| `Ai.DailyTick` (industry drain) | 1,569 |
| `Population.MonthlyTick` | 968 |
| `Politics.MonthlyTick` | 490 |
| `Economy.MonthlyTick` | 397 |

One commodity dominates. `bronze` costs about 380 ms per month, on day 7 (the 1,010 ms and 656 ms spikes). In the first month, 480 of its 800 ms is `BuildOrder`: 286 ms of `Opinion` through the moddable `TradePriority`, and 72 ms of sorting. The other 286 ms is route building.

Changes:

| Change | Why it is exact | Measured |
|---|---|---|
| `TradeNetworkSystem` keeps the markets of the current commodity in a list with a slot per state, in place of a case-insensitive string dictionary. | Same insertion order. State keys are unique (`World.StateByKey`). | Noise level. |
| `Totals` reuses the per-owner row lists. | Same rows in the same order. | Noise level. |
| `CultureSystem` keeps each culture's sum while `_mods` lives, and reuses the emptied `_mods` dictionaries. | Same additions in the same order. | Culture provider about 1,090 to 960 ms over 62 days. |

Rejected:

* Skipping the culture sum for keys that no tradition gives. **It changed the digest from day 30.** `ComputeFor` creates culture states on first use (`Of`), so skipping it changes when those states exist.

Result, alternating runs, stream, 62 days: baseline 204.8 and 200.6 ms per day; patch 208.6 and 194.2 ms per day. **This is no measurable change.**

Correctness: 365 days stream and 120 days non-stream, deep digest: 26 of 26 and 8 of 8 lines match the baseline.

The remaining cost cannot be removed by exact work alone:

* Opinion and priority are virtual rule methods.
* The caches are not thread-safe.
* The monthly work is scheduled into days 1 to 16.

Spreading that work over the whole month would change the scheduling and the digests. It was proposed and rejected; it is not done.

### Fourth round: less repeated work, same schedule

A 47-day trace (month 2, days 1 to 16) showed four places that redo identical work. Each now keeps a snapshot of exactly what it reads and reuses its last answer only when the snapshot matches.

| Change | Why it is exact | Measured (month-2 window of the trace, or `--profile` over 62 days) |
|---|---|---|
| `CultureSystem.ComputeFor` keeps each country's culture shares. It recounts them only when the owned states (list, keys), the "has pops" flags, or any pop's culture reference or size bits differ. | Shares are a function of exactly those inputs; same order, same arithmetic. A country with no people (fallback to `PrimaryCulture`) is never reused. Culture states are still created through `Of` as before. | Culture provider 1,924 to 1,259 ms per 62 days. `ComputeFor` 265 to 122 ms. |
| `AiSystem.BorderCache` keeps the border sets when the controller (reference and index) at both ends of every land border is unchanged. | The sets depend only on those controllers. Controller writes do not always raise `MapChanged`, so the controllers themselves are compared, not the map version. | 237 to 149 ms. |
| `TradeNetworkSystem.ProjectsIn` keeps each country's grouped and sorted construction queue from one commodity to the next. It regroups only when the queue (project, state, building, building key, method, progress bits, cost bits) differs. | The grouping and order read nothing else. | See below. |
| `SortProjects` sorts once-read keys with `Array.Sort` and a queue-index tie-break, in place of an `OrderBy`/`ThenBy` chain. | Same comparers; the index tie-break gives the stable order LINQ gives. | Part of the line above. |

Result, alternating runs, stream, 62 days, three pairs: baseline 212.0, 220.9 and 212.9 ms per day; patch 200.5, 197.1 and 195.8 ms per day. **About 8% less per day.** The worst single tick (757 to 797 ms baseline, 727 to 776 ms patch) and February days 1 to 16 (average 280 ms, maximum 552 to 588 ms) are within run-to-run noise of before. The early-month days are cheaper only by the amounts in the table.

Correctness: after every change, 365 days stream and 120 days non-stream, deep digest: 26 of 26 and 8 of 8 lines match the baseline.

Looked at, not changed:

* The `PoliticsSystem` provider lock. Removing it as a test saved 90 of 999 ms; the lock stays. The remaining cost is `ComputeModifiers` and its allocations, rebuilt per country per game hour.
* Trade priorities across commodities. A settlement mutates gold and stockpiles between commodities, and `TradePriority` and `BaseOpinion` are moddable, so reuse cannot be shown exact.

### Not solved

* Single ticks of 200 to 800 ms remain, on day 7 at 20h each month and on days 12 to 16. They no longer stall frames in single player, but the in-game date pauses during them.
* Multiplayer still runs ticks on the session path, so it keeps the old stalls.
* **Making `Opinion` safe to call from several threads** and evaluating trade priorities in parallel would shorten the long ticks themselves. That needs an audit of every lazy cache in `PopulationSystem`, `PoliticsSystem` and the modifier providers, and it must hold for mod rule overrides.

## Correctness checks

| Check | Result |
|---|---|
| `dotnet build -c Release` (solution) | 0 warnings, 0 errors |
| `burger borders test` | 27 passed, 0 failed |
| `burger geo test` | 56 passed, 0 failed |
| `burger figure test` | 95 passed, 0 failed |
| `burger validate projects/World500BCE` | OK, 0 errors, 0 warnings |
| `burger validate projects/SampleGame` | OK, 0 errors, 0 warnings |
| World500BCE state digests, 365 days, every 30 days (`--deep-digest`) | 26 of 26 lines equal to HEAD |
| Worker thread, live World500BCE (Linux, source build and `linux-x64` release) | runs, HUD composites correctly, date advances. The state digests come from `validate`, which runs ticks inline. The worker path has no digest check of its own. |
| Lookup texture equivalence (patch and skip paths) | 0 mismatches in 7,400 checked frames (Political mode) |
| Screenshot diff against base2, 1280x720, zoom 300 / 800 / 40 | 1.55% / 0.64% / 3.17% of pixels differ by more than 8. Base2 against itself gives 1.01% / 1.17% / 3.36%. Both diff masks are label placement jitter only; map colors, borders and terrain match. |
| Patch applies to a clean HEAD (`git apply --check`) | OK |
| Release `linux-x64` started and rendered a screenshot | OK |

Not covered: the worker path on Windows or on a second machine, globe mode, the menu map, other map modes, selection, the Trees switch on a project with trees (SampleGame), multiplayer sessions, and any run on Windows.

## Compatibility

* **Saves.** No save code was touched. The simulation changes keep every state digest equal to HEAD over a year.
* **Modding.** No data formats, scripts or sandbox rules were touched.
* **Map accuracy.** No terrain, border or LUT output was changed; equivalence was checked as above.
* **`settings.cfg`.** Three keys were added: `msaa` (written only once the player has set it, so the software-renderer default can apply), `relief_detail` and `trees`. Older builds ignore unknown keys. A file without them loads with everything on.

## Reproducing

All commands run from the repository root.

```sh
export DOTNET_ROLL_FORWARD=Major
dotnet build -c Release
G=src/Burger.Game/bin/Release/net8.0/Burger.Game.dll

# scripted benchmark (writes BENCH lines and per-leg GPU figures to <dir>/error.log)
dotnet $G --game projects/World500BCE/game --bench --size 1920x1080 --log-dir /tmp/bench-run
BENCH_MOUSE=sweep dotnet $G --game projects/World500BCE/game --bench --size 1920x1080 --log-dir /tmp/bench-sweep

# variants
BURGER_NO_MSAA=1 ...                 # no 4x sample buffer
BENCH_SKIP=map,ui ...                # skip stages (map|sea|terrain|trees|ui)
BURGER_NO_GPU_TIMER=1 ...            # GPU timer off
LIBGL_ALWAYS_SOFTWARE=1 __GLX_VENDOR_LIBRARY_NAME=mesa ...   # llvmpipe
__GLX_VENDOR_LIBRARY_NAME=mesa ...                            # zink on NVIDIA

# live simulation profile (PERF lines every 10 s)
# BURGER_SYNC_TICKS=1 runs every tick on the main thread for comparison
dotnet $G --game projects/World500BCE/game --observe --perf --speed 3 --vsync off --size 1920x1080 --lookat 12,42 --zoom 300 --log-dir /tmp/live

# correctness
T=src/Burger.Tools/bin/Release/net8.0/burger.dll
dotnet $T borders test; dotnet $T geo test; dotnet $T figure test
dotnet $T validate projects/World500BCE; dotnet $T validate projects/SampleGame

# simulation hitches and determinism (compare the DIGEST lines with a HEAD build)
dotnet $T validate World500BCE --days 365 --stream --tick-stats --digest 30 --deep-digest
dotnet $T validate World500BCE --days 60 --stream --tick-stats --profile
```

How to read the output:

* `BENCH ... <leg>: n= fps= frame_ms avg= p50= p95= p99= max=` gives per-leg frame times.
* `BENCH ... ALL stages:` gives the CPU stage percentiles, `KB/frame by stage` and `GC Δ gen0/gen1/gen2, pause`.
* `BENCH ... <leg> gpu ...` gives GPU stage percentiles for that leg.
* `BENCH hitch ...` is one line per frame of 50 ms or more, with its breakdown.

Keep the cursor off the window, or rely on `BENCH_MOUSE`. Make sure no `settings.cfg` sits next to the dll unless the run is meant to test one.

## Round 5: opinion and modifier code

* **Culture modifiers (`CultureSystem.cs`), KEPT.** Each culture's tradition sums are rebuilt only when its traditions, weights or modifier values change, compared by reference and bit pattern. A country's culture mix result is reused while its shares and every culture's version are unchanged.
* **Politics modifiers, REVERTED.** Direct sums, a dictionary pool and a core-land snapshot were slower under `--profile` in 3 of 3 rounds: provider #0 took 729–890 ms, against 618–679 ms on the old code. `PoliticsSystem.cs` is back at HEAD.
* **Correctness.** Digests are IDENTICAL: 26 of 26 with `--stream` over 365 days, and 8 of 8 without it over 120 days.
* **Wall time.** World500BCE, 62 days, `--stream`, 5 alternating runs on this machine:

  | Build | Times (ms) | Mean (ms) |
  |---|---|---|
  | Old | 11913, 11377, 11576, 11369, 11527 | 11552 |
  | New | 10393, 11071, 10504, 10611, 10995 | 10715 |

  The new build is 7.2% faster and won all 5 runs. Only this machine was tested.
* **OPEN.**
  * The baseline is HEAD plus the current tree with the old `CultureSystem.cs`. The scratchpad copy of the exact pre-round tree was lost.
  * `WealthIndex` and the `lock` paths are untested as costs. Trace numbers for them show safepoint bias.

## Round 6: early-month commodity settlement (exact, no rescheduling)

- `Modifiers.Sum` memo: a last-hit fast path for the same scope and key.
- `TradeNetworkSystem.BuildOrder`: computes the pair priorities for a whole buyer row at once through `CommoditySystem.TradePriorities` and `DiplomacySystem.TryOpinionsOf`. It falls back to per-pair `Opinion` when the rules or scripts override `BaseOpinion`/`TradePriority`, or when a population or politics entry is missing.
- `TryOpinionsOf`: the enemy flags reset only the indices that were set, not the whole country array.
- `FindRoute`: presized path list (neutral).
- Digests: STREAM_SAME 26/26, NONSTREAM_SAME 8/8 against the base.
- 62-day stream, 4 rounds old/new: max tick 711-765 vs 589-629 ms; p99 73-75 vs 67-72 ms; wall roughly equal (noise).
- OPEN: `EnsureLandEdges` topology hash and `LandSearch` per-origin allocation in route misses are still a cost; this was tested only on this machine.

## Round 7: opinion row math (exact)

- `TryOpinionsOf` reads each pair's alliance and overlord facts once and also hands back TradePriority's +100/+150 terms, so `TradePriorities` no longer looks them up again; `OverlordOf(from)` is now one lookup per pair, not two.
- The rule-override reflection checks (`GetMethod`) are cached per rules type, no longer run for every row.
- `Relations.ModifierSum`: a plain loop in list order from 0, the same additions as `Enumerable.Sum`, without LINQ.
- Moddable virtuals (`ReligionOpinion`, `ColonialOpinion`) are still called once per pair.
- Digests: STREAM_SAME 26/26, NONSTREAM_SAME 8/8.
- 62-day stream, 5 paired rounds old/new: `Match.Order` total 711-806 vs 612-686 ms (lower in 5/5); wall 11107-12237 vs 10842-12115 ms (lower in 5/5); worst tick lower in 4/5 (524-652 vs 536-596 ms).
- Rejected after measuring (no gain): reusing route-search queues, uninitialized search arrays, removing the `ModifierValue` lock, an id-based path walk.
- Trace note: the sampler blamed `PoliticsSystem.ModifierValue` for unmanaged time, but direct timers show the row's `Sum(to, "opinion")` costs only 37 ms total (8.8 ms on the worst day).
