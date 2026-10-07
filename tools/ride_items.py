"""Do items on a moving block ride with it? Builds wiki engine B high in the sky, puts a stack on its east slime,
starts it and tracks the stack. usage: py tools/ride_items.py [flatIdx]"""
import sys, os, time, re, math
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
import flying_machine as FM
from pilot import *
S = R.S
idx = int(sys.argv[1]) if len(sys.argv) > 1 else 21
cmd("god 1"); cmd("clearenemies 80"); cmd("clearinv")
# high above the ship: open sky, nothing to run into (blocks need no support)
cmd("tpship"); time.sleep(1.0)
f = R.feet_cell(); fc = [f[0] - 6, f[1], f[2] - 2]; y = f[1] + 25
FM.build("Beast", fc, y)
time.sleep(1.0)
tx, tz = (fc[0] + 1 + .5) * S, (fc[2] + 1 + .5) * S
def item():
    ms = [tuple(map(float, m)) for m in re.findall(r"Oak Planks(?:x\d+)?@([-\d.]+),([-\d.]+),([-\d.]+) held=False", cmd("find oak planks"))]
    ms = [m for m in ms if m[1] > (y - 3) * S]   # the one up at the machine (old test stacks lie on the ground)
    return min(ms, key=lambda m: abs(m[2] - tz)) if ms else None
cmd(f"giveat {tx:.2f} {(y + 2.6) * S:.2f} {tz:.2f} oak_planks 5"); time.sleep(2.0)
it = item()
print("stack resting at", it, "| slime top y", round((y + 1) * S, 2), "| on it:", it is not None and abs(it[1] - (y + 1) * S) < 1.2)
cmd(f"placeabs stone {fc[0]} {y} {fc[2] + 4}")
t0 = time.time(); rows = []
while time.time() - t0 < 4:
    m = re.findall(r"slime\((-?\d+), %d, %d\)" % (y, fc[2] + 1), cmd(f"near 60 {tx:.2f} {y * S:.2f} {tz:.2f}"))
    it = item()
    rows.append((round(time.time() - t0, 1), int(m[0]) - fc[0] if m else None, round(it[0] / S - fc[0], 2) if it else None, round(it[1] / S - y, 2) if it else None))
    time.sleep(0.4)
print("time, east slime x, stack x (cells), stack height above the machine (cells):"); [print("  ", r) for r in rows]
