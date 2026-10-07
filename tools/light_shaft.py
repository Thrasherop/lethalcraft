"""Light test bed: a 1x1 shaft 4 blocks deep with a torch on its wall; screenshots for light settings.
usage: py tools/light_shaft.py [setup] | <radius> <nearFloor> <scale> <name>"""
import sys, os, time
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from pilot import *
S = R.S
if sys.argv[1:2] == ["setup"]:
    if "inShipPhase=True" in R.state(): print(R.land(0)); time.sleep(4)
    cmd("god 1"); cmd("clearinv")
    fc = R.start_flat(18, [(0, 2)])
    sy = R.surface(fc, 0, 2)
    for y in range(sy, sy - 5, -1): R.dig(fc, 0, y, 2)
    cmd(f"tp {(fc[0] + .5) * S:.2f} {(sy - 4) * S + 0.6:.2f} {(fc[2] + 2.5) * S:.2f}"); time.sleep(1.5)
    print(cmd(f"placeabs torch {fc[0]} {sy - 3} {fc[2] + 2} 1"))
    print("shaft at", fc, sy)
else:
    r, fl, sc, name = sys.argv[1:5]
    cmd(f"lightshape {r} {fl}"); cmd(f"lights {sc}"); time.sleep(0.8)
    cmd("look 90 5"); shot(f"light_{name}_a")
    cmd("look 270 -20"); shot(f"light_{name}_b")
