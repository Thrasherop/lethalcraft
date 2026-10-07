"""In-game regression suite: scripted scenarios with pass/fail checks, run against the live game through the DevServer.

usage:  py tools/regress.py [moonIndex ...]      (default: 0 = Experimentation)
needs:  the game running with DevMode = true (tools/restart.sh), in orbit or landed.
Exit code = number of failed checks. Offline unit tests: dotnet test tests/LethalMinecraft.Tests
"""
import sys, os, time, math, re
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd
from moontour import pos, feet_cell, log_len, new_log, wait, state, S

results = []

def check(name, ok, detail=""):
    results.append((name, bool(ok), detail))
    print(f"  [{'PASS' if ok else 'FAIL'}] {name}" + (f"  ({detail})" if detail else ""), flush=True)
    return ok

def stats():
    m = re.search(r"natural=(\d+) molded=(\d+) bedrock=(\d+) cuts=(\d+) total=(\d+)", cmd("groundstats"))
    return dict(zip(["natural", "molded", "bedrock", "cuts", "total"], map(int, m.groups())))

def near_blocks(at=None):
    """blocks within 14 m of the player, or of the natural-grid cell `at` (tests can't rely on where the player ends up:
    a slope or an enemy can move them)"""
    out = {}
    where = f" {at[0] * S:.2f} {at[1] * S:.2f} {at[2] * S:.2f}" if at else ""
    for e in cmd(f"near 14{where}").split(" ; "):
        m = re.match(r"\s*([a-z_]+)\((-?\d+), (-?\d+), (-?\d+)\)y(\d+) f(\d+) s(\d+)", e)
        if m: out[(int(m.group(2)), int(m.group(3)), int(m.group(4)))] = (m.group(1), int(m.group(7)))
    return out

def item_y(name, x, z, r=1.2):
    best = None
    for e in cmd("objs").split(" ; "):
        m = re.match(r"\s*(.+?)@([-\d.]+),([-\d.]+),([-\d.]+)", e)
        if m and m.group(1) == name and math.hypot(float(m.group(2)) - x, float(m.group(4)) - z) < r:
            y = float(m.group(3))
            best = y if best is None else min(best, y)
    return best

def land(idx):
    if "inShipPhase=False" in state():
        cmd("tpship"); time.sleep(1); cmd("leave")
        wait(lambda: "inShipPhase=True" in state(), 120)
        time.sleep(2)
    cmd("deadline")
    cmd(f"route {idx}")
    for attempt in range(4):
        time.sleep(8)
        cmd("land")
        if wait(lambda: "landed=True" in cmd("leave_check"), 90): return True
    return False

def start_flat(idx=0, need=()):
    """go to the idx-th flattest open spot (each test gets its own, away from protected areas); if any column in `need`
    has no ground surface near the player's height, try the next spots"""
    for attempt in range(14):
        cmd("tpship"); time.sleep(1)
        r = cmd(f"flatspot {idx + attempt * 3}"); time.sleep(1.5)
        if not r.startswith("ok"): continue
        cmd("tprel 0 0 1"); time.sleep(1.2)
        fc = feet_cell()
        # every column needs open ground: a surface below the player's head with nothing (trees, buildings) over it
        ok = True
        for dx, dz in need:
            sy = surface(fc, dx, dz)
            if sy is None or any(cmd(f"obstructed {fc[0] + dx} {sy + k} {fc[2] + dz}") != "no" for k in range(1, 4)): ok = False; break
        if ok: return fc
    return None

# ---------------------------------------------------------------- outside
def surface(fc, dx, dz):
    """absolute y of the topmost non-air cell in column (fc.x+dx, fc.z+dz) near the player's height"""
    for y in range(4, -10, -1):
        if ") Air" not in cmd(f"cellabs {fc[0] + dx} {fc[1] + y} {fc[2] + dz}"):
            return fc[1] + y if y < 4 else None  # solid at head height: under a roof or inside a building
    return None

def dig(fc, dx, y, dz):
    return cmd(f"digabs {fc[0] + dx} {y} {fc[2] + dz}").split(" (")[0]

def place(key, fc, dx, y, dz, facing=1):
    return cmd(f"placeabs {key} {fc[0] + dx} {y} {fc[2] + dz} {facing}")

def t_outside_dig():
    print("- outside: dig, molds, item gravity, layers")
    fc = start_flat(0, [(0, 2), (1, 2)])
    if not check("found a flat outdoor spot", fc): return
    sy = surface(fc, 0, 2)
    s0 = stats()
    cx, cz = (fc[0] + 0.5) * S, (fc[2] + 2.5) * S
    cmd(f"giveat {cx:.2f} {(sy + 2) * S:.2f} {cz:.2f} cobblestone 3"); time.sleep(2.5)
    y0 = item_y("Cobblestone", cx, cz)
    r = dig(fc, 0, sy, 2)
    check("surface cell digs", r.startswith(("dug", "broke")), r)
    # the surface can sit near the bottom of its cell (the item then still rests on the cell below): dig that one too
    r2 = dig(fc, 0, sy - 1, 2)
    time.sleep(2.0)
    y1 = item_y("Cobblestone", cx, cz)
    check("item on the ground falls into the new hole", y0 is not None and y1 is not None and y1 < y0 - 0.5, f"{y0} -> {y1} ({r2})")
    s1 = stats()
    check("walls of the hole became natural blocks", s1["natural"] >= s0["natural"] + 4, f"{s0['natural']} -> {s1['natural']}")
    check("geometry was cut", s1["cuts"] > s0["cuts"])
    deep = [dig(fc, 0, sy - k, 2) for k in range(2, 9)]
    check("digging straight down keeps going (no shallow bedrock)", all(d.startswith(("dug", "broke")) for d in deep), ", ".join(deep))
    check("deep ground turns to stone", any("stone" in d or "ore" in d for d in deep[3:]), ", ".join(deep[3:]))
    ry = surface(fc, 1, 2)
    info = cmd(f"cellabs {fc[0] + 1} {ry} {fc[2] + 2}")
    check("hole rim is a molded/solid natural cell", ") Partial" in info or ") Solid" in info, info[:60])

def t_blocks_and_holes():
    print("- outside: placed blocks next to dug ground")
    fc = start_flat(1, [(x, z) for x in (-1, 0, 1, 2) for z in (2, 3)])
    if not check("found a flat outdoor spot", fc): return
    top = max(surface(fc, x, z) for x in (-1, 0, 1) for z in (2, 3))
    for x in (-1, 0, 1):
        for z in (2, 3):
            for y in (top, top - 1):
                dig(fc, x, y, z)
    place("cobblestone", fc, 0, top - 1, 2); place("cobblestone", fc, 0, top, 2)
    time.sleep(0.6)
    nb = near_blocks(fc)
    check("placed blocks exist in the pit", sum(1 for v in nb.values() if v[0] == "cobblestone" and v[1] == 0) == 2)
    dig(fc, 0, top - 2, 2); time.sleep(0.5)
    nb2 = near_blocks(fc)
    # (the ground itself can be natural cobblestone on stony moons: only look at the two placed cells)
    cells = [(fc[0], top - 1, fc[2] + 2), (fc[0], top, fc[2] + 2)]
    there = {k: nb2.get(k) for k in cells}
    check("placed column survives digging under it", all(v and v[0] == "cobblestone" for v in there.values()), str(there))
    check("no natural block generated inside a placed one", all(v and not (v[1] & 128) for v in there.values()), str(there))
    # a torch standing on raw ground pops off when that ground is dug
    ty = surface(fc, 2, 3)
    tr = place("torch", fc, 2, ty + 1, 3, 1); time.sleep(0.4)
    had = any(v[0] == "torch" for v in near_blocks(fc).values())
    dr = dig(fc, 2, ty, 3); time.sleep(0.6)
    gone = not any(v[0] == "torch" for v in near_blocks(fc).values())
    check("torch standing on dug-out ground pops off", had and gone, f"had={had} gone={gone} place={tr} dig={dr} cell={cmd(f'cellabs {fc[0] + 2} {ty} {fc[2] + 3}')[:70]}")

def t_sand():
    print("- outside: falling sand into a hole")
    fc = start_flat(2, [(0, z) for z in (3, 4, 5)])
    if not check("found a flat outdoor spot", fc): return
    # a column with nothing (trees, props) over the hole: sand rightly comes to rest on obstacles
    col = next((z for z in (3, 4, 5) if all(cmd(f"obstructed {fc[0]} {surface(fc, 0, z) + k} {fc[2] + z}") == "no" for k in range(1, 5))), 3)
    sy = surface(fc, 0, col)
    for y in (sy, sy - 1, sy - 2): dig(fc, 0, y, col)
    pr = place("sand", fc, 0, sy + 3, col); time.sleep(4)
    sand = [k for k, v in near_blocks(fc).items() if v[0] == "sand"]
    if not check("sand fell to the bottom of the hole", sand and sand[0][1] == sy - 2, f"{sand} surface={sy} place={pr}"):
        for y in range(sy + 3, sy - 7, -1):
            print("     ", y, cmd(f"cellabs {fc[0]} {y} {fc[2] + col}")[:120], "obstructed:", cmd(f"obstructed {fc[0]} {y} {fc[2] + col}"))

def t_piston():
    print("- outside: pistons in a trench")
    fc = start_flat(3, [(x, 2) for x in range(-1, 5)])
    if not check("found a flat outdoor spot", fc): return
    top = max(surface(fc, x, 2) for x in range(-1, 5))
    dr = [dig(fc, x, y, 2) for x in range(-1, 5) for y in (top, top - 1)]
    place("piston", fc, -1, top - 1, 2, 5); place("cobblestone", fc, 0, top - 1, 2); time.sleep(0.3)
    place("redstone_block", fc, -1, top, 2); time.sleep(1.5)
    nb = near_blocks(fc)
    head = [k for k, v in nb.items() if v[0] == "piston_head"]
    cob = [k for k, v in nb.items() if v[0] == "cobblestone" and v[1] != 128]
    if not check("piston extends and pushes the block", head and cob and cob[0][0] == head[0][0] + 1, f"head={head} cobble={cob}"):
        print("      blocks:", {k: v for k, v in nb.items() if v[1] != 128}, "digs:", dr)
        for x in range(-1, 5):
            print("     ", x, cmd(f"cellabs {fc[0] + x} {top - 1} {fc[2] + 2}")[:110], "obstructed:", cmd(f"obstructed {fc[0] + x} {top - 1} {fc[2] + 2}"))
    cmd(f"breakabs {fc[0] - 1} {top} {fc[2] + 2}"); time.sleep(1.2)
    check("piston retracts", not any(v[0] == "piston_head" for v in near_blocks(fc).values()))

def t_tnt():
    print("- outside: TNT crater")
    fc = start_flat(4, [(0, 4)])
    if not check("found a flat outdoor spot", fc): return
    sy = surface(fc, 0, 4)
    s0 = stats()
    pr = place("tnt", fc, 0, sy + 1, 4); cmd("tprel 0 0 -6"); ig = cmd("ignite 20"); time.sleep(4)
    s1 = stats()
    if not check("TNT carves a crater", s1["cuts"] > s0["cuts"] and s1["natural"] > s0["natural"] + 6, f"{s0} -> {s1} place={pr} ignite={ig}"):
        for y in (sy, sy - 1):
            print("     ", cmd(f"cellabs {fc[0]} {y} {fc[2] + 4}")[:150])
            print("     ", cmd(f"raysat {fc[0]} {y} {fc[2] + 4}")[:250])
        print("     ", new_log(0).splitlines()[-40:] and [l for l in new_log(0).splitlines()[-60:] if "Explosion" in l or "Error" in l][:5])
    under = cmd(f"cellabs {fc[0]} {sy} {fc[2] + 4}")
    check("the ground the TNT stood on is gone (no skin left over the crater)", "dug=yes" in under, under[-200:])

# ---------------------------------------------------------------- inside
def find_wall(fc):
    """nearest wall in a straight line, with nothing (props, furniture) standing in the room in between"""
    for k in range(1, 7):
        for dx, dz, yaw in ((1, 0, 90), (-1, 0, 270), (0, 1, 0), (0, -1, 180)):
            if ") Air" in cmd(f"cellabs {fc[0] + dx * k} {fc[1] + 1} {fc[2] + dz * k}"): continue
            clear = all(cmd(f"obstructed {fc[0] + dx * j} {fc[1] + y} {fc[2] + dz * j}") == "no" for j in range(1, k) for y in (0, 1))
            # the "wall" must be the room's wall, not furniture (server racks, shelves stay solid)
            clear = clear and all(cmd(f"props {fc[0] + dx * j} {fc[1] + y} {fc[2] + dz * j}") == "none" for j in range(1, k + 2) for y in (0, 1))
            if clear: return k, dx, dz, yaw
    return None

def settle():
    """wait until the player stops moving (nodes can be on stairs)"""
    last = pos()
    for _ in range(10):
        time.sleep(0.4)
        p = pos()
        if math.dist(p, last) < 0.02: return p
        last = p
    return last

def t_inside():
    print("- inside: tunnel through a wall, walk it, floor and ceiling")
    w = None
    why = {}
    for node in range(0, 60, 2):
        r = cmd(f"tpnode {node}")
        if not r.startswith("ok"): check("facility has nodes", False, r); return
        time.sleep(1.0); p = settle()
        fc = feet_cell()
        info = cmd("cellinfo 0 0 0")
        if "entrance" in cmd("cellinfo 0 2 0"): why["entrance"] = why.get("entrance", 0) + 1; continue  # protected on purpose
        # a 2-block tunnel has 2.8 m above the grid line: rooms whose floor sits higher than ~0.25 m above it leave less
        # than the 2.5 m a standing player needs, so dig those 3 blocks tall (as a player would)
        yoff = int(re.search(r"gridYOff=(\d+)", info).group(1)) / 1000 * S
        tall = (0, 1, 2) if p[1] - (fc[1] * S + yoff) > 0.25 else (0, 1)
        # a level floor (not stairs or ramps) under and around the player
        def level(info): return (") Solid" in info or (") Partial" in info and "facing=Up" in info)) and "dug=yes" not in info  # thin floors are partial; not one an earlier test dug
        if not all(level(cmd(f"cellabs {fc[0] + ox} {fc[1] - 1} {fc[2] + oz}")) for ox, oz in ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1))):
            why["floor"] = why.get("floor", 0) + 1; continue
        w = find_wall(fc)
        if w: break
        why["no wall"] = why.get("no wall", 0) + 1
    if not check("found a wall near a node", w):
        print("      rejected nodes:", why)
        return
    k, dx, dz, yaw = w
    res = []
    length = k + 4
    # a slanted wall can reach into the player's own cell: mine whatever of it is there, as a player would
    for y in tall:
        if ") Air" not in cmd(f"cellabs {fc[0]} {fc[1] + y} {fc[2]}"):
            r0 = cmd(f"digabs {fc[0]} {fc[1] + y} {fc[2]} force").split(" (")[0]
            if r0 != "bedrock": res.append(r0)  # (a protected spot next to a door: leave it)
    for j in range(1, k + 5):
        row = [cmd(f"digabs {fc[0] + dx * j} {fc[1] + y} {fc[2] + dz * j} force").split(" (")[0] for y in tall]
        if any("bedrock" in r for r in row):
            why = cmd(f"cellabs {fc[0] + dx * j} {fc[1]} {fc[2] + dz * j}")
            if any(f"bedrock={w}" in why for w in ("entrance", "door", "interactable", "invisible wall")):
                length = j - 1; res.append(f"(stopped at the protected {why.split('bedrock=')[1].split()[0]})"); break
        res += row
    if not check("wall tunnel digs (no bedrock behind the wall)", not any("bedrock" in r for r in res) and length > k, ", ".join(res)):
        for j in range(1, k + 5):
            info = cmd(f"cellabs {fc[0] + dx * j} {fc[1]} {fc[2] + dz * j}")
            if "bedrock=no" not in info: print("     bedrock reason:", info[:200])
    # furniture standing in the room behind the wall stays solid (by design): the walk ends there
    for j in range(1, length + 1):
        pr = [cmd(f"props {fc[0] + dx * j} {fc[1] + y} {fc[2] + dz * j}") for y in tall[:2]]
        if any(x != "none" for x in pr):
            print(f"      (a prop stands in the tunnel {j} cells in: {pr})"); length = j - 1; break
    cmd(f"tp {(fc[0] + .5) * S:.2f} {pos()[1] + 0.1:.2f} {(fc[2] + .5) * S:.2f}"); time.sleep(0.8)
    cmd(f"look {yaw} 0"); time.sleep(0.3)
    p0 = settle(); cmd("walk 1 0 2.0 3"); time.sleep(2.4); p1 = pos()
    cmd(f"photo {os.path.join(os.path.dirname(__file__), '..', 'shots', 'regress_walk.png')} {p1[0]:.2f} {p1[1] + 1.6:.2f} {p1[2]:.2f} {yaw} 10")
    moved = math.hypot(p1[0] - p0[0], p1[2] - p0[2])
    need = min(4.5, max(length - 1, 0.6) * S * 0.8)
    note = ""
    if moved < need:
        # a 2-block tunnel leaves 2.8 m from the grid line; floors above the grid line leave less than the 2.5 m a
        # standing player needs at the entrance. Like in Minecraft, crouching (or a taller entrance) gets you in.
        cmd(f"tp {(fc[0] + .5) * S:.2f} {p0[1] + 0.1:.2f} {(fc[2] + .5) * S:.2f}"); time.sleep(0.6); settle()
        print("     ", cmd("crouch 1")); time.sleep(0.6)
        p0c = pos(); cmd("walk 1 0 4.5 2"); time.sleep(4.9); p1 = pos()
        if math.hypot(p1[0] - p0c[0], p1[2] - p0c[2]) <= need:
            d = {90: (1, 0), 270: (-1, 0), 0: (0, 1), 180: (0, -1)}[yaw]
            print("     crouched blocker:", re.findall(r"<CAP[^>]*>", cmd(f"rayinfo {p1[0]:.2f} {p1[1] + 0.8:.2f} {p1[2]:.2f} {d[0]} 0 {d[1]}"))[:4])
            cmd(f"photo {os.path.join(os.path.dirname(__file__), '..', 'shots', 'regress_crouch3p.png')} {p1[0] - d[0] * 2.2:.2f} {p1[1] + 1.6:.2f} {p1[2] - d[1] * 2.2:.2f} {yaw} 25")
            print("     cells ahead:", [cmd(f"cellabs {fc[0] + dx * j} {fc[1] + y} {fc[2] + dz * j}")[:60] for j in (0, 1, 2) for y in (-1, 0, 1, 2)])
        cmd("crouch 0")
        moved = math.hypot(p1[0] - p0c[0], p1[2] - p0c[2])
        note = f" crouched (standing blocked at the entrance)"
    ok = check("player walks through the tunnel (no invisible walls)" + note, moved > need, f"{moved:.1f} m")
    if not ok:
        d = {90: (1, 0), 270: (-1, 0), 0: (0, 1), 180: (0, -1)}[yaw]
        cmd(f"photo {os.path.join(os.path.dirname(__file__), '..', 'shots', 'regress_walk3p.png')} {p1[0] - d[0] * 2.2:.2f} {p1[1] + 2.0:.2f} {p1[2] - d[1] * 2.2:.2f} {yaw} 25")
        print(f"     start={p0} end={p1} yaw={yaw}")
        for hgt in (0.3, 1.0, 1.7):
            hits = [h for h in cmd(f"rayinfo {p1[0]:.2f} {p1[1] + hgt:.2f} {p1[2]:.2f} {d[0]} 0 {d[1]}").split("] ") if "LineOfSight" not in h and "trig=True" not in h]
            print(f"     blocker at +{hgt}: {hits[:2]}")
        for j in range(1, k + 5):
            for y in (0, 1):
                info = cmd(f"cellabs {fc[0] + dx * j} {fc[1] + y} {fc[2] + dz * j}")
                if "bedrock=no" not in info: print("     bedrock cell:", info[:160])
    cmd(f"tp {(fc[0] + .5) * S:.2f} {p0[1] + 0.1:.2f} {(fc[2] + .5) * S:.2f}"); time.sleep(0.8); settle()
    def protected(x, y, z):
        info = cmd(f"cellabs {x} {y} {z}")
        return "bedrock=entrance" in info or "bedrock=door" in info
    f = cmd(f"digabs {fc[0] - dx} {fc[1] - 1} {fc[2] - dz}").split(" (")[0]
    floor_ok = f.startswith(("dug", "broke", "air")) or (f == "bedrock" and protected(fc[0] - dx, fc[1] - 1, fc[2] - dz))
    check("facility floor digs" + (" (next to a door/entrance: protected)" if f == "bedrock" else ""), floor_ok, f)
    up = []
    for y in range(2, 10):
        up.append(cmd(f"digabs {fc[0] - dx} {fc[1] + y} {fc[2] - dz}").split(" (")[0])
        if sum(1 for u in up if u.startswith(("dug", "broke"))) >= 2: break
    dug_up = any(u.startswith(("dug", "broke")) for u in up)
    if not check("facility ceiling digs" + ("" if dug_up else " (room taller than 8 blocks: nothing to dig)"),
          all("bedrock" not in u or protected(fc[0] - dx, fc[1] + 2 + i, fc[2] - dz) for i, u in enumerate(up)), ", ".join(up)):
        for y in range(2, 6): print("     ", cmd(f"cellabs {fc[0] - dx} {fc[1] + y} {fc[2] - dz}")[:160])

def t_inside_outside_switch():
    print("- facility inside/outside switching through tunnels")
    m = re.search(r"bounds=Center: \(([-\d.]+), ([-\d.]+), ([-\d.]+)\), Extents: \(([-\d.]+), ([-\d.]+), ([-\d.]+)\)", cmd("facility"))
    if not check("facility bounds known", m): return
    cx, cy, cz, ex, ey, ez = map(float, m.groups())
    if stats()["cuts"] == 0: cmd("digcell 0 -1 1")
    cmd("tpship"); time.sleep(1)
    node = cmd("tpnode 3"); time.sleep(1.2)
    p = pos()
    cmd("tpship"); time.sleep(1.0)
    cmd(f"tp {p[0]:.2f} {p[1] + 0.2:.2f} {p[2]:.2f}")
    seen = []
    for _ in range(10):
        time.sleep(0.2); seen.append(cmd("facility").split("inTile=")[1][:25])
    check("walking into a facility tile from a tunnel = inside", any("inside=True" in x for x in seen), str(seen[-1]))
    cmd(f"tp {p[0]:.2f} {cy + ey + 12:.2f} {p[2]:.2f}")
    seen = []
    for _ in range(8):  # the player falls back in quickly (rock has no collision): sample while above the facility
        time.sleep(0.15); seen.append("inside=False" in cmd("facility"))
    check("climbing out above the facility = outside", any(seen), str(seen))
    cmd("tpship"); time.sleep(1)

def t_bedrock():
    print("- bedrock protection")
    r = cmd("tpmain"); time.sleep(1.2)
    if r.startswith("ok"):
        info = cmd("cellinfo 0 -1 0")
        check("ground at the main entrance is protected", "bedrock=entrance" in info or "bedrock=door" in info, info[:120])
    cmd("tpship"); time.sleep(1)
    info = cmd("cellinfo 0 -1 0")
    check("ground under the ship can't be dug", "bedrock=no" not in info or ") Air" in info, info[:120])

# ---------------------------------------------------------------- crafting grid
def grab_all():
    for _ in range(12):
        if not cmd("grab").startswith("grabbing"): break
        time.sleep(0.7)

def hotbar(state):
    """[(key or None, count)] for the 9 hotbar slots as the crafting screen sees them"""
    out = []
    for e in re.search(r"hotbar=\[([^\]]*)\]", state).group(1).split(","):
        out.append((e.split(":")[0], int(e.split(":")[1])) if ":" in e else (None, 0))
    return out

def slot_of(state, key):
    for i, (k, n) in enumerate(hotbar(state)):
        if k == key: return i, n
    return None, 0

def total_of(state, key):
    return sum(n for k, n in hotbar(state) if k == key)

def free_slots(state):
    return [i for i, (k, n) in enumerate(hotbar(state)) if k is None]

def settle_ui(wait=1.6):
    time.sleep(wait)  # items coming back are spawned and picked up one by one
    return cmd("craftui state")

def empty_hotbar():
    cmd("craftui close"); cmd("clearinv"); time.sleep(0.6)

def t_crafting():
    print("- crafting grid: patterns, real item moves, shift-craft, leftovers, full inventory")
    cmd("craftui close")
    empty_hotbar()
    start_flat(5)  # away from the ship floor (old test items lie around there)
    for k, n in (("cobblestone", 5), ("stick", 4), ("oak_log", 3), ("dirt", 2)): cmd(f"give {k} {n}")
    time.sleep(1.5); grab_all(); time.sleep(0.8)
    st = cmd("craftui open table")
    ci, cn = slot_of(st, "cobblestone"); si, sn = slot_of(st, "stick")
    if not check("ingredients in the hotbar", ci is not None and si is not None and cn >= 3 and sn >= 2, st): return
    c_all, s_all = total_of(st, "cobblestone"), total_of(st, "stick")
    # stone pickaxe: pick the cobblestone stack up, right-click one into each top cell, put the rest back
    st = cmd(f"craftui click hot {ci}")
    check("picking a stack up takes it out of the hotbar", hotbar(st)[ci][0] is None and f"cursor=cobblestonex{cn}" in st, st)
    for cell in (0, 1, 2): cmd(f"craftui click grid {cell} right")
    cmd(f"craftui click hot {ci}")  # empty slot: the rest goes back into the inventory
    st = settle_ui()
    check("the rest of the stack goes back into the inventory", total_of(st, "cobblestone") == c_all - 3 and "cursor=-x0" in st, st)
    si, sn = slot_of(st, "stick")
    cmd(f"craftui click hot {si}")
    cmd("craftui click grid 4 right"); st = cmd("craftui click grid 7 right")
    check("right-click places one item", f"cursor=stickx{sn - 2}" in st, st)
    cmd(f"craftui click hot {si}")
    st = settle_ui()
    check("pickaxe pattern shows the result", "out=stone_pickaxex1" in st, st)
    cmd("craftui click grid 7"); st = cmd("craftui click grid 6")
    check("a wrong shape makes nothing", "out=-" in st, st)
    cmd("craftui click grid 6"); cmd("craftui click grid 7")
    st = cmd("craftui click out 0")
    check("taking the output uses up the grid", "grid=[.,.,.,.,.,.,.,.,.]" in st, st)
    st = settle_ui()
    check("the crafted pickaxe lands in the inventory", slot_of(st, "stone_pickaxe")[0] is not None, st)
    check("crafting used exactly the pattern's items", total_of(st, "cobblestone") == c_all - 3 and total_of(st, "stick") == s_all - 2, st)
    # shift-click: a stack of logs in one cell -> all of it into planks at once
    li, ln = slot_of(st, "oak_log")
    l_all, p_all = total_of(st, "oak_log"), total_of(st, "oak_planks")
    cmd(f"craftui click hot {li}"); cmd("craftui click grid 4")
    cmd("craftui click out 0 shift")
    st = settle_ui()
    check("shift-click crafts as many as possible", total_of(st, "oak_log") == l_all - ln and total_of(st, "oak_planks") == p_all + 4 * ln, st)
    # closing: what's left in the grid comes back
    di, dn = slot_of(st, "dirt"); d_all = total_of(st, "dirt")
    cmd(f"craftui click hot {di}"); cmd("craftui click grid 0")
    cmd("craftui close"); time.sleep(1.6)
    st = cmd("craftui open")
    check("closing puts grid items back in the inventory", total_of(st, "dirt") == d_all, st)
    check("pocket grid is 2x2", "size=2" in st, st)
    cmd("craftui close")
    # full inventory: leftovers drop at your feet
    cmd("give gravel 2"); time.sleep(1.2); grab_all(); time.sleep(0.6)
    st = cmd("craftui open table")
    gi, gn = slot_of(st, "gravel")
    if not check("gravel in the hotbar", gi is not None, st): cmd("craftui close"); return
    cmd(f"craftui click hot {gi}"); cmd("craftui click grid 0")
    # fill every free slot (including the one the gravel left) with different items, through the inventory itself
    fillers = ["wool_white", "wool_red", "wool_blue", "wool_yellow", "glass", "bricks", "bookshelf", "ice", "obsidian"]
    for k in fillers:
        if not free_slots(cmd("craftui state")): break
        cmd(f"invgive {k} 1"); time.sleep(1.2)
    time.sleep(1.0)
    st = cmd("craftui state")
    full = not free_slots(st)
    before = sum(1 for e in cmd("objs").split(" ; ") if e.strip().startswith("Gravel"))
    cmd("craftui close"); time.sleep(1.8)
    after = sum(1 for e in cmd("objs").split(" ; ") if e.strip().startswith("Gravel"))
    check("with a full inventory, leftovers drop at your feet", full and after > before, f"full={full} gravel on ground {before}->{after}")
    # tidy up: empty the hotbar so later tests (and moons) start clean
    empty_hotbar()

def t_pearl():
    print("- ender pearl: throw, teleport, one pearl used")
    fc = start_flat(6)
    if not check("found a flat outdoor spot", fc): return
    cmd("clearinv"); time.sleep(0.4)
    cmd("invgive ender_pearl 3"); time.sleep(1.6)
    st = cmd("state")
    if "Ender Pearl" not in st:
        cmd("give ender_pearl 3"); time.sleep(1.4); cmd("grab"); time.sleep(1.2); st = cmd("state")
    m = re.search(r"slots=\[([^\]]*)\]", st)
    slots = m.group(1).split(",") if m else []
    idx = next((i for i, e in enumerate(slots) if e.startswith("Ender Pearl")), None)
    if not check("holding ender pearls", idx is not None, st[:200]): return
    # throw where nothing (a tree, a rock) stands within 15 m
    p = pos(); yaw = 0
    for y in (0, 90, 180, 270):
        dx, dz = math.sin(math.radians(y)), math.cos(math.radians(y))
        hits = [h for h in cmd(f"rayinfo {p[0]:.2f} {p[1] + 2.2:.2f} {p[2]:.2f} {dx:.3f} 0.26 {dz:.3f}").split("] ") if h.startswith("[") and "trig=False" in h and re.search(r" L(0|8|11|25|26) ", h)]  # level geometry only
        first = min((float(h[1:].split()[0]) for h in hits), default=99)
        if first > 15: yaw = y; break
    cmd(f"slot {idx}"); cmd(f"look {yaw} -15"); time.sleep(0.4)
    p0 = pos()
    cmd("rmb"); time.sleep(4.0)
    p1 = pos()
    d = math.hypot(p1[0] - p0[0], p1[2] - p0[2])
    check("the thrower teleports to where the pearl lands", d > 5, f"{d:.1f} m")
    left = [e for e in cmd("find Ender").split(" ; ") if "held=True" in e]
    check("one pearl was used", any(e.startswith("Ender Pearlx2") for e in left), str(left))
    cmd("clearinv")

def t_hand_place():
    print("- placing with a real right-click: on the ground, and a torch in the cell you stand in")
    fc = start_flat(8, [(0, 0), (0, 2)])
    if not check("found a flat outdoor spot", fc): return
    cmd("clearinv"); time.sleep(0.4)
    cmd("invgive cobblestone 4"); cmd("invgive torch 4"); time.sleep(2.5)
    def count(name):
        m = re.search(name + r"x(\d+)", cmd("state")); return int(m.group(1)) if m else 0
    def select(name):
        sl = re.search(r"slots=\[([^\]]*)\]", cmd("state")).group(1).split(",")
        i = next((i for i, e in enumerate(sl) if e.startswith(name)), None)
        if i is not None: cmd(f"slot {i}"); time.sleep(0.3)
        return i is not None
    if not check("blocks in the hotbar", select("Cobblestone"), cmd("state")[:160]): return
    c0 = count("Cobblestone")
    cmd("look 0 55"); time.sleep(0.4); cmd("rmb"); time.sleep(0.8)
    check("right-click places a block on the ground in front", count("Cobblestone") == c0 - 1, cmd("place?")[:200])
    # stand in a dug cell and put a torch on its wall (the torch goes into your own cell)
    sy = surface(fc, 0, 0)
    for y in (sy, sy - 1, sy - 2): dig(fc, 0, y, 0)
    cmd(f"tp {(fc[0] + .5) * S:.2f} {(sy - 2) * S + 1.5:.2f} {(fc[2] + .5) * S:.2f}"); time.sleep(1.5)
    select("Torch"); t0 = count("Torch")
    cmd("look 90 10"); time.sleep(0.4); cmd("rmb"); time.sleep(0.8)
    check("a torch can be placed in the cell you stand in", count("Torch") == t0 - 1, cmd("place?")[:220])
    cmd("clearinv")

def t_integrity():
    print("- digging never deletes more of a level object than the dug cells (buildings, rooms, big props)")
    dug = 0
    # outside: the building at the main entrance (one mesh for the whole intro area on some moons) and open ground
    if cmd("tpmain").startswith("ok"):
        time.sleep(1.0); cmd("tprel 0 0 -4"); time.sleep(0.8)
        for dx, dz in ((0, 0), (1, 0), (0, 1)):
            r = cmd(f"digcell {dx} -1 {dz}"); dug += r.startswith(("dug", "broke"))
    for i in (0, 4):
        if start_flat(10 + i):
            fc = feet_cell(); sy = surface(fc, 0, 2)
            if sy is not None: dug += dig(fc, 0, sy, 2).startswith(("dug", "broke"))
    # inside: floors and the nearest wall at several nodes (rooms are often one mesh per tile)
    for node in range(1, 37, 6):  # odd nodes: t_inside walks from the even ones
        if not cmd(f"tpnode {node}").startswith("ok"): break
        time.sleep(0.8); fc = feet_cell()
        dug += cmd(f"digabs {fc[0]} {fc[1] - 1} {fc[2]} force").startswith(("dug", "broke"))
        w = None
        for k in range(1, 5):
            for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                if ") Air" not in cmd(f"cellabs {fc[0] + dx * k} {fc[1] + 1} {fc[2] + dz * k}"): w = (dx * k, dz * k); break
            if w: break
        if w: dug += cmd(f"digabs {fc[0] + w[0]} {fc[1] + 1} {fc[2] + w[1]} force").startswith(("dug", "broke"))
    cmd("tpship"); time.sleep(0.8)
    check("dug cells all over the level", dug >= 6, f"{dug} cells")
    r = cmd("carvecheck")
    check("every carved object keeps everything but the dug cells", r == "ok", r[:400])

def t_craft_lock():
    print("- the character doesn't act while the crafting screen is open")
    fc = start_flat(9)
    if not check("found a flat outdoor spot", fc): return
    cmd("clearinv"); cmd("invgive cobblestone 3"); time.sleep(2.0)
    if "crouching=True" in cmd("flags2"): cmd("crouch 0")
    p0 = pos(); held0 = "Cobblestone" in cmd("state")
    cmd("craftui open"); time.sleep(0.4)
    for k in ("LeftCtrl", "Space", "G", "Q"):
        cmd(f"presskey {k}"); time.sleep(0.5)
    st = cmd("flags2"); p1 = pos(); still = "Cobblestone" in cmd("state")
    check("no crouch, jump or drop while crafting", "crouching=False" in st and abs(p1[1] - p0[1]) < 0.2 and still == held0, f"{st} dy={p1[1] - p0[1]:.2f} held={still}")
    cmd("craftui close"); time.sleep(0.5)
    cmd("presskey LeftCtrl"); time.sleep(0.6)
    check("crouch works again after closing", "crouching=True" in cmd("flags2"), cmd("flags2"))
    cmd("crouch 0"); cmd("clearinv")

def t_chest():
    print("- chests: store, shift-move, take, keep, drop when broken")
    fc = start_flat(11, [(0, 2)])
    if not check("found a flat outdoor spot", fc): return
    cmd("clearinv"); cmd("chestui close")
    for k, n in (("cobblestone", 20), ("oak_planks", 12), ("stone_pickaxe", 1)): cmd(f"invgive {k} {n}")
    time.sleep(2.5)
    sy = surface(fc, 0, 2)
    r = place("chest", fc, 0, sy + 1, 2)
    if not check("chest placed", r.startswith("ok"), r): return
    c = (fc[0], sy + 1, fc[2] + 2)
    cmd(f"tp {(c[0] + .5) * S:.2f} {pos()[1]:.2f} {(c[2] - 1.0) * S:.2f}"); time.sleep(0.6)
    st = cmd(f"chestui open {c[0]} {c[1]} {c[2]}")
    if not check("chest screen opens", "open=True" in st, st): return
    def cells(st): return re.search(r"cells=\[([^\]]*)\]", st).group(1).split(",")
    def total(key, st): return total_of(st, key) + sum(int(x.rsplit("x", 1)[1]) for x in cells(st) if x.startswith(key + "x"))
    before = {k: total(k, st) for k in ("cobblestone", "oak_planks", "stone_pickaxe")}
    ci, cn = slot_of(st, "cobblestone")
    cmd(f"chestui click hot {ci}"); st = cmd("chestui click chest 0"); time.sleep(0.8); st = cmd("chestui state")
    check("a stack goes into a chest slot", cells(st)[0] == f"cobblestonex{cn}" and "cursor=-x0" in st, st)
    pi, pn = slot_of(st, "oak_planks")
    cmd(f"chestui click hot {pi} shift"); time.sleep(1.0); st = cmd("chestui state")
    check("shift-click moves a hotbar stack into the chest", any(x == f"oak_planksx{pn}" for x in cells(st)) and slot_of(st, "oak_planks")[0] is None, st)
    tk = slot_of(st, "stone_pickaxe")[0]
    cmd(f"chestui click hot {tk}"); cmd("chestui click chest 9"); time.sleep(0.8)
    cmd("chestui click chest 0 shift"); time.sleep(1.8); st = cmd("chestui state")
    check("shift-click takes a stack back into the inventory", cells(st)[0] == "." and total_of(st, "cobblestone") == cn, st)
    check("no items made or lost", all(total(k, st) == v for k, v in before.items()), f"{before} -> {[(k, total(k, st)) for k in before]}")
    cmd("chestui close"); time.sleep(0.5)
    st = cmd(f"chestui open {c[0]} {c[1]} {c[2]}")
    check("the chest keeps its contents", "stone_pickaxex1" in st and "oak_planks" in st, st)
    cmd("chestui close"); time.sleep(0.3)
    on_ground = lambda name: sum(1 for e in cmd("objs").split(" ; ") if e.strip().startswith(name))
    g0 = on_ground("Stone Pickaxe")
    cmd(f"breakabs {c[0]} {c[1]} {c[2]}"); time.sleep(1.5)
    check("a broken chest drops what was inside", on_ground("Stone Pickaxe") > g0, f"{g0} -> {on_ground('Stone Pickaxe')}")
    cmd("clearinv")

def t_company():
    print("- Gordion: no digging (AllowDiggingAtCompany = false)")
    fc = start_flat(0) or feet_cell()
    s0 = stats()
    res = [cmd(f"digabs {fc[0] + dx} {fc[1] + dy} {fc[2] + dz}").split(" (")[0] for dx, dy, dz in ((0, -1, 0), (1, -1, 0), (0, -2, 1), (2, -3, 0))]
    check("every dig at the Company is refused", all(r == "bedrock" for r in res), ", ".join(res))
    cmd(f"placeabs tnt {fc[0] + 2} {fc[1]} {fc[2] + 2} 1"); cmd("ignite 20"); time.sleep(4)
    s1 = stats()
    check("TNT leaves the Company's ground alone", s1["cuts"] == s0["cuts"], f"{s0} -> {s1}")

TESTS = [t_integrity, t_crafting, t_craft_lock, t_chest, t_pearl, t_hand_place, t_outside_dig, t_blocks_and_holes, t_sand, t_piston, t_tnt, t_inside, t_inside_outside_switch, t_bedrock]

if __name__ == "__main__":
    args = sys.argv[1:]
    only = None
    if "-t" in args:
        i = args.index("-t"); only = args[i + 1].split(","); del args[i:i + 2]
        TESTS = [t for t in TESTS if t.__name__ in only]
    moons = [int(x) for x in args] or [0]
    cmd("god 1"); cmd("photolight 0")
    if only and not args and "inShipPhase=False" in state():
        # quick rerun on the current moon
        for t in TESTS: t()
        failed = [r for r in results if not r[1]]
        print(f"{len(results) - len(failed)}/{len(results)} checks passed")
        sys.exit(len(failed))
    for idx in moons:
        since = log_len()
        name = cmd("levels").split(",")[idx].split("=")[1]
        print(f"== {name}")
        if not check(f"lands on {name}", land(idx)): continue
        time.sleep(6)
        for t in ([t_company] if "Gordion" in name else TESTS):
            cmd("clearenemies 80")  # tests aren't about enemy AI; one latched onto the (god-mode) player breaks both
            try: t()
            except Exception as e: check(t.__name__ + " ran", False, repr(e))
        lines = new_log(since).splitlines()
        # vanilla bugs that aren't ours (their stack trace is on the following lines)
        vanilla = ("SpikeRoofTrap", "SpawnStateException", "StormyWeather", "RuntimeNavMeshBuilder", "eliminated all possible nodes",
                   "BushWolfEnemy")  # (the fox breaks when it mauls the test player, who is in god mode)
        errs = []
        for i, l in enumerate(lines):
            if not (l.startswith("[Error") or l.startswith("[Fatal")) or "Lobby could not be created" in l: continue
            # the stack trace: the lines up to the next log entry
            stack = []
            for x in lines[i + 1:i + 20]:
                if x.startswith("["): break
                stack.append(x)
            ours = any("LethalMinecraft" in x for x in stack)
            if any(v in l or any(v in x for x in stack) for v in vanilla) and not ours: continue
            errs.append(l)
        check("no mod errors in the log", not errs, errs[0][:200] if errs else "")
    failed = [r for r in results if not r[1]]
    print(f"\n{len(results) - len(failed)}/{len(results)} checks passed")
    for n, _, d in failed: print(f"  FAILED: {n}  {d}")
    sys.exit(len(failed))
