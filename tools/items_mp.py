"""#244 part E, two instances: what the client makes with numbers the host gives (a dyed chestplate, a firework star, a
rocket) is the same thing on the host: the host draws the client's dyed chestplate in its colour, reads the client's
star and rocket, and sees the client's rocket go off. Run tools/mp.sh first (host 28771, client 28772).
usage: py tools/items_mp.py"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd

H, C = 28771, 28772
ok = []
HERE = os.path.dirname(os.path.abspath(__file__))
def check(name, cond, detail=""):
    ok.append(bool(cond)); print(f"  [{'PASS' if cond else 'FAIL'}] {name}  ({str(detail)[:300]})", flush=True)
def state(port): return cmd("state", port)
def pos(port): return tuple(map(float, re.search(r"pos=([-\d.]+),([-\d.]+),([-\d.]+)", state(port)).groups()))
def hotbar(port): return re.search(r"hotbar=\[([^\]]*)\]", cmd("craftui state", port)).group(1).split(",")
def hot(port, key): return next((i for i, x in enumerate(hotbar(port)) if x.split(":")[0].split("#")[0] == key), None)
def select(port, name):
    hb = re.search(r"slots=\[([^\]]*)\]", state(port)).group(1).split(",")
    i = next((i for i, s in enumerate(hb) if s.startswith(name)), None)
    if i is not None: cmd(f"slot {i}", port); time.sleep(0.6)
def craft(port, lay):
    """lay [(type, cells)] in the client's table grid with its clicks; the output (waiting for the host's number: a few tries)"""
    cmd("craftui open table", port); time.sleep(0.6)
    for key, cells in lay:
        i = hot(port, key)
        if i is None: return "no " + key
        cmd(f"craftui click hot {i}", port)
        for c in cells: cmd(f"craftui click grid {c} right", port)
        cmd(f"craftui click hot {i}", port)
    out = "-"
    for k in range(8):
        time.sleep(0.5); cmd("craftui click grid 8 right", port)  # (a click: the screen looks again, the host's number may be here now)
        out = re.search(r"out=(\S+)", cmd("craftui state", port)).group(1)
        if out != "-": break
    cmd("craftui click out 0 shift", port); time.sleep(0.6)
    cmd("craftui close", port); time.sleep(0.5)
    return out
def give(port, *kv):
    cmd("clearinv", port); time.sleep(0.4)
    for k, n in kv: cmd(f"invgive {k} {n}", port)
    time.sleep(2.0)

import regress as R
for p in (H, C): cmd("god 1", p); cmd("gamemode survival", p); cmd("hunger 20 5", p)
cmd("mobspawns 0", H); cmd("clearenemies 400 all", H); cmd("daytime 0.3", H)
fc = R.start_flat(100, [(0, 2), (0, 3)])
hx, hy, hz = pos(H); cmd(f"tp {hx:.2f} {hy + 0.3:.2f} {hz + 2.5:.2f}", C); time.sleep(1.5)

print("- the client dyes a leather chestplate red and wears it: the host draws it red")
give(C, ("leather_chestplate", 1), ("red_dye", 1))
out = craft(C, [("leather_chestplate", (4,)), ("red_dye", (1,))])
check("the client's grid makes a dyed chestplate (the colour's number from the host)", out.startswith("leather_chestplate#"), out)
select(C, "Leather Chestplate"); cmd("rmb", C); time.sleep(1.5)
hh = cmd("playerheld", H); print("   host:", hh[:300])
m = re.search(r"worn=-,(leather_chestplate#\d+)", hh.split("|")[2] if hh.count("|") >= 2 else hh)
check("the host has the client wearing it, its number known", "leather_chestplate#" in hh, hh[:200])
# (the host looks at the client: a picture)
cx, cy, cz = pos(C); hx, hy, hz = pos(H)
import math
yaw = math.degrees(math.atan2(cx - hx, cz - hz))
cmd(f"look {yaw:.0f} 10", H); time.sleep(1.5)
cmd(f"screenshot {os.path.join(HERE, '..', 'shots', 'iemp_host_sees_red.png')}", H); time.sleep(0.6)

print("- the client crafts a firework star and a rocket of it: the host reads them, and sees the rocket go off")
give(C, ("gunpowder", 3), ("lime_dye", 1), ("paper", 1))
out = craft(C, [("gunpowder", (0,)), ("lime_dye", (1,))])
check("the client's star", out.startswith("firework_star#"), out)
out2 = craft(C, [("paper", (4,)), ("gunpowder", (0, 1)), ("firework_star", (6,))])
check("the client's rocket of it", out2.startswith("firework_rocket#") and "x3" in out2, out2)
select(C, "Firework Rocket"); time.sleep(0.5)
hh = cmd("playerheld", H); print("   host:", hh[:300])
check("the host reads the client's rocket by its number", out2.split("x3")[0] in hh, hh[:200])
e0 = cmd("e244", H)
cmd("look 0 80", C); time.sleep(0.3); cmd("rmb", C); time.sleep(3.0)
e1 = cmd("e244", H)
f0 = int(re.search(r"fwExploded=(\d+)", e0).group(1)); f1 = int(re.search(r"fwExploded=(\d+)", e1).group(1))
check("the host sees the client's rocket go off, a small ball", f1 > f0 and "SmallBall" in e1, (f0, f1, re.search(r"fwLast=(\S+)", e1).group(1)))
c1 = cmd("e244", C)
check("so does the client", "SmallBall" in c1, re.search(r"fwLast=(\S+)", c1).group(1))

print(f"\n{sum(ok)}/{len(ok)} checks passed")
