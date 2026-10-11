#!/usr/bin/env bash
set -euo pipefail
PROJECT=${1:-Earth1930}
DAYS=${2:-365}
OUT=${OUT:-"${TMPDIR:-/tmp}/burger-profile-dense-$(date +%Y%m%d-%H%M%S)"}
if [[ $PROJECT == -h || $PROJECT == --help ]]; then
  echo 'Usage: bash profile-sim-dense.sh [PROJECT] [DAYS]'; exit 0
fi
[[ $DAYS =~ ^[1-9][0-9]*$ ]] || { echo 'DAYS must be a positive integer' >&2; exit 2; }
ROOT=${REPO_ROOT:-}
if [[ -z $ROOT ]]; then
  ROOT=$(git rev-parse --show-toplevel 2>/dev/null || true)
  [[ -n $ROOT ]] || ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
fi
[[ -d "$ROOT/projects/$PROJECT/game" ]] || { echo "Missing project: $ROOT/projects/$PROJECT/game" >&2; exit 2; }
mkdir -p "$OUT"
cd "$ROOT"
export DOTNET_ROLL_FORWARD=${DOTNET_ROLL_FORWARD:-Major}
if ! dotnet build BurgerEngine.sln -c Release -nologo >"$OUT/build.log" 2>&1; then
  dotnet build src/Burger.Tools/Burger.Tools.csproj -c Release --no-restore -nologo >>"$OUT/build.log" 2>&1
fi
set +e
dotnet src/Burger.Tools/bin/Release/net8.0/burger.dll validate "$PROJECT" --days "$DAYS" --profile >"$OUT/profile.log" 2>&1
RC=$?
set -e
python3 - "$OUT/profile.log" "$PROJECT" "$DAYS" "$RC" "$OUT" <<'PY'
import re, sys
from pathlib import Path
p=Path(sys.argv[1]); log=p.read_text(errors='replace'); project=sys.argv[2]; days=int(sys.argv[3]); rc=int(sys.argv[4]); out=Path(sys.argv[5]); lines=log.splitlines()
s=next((x.split(':',1)[1].strip() for x in lines if x.startswith('  time per system ')), '')
items=[]
for x in s.split(','):
 m=re.fullmatch(r'\s*(.*?)\s+([0-9]+(?:\.[0-9]+)?)\s*',x)
 if m: items.append((m.group(1),float(m.group(2))))
items.sort(key=lambda x:x[1],reverse=True)
(out/'all-metrics.tsv').write_text('name\tper-day\ttype\n'+''.join(f'{n}\t{v/days:.6f}\t{"count/day" if n.endswith("#") else "ms/day"}\n' for n,v in items))
t=next((re.search(r'simulated\s+(\d+) days in\s+(\d+) ms',x) for x in lines if 'simulated ' in x and ' days in ' in x),None)
world=next((x.strip() for x in lines if x.startswith('  simulated ')),'')
time=[(n,v) for n,v in items if not n.endswith('#')]
counts=[(n[:-1],v/days) for n,v in items if n.endswith('#')]
r=[f'{project} | {days}d | exit {rc}', f'Total {t.group(2)} ms; {int(t.group(2))/days:.2f} ms/sim-day' if t else 'Total runtime unavailable',world[:240], 'TOP SCOPES ms/sim-day (nested scopes overlap):']
r += [f'{i:2}. {v:.3f} {n}' for i,(n,v) in enumerate(time[:24],1)]
if counts: r += ['COUNTS/day: '+', '.join(f'{n} {v:.2f}' for n,v in counts[:8])]
st=next((x.strip() for x in lines if x.startswith('OK -') or x.startswith('ERROR')),'')
if st:r.append(st)
r.append(f'Raw {p}; all metrics {out/"all-metrics.tsv"}')
text='\n'.join(x for x in r if x)
if len(text)>2999:
 while len(text)>2999 and len(r)>7:
  r.pop(-3 if counts else -2); text='\n'.join(x for x in r if x)
print(text[:2999])
PY
cp "$OUT/profile.log" "$OUT/raw.log"
exit "$RC"
