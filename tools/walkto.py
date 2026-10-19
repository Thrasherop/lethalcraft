"""Hand-play helper: walk to a point (x z) like a player, facing it: py tools/walkto.py x z [stop=2]"""
import sys, os, re, math, time
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd
tx, tz = float(sys.argv[1]), float(sys.argv[2]); stop = float(sys.argv[3]) if len(sys.argv) > 3 else 2.0
last = None
for _ in range(80):
    m = re.search(r"pos=(-?[\d.]+),(-?[\d.]+),(-?[\d.]+)", cmd("state")); p = tuple(float(v) for v in m.groups())
    dx, dz = tx - p[0], tz - p[2]; d = math.hypot(dx, dz)
    if d <= stop: print(f"there: d={d:.1f} pos={p[0]:.1f},{p[1]:.1f},{p[2]:.1f}"); break
    if last and math.hypot(p[0]-last[0], p[2]-last[2]) < 0.05: cmd("jump")
    last = p
    cmd(f"look {(math.degrees(math.atan2(dx, dz)) + 360) % 360:.0f} 0")
    cmd(f"walk 1 0 {min(1.0, d / 4.5):.2f}"); time.sleep(min(1.0, d / 4.5) + 0.1)
else: print(f"stopped at {p[0]:.1f},{p[1]:.1f},{p[2]:.1f} d={d:.1f}")
