"""Hand-play (dev input): hanging signs, Minecraft's. A cobblestone pillar three high with one block sticking out at
the top; an oak hanging sign under that block (chains), one on the pillar's side (a bracket), one tried on bare ground
(Minecraft: can't). Each one's text typed with the keyboard as it's placed (two lines, Enter between, Esc to finish).
Photos of each. Run on a landed moon, daytime: py tools/hangsign.py"""
import sys, os, re, time, math
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd

S = 1.4  # (Plugin.S: a block, in metres)
SHOTS = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "shots")).replace("\\", "/")
DIRS = {(1, 0, 0): 5, (-1, 0, 0): 4, (0, 0, 1): 2, (0, 0, -1): 3}  # (Faces.Dir: the face hit -> facing)
def state(): return cmd("state")
def slots(): return re.search(r"slots=\[([^\]]*)\]", state()).group(1).split(",")
def pos(): return tuple(map(float, re.search(r"pos=(-?[\d.]+),(-?[\d.]+),(-?[\d.]+)", state()).groups()))
def idx(name): return next(i for i, x in enumerate(slots()) if x.startswith(name))
def add(a, b): return tuple(x + y for x, y in zip(a, b))
def probe():
    r = cmd("place?"); k = re.search(r"ok=(\w+) key=\[[^\]]*\] \((-?\d+), (-?\d+), (-?\d+)\) facing=(\d+)", r)
    if not k: return None, False, -1
    return (int(k.group(2)), int(k.group(3)), int(k.group(4))), k.group(1) == "True", int(k.group(5))
def aim(tx, ty, tz):
    x, y, z = pos(); eye = y + 2.15; dx, dz = tx - x, tz - z; d = math.hypot(dx, dz)
    return (math.degrees(math.atan2(dx, dz)) + 360) % 360, -math.degrees(math.atan2(ty - eye, d))
def place_where(ok_cells, slot, around, heights, radii=(2.6, 3.4, 4.2)):
    """Stand on rings around `around` (world x, z), aim at each height and nearby, and place the first time the
    target is one of ok_cells ({cell: facing or None}). Returns (cell, facing) or None."""
    cmd(f"slot {slot}"); time.sleep(0.2)
    for r in radii:
        for a in range(0, 360, 45):
            sx, sz = around[0] + r * math.sin(math.radians(a)), around[1] + r * math.cos(math.radians(a))
            cmd(f"tp {sx:.2f} {gy + 0.6:.2f} {sz:.2f}"); time.sleep(0.5)
            for h in heights:
                yw0, pt0 = aim(around[0], h, around[1])
                for dyw in (0, -10, 10, -20, 20):
                    for dpt in range(-24, 25, 4):
                        cmd(f"look {(yw0 + dyw) % 360:.1f} {max(-89, min(89, pt0 + dpt)):.1f}"); k, ok, f = probe()
                        if ok and k in ok_cells and (ok_cells[k] is None or ok_cells[k] == f or (ok_cells[k] == 0 and f >= 16)):
                            cmd("rmb"); time.sleep(1.2); return k, f
    return None
def write(*lines):
    for i, l in enumerate(lines):
        cmd("typetext " + l.replace(" ", "_")); time.sleep(0.4)
        if i < len(lines) - 1: cmd("keys Enter 0.05"); time.sleep(0.3)
    cmd("keys Escape 0.05"); time.sleep(0.8)
def ui(): return cmd("inputstate")

cmd("god 1"); cmd("mobspawns 0"); cmd("hunger 20 5")
if len(sys.argv) > 3: cmd("tp " + " ".join(sys.argv[1:4]))  # (a spot of your own: x y z)
else: cmd("flatspot 33")
time.sleep(1.5); cmd("clearinv"); time.sleep(0.5)
for k in ["cobblestone 8", "oak_hanging_sign 4"]: cmd("invgive " + k); time.sleep(0.5)
time.sleep(1)
x0, gy, z0 = pos()
cob = idx("Cobblestone")
cmd(f"slot {cob}")
for pt in (40, 48, 56, 64):
    cmd(f"look 0 {pt}"); A, ok, f = probe()
    if ok and f == 1: break
if ok: cmd("rmb"); time.sleep(1.2)
else:  # (one already there from a run before: build on it)
    A = tuple(map(int, re.search(r"cobblestone\((-?\d+), (-?\d+), (-?\d+)\)", cmd("near 9")).groups()))
objs = re.findall(r"pos=(-?[\d.]+),(-?[\d.]+),(-?[\d.]+)", cmd("objinfo LMC_cobblestone 4"))
ax, ay, az = min((tuple(map(float, o)) for o in objs), key=lambda q: (q[0] - x0) ** 2 + (q[2] - z0) ** 2)
print("pillar base", A, "at", (ax, ay, az))
for h in range(1, 3):
    c = add(A, (0, h, 0))
    print("cobble", c, place_where({c: 1}, cob, (ax, az), [ay + (h - 0.5) * S]))
# the overhang: any side of the top block
top = add(A, (0, 2, 0))
r = place_where({add(top, d): None for d in DIRS}, cob, (ax, az), [ay + 2 * S])  # (cobblestone has no facing)
print("overhang", r)
if r is None: sys.exit("no overhang")
B = r[0]; side = tuple(b - t for b, t in zip(B, top))
print([x for x in cmd("near 9").split(" ; ") if "cobble" in x])

sg = idx("Oak Hanging Sign")
# 1) on bare ground (Minecraft: a hanging sign needs a block over it or a wall)
cmd(f"slot {sg}"); cmd(f"tp {ax + 4:.2f} {gy + 0.6:.2f} {az + 4:.2f}"); time.sleep(0.6)
cmd("look 0 45"); k, ok, f = probe(); print("on bare ground: place? ok =", ok, "facing", f)
if ok: cmd("rmb"); time.sleep(1); print("  placed on the ground?!", [x for x in cmd("near 9").split(" ; ") if "hanging" in x])

# 2) under the overhang (its bottom face, facing 0): chains up to it
H = add(B, (0, -1, 0))
r = place_where({H: 0}, sg, (ax, az), [ay + 1.0 * S, ay + 1.5 * S], radii=(2.0, 2.8, 3.6))
print("under the overhang", H, r)
if r: write("Hello", "from below")
# 3) on the pillar's side (a face of the middle block away from the overhang): a bracket
mid = add(A, (0, 1, 0))
walls = {add(mid, d): f for d, f in DIRS.items() if d != side}
r = place_where(walls, idx("Oak Hanging Sign"), (ax, az), [ay + S], radii=(2.8, 3.6))
print("on the wall", r)
if r: write("Wall", "bracket")
print([x for x in cmd("near 9").split(" ; ") if "hanging" in x])
