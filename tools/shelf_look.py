"""#237: an oak shelf on a stone wall, a log, a diamond and a torch put in its three slots with [E] (as a player does),
looked at from the front and from 45 degrees. usage: py tools/shelf_look.py <shot-prefix>"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from regress import cmd, pos, S, place, surface
tag = sys.argv[1] if len(sys.argv) > 1 else "shelf"
if "inShipPhase=True" in cmd("state"): print("land:", R.land(0)); time.sleep(2)
cmd("craftui close"); cmd("god 1"); cmd("gamemode survival"); cmd("clearenemies 50 all"); cmd("dayfreeze 1"); cmd("daytime 0.4")
fc = R.start_flat(7, [(dx, dz) for dx in (-2, -1, 0, 1, 2) for dz in range(0, 5)])
sy = max(surface(fc, dx, 3) for dx in (-1, 0, 1)) + 1
# a stone wall two high at dz 4, the shelf on its face at dz 3 (facing us, -z), at eye height
for dx in (-1, 0, 1):
    for y in (sy, sy + 1): place("stone", fc, dx, y, 4)
print(place("oak_shelf", fc, 0, sy + 1, 3, facing=3))
time.sleep(1.0)
cmd("clearinv"); cmd("invgive oak_log 1"); cmd("invgive diamond 1"); cmd("invgive torch 1"); time.sleep(2.0)
m = re.search(r"oak_shelf\((-?\d+), (-?\d+), (-?\d+)\)y(\d+)", cmd("near 6"))
cx, cy, cz, yo = (int(m.group(i)) for i in range(1, 5))
mid = ((cx + .5) * S, cy * S + yo / 1000 * S + 0.5 * S, (cz + .5) * S)
cmd(f"tp {mid[0]:.2f} {(sy) * S + R.YO + 0.1:.2f} {(cz - 1.6) * S:.2f}"); time.sleep(1.2)
def select(name):
    sl = re.search(r"slots=\[([^\]]*)\]", cmd("state")).group(1).split(",")
    i = next((i for i, e in enumerate(sl) if e.startswith(name)), None)
    if i is not None: cmd(f"slot {i}"); time.sleep(0.3)
    return i
# its slots from the left as you face it: left, middle, right
for name, off in (("Oak Log", -0.3125), ("Diamond", 0.0), ("Torch", 0.3125)):
    select(name)
    R.look_at(mid[0] + off * S, mid[1], mid[2] - 0.45 * S); time.sleep(0.4)  # (you face +z: your left is -x)
    cmd("keys E 0.1"); time.sleep(0.8)
print("left in hand:", re.search(r"slots=\[([^\]]*)\]", cmd("state")).group(1))
for name, (dx, dz) in (("front", (0.0, -2.0)), ("angle", (1.6, -1.6)), ("side", (2.0, 0.0))):
    cmd(f"tp {mid[0] + dx * S:.2f} {sy * S + R.YO + 0.1:.2f} {mid[2] + dz * S:.2f}"); time.sleep(1.2)
    R.look_at(*mid); time.sleep(0.4)
    cmd(f"screenshot E:/claude/mods/lmc_lect238/shots/{tag}_{name}.png"); time.sleep(0.6)
print("done")
