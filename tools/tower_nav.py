"""#214 repro: build stepped plank towers by the ship with real right-clicks (the owner's build), stand on top, spawn a
monster below and watch whether it climbs to you.
usage: py tools/tower_nav.py build              (by the ship: steps 1-2-3 high, then a 3-high tower beside them)
       py tools/tower_nav.py watch <monster> [secs] [dist]   (stand on the top, spawn it, log its height every second)"""
import sys, os, time, re, math
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from regress import cmd, S

def feet_cell():
    return R.feet_cell()

def count(name):
    m = re.search(name + r"x(\d+)", cmd("state")); return int(m.group(1)) if m else 0

def select(name):
    sl = re.search(r"slots=\[([^\]]*)\]", cmd("state")).group(1).split(",")
    i = next((i for i, e in enumerate(sl) if e.startswith(name)), None)
    if i is not None: cmd(f"slot {i}"); time.sleep(0.3)
    return i is not None

def top_of(fc, dx, dz):
    """absolute y of the topmost non-air cell in a column (placed blocks count)"""
    return R.surface(fc, dx, dz)

def place_on(fc, dx, dz):
    """real right-click onto the top face of the column (dx, dz): one more block on it"""
    y = top_of(fc, dx, dz)
    if y is None: return None
    tx, tz = (fc[0] + dx + 0.5) * S, (fc[2] + dz + 0.5) * S
    ty = (y + 1) * S + R.YO - 0.02
    R.look_at(tx, ty, tz); time.sleep(0.35)
    n0 = count("Oak Planks")
    cmd("rmb"); time.sleep(0.7)
    return count("Oak Planks") < n0

def build():
    if "inShipPhase=True" in cmd("state"): print("land first"); return
    R.grid_yo()
    cmd("god 1"); cmd("dayfreeze 1"); cmd("clearenemies"); cmd("clearinv"); time.sleep(0.3)
    cmd("invgive oak_planks 64"); time.sleep(2.0)
    if not select("Oak Planks"): print("no planks:", cmd("state")[:200]); return
    # on open, fairly flat ground (by the ship's door the hillside is too steep to pillar up on)
    if len(sys.argv) > 2 and sys.argv[2] == "here": fc = feet_cell()
    else: fc = R.start_flat(int(sys.argv[2]) if len(sys.argv) > 2 else 0, [(dx, 0) for dx in range(0, 7)])
    if not fc: return
    print("standing at cell", fc, "surface heights dx 2..5:", [top_of(fc, dx, 0) for dx in range(2, 6)])
    # steps: column dx=2 one high, dx=3 two high, dx=4 three high, dx=5 three high (the tower's top, two wide);
    # each pillared up as a player does: stand there, look down, jump and right-click
    plan = {2: 1, 3: 2, 4: 3, 5: 3}
    for dx, h in plan.items():
        x, z = (fc[0] + dx + 0.5) * S, (fc[2] + 0.5) * S
        cmd(f"tp {x:.2f} {(fc[1] + 3) * S + R.YO:.2f} {z:.2f}"); time.sleep(1.5)
        R.wait(lambda: "grounded=True" in cmd("state"), 3, step=0.15)
        cmd("look 90 89"); time.sleep(0.3)
        for k in range(h):
            n0 = count("Oak Planks")
            cmd("keys Space 0.1"); cmd("mouse right 0.7"); time.sleep(1.1)
            print(f"  column dx={dx}: block {k + 1}/{h} {'placed' if count('Oak Planks') < n0 else 'NOT placed'}")
    print("columns now:", {dx: top_of(fc, dx, 0) for dx in plan})
    print("near:", cmd("near 6")[:600])
    open(os.path.join(os.path.dirname(__file__), "..", "shots", "tower_fc.txt"), "w").write(repr(fc))
    time.sleep(2.0)
    print("moonnav:", cmd("moonnav"))

def blocks():
    """placed blocks near: {(x, y, z): yoff}"""
    out = {}
    for m in re.finditer(r"oak_planks\((-?\d+), (-?\d+), (-?\d+)\)y(\d+)", cmd("near 12")):
        out[(int(m.group(1)), int(m.group(2)), int(m.group(3)))] = int(m.group(4))
    return out

def face_point(c, yo, d):
    """the middle of a block's face: d = (dx, dy, dz) one of the six directions"""
    x = (c[0] + 0.5 + d[0] * 0.49) * S
    y = (c[1] + 0.5 + d[1] * 0.49) * S + yo / 1000 * S
    z = (c[2] + 0.5 + d[2] * 0.49) * S
    return x, y, z

def place_against(c, d):
    b = blocks()
    if c not in b: print("   no block at", c); return None
    R.look_at(*face_point(c, b[c], d)); time.sleep(0.35)
    before = set(b)
    cmd("rmb"); time.sleep(0.8)
    new = set(blocks()) - before
    return new.pop() if new else None

def build_slope():
    """by the ship's door, on the hillside: a tower 3 high with steps 2 and 1 high down toward where I stand, each block
    placed with a real right-click onto the ground or a block's face, as a player builds"""
    if "inShipPhase=True" in cmd("state"): print("land first"); return
    R.grid_yo()
    cmd("god 1"); cmd("dayfreeze 1"); cmd("clearenemies"); cmd("clearinv"); time.sleep(0.3)
    cmd("invgive oak_planks 64"); time.sleep(2.0)
    select("Oak Planks")
    cmd("tp -5 6 9"); time.sleep(2.0)
    print("me:", cmd("state")[:50])
    before = set(blocks())
    for yaw in (90, 0, 180, 270):
        cmd(f"look {yaw} 50"); time.sleep(0.4)
        if "ok=True" in cmd("place?"): break
    cmd("rmb"); time.sleep(0.8)
    a = (set(blocks()) - before).pop()
    print("A on the ground:", a, "facing yaw", yaw)
    # the tower: two more on A
    a2 = place_against(a, (0, 1, 0)); a3 = place_against(a2, (0, 1, 0)) if a2 else None
    # steps back toward me (the direction I look from)
    back = {90: (-1, 0, 0), 270: (1, 0, 0), 0: (0, 0, -1), 180: (0, 0, 1)}[yaw]
    # (on a hillside the low faces may be in the ground: the first one that takes a block)
    b = place_against(a, back) or place_against(a2, back)
    b2 = place_against(b, (0, 1, 0)) if b else None
    c = (place_against(b, back) or (place_against(b2, back) if b2 else None)) if b else None
    print("tower", a, a2, a3, "step 2", b, b2, "step 1", c)
    print("near:", cmd("near 8")[:500])
    open(os.path.join(os.path.dirname(__file__), "..", "shots", "tower_top.txt"), "w").write(repr(a3 or a2 or a))
    time.sleep(2.5)
    print("moonnav:", cmd("moonnav"))

def watch(name, secs=60, dist=10):
    R.grid_yo()
    if "slope" in sys.argv:
        t = eval(open(os.path.join(os.path.dirname(__file__), "..", "shots", "tower_top.txt")).read())
        cmd(f"tp {(t[0] + .5) * S:.2f} {(t[1] + 6) * S:.2f} {(t[2] + .5) * S:.2f}"); time.sleep(2.5)
    else:
        fc = eval(open(os.path.join(os.path.dirname(__file__), "..", "shots", "tower_fc.txt")).read())
        top = top_of(fc, 5, 0)
        x, z = (fc[0] + 5 + 0.5) * S, (fc[2] + 0.5) * S
        cmd(f"tp {x:.2f} {(top + 8) * S:.2f} {z:.2f}"); time.sleep(2.5)  # (dropped onto the top: god mode)
    print("me:", cmd("state")[:60], "| moonnav:", cmd("moonnav"))
    cmd("clearenemies"); time.sleep(0.5)
    print("spawn:", cmd(f"enemy {name} {dist}"))
    if "provoke" in sys.argv: time.sleep(2.0); print("provoke:", cmd(f"provoke {name}"))
    me_y = R.pos()[1]
    best = 99.0
    t0 = time.time()
    while time.time() - t0 < secs:
        es = [e for e in cmd("enemies").split(" ; ") if e.lower().startswith(name.lower()[:5]) and "dead=False" in e]
        if es:
            e = min(es, key=lambda x: float(re.search(r" d=([\d.]+)", x).group(1)))
            m = re.search(r"@([-\d.]+),([-\d.]+),([-\d.]+) d=([\d.]+)", e)
            d = float(m.group(4)); best = min(best, d)
            print(f"  {time.time() - t0:4.0f}s y={float(m.group(2)):6.2f} (me {me_y:.2f}) d={d:5.1f} " + " ".join(re.findall(r"(?:state|path|onNav|rem|dest)=\S+", e)))
        time.sleep(1.0)
    print("closest:", best)
    print("path:", cmd(f"enemypath {name.split()[0]}")[:400])

if __name__ == "__main__":
    if sys.argv[1] == "build": build()
    elif sys.argv[1] == "slope": build_slope()
    else: watch(sys.argv[2], float(sys.argv[3]) if len(sys.argv) > 3 else 60, sys.argv[4] if len(sys.argv) > 4 else 10)
