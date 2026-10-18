"""Hand-play (dev input, real mining): from a flat spot, dig a shaft straight down with the held pickaxe to each depth
(blocks below the surface), then a 1x2 tunnel that many blocks long at it, and report what a player saw: the blocks
mined (picked up) and the ore showing in the tunnel's walls (natural cells only appear once dug next to).
py tools/stripmine.py [depths=5,20,35] [length=16] [flatspot=33]"""
import sys, os, re, time, math, collections, functools
print = functools.partial(print, flush=True)
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd

depths = [int(x) for x in (sys.argv[1] if len(sys.argv) > 1 else "5,20,35").split(",")]
length = int(sys.argv[2]) if len(sys.argv) > 2 else 16
spot = sys.argv[3] if len(sys.argv) > 3 else "33"
S = 1.4
ORES = ("coal", "iron", "copper", "gold", "redstone", "lapis", "diamond", "emerald", "gravel", "lava", "granite", "diorite", "andesite")

def state(): return cmd("state")
def pos(): return tuple(map(float, re.search(r"pos=(-?[\d.]+),(-?[\d.]+),(-?[\d.]+)", state()).groups()))
def cell(): return tuple(map(int, re.match(r"\((-?\d+), (-?\d+), (-?\d+)\)", cmd("cellinfo 0 0 0")).groups()))
def seen(r=7):
    c = collections.Counter()
    for x in cmd(f"near {r}").split(" ; "):
        k = re.sub(r"\(.*", "", x)
        for o in ORES:
            if o in k: c[k] += 1
    return c

cmd("god 1"); cmd("mobspawns 0"); cmd("hunger 20 5"); cmd("clearinv"); time.sleep(0.5)
cmd("invgive pickaxe 1"); time.sleep(1.0); cmd("slot 0")
cmd(f"flatspot {spot}"); time.sleep(1.5)
top = cell(); print("surface cell", top, "moon", re.search(r"level=\d+ \w+", state()).group(0))
t0 = time.time()
for depth in depths:
    # down the shaft to this depth
    cmd("look 0 89")
    stuck = 0
    while top[1] - cell()[1] < depth:
        # (stand in the middle of your cell: from its edge, straight down digs the neighbour and you stay on the rim)
        pc = cell(); x, y, z = pos()
        cmd(f"tp {(pc[0] + 0.5) * S:.2f} {y + 0.05:.2f} {(pc[2] + 0.5) * S:.2f}"); time.sleep(0.4); cmd("look 0 89")
        before = cell()
        cmd("mouse left 1.4"); time.sleep(1.6); cmd("hunger 20 5")
        stuck = stuck + 1 if cell() == before else 0
        if stuck > 8: print("   stuck at", cell(), cmd("mine?")[:80]); break
        if time.time() - t0 > 1500: break
    here = cell(); print(f"-- {top[1] - here[1]} below the surface at {here}")
    base_seen = seen()
    # a 1x2 tunnel north (+z): the block at head height, the one at the feet, then a step
    yaw = 0
    mined = collections.Counter()
    y0 = cell()[1]
    for i in range(length):
        here = cell()
        # (a level tunnel: the cells in front at head and feet height, aimed at until the target is that cell)
        for dy in (1, 0):
            want = (here[0], y0 + dy, here[2] + 1)
            for k in range(6):
                hit = None
                for pitch in range(-30, 70, 3):
                    cmd(f"look {yaw} {pitch}"); m = cmd("mine?")
                    mm = re.search(r"\((-?\d+), (-?\d+), (-?\d+)\)", m)
                    if mm and tuple(map(int, mm.groups())) == want: hit = m; break
                if not hit: break
                if hit.startswith("block"):
                    bk = re.search(r"\) (\w+)", cmd("state").split("target=")[1]); mined[bk.group(1) if bk else "?"] += 1
                else: mined["ground"] += 1
                cmd("mouse left 0.7"); time.sleep(0.75)
        x, y, z = pos(); cmd(f"tp {(here[0] + 0.5) * S:.2f} {y + 0.05:.2f} {(here[2] + 1.5) * S:.2f}"); time.sleep(0.35)
        cmd("hunger 20 5")
    print("   mined:", dict(mined))
    p = cell(); s = seen(9)
    print(f"   tunnel {length} long to {p}: ore in its walls: {dict(s)}")
    # back to the shaft for the next depth
    cmd(f"look 180 0"); cmd(f"walk 1 0 {length * 0.3:.1f}"); time.sleep(length * 0.3 + 0.5)
for k in range(6): cmd("grab"); time.sleep(0.5)
print("picked up:", re.search(r"slots=\[([^\]]*)\]", state()).group(1))
print(f"{time.time() - t0:.0f} s")
