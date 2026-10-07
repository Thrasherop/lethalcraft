"""Metal in a lightning storm: land on a stormy moon, drop metal items outside after the storm has picked its targets,
and check they join its list (with the fix) or don't (storm 0 = the game's own behaviour), then wait for a strike on one.
usage: py tools/storm_test.py [moonIdx]"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from regress import cmd

def main(idx=0):
    if "inShipPhase=False" in R.state():
        cmd("tpship"); time.sleep(1); cmd("leave")
        R.wait(lambda: "inShipPhase=True" in R.state(), 120); time.sleep(2)
    cmd("deadline"); cmd(f"route {idx}"); time.sleep(8)
    print("weather:", cmd("weather Stormy"))
    cmd("land")
    if not R.wait(lambda: "landed=True" in cmd("leave_check"), 90): print("didn't land"); return
    cmd("god 1")
    time.sleep(20)  # the storm collects the metal lying around 15 s in
    print("at start:", cmd("storm"))
    fc = R.start_flat(4)
    px, py_, pz = (float(v) for v in re.search(r"pos=([-\d.]+),([-\d.]+),([-\d.]+)", cmd("state")).groups())
    # the game's own behaviour: an ingot made now is never a target
    cmd("storm 0")
    cmd(f"giveat {px + 3:.2f} {py_ + 1.5:.2f} {pz:.2f} iron_ingot 1"); time.sleep(2)
    off = cmd("storm")
    print("join off, ingot dropped:", off)
    cmd("storm 1")
    cmd(f"giveat {px - 3:.2f} {py_ + 1.5:.2f} {pz:.2f} flint_and_steel 1"); time.sleep(2)
    on = cmd("storm")
    print("join on, flint dropped:", on)
    print("RESULT off-excludes-ingot:", "Iron Ingot" not in off, " on-includes-flint:", "Flint and Steel" in on)
    # the real thing: a pickaxe made mid-storm, carried around outside, draws the lightning to you
    cmd("clearinv"); cmd("invgive iron_pickaxe 1"); time.sleep(1.5)
    sl = re.search(r"slots=\[([^\]]*)\]", cmd("state")).group(1).split(",")
    cmd(f"slot {next(i for i, e in enumerate(sl) if e.startswith('Iron Pickaxe'))}")
    print("holding:", re.search(r"held=(\S+)", cmd("state")).group(1), "|", cmd("storm"))
    s0 = int(re.search(r"strikes on metal (\d+)", cmd("storm")).group(1))
    t0 = time.time(); seen = []
    while time.time() - t0 < 180:
        s = cmd("storm")
        m = re.search(r"targeting (.+?) \|", s)
        if m and (not seen or seen[-1] != m.group(1)): seen.append(m.group(1)); print(f"{time.time() - t0:5.1f}s targeting {m.group(1)}")
        n = int(re.search(r"strikes on metal (\d+)", s).group(1))
        if n > s0 and seen and seen[-1] == "Iron Pickaxe": print(f"{time.time() - t0:5.1f}s STRUCK while targeting the held pickaxe; hp", re.search(r"hp=(\d+)", cmd("state")).group(1)); break
        if n > s0: print(f"{time.time() - t0:5.1f}s strike #{n} (target {seen[-1] if seen else '?'})"); s0 = n
        time.sleep(0.5)
    print("targets seen:", seen)

if __name__ == "__main__":
    main(int(sys.argv[1]) if len(sys.argv) > 1 else 0)
