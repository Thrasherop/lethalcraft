"""#14: break a full ship chest while the ship is taking off; do its contents drop (and stay once in orbit)?
usage: py tools/chest_takeoff.py [seconds after takeoff starts] [moonIdx]"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
import pilot
from regress import cmd

def ship_items(names=("Cobblestone", "Oak Planks", "Chest")):
    out = {}
    for n in names:
        r = cmd(f"find {n}")
        out[n] = [e for e in r.split(" ; ") if e.strip() and "held=False" in e]
    return out

def chests():
    return set(re.findall(r"chest\((-?\d+, -?\d+, -?\d+)\)y(\d+)", cmd("near 14")))

mine = None
def chest_block():
    return mine in chests()

def run(delay=4.0, moon=0):
    if "inShipPhase=False" not in cmd("state"): print("landing", R.land(moon)); time.sleep(6)
    cmd("god 1"); cmd("tpship"); time.sleep(1.0)
    # a chest on the ship floor, in front of the player
    global mine
    had = chests()
    R.hold("chest")
    cmd("look 0 60"); time.sleep(0.4); cmd("rmb"); time.sleep(1.0)
    new = chests() - had
    if not new: print("couldn't place a chest in the ship:", cmd("place?")[:200]); return
    mine = new.pop(); print("chest at", mine)
    # fill it
    cmd("invgive cobblestone 20"); cmd("invgive oak_planks 12"); time.sleep(2.0)
    st = cmd("chestui open near")
    for k in ("cobblestone", "oak_planks"):
        i = R.slot_of(cmd("chestui state"), k)[0]
        if i is not None: cmd(f"chestui click hot {i} shift"); time.sleep(0.8)
    print("chest:", re.search(r"cells=\[([^\]]*)\]", cmd("chestui state")).group(1)[:80])
    cmd("chestui close"); time.sleep(0.5)
    R.hold("iron_axe")
    cmd("look 0 60"); time.sleep(0.4)
    before = ship_items()
    print("loose items in the ship before:", {k: len(v) for k, v in before.items()})
    # take off, and break it on the way up
    cmd("leave"); t0 = time.time()
    time.sleep(delay)
    print(f"breaking it {delay:.0f} s into the takeoff:", cmd("state")[:60], "| target:", re.search(r"target=(\S+)", cmd("state")).group(1))
    cmd("lmb down"); R.wait(lambda: not chest_block(), 8, step=0.2); cmd("lmb up")
    print(f"  chest gone after {time.time() - t0 - delay:.1f}s: {not chest_block()}")
    time.sleep(1.0)
    mid = ship_items()
    print("  right after:", {k: len(v) for k, v in mid.items()}, [e[:70] for v in mid.values() for e in v][:4])
    R.wait(lambda: "inShipPhase=True" in cmd("state"), 90)
    time.sleep(4)
    after = ship_items()
    print("in orbit:", {k: len(v) for k, v in after.items()}, [e[:70] for v in after.values() for e in v][:4])

if __name__ == "__main__":
    run(float(sys.argv[1]) if len(sys.argv) > 1 else 4.0, int(sys.argv[2]) if len(sys.argv) > 2 else 0)
