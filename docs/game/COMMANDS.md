# BurgerEngine command reference

Run commands from the repository root unless noted otherwise.

## Prerequisites

- .NET SDK 8 or newer
- A graphical X11 or Wayland session for the game or Studio
- Python 3; map generation additionally needs NumPy, SciPy, and Pillow
- `git`, authenticated `gh`, and `jq` for upstream patch automation

## Build and launch

```bash
dotnet restore BurgerEngine.sln
dotnet build BurgerEngine.sln -c Release
dotnet build src/Burger.Game/Burger.Game.csproj -c Release

./BurgerStudio.sh
./burger.sh
./BurgerEngine.sh run SampleGame
./BurgerEngine.sh run Earth1930 --perf --log-dir logs
__NV_PRIME_RENDER_OFFLOAD=1 __GLX_VENDOR_LIBRARY_NAME=nvidia ./BurgerEngine.sh run Earth1930 --perf --log-dir logs
./WBE.sh
```

The Bash launchers restore and build Release binaries before running. `BurgerEngine.sh` defaults to the CLI `list` command; `burger.sh` is the direct CLI launcher.

`__NV_PRIME_RENDER_OFFLOAD=1` requests NVIDIA PRIME render offload, which runs the game on the discrete NVIDIA GPU instead of the integrated GPU when the system supports PRIME. `__GLX_VENDOR_LIBRARY_NAME=nvidia` tells the GLX loader to use NVIDIA's OpenGL implementation. Together, they launch Earth 1930 using the NVIDIA GPU; they are mainly useful on hybrid-GPU Linux laptops and are unnecessary on systems with only one GPU. The final command, `./BurgerEngine.sh run Earth1930`, builds the engine if needed and starts the Earth 1930 project.

## CLI commands

The CLI is available as `./burger.sh <command>` or as `dotnet src/Burger.Tools/bin/Release/net8.0/burger.dll <command>` after building.

```bash
./burger.sh help
./burger.sh list
./burger.sh presets

./burger.sh new MyGame
./burger.sh new MyGame --preset hoi4 --base isle
./burger.sh new MyGame --blank
./burger.sh new MyGame --preset hoi4 --features military,combat_hoi4,politics

./burger.sh features MyGame
./burger.sh features MyGame add military combat_hoi4
./burger.sh features MyGame remove ai

./burger.sh validate MyGame
./burger.sh validate MyGame --days 30
./burger.sh validate MyGame --days 60 --profile
./burger.sh validate World500BCE --days 90 --profile --digest 30 --max-tick-ms 2000 --max-ms-per-day 100

tools/profile-sim.sh Earth1930 365
tools/profile-sim-dense.sh Earth1930 365

tools/zip.sh
tools/zip.sh /tmp/WrektBurgerEngine-share.zip

./burger.sh run MyGame
./burger.sh run MyGame --tag USA

./burger.sh export MyGame
./burger.sh export MyGame --name MyGame
./burger.sh export MyGame --name MyGame --out projects/MyGame/export/custom
./burger.sh export MyGame --name MyGame --rid win-x64

./burger.sh generate-locations MyGame
./burger.sh docs
./burger.sh docs MyGame
```

Project arguments can be names such as `MyGame` or project paths. Feature removal moves changed files to `removed_features/<feature-id>/`. `burger validate` checks data and simulates the requested number of days headlessly. `burger docs` regenerates `docs/game/SCRIPT_REFERENCE.md`.

With `--profile`, `burger validate` reports total time, calls, average milliseconds per simulated day, and per-day timing diagnostics for every profile scope:

- `max_day_ms`: the maximum accumulated time for that scope on one simulated day.
- `p99_day_ms`: the 99th percentile of accumulated daily times.
- `worst_days`: up to five `GameDate.TotalDays` values with the highest daily times.

The `tools/profile-sim.sh` wrapper saves the complete output and a filtered profile-lines file. `tools/profile-sim-dense.sh` additionally writes `all-metrics.tsv` and a summary report. The daily max and p99 values make month-boundary spikes visible; inspect `worst_days` against the calendar to distinguish monthly pulses from event-driven spikes such as wars, peace deals, or battles.

The profiler records daily totals for each named scope, including nested scopes and path counters. Timing scopes are accumulated against the simulation day active when they run; count-only scopes have zero timing values.

`--max-tick-ms` and `--max-ms-per-day` enable tick measurements and fail validation when the worst hourly tick or average simulation time per day exceeds the supplied limit. These are host-dependent regression budgets, not frame-rate guarantees. `PROFILE` lines include nested AI tasks and trade routing; `max_daily_ms` is a daily total, while `TICKSTATS` reports individual tick latency.

## Source packaging

Create and validate a source archive from the repository root:

```bash
tools/zip.sh
tools/zip.sh /tmp/WrektBurgerEngine-share.zip
tools/zip.sh dist/WrektBurgerEngine.zip
```

The default archive is written to the repository root with a timestamped name. An explicit relative path is resolved from the repository root. The archive contains source and build metadata only: C#, project files, Python, shell, and PowerShell scripts. It excludes game assets and data, `.git`, IDE metadata, `bin/` and `obj/` build output, logs, saves, removed feature staging files, Python caches, documentation, and temporary editor files. The script tests the completed archive with `unzip -t`.

## Direct binaries and runtime options

```bash
dotnet src/Burger.Game/bin/Release/net8.0/Burger.Game.dll --game projects/SampleGame/game
dotnet src/Burger.Game/bin/Release/net8.0/Burger.Game.dll --game projects/Earth1930/game --observe
dotnet src/Burger.Game/bin/Release/net8.0/Burger.Game.dll --game projects/Earth1930/game --perf --size 1280x720 --log-dir logs
dotnet src/Burger.Studio/bin/Release/net8.0/BurgerStudio.dll
```

### Game flags

Run with `./BurgerEngine.sh run <Project> <flags>` or the `Burger.Game.dll` directly. A bare folder argument also works as `--game`.

| Flag | Meaning |
| --- | --- |
| `--game <folder>` | Game folder (default: `game` next to the exe). |
| `--tag <TAG>` | Skip the menu and play as that country tag. |
| `--observe` | Skip the menu and watch the AI. |
| `--load <save>` | Load a save file. |
| `--size WxH` | Window size, for example `1600x900`. |
| `--log-dir <folder>` | Folder for `error.log` and `--perf` output. |
| `--perf` | Log per-stage frame timings. |
| `--bench` | Scripted render benchmark (see below). Implies `--observe`, vsync and frame cap off. |
| `--speed <n>` | Starting game speed. |
| `--open <screen>` | Open a screen at start. |
| `--exec <command>` | Run a console command at start; repeatable. |
| `--bias <0-1>` | Historical bias for this run (default: the player's setting). |
| `--mapmode <mode>` | Start in a map mode, for example `terrain`. |
| `--zoom <n>` | Starting camera zoom. |
| `--lookat <lon,lat>` | Aim the camera at a longitude and latitude (degrees); `--zoom` sets the distance (testing). |
| `--no-fog-shading` | Turn off fog shading. |
| `--no-units` | Hide units. |
| `--hidden` | Hidden window (testing). |
| `--screenshot <file.png>` | Render, save a screenshot and quit. |
| `--frames <N>` | Frames to render before the screenshot. |
| `--days <D>` | Days of simulation before the screenshot. |
| `-h`, `--help` | Print usage. |

Figure tools (each renders and quits): `--figure-sheet <png>`, `--figure-frames <dir>`, `--figure-gif <dir>`, `--figure-gear <png>`, `--figure-people <png>`, `--figure-variants <dir>`, with `--figure KEY[,KEY...|all]`, `--figure-clip a,b`, `--figure-size N` (default 480) and `--figure-force`.

Examples:

```bash
./BurgerEngine.sh run World500BCE --size 1600x900 --observe --zoom 0.15 --mapmode terrain --screenshot shot.png --frames 150
./BurgerEngine.sh run Earth1930 --bench --size 1920x1080 --log-dir logs
```

### Benchmark (`--bench`)

One continuous camera pass (far, mid and near views, pans and a zoom sweep, no jumps). The log gets a `BENCH` line per leg plus an `ALL` line and one stage breakdown for the whole run. Variants use environment variables:

| Variable | Effect |
| --- | --- |
| `BENCH_SKIP=map,sea,terrain,trees,ui` | Comma list of parts to skip. |
| `BURGER_NO_MSAA=1` | Turn off 4x MSAA. |

`tools/profile-render.sh [Project]` (env `DURATION`, `SIZE`, `OUT`, `GPU=nvidia`) builds, runs with `--perf` and collects host and GPU info; `tools/profile-sim.sh` does the same for simulation.

### Debug logging variables

Set to `1` before a run to log that system: `BURGER_AI_LOG`, `BURGER_AI_DEBUG`, `BURGER_AI_CHECK`, `BURGER_PURSUIT_LOG`, `BURGER_AMB_LOG`, `BURGER_ALLY_LOG`, `BURGER_DIPLO_LOG`, `BURGER_WAR_LOG`, `BURGER_WARCAUSE_LOG`, `BURGER_WARSCORE_LOG`, `BURGER_PEACE_LOG`, `BURGER_FACT_LOG`, `BURGER_FOCUS_LOG`, `BURGER_DECISION_LOG`, `BURGER_GOV_LOG`, `BURGER_LABEL_DEBUG`. Runtime errors normally go to `logs/error.log`.

## UI and asset tools

```bash
tools/compress-ui-bg.sh --help
tools/compress-ui-bg.sh
tools/compress-ui-bg.sh --apply --jobs 4
tools/compress-ui-bg.sh --dir assets/ui/bg --apply

python3 tools/ui-bg-preview.py
python3 tools/ui-bg-preview.py --source assets/ui/bg --output /tmp/ui-bg-preview.html

python3 tools/assets/make_ui_gfx.py projects/MyGame/game
python3 tools/assets/backend_gen.py src/Burger.Core assets/backend
```

`compress-ui-bg.sh` is a dry run unless `--apply` is supplied. It uses `oxipng`, `zopflipng`, or Pillow and verifies decoded pixels before replacement. The UI generator writes editable textures to a project's `game/gfx/interface/ui/` directory.

## Earth map and population tools

Run these from `tools/earth/`:

```bash
cd tools/earth
python3 get_elevation.py elev_z5.npy
python3 make_earth.py <geojson-dir> elev_z5.npy ../../assets_src/earth1930/game
python3 make_pops.py <pop_units.json> ../../projects/Earth1930/game
cd ../..
```

The map source is `assets_src/earth1930/game`; generated wizard assets are copied to `assets/bases/earth1930`. See `tools/earth/README.txt` for Natural Earth inputs and required layers.

## Testing and inspection

```bash
dotnet restore BurgerEngine.sln
dotnet build BurgerEngine.sln -c Release --no-restore
./burger.sh validate SampleGame --days 60 --profile
dotnet build src/Burger.Game/Burger.Game.csproj -c Release

git status --short --branch
git diff --stat
git log -10 --oneline --decorate
```

There is no conventional .NET test project in the repository; project validation and headless simulation are performed by `burger validate`.

## Git and upstream patch automation

`tools/burger.sh` applies one verified, non-merge upstream commit to a clean GitHub fork. It creates a new branch, never pushes, retries conflicts with `-X theirs`, and runs detected tests.

The equivalent Bash entry point in this checkout is:

```bash
tools/git/burger.sh https://github.com/BurgerKingHotline/BurgerEngine.git FULL_40_CHARACTER_SHA
```

It delegates to `tools/merge.sh` and has the same behavior: it requires a clean worktree, validates the full commit SHA and fork relationship, rejects merge/root commits, creates an `auto-patch/<sha>-<timestamp>-<pid>` branch, attempts a normal cherry-pick, retries actual content conflicts with `-X theirs`, runs detected tests, never pushes, and restores the original branch if automatic application fails.

```bash
tools/burger.sh
gh auth status
gh auth login
./tools/burger.sh https://github.com/BurgerKingHotline/BurgerEngine.git FULL_40_CHARACTER_SHA

git remote -v
git fetch --no-tags <remote-url> <sha>
git show --stat <sha>
git switch -c feature/my-change
git cherry-pick <sha>
git cherry-pick --abort
git diff main...HEAD
```

The automation rejects merge commits and restores the original branch if it cannot apply the patch automatically.

To validate, commit, rebase onto `origin/main`, and push directly to the remote `main` branch, run this from a change branch:

```bash
tools/check-rebase-push-main.sh "Describe the change"
```

The script runs `./burger.sh validate SampleGame --days 60`, displays the pending changes, asks for confirmation before staging and rebasing, asks again before pushing, and refuses to run from `main`. After a successful push, it fast-forwards and selects local `main`, then deletes the original change branch. Override the validation command when needed with `CHECK_CMD='...' tools/check-rebase-push-main.sh "message"`. It uses `git add -A`, so all tracked and untracked changes are included; inspect the displayed status carefully.

## Output locations

- Logs: `logs/`, or the directory passed to `--log-dir`
- Saves: `saves/` beside the running game
- Exports: `projects/<Project>/export/<Name>/`
- Script reference: `docs/game/SCRIPT_REFERENCE.md`
- UI preview: `assets/ui/bg/ui-bg-preview.html`

Avoid committing temporary logs, saves, previews, or build output unless the generated file is an intentional repository asset.

## Geography

- `burger geo [project]` reports on historical provinces; `burger geo test` runs the self-test and scale test. Console: `geo <location>`. See [GEOGRAPHY.md](GEOGRAPHY.md).
- `burger geo simulate <project> [--years N] [--war] [--split-population P]` runs the sim headless and reports dynamic splits and merges, validator errors, determinism and save/load.
- Console: `geodate <date|clear>`, `geowar [n|off]` (test occupation), `geoskip [months]` (advance and report province changes).
- Console debug view: `geodebug [on|off]` switches Provinces mode to a debug palette (orange = made by a front line, cyan = growth split, yellow = merge, gray = from data files); `geolist [n]` prints the last n dynamic changes with date, parents, children and reason.
- Console overlay: `geobase [on|off]` draws the province borders from the start of the game as magenta lines above the current shapes in Provinces mode (display only, not interactive). Also: `geosplit <pop>` sets the growth split level, commands chain with `;`, and `geoskip` takes years (`geoskip 30y`).
