"""#14: break a full ship chest while the ship is taking off; do its contents drop (and stay once in orbit)?
usage: py tools/chest_takeoff.py [seconds after takeoff starts] [moonIdx] [--client]
  --client: the chest is placed, filled and broken by the joined client (tools/mp.sh); the host flies the ship
  --landing: placed and filled in orbit, broken on the way down"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
import pilot
from regress import cmd

P = cmd  # the player who does the chest work: the host, or the client with --client

def hold(key):
    name = " ".join(w.capitalize() for w in key.split("_"))
    P("clearinv"); time.sleep(1.0); P(f"invgive {key} 1")
    for _ in range(20):
        sl = re.search(r"slots=\[([^\]]*)\]", P("state")).group(1).split(",")
        i = next((j for j, e in enumerate(sl) if e.startswith(name)), None)
        if i is not None:
            P(f"slot {i}"); time.sleep(0.4)
            if f"held={name}" in P("state"): return True
        time.sleep(0.3)
    return False

def ship_items(names=("Cobblestone", "Oak Planks", "Chest")):
    """item totals once they hold steady (stacks merging on the floor change hands for a moment)"""
    last = None
    for _ in range(8):
        now = _ship_items(names)
        if now == last: return now
        last = now; time.sleep(1.5)
    return last

def _ship_items(names):
    """how many of each lie loose (not held) in the level: item totals, since a drop can merge into a stack already there"""
    out = {}
    for n in names:
        r = cmd(f"find {n}")
        out[n] = sum(int(m.group(1)) if m.group(1) else 1 for e in r.split(" ; ") if e.strip() and "held=False" in e
                     for m in [re.search(r"x(\d+)@", e)])
    return out

def chests():
    return set(re.findall(r"chest\((-?\d+, -?\d+, -?\d+)\)y(\d+)", P("near 14")))

mine = None
def chest_block():
    return mine in chests()

def run(delay=4.0, moon=0, landing=False):
    if landing:
        # in orbit, the ship routed to the moon: the chest is broken on the way down
        if "inShipPhase=True" not in cmd("state"):
            cmd("tpship"); time.sleep(1); cmd("leave"); R.wait(lambda: "inShipPhase=True" in cmd("state"), 120); time.sleep(2)
        cmd("deadline"); cmd(f"route {moon}"); time.sleep(8)
    elif "inShipPhase=False" not in cmd("state"): print("landing", R.land(moon)); time.sleep(6)
    P("god 1"); P("tpship"); time.sleep(1.0)
    # a chest on the ship floor, in front of the player
    global mine
    had = chests()
    hold("chest")
    P("look 0 60"); time.sleep(0.4); P("rmb"); time.sleep(1.0)
    new = chests() - had
    if not new: print("couldn't place a chest in the ship:", P("place?")[:200]); return
    mine = new.pop(); print("chest at", mine)
    # fill it
    P("invgive cobblestone 20"); P("invgive oak_planks 12"); time.sleep(2.0)
    st = P("chestui open near")
    for k in ("cobblestone", "oak_planks"):
        i = R.slot_of(P("chestui state"), k)[0]
        if i is not None: P(f"chestui click hot {i} shift"); time.sleep(0.8)
    print("chest:", re.search(r"cells=\[([^\]]*)\]", P("chestui state")).group(1)[:80])
    P("chestui close"); time.sleep(0.5)
    hold("iron_axe")
    P("look 0 60"); time.sleep(0.4)
    before = ship_items()
    print("loose items before:", before)
    # take off (or land), and break it on the way
    cmd("land" if landing else "leave"); t0 = time.time()
    time.sleep(delay)
    print(f"breaking it {delay:.0f} s into the {'landing' if landing else 'takeoff'}:", P("state")[:60], "| target:", re.search(r"target=(\S+)", P("state")).group(1))
    P("lmb down"); R.wait(lambda: not chest_block(), 8, step=0.2); P("lmb up")
    print(f"  chest gone after {time.time() - t0 - delay:.1f}s: {not chest_block()}")
    time.sleep(1.0)
    mid = ship_items()
    print("  right after:", mid, "| new:", {k: mid[k] - before[k] for k in mid})
    if landing: R.wait(lambda: "landed=True" in cmd("leave_check"), 90)
    else: R.wait(lambda: "inShipPhase=True" in cmd("state"), 90)
    time.sleep(4)
    after = ship_items()
    print("landed:" if landing else "in orbit:", after, "| new:", {k: after[k] - before[k] for k in after}, "(expected: 20 cobblestone, 12 planks, 1 chest)")

if __name__ == "__main__":
    if "--client" in sys.argv:
        P = lambda c: cmd(c, 28772); sys.argv.remove("--client")
        print("as the client")
    landing = "--landing" in sys.argv
    if landing: sys.argv.remove("--landing")
    run(float(sys.argv[1]) if len(sys.argv) > 1 else 4.0, int(sys.argv[2]) if len(sys.argv) > 2 else 0, landing)
