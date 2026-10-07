"""Play check (real input, max look-down): build a pillar by jumping, dig back down through it, dig a 1x1 shaft into
the ground, then pillar out of the shaft. usage: py tools/shaft_play.py [moonIdx] [flatIdx]"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from pilot import *
S = R.S
if "inShipPhase=True" in cmd("state"): print("land:", R.land(int(sys.argv[1]) if len(sys.argv) > 1 else 0)); time.sleep(3)
cmd("god 1"); cmd("clearenemies"); cmd("clearinv"); time.sleep(0.3)
fc = R.start_flat(int(sys.argv[2]) if len(sys.argv) > 2 else 20, [(0, 0)])
print("spot:", fc)
cmd("give cobblestone 24"); cmd("give pickaxe 1"); time.sleep(1.5)
for t in range(3):
    ms = [m for m in re.findall(r"(Cobblestone|Diamond Pickaxe)@([-\d.]+),([-\d.]+),([-\d.]+) d=([\d.]+) held=False", cmd("objs")) if float(m[4]) < 4]
    if not ms: break
    pick_up_all(float(ms[0][1]), float(ms[0][3]), tries=3, y=float(ms[0][2]))
print(slots())

def pillar(n):
    select("Cobblestone"); set_pitch(89); time.sleep(0.3)
    ys = [pos()[1]]
    for i in range(n):
        cmd("keys Space 0.1"); cmd("mouse right 0.7"); time.sleep(1.1); ys.append(pos()[1])
    return [round(b - a, 2) for a, b in zip(ys, ys[1:])]

def dig_down(n, hold=1.5):
    select("Diamond Pickaxe"); set_pitch(89); time.sleep(0.3)
    ys = [pos()[1]]
    for i in range(n):
        what = cmd("mine?")[:60]
        mine(hold); time.sleep(0.5); ys.append(pos()[1])
        print(f"   dig {i}: {what} -> fell {ys[-2] - ys[-1]:.2f}")
    return [round(a - b, 2) for a, b in zip(ys, ys[1:])]

g0 = pos()[1]
print("pillar up 3:", pillar(3))
print("dig back down through it:", dig_down(3))
print(f"  now {pos()[1] - g0:+.2f} m from the ground")
print("dig a shaft 3 deep:", dig_down(3))
print("  walls at the feet:", [cmd(f"cellinfo {dx} 0 {dz}").split(" gridY")[0].split(") ")[-1] for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1))])
print(f"  now {pos()[1] - g0:+.2f} m")
print("pillar out:", pillar(4), f"-> {pos()[1] - g0:+.2f} m", slots()[:2])
