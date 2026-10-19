"""Hand-play (dev input): three oak shelves in a row on flat ground, all facing you, a block of redstone behind each,
nine different stacks in the hotbar; then [E] on the middle one twice (Minecraft's swapHotbar with nine slots: the
left shelf as you face it takes slots 0-2, the middle 3-5, the right 6-8) and prints the hotbar and shelves each time.
Run on a landed moon: py tools/shelfrow.py"""
import sys, os, re, time
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd

S = 1.7
def state(): return cmd("state")
def slots(): return re.search(r"slots=\[([^\]]*)\]", state()).group(1).split(",")
def pos(): return tuple(map(float, re.search(r"pos=(-?[\d.]+),(-?[\d.]+),(-?[\d.]+)", state()).groups()))
def idx(name): return next(i for i, x in enumerate(slots()) if x.startswith(name))
def key_at():
    r = cmd("place?"); k = re.search(r"key=\[[^\]]*\] \((-?\d+), (-?\d+), (-?\d+)\)", r)
    return (tuple(map(int, k.groups())) if k else None), "ok=True" in r
def place_at(cell, slot, stand, yaws, pitches=range(10, 80, 3)):
    cmd(f"slot {slot}"); cmd(f"tp {stand[0]:.2f} {stand[1]:.2f} {stand[2]:.2f}"); time.sleep(0.6)
    for yw in yaws:
        for pt in pitches:
            cmd(f"look {yw} {pt}"); k, ok = key_at()
            if ok and k == cell: cmd("rmb"); time.sleep(1); return True
    return False
def shelves():
    out = []
    for x in cmd("objinfo ~LMC_ShelfItem 12").split("["):
        m = re.search(r"LMC_ShelfItem\d .*?pos=(-?[\d.]+),(-?[\d.]+),(-?[\d.]+).*? mf=LMC_(\S+)", x)
        if m: out.append((float(m.group(1)), m.group(4).replace("sprite_item_", "")))
    return [k for _, k in sorted(out)]

cmd("god 1"); cmd("mobspawns 0"); cmd("hunger 20 5")
cmd("flatspot 33"); time.sleep(1.5); cmd("clearinv"); time.sleep(0.5)
for k in ["oak_shelf 3", "redstone_block 3"]: cmd("invgive " + k); time.sleep(0.5)
time.sleep(1)
x0, y0, z0 = pos()
# the middle shelf: the cell 2 ahead (+z), looking +z (a shelf faces the one placing it)
cmd("look 0 40"); mid, ok = key_at()
cmd("look 0 30"); k2, ok2 = key_at()
mid = k2 if ok2 else mid
row = [(mid[0] - 1, mid[1], mid[2]), mid, (mid[0] + 1, mid[1], mid[2])]
for i, c in enumerate(row):
    print("shelf", c, place_at(c, idx("Oak Shelf"), (x0 + (c[0] - mid[0]) * S, y0 + 0.6, z0), [356, 0, 4]))
for c in row:
    print("redstone behind", place_at((c[0], c[1], c[2] + 1), idx("Block of Red"), (x0 + (c[0] - mid[0]) * S, y0 + 0.6, z0 + 6 * S), [176, 180, 184]))
print([x for x in cmd("near 9").split(" ; ") if "shelf" in x or "redstone" in x])
for k in ["oak_planks 6", "cobblestone 5", "sand 7", "gravel 8", "andesite 9", "stone 3", "mud_bricks 2", "granite 10"]:
    cmd("invgive " + k); time.sleep(0.5)
time.sleep(1)
cmd("tp {:.2f} {:.2f} {:.2f}".format(x0, y0 + 0.6, z0 + 0.3)); time.sleep(0.6)
for n in range(2):
    cmd("hunger 20 5"); cmd("slot 4"); cmd("look 0 30"); time.sleep(0.3)
    print("target", state().split("target=")[1][:40])
    print("before", slots(), "slot", re.search(r" slot=(\d+)", state()).group(1), "| shelves left->right", shelves())
    cmd("keys E 0.1"); time.sleep(8)
    print("after ", slots(), "slot", re.search(r" slot=(\d+)", state()).group(1), "| shelves left->right", shelves())
