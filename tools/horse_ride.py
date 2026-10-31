"""#246: ride a horse at a staircase (two stair steps up to a landing, three wide), and at a single block; log how high it
gets. With 'jump': hold [Space] in front of a 1-block wall, then let go.
usage: py tools/horse_ride.py [stairs|block|jump]"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from regress import cmd, pos, place, start_flat, surface, look_at, S
what = sys.argv[1] if len(sys.argv) > 1 else "stairs"
if "inShipPhase=True" in cmd("state"): print("land:", R.land(0)); time.sleep(2)
cmd("craftui close"); cmd("gamemode survival"); cmd("god 1"); cmd("mobspawns 0"); cmd("clearenemies 200 all")
fc = start_flat(int(os.environ.get("SPOT", "12")), [(dx, dz) for dx in (-1, 0, 1) for dz in range(0, 12)])
if not fc: sys.exit("no flat spot")
sy = max(surface(fc, dx, dz) for dx in (-1, 0, 1) for dz in range(0, 12))
print("ground under the line:", [surface(fc, 0, dz) for dz in range(0, 12)], "sy", sy)
narrow = what == "stairs1"
if narrow: what = "stairs"
for dx in ((0,) if narrow or what == "wall2" else (-1, 0, 1)):
    if not narrow and what != "wall2":
        for dz in range(0, 12): place("stone", fc, dx, sy + 1, dz)
    if what == "stairs":
        b = sy + 2 if not narrow else sy + 1
        place("cobblestone_stairs", fc, dx, b, 6, facing=3)
        place("stone", fc, dx, b, 7); place("cobblestone_stairs", fc, dx, b + 1, 7, facing=3)
        for dz in range(8, 12): place("stone", fc, dx, b, dz); place("stone", fc, dx, b + 1, dz)
    elif what == "wall2":
        for dz in range(6, 8): place("stone", fc, dx, sy + 2, dz); place("stone", fc, dx, sy + 3, dz)
    else:
        for dz in range(6, 12): place("stone", fc, dx, sy + 2, dz)
time.sleep(3.0)
floor = (sy + 2) * S + R.YO if not narrow and what != "wall2" else pos()[1]
cmd(f"tp {(fc[0] + .5) * S:.2f} {floor + 0.3:.2f} {(fc[2] + 1.5) * S:.2f}"); time.sleep(1.5)
cmd("look 0 0"); print(cmd("enemy horse 3")); time.sleep(1.0)
cmd("horse saddle"); time.sleep(0.3)
for attempt in range(4):
    cmd(f"tp {(fc[0] + .5) * S:.2f} {floor + 0.3:.2f} {(fc[2] + 1.5) * S:.2f}"); time.sleep(0.6)
    cmd("look 0 0"); cmd("enemyhold horse 2.8"); cmd("enemyrelease horse"); time.sleep(0.3)
    a = [x for x in cmd("animals").split(" ; ") if "Horse@" in x]
    x, y, z = map(float, re.search(r"@([-\d.]+),([-\d.]+),([-\d.]+)", a[0]).groups())
    look_at(x, y + 1.1, z); time.sleep(0.2); cmd("keys E 0.1"); time.sleep(0.8)
    if "riding=True" in cmd("horse"): break
print("riding:", cmd("horse"))
cmd(f"look 0 0"); time.sleep(0.3)
def hy():
    m = re.search(r"Horse@([-\d.]+),([-\d.]+),([-\d.]+)", cmd("animals")); return tuple(map(float, m.groups()))
h0 = hy(); print("horse at", h0, "floor", round(floor, 2))
ys = []
def sample(n):
    for i in range(n):
        h = hy(); ys.append(round(h[1] - floor, 2)); time.sleep(0.2)
if what == "wall2":
    # up to the wall, then a full jump (held half a second) going forward
    cmd("keys W 0.9"); sample(5)
    cmd("keys W+Space 0.55"); sample(3); cmd("keys W 2"); sample(12)
    print("after the jump:", cmd("horse").split("jumps=")[1][:80])
else:
    cmd("keys W 5"); sample(10)
    cmd(f"screenshot E:/claude/mods/lmc_horse246/shots/horse_{sys.argv[1]}_ride.png"); sample(15)
print("horse height over the floor:", ys)
print(f"highest {max(ys):.2f} m; a block is {S} m")
cmd("keys LeftCtrl 0.1"); time.sleep(1.0)
h = hy(); cmd(f"tp {h[0] + 6:.2f} {h[1] + 2:.2f} {h[2]:.2f}"); time.sleep(1.5); look_at(h[0], h[1] + 0.5, h[2]); time.sleep(0.4)
cmd(f"screenshot E:/claude/mods/lmc_horse246/shots/horse_{sys.argv[1] if len(sys.argv) > 1 else 'stairs'}.png")
