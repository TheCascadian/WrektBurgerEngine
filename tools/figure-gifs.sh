#!/usr/bin/env bash
# One looping .gif per animation of a figure, on a turntable: assets/figure_gifs/<figure>/<clip>.gif (git-ignored).
#   tools/figure-gifs.sh [-c clip[,clip...]] [-f] figure[,figure...] | all
# Frames go straight from the GPU into ffmpeg (no PNG files), one clip encodes while the next renders, and a .gif whose mesh and
# poses have not changed since it was made is skipped. -f makes them all again. Nothing is rendered unless named; "all" is every animated figure.
#   tools/figure-gifs.sh -c fire soldier_sov          # just soldier_sov/fire.gif
#   tools/figure-gifs.sh all                           # everything (only what changed)
# Env: SIZE=480 (gif pixel size), OUT=dir, GAME=<game folder> (default projects/SampleGame/game, so its gfx/models/ figures are used),
#      NOBUILD=1 skips the build.
set -euo pipefail
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd); cd "$ROOT"
export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-Major}"
OUT=${OUT:-"$ROOT/assets/figure_gifs"}; SIZE=${SIZE:-480}; GAME=${GAME:-"$ROOT/projects/SampleGame/game"}
CLIPS=""; FORCE=""
while getopts "c:f" o; do case $o in c) CLIPS=$OPTARG ;; f) FORCE=1 ;; *) exit 2 ;; esac; done
shift $((OPTIND - 1))
[[ $# -gt 0 ]] || { sed -n 2,11p "$0" >&2; exit 2; }
FIGS=$(IFS=,; echo "$*")
command -v ffmpeg >/dev/null || { echo "ffmpeg is needed" >&2; exit 2; }
[[ -n "${NOBUILD:-}" ]] || dotnet build src/Burger.Game -c Release -nologo -v q >/dev/null
dotnet src/Burger.Game/bin/Release/net8.0/Burger.Game.dll --game "$GAME" --figure "$FIGS" --figure-gif "$OUT" --figure-size "$SIZE" \
  ${CLIPS:+--figure-clip "$CLIPS"} ${FORCE:+--figure-force}
