"""Play check: hand-built observer -> lamp. Place a lamp, an observer against it (face toward me), a stone in front of
the observer, then break the stone by hand: the lamp must flash. usage: py tools/obs_play.py [flatIdx]"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from pilot import *
S = R.S
idx = int(sys.argv[1]) if len(sys.argv) > 1 else 20
if "inShipPhase=True" in cmd("state"): print("land:", R.land(int(sys.argv[2]) if len(sys.argv) > 2 else 12)); time.sleep(3)
cmd("god 1"); cmd("clearinv"); cmd("clearenemies"); time.sleep(0.4)
fc = R.start_flat(idx, [(1, 0), (2, 0), (3, 0), (4, 0)])
print("start", fc)
if fc is None: sys.exit("no flat spot")
for k, n in (("redstone_lamp", 1), ("observer", 1), ("stone", 2), ("pickaxe", 1)): cmd(f"give {k} {n}")
time.sleep(1.5)
for t in range(4):
    ms = [m for m in re.findall(r"(Redstone Lamp|Observer|Stone|Diamond Pickaxe)@([-\d.]+),([-\d.]+),([-\d.]+) d=([\d.]+) held=False", cmd("objs")) if float(m[4]) < 4]
    if not ms: break
    pick_up_all(float(ms[0][1]), float(ms[0][3]), tries=3, y=float(ms[0][2]))
print(slots())

def blocks():
    return {k: v for k, v in R.near_blocks().items() if v[0] in ("redstone_lamp", "observer", "stone")}

def wpos(key, cell):
    for e in cmd(f"where {key}").split(" ; "):
        m = re.match(r"\((-?\d+), (-?\d+), (-?\d+)\)@([-\d.]+),([-\d.]+),([-\d.]+)", e.strip())
        if m and tuple(map(int, m.groups()[:3])) == cell: return tuple(map(float, m.groups()[3:]))

def face_of(key, cell, d):
    x, y, z = wpos(key, cell)
    return x + d[0] * S * 0.49, y + d[1] * S * 0.49, z + d[2] * S * 0.49

# step back 2 cells first so the stone (3rd block toward me) isn't in my own cell
x0, y0, z0 = pos()
cmd("look 90 0"); time.sleep(0.3)
select("Redstone Lamp"); set_pitch(28); use(); time.sleep(0.6)
b = blocks(); print("lamp:", b)
lk = next(k for k, v in b.items() if v[0] == "redstone_lamp")
go_to((lk[0] - 3.2) * S, (lk[2] + .5) * S, stop=0.3)
select("Observer"); aim_at(*face_of("redstone_lamp", lk, (-1, 0, 0))); use(); time.sleep(0.6)
b = blocks(); print("observer:", {k: v for k, v in b.items() if v[0] == "observer"})
ok = next((k for k, v in b.items() if v[0] == "observer"), None)
select("Stone"); aim_at(*face_of("observer", ok, (-1, 0, 0))); use(); time.sleep(1.2)
print("built:", blocks())
shot("obs_built")
lamp_before = blocks()[lk]
sk = next(k for k, v in blocks().items() if v[0] == "stone")
select("Diamond Pickaxe"); aim_at(*wpos("stone", sk))
cmd("mouse left 1.2")
seen = []
t0 = time.time()
while time.time() - t0 < 3.0:
    v = blocks().get(lk); seen.append((round(time.time() - t0, 2), v[1] if v else None))
    time.sleep(0.05)
print("lamp before:", lamp_before, "states over time:", [s for s in seen if s[1] != lamp_before[1]][:10] or "never changed")
print("after:", blocks())
