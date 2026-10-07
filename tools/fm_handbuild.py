"""Play test: build the wiki's Two-way engine B by hand (real mouse/keys, in creative), the way a player would:
items from the creative menu, two dirt scaffolds to place the pistons against (a piston faces you, so you place it
against a block on its far side), the slime and observers against the pistons/slime, scaffolds broken, then start it
by placing a block in front of the north observer, and ride it.
Cells (relative to the start column, top view, x east, z north), machine level y = ground + 2:
   z3: obs(face N)          scaffold A: (-1, z1) two high      scaffold B: (2, z2) two high
   z2: slime   | sp(W)
   z1: sp(E)   | slime
   z0:         | obs(face S)
usage: py tools/fm_handbuild.py [flatIdx] [ride]"""
import sys, os, time, re, math
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from pilot import *
S = R.S

def where(key, cell):
    for e in cmd(f"where {key} 30").split(" ; "):
        m = re.match(r"\((-?\d+), (-?\d+), (-?\d+)\)@([-\d.]+),([-\d.]+),([-\d.]+)", e.strip())
        if m and tuple(map(int, m.groups()[:3])) == cell: return tuple(map(float, m.groups()[3:]))

def face(key, cell, d):
    c = where(key, cell)
    if c is None: return None
    return (c[0] + d[0] * S * 0.48, c[1] + d[1] * S * 0.48, c[2] + d[2] * S * 0.48)

def block_at(cell):
    for e in cmd(f"near 30").split(" ; "):
        m = re.match(r"\s*(\w+)\((-?\d+), (-?\d+), (-?\d+)\)y\d+ f(\d+)", e)
        if m and (int(m.group(2)), int(m.group(3)), int(m.group(4))) == cell: return m.group(1), int(m.group(5))
    return None

def stand(cx, cz):
    """walk (real keys) to the middle of a column"""
    go_to((cx + .5) * S, (cz + .5) * S, stop=0.35)

def put(item, at_point, expect_cell, expect_key=None, expect_facing=None):
    select(item); time.sleep(0.2)
    aim_at(*at_point); time.sleep(0.25)
    use(); time.sleep(0.6)
    b = block_at(expect_cell)
    ok = b is not None and (expect_key is None or b[0] == expect_key) and (expect_facing is None or b[1] == expect_facing)
    print(f"  {item:15s} -> {expect_cell}: {b} {'OK' if ok else 'WRONG'}")
    return ok

def creative_items(keys_tabs):
    cmd("keys I 0.08"); time.sleep(0.7)
    hot = 0
    for tab, key in keys_tabs:
        click_slot("creativeui", "tabs", tab)
        items = re.search(r"items=\[([^\]]*)\]", cmd("creativeui state")).group(1).split(",")
        click_slot("creativeui", "items", items.index(key))
        click_slot("creativeui", "hot", hot); hot += 1; time.sleep(0.6)
    time.sleep(1.0)
    print("hotbar:", cmd("creativeui state").split("hotbar=")[1])
    cmd("keys I 0.08"); time.sleep(0.5)

if __name__ == "__main__":
    idx = int(sys.argv[1]) if len(sys.argv) > 1 else 7
    ride = "ride" in sys.argv
    cmd("god 1"); cmd("clearenemies 80"); cmd("clearinv")
    if "gm=creative" not in cmd("state"): print(cmd("gamemode creative"))
    fc = R.start_flat(idx, [(dx, dz) for dx in range(-2, 4) for dz in range(-2, 6)])
    print("spot:", fc)
    X, Z = fc[0], fc[2]
    sy = max(R.surface(fc, dx, dz) for dx, dz in ((-1, 1), (0, 1), (1, 1), (0, 2), (1, 2), (2, 2), (0, 3), (1, 0)))
    y = sy + 2
    creative_items([(0, "dirt"), (1, "slime"), (1, "sticky_piston"), (1, "observer"), (0, "stone")])
    print(slots())
    ok = True
    # scaffold A: two dirt at (-1, z1), placed on the ground from the east
    stand(X + 1, Z + 1)
    gx, gz = (X - 1 + .5) * S, (Z + 1 + .5) * S
    select("Dirt"); aim_at(gx, (sy + 0.9) * S, gz); use(); time.sleep(0.6)
    a1 = [c for c in ((X - 1, sy + 1, Z + 1), (X - 1, sy, Z + 1)) if block_at(c)]
    print("  scaffold A bottom:", a1, [block_at(c) for c in a1])
    if not a1: sys.exit("could not start scaffold A")
    base = a1[0]
    ok &= put("Dirt", face("dirt", base, (0, 1, 0)), (base[0], base[1] + 1, base[2]), "dirt")
    top = (base[0], base[1] + 1, base[2]); y = top[1]
    # sticky piston facing east at (0, z1): against scaffold A's east face, looking west (stand east)
    stand(X + 3, Z + 1)
    ok &= put("Sticky Piston", face("dirt", top, (1, 0, 0)), (X, y, Z + 1), "sticky_piston", 5)
    # slime at (1, z1): against the piston's east face
    ok &= put("Slime Block", face("sticky_piston", (X, y, Z + 1), (1, 0, 0)), (X + 1, y, Z + 1), "slime")
    # scaffold B: two dirt at (2, z2), placed on the ground from the east
    stand(X + 4, Z + 2)
    select("Dirt"); aim_at((X + 2 + .5) * S, (sy + 0.9) * S, (Z + 2 + .5) * S); use(); time.sleep(0.6)
    b1 = next((c for c in ((X + 2, y - 1, Z + 2), (X + 2, y - 2, Z + 2)) if block_at(c)), None)
    print("  scaffold B bottom:", b1)
    if b1 and b1[1] < y - 1: ok &= put("Dirt", face("dirt", b1, (0, 1, 0)), (b1[0], b1[1] + 1, b1[2]), "dirt"); b1 = (b1[0], b1[1] + 1, b1[2])
    if b1: ok &= put("Dirt", face("dirt", b1, (0, 1, 0)), (b1[0], b1[1] + 1, b1[2]), "dirt")
    btop = (X + 2, y, Z + 2)
    # sticky piston facing west at (1, z2): against scaffold B's west face, looking east (stand west)
    stand(X - 2, Z + 2)
    ok &= put("Sticky Piston", face("dirt", btop, (-1, 0, 0)), (X + 1, y, Z + 2), "sticky_piston", 4)
    # slime at (0, z2): against that piston's west face
    ok &= put("Slime Block", face("sticky_piston", (X + 1, y, Z + 2), (-1, 0, 0)), (X, y, Z + 2), "slime")
    # observer facing north at (0, z3): against the slime's north face, looking south (stand north)
    stand(X, Z + 5)
    ok &= put("Observer", face("slime", (X, y, Z + 2), (0, 0, 1)), (X, y, Z + 3), "observer", 2)
    # observer facing south at (1, z0): against the other slime's south face, looking north (stand south)
    stand(X + 1, Z - 2)
    ok &= put("Observer", face("slime", (X + 1, y, Z + 1), (0, 0, -1)), (X + 1, y, Z), "observer", 3)
    shot("fm_hand_built")
    # break the scaffolds (one click each in creative), from beside them
    select("Stone")
    stand(X - 2, Z + 1)
    for c in ((X - 1, y, Z + 1), (X - 1, y - 1, Z + 1)):
        w = where("dirt", c)
        if w: aim_at(*w); cmd("mouse left 0.06"); time.sleep(0.5)
    stand(X + 4, Z + 3)
    for c in ((X + 2, y, Z + 2), (X + 2, y - 1, Z + 2)):
        w = where("dirt", c)
        if w: aim_at(*w); cmd("mouse left 0.06"); time.sleep(0.5)
    left = [c for c in ((X - 1, y, Z + 1), (X - 1, y - 1, Z + 1), (X + 2, y, Z + 2), (X + 2, y - 1, Z + 2)) if block_at(c)]
    print("scaffolds left:", left)
    parts = {c: block_at(c) for c in ((X, y, Z + 1), (X + 1, y, Z + 1), (X + 1, y, Z + 2), (X, y, Z + 2), (X, y, Z + 3), (X + 1, y, Z))}
    print("machine:", parts, "ALL PLACED RIGHT" if ok else "SOMETHING WRONG")
    if not ok: sys.exit(1)
    if ride:
        # fly up and land on top of the machine (on the east slime), then start it from there
        stand(X + 1, Z + 1)
        cmd("keys Space 0.07"); time.sleep(0.18); cmd("keys Space 0.07"); time.sleep(0.3)
        cmd("keys Space 0.8"); time.sleep(1.0)
        go_to((X + 1 + .5) * S, (Z + 1 + .5) * S, stop=0.2)
        cmd("keys LeftCtrl 2.0"); time.sleep(2.2)
        print("on the machine:", state()[5][:40], cmd("state").split("fly=")[1][:30])
    # start it: a block in front of the north observer's face
    stand(X, Z + 5) if not ride else None
    t = (X, y, Z + 4)
    if not ride:
        select("Stone"); ok2 = put("Stone", face("observer", (X, y, Z + 3), (0, 0, 1)), t, "stone")
    else:
        select("Stone"); aim_at((X + .5) * S, (y + .5) * S, (Z + 3 + .48) * S + 0.0); use(); time.sleep(0.4)
    p0 = pos(); t0 = time.time()
    track = []
    while time.time() - t0 < 5:
        sl = [k for k, v in R.near_blocks().items() if v[0] == "slime" and k[1] == y]
        track.append((round(time.time() - t0, 1), sorted(k[0] - X for k in sl), round(pos()[0] - p0[0], 1)))
        time.sleep(0.5)
    print("slime x / player dx over time:", track)
    shot("fm_hand_flying")
