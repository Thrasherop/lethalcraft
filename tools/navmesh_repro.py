"""#25: do outside monsters lose the navmesh ("Agent not on nav mesh" spam) when the ground under them is dug out?
Spawns a monster, watches it with no digging (the control), then digs a pit under it and watches again.
usage: py tools/navmesh_repro.py [moonIdx] [monster name]"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from regress import cmd
from moontour import log_len, new_log, S

ERR = ("Agent not on nav mesh", "CalculatePolygonPath")

def the(name):
    for e in cmd("enemies").split(" ; "):
        if e.lower().startswith(name.lower()) and "dead=False" in e:
            m = re.search(r"@([-\d.]+),([-\d.]+),([-\d.]+)", e)
            return e, tuple(float(x) for x in m.groups())
    return None, None

def watch(label, name, secs):
    n0 = log_len(); t0 = time.time(); states = set()
    while time.time() - t0 < secs:
        e, _ = the(name)
        if e: states.add(re.search(r"onNav=(\S+)", e).group(1))
        time.sleep(1.0)
    log = new_log(n0)
    errs = sum(log.count(k) for k in ERR)
    print(f"  {label}: {errs} navmesh errors in {secs} s, onNav seen {sorted(states)}")
    for line in [l for l in log.splitlines() if any(k in l for k in ERR)][:3]: print("    ", line[:160])
    return errs

def run(moon=1, name="Manticoil"):
    if "inShipPhase=False" not in cmd("state"): print("landing", R.land(moon)); time.sleep(8)
    cmd("god 1"); cmd("clearenemies 200")
    fc = R.start_flat(6)
    print("spawn:", cmd(f"enemy {name.split()[0].lower()} 12")[:120]); time.sleep(3)
    e, p = the(name)
    if not e: print("no", name); return
    print(" ", e[:200])
    watch("no digging", name, 30)
    # a pit under where it stands (game time slowed meanwhile, or it walks off before the digging is done)
    cmd("time 0.02")
    try:
        e, p = the(name)
        # (the ground grid is shifted per level: stand 4 cells beside it and take the cell from there)
        cmd(f"tp {p[0]:.2f} {p[1] + 0.5:.2f} {p[2] - 4 * S:.2f}"); time.sleep(0.5)
        fc = R.feet_cell(); cx, cy, cz = fc[0], fc[1], fc[2] + 4
        dug = 0
        for dy in (0, 1, 2):
            for dx in range(-1, 2):
                for dz in range(-1, 2):
                    if re.match(r"(dug|broke) ", cmd(f"digabs {cx + dx} {cy - dy} {cz + dz}")): dug += 1
        print(f"  dug {dug} cells under it at {cx},{cy},{cz}; it's at {p}; now {the(name)[0][:200]}")
    finally:
        cmd("time 1")
    errs = watch("after digging under it", name, 40)
    print("RESULT:", "reproduced" if errs else "not reproduced")

if __name__ == "__main__":
    run(int(sys.argv[1]) if len(sys.argv) > 1 else 1, " ".join(sys.argv[2:]) or "Manticoil")
