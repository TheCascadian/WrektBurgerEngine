#!/usr/bin/env bash
set -euo pipefail

# Deterministic, headless simulation profile. This measures the existing fixed
# game.Tick() loop; it never skips ticks or changes scheduling.
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
PROJECT=${1:-Earth1930}
DAYS=${2:-365}
FOCUS=${FOCUS:-}
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
  FOCUS=Ai.MonthlyTick tools/profile-sim.sh Earth1930 365
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
grep -E '(^| )((PERF|Path)\.|time per system|Validating|  loaded|Simulating|  simulated|  feature |  world:)' "$OUT/profile.log" >"$OUT/all-profile-lines.txt" || true
{
  echo "command: dotnet src/Burger.Tools/bin/Release/net8.0/burger.dll validate $PROJECT --days $DAYS --profile"
  echo "host: $(uname -a)"
  echo "date: $(date -Is)"
  echo "dotnet: $(dotnet --version 2>/dev/null || true)"
  echo "git: $(git rev-parse --short HEAD 2>/dev/null || true)"
  echo "runtime_exit: $RC"
  echo "profile_lines: $(wc -l < "$OUT/all-profile-lines.txt")"
} >"$OUT/metadata.txt"
{
  echo "============================================================"
  echo "SIMULATION PROFILE"
  echo "============================================================"
  echo "project       : $PROJECT"
  echo "days          : $DAYS"
  echo "output        : $OUT"
  echo "runtime exit  : $RC"
  echo "focus         : ${FOCUS:-all systems}"
  echo "all profile lines: $OUT/all-profile-lines.txt"
  echo
  echo "RUN"
  echo "------------------------------------------------------------"
  grep -E '^(Validating|  loaded|Simulating|  simulated|  feature )' "$OUT/profile.log" || true
  echo
  echo "SYSTEM TIME (milliseconds per simulated day)"
  echo "------------------------------------------------------------"
  echo " rank  ms/day   share   function"
  echo "------ -------- ------- ------------------------------------"
  grep '^  time per system' "$OUT/profile.log" | sed 's/^  time per system[^:]*: //' |
    tr ',' '\n' | sed 's/^ *//' | awk -F' ' '
      { name=$0; sub(/[[:space:]][0-9.]+$/, "", name); value=$NF; total += value; n++; row[n]=name "\t" value }
      END {
        for (i=1; i<=n; i++) { split(row[i], a, "\t"); printf "%s\t%.6f\n", a[1], a[2] }
      }
    ' | sort -t $'\t' -k2,2nr | awk -F'\t' -v total="$(grep '^  time per system' "$OUT/profile.log" | sed 's/^  time per system[^:]*: //' | tr ',' '\n' | awk '{sum += $NF} END {print sum+0}')" '
      { printf "%5d  %8.2f  %6.1f%%  %s\n", NR, $2, (total ? 100*$2/total : 0), $1 }
    ' | tee "$OUT/system-functions.txt"
  if [[ -n "$FOCUS" ]]; then
    echo
    echo "FOCUSED SYSTEM: $FOCUS"
    echo "------------------------------------------------------------"
    grep '^  time per system' "$OUT/profile.log" | sed 's/^  time per system[^:]*: //' |
      tr ',' '\n' | sed 's/^ *//' | awk -v focus="$FOCUS" '
        { value=$NF; name=$0; sub(/[[:space:]][0-9.]+$/, "", name) }
        name == focus { printf "%s: %.6f ms per simulated day\n", name, value; found=1 }
        END { if (!found) { print "No exact matching system was reported."; exit 1 } }
      ' || true
  fi
  echo
  echo "PATHFINDING"
  echo "------------------------------------------------------------"
  grep '^  ' "$OUT/profile.log" | grep -E '^  (Path\.|time per system)' | tail -1 || true
  grep '^  time per system' "$OUT/profile.log" | tail -1 | sed 's/^  time per system[^:]*: //' |
    tr ',' '\n' | sed 's/^ *Path\./Path./' | grep '^Path\.' || true
  echo
  echo "WORLD / FEATURE SNAPSHOTS"
  echo "------------------------------------------------------------"
  grep -E '^  (simulated|feature )|^  world:' "$OUT/profile.log" || true
  echo
  echo "ALL CAPTURED PROFILE LINES (authoritative)"
  echo "------------------------------------------------------------"
  cat "$OUT/all-profile-lines.txt"
} | tee "$OUT/summary.txt"
echo
echo "Detailed files:"
echo "  $OUT/system-functions.txt  ranked system timings"
echo "  $OUT/profile.log           complete profiler output"
echo "  $OUT/raw.log               raw copy of profiler output"
echo "  $OUT/all-profile-lines.txt every recognized timing/world/profile line"
echo "  $OUT/metadata.txt          command, host, runtime, revision, completeness"
echo "Artifacts: $OUT"
exit "$RC"
