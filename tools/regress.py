"""In-game regression suite: scripted scenarios with pass/fail checks, run against the live game through the DevServer.

usage:  py tools/regress.py [moonIndex ...]      (default: 0 = Experimentation)
needs:  the game running with DevMode = true (tools/restart.sh), in orbit or landed.
Exit code = number of failed checks. Offline unit tests: dotnet test tests/LethalMinecraft.Tests
"""
import sys, json, os, time, math, re
sys.path.insert(0, os.path.dirname(__file__))
import timing
# (before anything takes dev.cmd: where the run's time goes, printed at the end; --speed N runs the game N times faster)
timing.install(float(sys.argv[sys.argv.index("--speed") + 1]) if __name__ == "__main__" and "--speed" in sys.argv else None)
from dev import cmd, cmds
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

YO = 0.0  # the moon grid's height offset in metres (it differs per landing: a cell's bottom is at y * S + YO)

def grid_yo():
    global YO
    old = YO
    try: YO = int(cmd("gridyoff")) / 1000.0 * S
    except Exception: YO = 0.0
    if abs(YO - old) > 1e-6: print(f"  (this landing's grid offset: {YO:.2f} m)")
    return YO

def start_flat(idx=0, need=()):
    """go to the idx-th flattest open spot (each test gets its own, away from protected areas); if any column in `need`
    has no ground surface near the player's height, try the next spots"""
    grid_yo()
    why = []
    def settle(most):
        # (on the ground again after a teleport: no fixed wait for it)
        time.sleep(0.3); wait(lambda: "grounded=True" in cmd("state"), most, step=0.15)
    for attempt in range(28):
        if attempt == 14:
            # every spot tried has an earlier test's build on it (a small moon runs out of flat spots after ~40 tests):
            # take the earlier tests' placed blocks away (dug ground stays) and look again
            if not all(w == "built on" or w.startswith("(") for w in why): break
            print("  (every flat spot is built on: clearing the blocks earlier tests placed:", cmd("clearworld"), ")")
            time.sleep(1.0); why.append("|cleared|")
        attempt %= 14
        cmd("tpship"); time.sleep(0.3)
        r = cmd(f"flatspot {idx + attempt * 3}")
        if not r.startswith("ok"): why.append(r[:40]); continue
        settle(1.2)
        cmd("tprel 0 0 1"); cmd("unsink"); settle(1.0)  # (sinking from the last spot: a teleport doesn't end it)
        time.sleep(0.4)  # (sinking in quicksand shows after a moment)
        # not in quicksand or water (sinking makes the game drop what you hold)
        if "sinking=True" in cmd("flags") or "underwater=True" in cmd("flags"): why.append("sinking"); continue
        fc = feet_cell()
        # not on (or over) what earlier tests built: their floors stand in the air above the ground (natural ground
        # blocks have the 128 flag in their state; placed ones don't)
        built = set()
        for m in re.finditer(r"\w+\((-?\d+), (-?\d+), (-?\d+)\)y\d+ f\d+ s(\d+)", cmd("near 12")):
            if int(m.group(4)) < 128: built.add((int(m.group(1)), int(m.group(3))))
        cols = set(need) | {(0, 0)}
        if any((fc[0] + dx, fc[2] + dz) in built for dx, dz in cols): why.append("built on"); continue
        # every column needs open ground: a surface below the player's head with nothing (trees, buildings) over it
        ok = True
        for dx, dz in need:
            sy = surface(fc, dx, dz)
            if sy is None or any(r != "no" for r in cmds(f"obstructed {fc[0] + dx} {sy + k} {fc[2] + dz}" for k in range(1, 4))):
                ok = False; why.append(f"({dx},{dz}) {'no ground' if sy is None else 'obstructed'}"); break
        if ok: return fc
    print("  (no spot:", "; ".join(why), ")")
    return None

# ---------------------------------------------------------------- outside
def hold(key, n=1, timeout=6.0, name=None):
    """clear the hotbar, give one item and put it in hand; True once it's really held (the hotbar updates a moment
    after clearinv/invgive, so pick the slot by the item's name and check, rather than the first non-empty slot)"""
    name = name or " ".join(w.capitalize() for w in key.split("_"))
    # (items still on their way in, like something just crafted, land after a clear: wait until it stays empty)
    for _ in range(3):
        cmd("clearinv")
        wait(lambda: all(e == "-" for e in re.search(r"slots=\[([^\]]*)\]", cmd("state")).group(1).split(",")), 3, step=0.2)
        time.sleep(0.8)
        if all(e == "-" for e in re.search(r"slots=\[([^\]]*)\]", cmd("state")).group(1).split(",")): break
    cmd(f"invgive {key} {n}")
    t0 = time.time()
    while time.time() - t0 < timeout:
        st = cmd("state")
        sl = re.search(r"slots=\[([^\]]*)\]", st).group(1).split(",")
        i = next((j for j, e in enumerate(sl) if e.startswith(name)), None)
        if i is not None:
            cmd(f"slot {i}"); time.sleep(0.4)
            if f"held={name}" in cmd("state"): return True
        time.sleep(0.3)
    print(f"  (couldn't get {name} into hand: {cmd('state')[:160]})")
    print("   flags:", cmd("flags")[:300])
    print("   grab queue:", cmd("grabq"))
    print("   nearby:", [e for e in cmd("objs").split(" ; ") if name in e][:3])
    return False

def surface(fc, dx, dz):
    """absolute y of the topmost non-air cell in column (fc.x+dx, fc.z+dz) near the player's height"""
    ys = list(range(4, -10, -1))
    for y, r in zip(ys, cmds(f"cellabs {fc[0] + dx} {fc[1] + y} {fc[2] + dz}" for y in ys)):  # (one frame for the column)
        if ") Air" not in r:
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
    pit = [nb.get((fc[0], y, fc[2] + 2)) for y in (top - 1, top)]
    check("placed blocks exist in the pit", all(v and v[0] == "cobblestone" and v[1] == 0 for v in pit), str(pit))
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
    hk = (fc[0], top - 1, fc[2] + 2)  # piston at x-1 facing east: its head is at x
    check("piston retracts", hk is not None and near_blocks(fc).get(hk, (None,))[0] != "piston_head", f"head cell {hk}")

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
            # nor a door (its frame is protected on purpose, so a tunnel can't start there)
            clear = clear and not any(f"bedrock={r}" in cmd(f"cellabs {fc[0] + dx * j} {fc[1] + y} {fc[2] + dz * j}")
                                      for j in range(1, k + 2) for y in (0, 1) for r in ("door", "entrance", "interactable", "vent"))
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
            if any(f"bedrock={w}" in why for w in ("entrance", "door", "interactable", "invisible wall", "vent")):
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
    for yaw in (0, 90, 270, 180):  # (a direction with open ground in front: a tree trunk at a cell's edge can slip past the spot check)
        cmd(f"look {yaw} 55"); time.sleep(0.4)
        if "ok=True" in cmd("place?"): break
    cmd("rmb"); time.sleep(0.8)
    check("right-click places a block on the ground in front", count("Cobblestone") == c0 - 1, cmd("place?")[:200])
    # stand in a dug cell and put a torch on its wall (the torch goes into your own cell)
    sy = surface(fc, 0, 0)
    for y in (sy, sy - 1, sy - 2): dig(fc, 0, y, 0)
    cmd(f"tp {(fc[0] + .5) * S:.2f} {(sy - 2) * S + 1.5 + YO:.2f} {(fc[2] + .5) * S:.2f}"); time.sleep(1.5)
    select("Torch"); t0 = count("Torch")
    # a wall of the hole within reach (on a slope the upper part of the hole can be open air: look lower too)
    for yaw, pitch in [(y, p) for p in (10, 30, 50) for y in (90, 0, 180, 270)]:
        cmd(f"look {yaw} {pitch}"); time.sleep(0.3)
        if "ok=True" in cmd("place?"): break
    cmd("rmb"); time.sleep(0.8)
    if not check("a torch can be placed in the cell you stand in", count("Torch") == t0 - 1, cmd("place?")[:220]):
        print(f"      hole at {fc} surface {sy}; feet {feet_cell()} {cmd('state')[:45]}")
        for yaw in (90, 0, 180, 270):
            cmd(f"look {yaw} 10"); time.sleep(0.3); print(f"      yaw {yaw}:", cmd("place?")[30:150])
    cmd("clearinv")

def t_flying_machine():
    print("- flying machines (Minecraft wiki engines): moved observers fire, sticky pistons drop blocks on a short pulse")
    import flying_machine as FM
    # a sticky piston given an observer's 2-tick pulse leaves the block it pushed (Java "block dropping")
    fc = start_flat(12, [(0, 0), (1, 0), (2, 0), (3, 0)])
    if not check("found a flat outdoor spot", fc): return
    y = surface(fc, 1, 0) + 2
    place("observer", fc, 0, y, 0, 4); place("sticky_piston", fc, 1, y, 0, 5); place("cobblestone", fc, 2, y, 0)
    time.sleep(1.0)
    cmd(f"placeabs stone {fc[0] - 1} {y} {fc[2]}"); time.sleep(1.5)  # in front of the observer's face (it looks west)
    nb = near_blocks((fc[0] + 2, y, fc[2]))
    at = lambda dx: nb.get((fc[0] + dx, y, fc[2]), ("-", 0))[0]
    check("an observer pulse pushes the block, and the sticky piston leaves it there", at(2) == "-" and at(3) == "cobblestone" and at(1) == "sticky_piston",
          f"x+1..3: {at(1)}, {at(2)}, {at(3)}")
    for design, idx in (("Beast", 15), ("Bwest", 19), ("up", 21), ("Aeast", 24)):
        r = FM.run(design, idx, 3.5, verbose=False)
        if r is None: check(f"engine {design} flies", False, "no open spot"); continue
        moved, n0, n1 = r
        check(f"engine {design} flies ({'up' if design == 'up' else design[1:]}) and stays in one piece", moved >= 4 and n1 == n0, f"moved {moved} blocks, parts {n0} -> {n1}")

def t_fire():
    print("- fire: flint and steel lights the ground, it hurts, burns wood away, lights TNT, punching puts it out")
    fc = start_flat(17, [(0, 2), (0, 3), (1, 2), (-1, 2)])
    if not check("found a flat outdoor spot", fc): return
    def fires(): return [k for k, v in near_blocks().items() if v[0] == "fire"]
    def hp(): return int(re.search(r"hp=(\d+)", cmd("state")).group(1))
    cmd("clearinv"); cmd("invgive flint_and_steel 1"); time.sleep(2.0)
    sl = re.search(r"slots=\[([^\]]*)\]", cmd("state")).group(1).split(",")
    cmd(f"slot {next(i for i, e in enumerate(sl) if e.startswith('Flint'))}"); time.sleep(0.3)
    g = surface(fc, 0, 2)
    cmd(f"tp {(fc[0] + .5) * S:.2f} {(surface(fc, 0, 0) + 1) * S + 0.3 + YO:.2f} {(fc[2] + .5) * S:.2f}"); time.sleep(1.0)
    cmd("look 0 50"); time.sleep(0.4)
    f0 = len(fires()); cmd("rmb"); time.sleep(0.8)
    check("flint and steel lights the ground", len(fires()) > f0, cmd("fire?")[:120])
    # standing in it hurts (god mode off for this)
    lit = fires()
    if lit:
        k = lit[0]
        cmd("god 0"); h0 = hp()
        cmd(f"tp {(k[0] + .5) * S:.2f} {k[1] * S + 0.4 + YO:.2f} {(k[2] + .5) * S:.2f}"); time.sleep(1.6)
        h1 = hp(); cmd("god 1")
        check("standing in fire hurts", h1 < h0, f"hp {h0} -> {h1}")
        cmd(f"tp {(fc[0] + .5) * S:.2f} {(surface(fc, 0, 0) + 1) * S + 0.3 + YO:.2f} {(fc[2] - 2 + .5) * S:.2f}"); time.sleep(0.8)
    # wood burns away, without dropping anything
    y = surface(fc, -1, 2) + 1
    for dz in (2, 3):
        place("oak_planks", fc, -1, y, dz); place("oak_planks", fc, -1, y + 1, dz)
    cmd(f"placeabs fire {fc[0] - 2} {y} {fc[2] + 2}"); cmd(f"placeabs fire {fc[0] - 2} {y} {fc[2] + 3}")  # (a lone fire can die out first)
    def planks_near():
        n = 0
        for e in cmd("find Oak Planks").split(" ; "):
            m = re.match(r"\s*Oak Planks(?:x(\d+))?@([-\d.]+),([-\d.]+),([-\d.]+) held=False", e)
            if m and math.hypot(float(m.group(2)) - (fc[0] - 1 + .5) * S, float(m.group(4)) - (fc[2] + 2.5) * S) < 4 * S:
                n += int(m.group(1) or 1)
        return n
    d0 = planks_near()
    # fire spreads at random (like Minecraft): most of the wood is gone within a minute (usually well under)
    left = lambda: [k for k, v in near_blocks((fc[0] - 1, y, fc[2] + 2)).items() if v[0] == "oak_planks"]
    wait(lambda: len(left()) <= 1, 60, step=1)
    check("fire spreads into wood and burns it away (nothing drops)", len(left()) <= 1 and planks_near() == d0,
          f"planks left {left()} of 4, dropped planks nearby {d0} -> {planks_near()}")
    # punching puts it out
    cmd(f"tp {(fc[0] + .5) * S:.2f} {(surface(fc, 0, 0) + 1) * S + 0.3 + YO:.2f} {(fc[2] + .5) * S:.2f}"); time.sleep(1.0)
    # (facing away from the burning wood, so a fire spreading from it can't be in the way)
    for yaw in (180, 90, 270):  # a direction where the strike lands on open ground
        cmd(f"look {yaw} 50"); time.sleep(0.4)
        if "obstructed0.6=False" in cmd("fire?"): break
    before = set(fires()); cmd("rmb"); time.sleep(0.6)
    fp = feet_cell()
    # the one just struck: the new fire nearest the player (the wood may light another one meanwhile)
    new = sorted(set(fires()) - before, key=lambda k: (k[0] - fp[0]) ** 2 + (k[2] - fp[2]) ** 2)[:1]
    if check("lit another to punch out", new, cmd("fire?")[:100]):
        import pilot
        k = new[0]; pilot.aim_at((k[0] + .5) * S, (k[1] + .3) * S + YO, (k[2] + .5) * S); time.sleep(0.3)  # (the player can slide on a slope)
        cmd("lmb 0.15"); time.sleep(0.8)
        check("a punch puts fire out", new[0] not in fires(), f"{new[0]} {'still burning' if new[0] in fires() else 'out'} ({cmd('mine?')[:60]})")
    # TNT next to a fire lights
    ty = surface(fc, 1, 2) + 1
    place("tnt", fc, 1, ty, 3); cmd(f"placeabs fire {fc[0] + 1} {ty} {fc[2] + 2}"); time.sleep(1.5)
    t = near_blocks((fc[0] + 1, ty, fc[2] + 3)).get((fc[0] + 1, ty, fc[2] + 3))
    check("fire lights TNT next to it", t is not None and t[0] == "tnt" and t[1] & 1, f"{t}")
    time.sleep(4.5)  # (it goes off; last, since it reshapes the ground)
    cmd("clearinv")

def t_furnace_ui():
    print("- the furnace screen (#83): [E] opens it; shift-click puts ore in to smelt and coal in to burn; the result comes out")
    import pilot
    fc = start_flat(28, [(0, 0), (0, 2)])
    if not check("found a flat outdoor spot", fc): return
    cmd("gamemode survival"); cmd("clearinv"); time.sleep(0.4)
    try:
        sy = surface(fc, 0, 2)
        place("furnace", fc, 0, sy + 1, 2); time.sleep(0.6)
        stand_on(fc, 0, sy + 1, 0)
        cmd("invgive cobblestone 3"); cmd("invgive coal 1"); time.sleep(1.5)
        pilot.aim_at((fc[0] + .5) * S, (sy + 1.5) * S + YO, (fc[2] + 2.5) * S); time.sleep(0.4)
        cmd("keys E 0.1"); time.sleep(0.8)
        s = cmd("furnaceui state")
        if not check("[E] on a furnace opens its screen", "open=True" in s, s[:160]): return
        hot = re.search(r"hotbar=\[([^\]]*)\]", s).group(1).split(",")
        for want in ("cobblestone", "coal"):
            i = next((j for j, e in enumerate(hot) if e.startswith(want + ":")), None)
            if i is not None: cmd(f"furnaceui click hot {i} shift"); time.sleep(0.8)
        s = cmd("furnaceui state")
        check("shift-click puts cobblestone in to smelt and coal in the fuel slot", "in=cobblestonex3" in s and "fuel=coalx1" in s or "lit=True" in s, s[:200])
        wait(lambda: "out=stonex3" in cmd("furnaceui state"), 20, step=1.0)
        s = cmd("furnaceui state")
        check("it burns and the stone comes out (three)", "out=stonex3" in s, s[:200])
        cmd("furnaceui click furnace 2 shift"); time.sleep(1.5)
        st = cmd("state")
        check("shift-click on the result: into the hotbar", "Stonex3" in st and "out=." in cmd("furnaceui state"), st[60:200])
        cmd("furnaceui close")
    finally:
        cmd("furnaceui close"); cmd("clearinv")

def t_creative():
    print("- creative mode: /gamemode, the creative menu, blocks that don't run out, instant breaking without drops, flight")
    fc = start_flat(22, [(0, 2), (1, 2)])
    if not check("found a flat outdoor spot", fc): return
    cmd("clearinv"); time.sleep(0.4)
    def st(): return cmd("state")
    def count(name):
        m = re.search(name + r"x(\d+)", st()); return int(m.group(1)) if m else 0
    def y(): return float(re.search(r"pos=[-\d.]+,([-\d.]+)", st()).group(1))
    def dropped(): return sum(1 for e in cmd("objs").split(" ; ") if e.strip().startswith("Cobblestone@") and "held=False" in e)
    def placed(): return [k for k, v in near_blocks().items() if v[0] == "cobblestone"]
    r = cmd("gamemode creative")
    if not check("/gamemode creative switches the host to creative", "gm=creative" in st(), r): return
    try:
        # the creative menu: [I], a full stack onto the mouse, into the hotbar slot clicked
        cmd("keys I 0.08"); time.sleep(0.6)
        cmd("creativeui click tabs 0")  # (the menu remembers its last tab, like Minecraft)
        s = cmd("creativeui state")
        if not check("[I] opens the creative menu", "open=True" in s, s[:120]): return
        items = re.search(r"items=\[([^\]]*)\]", s).group(1).split(",")
        cmd(f"creativeui click items {items.index('cobblestone')}"); cmd("creativeui click hot 4"); time.sleep(2.0)
        s = cmd("creativeui state")
        check("an item from the menu lands in the hotbar slot you click", "hotbar=[-,-,-,-,cobblestone:64" in s, s.split(" cursor=")[1])
        # the mouse wheel scrolls a tab longer than the menu's 45 slots (#65)
        if check("the Building tab has more than the 45 slots show", len(items) > 45, len(items)):
            cmd("mouse wheel -1"); time.sleep(0.5)
            s = cmd("creativeui state")
            shown = re.search(r"shown=\[([^\]]*)\]", s).group(1).split(",")
            if check("the wheel scrolls it a row: the items past the first 45 show", "scroll=1/" in s and items[45] in shown, re.search(r"scroll=\S+", s).group(0)):
                key = items[45]
                cmd(f"creativeui click items {shown.index(key)}"); cmd("creativeui click hot 5"); time.sleep(2.0)
                s = cmd("creativeui state")
                check("an item from the scrolled rows lands in the hotbar", f"{key}:" in s.split("hotbar=[")[1].split("]")[0].split(",")[5], s.split(" cursor=")[1][:160])
            cmd("mouse wheel 5"); time.sleep(0.5)
            check("the wheel scrolls back to the top", "scroll=0/" in cmd("creativeui state"))
        # the game's own equipment, from the last tab: a click puts one in the hotbar
        cmd("creativeui click tabs 4")
        lc = re.search(r"items=\[([^\]]*)\]", cmd("creativeui state")).group(1).split(",")
        if check("the Lethal Company tab has the store's items and the shotgun", all("lc:" + n in lc for n in ("Shovel", "Stun grenade", "Shotgun", "Ammo")), ",".join(lc)[:200]):
            cmd(f"creativeui click items {lc.index('lc:Shovel')}")
            wait(lambda: "Shovel" in re.search(r"slots=\[([^\]]*)\]", st()).group(1).split(","), 4, step=0.3)
            check("a click on a Lethal Company item puts one in the hotbar", "Shovel" in re.search(r"slots=\[([^\]]*)\]", st()).group(1).split(","), st()[:200])
        cmd("creativeui click tabs 0")
        cmd("keys I 0.08"); time.sleep(0.5)
        cmd("slot 4"); time.sleep(0.3)
        for yaw in (0, 90, 180, 270):  # a direction with ground in reach (the spot can sit at the edge of a drop)
            cmd(f"look {yaw} 55"); time.sleep(0.4)
            if "ok=True" in cmd("place?"): break
        n0, p0, d0 = count("Cobblestone"), len(placed()), dropped()
        before_cells = placed()
        cmd("rmb"); time.sleep(0.8)
        check("placing in creative doesn't use up the stack", len(placed()) == p0 + 1 and count("Cobblestone") == n0, f"placed {p0} -> {len(placed())}, stack {n0} -> {count('Cobblestone')}")
        # aim at the block just placed (placing it can nudge the player on a slope)
        new = [k for k in placed() if k not in before_cells]
        if new:
            import pilot
            k = new[0]; pilot.aim_at((k[0] + .5) * S, (k[1] + .5) * S + YO, (k[2] + .5) * S); time.sleep(0.3)
        tgt = re.search(r"target=(\S+)", st()).group(1)
        cmd("lmb 0.05"); time.sleep(1.0)
        check("one click breaks a block in creative, and nothing drops", len(placed()) == p0 and dropped() == d0, f"blocks {len(placed())} (was {p0}), dropped {d0} -> {dropped()}, aimed at {tgt}, pos {st()[:30]}")
        # flight: double-tap jump, hold it to rise, let go to hover, crouch to come down; landing ends it
        y0 = y()
        cmd("keys Space 0.07"); time.sleep(0.18); cmd("keys Space 0.07"); time.sleep(0.3)
        cmd("keys Space 1.0"); time.sleep(1.3)
        y1 = y(); time.sleep(1.0); y2 = y()
        check("double-tap jump flies: holding jump rises, letting go hovers", "fly=True" in st() and y1 - y0 > 3 and abs(y2 - y1) < 0.1, f"rose {y1 - y0:.1f} m, drift {y2 - y1:+.2f}")
        cmd("keys LeftCtrl 4.0")
        wait(lambda: "fly=False" in st(), 5, step=0.2)
        cmd("keys LeftCtrl 0.01"); time.sleep(0.5)
        fl = re.search(r"fly=\w+ grounded=\w+", st()).group(0)
        check("crouch sinks, and touching down ends flight", "fly=False" in fl and abs(y() - y0) < 1.5, f"{y() - y0:+.1f} m from the start, {fl}")
    finally:
        cmd("creativeui close")
        r = cmd("gamemode survival")
    check("/gamemode survival switches back", "gm=survival" in st() and "fly=False" in st(), r)
    cmd("clearinv")

def t_pillar():
    print("- looking all the way down (real input): pillar up by jumping, dig back down through it, dig a 1x1 shaft and pillar out")
    fc = start_flat(18, [(0, 0)])
    if not check("found a flat outdoor spot", fc): return
    cmd("clearinv"); time.sleep(0.4)
    cmd("invgive cobblestone 8"); cmd("invgive pickaxe 1"); time.sleep(2.5)
    def count(name):
        m = re.search(name + r"x(\d+)", cmd("state")); return int(m.group(1)) if m else 0
    def select(name):
        sl = re.search(r"slots=\[([^\]]*)\]", cmd("state")).group(1).split(",")
        i = next((i for i, e in enumerate(sl) if e.startswith(name)), None)
        if i is not None: cmd(f"slot {i}"); time.sleep(0.3)
        return i is not None
    def y(): return float(re.search(r"pos=[-\d.]+,([-\d.]+)", cmd("state")).group(1))
    def jumps(n):
        select("Cobblestone"); cmd("look 0 80"); time.sleep(0.3)
        for i in range(n): cmd("keys Space 0.1"); cmd("mouse right 0.7"); time.sleep(1.1)
    def digs(n):
        select("Diamond Pickaxe"); cmd("look 0 80"); time.sleep(0.3)
        for i in range(n): cmd("mouse left 1.5"); time.sleep(2.0)
    if not check("blocks and a pickaxe in the hotbar", select("Cobblestone") and count("Cobblestone") == 8, cmd("state")[:160]): return
    # stand in the middle of the cell: off-centre, the 0.4 m-wide player keeps standing on a neighbour's edge over the
    # hole being dug under them (like Minecraft), which isn't what this test is about
    cmd(f"tp {(fc[0] + .5) * S:.2f} {y() + 0.3:.2f} {(fc[2] + .5) * S:.2f}"); time.sleep(1.2)
    y0 = y()
    jumps(3)
    check("jump + right-click while looking down puts a block under you (x3)", y() - y0 > 3 * S - 0.25 and count("Cobblestone") == 5,
          f"rose {y() - y0:.2f} m, {count('Cobblestone')} left; {cmd('place?')[:120]}")
    select("Diamond Pickaxe"); cmd("look 0 80"); time.sleep(0.4)
    m = cmd("mine?")
    check("on top of a 1-wide pillar, looking down targets the block you stand on", m.startswith("block"), m[:120])
    digs(3)
    check("digging down goes back through the pillar", y() - y0 < 0.3, f"{y() - y0:+.2f} m")
    y1 = y()
    digs(2)
    walls = [cmd(f"cellinfo {dx} 0 {dz}") for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1))]
    if not y() < y1 - 2 * S + 0.3:  # (the map seed, to replay a failing spot with dev 'seed <n>': #29)
        print("    map seed:", cmd("seed"), "| feet cell", feet_cell(), "| walls", [w[:90] for w in walls])
    check("digging straight down a 1x1 shaft keeps going down (not into its walls)", y() < y1 - 2 * S + 0.3,
          f"{y() - y1:+.2f} m; walls {[w.split(' gridY')[0].split(') ')[-1] for w in walls]}")
    y2 = y()
    jumps(2)
    check("pillar back up out of the shaft", y() - y2 > 2 * S - 0.25, f"rose {y() - y2:.2f} m")
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
    g = cmd("ghostcheck")
    check("no visible geometry left standing in dug cells (no see-through walls)", " 0 with uncut" in g, g[:400])
    gp = cmd("gapcheck")
    check("every side of a dug cell is closed (no see-through gaps into the void)", gp.startswith("0 gaps"), gp[:400])

def t_ore_blocks():
    print("- store ore blocks craft back into materials (iron, diamond, coal) and on into tools")
    cmd("craftui close")
    empty_hotbar()
    start_flat(5)
    for k, n in (("iron_block", 1), ("diamond_block", 1), ("coal_block", 1), ("stick", 2)): cmd(f"give {k} {n}")
    time.sleep(1.5); grab_all(); time.sleep(0.8)
    st = cmd("craftui open table")
    def uncraft(block, result):
        st = cmd("craftui state")
        i, n = slot_of(st, block)
        if not check(f"{block} in the hotbar", i is not None, st): return None
        cmd(f"craftui click hot {i}"); st = cmd("craftui click grid 4")
        ok = check(f"a {block} makes nine {result}", f"out={result}x9" in st, st)
        cmd("craftui click out 0 shift")
        st = settle_ui()
        check(f"the nine {result} land in the inventory", total_of(st, result) == 9 and total_of(st, block) == 0, st)
        return ok
    uncraft("iron_block", "iron_ingot")
    uncraft("diamond_block", "diamond")
    uncraft("coal_block", "coal")
    # a diamond pickaxe from the bought diamonds (they aren't scrap: nothing to sell)
    st = cmd("craftui state")
    di, dn = slot_of(st, "diamond")
    cmd(f"craftui click hot {di}")
    for cell in (0, 1, 2): cmd(f"craftui click grid {cell} right")
    cmd(f"craftui click hot {di}")
    si, sn = slot_of(settle_ui(), "stick")
    cmd(f"craftui click hot {si}"); cmd("craftui click grid 4 right"); cmd("craftui click grid 7 right"); cmd(f"craftui click hot {si}")
    st = settle_ui()
    check("three diamonds and two sticks make a diamond pickaxe", "out=pickaxex1" in st, st)
    cmd("craftui click out 0 shift")
    st = settle_ui()
    check("the diamond pickaxe lands in the inventory", slot_of(st, "pickaxe")[0] is not None and total_of(st, "diamond") == 6, st)
    # and back: nine coal make a block of coal
    ci, cn = slot_of(st, "coal")
    cmd(f"craftui click hot {ci}")
    for cell in range(9): cmd(f"craftui click grid {cell} right")
    st = settle_ui()
    check("nine coal make a block of coal", "out=coal_blockx1" in st, st)
    cmd("craftui close"); time.sleep(0.5)

def t_ore_drops():
    print("- mined ore: raw iron is worth nothing and stacks; diamonds sell for 45-65 (#43)")
    import pilot
    cmd("craftui close"); cmd("gamemode survival")  # (creative mines without drops)
    fc = start_flat(7, [(0, 2), (1, 2)])
    if not check("found a flat outdoor spot", fc): return
    sy = surface(fc, 0, 2)
    try:
        if not hold("stone_pickaxe"): return
        def mine(dx, ore):
            # (from where the test started: the pickup step moves the player)
            cmd(f"tp {(fc[0] + .5) * S:.2f} {(sy + 1.1) * S + YO:.2f} {(fc[2] + .5) * S:.2f}"); time.sleep(0.8)
            r = place(ore, fc, dx, sy + 1, 2); time.sleep(0.5)
            pilot.aim_at((fc[0] + dx + .5) * S, (sy + 1.5) * S + YO, (fc[2] + 2.5) * S); time.sleep(0.3)
            aimed = cmd("mine?")
            for _ in range(2):  # (a click right after switching tools can be lost)
                cmd("mouse left 3.0"); time.sleep(3.4)
                if cmd("mine?").split(" (")[0] != aimed.split(" (")[0] or "(" not in cmd("mine?"): break
            print(f"    mined {ore}: placed {r[:30]} aimed {aimed[:30]} -> {cmd('blockcount ' + ore)}")
        def collect(name):
            # (step right next to each drop of this kind near the test spot, then pick it up)
            cx, cz = (fc[0] + .5) * S, (fc[2] + 2.5) * S
            for e in cmd(f"find {name}").split(" ; "):
                m = re.search(r"@([-\d.]+),([-\d.]+),([-\d.]+) held=False", e)
                if not m: continue
                x, y, z = map(float, m.groups())
                if math.hypot(x - cx, z - cz) > 5: continue
                cmd(f"tp {x + 0.9:.2f} {y + 0.2:.2f} {z:.2f}"); time.sleep(0.8)
                cmd("grab"); time.sleep(1.2)
        mine(0, "iron_ore"); mine(1, "iron_ore")
        collect("raw iron")
        sv = cmd("slotvalues")
        iron = [e for e in sv.split(",") if e.startswith("Raw Iron")]
        n = int(re.match(r"Raw Ironx(\d+)", iron[0]).group(1)) if len(iron) == 1 else 0
        check("mined iron ores give Raw Iron worth $0, in one stack", len(iron) == 1 and n >= 2 and iron[0].endswith(":$0"),
              f"{sv} | left: {cmd('blockcount iron_ore')} | on the ground: {cmd('find raw')[:200]} | me: {cmd('state')[:40]}")
        if not hold("iron_pickaxe"): return  # (diamonds need an iron pickaxe, like Minecraft)
        mine(0, "diamond_ore")
        collect("diamond")
        sv = cmd("slotvalues")
        vals = [int(e.split(":$")[1]) for e in sv.split(",") if e.startswith("Diamond") and ":$" in e]
        check("mined diamond ore drops a Diamond worth 45-65", len(vals) == 1 and 45 <= vals[0] <= 65, sv)
    finally:
        cmd("clearinv")

def t_throw_one():
    print("- [Q] with a stack in hand throws one (it doesn't jump straight back); the last one, and a tool, drop (#52)")
    fc = start_flat(9, [(0, 2), (0, 3)])
    if not check("found a flat outdoor spot", fc): return
    cmd("gamemode survival"); cmd("look 0 0")
    def count():
        m = re.search(r"held=Cobblestone .*?slots=\[[^\]]*?Cobblestonex(\d+)", cmd("state"))
        return int(m.group(1)) if m else 0
    def ground(): return sum(int(n) for n in re.findall(r"Cobblestonex(\d+)@[^ ]+ held=False", cmd("find cobble")))
    try:
        if not hold("cobblestone", 3): return
        g0 = ground()
        cmd("keys Q 0.1"); time.sleep(2.5)
        check("Q throws one of the stack (3 -> 2, one on the ground, still there after 2.5 s)", count() == 2 and ground() == g0 + 1, f"{cmd('state')[60:120]} ground {g0}->{ground()}")
        cmd("keys Q 0.1"); time.sleep(1.0); cmd("keys Q 0.1"); time.sleep(1.5)
        st = cmd("state")
        check("the last one drops too (empty hand, all 3 on the ground)", "held=-" in st and ground() == g0 + 3, f"{st[60:120]} ground {ground()}")
        if not hold("stone_pickaxe"): return
        cmd("keys Q 0.1"); time.sleep(1.5)
        check("Q drops a tool", "held=-" in cmd("state") and "held=False" in cmd("find stone pickaxe"), cmd("state")[60:120])
    finally:
        cmd("clearinv")

def t_totem():
    print("- Totem of Undying in the hotbar: a deadly hit uses it up instead, full health, back in the ship (#53)")
    fc = start_flat(11)
    if not check("found a flat outdoor spot", fc): return
    cmd("gamemode survival"); cmd("clearinv"); time.sleep(1.0)
    try:
        cmd("invgive totem_of_undying 1"); cmd("invgive cobblestone 4")
        wait(lambda: "Totem of Undying" in cmd("state") and "Cobblestonex4" in cmd("state"), 5, step=0.3)
        cmd("god 0"); r = cmd("hurt 300"); time.sleep(1.5)
        st = cmd("state")
        check("a deadly hit doesn't kill: full health, alive", "dead=False" in st and "hp=100" in st, f"{r} {st[:120]}")
        check("the totem is used up (the rest of the hotbar stays)", "Totem" not in st and "Cobblestonex4" in st, st[60:160])
        check("you're back in the ship", "inElevator=True" in cmd("ship"), cmd("ship")[-120:])
    finally:
        cmd("god 1"); cmd("clearinv")

def t_redstone_ore():
    print("- redstone ore (#47): an iron pickaxe gets 4-5 redstone dust out of it")
    import pilot
    cmd("craftui close"); cmd("gamemode survival")
    fc = start_flat(19, [(0, 2)])
    if not check("found a flat outdoor spot", fc): return
    sy = surface(fc, 0, 2)
    try:
        if not hold("iron_pickaxe"): return
        place("redstone_ore", fc, 0, sy + 1, 2); time.sleep(0.5)
        pilot.aim_at((fc[0] + .5) * S, (sy + 1.5) * S + YO, (fc[2] + 2.5) * S); time.sleep(0.3)
        cmd("mouse left 3.0"); time.sleep(3.4)
        n = sum(int(x) for x in re.findall(r"Redstone Dustx(\d+)@[^ ]+ held=False", cmd("find redstone dust")))
        check("4-5 redstone dust on the ground", 4 <= n <= 5, cmd("find redstone dust")[:200])
    finally:
        cmd("clearinv")


def t_durability():
    print("- tools wear out (#48): a use per block mined; a worn-out pickaxe breaks and is gone")
    import pilot
    fc = start_flat(13, [(0, 2), (1, 2), (2, 2)])
    if not check("found a flat outdoor spot", fc): return
    sy = surface(fc, 0, 2)
    cmd("gamemode survival")
    try:
        if not hold("wooden_pickaxe"): return
        r = cmd("tooluse"); mx = int(re.search(r"/(\d+)", r).group(1))
        check("a wooden pickaxe has 59 uses", mx == 59, r)
        cmd(f"tooluse {mx - 2}")
        def mine(dx):
            # (held until the block is gone, and no longer: held on, it goes on into the ground under it)
            k = re.search(r"\(-?\d+, -?\d+, -?\d+\)", place("dirt", fc, dx, sy + 1, 2)).group(0); time.sleep(0.5)
            pilot.aim_at((fc[0] + dx + .5) * S, (sy + 1.5) * S + YO, (fc[2] + 2.5) * S); time.sleep(0.3)
            cmd("mouse left 4.0")
            wait(lambda: "dirt" + k not in cmd("near 6"), 4.0, step=0.1)
            cmd("mouse release"); time.sleep(0.5)
        mine(0)
        r = cmd("tooluse")
        check("mining a block uses it once", f"used {mx - 1}/" in r, r)
        mine(1); time.sleep(1.0)
        st = cmd("state")
        check("worn out, it breaks: gone from the hotbar", "Wooden Pickaxe" not in st, st[60:150])
    finally:
        cmd("clearinv")
def t_enchanting():
    print("- enchanting (#46): lapis from deep ore; a table with bookshelves around it; the screen spends lapis and levels")
    import pilot
    fc = start_flat(11, [(dx, dz) for dx in range(-2, 3) for dz in range(0, 6)])
    if not check("found a flat outdoor spot", fc): return
    cmd("gamemode survival"); cmd("enchantui close"); cmd("craftui close")
    try:
        sy = max(surface(fc, dx, dz) for dx in range(-2, 3) for dz in range(0, 6)) + 1
        for dx in range(-2, 3):
            for dz in range(0, 6): place("stone", fc, dx, sy, dz)
        time.sleep(0.5)
        cmd(f"tp {(fc[0] + .5) * S:.2f} {(sy + 1.5) * S + YO:.2f} {(fc[2] + .5) * S:.2f}"); time.sleep(0.3)
        wait(lambda: "grounded=True" in cmd("state"), 2.0, step=0.15)
        # lapis ore, mined with a stone pickaxe
        k = re.search(r"\(-?\d+, -?\d+, -?\d+\)", place("lapis_ore", fc, 0, sy + 1, 2)).group(0); time.sleep(0.5)
        if not hold("stone_pickaxe"): return
        pilot.aim_at((fc[0] + .5) * S, (sy + 1.5) * S + YO, (fc[2] + 2.5) * S); time.sleep(0.3)
        cmd("mouse left 6.0"); wait(lambda: "lapis_ore" + k not in cmd("near 6"), 6.0, step=0.1); cmd("mouse release"); time.sleep(1.5)
        got = sum(int(m.group(1)) for m in re.finditer(r"Lapis Lazulix(\d+)@[^;]*held=False", cmd("find lapis")))
        check("lapis ore mined with a stone pickaxe drops 2-4 lapis lazuli", 2 <= got <= 4, cmd("find lapis")[:120])
        # the table, bookshelves in a ring two blocks out (one gap to walk in)
        place("enchanting_table", fc, 0, sy + 1, 3)
        n = 0
        for dx in range(-2, 3):
            for dz in range(1, 6):
                if (abs(dx) == 2 or abs(dz - 3) == 2) and (dx, dz) != (0, 1): place("bookshelf", fc, dx, sy + 1, dz); n += 1
        time.sleep(0.8)
        cmd(f"tp {(fc[0] + .5) * S:.2f} {(sy + 1.5) * S + YO:.2f} {(fc[2] + 2.5) * S:.2f}"); time.sleep(0.5)
        cmd("clearinv"); time.sleep(0.5)
        for key, c in (("pickaxe", 1), ("lapis_lazuli", 10)): cmd(f"invgive {key} {c}")
        time.sleep(2.5)
        cmd("xplevel 30")
        i = next((n for n, (kk, _) in enumerate(hotbar(cmd("craftui state"))) if kk == "pickaxe"), None)
        if i is not None: cmd(f"slot {i}"); time.sleep(0.5)
        bt0 = float(re.search(r"breaktime_stone=([\d.]+)", cmd("itemdata")).group(1))
        pilot.aim_at((fc[0] + .5) * S, (sy + 1.9) * S + YO, (fc[2] + 3.5) * S); time.sleep(0.3)
        cmd("keys E 0.1"); time.sleep(0.8)
        st = cmd("enchantui state")
        if not check("[E] on the table opens the enchanting screen", "open=True" in st and "shelves=15" in st, st[:160]): return
        def hot(st, key):
            for i, (kk, nn) in enumerate(hotbar(st)):
                if kk and kk.startswith(key): return i
        cmd(f"enchantui click hot {hot(st, 'pickaxe')}"); cmd("enchantui click item 0")
        cmd(f"enchantui click hot {hot(cmd('enchantui state'), 'lapis_lazuli')}"); cmd("enchantui click lapis 0")
        time.sleep(0.3); st = cmd("enchantui state")
        lap0 = int(re.search(r"lapis=(\d+)", st).group(1))
        offers = re.findall(r"(\d+):(\d):", re.search(r"offers=\[([^\]]*)\]", st).group(1))  # (required level, cost)
        check("three offers, the top one needing level 30 (15 bookshelves)", len(offers) == 3 and offers[2] == ("30", "3"), st[:240])
        cmd("enchantui click offer 2"); time.sleep(0.3); st = cmd("enchantui state")
        check("taking it costs 3 lapis and 3 levels", f"lapis={lap0 - 3}" in st and "level=27" in st, f"lapis {lap0} -> {st[:200]}")
        check("the item in the slot is enchanted", re.search(r"item=pickaxe#\d+", st) is not None, st[:200])
        cmd("enchantui close"); time.sleep(2.0)
        i = hot(cmd("craftui state"), "pickaxe")
        if i is not None: cmd(f"slot {i}"); time.sleep(0.6)
        d = cmd("itemdata")
        check("it comes back enchanted, with the top offer's enchantment", "ench=" in d and re.search(r"ench=\w", d) is not None, d)
        bt = float(re.search(r"breaktime_stone=([\d.]+)", d).group(1))
        check("Efficiency makes it mine faster (if it got Efficiency)", "Efficiency" not in d or bt < bt0 * 0.85, f"{bt0} -> {d}")
        check("the lapis left comes back too", f"lapis_lazuli:{lap0 - 3}" in cmd("craftui state"), cmd("craftui state")[-120:])
    finally:
        cmd("enchantui close"); cmd("clearinv")
def stone_floor(fc, dxs, dzs, lift=0):
    """a stone floor on the highest ground under it (a ground cell can be mostly air: the terrain may sit well below its
    top), `lift` blocks higher still (clear of slopes, trees and rocks poking up into the cells above it); returns the
    floor's cell height"""
    tops = [t for t in (surface(fc, dx, dz) for dx in dxs for dz in dzs) if t is not None]
    if not tops: tops = [int((pos()[1] - YO) // S) - 1]  # (no ground found under some column: from where we stand)
    sy = max(tops) + 1 + lift
    for dx in dxs:
        for dz in dzs: place("stone", fc, dx, sy, dz)
    time.sleep(0.6)
    return sy

def block_at(fc, dx, y, dz):
    """(key, state) of the block in that cell, or None (looked up around the cell itself, wherever the player is)"""
    c = (fc[0] + dx, y, fc[2] + dz)
    for m in re.finditer(r"(\w+)\((-?\d+), (-?\d+), (-?\d+)\)y\d+ f\d+ s(\d+)", cmd(f"near 3 {(c[0] + .5) * S:.2f} {(c[1] + .5) * S + YO:.2f} {(c[2] + .5) * S:.2f}")):
        if tuple(map(int, m.group(2, 3, 4))) == (fc[0] + dx, y, fc[2] + dz): return (m.group(1), int(m.group(5)))

def stand_on(fc, dx, y, dz):
    cmd(f"tp {(fc[0] + dx + .5) * S:.2f} {(y + .5) * S + YO:.2f} {(fc[2] + dz + .5) * S:.2f}"); time.sleep(0.3)
    wait(lambda: "grounded=True" in cmd("state"), 2.0, step=0.15); time.sleep(0.3)

def walk_heights(secs, y0):
    cmd(f"keys W {secs}"); ys = []
    for _ in range(int((secs + 0.3) / 0.15)): time.sleep(0.15); ys.append(round(pos()[1] - y0, 2))
    return ys

def t_slabs():
    print("- slabs (#23): half blocks, top or bottom by where you click, two make a full block; they stay up on their own")
    import pilot
    fc = start_flat(9, [(dx, dz) for dx in range(-1, 3) for dz in range(0, 5)])
    if not check("found a flat outdoor spot", fc): return
    cmd("gamemode survival")
    try:
        f = stone_floor(fc, range(-1, 3), range(0, 5))
        for y in (1, 2): place("stone", fc, -1, f + y, 4)  # a wall to build against
        time.sleep(0.5)
        stand_on(fc, 0, f + 1, 0)
        if not hold("oak_slab", 8): return
        pilot.aim_at((fc[0] + .5) * S, (f + 1) * S + 0.01 + YO, (fc[2] + 2.5) * S); time.sleep(0.3)
        aim = cmd("place?")  # (what the click will do: shown if it doesn't)
        cmd("rmb"); time.sleep(0.8)
        check("on the floor: a bottom slab", block_at(fc, 0, f + 1, 2) == ("oak_slab", 0), f"{block_at(fc, 0, f + 1, 2)} | aimed: {aim[:160]} | me {pos()} | {cmd('state')[60:140]}")
        pilot.aim_at((fc[0] + .5) * S, (f + 1.5) * S + 0.01 + YO, (fc[2] + 2.5) * S); time.sleep(0.3)
        cmd("rmb"); time.sleep(0.8)
        check("another onto it: one full block of two", block_at(fc, 0, f + 1, 2) == ("oak_slab", 2), block_at(fc, 0, f + 1, 2))
        # the wall's side (from the cell in front of it), upper half: a top slab
        stand_on(fc, -1, f + 1, 1)
        pilot.aim_at((fc[0] - .5) * S, (f + 2.8) * S + YO, (fc[2] + 4) * S - 0.01); time.sleep(0.3)
        cmd("rmb"); time.sleep(0.8)
        check("on the upper half of a wall: a top slab", block_at(fc, -1, f + 2, 3) == ("oak_slab", 1), block_at(fc, -1, f + 2, 3))
        # walk up onto a bottom slab, no jumping
        place("oak_slab", fc, 1, f + 1, 2); time.sleep(0.5)
        stand_on(fc, 1, f + 1, 0)
        cmd("look 0 0"); ys = walk_heights(0.6, pos()[1])
        check("you walk up onto a bottom slab", max(ys) > 0.4 * S, ys)
        # standing on its own: the stone under a slab (and stairs) goes, they stay
        place("stone", fc, 2, f + 1, 2); place("oak_slab", fc, 2, f + 2, 2); time.sleep(0.5)
        cmd(f"breakabs {fc[0] + 2} {f + 1} {fc[2] + 2}"); time.sleep(1.0)
        check("the block under a slab breaks: the slab stays", block_at(fc, 2, f + 2, 2) == ("oak_slab", 0), block_at(fc, 2, f + 2, 2))
        on_ground = lambda: sum(int(m.group(1)) for m in re.finditer(r"Oak Slabx(\d+)@[^;]*held=False", cmd("find oak slab")))
        g0 = on_ground()
        cmd(f"breakabs {fc[0]} {f + 1} {fc[2] + 2}"); time.sleep(1.5)
        check("a full block of two drops two slabs", on_ground() - g0 == 2, f"{g0} -> {on_ground()}")
    finally:
        cmd("clearinv")

def t_trapdoors():
    print("- trapdoors (#23): over a hole, you walk across it shut; [E] opens it and you drop in")
    import pilot
    fc = start_flat(13, [(dx, dz) for dx in range(0, 3) for dz in range(0, 6)])
    if not check("found a flat outdoor spot", fc): return
    cmd("gamemode survival")
    try:
        f = stone_floor(fc, range(0, 3), range(0, 6))
        place("stone", fc, 1, f - 1, 3)
        cmd(f"breakabs {fc[0] + 1} {f} {fc[2] + 3}"); time.sleep(0.6)  # a hole, one block deep
        stand_on(fc, 1, f + 1, 1)
        if not hold("oak_trapdoor", 2): return
        # the hole's far side, upper half: a trapdoor at the top of the hole
        pilot.aim_at((fc[0] + 1.5) * S, (f + 0.8) * S + YO, (fc[2] + 4) * S - 0.01); time.sleep(0.3)
        aim = cmd("place?")
        cmd("rmb"); time.sleep(0.8)
        t = block_at(fc, 1, f, 3)
        if not check("placed over the hole, at its top", t is not None and t[0] == "oak_trapdoor" and t[1] == 2, f"{t} | aimed: {aim[:160]} | me {pos()}"): return
        stand_on(fc, 1, f + 1, 0)
        cmd("look 0 0"); ys = walk_heights(1.2, pos()[1])
        check("shut, you walk across it", min(ys) > -0.3 and pos()[2] > (fc[2] + 4) * S, f"{ys} z={pos()[2]:.1f}")
        stand_on(fc, 1, f + 1, 1)
        pilot.aim_at((fc[0] + 1.5) * S, (f + 0.9) * S + YO, (fc[2] + 3.5) * S); time.sleep(0.3)
        cmd("keys E 0.1"); time.sleep(0.8)
        t = block_at(fc, 1, f, 3)
        check("[E] opens it", t is not None and t[1] & 1 == 1, t)
        stand_on(fc, 0, f + 1, 3)
        cmd("look 90 0"); ys = walk_heights(0.5, pos()[1])
        check("open, you drop into the hole", min(ys) < -1.0, ys)
    finally:
        cmd("clearinv")

def t_repeaters():
    print("- repeaters (#23): placed pointing the way you look; pass power on one way, after their delay; [E] sets it")
    import pilot
    fc = start_flat(21, [(0, 0), (1, 2), (5, 1)])  # (it builds its own floor)
    if not check("found a flat outdoor spot", fc): return
    cmd("gamemode survival")
    try:
        f = stone_floor(fc, range(-1, 8), (0, 1, 2, 3), lift=2) + 1  # (the level on top of the floor)
        stand_on(fc, 1, f, 0)
        if not hold("repeater", 4, name="Redstone Repeater"): return
        # placed by a right-click on the floor, looking east (+x): it points east
        cmd("look 90 30"); time.sleep(0.3)
        pilot.aim_at((fc[0] + 1.5) * S, f * S + 0.01 + YO, (fc[2] + 2.5) * S); time.sleep(0.3)
        cmd("look 90 50"); pilot.aim_at((fc[0] + 1.5) * S, f * S + 0.01 + YO, (fc[2] + 2.5) * S); time.sleep(0.3)
        cmd("rmb"); time.sleep(0.8)
        r = [m for m in re.finditer(r"repeater\((-?\d+), (-?\d+), (-?\d+)\)y\d+ f(\d+) s(\d+)", cmd("near 10"))]
        if not check("a right-click places a repeater on the floor", len(r) == 1, cmd("near 4")[:200]): return
        rx = int(r[0].group(1)) - fc[0]; rz = int(r[0].group(3)) - fc[2]; rf = int(r[0].group(4))
        check("it points the way you looked (north, at the floor ahead)", rf == 2, r[0].group(0))
        dxz = {2: (0, 1), 3: (0, -1), 4: (-1, 0), 5: (1, 0)}.get(rf, (0, 1))
        front, back = (rx + dxz[0], rz + dxz[1]), (rx - dxz[0], rz - dxz[1])
        # a lamp in front of it, a redstone block behind: the lamp lights (a tick later)
        place("redstone_lamp", fc, front[0], f, front[1]); time.sleep(0.5)
        place("redstone_block", fc, back[0], f, back[1]); time.sleep(0.6)
        lamp = block_at(fc, front[0], f, front[1])
        check("power behind it comes out of its front (the lamp lights)", lamp and lamp[1] & 1 == 1, (lamp, block_at(fc, rx, f, rz)))
        # one way: a lamp behind a repeater, power in front of it: the lamp stays dark
        place("redstone_lamp", fc, 4, f, 1); place("repeater", fc, 5, f, 1, 5); place("redstone_block", fc, 6, f, 1)
        time.sleep(0.8)
        behind = block_at(fc, 4, f, 1)
        check("power in front of it doesn't go back through it", behind and behind[1] & 1 == 0, behind)
        # [E] sets the delay: 1 -> 2 ticks (state bits 1-2)
        before = block_at(fc, rx, f, rz)
        stand_on(fc, rx - 1, f, rz)  # (beside it: in line, the block behind it is in the way)
        pilot.aim_at((fc[0] + rx + .5) * S, f * S + 0.1 + YO, (fc[2] + rz + .5) * S); time.sleep(0.3)
        cmd("keys E 0.1"); time.sleep(0.6)
        after = block_at(fc, rx, f, rz)
        check("[E] makes the delay one tick longer", before and after and ((after[1] >> 1) & 3) == ((before[1] >> 1) & 3) + 1, (before, after))
        # a 4-tick delay holds a short pulse: take the block away, the lamp stays lit a while (4 ticks + the lamp's 4)
        for _ in range(2): cmd("keys E 0.1"); time.sleep(0.4)
        d = (block_at(fc, rx, f, rz)[1] >> 1) & 3
        cmd(f"breakabs {fc[0] + back[0]} {f} {fc[2] + back[1]}")
        t0 = time.time(); lit_for = None
        while time.time() - t0 < 2.0:
            l = block_at(fc, front[0], f, front[1])
            if l and l[1] & 1 == 0: lit_for = time.time() - t0; break
            time.sleep(0.05)
        check("with a 4-tick delay, the lamp goes out 0.3-0.8 s after the power is gone", d == 3 and lit_for is not None and 0.3 <= lit_for <= 0.8, f"delay bits {d}, out after {lit_for}")
    finally:
        cmd("clearinv")

def t_comparators():
    print("- comparators (#23): pass on the signal behind them (its strength); subtract mode takes the sides off; read chests")
    import pilot
    fc = start_flat(27, [(0, 0), (1, 2), (4, 1)])
    if not check("found a flat outdoor spot", fc): return
    cmd("gamemode survival")
    try:
        f = stone_floor(fc, range(-1, 8), range(-6, 6), lift=2) + 1
        def st(dx, dz):
            b = block_at(fc, dx, f, dz); return b[1] if b else None
        # placed by a right-click on the floor, looking east: it points east
        stand_on(fc, 0, f, 1)
        if not hold("comparator", 4, name="Redstone Comparator"): return
        cmd("look 90 50"); pilot.aim_at((fc[0] + 1.5) * S, f * S + 0.01 + YO, (fc[2] + 1.5) * S); time.sleep(0.4)
        cmd("rmb"); time.sleep(0.8)
        m = re.search(r"comparator\((-?\d+), (-?\d+), (-?\d+)\)y\d+ f(\d+)", cmd("near 6"))
        if not check("a right-click places a comparator, pointing the way you look (east)", m and m.group(4) == "5" and int(m.group(1)) - fc[0] == 1 and int(m.group(3)) - fc[2] == 1, m and m.group(0)): return
        # a redstone block behind, dust in front: full strength comes out, the dust falls off from 15
        for x in range(2, 7): place("redstone_dust", fc, x, f, 1)
        place("redstone_block", fc, 0, f, 1); time.sleep(1.0)
        d = [st(x, 1) for x in range(2, 7)]
        check("a signal behind it comes out at its strength (dust 15, 14, ...)", st(1, 1) == 15 and d == [15, 14, 13, 12, 11], (st(1, 1), d))
        # a weaker signal at its side (dust at 11): compare mode still passes 15 on
        for z in range(-4, 1): place("redstone_dust", fc, 1, f, z)
        place("redstone_block", fc, 1, f, -5); time.sleep(1.0)
        check("a weaker signal at its side: it still passes the one behind on", st(1, 0) == 11 and st(1, 1) == 15, (st(1, 0), st(1, 1)))
        # [E]: subtract mode: 15 - 11 = 4
        stand_on(fc, 1, f, 3)
        pilot.aim_at((fc[0] + 1.5) * S, f * S + 0.1 + YO, (fc[2] + 1.5) * S); time.sleep(0.4)
        cmd("keys E 0.1"); time.sleep(1.0)
        d = [st(x, 1) for x in range(2, 7)]
        check("[E]: subtract mode, behind minus the side (15 - 11 = 4)", st(1, 1) == 16 | 4 and d == [4, 3, 2, 1, 0], (st(1, 1), d))
        # a chest behind: how full it is (4 stacks of 27 slots: 1 + 14 x 4/27 = 3)
        place("chest", fc, 0, f, 4, 4); place("comparator", fc, 1, f, 4, 5)
        for x in range(2, 6): place("redstone_dust", fc, x, f, 4)
        time.sleep(0.8)
        check("an empty chest behind it: nothing", st(1, 4) == 0, st(1, 4))
        hold("cobblestone", 64)
        for _ in range(3): cmd("invgive cobblestone 64")
        time.sleep(1.0)
        cmd(f"chestui open {fc[0]} {f} {fc[2] + 4}")
        for i in range(9): cmd(f"chestui click hot {i} shift"); time.sleep(0.15)
        cmd("chestui close"); time.sleep(1.0)
        d = [st(x, 4) for x in range(2, 6)]
        check("a chest with 4 stacks in it reads 3", st(1, 4) == 3 and d == [3, 2, 1, 0], (st(1, 4), d))
    finally:
        cmd("chestui close"); cmd("clearinv")

def t_creeper():
    print("- creeper (#59): spawns, walks up to you, hisses and swells, explodes (blocks go); walk away and it calms down")
    fc = start_flat(31, [(0, 0), (2, 2)])
    if not check("found a flat outdoor spot", fc): return
    cmd("gamemode survival"); cmd("god 1"); cmd("clearenemies 60")
    def creepers(): return [e for e in cmd("enemies").split(" ; ") if e.startswith("Creeper@")]
    def state(): c = creepers(); return int(re.search(r"state=(\d)", c[0]).group(1)) if c else None
    try:
        # in the light (a placed torch within 8 blocks): it doesn't spawn
        # (spawn one to see which spot the game picks, then light that spot and spawn again there)
        r = cmd("enemy creeper 14"); time.sleep(0.5); cmd("clearenemies 60")
        m = re.search(r"at (-?[\d.]+),(-?[\d.]+),(-?[\d.]+)", r)
        if m:
            x, y, z = (float(v) for v in m.groups())
            tc = (int(x // S), int((y - YO) // S) + 1, int(z // S))
            cmd(f"placeabs torch {tc[0]} {tc[1]} {tc[2]} 1"); time.sleep(0.5)
            cmd("enemy creeper 14"); time.sleep(1.5)
            check("a torch where it would spawn keeps it from spawning", not creepers(), creepers()[:1])
            cmd(f"breakabs {tc[0]} {tc[1]} {tc[2]}"); cmd("clearenemies 60"); time.sleep(0.5)
        # walk away in time: it starts its fuse, then stops when you're far enough
        r = cmd("enemy creeper 14")
        if not check("a creeper spawns outside", "spawned Creeper" in r and creepers(), r): return
        ok = wait(lambda: state() == 2, 40, step=0.2)
        check("it comes up to you and starts its fuse (hiss, swell)", ok, creepers()[:1])
        if ok:
            cmd(f"tprel 0 0 {12 * S:.1f}"); time.sleep(0.6)
            calmed = wait(lambda: state() in (0, 1), 3, step=0.1)
            check("walking away stops the fuse (no explosion)", calmed and creepers(), creepers()[:1])
        # stand still by a block: it blows up, the block goes, the creeper's gone
        f = surface(fc, 0, 0) + 1
        place("cobblestone", fc, 1, f, 0); place("cobblestone", fc, -1, f, 0)
        cmd(f"tp {(fc[0] + .5) * S:.2f} {f * S + YO + 0.1:.2f} {(fc[2] + 1.5) * S:.2f}")  # (between the two blocks' row)
        time.sleep(0.5)
        if not creepers(): cmd("enemy creeper 10")
        hp0 = int(re.search(r"hp=(\d+)", cmd("state")).group(1))
        boom = wait(lambda: not creepers(), 50, step=0.25)
        check("it explodes next to you and is gone", boom, creepers()[:1])
        time.sleep(1.0)
        left = [block_at(fc, dx, f, 0) for dx in (1, -1)]
        check("the blast breaks blocks near it", boom and any(b is None for b in left), left)
    finally:
        cmd("clearenemies 60"); cmd("god 1"); cmd("clearinv")

def t_unstuck():
    print("- /unstuck (#64): boxed in so you can't move, anyone can type /unstuck and lands on free ground nearby")
    fc = start_flat(3, [(0, 0), (1, 0), (-1, 0)])
    if not check("found a flat outdoor spot", fc): return
    cmd("gamemode survival")
    def chat(text):
        cmd("keys Slash 0.08"); time.sleep(0.5)
        cmd("chattext " + text.replace(" ", "_")); time.sleep(0.2)
        cmd("keys Enter 0.08"); time.sleep(1.5)
        return cmd("chattext").split("chat='")[1]
    try:
        f = surface(fc, 0, 0) + 1
        stand_on(fc, 0, f, 0); time.sleep(0.5)
        # stone all round, two high, and over your head
        for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            for dy in (0, 1): place("stone", fc, dx, f + dy, dz)
        place("stone", fc, 0, f + 2, 0); time.sleep(0.8)
        p0 = pos()
        for k in ("W", "S", "A", "D", "Space"): cmd(f"keys {k} 0.5"); time.sleep(0.7)
        p1 = pos()
        check("boxed in, you can't get out", math.dist((p0[0], p0[2]), (p1[0], p1[2])) < 0.5, (p0, p1))
        said = chat("unstuck")
        time.sleep(1.0)
        p2 = pos()
        c2 = (math.floor(p2[0] / S), math.floor(p2[2] / S))
        out = c2 != (fc[0], fc[2]) and math.dist((p1[0], p1[2]), (p2[0], p2[2])) < 7
        check("/unstuck moves you out, a few metres at most", out and "Moved you" in said, (said[-90:], p2))
        walked = []
        for k in ("S", "W", "A", "D"):  # (some way is open: the box is on one side)
            p3 = pos(); cmd(f"keys {k} 0.6"); time.sleep(0.9); p4 = pos()
            walked.append(round(math.dist((p3[0], p3[2]), (p4[0], p4[2])), 2))
        check("and you can walk again", max(walked) > 0.5, walked)
        check("not twice in a row (10 s wait)", "Wait a few seconds" in chat("unstuck")[-120:])
    finally:
        for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            for dy in (0, 1): cmd(f"breakabs {fc[0] + dx} {f + dy} {fc[2] + dz}")
        cmd(f"breakabs {fc[0]} {f + 2} {fc[2]}")
def t_ship_loot():
    print("- ship loot (#69): loose loot in orbit is the ship's (a reload moved it by the door, then the round's end deleted it)")
    if "inShipPhase=True" not in cmd("state"):
        check("in orbit for this test", False, "landed"); return
    def clocks(): return [e.strip() for e in cmd("find clock").split(" ; ") if "@" in e]
    before = len(clocks())
    cmd("loot Clock 2"); time.sleep(3.0)  # (spawned loose, not the ship's: like an item the game moved on loading)
    c = clocks()
    check("loose loot in the ship becomes the ship's", len(c) >= before + 2 and all("room=True" in e and "parent=HangarShip" in e for e in c), [e[-90:] for e in c][:4])

def t_jukebox():
    print("- jukebox (#31): [E] with a music disc plays its track there; [E] gives it back; a broken one drops it")
    import pilot
    fc = start_flat(26, [(0, 0), (0, 2)])
    if not check("found a flat outdoor spot", fc): return
    cmd("gamemode survival")
    try:
        f = stone_floor(fc, range(-1, 2), (0, 1, 2, 3), lift=2) + 1
        place("jukebox", fc, 0, f, 2); time.sleep(0.6)
        stand_on(fc, 0, f, 0)
        if not hold("music_disc_cat", 1, name="Music Disc (cat)"): return
        def aim(): pilot.aim_at((fc[0] + .5) * S, (f + .5) * S + YO, (fc[2] + 2.5) * S); time.sleep(0.4)
        aim(); cmd("keys E 0.1"); time.sleep(3.0)
        j = cmd("jukebox")
        check("[E] with a disc puts it in: it plays (cat, Minecraft's track)", "cat playing=True" in j, j)
        check("the disc is out of your hand", "Music Disc" not in cmd("state"), cmd("state")[60:140])
        def discs(): return [m.group(1) for m in re.finditer(r"Music Disc \(cat\)@[^;]*held=False val=(\d+)", cmd("find music"))]
        d0 = len(discs())
        aim(); cmd("keys E 0.1"); time.sleep(1.5)
        check("[E] again: it stops and the disc pops out, with a value", "none" in cmd("jukebox") and len(discs()) == d0 + 1 and int(discs()[-1]) > 0, f"{cmd('jukebox')} | {discs()}")
        # back in, then the jukebox broken: the disc drops
        cmd("grab"); time.sleep(1.0)
        if "Music Disc" in cmd("state"):
            sl = re.search(r"slots=\[([^\]]*)\]", cmd("state")).group(1).split(",")
            cmd("slot " + str(next(i for i, e in enumerate(sl) if e.startswith("Music Disc")))); time.sleep(0.4)
            aim(); cmd("keys E 0.1"); time.sleep(2.0)
        d1 = len(discs())
        k = re.search(r"jukebox\((-?\d+), (-?\d+), (-?\d+)\)", cmd("near 6"))
        cmd(f"breakabs {k.group(1)} {k.group(2)} {k.group(3)}"); time.sleep(1.5)
        check("a broken jukebox stops and drops its disc", "none" in cmd("jukebox") and len(discs()) == d1 + 1, f"{cmd('jukebox')} | {d1} -> {len(discs())}")
    finally:
        cmd("clearinv")

def t_honey():
    print("- honey blocks (#23): slow to walk on; pistons move what touches them, but they don't stick to slime")
    fc = start_flat(9, [(0, 0), (0, 3)])
    if not check("found a flat outdoor spot", fc): return
    f = stone_floor(fc, range(-1, 3), range(0, 13), lift=2) + 1
    for dz in range(0, 8): place("honey_block", fc, 1, f - 1, dz)  # (a honey strip in the floor, beside the stone)
    time.sleep(0.5)
    def walk(dx):
        stand_on(fc, dx, f, 0); cmd("look 0 0"); time.sleep(0.3)
        z0 = pos()[2]; cmd("keys W 0.8"); time.sleep(1.1)
        return pos()[2] - z0
    stone, honey = walk(0), walk(1)
    check("walking on honey is much slower", honey < stone * 0.7 and stone > 1.0, f"stone {stone:.2f} m, honey {honey:.2f} m")
    # pistons: honey takes the block on it along, not the slime beside it
    # (in the air: honey sticks to whatever it touches, a floor under it too, like Minecraft's; and at a height where
    # no level geometry is in the cells it moves into, or the push is refused, rightly)
    y = f + 2
    rig = [(dx, dy, dz) for dx in (-1, 0, 1, 2, 3) for dy in (0, 1) for dz in (10, 11)]
    for yy in range(f + 1, f + 10):
        if all(cmd(f"obstructed {fc[0] + dx} {yy + dy} {fc[2] + dz}").startswith("no") for dx, dy, dz in rig): y = yy; break
    def at(dx, dy, dz):
        c = (fc[0] + dx, y + dy, fc[2] + dz)
        return near_blocks(c).get(c)  # (around that cell: it's further than "near" reaches from the player)
    place("honey_block", fc, 1, y, 10); place("stone", fc, 1, y + 1, 10); place("slime", fc, 1, y, 11)
    place("piston", fc, 0, y, 10, 5); time.sleep(0.4); place("redstone_block", fc, -1, y, 10); time.sleep(1.5)
    moved = (at(2, 0, 10), at(2, 1, 10), at(1, 0, 11))
    check("a piston moves honey and what's on it, the slime beside it stays", moved[0] and moved[0][0] == "honey_block" and moved[1] and moved[1][0] == "stone" and moved[2] and moved[2][0] == "slime", moved)

def t_water():
    print("- water (#19): a source flows out (a few blocks), a bucket takes it back and pours it; it washes torches away,")
    print("  turns lava to obsidian, softens falls; under it, the game's underwater view and drowning")
    import pilot
    fc = start_flat(11, [(dx, dz) for dx in range(-1, 3) for dz in range(0, 6)])
    if not check("found a flat outdoor spot", fc): return
    cmd("gamemode survival")
    def water(): return int(re.search(r"blocks=(\d+)", cmd("water")).group(1))
    try:
        f = stone_floor(fc, range(-3, 8), range(-1, 8), lift=2) + 1
        w0 = water()
        place("torch", fc, 0, f, 5); time.sleep(0.3)
        cmd(f"placeabs water {fc[0]} {f} {fc[2] + 3} 1"); time.sleep(3.0)
        n = water() - w0
        check("one source flows out over the floor, and stops (a few blocks' reach)", 20 <= n <= 400, f"{n} water blocks")
        check("it washes a torch away", block_at(fc, 0, f, 5) is not None and block_at(fc, 0, f, 5)[0] == "water", block_at(fc, 0, f, 5))
        stand_on(fc, 0, f, 0)
        if not hold("bucket", 1, name="Bucket"): return
        pilot.aim_at((fc[0] + .5) * S, f * S + 0.15 + YO, (fc[2] + 3.5) * S); time.sleep(0.4)
        cmd("rmb"); time.sleep(1.5)
        check("an empty bucket takes the source: a water bucket", "Water Bucket" in cmd("state"), cmd("state")[60:140])
        wait(lambda: water() <= w0, 5, step=0.5)
        check("with its source gone, the flowing water dries up", water() <= w0, f"{water() - w0} left")
        pilot.aim_at((fc[0] + 1.5) * S, f * S + 0.02 + YO, (fc[2] + 2.5) * S); time.sleep(0.4)
        cmd("rmb"); time.sleep(2.0)
        check("the water bucket pours a source out (and is empty again)", block_at(fc, 1, f, 2) == ("water", 0) and "held=Bucket" in cmd("state"), (block_at(fc, 1, f, 2), cmd("state")[60:120]))
        cmd(f"placeabs lava {fc[0] - 2} {f} {fc[2] + 7} 1"); time.sleep(0.5)
        if hold("water_bucket", 1, name="Water Bucket"):
            stand_on(fc, -2, f, 5)
            pilot.aim_at((fc[0] - 1.5) * S, f * S + 0.4 + YO, (fc[2] + 7.5) * S); time.sleep(0.4)
            cmd("rmb"); time.sleep(1.5)
            check("water on lava: obsidian", block_at(fc, -2, f, 7) == ("obsidian", 0), block_at(fc, -2, f, 7))
        # a fall into a deep pool: no damage; under, the game's underwater state
        # (a pool on the floor: stone walls three high around 3x3 of water)
        for dy in range(0, 3):
            for dx in range(3, 8):
                for dz in range(0, 5):
                    inside = 4 <= dx <= 6 and 1 <= dz <= 3
                    if inside: cmd(f"placeabs water {fc[0] + dx} {f + dy} {fc[2] + dz} 1")
                    else: place("stone", fc, dx, f + dy, dz)
        time.sleep(2.0)
        cmd("god 0"); cmd("heal"); time.sleep(0.3)
        cmd(f"tp {(fc[0] + 5.5) * S:.2f} {(f + 21) * S + YO:.2f} {(fc[2] + 2.5) * S:.2f}"); time.sleep(4.0)
        st = state()
        check("a 20-block fall into water: no damage", "hp=100" in st and "dead=False" in st, st[:80])
        cmd("look 0 0"); time.sleep(0.6)
        check("in it, head under: the game's underwater state", "underwater=True" in cmd("flags"), cmd("flags")[-200:-120])
        # swimming is off by default ([Water] Swimming): water is Lethal Company's hazard, you can't swim up
        y0 = pos()[1]; cmd("swimup 1"); time.sleep(1.0); cmd("swimup 0")
        check("swimming off (the default): [Space] doesn't take you up", pos()[1] - y0 < 0.3, f"{pos()[1] - y0:+.2f} m")
        cmd("cfg Water Swimming true"); time.sleep(0.3)
        y0 = pos()[1]; cmd("swimup 1"); time.sleep(1.0); cmd("swimup 0")
        check("swimming on: [Space] swims up", pos()[1] - y0 > 0.8, f"{pos()[1] - y0:+.2f} m")
    finally:
        cmd("swimup 0"); cmd("cfg Water Swimming false"); cmd("god 1"); cmd("heal"); cmd("clearinv")

def t_panes():
    print("- glass panes (#49): a pane joins the blocks beside it, and stops you walking through")
    fc = start_flat(16, [(dx, dz) for dx in (-1, 0, 1) for dz in (1, 2, 3)])
    if not check("found a flat outdoor spot", fc): return
    sy = surface(fc, 0, 2)
    for dx in (-1, 1): place("stone", fc, dx, sy + 1, 2)
    place("glass_pane", fc, 0, sy + 1, 2); time.sleep(1.0)
    m = re.search(r"glass_pane\(-?\d+, -?\d+, -?\d+\)y\d+ f\d+ s\d+ v(\d+)", cmd("near 8"))
    check("between two blocks it joins both (west and east arms)", m and int(m.group(1)) == 12, m.group(0) if m else cmd("near 8")[:200])
    cmd(f"tp {(fc[0] + .5) * S:.2f} {(sy + 1.1) * S + YO:.2f} {(fc[2] + .6) * S:.2f}"); time.sleep(1.0)
    cmd("look 0 0"); z0 = pos()[2]
    cmd("keys W 2.0"); time.sleep(2.3)
    moved = pos()[2] - z0
    check("it stops you", moved < 1.6 * S, f"moved {moved:.2f} m")
    for dx in (-1, 1): dig(fc, dx, sy + 1, 2)
    time.sleep(1.0)
    m = re.search(r"glass_pane\(-?\d+, -?\d+, -?\d+\)y\d+ f\d+ s\d+ v(\d+)", cmd("near 8"))
    check("on its own it spans its block (not just a post)", m and int(m.group(1)) in (3, 12), m.group(0) if m else "")
def t_doors():
    print("- doors (#54): a right-click places one two high; [E] opens and shuts both halves; shut, you can't walk through")
    import pilot
    fc = start_flat(15, [(0, 1), (0, 2), (0, 3), (0, 4)])
    if not check("found a flat outdoor spot", fc): return
    cmd("gamemode survival")
    def doors(): return [(tuple(map(int, m.group(1, 2, 3))), int(m.group(4))) for m in re.finditer(r"oak_door\((-?\d+), (-?\d+), (-?\d+)\)y\d+ f\d+ s(\d+)", cmd("near 12"))]
    try:
        if not hold("oak_door"): return
        # a stone floor to stand and build on (a ground cell can be mostly air: the terrain may sit well below its top)
        sy = max(surface(fc, 0, dz) for dz in range(0, 5))
        for dz in range(-2, 5): place("stone", fc, 0, sy + 1, dz)  # (from two blocks back: the walk at the door)
        time.sleep(0.5)
        cmd(f"tp {(fc[0] + .5) * S:.2f} {(sy + 2.5) * S + YO:.2f} {(fc[2] + .5) * S:.2f}"); time.sleep(0.3)
        wait(lambda: "grounded=True" in cmd("state"), 2.0, step=0.15); time.sleep(0.3)
        cmd("look 0 50"); time.sleep(0.4)
        cmd("rmb"); time.sleep(1.0)
        d = doors()
        if not check("a right-click places a door: two halves (lower, upper)", len(d) == 2 and sorted(s for _, s in d) == [0, 2], d): return
        low = min(d, key=lambda e: e[0][1])[0]
        def panel():
            """where the lower half's panel is drawn (open, it swings round to the hinge side of its cell)"""
            for m in re.finditer(r"\((-?\d+), (-?\d+), (-?\d+)\)@(-?[\d.]+),(-?[\d.]+),(-?[\d.]+)", cmd("where oak_door 15")):
                if tuple(map(int, m.group(1, 2, 3))) == low: return tuple(map(float, m.group(4, 5, 6)))
        def use_door():
            c = panel(); pilot.aim_at(c[0], c[1] + 0.2, c[2]); time.sleep(0.3)
            cmd("keys E 0.1"); time.sleep(1.0)
        c0 = panel()
        use_door()
        check("[E] opens both halves", sorted(s for _, s in doors()) == [1, 3], doors())
        use_door()
        check("[E] again shuts them", sorted(s for _, s in doors()) == [0, 2], doors())
        # walk at it from two blocks back
        def walk_through():
            cmd(f"tp {c0[0]:.2f} {c0[1] - 0.5 * S + 0.3:.2f} {c0[2] - 2.0 * S:.2f}"); time.sleep(1.0)
            cmd("look 0 0")
            cmd("keys W 2.0"); time.sleep(2.3)
            return pos()[2] - c0[2]  # (past the shut door's panel: > 0)
        shut = walk_through()
        check("shut, it stops you", shut < 0, f"ended {shut:.2f} m past the door")
        use_door()
        check("[E] opens it again", sorted(s for _, s in doors()) == [1, 3], doors())
        opened = walk_through()
        check("open, you walk through", opened > S, f"ended {opened:.2f} m past the door (shut: {shut:.2f})")
    finally:
        cmd("clearinv")
def t_ladders():
    print("- ladders (#30): [E] on a ladder, W climbs it, over the top onto the wall")
    import pilot
    fc = start_flat(18, [(0, dz) for dz in range(0, 4)])
    if not check("found a flat outdoor spot", fc): return
    sy = surface(fc, 0, 2)
    for dy in (1, 2, 3): place("stone", fc, 0, sy + dy, 3)
    for dy in (1, 2, 3): place("ladder", fc, 0, sy + dy, 2, facing=3)   # (on the wall's south face)
    time.sleep(1.0)
    n = len(re.findall(r"ladder\(", cmd("near 8")))
    if not check("three ladders on the wall", n == 3, cmd("near 8")[:200]): return
    cmd("clearinv")
    # a step back from the ladder (in the cell in front of it), looking at the bottom rung
    cmd(f"tp {(fc[0] + .5) * S:.2f} {(sy + 1.1) * S + YO:.2f} {(fc[2] + 1.4) * S:.2f}"); time.sleep(1.0)
    y0 = pos()[1]
    pilot.aim_at((fc[0] + .5) * S, (sy + 1.6) * S + YO, (fc[2] + 2.3) * S); time.sleep(0.4)
    tip = cmd("hover")
    check("looking at it: Climb : [E]", "Climb" in tip, tip)
    cmd("keys E 0.1"); time.sleep(1.2)
    top = y0
    cmd("keys W 2.5")
    for _ in range(12): time.sleep(0.25); top = max(top, pos()[1])
    check("W climbs it and over the top (up onto the wall, three blocks up)", top - y0 > 2.5 * S, f"rose {top - y0:.2f} m")
def t_stairs():
    print("- stairs (#50): you walk straight up them, no jumping")
    fc = start_flat(17, [(0, dz) for dz in range(0, 5)])
    if not check("found a flat outdoor spot", fc): return
    sy = max(surface(fc, 0, dz) for dz in range(0, 5))
    # a stone floor to start on (a ground cell can be mostly air: the terrain may sit well below its top), then two
    # steps up: a stair, then a stone block with a stair on it (the low sides towards the player, south)
    for dz in range(0, 5): place("stone", fc, 0, sy + 1, dz)
    place("cobblestone_stairs", fc, 0, sy + 2, 2, facing=3)
    place("stone", fc, 0, sy + 2, 3)
    place("cobblestone_stairs", fc, 0, sy + 3, 3, facing=3)
    for dz in range(4, 8): place("stone", fc, 0, sy + 3, dz)  # (a landing at the top)
    time.sleep(1.0)
    cmd(f"tp {(fc[0] + .5) * S:.2f} {(sy + 2.5) * S + YO:.2f} {(fc[2] + .5) * S:.2f}"); time.sleep(0.3)
    wait(lambda: "grounded=True" in cmd("state"), 2.0, step=0.15); time.sleep(0.3)
    cmd("look 0 0"); p0 = pos(); y0 = p0[1]
    # (heights sampled on the way: past the ends of the floor you walk off it)
    def walk(secs):
        cmd(f"keys W {secs}"); ys = []
        for _ in range(int((secs + 0.3) / 0.15)): time.sleep(0.15); ys.append(pos()[1] - y0)
        return ys
    ys = walk(1.6)
    check("walking forward climbs both steps (about two blocks up)", max(ys) > 1.6 * S,
          f"got {max(ys):.2f} m up (a block is {S} m), from {p0} to {pos()}; {cmd('near 3')}")
    # and back down: from the top, turn round and walk down to the floor
    cmd("look 180 0"); ys = walk(1.6)
    check("and back down", min(abs(y) for y in ys) < 0.3, f"heights on the way {['%.2f' % y for y in ys]}")

def armor_reduce(dmg, pts, tough):
    """armor as extra effective health (the balance defaults: 2% a point, 3.125% a point of toughness)"""
    v = dmg / (1.0 + pts * 0.02 + tough * 0.03125)
    return max(1, int(v + 0.5) if v - int(v) != 0.5 else (int(v) if int(v) % 2 == 0 else int(v) + 1))  # (C#'s banker's rounding)

def t_armor():
    print("- armor: the [I] inventory's armor slots, right-click to wear, damage reduced like Minecraft (falls aren't)")
    cmd("craftui close")
    # start bare (a saved game may have someone wearing armor)
    cmd("keys I 0.08"); time.sleep(0.6)
    for i in range(4): cmd(f"craftui click armor {i} shift")
    time.sleep(0.8); cmd("craftui close"); time.sleep(0.4)
    empty_hotbar(); cmd("clearinv"); time.sleep(0.5)
    start_flat(6)
    for k in ("iron_chestplate", "diamond_helmet", "iron_leggings", "golden_boots"): cmd(f"invgive {k} 1")
    time.sleep(2.0)
    cmd("keys I 0.08"); time.sleep(0.6)
    st = cmd("craftui state")
    if not check("[I] shows four armor slots", "armor=[-,-,-,-]" in st, st): cmd("craftui close"); return
    ci = slot_of(st, "iron_chestplate")[0]
    cmd(f"craftui click hot {ci}")
    st = cmd("craftui click armor 0")
    check("a chestplate doesn't go in the helmet slot", "cursor=iron_chestplatex1" in st and "armor=[-,-,-,-]" in st, st)
    st = cmd("craftui click armor 1")
    check("the chestplate goes in its slot", "armor=[-,iron_chestplate,-,-]" in st and "pts=6" in st, st)
    st = cmd(f"craftui click hot {slot_of(st, 'diamond_helmet')[0]} shift")
    check("shift-click puts a helmet on", "armor=[diamond_helmet,iron_chestplate,-,-]" in st and "pts=9" in st, st)
    cmd("craftui close"); time.sleep(0.5)
    # right-click with leggings in hand
    st = cmd("state")
    sl = re.search(r"slots=\[([^\]]*)\]", st).group(1).split(",")
    cmd(f"slot {next(i for i, e in enumerate(sl) if e.startswith('Iron Leggings'))}"); time.sleep(0.4)
    cmd("rmb"); time.sleep(1.0)
    cmd("keys I 0.08"); time.sleep(0.6)
    st = cmd("craftui state")
    check("right-click puts the leggings on", "armor=[diamond_helmet,iron_chestplate,iron_leggings,-]" in st and "pts=14" in st and total_of(st, "iron_leggings") == 0, st)
    cmd("craftui close"); time.sleep(0.4)
    # damage: 14 points, toughness 2 (the diamond helmet)
    if "inShipPhase=True" not in cmd("state"):
        def hp(): return int(re.search(r"hp=(\d+)", cmd("state")).group(1))
        # (in the ship: with god mode off for this, a fall or quicksand outside could kill the player and end the round)
        cmd("tpship"); cmd("unsink"); time.sleep(1.0)
        cmd("hunger 20 5")  # (fed: starvation damage during the measurement would count as a hit)
        cmd("god 0")
        try:
            got = {}
            for dmg, cause in ((20, "Mauling"), (60, "Mauling"), (20, "Gravity")):
                cmd("heal"); time.sleep(0.3); h0 = hp(); cmd(f"hurt {dmg} {cause}"); time.sleep(0.4); got[(dmg, cause)] = h0 - hp()
        finally:
            cmd("heal"); cmd("god 1")
        want = {(20, "Mauling"): armor_reduce(20, 14, 2), (60, "Mauling"): armor_reduce(60, 14, 2), (20, "Gravity"): 20}
        check("armor turns damage down like Minecraft's (and falls go straight through)", got == want, f"got {got}, want {want}")
    # take it all off again (other tests expect a bare player)
    cmd("keys I 0.08"); time.sleep(0.6)
    for i in range(4): cmd(f"craftui click armor {i} shift")
    st = settle_ui()
    check("shift-click takes armor off into the hotbar", "armor=[-,-,-,-]" in st and total_of(st, "iron_chestplate") == 1, st)
    cmd("craftui close"); time.sleep(0.4)

def t_pick_block():
    print("- creative: middle-click picks the block you look at (in the hotbar already: select it; else a stack in hand)")
    import pilot
    fc = start_flat(26, [(0, 2)])
    if not check("found a flat outdoor spot", fc): return
    def st(): return re.search(r"slot=(\d+) held=(.*?) wt=.*?slots=\[([^\]]*)\]", cmd("state")).groups()
    cmd("clearinv"); time.sleep(1.0); cmd("gamemode creative"); time.sleep(0.5)
    try:
        sy = surface(fc, 0, 2)
        place("glass", fc, 0, sy + 1, 2); time.sleep(0.5)
        cell = f"({fc[0]}, {sy + 1}, {fc[2] + 2})"
        def aim():
            for dy in (0.5, 0.3, 0.7):
                pilot.aim_at((fc[0] + .5) * S, (sy + 1 + dy) * S + YO, (fc[2] + 2.5) * S); time.sleep(0.3)
                if cell in cmd("mine?"): return
        aim(); cmd("mmb")
        wait(lambda: st()[1] == "Glass", 4, step=0.3)
        check("picking a block puts a stack of it in your hand", st()[1] == "Glass" and "Glassx64" in st()[2], str(st()))
        cmd("invgive cobblestone 10"); time.sleep(2.0)
        cobble = st()[2].split(",").index("Cobblestonex10"); cmd(f"slot {cobble}"); time.sleep(0.4)
        aim(); time.sleep(0.3); cmd("mmb"); time.sleep(1.0)
        check("picking a block that's in the hotbar selects it (no second stack)", st()[1] == "Glass" and st()[2].count("Glass") == 1, str(st()))
        # a full hotbar: the held item makes room
        for k in ("dirt", "sand", "gravel", "bricks", "torch", "stone", "oak_planks"): cmd(f"invgive {k} 1")
        wait(lambda: "-" not in st()[2].split(","), 5, step=0.3); time.sleep(1.0)
        r = place("ice", fc, 0, sy + 2, 2); time.sleep(0.5)
        ice = re.search(r"\(-?\d+, -?\d+, -?\d+\)", r).group(0) if re.search(r"\(-?\d+, -?\d+, -?\d+\)", r) else None
        held = st()[1]
        for dy in (0.5, 0.7, 0.85, 0.3):  # (aimed at the ice's cell, not the glass under it)
            pilot.aim_at((fc[0] + .5) * S, (sy + 2 + dy) * S + YO, (fc[2] + 2.5) * S); time.sleep(0.3)
            if ice and ice in cmd("mine?"): break
        aimed = cmd("mine?")[:60]
        cmd("mmb")
        wait(lambda: st()[1] == "Ice", 4, step=0.3)
        check("with a full hotbar, the picked block replaces the held item", st()[1] == "Ice" and not any(e.startswith(held + "x") or e == held for e in st()[2].split(",")),
              f"held {held} before, aimed at {aimed} (ice {ice}): {st()}")
    finally:
        cmd("gamemode survival"); cmd("clearinv")
def t_auto_pickup():
    print("- auto-pickup (AutoPickupItems, on for this test): walking over Minecraft items picks them up, not loot, not what you just dropped")
    fc = start_flat(28, [(0, 2)])
    if not check("found a flat outdoor spot", fc): return
    def st(): return re.search(r"slot=(\d+) held=(.*?) wt=.*?slots=\[([^\]]*)\]", cmd("state")).groups()
    cmd("clearinv"); time.sleep(1.0)
    try:
        hold("cobblestone", 5); cmd("look 0 30")
        cmd("autopick 0"); cmd("give torch 8"); time.sleep(3.0)
        check("off: items on the ground stay there", "Torch" not in st()[2], str(st()))
        cmd("autopick 1"); cmd("tprel 0 0 0.7")
        wait(lambda: "Torchx8" in st()[2], 4, step=0.3)
        wait(lambda: st()[1] == "Cobblestone", 3, step=0.2)  # (the game switches to the new slot; back to what you held)
        s1 = st()
        check("on: walking over them picks them up, and you keep holding what you held", "Torchx8" in s1[2] and s1[1] == "Cobblestone", str(s1))
        cmd("give ender_pearl 1"); time.sleep(2.5); cmd("tprel 0 0 0.7"); time.sleep(2.5)
        check("loot (an ender pearl) isn't swept up", "Ender Pearl" not in st()[2], str(st()))
        cmd(f"slot {st()[2].split(',').index('Torchx8')}"); time.sleep(0.4); cmd("keys G 0.08"); time.sleep(1.0)
        check("what you just dropped stays on the ground a moment", "Torch" not in st()[2], str(st()))
        wait(lambda: "Torchx8" in st()[2], 6, step=0.3)
        check("then it comes back if you stand on it", "Torchx8" in st()[2], f"{st()} | me {pos()} | torch {cmd('find torch')[:120]}")
    finally:
        cmd("autopick 0"); cmd("clearinv")

def t_big_inventory():
    print("- the big inventory (BigInventory, on for this test): the 3x9 grid in [I], shift-click in and out, weight counts, off = out only")
    def wt(): return float(re.search(r"weight=([\d.]+)", cmd("biginv")).group(1))
    def cells(st): return re.search(r"cells=\[([^\]]*)\]", st).group(1).split(",")
    cmd("craftui close"); cmd("biginv 1"); time.sleep(0.5)
    try:
        # (start empty: whatever an earlier run left stored comes out)
        cmd("keys I 0.08"); time.sleep(0.8)
        for i, c in enumerate(cells(cmd("craftui state"))):
            if c != ".": cmd(f"craftui click storage {i} shift"); time.sleep(0.4)
        cmd("craftui close"); time.sleep(0.5)
        empty_hotbar()
        for k, n in (("cobblestone", 40), ("oak_planks", 12)): cmd(f"invgive {k} {n}")
        time.sleep(2.0)
        cmd("keys I 0.08"); time.sleep(0.8)
        st = cmd("craftui state")
        if not check("[I] shows the storage grid", "storageShown=True" in st, st[-200:]): return
        cmd(f"craftui click hot {slot_of(st, 'cobblestone')[0]} shift"); time.sleep(0.8)
        st = cmd("craftui state")
        check("shift-click puts a hotbar stack into the storage", "cobblestonex40" in cells(st) and slot_of(st, "cobblestone")[0] is None, st[-260:])
        pi = slot_of(st, "oak_planks")[0]
        cmd(f"craftui click hot {pi}"); cmd("craftui click storage 9"); time.sleep(0.8)
        st = cmd("craftui state")
        check("a stack clicked into a storage slot lands there", cells(st)[9] == "oak_planksx12", st[-260:])
        cmd("craftui close"); time.sleep(0.8)
        check("what's stored counts toward carry weight", wt() > 0.0, cmd("biginv"))
        cmd("keys I 0.08"); time.sleep(0.8)
        cmd("biginv 0"); time.sleep(0.5)
        cmd("craftui click storage 9 shift"); time.sleep(0.8)
        st = cmd("craftui state")
        check("switched off: what's stored still comes out", cells(st)[9] == "." and slot_of(st, "oak_planks")[0] is not None, st[-260:])
        cmd(f"craftui click hot {slot_of(st, 'oak_planks')[0]}"); cmd("craftui click storage 9"); time.sleep(0.8)
        st = cmd("craftui state")
        check("switched off: nothing new goes in", cells(st)[9] == ".", st[-260:])
        cmd("biginv 1"); time.sleep(0.3)
        # (the planks are still on the mouse: closing puts them back in the hotbar) then the rest comes out
        cmd("craftui close"); time.sleep(0.8); cmd("keys I 0.08"); time.sleep(0.8)
        for i, c in enumerate(cells(cmd("craftui state"))):
            if c != ".": cmd(f"craftui click storage {i} shift"); time.sleep(0.8)
        cmd("craftui close"); time.sleep(0.8)
        check("emptied again: no stored weight left", wt() == 0.0, cmd("biginv"))
    finally:
        cmd("craftui close"); cmd("biginv 0"); cmd("clearinv")

def t_swords():
    print("- swords: crafted (not sold), a diamond one kills a baboon hawk (4 HP) in three swings, one swing per 0.7 s")
    import sword_test as SW
    cmd("craftui close")
    empty_hotbar()
    start_flat(7)
    for k, n in (("diamond", 2), ("stick", 1)): cmd(f"invgive {k} {n}")
    time.sleep(1.8)
    st = cmd("craftui open table")
    di = slot_of(st, "diamond")[0]
    cmd(f"craftui click hot {di}"); cmd("craftui click grid 1 right"); cmd("craftui click grid 4 right"); cmd(f"craftui click hot {di}")
    si = slot_of(settle_ui(), "stick")[0]
    cmd(f"craftui click hot {si}"); st = cmd("craftui click grid 7")
    check("two diamonds over a stick make a diamond sword", "out=diamond_swordx1" in st, st)
    cmd("craftui click out 0"); cmd("craftui close")
    wait(lambda: "Diamond Sword" in cmd("state"), 4, step=0.3)  # (it arrives a moment later)
    check("swords aren't sold", "Diamond Sword" not in cmd("storeprices") and "LMC_NotSold" in cmd("termparse buy diamond sword"), cmd("termparse buy diamond sword"))
    took, hits = SW.fight("diamond_sword", 9)
    dmg = sum(d for _, d in hits)
    gaps = [round(b[0] - a[0], 2) for a, b in zip(hits, hits[1:])]
    check("a diamond sword kills a baboon hawk in two swings (2 damage a hit)", dmg >= 4 and len(hits) <= 2, f"hits {hits}")
    check("one swing per 0.7 s at most (clicking faster doesn't help)", all(g >= 0.65 for g in gaps), f"gaps {gaps}")
    # a second one on the same spot, the first one's body in front of it: a dead monster doesn't soak up the swings
    took, hits = SW.fight("stone_sword", 9)
    check("swings go past a dead monster to the live one behind it", sum(d for _, d in hits) >= 4, f"hits {hits}")
    cmd("clearinv")

def t_trees():
    print("- trees: chop a moon tree down (an axe is faster), it shatters like the cruiser's and drops oak logs")
    import tree_test as TT
    if not TT.trees(400):
        print("  (no trees on this moon)")
        return
    r = TT.chop("wooden_axe", 0, None, 2.0)
    if not check("found a tree to chop", r is not None): return
    gone, took, logs = r
    check("a wooden axe fells a tree in about 4.5 s", gone and took < 8, f"{'down' if gone else 'standing'} after {took:.1f}s")
    check("it drops oak logs on the ground beside it", logs >= 1, f"{logs} new log stacks")
    cmd("clearinv")

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
    # (every one in the level, not just the nearest few: other tests leave things lying around)
    on_ground = lambda name: sum(1 for e in cmd("find " + name.lower()).split(" ; ") if e.strip().startswith(name) and "held=False" in e)
    g0 = on_ground("Stone Pickaxe")
    cmd(f"breakabs {c[0]} {c[1]} {c[2]}"); time.sleep(1.5)
    check("a broken chest drops what was inside", on_ground("Stone Pickaxe") > g0, f"{g0} -> {on_ground('Stone Pickaxe')}")
    cmd("clearinv")

def t_tool_wear_kept():
    print("- a worn tool keeps its wear through a chest and the [I] inventory (#56)")
    fc = start_flat(19, [(0, 2)])
    if not check("found a flat outdoor spot", fc): return
    cmd("gamemode survival"); cmd("chestui close"); cmd("craftui close")
    def wear():
        r = cmd("tooluse"); m = re.search(r"used (\d+)/", r)
        return int(m.group(1)) if m else r
    def slot(st):
        for i, (k, n) in enumerate(hotbar(st)):
            if k and k.startswith("iron_pickaxe"): return i
    def to_hand():
        i = slot(cmd("craftui state"))
        if i is not None: cmd(f"slot {i}"); time.sleep(0.5)
    try:
        if not hold("iron_pickaxe"): return
        cmd("tooluse 100"); time.sleep(0.5)
        check("worn to 100 uses", wear() == 100, cmd("tooluse"))
        sy = surface(fc, 0, 2)
        r = place("chest", fc, 0, sy + 1, 2)
        if not check("chest placed", r.startswith("ok"), r): return
        c = (fc[0], sy + 1, fc[2] + 2)
        st = cmd(f"chestui open {c[0]} {c[1]} {c[2]}")
        if not check("chest screen opens", "open=True" in st, st): return
        cmd(f"chestui click hot {slot(st)}"); cmd("chestui click chest 4"); time.sleep(0.8)
        st = cmd("chestui state")
        check("the worn pickaxe is in the chest", "iron_pickaxe#100x1" in st, st)
        cmd("chestui click chest 4 shift"); time.sleep(1.8)
        cmd("chestui close"); time.sleep(0.5)
        to_hand()
        check("taken back out, it's still worn (100 uses)", wear() == 100, cmd("tooluse"))
        # moved to another hotbar slot in the [I] inventory
        cmd("keys I 0.08"); time.sleep(0.6)
        st = cmd("craftui state"); i = slot(st); j = free_slots(st)[0]
        cmd(f"craftui click hot {i}"); cmd(f"craftui click hot {j}"); time.sleep(1.2)
        cmd("craftui close"); time.sleep(0.6)
        to_hand()
        check("moved in the [I] inventory, it's still worn", wear() == 100, cmd("tooluse"))
        # a broken chest drops it worn
        st = cmd(f"chestui open {c[0]} {c[1]} {c[2]}")
        cmd(f"chestui click hot {slot(st)}"); cmd("chestui click chest 0"); time.sleep(0.8)
        # (into the chest for sure: still in hand, breaking the chest would count as a use of it)
        if not check("put back in the chest", wait(lambda: "iron_pickaxe#100x1" in cmd("chestui state"), 3, step=0.3), cmd("chestui state")[:160]): return
        cmd("chestui close"); time.sleep(0.3)
        cmd(f"breakabs {c[0]} {c[1]} {c[2]}"); time.sleep(1.5)
        # (step onto the dropped pickaxe itself: the chest drops too, and other tests leave things lying around)
        cx, cz = (c[0] + .5) * S, (c[2] + .5) * S
        drops = [tuple(map(float, m.groups())) for m in re.finditer(r"Iron Pickaxe@([-\d.]+),([-\d.]+),([-\d.]+) held=False", cmd("find iron pickaxe"))]
        if drops:
            x, y, z = min(drops, key=lambda d: (d[0] - cx) ** 2 + (d[2] - cz) ** 2)
            cmd(f"tp {x:.2f} {y + 0.2:.2f} {z:.2f}"); time.sleep(0.5)
        for _ in range(4):
            if "Iron Pickaxe" in cmd("state"): break
            cmd("grab"); time.sleep(0.8)
        to_hand()
        check("dropped by a broken chest, it's still worn", wear() == 100, cmd("tooluse") + " | " + cmd("state")[60:180])
    finally:
        cmd("chestui close"); cmd("craftui close"); cmd("clearinv")

def t_slime_observer():
    print("- slime blocks stick when pushed and pulled; observers pulse when what they watch changes")
    # every cell the contraptions use or move into must be open air (no tree, rock or ground in the way)
    cells = [(x, dy, z) for x in (-1, 0, 1, 2) for dy in (0, 1) for z in (2, 3, 6, 9)]
    fc = y = None
    for attempt in range(6):
        fc = start_flat(14 + attempt * 2, [(0, 2), (0, 6), (0, 9)])
        if not fc: continue
        tops = [surface(fc, x, z) for x in (-1, 0, 1, 2) for z in (2, 3, 6, 9)]
        if any(t is None for t in tops): fc = None; continue
        y = max(tops) + 2  # build in the air above the ground
        rs = cmds(c for x, dy, z in cells for c in (f"obstructed {fc[0] + x} {y + dy} {fc[2] + z}", f"cellabs {fc[0] + x} {y + dy} {fc[2] + z}"))
        if all(o == "no" and ") Air" in c for o, c in zip(rs[0::2], rs[1::2])): break
        fc = None
    if not check("found a flat outdoor spot", fc): return
    X, Z = fc[0], fc[2]
    def at(x, yy, z): return near_blocks((X + x, yy, Z + z)).get((X + x, yy, Z + z), (None,))[0]
    # 1) push: piston east, slime in front with stone on top and planks beside it
    place("slime", fc, 1, y, 2); place("stone", fc, 1, y + 1, 2); place("oak_planks", fc, 1, y, 3)
    place("piston", fc, 0, y, 2, 5); time.sleep(0.3)
    place("redstone_block", fc, -1, y, 2); time.sleep(1.5)
    moved = (at(2, y, 2), at(2, y + 1, 2), at(2, y, 3))
    if not check("a slime block drags the blocks stuck to it", moved == ("slime", "stone", "oak_planks"), str(moved)):
        print("      piston:", near_blocks((X, y, Z + 2)).get((X, y, Z + 2)), "now at 1:", (at(1, y, 2), at(1, y + 1, 2), at(1, y, 3)))
        for x, dy, z in ((2, 0, 2), (2, 1, 2), (2, 0, 3), (1, -1, 2), (1, 0, 1)):
            print(f"      ({x},{dy},{z})", cmd(f"cellabs {X + x} {y + dy} {Z + z}")[:90], "obstructed:", cmd(f"obstructed {X + x} {y + dy} {Z + z}"))
    # 2) pull: sticky piston pushes slime (with stone on top) out, then pulls it all back
    place("slime", fc, 1, y, 6); place("stone", fc, 1, y + 1, 6)
    place("sticky_piston", fc, 0, y, 6, 5); time.sleep(0.3)
    place("redstone_block", fc, -1, y, 6); time.sleep(1.5)
    out = (at(2, y, 6), at(2, y + 1, 6))
    cmd(f"breakabs {X - 1} {y} {Z + 6}"); time.sleep(1.5)
    back = (at(1, y, 6), at(1, y + 1, 6))
    check("a sticky piston pulls the whole slime structure back", out == ("slime", "stone") and back == ("slime", "stone"), f"out={out} back={back} piston={at(0, y, 6)}")
    # 3) observer: watching (1,y,9); a lamp behind it
    place("observer", fc, 0, y, 9, 5); place("redstone_lamp", fc, -1, y, 9); time.sleep(1.0)
    p0 = int(cmd("pulses"))
    place("cobblestone", fc, 1, y, 9)
    lit_at, dark_at, t0 = None, None, time.time()
    while time.time() - t0 < 1.5 and dark_at is None:
        e = near_blocks((X - 1, y, Z + 9)).get((X - 1, y, Z + 9))
        on = bool(e and e[1] & 1)
        if on and lit_at is None: lit_at = time.time()
        if not on and lit_at is not None: dark_at = time.time()
        time.sleep(0.02)
    time.sleep(0.3)
    p1 = int(cmd("pulses"))
    check("an observer pulses once when the block it watches changes", p1 - p0 == 1, f"{p0} -> {p1}")
    check("the pulse powers what's behind it", lit_at is not None, "lamp lit" if lit_at else "lamp never lit")
    # Minecraft: a lamp goes out 4 ticks after losing power, so the 2-tick pulse shows for ~0.3 s (not a 1-frame blink)
    shown = (dark_at or time.time()) - lit_at if lit_at else 0
    check("the lamp's flash lasts long enough to see (lamp off delay)", 0.18 < shown < 1.0, f"lit for {shown:.2f}s")
    cmd(f"breakabs {X - 1} {y} {Z + 2}")  # switch the first contraption's piston off (other tests look for piston heads)

def t_store_names():
    print("- the store understands multi-word names (the terminal reads one word per noun)")
    cases = {"buy block of iron": "Block of Iron", "buy block of coal 2": "Block of Coal", "buy diamond block": "Block of Diamond",
             "buy stone": "Stone", "buy stone 5": "Stone", "buy oak log": "Oak Log", "buy redstone torch": "Redstone Torch",
             "buy chest": "Chest", "buy shovel": "Shovel", "buy flint and steel": "Flint and Steel"}
    bad = {q: r for q, want in cases.items() for r in [cmd(f"termparse {q}")] if not r.endswith("item=" + want)}
    check("each name reaches the right item (and vanilla items still work)", not bad, str(bad)[:300])
    # a plural or typo goes to the closest store word, not the first sharing 3 letters (#39: bookshelves -> boombox)
    plural = {q: r for q, want in {"buy bookshelves": "Bookshelf", "buy observers 2": "Observer", "buy cooked porkchops": "Cooked Porkchop",
              "buy redstone torches": "Redstone Torch", "buy tnt crate": "TNT Crate", "buy jack o lantern": "Jack o'Lantern"}.items()
              for r in [cmd(f"termparse {q}")] if not r.endswith("item=" + want)}
    check("plurals and look-alike names reach the right item (bookshelves isn't a boombox)", not plural, str(plural)[:300])
    sc = cmd("storecheck")
    check("every store item's word (and its plurals) orders that item", " 0 wrong" in sc, sc[:300])
    # tiered tools are crafting only (buy wood, craft a wooden pickaxe, work your way up)
    sold = {q: r for q in ("buy stone pickaxe", "buy stone axe", "buy stone shovel", "buy iron pickaxe", "buy diamond") for r in [cmd(f"termparse {q}")]
            if "LMC_NotSold" not in r}
    check("no tiered tools in the store (ordering one says it's crafting only)", not sold, str(sold)[:300])
    prices = {k: v for k, v in (("Block of Iron", 600), ("Block of Diamond", 1200), ("Block of Coal", 200), ("Slime Block", 800), ("Observer", 100), ("TNT", 20), ("TNT Crate", 200))
              if f"{k}={v}" not in cmd("storeprices")}
    check("store prices are the balance defaults (iron block 600, diamond block 1200, coal block 200, slime 800, observer 100, TNT 20 and a crate of 20 for 200)", not prices, cmd("storeprices")[:300])
    # typed for real (letter by letter) after a purchase: that screen used to cap input at 15 characters
    if "inShipPhase=True" in state():
        import pilot
        cmd("credits 5000")  # (2 slime blocks: 1600 at the balance prices)
        if check("the terminal opens", pilot.use_terminal()):
            for line in ("buy tnt", "confirm", "buy slime block 2", "", "deny", "buy redstone block", "", "deny"):
                if line == "":
                    scr = cmd("termscreen"); continue
                cmd("termtype " + line); time.sleep(0.6)
                if line.startswith("buy slime"): slime = cmd("termscreen")
                if line.startswith("buy redstone"): red = cmd("termscreen")
            pilot.press("Tab")
            check("after a purchase, 'buy slime block 2' orders 2 slime blocks", "Slime Block" in slime and "Amount: 2" in slime, slime[-160:])
            check("after a purchase, 'buy redstone block' orders a Block of Redstone", "Block of Redstone" in red, red[-160:])

def t_nodes_air():
    print("- no phantom ground: the air above every AI node (where monsters walk) reads as air")
    for where in ("outside", "inside"):
        r = cmd(f"nodecheck {where}")
        check(f"{where} nodes are in open air", " 0 in phantom" in r, r[:400])

def t_screen_clicks():
    print("- clicks inside a crafting screen don't act in the world after it closes")
    fc = start_flat(5)
    if not check("found a flat outdoor spot", fc): return
    cmd("clearinv"); cmd("give torch 5"); time.sleep(1.5); grab_all(); time.sleep(0.8)
    cmd("look 0 45"); time.sleep(0.4)
    torches = lambda: sum(1 for e in cmd("near 6").split(" ; ") if e.strip().startswith("torch"))
    t0 = torches()
    cmd("keys I 0.08"); time.sleep(0.8)
    xy = cmd("craftui pos grid 0")
    cmd(f"mouse moveto {xy}"); time.sleep(0.2)
    cmd("mouse right 0.08"); time.sleep(0.6)
    cmd("mouse left 0.08"); time.sleep(0.6)
    cmd("keys I 0.08"); time.sleep(1.2)
    check("no torch placed by a right-click made in the screen", torches() == t0, f"{t0} -> {torches()}")
    cmd("clearinv")

def t_company():
    print("- Gordion: no digging (AllowDiggingAtCompany = false); the platform is open air")
    # the platform in front of the big wall by the selling window is box colliders: open air above it once read as
    # underground (a TNT there filled the air with blocks 4-5 out from the wall)
    phantom = [(x, y, z) for z in range(-34, -6, 2) for x in range(-20, -12) for y in (-2, -1, 0)
               if ") Air" not in cmd(f"cellabs {x} {y} {z}")]
    check("open air above the Company platform is air", not phantom, str(phantom[:6]))
    fc = start_flat(0) or feet_cell()
    s0 = stats()
    res = [cmd(f"digabs {fc[0] + dx} {fc[1] + dy} {fc[2] + dz}").split(" (")[0] for dx, dy, dz in ((0, -1, 0), (1, -1, 0), (0, -2, 1), (2, -3, 0))]
    check("every dig at the Company is refused", all(r == "bedrock" for r in res), ", ".join(res))
    cmd(f"placeabs tnt {fc[0] + 2} {fc[1]} {fc[2] + 2} 1"); cmd("ignite 20"); time.sleep(4)
    s1 = stats()
    check("TNT leaves the Company's ground alone", s1["cuts"] == s0["cuts"], f"{s0} -> {s1}")

# tests that time real input tightly (a jump and a right-click at its top, a double-tap): at higher game speeds a
# command's round trip is too much game time (about 11 ms of wall time each), so they run at most this fast
# (found by running at 6x and 8x: a double-tap, a jump-and-place, swing timing, a lamp's short flash, items arriving)
MAX_SPEED = {"t_pillar": 2, "t_creative": 2, "t_big_inventory": 4, "t_trees": 2, "t_flying_machine": 2, "t_swords": 4, "t_armor": 4, "t_crafting": 4, "t_slime_observer": 2, "t_ore_drops": 2, "t_ladders": 4, "t_doors": 4, "t_durability": 4, "t_slabs": 4, "t_trapdoors": 4, "t_redstone_ore": 2, "t_enchanting": 2, "t_tool_wear_kept": 4, "t_repeaters": 2, "t_jukebox": 4, "t_honey": 4, "t_water": 2, "t_comparators": 4, "t_unstuck": 4}

TESTS = [t_store_names, t_nodes_air, t_integrity, t_crafting, t_ore_blocks, t_ore_drops, t_stairs, t_slabs, t_trapdoors, t_repeaters, t_comparators, t_creeper, t_unstuck, t_jukebox, t_honey, t_water, t_enchanting, t_ladders, t_doors, t_panes, t_durability, t_redstone_ore, t_throw_one, t_totem, t_armor, t_big_inventory, t_pick_block, t_auto_pickup, t_swords, t_trees, t_craft_lock, t_screen_clicks, t_chest, t_tool_wear_kept, t_slime_observer, t_pearl, t_hand_place, t_pillar, t_creative, t_furnace_ui, t_flying_machine, t_fire, t_outside_dig, t_blocks_and_holes, t_sand, t_piston, t_tnt, t_inside, t_inside_outside_switch, t_bedrock]
ORBIT_TESTS = [t_ship_loot]  # (run in orbit, once, before the first landing)

def keybind_overrides():
    """keybinds changed from the defaults (InputUtils' global and local files): tests press the default keys"""
    import glob
    found = []
    for f in glob.glob(os.path.expandvars(r"%USERPROFILE%\AppData\LocalLow\ZeekerssRBLX\Lethal Company\InputUtils\controls\*.json")) +              glob.glob(r"O:\SteamLibrary\steamapps\common\Lethal Company\BepInEx\config\controls\*.json"):
        try:
            if json.load(open(f, encoding="utf-8")).get("overrides"): found.append(os.path.basename(f))
        except Exception: pass
    return found

if __name__ == "__main__":
    args = sys.argv[1:]
    if keybind_overrides(): print("WARNING: keybinds changed from the defaults (tests press the default keys):", keybind_overrides())
    cmd("mobspawns 0")  # (creepers wandering in blow up other tests' builds; t_creeper spawns its own)
    if "--speed" in args:
        i = args.index("--speed"); del args[i:i + 2]
    only = None
    if "-t" in args:
        i = args.index("-t"); only = args[i + 1].split(","); del args[i:i + 2]
        TESTS = [t for t in TESTS if t.__name__ in only]
        ORBIT_TESTS = [t for t in ORBIT_TESTS if t.__name__ in only]
    moons = [int(x) for x in args] or [0]
    T0 = time.perf_counter()
    cmd("god 1"); cmd("photolight 0")
    cmd("gamemode survival"); cmd("shipcarry 1")  # tests expect survival (a manual session may have left creative on)
    # (tests that need orbit: before the first landing)
    if "inShipPhase=True" in cmd("state"):
        for t in ORBIT_TESTS: t()
    if only and not args:
        # quick rerun on the current moon
        for t in TESTS: t()
        failed = [r for r in results if not r[1]]
        print(f"{len(results) - len(failed)}/{len(results)} checks passed")
        sys.exit(len(failed))
    for idx in moons:
        since = log_len()
        name = cmd("levels").split(",")[idx].split("=")[1]
        print(f"== {name}")
        with timing.section(f"{name}: landing"):
            landed = land(idx)
            # (the day clock stops: a slow run, or a fast one where every command costs game time, outlasts a day and
            # the ship leaves at midnight)
            if landed: cmd("dayfreeze 1"); cmd("hunger 20 5"); time.sleep(6)  # (fed: a long run would otherwise starve the player)
        if not check(f"lands on {name}", landed): continue
        for t in ([t_company] if "Gordion" in name else TESTS):
            cmd("clearenemies 80")  # tests aren't about enemy AI; one latched onto the (god-mode) player breaks both
            with timing.section(f"{name}: {t.__name__}"):
                full = timing.SPEED
                if MAX_SPEED.get(t.__name__, full) < full: timing.set_speed(MAX_SPEED[t.__name__])
                try: t()
                except Exception as e: check(t.__name__ + " ran", False, repr(e))
                finally:
                    if timing.SPEED != full: timing.set_speed(full)
        lines = new_log(since).splitlines()
        # vanilla bugs that aren't ours (their stack trace is on the following lines)
        vanilla = ("SpikeRoofTrap", "SpawnStateException", "StormyWeather", "RuntimeNavMeshBuilder", "eliminated all possible nodes",
                   "BushWolfEnemy",  # (the fox breaks when it mauls the test player, who is in god mode)
                   "GiantKiwiAI", "PlayAudioAnimationEvent",  # (vanilla: no mod code in their stacks; seen 2026-10-08)
                   "CalculatePolygonPath", "Agent not on nav mesh")  # (stranded monsters: under investigation, issue #25)
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
        # monsters knocked off the navmesh (seen twice, both while tests teleported the god-mode player among them;
        # not reproduced by digging under a monster): reported, not failed
        offnav = [e for e in errs if "not on nav mesh" in e]
        if offnav: print(f"      note: {len(offnav)} 'agent not on nav mesh' errors ({offnav[0][:90]})")
        errs = [e for e in errs if "not on nav mesh" not in e]
        check("no mod errors in the log", not errs, errs[0][:200] if errs else "")
    cmd("dayfreeze 0")
    timing.report(time.perf_counter() - T0)
    failed = [r for r in results if not r[1]]
    print(f"\n{len(results) - len(failed)}/{len(results)} checks passed")
    for n, _, d in failed: print(f"  FAILED: {n}  {d}")
    sys.exit(len(failed))
