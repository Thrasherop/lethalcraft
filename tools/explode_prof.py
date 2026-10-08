"""Where the host's time goes during big explosions (#18): stress scenarios with the dev profiler on.
  baseline  nothing happening (the frame time to compare against)
  chain     a 7x7x3 block of stone threaded with TNT, set off as a chain
  craters   20 TNT on bare ground, set off together (raw-ground craters)
  mines     12 game explosions (landmine / Old Bird missile path) on bare ground, without and with ground carving
usage: py tools/explode_prof.py [scenario...] [--moon N]"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from regress import cmd, S

def profile(label, seconds, action):
    cmd("prof on")
    t0 = time.time()
    action()
    time.sleep(max(0.0, seconds - (time.time() - t0)))
    out = cmd("prof off")
    print(f"\n=== {label}")
    head, _, rest = out.partition(" | worst: ")
    print("  " + head)
    worst, _, sections = rest.partition(" | ")
    print("  worst frames:", worst)
    for s in sections.split(" ; ")[:22]:
        if s.strip(): print("   ", s.strip())
    return out

def spot(idx):
    fc = R.start_flat(idx, [(dx, dz) for dx in (-3, 0, 3) for dz in (2, 5, 8)])
    return fc

def scenario_baseline():
    R.start_flat(2)
    profile("baseline (5 s, nothing happening)", 5, lambda: None)

def scenario_chain():
    fc = spot(4)
    sy = R.surface(fc, 0, 5)
    n_tnt = 0
    for dx in range(-3, 4):
        for dz in range(2, 9):
            for dy in range(1, 4):
                tnt = (dx + dz + dy) % 2 == 0
                cmd(f"placeabs {'tnt' if tnt else 'stone'} {fc[0] + dx} {sy + dy} {fc[2] + dz}")
                n_tnt += tnt
    time.sleep(2.0)
    cmd(f"tp {(fc[0] + .5) * S:.2f} {(sy + 1) * S + 0.5:.2f} {(fc[2] - 12 + .5) * S:.2f}"); time.sleep(1.0)
    # (light a TNT in the middle: TNT sits where dx + dz + dy is even, in the block's own offsets)
    dy = 1 if (0 + 5 + 1) % 2 == 0 else 2
    profile(f"chain: 7x7x3 stone with {n_tnt} TNT (12 s)", 12, lambda: print("   lit:", cmd(f"igniteabs {fc[0]} {sy + dy} {fc[2] + 5}")))

def scenario_craters():
    fc = spot(8)
    cells = []
    for dx in (-8, -4, 0, 4, 8):          # (20 TNT)
        for dz in (4, 8, 12, 16):
            sy = R.surface(fc, dx, dz)
            if sy is None: continue
            cmd(f"placeabs tnt {fc[0] + dx} {sy + 1} {fc[2] + dz}"); cells.append((fc[0] + dx, sy + 1, fc[2] + dz))
    time.sleep(1.5)
    cmd(f"tp {(fc[0] + .5) * S:.2f} {(R.surface(fc, 0, 0) + 1) * S + 0.5:.2f} {(fc[2] - 12) * S:.2f}"); time.sleep(1.0)
    def go():
        for c in cells: cmd(f"igniteabs {c[0]} {c[1]} {c[2]}")
    profile(f"craters: {len(cells)} TNT on bare ground (8 s)", 8, go)

def scenario_mines():
    for carve in (0, 1):
        fc = spot(12 + carve * 4)
        pts = []
        for dx in (-8, -3, 2, 7):
            for dz in (6, 11, 16):
                sy = R.surface(fc, dx, dz)
                if sy is not None: pts.append(((fc[0] + dx + .5) * S, (sy + 1) * S, (fc[2] + dz + .5) * S))
        cmd(f"tp {(fc[0] + .5) * S:.2f} {(R.surface(fc, 0, 0) + 1) * S + 0.5:.2f} {(fc[2] - 14) * S:.2f}"); time.sleep(1.0)
        def go():
            for p in pts: cmd(f"boom {p[0]:.2f} {p[1]:.2f} {p[2]:.2f} {carve}")
        profile(f"mines: {len(pts)} game explosions, ground carving {'ON' if carve else 'off (default)'} (6 s)", 6, go)

def scenario_aftermath():
    """the lag that stays (the host's frame time near big craters long after they went off): idle frames at a spot,
    then 20 TNT craters there, then idle frames at the same spot again once everything has settled"""
    fc = spot(20)
    cmd("fpsinfo -1")  # (frame rate uncapped: a lasting cost in rendering or physics shows instead of hiding under the cap)
    stand = f"tp {(fc[0] + .5) * S:.2f} {(R.surface(fc, 0, 0) + 1) * S + 0.5:.2f} {(fc[2] - 4) * S:.2f}"
    cmd(stand); cmd("look 0 10"); time.sleep(2.0)
    print("\n  before:", cmd("scenestats"))
    profile("aftermath: idle at the spot BEFORE (5 s)", 5, lambda: None)
    cells = []
    for dx in (-8, -4, 0, 4, 8):          # (20 TNT: enough for the lasting cost to stand out)
        for dz in (4, 8, 12, 16):
            sy = R.surface(fc, dx, dz)
            if sy is None: continue
            cmd(f"placeabs tnt {fc[0] + dx} {sy + 1} {fc[2] + dz}"); cells.append((fc[0] + dx, sy + 1, fc[2] + dz))
    time.sleep(1.0)
    cmd(f"tp {(fc[0] + .5) * S:.2f} {(R.surface(fc, 0, 0) + 1) * S + 0.5:.2f} {(fc[2] - 14) * S:.2f}"); time.sleep(0.5)
    for c in cells: cmd(f"igniteabs {c[0]} {c[1]} {c[2]}")
    time.sleep(15.0)  # (everything has gone off, fallen and settled)
    cmd(stand); cmd("look 0 10"); time.sleep(2.0)
    print("\n  after:", cmd("scenestats"))
    profile(f"aftermath: idle at the same spot AFTER {len(cells)} craters (5 s)", 5, lambda: None)

if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    moon = int(sys.argv[sys.argv.index("--moon") + 1]) if "--moon" in sys.argv else 0
    if "--moon" in sys.argv: args = [a for a in args if a != str(moon)]
    print("landing", R.land(moon)); time.sleep(8)
    cmd("god 1"); cmd("clearenemies 200")
    for s in args or ["baseline", "chain", "craters", "mines"]:
        globals()["scenario_" + s]()
