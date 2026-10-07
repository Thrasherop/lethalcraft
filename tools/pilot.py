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

def go_to(x, z, stop=0.8, max_steps=30, sprint=False):
    last = None
    for _ in range(max_steps):
        px, py, pz = pos()
        d = math.hypot(x - px, z - pz)
        if d < stop: return d
        if last is not None and abs(last - d) < 0.05:  # stuck: hop
            walk(0.3, "W+Space")
        last = d
        face(x, z, tol=6)
        walk(min(0.8, max(0.15, d / 4.5)), "W+LeftShift" if sprint and d > 4 else "W")
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
    press(f"Digit{i + 1}"); return True

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

def pick_up_all(x, z, tries=10):
    face(x, z); set_pitch(60)
    for _ in range(tries):
        press("E"); time.sleep(0.9)
