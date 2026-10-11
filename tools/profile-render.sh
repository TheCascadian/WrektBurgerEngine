#!/usr/bin/env bash
set -euo pipefail

# External evidence collector for terrain/shader/tree/VSync investigations.
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
PROJECT=${1:-Earth1930}; DURATION=${DURATION:-35}; SIZE=${SIZE:-1280x720}
OUT=${OUT:-"$ROOT/logs/render-profile-$(date +%Y%m%d-%H%M%S)"}
mkdir -p "$OUT"; cd "$ROOT"
export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-Major}"
if [[ ${GPU:-auto} == nvidia ]]; then
  export __NV_PRIME_RENDER_OFFLOAD=1
  export __GLX_VENDOR_LIBRARY_NAME=nvidia
  export __VK_LAYER_NV_optimus=NVIDIA_only
fi
echo "Render profile: project=$PROJECT size=$SIZE duration=${DURATION}s output=$OUT" | tee "$OUT/summary.txt"
date -Is | tee -a "$OUT/summary.txt"; uname -a | tee -a "$OUT/summary.txt"
for cmd in glxinfo vulkaninfo xrandr nvidia-smi; do
  if command -v "$cmd" >/dev/null 2>&1; then
    case "$cmd" in
  glxinfo) "$cmd" -B >"$OUT/$cmd.txt" 2>&1 || true;;
      vulkaninfo) "$cmd" --summary >"$OUT/$cmd.txt" 2>&1 || true;;
      xrandr) "$cmd" --current >"$OUT/$cmd.txt" 2>&1 || true;;
      nvidia-smi) "$cmd" --query-gpu=name,utilization.gpu,utilization.memory,clocks.gr,temperature.gpu --format=csv,noheader >"$OUT/$cmd.txt" 2>&1 || true;;
    esac
  fi
done
GAME="$ROOT/projects/$PROJECT/game"
test -d "$GAME" || { echo "Missing game directory: $GAME" >&2; exit 2; }
find "$GAME/map" -maxdepth 1 -type f -printf '%f %s bytes\n' | sort | tee "$OUT/map-files.txt" >>"$OUT/summary.txt"
find "$GAME/gfx" -type f \( -iname '*.png' -o -iname '*.jpg' -o -iname '*.tga' \) -printf '%s %p\n' | sort -nr >"$OUT/texture-files.txt"
awk 'NR == 1 {largest=$1} {sum += $1; n++} END {printf "textures=%d total_bytes=%d largest_bytes=%s\n", n, sum, (n ? largest : 0)}' "$OUT/texture-files.txt" | tee -a "$OUT/summary.txt"
if ! dotnet build BurgerEngine.sln -c Release -nologo >"$OUT/build.log" 2>&1; then
  # Some distro SDK packages omit workload resolver directories; existing assets
  # are still sufficient for the game project, so retry without restore.
  dotnet build src/Burger.Game/Burger.Game.csproj -c Release --no-restore -nologo >>"$OUT/build.log" 2>&1
fi
set +e
timeout --signal=TERM --kill-after=5s "${DURATION}s" dotnet src/Burger.Game/bin/Release/net8.0/Burger.Game.dll \
  --game "$GAME" --observe --perf --size "$SIZE" --log-dir "$OUT" >"$OUT/game.log" 2>&1
RC=$?; set -e; echo "runtime_exit=$RC" | tee -a "$OUT/summary.txt"
grep -E 'PERF (fps|render|systems|paths|world)|frame p50|avg FPS|map |EndDrawing|simulation ' "$OUT/error.log" "$OUT/game.log" 2>/dev/null | tee "$OUT/perf-lines.txt" || true
{
  echo; echo "Interpretation: map/input/labels/... are CPU submission timings. EndDrawing/flush+swap includes queued rendering, driver work, and VSync wait."
  echo "High EndDrawing with low map points to present/VSync/compositor pacing; high map points to render submission. Shader GPU time is not isolated by --perf."
  echo "Trees are part of map rendering but have no separate timer in the current build. See host snapshots for renderer/GPU/refresh evidence."
} | tee -a "$OUT/summary.txt"
echo "Artifacts: $OUT"
