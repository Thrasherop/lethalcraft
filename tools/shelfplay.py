"""Hand-play (dev input): a shelf on flat ground, three things put on it with [E], a redstone block beside it, then [E]
on the powered shelf swaps its three with the hotbar's last three (Minecraft's swapHotbar). Prints each step."""
import sys, os, re, math, time
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd

def state(): return cmd("state")
def slots(): return re.search(r"slots=\[([^\]]*)\]", state()).group(1).split(",")
def pos(): return tuple(map(float, re.search(r"pos=(-?[\d.]+),(-?[\d.]+),(-?[\d.]+)", state()).groups()))
def sel(name):
    i = next((i for i, x in enumerate(slots()) if x.lower().startswith(name.lower())), None)
    if i is not None: cmd(f"slot {i}")
    return i
def aim(tx, ty, tz):
    x, y, z = pos(); eye = y + 2.15; dx, dz = tx - x, tz - z; d = math.hypot(dx, dz)
    cmd(f"look {(math.degrees(math.atan2(dx, dz)) + 360) % 360:.1f} {-math.degrees(math.atan2(ty - eye, d)):.1f}")
def target(): return state().split("target=")[1][:50]
def shelf_items():
    return re.findall(r"\[(LMC_ShelfItem\d) .*? mf=(\S+)", cmd("objinfo ~LMC_ShelfItem 4"))

cmd("god 1"); cmd("mobspawns 0"); cmd("hunger 20 5")
cmd("flatspot 33"); time.sleep(1.5); cmd("tprel 0 0 1"); time.sleep(1)
cmd("clearinv"); time.sleep(0.5)
# the hotbar, slot by slot: 0 shelf, 1 wall, 2 pane, 3 sign, 4 redstone, 5 dirt, 6 stone, 7 mud bricks, 8 empty
for k in ["oak_shelf 1", "resin_brick_wall 3", "glass_pane 2", "oak_hanging_sign 2", "redstone_block 1", "dirt 1", "stone 3", "mud_bricks 2"]:
    cmd("invgive " + k); time.sleep(0.5)
time.sleep(1.5); print("hotbar:", slots())
sel("Oak Shelf")
for y in range(0, 360, 20):
    cmd(f"look {y} 45"); r = cmd("place?")
    if "ok=True" in r and "GroundChunk" in r: cmd("rmb"); time.sleep(0.8); break
c = tuple(map(float, re.search(r"pos=(-?[\d.]+),(-?[\d.]+),(-?[\d.]+)", cmd("objinfo LMC_oak_shelf 1")).groups()))
cell = re.search(r"oak_shelf\((-?\d+), (-?\d+), (-?\d+)\)y\d+ f(\d)", cmd("near 6"))
print("shelf at", c, "cell", cell.groups())
# its front: stand 2.3 m out from it, every side tried, the one where the ray hits the shelf's open face
for name in ("Resin Brick Wall", "Glass Pane", "Oak Hanging Sign"):
    sel(name); done = False
    for dx, dz in ((0, -1), (0, 1), (1, 0), (-1, 0)):
        cmd(f"tp {c[0] + dx * 2.3:.2f} {c[1] + 0.6:.2f} {c[2] + dz * 2.3:.2f}"); time.sleep(0.6)
        wx, wz = -dz, dx
        for off in (-0.45, 0.0, 0.45):
            n0 = len(shelf_items())
            aim(c[0] + dx * 0.7 + wx * off, c[1], c[2] + dz * 0.7 + wz * off); time.sleep(0.2)
            if "oak_shelf" not in target(): continue
            held = slots()
            cmd("keys E 0.1"); time.sleep(0.8)
            if len(shelf_items()) > n0: done = True; break
        if done: break
    print(name, "->", shelf_items())
print("hotbar now:", slots())

