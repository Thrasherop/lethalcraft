"""Hand-play helper: face a mob (by name, from `enemies`) and walk up to it, the way a player would.

usage:  py tools/approach.py <MobName> [stop_distance=2.5] [pitch=20]
"""
import sys, os, re, math, time
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd

def pos_of(name):
    for line in cmd("enemies").split(" ; "):
        m = re.match(r"\s*%s@(-?[\d.]+),(-?[\d.]+),(-?[\d.]+)" % re.escape(name), line)
        if m and "dead=False" in line: return tuple(float(v) for v in m.groups())
    return None

def me():
    m = re.search(r"pos=(-?[\d.]+),(-?[\d.]+),(-?[\d.]+)", cmd("state"))
    return tuple(float(v) for v in m.groups())

name = sys.argv[1]; stop = float(sys.argv[2]) if len(sys.argv) > 2 else 2.5; pitch = sys.argv[3] if len(sys.argv) > 3 else "20"
for _ in range(30):
    t = pos_of(name); p = me()
    if t is None: print("no", name); break
    dx, dz = t[0] - p[0], t[2] - p[2]
    d = math.hypot(dx, dz)
    yaw = (math.degrees(math.atan2(dx, dz)) + 360) % 360
    cmd(f"look {yaw:.0f} {pitch}")
    if d <= stop: print(f"at {name}: d={d:.1f} yaw={yaw:.0f}"); break
    cmd(f"walk 1 0 {min(0.6, (d - stop) / 4.5 + 0.05):.2f}")
    time.sleep(0.7)
