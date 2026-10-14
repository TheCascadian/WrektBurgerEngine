#!/usr/bin/env bash
# Segmented, resumable download of the GEBCO 2026 sub-ice GeoTIFF zip. The server limits each connection to ~100 KB/s,
# so the file is fetched in N parallel byte ranges. Usage: fetch_gebco.sh <output dir> [segments]
set -euo pipefail
OUT=${1:?output dir}; N=${2:-24}
URL="https://dap.ceda.ac.uk/bodc/gebco/global/gebco_2026/sub_ice_topography_bathymetry/geotiff/gebco_2026_sub_ice_topo_geotiff.zip?download=1"
mkdir -p "$OUT/parts"; cd "$OUT"
SIZE=$(curl -sIL "$URL" | awk 'tolower($1)=="content-length:"{s=$2} END{print s+0}')
echo "size $SIZE bytes, $N segments; resumes from parts/ if interrupted"
CH=$(( (SIZE + N - 1) / N ))
(
  prev=0; t0=$(date +%s)
  while sleep 5; do
    cur=$(cat parts/p* 2>/dev/null | wc -c); now=$(date +%s)
    rate=$(( (cur-prev)/5 )); prev=$cur
    if [ "$rate" -gt 0 ]; then eta=$(( (SIZE-cur)/rate/60 )); else eta=-1; fi
    printf '%3d%%  %5d / %d MB  %5d KB/s  ETA %s min  elapsed %ds\n' $((cur*100/SIZE)) $((cur/1048576)) $((SIZE/1048576)) $((rate/1024)) "$eta" $((now-t0))
  done
) &
MON=$!
PIDS=()
for i in $(seq 0 $((N-1))); do
  (
    a=$((i*CH)); b=$((a+CH-1)); [ $b -ge $SIZE ] && b=$((SIZE-1)); want=$((b-a+1)); f=parts/p$i
    for try in 1 2 3 4 5 6 7 8; do
      have=$( [ -f $f ] && stat -c %s $f || echo 0 ); [ "$have" -ge "$want" ] && break
      curl -sfL -r $((a+have))-$b "$URL" >> $f || sleep 3
    done
    [ "$(stat -c %s $f)" -eq "$want" ] || { echo "segment $i incomplete"; exit 1; }
  ) &
  PIDS+=($!)
done
wait "${PIDS[@]}"
kill $MON 2>/dev/null || true
cat $(for i in $(seq 0 $((N-1))); do echo parts/p$i; done) > gebco_2026_sub_ice_topo_geotiff.zip
[ "$(stat -c %s gebco_2026_sub_ice_topo_geotiff.zip)" -eq "$SIZE" ] || { echo "size mismatch"; exit 1; }
rm -rf parts; sha256sum gebco_2026_sub_ice_topo_geotiff.zip | tee SHA256.txt; unzip -l gebco_2026_sub_ice_topo_geotiff.zip
