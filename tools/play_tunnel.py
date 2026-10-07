"""Play session: enter the facility by hand, walk to a wall, mine a 2-high tunnel 4 blocks long with the real mouse,
light it, photograph it, then check it with ghostcheck/gapcheck. usage: py tools/play_tunnel.py <moonIdx> [mineshaft]"""
import sys, os, time, re, math
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from pilot import *
S = R.S
moon = int(sys.argv[1]) if len(sys.argv) > 1 else 2
want_mineshaft = "mineshaft" in sys.argv
for i in range(6):
    print(R.land(moon)); time.sleep(4)
    if not want_mineshaft or "TileBlocker" in cmd("objinfo ~TileBlocker 1"): break
cmd("god 1"); cmd("dayfreeze 1"); cmd("clearinv")
cmd("tpmain"); time.sleep(1.2)
for t in range(8):
    if "enter" in cmd("hover").lower(): break
    turn(45)
press("E", 1.8); time.sleep(2.5)
print("inside:", state()[5][:60])
cmd("give pickaxe 1"); cmd("give torch 6"); time.sleep(1.5)
for name in ("Diamond Pickaxe", "Torch"):
    m = re.search(name + r"@([-\d.]+),([-\d.]+),([-\d.]+) d=[\d.]+ held=False", cmd("objs"))
    if m: pick_up_all(float(m.group(1)), float(m.group(3)), tries=2, y=float(m.group(2)))
print(slots())
# walk (along the navmesh) to the nearest inside AI node that has a wall next to it, away from the entrance
w = None
here = pos()
cands = []
for i in range(0, 60):
    q = cmd(f"nodepos {i}")
    m = re.match(r"([-\d.]+),([-\d.]+),([-\d.]+)", q)
    if m: cands.append(tuple(map(float, m.groups())))
cands = sorted(set(cands), key=lambda q: math.dist(q, here))
for q in cands:
    if math.dist(q, here) < 8: continue
    print("walking to node", q, travel(*q, sprint=False))
    fc = R.feet_cell()
    if "entrance" in cmd("cellinfo 0 2 0") or "door" in cmd("cellinfo 0 1 0"): continue
    w = R.find_wall(fc)
    if w: break
print("wall:", w)
if w:
    k, dx, dz, yaw = w
    x, y, z, cyaw, *_ = state(); turn((yaw - cyaw + 540) % 360 - 180)
    select("Diamond Pickaxe")
    for step in range(k + 4):
        set_pitch(8); mine(0.9)
        set_pitch(-30); mine(0.9)
        set_pitch(0); walk(0.45)
    select("Torch"); turn(90); set_pitch(10); use(); turn(-90)
    set_pitch(0); shot("tunnel_" + str(moon) + "_fwd")
    turn(180); shot("tunnel_" + str(moon) + "_back")
print("ghost:", cmd("ghostcheck")[:200]); print("gaps:", cmd("gapcheck")[:300])
