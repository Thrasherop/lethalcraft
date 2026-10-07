"""Ride a flying machine: build wiki Two-way engine B in the air, fly onto it (creative, real keys), land on its
east slime, start it, and see whether the player is carried. usage: py tools/ride_test.py [flatIdx]"""
import sys, os, time, re, math
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
import flying_machine as FM
from pilot import *
S = R.S
idx = int(sys.argv[1]) if len(sys.argv) > 1 else 13
cmd("god 1"); cmd("clearenemies 80")
if "gm=creative" not in cmd("state"): cmd("gamemode creative")
fc = R.start_flat(idx, [(dx, dz) for dx in range(0, 2) for dz in range(0, 4)])
y = max(R.surface(fc, dx, dz) for dx, dz in ((0, 1), (0, 2), (1, 1), (1, 2))) + (int(sys.argv[2]) if len(sys.argv) > 2 else 12)  # high in the open sky
FM.build("Beast", fc, y)
time.sleep(1.0)
# fly up and come down onto the slime at (1, y, 1)
tx, tz = (fc[0] + 1 + .5) * S, (fc[2] + 1 + .5) * S
go_to(tx + 2.5, tz, stop=0.3)
cmd("keys Space 0.07"); time.sleep(0.18); cmd("keys Space 0.07"); time.sleep(0.3)
up = (y + 3) * S - pos()[1]
cmd(f"keys Space {max(0.5, up / 8 + 0.3):.1f}"); time.sleep(max(0.5, up / 8 + 0.3) + 0.3)
for i in range(3):
    go_to(tx, tz, stop=0.12)
    if math.hypot(pos()[0] - tx, pos()[2] - tz) < 0.25: break
cmd("keys LeftCtrl 3.0"); time.sleep(3.2)
st = cmd("state")
print("standing:", st[:40], re.search(r"fly=\w+ grounded=\w+", st).group(0), "feet cell:", R.feet_cell(), "machine y:", y)
x0 = pos()[0]
cmd(f"placeabs stone {fc[0]} {y} {fc[2] + 4}")   # start it (block in front of the north observer)
t0 = time.time(); rows = []
while time.time() - t0 < 4:
    nb = R.near_blocks()
    east = [k for k, v in nb.items() if v[0] == "slime" and k[1] == y and k[2] == fc[2] + 1]
    px, py_, pz = pos()
    rel = round(px / S - (east[0][0] + .5), 2) if east else None   # player x minus the east slime's centre, in blocks
    rows.append((round(time.time() - t0, 2), east[0][0] - fc[0] if east else None, rel, round(pz / S - (fc[2] + 1.5), 2), round(py_, 1)))
    time.sleep(0.15)
print("time, east slime x, player x-off, z-off (blocks), player y:"); [print("  ", r) for r in rows]
shot("ride")
