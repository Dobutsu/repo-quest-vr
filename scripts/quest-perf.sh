#!/usr/bin/env bash
# Frame rate per level from the headset log: the Quest runtime logs a VrApi line every second (FPS, App GPU time,
# CPU&GPU time, stale frames); the game logs level changes. Target at 72 Hz: 72 FPS, 13.9 ms.
#   scripts/quest-perf.sh [log] [--since HH:MM] [--pid PID]
# Default log: work/quest-lights-logcat.txt (the running recorder); default: the game's latest process.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
log="$root/work/quest-lights-logcat.txt"
since=""
pid=""
while [ $# -gt 0 ]; do
    case "$1" in
        --since) since="$2"; shift 2 ;;
        --pid) pid="$2"; shift 2 ;;
        *) log="$1"; shift ;;
    esac
done

python3 - "$log" "$since" "$pid" <<'PY'
import re, sys
from statistics import median

log, since, pid = sys.argv[1], sys.argv[2], sys.argv[3]
lines = open(log, errors="ignore").read().splitlines()

# The game's process: the pid of its latest "Unity" line unless given
if not pid:
    for l in reversed(lines):
        m = re.match(r"\S+ \S+ +(\d+) +\d+ [VDIWEF] Unity", l)
        if m:
            pid = m.group(1)
            break
start = None
for i, l in enumerate(lines):
    if re.match(rf"\S+ \S+ +{pid} ", l):
        start = i
        break
if start is None:
    sys.exit(f"no game process {pid} in {log}")

level, order, data = "(start)", [], {}
for l in lines[start:]:
    m = re.match(r"(\S+) (\S+)", l)
    if not m or (since and m.group(2) < since):
        continue
    lv = re.search(r"(?:updated|Changed) level to: Level - (.+)$", l)
    if lv and re.match(rf"\S+ \S+ +{pid} ", l):
        level = f"{lv.group(1).strip()} @{m.group(2)[:5]}"
        order.append(level)
        continue
    if "VrApi" in l and "FPS=" in l:
        f = re.search(r"FPS=(\d+)/(\d+)", l)
        app = re.search(r"App=([\d.]+)ms", l)
        both = re.search(r"CPU&GPU=([\d.]+)ms", l)
        stale = re.search(r"Stale=(\d+)", l)
        data.setdefault(level, []).append((int(f.group(1)), float(app.group(1)) if app else 0.0,
                                           float(both.group(1)) if both else 0.0, int(stale.group(1)) if stale else 0))

print(f"game process {pid}; target 72 FPS / 13.9 ms")
print(f"{'level':28s} {'sec':>4s} {'FPS avg':>7s} {'med':>4s} {'p10':>4s} {'<45%':>5s} {'GPU ms':>6s} {'CPU&GPU':>7s} {'stale/s':>7s}")
for lv in ["(start)"] + order:
    v = data.get(lv)
    if not v or len(v) < 5:
        continue
    fps = sorted(x[0] for x in v)
    n = len(fps)
    print(f"{lv[:28]:28s} {n:4d} {sum(fps)/n:7.1f} {fps[n//2]:4d} {fps[n//10]:4d} "
          f"{100*sum(1 for x in fps if x < 45)//n:4d}% {median(x[1] for x in v):6.1f} "
          f"{median(x[2] for x in v):7.1f} {sum(x[3] for x in v)/n:7.1f}")
PY
