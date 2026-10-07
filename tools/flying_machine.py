"""Minecraft wiki flying machine engines (Tutorial:Flying_machines/Java_Engine_Gallery), built from their schematics
floating above flat ground, started the way the wiki says (a block change in front of one observer), then tracked.
  B east / B west  "Two-way engine B": 2 observers, 2 slime, 2 sticky pistons in one layer (top view, north up):
                       z+3: [obs face N]
                       z+2: [slime    ][sticky piston facing W]
                       z+1: [sticky piston facing E][slime   ]
                       z+0:            [obs face S]
  A east           "Two-way engine A": the same 2x2 core, the observers on top of the slime blocks facing up.
  vertical up      "Vertical, 2-way, common slim variant" (side view, x across, y up):
                       y+3: [obs face up][slime]
                       y+2: [sticky piston down][slime]
                       y+1: [slime][sticky piston up]
                       y+0: [slime][obs face down]
usage: py tools/flying_machine.py [design] [flatIdx] [seconds]   (design: Beast, Bwest, Aeast, up; default Beast)"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from regress import cmd, place

DOWN, UP, N, S_, W, E = 0, 1, 2, 3, 4, 5

def build(design, fc, y):
    """Places the engine; returns (trigger cell (dx, y, dz), axis 'x' or 'y', expected sign)."""
    if design in ("Beast", "Bwest"):
        place("slime", fc, 0, y, 2); place("sticky_piston", fc, 1, y, 2, W)
        place("sticky_piston", fc, 0, y, 1, E); place("slime", fc, 1, y, 1)
        place("observer", fc, 0, y, 3, N); place("observer", fc, 1, y, 0, S_)
        return ((0, y, 4), "x", 1) if design == "Beast" else ((1, y, -1), "x", -1)
    if design == "Aeast":
        place("slime", fc, 0, y, 2); place("sticky_piston", fc, 1, y, 2, W)
        place("sticky_piston", fc, 0, y, 1, E); place("slime", fc, 1, y, 1)
        place("observer", fc, 0, y + 1, 2, UP); place("observer", fc, 1, y + 1, 1, UP)
        return (0, y + 2, 2), "x", 1   # above the observer on the west slime: it powers the piston that pushes east
    if design == "up":
        place("slime", fc, 0, y, 2); place("slime", fc, 0, y + 1, 2)
        place("sticky_piston", fc, 0, y + 2, 2, DOWN); place("observer", fc, 0, y + 3, 2, UP)
        place("observer", fc, 1, y, 2, DOWN); place("sticky_piston", fc, 1, y + 1, 2, UP)
        place("slime", fc, 1, y + 2, 2); place("slime", fc, 1, y + 3, 2)
        return (1, y - 1, 2), "y", 1    # below the bottom observer: it powers the piston facing up
    raise ValueError(design)

KINDS = ("slime", "sticky_piston", "observer", "piston_head")

def parts(center, r=60):
    out = []
    for e in cmd(f"near {r} {center[0]:.2f} {center[1]:.2f} {center[2]:.2f}").split(" ; "):
        m = re.match(r"\s*(\w+)\((-?\d+), (-?\d+), (-?\d+)\)", e)
        if m and m.group(1) in KINDS: out.append((m.group(1), int(m.group(2)), int(m.group(3)), int(m.group(4))))
    return out

def run(design="Beast", idx=24, secs=4.0, verbose=True):
    S = R.S
    cmd("god 1"); cmd("clearenemies 80")
    need = [(dx, dz) for dx in range(0, 2) for dz in range(0, 4)]
    fc = R.start_flat(idx, need)
    if fc is not None:
        # high enough to clear bushes and small bumps along the way
        sy = [s for s in (R.surface(fc, dx, dz) for dx, dz in ((0, 1), (0, 2), (1, 1), (1, 2))) if s is not None]  # (0 is a surface)
        y = (max(sy) if sy else fc[1]) + 10
    else:
        # no open ground: build it high in the air off the side of the ship (blocks don't need support)
        cmd("tpship"); time.sleep(1.0)
        fc = R.feet_cell(); fc = [fc[0] - 25 - idx, fc[1], fc[2]]
        y = fc[1] + 14
        if verbose: print(design, ": no open ground, building in the air at", fc, y)
    # the flight path must be open air (level geometry stops a machine, as it should): go higher until it is
    def clear(y0):
        if design == "up": cells = [(dx, y0 + 4 + i, 2) for i in range(10) for dx in (0, 1)]
        else:
            sgn = -1 if design == "Bwest" else 1
            cells = [(sgn * i if sgn > 0 else 1 - i, y0 + dy, dz) for i in range(2, 12) for dz in range(0, 4) for dy in ((0, 1) if design == "Aeast" else (0,))]
        for dx, cy, dz in cells:
            c = (fc[0] + dx, cy, fc[2] + dz)
            if cmd(f"obstructed {c[0]} {c[1]} {c[2]}") != "no" or ") Air" not in cmd(f"cellabs {c[0]} {c[1]} {c[2]}"): return False
        return True
    for lift in range(5):
        if clear(y): break
        y += 6
    else:
        if verbose: print(design, ": flight path still blocked at y", y)
    trig, axis, sign = build(design, fc, y)
    time.sleep(1.0)
    center = ((fc[0] + .5) * S, (y + .5) * S, (fc[2] + 2) * S)
    # only this machine: its rows across the direction it flies (other contraptions may be standing around)
    if axis == "x": mine = lambda p: p[2] in (y, y + 1) and fc[2] <= p[3] <= fc[2] + 3
    else: mine = lambda p: p[1] in (fc[0], fc[0] + 1) and p[3] == fc[2] + 2
    mparts = lambda c, r=60: [p for p in parts(c, r) if mine(p)]
    p0 = [p for p in mparts(center) if p[0] in ("slime", "observer", "sticky_piston")]
    cmd(f"placeabs stone {fc[0] + trig[0]} {trig[1]} {fc[2] + trig[2]}")
    t0, track = time.time(), []
    while time.time() - t0 < secs:
        sl = [p for p in mparts(center) if p[0] == "slime"]
        track.append((round(time.time() - t0, 1), sorted((p[1] - fc[0]) if axis == "x" else (p[2] - y) for p in sl)))
        time.sleep(0.25)
    p1 = [p for p in mparts(center, 90) if p[0] in ("slime", "observer", "sticky_piston")]
    shift = [((p[1] - fc[0]) if axis == "x" else (p[2] - y)) for p in p1 if p[0] == "slime"]
    start = [((p[1] - fc[0]) if axis == "x" else (p[2] - y)) for p in p0 if p[0] == "slime"]
    moved = (min(shift) - min(start)) * sign if shift and start else 0
    if verbose:
        print(f"{design}: spot {fc} y={y}; slime {'x' if axis == 'x' else 'y'} over time: {track[::3]}")
        print(f"  parts {len(p0)} -> {len(p1)}; moved {moved} blocks {'east' if axis == 'x' and sign > 0 else 'west' if axis == 'x' else 'up'}")
    return moved, len(p0), len(p1)

if __name__ == "__main__":
    d = sys.argv[1] if len(sys.argv) > 1 else "Beast"
    if "--fresh" in sys.argv: sys.argv.remove("--fresh"); print("fresh landing:", R.land(int(os.environ.get("MOON", "0")))); time.sleep(4)
    run(d, int(sys.argv[2]) if len(sys.argv) > 2 else 24, float(sys.argv[3]) if len(sys.argv) > 3 else 4.0)
