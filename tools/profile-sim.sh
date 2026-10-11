#!/usr/bin/env bash
set -euo pipefail

# Deterministic, headless simulation profile. This measures the existing fixed
# game.Tick() loop; it never skips ticks or changes scheduling.
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
PROJECT=${1:-Earth1930}
DAYS=${2:-365}
OUT=${OUT:-"${TMPDIR:-/tmp}/burger-profile-sim-$(date +%Y%m%d-%H%M%S)"}
mkdir -p "$OUT"
cd "$ROOT"
export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-Major}"

if [[ ${PROJECT} == -h || ${PROJECT} == --help ]]; then
  cat <<'EOF'
Usage: tools/profile-sim.sh [PROJECT] [DAYS]

Runs the deterministic headless simulation with --profile.
Examples:
  tools/profile-sim.sh Earth1930 365
  OUT=logs/sim-profile tools/profile-sim.sh SampleGame 60
EOF
  exit 0
fi
[[ "$DAYS" =~ ^[1-9][0-9]*$ ]] || { echo "DAYS must be a positive integer" >&2; exit 2; }
GAME="$ROOT/projects/$PROJECT/game"
test -d "$GAME" || { echo "Missing game directory: $GAME" >&2; exit 2; }

echo "Simulation profile: project=$PROJECT days=$DAYS output=$OUT"
if ! dotnet build BurgerEngine.sln -c Release -nologo >"$OUT/build.log" 2>&1; then
  # Supports distro SDK packages whose workload resolver is incomplete while
  # still using the repository's already-restored project assets.
  dotnet build src/Burger.Tools/Burger.Tools.csproj -c Release --no-restore -nologo >>"$OUT/build.log" 2>&1
fi

set +e
dotnet src/Burger.Tools/bin/Release/net8.0/burger.dll validate "$PROJECT" --days "$DAYS" --profile \
  >"$OUT/profile.log" 2>&1
RC=$?
set -e
cp "$OUT/profile.log" "$OUT/raw.log"

echo "runtime_exit=$RC"
grep -E '^(Validating|  loaded|Simulating|  simulated|  time per system|  feature )' "$OUT/profile.log" | tee "$OUT/summary.txt" || true
echo "Artifacts: $OUT"
exit "$RC"
