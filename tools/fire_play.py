"""Play check for fire (real input): light it, look at it, stand in it, burn wood, light TNT, punch it out,
creative is immune. usage: py tools/fire_play.py [flatIdx]"""
import sys, os, time, re, math
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from pilot import *
S = R.S
idx = int(sys.argv[1]) if len(sys.argv) > 1 else 9
def fires(r=12): return [e.strip() for e in cmd(f"near {r}").split(" ; ") if e.strip().startswith("fire(")]
def hp(): return int(re.search(r"hp=(\d+)", cmd("state")).group(1))
def strike(): use(); time.sleep(0.5)
cmd("gamemode survival"); cmd("god 0"); cmd("clearenemies 80"); cmd("clearinv")
cmd("invgive flint_and_steel 1"); time.sleep(2.0)
fc = R.start_flat(idx, [(0, 2), (1, 2), (0, 3), (1, 3), (2, 2), (2, 3)])
print("spot:", fc)
select("Flint and Steel")
# 1. light the ground two blocks ahead (aimed at the actual surface there) and look at it
g = R.surface(fc, 0, 2)
aim_at((fc[0] + .5) * S, (g + 1) * S + 0.15, (fc[2] + 2.5) * S); time.sleep(0.2)
print("   fire?:", cmd("fire?")[:120]); strike()
print("1. lit:", fires())
cmd("look 0 25"); time.sleep(0.3); shot("fire_ground")
# 2. walk into it and stand there
h0 = hp(); f = fires()
if f:
    m = re.match(r"fire\((-?\d+), (-?\d+), (-?\d+)\)", f[0]); cx, cz = int(m.group(1)), int(m.group(3))
    go_to((cx + .5) * S, (cz + .5) * S, stop=0.25); time.sleep(1.5)
    print(f"2. standing in fire 1.5 s: hp {h0} -> {hp()}")
    go_to((cx + .5) * S, (cz - 2.5) * S, stop=0.4)
# 3. a little wooden hut: planks around a log, then fire at its foot
sy = R.surface(fc, 2, 3)
for dx, dy, dz, k in ((2, 1, 3, "oak_planks"), (3, 1, 3, "oak_planks"), (2, 2, 3, "oak_planks"), (3, 2, 3, "oak_planks"), (2, 1, 4, "oak_log"), (3, 1, 4, "leaves")):
    cmd(f"placeabs {k} {fc[0] + dx} {sy + dy} {fc[2] + dz}")
time.sleep(0.5)
def wood(): return len([k for k, v in R.near_blocks((fc[0] + 2, sy + 1, fc[2] + 3)).items() if v[0] in ("oak_planks", "oak_log", "leaves")])
w0 = wood()
go_to((fc[0] + 2.5) * S, (fc[2] + 0.5) * S, stop=0.3)
cmd("look 0 30"); time.sleep(0.3)
aim_at((fc[0] + 2.5) * S, (sy + 1.02) * S + 0.2, (fc[2] + 2.6) * S); strike()   # the ground just in front of the planks
print("3. wood blocks", w0, "| fires right after lighting:", len(fires()))
items0 = len(re.findall(r"(Oak Planks|Oak Log|Leaves)@", cmd("objs")))
for t in range(8):
    time.sleep(2.0)
    print(f"   t={2 * (t + 1)}s wood {wood()} fires {len(fires(16))}")
    if t == 1: shot("fire_wood")
print("   wood items dropped:", len(re.findall(r"(Oak Planks|Oak Log|Leaves)@", cmd("objs"))) - items0)
# 4. TNT next to a fire lights
tx = fc[0] - 2
tsy = R.surface(fc, -2, 2)
cmd(f"placeabs tnt {tx} {tsy + 1} {fc[2] + 2}"); time.sleep(0.3)
go_to((tx + .5) * S, (fc[2] - 0.5) * S, stop=0.3)
tg = R.surface(fc, -1, 2)
aim_at((tx + 1 + .5) * S, (tg + 1) * S + 0.15, (fc[2] + 2 + .5) * S); time.sleep(0.2)   # the ground beside the TNT
print("   fire?:", cmd("fire?")[:120]); strike()
time.sleep(1.5)
tnt = [e for e in cmd("near 12").split(" ; ") if e.strip().startswith(f"tnt({tx}")]
print("4. TNT beside a fire:", tnt, "(s1 = lit)")
go_to((tx + .5) * S, (fc[2] - 6) * S, stop=0.5); time.sleep(4.5)
# 5. punch a fire out
cmd("look 0 45"); time.sleep(0.3); strike()
n1 = len(fires(6)); f = fires(6)
if f:
    m = re.match(r"fire\((-?\d+), (-?\d+), (-?\d+)\)y(\d+)", f[0])
    w = [e for e in cmd("where fire 10").split(" ; ") if e.startswith(f"({m.group(1)}, {m.group(2)}, {m.group(3)})")]
    if w:
        wx, wy, wz = map(float, w[0].split("@")[1].split(","))
        aim_at(wx, wy, wz); cmd("mouse left 0.08"); time.sleep(0.5)
print(f"5. punched: fires nearby {n1} -> {len(fires(6))}")
# 6. creative doesn't burn
cmd("gamemode creative"); time.sleep(0.3)
cmd("look 0 40"); time.sleep(0.3); strike()
f = fires(6)
if f:
    m = re.match(r"fire\((-?\d+), (-?\d+), (-?\d+)\)", f[0]); cx, cz = int(m.group(1)), int(m.group(3))
    h0 = hp(); go_to((cx + .5) * S, (cz + .5) * S, stop=0.25); time.sleep(1.5)
    print(f"6. creative in fire 1.5 s: hp {h0} -> {hp()}")
cmd("gamemode survival"); cmd("god 1")
