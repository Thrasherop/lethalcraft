"""Hand-play helper: fight the nearest live mob of a kind with the held weapon, like a player: face it, close in, swing
(the real input path, `mouse left`), watch your own health. py tools/fight.py <MobName> [maxSeconds=90] [godBelow=35]"""
import sys, os, re, math, time
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd
name = sys.argv[1]; until = time.time() + (float(sys.argv[2]) if len(sys.argv) > 2 else 90); god_below = int(sys.argv[3]) if len(sys.argv) > 3 else 35
kills = 0; seen = set()
def mobs():
    out = []
    for line in cmd("enemies").split(" ; "):
        m = re.match(r"\s*%s@(-?[\d.]+),(-?[\d.]+),(-?[\d.]+) d=([\d.]+)" % re.escape(name), line)
        if m and "dead=False" in line: out.append(tuple(float(v) for v in m.groups()))
    return out
while time.time() < until:
    st = cmd("state"); hp = int(re.search(r"hp=(\d+)", st).group(1))
    if "dead=True" in st: print("died"); break
    if hp < god_below and "god=True" not in cmd("god"): pass
    if hp < god_below: print(f"hp {hp}: god on"); cmd("god 1"); god_below = -1
    p = tuple(float(v) for v in re.search(r"pos=(-?[\d.]+),(-?[\d.]+),(-?[\d.]+)", st).groups())
    ms = mobs()
    if not ms: print("none left"); break
    t = min(ms, key=lambda m: m[3])
    dx, dz = t[0] - p[0], t[2] - p[2]; d = math.hypot(dx, dz)
    yaw = (math.degrees(math.atan2(dx, dz)) + 360) % 360
    pitch = -math.degrees(math.atan2(t[1] + 1.2 - (p[1] + 2.2), max(d, 0.1)))
    cmd(f"look {yaw:.0f} {pitch:.0f}")
    if d > 1.6: cmd(f"walk 1 0 {min(0.5, (d - 1.4) / 4.5 + 0.05):.2f}"); time.sleep(0.35)
    else: cmd("mouse left"); time.sleep(0.7)
    n = len(ms)
    if n < len(seen) if seen else False: pass
print("hp", re.search(r"hp=(\d+)", cmd("state")).group(1), "left", len(mobs()))
