#!/usr/bin/env bash
set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
STAMP=$(date +%Y%m%d-%H%M%S)
OUT=${1:-"$ROOT/WrektBurgerEngine-${STAMP}.zip"}

case "$OUT" in
  /*) ;;
  *) OUT="$ROOT/$OUT" ;;
esac

mkdir -p "$(dirname "$OUT")"
rm -f "$OUT"

command -v zip >/dev/null 2>&1 || {
  echo "zip is required but was not found" >&2
  exit 1
}

cd "$ROOT"

# Package source and build metadata only. Projects contain large maps, textures,
# saves, and generated data that must not be included in the source archive.
FILE_LIST=$(mktemp)
trap 'rm -f "$FILE_LIST"' EXIT
find src tools projects -type f \( \
  -name '*.cs' -o -name '*.csproj' -o -name '*.props' -o -name '*.targets' \
  -o -name '*.py' -o -name '*.sh' -o -name '*.ps1' \
\) -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/__pycache__/*' -print | sort >"$FILE_LIST"
find . -maxdepth 1 -type f \( -name '*.sh' -o -name '*.csproj' -o -name '*.sln' \) -print | sort >>"$FILE_LIST"
zip -q "$OUT" -@ <"$FILE_LIST"

echo "Created $OUT"
du -h "$OUT" | awk '{print "Archive size: " $1}'
unzip -tq "$OUT"
echo "Archive test: OK"
