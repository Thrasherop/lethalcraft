"""Visit moons, dig outside (pit + shaft) and inside (through a wall + into the floor), photograph, collect stats and errors."""
import sys, os, time, math, re
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd
from PIL import Image

LOG = r"O:\SteamLibrary\steamapps\common\Lethal Company\BepInEx\LogOutput.log"
S = 1.4
SHOTS = r"E:\claude\mods\lethal_minecraft\shots"

def log_len():
    try: return os.path.getsize(LOG)
    except: return 0

def new_log(since):
    with open(LOG, "rb") as f:
        f.seek(since)
        return f.read().decode("utf-8", "replace")

def state():
    return cmd("state")

def wait(pred, timeout=90, step=2):
    t = time.time()
    while time.time() - t < timeout:
        try:
            if pred(): return True
        except Exception: pass
        time.sleep(step)
    return False

def pos():
    for _ in range(5):
        m = re.search(r"pos=([-\d.]+),([-\d.]+),([-\d.]+)", state())
        if m: return tuple(map(float, m.groups()))
        time.sleep(1.0)  # the game may be busy for a moment
    raise RuntimeError("no player position")

def feet_cell():
    """the player's cell on the natural ground grid (the game shifts that grid vertically per level)"""
    m = re.match(r"\((-?\d+), (-?\d+), (-?\d+)\)", cmd("cellinfo 0 0 0"))
    if m: return [int(m.group(1)), int(m.group(2)), int(m.group(3))]
    p = pos()
    return [math.floor(p[0] / S), math.floor((p[1] + 0.2) / S), math.floor(p[2] / S)]

def cell_center(c):
    return ((c[0] + 0.5) * S, (c[1] + 0.5) * S, (c[2] + 0.5) * S)

def shot(path, x, y, z, yaw, pitch):
    if os.path.exists(path): os.remove(path)
    cmd(f"photo {path} {x:.2f} {y:.2f} {z:.2f} {yaw} {pitch}")
    for _ in range(80):
        time.sleep(0.05)
        if os.path.exists(path) and os.path.getsize(path) > 0: time.sleep(0.15); break
    return Image.open(path).resize((480, 270))

def sheet(name, views):
    """views: list of (x,y,z,yaw,pitch)"""
    tiles = [shot(os.path.join(SHOTS, f"_t{i}.png"), *v) for i, v in enumerate(views)]
    cols = 3
    rows = (len(tiles) + cols - 1) // cols
    img = Image.new("RGB", (480 * cols, 270 * rows))
    for i, t in enumerate(tiles): img.paste(t, ((i % cols) * 480, (i // cols) * 270))
    img.save(os.path.join(SHOTS, name + ".png"))

def orbit_views(c, dist=3.6):
    out = []
    for yaw, pitch in [(0, 20), (90, 20), (180, 20), (270, 20), (45, 62), (225, 35)]:
        r = math.radians(yaw); hd = dist * math.cos(math.radians(pitch)); vd = dist * math.sin(math.radians(pitch))
        out.append((c[0] - math.sin(r) * hd, c[1] + vd, c[2] - math.cos(r) * hd, yaw, pitch))
    return out

DIRS = {"+x": (1, 0, 90), "-x": (-1, 0, 270), "+z": (0, 1, 0), "-z": (0, -1, 180)}

def kind(info):
    m = re.search(r"\) (Air|Solid|Partial)", info)
    return m.group(1) if m else "?"

def outside(out):
    f = cmd("flatspot")
    if not f.startswith("ok"):
        out.append("   outside: no flat open spot: " + f); return
    time.sleep(1.2)
    cmd("tprel 0 0 1"); time.sleep(0.8)
    fc = feet_cell()
    res = []
    # 2x2 pit, 3 deep, two cells ahead
    for dy in (0, -1, -2):
        for dx, dz in ((0, 2), (1, 2), (0, 3), (1, 3)):
            res.append(cmd(f"digcell {dx} {dy} {dz}").split(" (")[0])
    # a shaft 12 deep in one corner
    c = (fc[0], fc[1], fc[2] + 2)
    t0 = time.time()
    shaft = cmd(f"digcol {c[0]} {c[2]} {c[1] - 3} {c[1] - 14}")
    bed = sum(1 for r in res if "bedrock" in r)
    out.append(f"   outside: pit {len(res) - bed}/{len(res)} dug ({bed} bedrock) | shaft: {shaft}")
    out.append("   outside stats: " + cmd("groundstats"))
    pc = cell_center((fc[0] + 1, fc[1] - 1, fc[2] + 3))
    pc = (pc[0] - 0.7, pc[1], pc[2] - 0.7)
    sheet(f"moon_out", orbit_views(pc, 4.2))

def inside(out, idx):
    t = cmd("tpnode 4")
    if not t.startswith("ok"):
        out.append("   inside: no inside nodes: " + t); return
    time.sleep(1.5)
    fc = feet_cell()
    # find the nearest wall at head height
    best = None
    for name, (dx, dz, yaw) in DIRS.items():
        for k in range(1, 7):
            info = cmd(f"cellinfo {dx * k} 1 {dz * k}")
            if kind(info) != "Air":
                if best is None or k < best[1]: best = (name, k, dx, dz, yaw)
                break
    views = []
    if best:
        name, k, dx, dz, yaw = best
        res = []
        for j in range(k, k + 4):
            for dy in (0, 1):
                res.append(cmd(f"digcell {dx * j} {dy} {dz * j}").split(" (")[0])
        bed = sum(1 for r in res if "bedrock" in r)
        out.append(f"   inside wall {name} at {k}: " + ", ".join(r.split(" ")[0] + (" " + r.split(" ")[1] if r.startswith(("dug", "broke")) else "") for r in res))
        p = pos()
        # stand back in the room, look into the tunnel
        back = max(0, k - 3)
        cx, cz = (fc[0] + dx * back + 0.5) * S, (fc[2] + dz * back + 0.5) * S
        views.append((cx, p[1] + 1.4, cz, yaw, 5))
        cx2, cz2 = (fc[0] + dx * (k + 1) + 0.5) * S, (fc[2] + dz * (k + 1) + 0.5) * S
        views.append((cx2, p[1] + 1.5, cz2, (yaw + 180) % 360, 5))  # from inside the tunnel looking back
        views.append((cx2, p[1] + 1.5, cz2, yaw, 0))
    else:
        out.append("   inside: no wall within 6 cells")
    # floor pit
    res = [cmd(f"digcell 0 {dy} 1").split(" (")[0] for dy in (0, -1, -2)]
    out.append("   inside floor: " + ", ".join(res))
    c = cell_center((fc[0], fc[1] - 1, fc[2] + 1))
    views += orbit_views(c, 3.0)[:3]
    sheet(f"moon_in", views)

def visit(idx, report):
    since = log_len()
    name = cmd("levels").split(",")[idx].split("=")[1]
    head = f"== [{idx}] {name}"
    cmd("deadline")
    cmd(f"route {idx}"); time.sleep(6)
    cmd("land")
    landed = wait(lambda: "landed=True" in cmd("leave_check"), 120)
    if not landed:
        report.append(head + ": did not land"); return
    time.sleep(8)
    out = []
    try: outside(out)
    except Exception as e: out.append(f"   outside crashed: {e}")
    try:
        os.replace(os.path.join(SHOTS, "moon_out.png"), os.path.join(SHOTS, f"moon{idx}_out.png"))
    except Exception: pass
    if name.lower() != "gordion":
        try: inside(out, idx)
        except Exception as e: out.append(f"   inside crashed: {e}")
        try: os.replace(os.path.join(SHOTS, "moon_in.png"), os.path.join(SHOTS, f"moon{idx}_in.png"))
        except Exception: pass
    out.append("   final: " + cmd("groundstats") + " | " + cmd("fps"))
    lg = new_log(since)
    prepared = re.findall(r"Prepared diggable '([^']+)' \(([^)]*)\) in (\d+) ms", lg)
    errs = [l for l in lg.splitlines() if ("Exception" in l or "[Error  :LethalCraft]" in l)]
    report.append(head)
    report.append("   prepared: " + "; ".join(f"{n} ({d}, {ms} ms)" for n, d, ms in prepared))
    report += out
    report.append(f"   errors: {len(errs)}" + ("" if not errs else " e.g. " + errs[0][:200]))
    cmd("tpship"); time.sleep(1)
    cmd("leave")
    wait(lambda: "inShipPhase=True" in state(), 120)
    time.sleep(3)

if __name__ == "__main__":
    cmd("god 1"); cmd("photolight 500")
    ids = [int(x) for x in sys.argv[1:]] if len(sys.argv) > 1 else list(range(13))
    report = []
    for i in ids:
        n0 = len(report)
        try: visit(i, report)
        except Exception as e: report.append(f"== [{i}] crashed: {e}")
        print("\n".join(report[n0:]), flush=True)
        open(os.path.join(SHOTS, "moontour.txt"), "w").write("\n".join(report))
