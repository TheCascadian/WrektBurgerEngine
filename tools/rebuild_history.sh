#!/usr/bin/env bash
# Rebuilds the start dates of a project after the gazetteer, the renames or the map changed: who owns each location at every date, the states cut along those borders,
# the snapshots, and the tests. usage: bash tools/rebuild_history.sh [--quick] [project dir]
#   --quick: only burger validate, no timeline-test and society-test (about 10 minutes less)
# Prints the time of each step. About 15 minutes with --quick, 25 without.
set -e
cd "$(dirname "$0")/.."
QUICK=0; if [ "$1" = "--quick" ]; then QUICK=1; shift; fi
P="${1:-projects/World500BCE}"
B=src/Burger.Tools/bin/Release/net8.0/burger.exe
W="${TMPDIR:-${TEMP:-/tmp}}/rebuild_history"; mkdir -p "$W"
t() { echo "[$(date +%H:%M:%S)] $1"; }
t "export location centres"
$B export-location-centres $P $W/locs.csv | tail -1
t "refine_states: who owns each location at every date"
python tools/refine_states.py $W/locs.csv $W/plan.txt 1 1 model 2>&1 | tail -1
t "split-states"
$B split-states $P --plan $W/plan.txt | tail -1
$B export-state-centres $P tools/historical-basemaps/states.csv | tail -1
t "history build (snapshots, countries, fused states)"
python tools/import-history.py build 2>&1 | tail -1
t "validate"
$B validate $P | tail -2
if [ $QUICK = 0 ]; then
  t "timeline-test"; $B timeline-test $P | tail -6
  t "society-test"; $B society-test $P | tail -2
fi
t "done"
