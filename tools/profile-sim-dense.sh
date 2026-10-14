#!/usr/bin/env bash
set -euo pipefail
PROJECT=${1:-SampleGame}
DAYS=${2:-365}
OUT=${OUT:-"${TMPDIR:-/tmp}/burger-profile-dense-$(date +%Y%m%d-%H%M%S)"}
if [[ $PROJECT == -h || $PROJECT == --help ]]; then
  echo 'Usage: tools/profile-sim-dense.sh [PROJECT] [DAYS]'; exit 0
fi
[[ "$DAYS" =~ ^[1-9][0-9]*$ ]] || { echo 'DAYS must be a positive integer' >&2; exit 2; }
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
p=Path(sys.argv[1]); lines=p.read_text(errors='replace').splitlines(); project=sys.argv[2]; days=int(sys.argv[3]); rc=int(sys.argv[4]); out=Path(sys.argv[5])
raw={}; daily={}
for line in lines:
 m=re.fullmatch(r'\s*PROFILE_RAW\s+(\S+)\s+total_ms=([0-9.]+)\s+calls=(\d+)\s*',line)
 if m: raw[m.group(1)]=(float(m.group(2)),int(m.group(3))); continue
 m=re.fullmatch(r'\s*PROFILE_DAY\s+(\S+)\s+ms_per_day=([0-9.]+)\s+calls_per_day=([0-9.]+)\s+max_day_ms=([0-9.]+)\s+p99_day_ms=([0-9.]+)\s+worst_days=([0-9,]*)\s*',line)
 if m: daily[m.group(1)]=tuple(map(float,m.group(2,3,4,5)))+(m.group(6),)
metrics=[]
for name,(total,calls) in raw.items():
 d=daily.get(name,(total/days,calls/days,0.0,0.0,''))
 metrics.append((name,total,calls,*d))
metrics.sort(key=lambda r:(-r[3],r[0]))
(out/'all-metrics.tsv').write_text('scope\ttotal_ms\tcalls\tms_per_day\tcalls_per_day\tmax_day_ms\tp99_day_ms\tworst_days\n'+''.join(f'{n}\t{t:.6f}\t{c}\t{md:.6f}\t{cd:.6f}\t{mx:.6f}\t{p99:.6f}\t{wd}\n' for n,t,c,md,cd,mx,p99,wd in metrics))
ai=[]; others=[]
for row in metrics:
 m=re.fullmatch(r'(Ai\.MonthlyTick\.[^\[]+)\[([^\]]+)\]',row[0])
 if m: ai.append((m.group(1),m.group(2),row))
 else: others.append(row)
(out/'ai-country-calls.tsv').write_text('scope\tcountry\ttotal_ms\tcalls\tcalls_per_day\n'+''.join(f'{scope}\t{tag}\t{r[1]:.6f}\t{r[2]}\t{r[4]:.6f}\n' for scope,tag,r in sorted(ai)))
agg={}
for scope,tag,r in ai:
 a=agg.setdefault(scope,[0.0,0,0.0]); a[0]+=r[1]; a[1]+=r[2]; a[2]+=r[4]
rank_norm=sorted(others,key=lambda r:(-r[3],r[0])); rank_total=sorted(others,key=lambda r:(-r[1],r[0]))
sim=next((re.search(r'simulated\s+(\d+) days in\s+(\d+) ms',x) for x in lines if 'simulated ' in x and ' days in ' in x),None)
world=next((x.strip() for x in lines if x.startswith('  simulated ')),'')
counts=[r for r in metrics if r[0].endswith('#')]
report=[f'{project} | {days} days | exit {rc}',f'Total sim: {sim.group(2)} ms; {int(sim.group(2))/days:.2f} ms/day' if sim else 'Total sim time unavailable',world[:220], 'TOP TIME / SIM-DAY (nested scopes overlap):']
report += [f'{i}. {r[0]} {r[3]:.3f} ms/day; {r[4]:.2f} calls/day; max {r[5]:.2f}ms p99 {r[6]:.2f}ms' for i,r in enumerate(rank_norm[:10],1)]
report += ['AI MONTHLY CHILD SCOPES (per-country call counts in ai-country-calls.tsv):']
report += [f'{n}: {v[0]:.1f}ms total; {v[1]} calls; {v[2]:.2f}/day' for n,v in sorted(agg.items(),key=lambda x:-x[1][0])[:8]]
report += ['TOP RAW TOTALS (ms; calls):']+[f'{r[0]} {r[1]:.1f}; {r[2]}' for r in rank_total[:5]]
if counts: report += ['COUNTERS/day: '+', '.join(f'{r[0]} {r[4]:.2f}' for r in sorted(counts,key=lambda r:-r[4])[:6])]
report += [f'Scopes: {len(raw)} | per-country AI entries: {len(ai)}',f'Full data: {out}/all-metrics.tsv; {out}/ai-country-calls.tsv; {p}']
text='\n'.join(x for x in report if x)
while len(text)>2990 and len(report)>7:
 report.pop(-4 if len(report)>8 else -2); text='\n'.join(x for x in report if x)
print(text[:2999])
PY
cp "$OUT/profile.log" "$OUT/raw.log"
exit "$RC"
