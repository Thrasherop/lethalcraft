"""Play check: pillar up with real input (look down, jump, hold right-click), in the open and out of a dug shaft.
usage: py tools/pillar_play.py [moonIdx]  (lands if in orbit)"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from pilot import *
if "inShipPhase=True" in cmd("state"): print("land:", R.land(int(sys.argv[1]) if len(sys.argv) > 1 else 0)); time.sleep(3)
cmd("god 1"); cmd("clearenemies"); cmd("clearinv"); time.sleep(0.3)
fc = R.start_flat(40, [(0, 0)])
cmd("give cobblestone 20"); cmd("give pickaxe 1"); time.sleep(1.5)
for t in range(3):
    ms = [m for m in re.findall(r"(Cobblestone|Diamond Pickaxe)@([-\d.]+),([-\d.]+),([-\d.]+) d=([\d.]+) held=False", cmd("objs")) if float(m[4]) < 4]
    if not ms: break
    pick_up_all(float(ms[0][1]), float(ms[0][3]), tries=3, y=float(ms[0][2]))

def pillar(n):
    select("Cobblestone"); set_pitch(89); time.sleep(0.3)
    ys = [pos()[1]]
    for i in range(n):
        cmd("keys Space 0.1"); cmd("mouse right 0.7"); time.sleep(1.1)
        ys.append(pos()[1])
    return [round(b - a, 2) for a, b in zip(ys, ys[1:])]

print("open ground, 3 jumps: rise per jump", pillar(3))
print("column under me:", [cmd(f"cellinfo 0 {-k} 0")[:40] for k in (1, 2, 3)])
shot("pillar_open")
# now dig a 4-deep shaft and climb back out the same way
select("Diamond Pickaxe"); set_pitch(89)
y0 = pos()[1]
for i in range(7):
    mine(0.8); time.sleep(0.4)
print(f"dug down {y0 - pos()[1]:.2f} m")
print("out of the shaft, 6 jumps:", pillar(6))
print(f"back to {pos()[1] - y0:+.2f} m of where the shaft started")
set_pitch(30); shot("pillar_shaft")
