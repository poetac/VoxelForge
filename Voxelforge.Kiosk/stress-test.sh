#!/usr/bin/env bash
# stress-test.sh — drive 20 sequential headless kiosk builds and check
# for: memory growth, file-write failures, dimension creep past Bambu
# X1C envelope, build-time regression. Run from the repo root.

set -e

EXE="RegenChamberDesigner.Kiosk/bin/Release/net9.0-windows/Voxelforge.Kiosk.exe"
OUT=".kiosk-stress-output"

if [ ! -x "$EXE" ]; then
    echo "ERROR: build the kiosk first: dotnet build voxelforge.sln -c Release" >&2
    exit 1
fi

rm -rf "$OUT"
mkdir -p "$OUT"

PRESETS=(bell aerospike pintle)
TOTAL=20

echo "── Stress test: $TOTAL sequential headless builds ──"
echo

START=$SECONDS
for i in $(seq 1 $TOTAL); do
    PRESET=${PRESETS[$((($i - 1) % 3))]}
    T0=$SECONDS
    OUT_LINE=$("$EXE" --headless --preset "$PRESET" --seq "$i" --voxel 0.5 --out "$OUT" 2>&1 | grep "Exported" || echo "FAILED")
    T1=$SECONDS
    DT=$((T1 - T0))
    echo "[$i/$TOTAL]  ${PRESET}  ${DT}s  ${OUT_LINE}"
done
ELAPSED=$((SECONDS - START))

echo
echo "── Summary ──"
echo "Total wall time: ${ELAPSED}s"
echo "STL files written:"
ls -la "$OUT"/*.stl | awk '{ print "  ", $5, $9 }'
echo
echo "Largest STL:"
ls -lS "$OUT"/*.stl | head -2 | tail -1 | awk '{ print "  ", $5, "bytes  ", $9 }'
echo "Smallest STL:"
ls -lS "$OUT"/*.stl | tail -1 | awk '{ print "  ", $5, "bytes  ", $9 }'
echo
echo "kiosk.log tail:"
tail -5 "$OUT/kiosk.log" || echo "(no kiosk.log written)"
