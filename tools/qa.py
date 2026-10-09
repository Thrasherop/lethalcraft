"""Detailed dig/build QA scenarios. Each scenario builds a situation, checks state and writes a multi-angle photo sheet.
usage: py tools/qa.py <scenario> [...]   (run while landed; outside scenarios start from flatspot/slopespot)"""
import sys, os, time, math, re
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd
from moontour import sheet, orbit_views, pos, feet_cell, SHOTS, S, log_len, new_log

def c(x):
    r = cmd(x)
    print(f"  > {x}: {r[:160]}")
    return r

def center(fc, d):
    return ((fc[0] + d[0] + 0.5) * S, (fc[1] + d[1] + 0.5) * S, (fc[2] + d[2] + 0.5) * S)

def items_near(p, r=4.0):
    out = []
    for e in cmd("objs").split(" ; "):
        m = re.match(r"\s*(.+?)@([-\d.]+),([-\d.]+),([-\d.]+) d=([\d.]+)", e)
        if not m: continue
        q = tuple(map(float, m.group(2, 3, 4)))
        if math.dist(q, p) < r: out.append((m.group(1), q))
    return out

def views_box(pc, d=4.5):
    """8 views around and above a point (cameras kept above it), + straight down"""
    v = []
    for yaw, pitch in [(0, 35), (90, 35), (180, 35), (270, 35), (45, 60), (135, 50), (225, 45), (315, 65)]:
        r = math.radians(yaw); hd = d * math.cos(math.radians(pitch)); vd = d * math.sin(math.radians(pitch))
        v.append((pc[0] - math.sin(r) * hd, pc[1] + vd, pc[2] - math.cos(r) * hd, yaw, pitch))
    v.append((pc[0], pc[1] + d * 1.2, pc[2] + 0.01, 0, 89))
    return v

def start_flat():
    c("tpship"); time.sleep(1)
    c("flatspot"); time.sleep(1.5)
    c("tprel 0 0 1"); time.sleep(1)
    return feet_cell()

# ---------------------------------------------------------------- outside
def stairs():
    fc = start_flat()
    for k in range(4):
        for x in (-1, 0, 1):
            for y in range(-k, 1):
                cmd(f"digcell {x} {y} {2 + k}")
    print("  stats:", cmd("groundstats"))
    sheet("qa_stairs", views_box(center(fc, (0, 0, 3.5)), 5))

def pillar_pit():
    fc = start_flat()
    for x in (-1, 0, 1):
        for z in (2, 3, 4):
            for y in (0, -1, -2):
                cmd(f"digcell {x} {y} {z}")
    c("placeg cobblestone 0 -2 3"); c("placeg cobblestone 0 -1 3")
    c("placeg oak_planks 1 -2 4")
    time.sleep(0.5)
    # dig the natural wall right next to the pillar's plank neighbour and under the pit floor beside it
    c("digcell 2 -2 4"); c("digcell 1 -3 4"); c("digcell 0 -3 3")
    time.sleep(0.5)
    print("  near:", cmd("near")[:400])
    sheet("qa_pillar", views_box(center(fc, (0, 0, 3))))

def offgrid():
    """a block placed on the raw (uneven) surface, then the ground dug around and under it"""
    fc = start_flat()
    c("place dirt 0 0 2")
    c("place cobblestone 1 0 2")
    time.sleep(0.5)
    print("  near:", cmd("near")[:300])
    c("digcell 0 0 3"); c("digcell 1 0 3"); c("digcell -1 0 2"); c("digcell -1 -1 2"); c("digcell 0 -1 3")
    time.sleep(0.5)
    sheet("qa_offgrid", views_box(center(fc, (0, 0, 2.5)), 3.8))

def sand_fall():
    fc = start_flat()
    for y in (0, -1, -2): c(f"digcell 0 {y} 3")
    c("placeg sand 0 2 3"); c("placeg sand 0 3 3"); c("placeg gravel 0 4 3")
    time.sleep(4)
    print("  near:", cmd("near")[:300])
    sheet("qa_sand", views_box(center(fc, (0, 0, 3)), 3.5))

def items_fall():
    fc = start_flat()
    p = center(fc, (0, 1, 3))
    c("placeg cobblestone 0 0 3"); c("placeg cobblestone 0 1 3")
    time.sleep(0.5)
    c(f"giveat {p[0]:.2f} {p[1] + 1.5:.2f} {p[2]:.2f} torch 4")
    time.sleep(2)
    before = items_near(p, 3)
    c("breakg 0 1 3"); time.sleep(1.5)
    mid = items_near(p, 3)
    c("breakg 0 0 3"); time.sleep(1.5)
    c("digcell 0 -1 3"); time.sleep(1.5)
    c("digcell 0 -2 3"); time.sleep(1.5)
    after = items_near(p, 6)
    print("  before:", before); print("  mid:", mid); print("  after:", after)

def tnt_out():
    fc = start_flat()
    c("placeg tnt 0 0 4")
    c("ignite 20"); time.sleep(4)
    print("  stats:", cmd("groundstats"))
    sheet("qa_tnt", views_box(center(fc, (0, 0, 4)), 6))

def piston():
    fc = start_flat()
    for x in range(-1, 5):
        for y in (0, -1):
            cmd(f"digcell {x} {y} 2")
    # piston at the west end of the trench pushing east: cobblestone, then a gap, then the natural east wall
    c("placeg piston -1 -1 2 5")
    c("placeg cobblestone 0 -1 2")
    c("placeg sticky_piston 3 -1 2 4")   # facing west, toward the cobblestone's landing spot
    time.sleep(0.3)
    c("placeg redstone_block -1 0 2")
    time.sleep(1.5)
    print("  extended:", [e for e in cmd("near").split(" ; ") if "dirt" not in e])
    sheet("qa_piston", views_box(center(fc, (1.5, 0, 2)), 4.0))
    c("breakg -1 0 2"); time.sleep(1.0)
    print("  retracted:", [e for e in cmd("near").split(" ; ") if "dirt" not in e])
    # sticky piston pulls the natural east-wall block? (it faces west; power it)
    c("placeg redstone_block 3 0 2"); time.sleep(1.5)
    print("  sticky on:", [e for e in cmd("near").split(" ; ") if "dirt" not in e])
    c("breakg 3 0 2"); time.sleep(1.5)
    print("  sticky off:", [e for e in cmd("near").split(" ; ") if "dirt" not in e])
    print("  stats:", cmd("groundstats"))

def slope():
    c("tpship"); time.sleep(1)
    r = c("slopespot 0"); time.sleep(1.5)
    fc = feet_cell()
    # tunnel horizontally into the uphill side: find which direction rises
    best = None
    for name, (dx, dz) in {"+x": (1, 0), "-x": (-1, 0), "+z": (0, 1), "-z": (0, -1)}.items():
        info = cmd(f"cellinfo {dx * 2} 1 {dz * 2}")
        if ") Solid" in info or ") Partial" in info:
            best = (dx, dz); break
    if not best: best = (1, 0)
    dx, dz = best
    for k in range(1, 5):
        for y in (0, 1):
            cmd(f"digcell {dx * k} {y} {dz * k}")
    print("  dir", best, "stats:", cmd("groundstats"))
    sheet("qa_slope", views_box(center(fc, (dx * 2, 1, dz * 2)), 5.0))

# ---------------------------------------------------------------- inside
def find_wall():
    for k in range(1, 7):
        for name, (dx, dz, yaw) in {"+x": (1, 0, 90), "-x": (-1, 0, 270), "+z": (0, 1, 0), "-z": (0, -1, 180)}.items():
            info = cmd(f"cellinfo {dx * k} 1 {dz * k}")
            if ") Air" not in info: return k, dx, dz, yaw
    return None

def inside_tunnel(node=4):
    cmd("photolight 500")
    c(f"tpnode {node}"); time.sleep(1.5)
    fc = feet_cell()
    w = find_wall()
    if not w: print("  no wall"); return
    k, dx, dz, yaw = w
    print("  wall at", k, dx, dz)
    for j in range(k, k + 5):
        for y in (0, 1):
            cmd(f"digcell {dx * j} {y} {dz * j}")
    # a side branch and a step up
    j = k + 3
    sx, sz = dz, dx
    for y in (0, 1): cmd(f"digcell {dx * j + sx} {y} {dz * j + sz}")
    cmd(f"digcell {dx * (k + 4)} 2 {dz * (k + 4)}")
    p = pos()
    v = []
    for j in (k - 2, k, k + 2, k + 4):
        x, z = (fc[0] + dx * j + 0.5) * S, (fc[2] + dz * j + 0.5) * S
        v.append((x, p[1] + 1.3, z, yaw, 8))
        v.append((x, p[1] + 1.3, z, (yaw + 180) % 360, 8))
    x, z = (fc[0] + dx * (k + 3) + 0.5) * S, (fc[2] + dz * (k + 3) + 0.5) * S
    v.append((x, p[1] + 2.4, z, yaw, 70))
    print("  stats:", cmd("groundstats"))
    sheet("qa_in_tunnel", v)

def inside_floor_ceiling(node=7):
    cmd("photolight 500")
    c(f"tpnode {node}"); time.sleep(1.5)
    fc = feet_cell()
    for y in (0, -1, -2): c(f"digcell 0 {y} 2")
    for y in (1, 2, 3, 4, 5): cmd(f"digcell 0 {y} -2")
    print("  cells above:", [cmd(f"cellinfo 0 {y} -2")[:60] for y in (3, 4, 5, 6)])
    p = pos()
    v = orbit_views(center(fc, (0, -1, 2)), 3.2)[:4]
    up = center(fc, (0, 2, -2))
    v += [(up[0], p[1] + 1.0, up[2] + 0.01, 0, -80), (up[0], p[1] + 1.4, up[2] + 2.5, 180, -35)]
    sheet("qa_in_floorceil", v)

def inside_tnt(node=10):
    cmd("photolight 500")
    c(f"tpnode {node}"); time.sleep(1.5)
    fc = feet_cell()
    w = find_wall()
    if not w: print("  no wall"); return
    k, dx, dz, yaw = w
    c(f"placeg tnt {dx * (k - 1)} 0 {dz * (k - 1)}")
    c("tprel 0 0 0")
    c(f"tp {(fc[0] - dx * 6 + .5) * S} {pos()[1] + 0.3} {(fc[2] - dz * 6 + .5) * S}")
    c("ignite 20"); time.sleep(4)
    print("  stats:", cmd("groundstats"))
    p = center(fc, (dx * k, 0, dz * k))
    sheet("qa_in_tnt", orbit_views(p, 4.0) + [((fc[0] + .5) * S, p[1] + 0.5, (fc[2] + .5) * S, yaw, 5)])

def walk_tunnel(node=4):
    """dig a 2-high tunnel through the nearest wall, then really walk through it (CharacterController collisions)"""
    c(f"tpnode {node}"); time.sleep(1.5)
    fc = feet_cell()
    w = find_wall()
    if not w: print("  no wall"); return
    k, dx, dz, yaw = w
    for j in range(1, k + 6):
        for y in (0, 1):
            cmd(f"digcell {dx * j} {y} {dz * j}")
    # stand in the middle of the feet cell, face down the tunnel, walk
    c(f"tp {(fc[0] + .5) * S:.2f} {pos()[1] + 0.1:.2f} {(fc[2] + .5) * S:.2f}"); time.sleep(0.8)
    c(f"look {yaw} 0"); time.sleep(0.3)
    p0 = pos()
    c("walk 1 0 2.5 3"); time.sleep(3.0)
    p1 = pos()
    moved = math.dist((p0[0], p0[2]), (p1[0], p1[2]))
    print(f"  walked {moved:.2f} m (tunnel {k + 5} cells = {(k + 5) * S:.1f} m), dy={p1[1] - p0[1]:.2f}, inside={cmd('facility').split('inside=')[1][:5]}")
    c(f"look {(yaw + 180) % 360} 0"); time.sleep(0.3)
    c("walk 1 0 2.5 3"); time.sleep(3.0)
    p2 = pos()
    print(f"  walked back {math.dist((p1[0], p1[2]), (p2[0], p2[2])):.2f} m")

if __name__ == "__main__":
    since = log_len()
    cmd("god 1")
    for name in sys.argv[1:]:
        print("==", name)
        try: globals()[name]()
        except Exception as e: print("  CRASH", e)
    errs = [l for l in new_log(since).splitlines() if "Exception" in l or "[Error  :LethalCraft]" in l]
    print("errors:", len(errs), *errs[:5], sep="\n  ")
