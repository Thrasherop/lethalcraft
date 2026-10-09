"""#55: an item left on blocks attached to the ship (a porch, a house on the roof) is still there after taking off and
landing again. Builds a stone walkway out of the hangar door (moon blocks touching the ship: they join it at takeoff),
puts a stack of bread on its outer end, takes off, lands again, and looks for the bread on the walkway.
usage: py tools/ship_items.py [old]   (old: shipcarry 0, items on ship blocks aren't the ship's)"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from dev import cmd
S = R.S

def ship_xyz():
    return tuple(map(float, re.search(r"elevator=([-\d.]+),([-\d.]+),([-\d.]+)", cmd("ship")).groups()))

def bread():
    out = []
    for e in cmd("find bread").split(" ; "):
        m = re.search(r"Breadx(\d+)@([-\d.]+),([-\d.]+),([-\d.]+)", e)
        if m: out.append(tuple(map(float, m.groups()[1:])))
    return out

def land():
    cmd("route 0"); time.sleep(5); cmd("land")
    for _ in range(60):
        time.sleep(2)
        if "landed=True" in cmd("leave_check"): break
    time.sleep(6)

def leave():
    cmd("leave")
    for _ in range(90):
        time.sleep(2)
        if "inShipPhase=True" in cmd("state"): break
    time.sleep(3)

print(cmd("shipcarry " + ("0" if "old" in sys.argv else "1")))
if "inShipPhase=True" in cmd("state"): land()
cmd("god 1"); cmd("clearenemies 80"); cmd("gamemode creative"); cmd("clearinv")
cmd("tpship"); time.sleep(1.0)
for cx in (-6, -7, -8, -9):
    cmd(f"placeabs stone {cx} -1 -10")
time.sleep(1.0)
# a stack of bread on the walkway's outer block, past the ship's own box (x -9.1)
cx, cy, cz = -9, -1, -10
sx, sy, sz = ship_xyz()
cmd(f"giveat {(cx + .5) * S:.2f} {(cy + 1.6) * S:.2f} {(cz + .5) * S:.2f} bread 3"); time.sleep(3.0)
b0 = bread()
rel0 = [(x - sx, y - sy, z - sz) for x, y, z in b0]
print("bread on the walkway:", b0, "relative to the ship:", rel0)
leave()
print("in orbit:", bread())
land()
sx, sy, sz = ship_xyz()
b1 = bread()
rel1 = [(x - sx, y - sy, z - sz) for x, y, z in b1]
print("landed again:", b1, "relative to the ship:", rel1)
same = any(abs(a[0] - b[0]) < 0.6 and abs(a[1] - b[1]) < 0.6 and abs(a[2] - b[2]) < 0.6 for a in rel0 for b in rel1)
print("RESULT:", "KEPT (still on the walkway)" if same else "LOST" if not b1 else "MOVED")
cmd("gamemode survival")
