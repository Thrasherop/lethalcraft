"""#238: put a lectern down and a book on it, by hand (right-click, then [E] with the book), and look at it from the front
and the side. usage: py tools/lectern_play.py <shot-prefix>"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from regress import cmd, pos, S
tag = sys.argv[1] if len(sys.argv) > 1 else "lectern"
if "inShipPhase=True" in cmd("state"): print("land:", R.land(0)); time.sleep(2)
cmd("craftui close"); cmd("god 1"); cmd("gamemode survival"); cmd("clearenemies 50 all"); cmd("dayfreeze 1"); cmd("daytime 0.4")
fc = R.start_flat(6, [(dx, dz) for dx in (-2, -1, 0, 1, 2) for dz in range(0, 5)])
cmd("clearinv"); cmd("invgive lectern 1"); cmd("invgive writable_book 1"); time.sleep(2.0)
def select(name):
    sl = re.search(r"slots=\[([^\]]*)\]", cmd("state")).group(1).split(",")
    i = next((i for i, e in enumerate(sl) if e.startswith(name)), None)
    if i is not None: cmd(f"slot {i}"); time.sleep(0.3)
    return i
print("slots:", re.search(r"slots=\[([^\]]*)\]", cmd("state")).group(1))
select("Lectern")
for yaw in (0, 90, 180, 270):
    cmd(f"look {yaw} 50"); time.sleep(0.4)
    if "ok=True" in cmd("place?"): break
print("place?", cmd("place?")[:120]); cmd("rmb"); time.sleep(1.0)
m = re.search(r"lectern\((-?\d+), (-?\d+), (-?\d+)\)y(\d+)", cmd("near 4"))
print("lectern:", m.group(0) if m else cmd("near 4")[:200])
cx, cy, cz, yo = (int(m.group(i)) for i in range(1, 5))
top = ((cx + .5) * S, (cy + 1) * S + yo / 1000 * S, (cz + .5) * S)
select("Book"); R.look_at(top[0], top[1] - 0.2, top[2]); time.sleep(0.4)
print("tip:", cmd("cursortip")[:80])
cmd("keys E 0.1"); time.sleep(1.0)
print("after [E]:", re.search(r"slots=\[([^\]]*)\]", cmd("state")).group(1))
for name, (dx, dz) in (("front", (0, -2.2)), ("side", (2.2, 0)), ("high", (0, -1.4))):
    cmd(f"tp {top[0] + dx * S:.2f} {cy * S + yo / 1000 * S + 0.1:.2f} {top[2] + dz * S:.2f}"); time.sleep(1.2)
    R.look_at(*top); time.sleep(0.4)
    cmd(f"screenshot E:/claude/mods/lmc_lect238/shots/{tag}_{name}.png"); time.sleep(0.6)
print("done")
