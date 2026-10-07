"""Repro for side blocks poking out of slopes: on hillside spots, dig a surface cell, then the cell below it,
and photograph the hole from the side. usage: py tools/slope_repro.py [spot ...]"""
import sys, os, time, math
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
cmd, S = R.cmd, R.S
SHOTS = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "shots"))

def photo(name, x, y, z, yaw, pitch):
    path = os.path.join(SHOTS, name + ".png")
    if os.path.exists(path): os.remove(path)
    cmd(f"photo {path} {x:.2f} {y:.2f} {z:.2f} {yaw} {pitch}")
    for _ in range(40):
        if os.path.exists(path) and os.path.getsize(path) > 0: break
        time.sleep(0.1)
    return path

def run(spot):
    cmd("tpship"); time.sleep(1)
    r = cmd(f"slopespot {spot}"); time.sleep(1.5)
    if not r.startswith("ok"): print("no slope spot", r); return
    fc = R.feet_cell()
    # a column two cells in front of the player
    dx, dz = 0, 2
    sy = R.surface(fc, dx, dz)
    if sy is None: print("no surface"); return
    x, z = (fc[0] + dx + 0.5) * S, (fc[2] + dz + 0.5) * S
    top = (sy + 1) * S
    print(f"spot {spot}: {r[:60]}  column surface y={sy}")
    photo(f"slope{spot}_0", x - 3.0, top + 2.0, z - 3.0, 45, 30)
    print("  dig surface:", R.dig(fc, dx, sy, dz))
    time.sleep(0.6)
    photo(f"slope{spot}_1", x - 3.0, top + 2.0, z - 3.0, 45, 30)
    print("  dig below:  ", R.dig(fc, dx, sy - 1, dz))
    time.sleep(0.6)
    photo(f"slope{spot}_2", x - 3.0, top + 2.0, z - 3.0, 45, 30)
    for ddx, ddz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        for y in (sy, sy - 1):
            print(f"   side ({ddx},{ddz}) y={y}:", cmd(f"cellabs {fc[0] + dx + ddx} {y} {fc[2] + dz + ddz}")[:110])

if __name__ == "__main__":
    cmd("god 1")
    for s in (sys.argv[1:] or ["0"]): run(int(s))
