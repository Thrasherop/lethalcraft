"""Repro/check: standing on blocks attached to the ship outside its own bounds when it takes off (user report:
the blocks leave with the ship and the player stays behind). Builds a stone walkway out of the hangar door by
hand (real clicks, from inside), walks to its outer end (past the ship's bounds), drops a stack there, then the
ship leaves. usage: py tools/ship_ride.py"""
import sys, os, time, re, math
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from pilot import *
S = R.S
def ship(): return cmd("ship")
if "inShipPhase=True" in cmd("state"): print("land:", R.land(int(sys.argv[1]) if len(sys.argv) > 1 else 0)); time.sleep(4)
print(cmd("shipcarry " + ("0" if "off" in sys.argv else "1")))
cmd("god 1"); cmd("clearenemies 80"); cmd("clearinv")
if "gm=creative" not in cmd("state"): cmd("gamemode creative")
cmd("invgive stone 16"); time.sleep(2.0)
cmd("tpship"); time.sleep(1.0)
# a walkway at the ship's floor level out of the hangar door, past the edge of the ship's bounds (x -9.1): moon
# blocks touching the ship, so they join it at takeoff (dev-placed; how they got there isn't what this is about)
for cx in (-6, -7, -8, -9):
    cmd(f"placeabs stone {cx} -1 -10")
time.sleep(1.0)
cmd("tpship"); time.sleep(1.0)
go_to(-4.3, -14.0, stop=0.3)
go_to(-10.5, -13.3, stop=0.2)   # walk out along it, to the middle of a block past the ship's box (x -9.1)
time.sleep(0.8)
print("standing at the outer end:", ship())
x, y, z = pos()
cmd(f"giveat {x - 0.4:.2f} {y + 1.5:.2f} {z + 0.3:.2f} bread 3"); time.sleep(1.5)
p0 = pos()
el0 = tuple(map(float, re.search(r"elevator=([-\d.]+),([-\d.]+),([-\d.]+)", ship()).groups()))
cmd("leave")
for t in range(14):
    time.sleep(1.0)
    s = ship()
    el = tuple(map(float, re.search(r"elevator=([-\d.]+),([-\d.]+),([-\d.]+)", s).groups()))
    pl = pos()
    m = re.findall(r"Breadx3@([-\d.]+),([-\d.]+),([-\d.]+)", cmd("find bread"))
    ie = re.search(r"inElevator=\w+", s).group(0)
    print(f"t={t + 1}s ship rose {el[1] - el0[1]:+.1f} m | player rose {pl[1] - p0[1]:+.1f} m ({ie}) | stack y {m[-1][1] if m else None}")

# afterwards (in orbit): the walkway joined the ship; take it back out so the next run starts clean
time.sleep(4.0)
for e in cmd("near 30").split(" ; "):
    m = re.match(r"\s*stone\((-?\d+), (-?\d+), (-5)\)y(\d+)", e)
    if m and int(m.group(1)) <= -6: cmd(f"rmkey 1 {m.group(4)} {m.group(1)} {m.group(2)} {m.group(3)}")
print("cleanup -> ship blocks:", re.search(r"ship=(\d+)", cmd("state")).group(1), "| player:", cmd("ship").split("player ")[1])
