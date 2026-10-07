"""Play the game with real input (keyboard/mouse through the game's input system), no teleporting.
Import it from a session script:  from pilot import *
   face(x, z)          turn with the mouse until facing a world point
   turn(deg) / pitch(deg)
   walk(sec, keys="W") hold keys (e.g. "W+LeftShift" to sprint, "A", "Space" to jump)
   go_to(x, z)         walk there (re-aims every step), returns distance left
   mine(sec)           hold left mouse; use(sec) hold right mouse; press(key)
   shot(name)          screenshot -> shots/pilot_<name>.png
"""
import sys, os, time, math, re
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd

SHOTS = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "shots"))
PX_PER_DEG = 12.5

def state():
    s = cmd("state")
    m = re.search(r"pos=([-\d.]+),([-\d.]+),([-\d.]+) yaw=([-\d.]+) pitch=([-\d.]+)", s)
    x, y, z, yaw, pitch = map(float, m.groups())
    return x, y, z, yaw, pitch, s

def pos():
    x, y, z, *_ = state(); return x, y, z

def turn(deg, frames=12):
    cmd(f"mouse look {deg * PX_PER_DEG:.1f} 0 {frames}"); time.sleep(frames / 50 + 0.25)

def pitch(deg, frames=10):
    """positive = look down"""
    cmd(f"mouse look 0 {-deg * PX_PER_DEG:.1f} {frames}"); time.sleep(frames / 50 + 0.25)

def set_pitch(target):
    for _ in range(3):
        p = state()[4]
        if abs(p - target) < 2: return
        pitch(target - p)

def face(x, z, tol=3):
    for _ in range(4):
        px, py, pz, yaw, *_ = state()
        want = math.degrees(math.atan2(x - px, z - pz)) % 360
        d = (want - yaw + 540) % 360 - 180
        if abs(d) < tol: return
        turn(d)

def walk(sec, keys="W"):
    cmd(f"keys {keys} {sec}"); time.sleep(sec + 0.15)

def press(key, sec=0.08):
    cmd(f"keys {key} {sec}"); time.sleep(sec + 0.2)

def go_to(x, z, stop=0.8, max_time=25.0, sprint=False):
    """walk to a point like a player: keep W held and steer with the mouse while moving; hop when stuck"""
    t0 = time.time(); last_check = (time.time(), pos()); keys = "W+LeftShift" if sprint else "W"
    face(x, z, tol=10)
    while time.time() - t0 < max_time:
        px, py, pz, yaw, *_ = state()
        d = math.hypot(x - px, z - pz)
        if d < stop: break
        want = math.degrees(math.atan2(x - px, z - pz)) % 360
        err = (want - yaw + 540) % 360 - 180
        if abs(err) > 60:
            cmd("keys W 0.01"); face(x, z, tol=8); continue
        cmd(f"mouse look {err * PX_PER_DEG * 0.8:.1f} 0 4")
        cmd(f"keys {keys if d > 4 else 'W'} 0.3")
        time.sleep(0.2)
        if time.time() - last_check[0] > 1.2:
            moved = math.dist(pos(), last_check[1])
            if moved < 0.3: cmd("keys W+Space 0.4"); time.sleep(0.4)
            last_check = (time.time(), pos())
    cmd("keys W 0.01")
    px, py, pz = pos()
    return math.hypot(x - px, z - pz)

def mine(sec):
    cmd(f"mouse left {sec}"); time.sleep(sec + 0.3)

def use(sec=0.1):
    cmd(f"mouse right {sec}"); time.sleep(sec + 0.3)

def term(text):
    r = cmd("termtype " + text); time.sleep(0.8); return r

def type_line(text):
    cmd("type " + text.replace(" ", "_")); time.sleep(0.05 * len(text) + 0.4)
    press("Enter")

def shot(name, wait=0.3):
    time.sleep(wait)
    path = os.path.join(SHOTS, f"pilot_{name}.png")
    if os.path.exists(path): os.remove(path)
    cmd(f"screenshot {path}")
    for _ in range(40):
        if os.path.exists(path) and os.path.getsize(path) > 0: break
        time.sleep(0.1)
    time.sleep(0.2)
    return path

def slots():
    return re.search(r"slots=\[([^\]]*)\]", cmd("state")).group(1).split(",")

def select(name):
    """hotbar slot by item name prefix, with the number keys"""
    sl = slots()
    i = next((i for i, e in enumerate(sl) if e.startswith(name)), None)
    if i is None: return False
    for _ in range(4):  # the game ignores switches during a pick-up animation: retry
        press(f"Digit{i + 1}"); time.sleep(0.15)
        if re.search(r"slot=(\d+)", cmd("state")).group(1) == str(i): return True
        time.sleep(0.4)
    return False

def click_slot(screen, area, index, right=False, shift=False):
    """real mouse click on a slot of an open screen (screen = 'craftui' or 'chestui')"""
    p = cmd(f"{screen} pos {area} {index}")
    if not re.match(r"-?\d+ -?\d+", p): return p
    x, y = p.split()
    cmd(f"mouse moveto {x} {y}"); time.sleep(0.12)
    if shift: cmd("keys LeftShift 0.5"); time.sleep(0.08)
    cmd(f"mouse {'right' if right else 'left'} 0.08"); time.sleep(0.35)
    return cmd(f"{screen} state")

def walk_out_of_ship():
    """from the spawn point, out of the hangar door (on Experimentation you drop down to the ground)"""
    for target in ((1.5, -14.3), (-2.5, -14.0), (-6.5, -14.0), (-10.0, -14.0)):
        go_to(*target, stop=0.7)

def pick_up_all(x, z, tries=10, y=None):
    """walk within reach, look at the spot and press E until nothing more comes (items pile up there)"""
    px, py, pz = pos()
    if math.hypot(x - px, z - pz) > 1.6:
        d = math.hypot(x - px, z - pz)
        go_to(x - (x - px) / d * 1.2, z - (z - pz) / d * 1.2, stop=0.3)
    for _ in range(tries):
        if y is not None: aim_at(x, y + 0.15, z)
        else: face(x, z); set_pitch(60)
        before = slots(); press("E"); time.sleep(0.9)
        if slots() == before and y is not None:
            # nothing picked: re-aim at whatever is still lying there
            # re-aim at the nearest item still lying within reach
            near = [tuple(map(float, mm.groups())) for mm in re.finditer(r"@([-\d.]+),([-\d.]+),([-\d.]+) d=[\d.]+ held=False", cmd("objs"))]
            px, py, pz = pos()
            near = [q for q in near if math.hypot(q[0] - px, q[2] - pz) < 2.5]
            if not near: break
            x, y, z = near[0]

def travel(x, y, z, sprint=True):
    """walk a navmesh route to a point (real input; the route only says where to turn)"""
    r = cmd(f"navpath {x} {y} {z}")
    if not r.startswith(("PathComplete", "PathPartial")): return r
    corners = [tuple(map(float, c.split(","))) for c in r.split()[1:]]
    for cx, cy, cz in corners[1:]:
        go_to(cx, cz, stop=1.0, sprint=sprint)
    px, py, pz = pos()
    return f"{r.split()[0]} {len(corners)} corners, {math.hypot(x - px, z - pz):.1f} m left"

def camera():
    m = re.match(r"([-\d.]+),([-\d.]+),([-\d.]+)", cmd("camera"))
    return tuple(map(float, m.groups())) if m else None

def aim_at(x, y, z, eye=1.75):
    """turn and pitch to look at a world point (from the real camera position when the game reports it)"""
    for _ in range(2):
        face(x, z, tol=1.5)
        c = camera()
        if c is None:
            px, py, pz, *_ = state(); c = (px, py + eye, pz)
        d = math.hypot(x - c[0], z - c[2])
        set_pitch(-math.degrees(math.atan2(y - c[1], max(d, 0.1))))

def use_terminal():
    """walk up to the ship's terminal, sweep the view until its 'Access terminal' prompt shows, press E"""
    go_to(5.87, -15.6, stop=0.3)
    for yt in range(105, 185, 8):
        x, y, z, yaw, *_ = state(); turn((yt - yaw + 540) % 360 - 180, frames=6)
        for pt in (5, -5, 15):
            set_pitch(pt)
            if "terminal" in cmd("hover").lower():
                press("E"); time.sleep(1.2)
                if "terminal=True" in cmd("flags"): return True
    return False
