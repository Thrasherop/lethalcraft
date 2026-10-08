"""In-game reproductions of the inventory bugs from the 1.4.0 playtest (GitHub issues), with real input ([E] while
aiming at items, mouse clicks in the inventory screen). Each prints what happened; run before and after a fix.
  #3  picked-up blocks land in separate single-item stacks
  #8  [E] says the inventory is full when a matching stack has room
  #12 picking an item up switches the selected slot
  #4  carry weight creeps up when items leave non-selected slots
usage: py tools/bug_repro.py [stacking|full|weight|all] [moonIdx]"""
import sys, os, time, re, math
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
import pilot
from regress import cmd

def st():
    s = cmd("state")
    slot = int(re.search(r"slot=(\d+)", s).group(1))
    held = re.search(r"held=([^=]+?) wt=", s).group(1)
    wt = int(re.search(r"wt=(-?\d+)lb", s).group(1))
    slots = re.search(r"slots=\[([^\]]*)\]", s).group(1).split(",")
    return slot, held, wt, slots

def ground_items(name, near, r=4.0):
    out = []
    for m in re.finditer(re.escape(name) + r"@([-\d.]+),([-\d.]+),([-\d.]+) d=[\d.]+ held=False", cmd("objs")):
        p = tuple(float(v) for v in m.groups())
        if math.dist(p, near) < r: out.append(p)
    return out

def pick_up(pos):
    """aim at an item on the ground and press E (the game's own pickup)"""
    pilot.aim_at(pos[0], pos[1] + 0.1, pos[2], eye=1.75)
    time.sleep(0.3)
    cmd("keys E 0.08"); time.sleep(1.2)

def spawn_row(key, n, count=1, step=1.0):
    """n separate ground stacks of `key` in a row in front of the player"""
    px, py_, pz, *_ = pilot.state()
    cmd("look 0 30"); time.sleep(0.3)
    for i in range(n):
        cmd(f"giveat {px + (i - (n - 1) / 2) * step:.2f} {py_ + 1.0:.2f} {pz + 1.8:.2f} {key} {count}")
    time.sleep(2.0)
    return (px, py_, pz + 1.8)

def repro_stacking():
    print("== #3 stacking / #12 slot switch: pick up 3 single cobblestone with [E] while holding a pickaxe")
    R.hold("wooden_pickaxe")
    s0 = st(); print("  before:", s0)
    at = spawn_row("cobblestone", 3)
    for p in sorted(ground_items("Cobblestone", at), key=lambda q: q[0]):
        pick_up(p)
        print("  picked one:", st())
    slot, held, wt, slots = st()
    cobble_slots = [e for e in slots if e.startswith("Cobblestone")]
    print(f"  RESULT #3: cobblestone in {len(cobble_slots)} slot(s) {cobble_slots}  (bug: more than 1)")
    print(f"  RESULT #12: selected slot {s0[0]} -> {slot}, holding {held}  (bug: no longer the pickaxe)")

def repro_full():
    print("== #8 full hotbar: 9 slots used, slot 1 has 12 observers; [E] on an observer on the ground")
    cmd("clearinv"); time.sleep(0.8)
    for k, n in (("observer", 12), ("dirt", 1), ("sand", 1), ("gravel", 1), ("glass", 1), ("bricks", 1), ("ice", 1), ("leaves", 1), ("snow_block", 1)):
        cmd(f"invgive {k} {n}"); time.sleep(0.5)
    R.wait(lambda: "-" not in st()[3], 8, step=0.5)
    print("  before:", st())
    at = spawn_row("observer", 1)
    items = ground_items("Observer", at)
    if not items: print("  (no observer on the ground)"); return
    pick_up(items[0])
    slot, held, wt, slots = st()
    obs = [e for e in slots if e.startswith("Observer")]
    left = ground_items("Observer", at)
    print(f"  RESULT #8: observer stacks {obs}, still on the ground: {len(left)}  (bug: still on the ground, stack stays 12)")
    print("  cursor tip:", cmd("cursortip")[:120])

def repro_weight():
    print("== #4 weight: move a stack out of and back into a non-selected slot through the [I] screen, 3 times")
    R.hold("wooden_pickaxe")
    cmd("invgive cobblestone 20"); time.sleep(2.0)
    base = st(); print("  start:", base)
    for i in range(3):
        cmd("keys I 0.08"); time.sleep(0.6)
        s = cmd("craftui state")
        ci = R.slot_of(s, "cobblestone")[0]
        cmd(f"craftui click hot {ci}")                 # picks the stack up (it leaves its slot)
        cmd(f"craftui click hot {ci}")                 # and puts it back
        time.sleep(1.5)
        cmd("craftui close"); time.sleep(1.0)
        print(f"  round {i + 1}:", st())
    cmd("clearinv"); time.sleep(1.5)
    empty = st()
    print(f"  RESULT #4: weight with the same items {base[2]} -> {st()[2] if False else '(see rounds)'}; empty hotbar weighs {empty[2]} lb  (bug: more than 0)")

if __name__ == "__main__":
    what = sys.argv[1] if len(sys.argv) > 1 else "all"
    if len(sys.argv) > 2 or "inShipPhase=True" in cmd("state"):
        print("landing", R.land(int(sys.argv[2]) if len(sys.argv) > 2 else 0)); time.sleep(6)
    cmd("god 1"); cmd("craftui close")
    R.start_flat(11)
    if what in ("stacking", "all"): repro_stacking()
    if what in ("full", "all"): R.start_flat(13); repro_full()
    if what in ("weight", "all"): repro_weight()
