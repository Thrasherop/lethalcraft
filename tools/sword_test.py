"""Swords vs tools on a real monster: a baboon hawk (4 HP) held in front of the player, hit with real mouse clicks
(faster than any swing cooldown), counting clicks and seconds to the kill.
usage: py tools/sword_test.py [item keys...]   (default: wooden_sword diamond_sword wooden_pickaxe)"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from regress import cmd

held_at = None

def hp_of(name="Baboon"):
    """the held monster (by where it stands): (dead, hp)"""
    # (earlier kills lie on the same spot: a living one there is the one being fought)
    for e in cmd("enemies").split(" ; "):
        if e.startswith(name) and held_at and held_at in e and "dead=False" in e:
            return (False, int(re.search(r"hp=(-?\d+)", e).group(1)))
    return (True, 0)

def fight(key, idx=3):
    cmd("clearenemies 80")
    name = " ".join(w.capitalize() for w in key.split("_"))
    for attempt in range(4):
        # a spot where the player isn't sinking (quicksand makes the game drop what you hold)
        fc = R.start_flat(idx + attempt * 5)
        R.hold(key)
        time.sleep(1.5)
        if "sinking=True" not in cmd("flags") and f"held={name}" in cmd("state"): break
        print("  (sinking or lost the item here: another spot)")
    cmd("look 0 5"); time.sleep(0.3)
    cmd("enemy baboon 6"); time.sleep(2.5)
    global held_at
    r = cmd("enemyhold baboon 1.7"); print(" ", r)
    held_at = re.search(r"at ([-\d.]+,[-\d.]+,[-\d.]+)", r).group(1)
    cmd("clearenemies 80")  # (everything else: another monster mauling the god-mode player makes the game drop what it holds)
    time.sleep(0.5)
    st = cmd("state")
    print("  mine?:", cmd("mine?")[:150]); print("  aim:", re.search(r"target=(\S+)", st).group(1), "held=" + re.search(r"held=([^=]+?) wt=", st).group(1), "|", [e for e in cmd("enemies").split(" ; ") if held_at in e and "dead=False" in e][:1])
    t0 = time.time(); clicks = 0; hits = []
    last = hp_of()[1]
    while time.time() - t0 < 15:
        cmd("mouse left 0.04"); clicks += 1
        if clicks % 8 == 0: cmd("clearenemies 80")
        time.sleep(0.25)
        dead, hp = hp_of()
        if hp != last: hits.append((round(time.time() - t0, 2), last - hp)); last = hp
        if dead or hp <= 0: break
    took = time.time() - t0
    if not dead: print("   (no kill) flags:", cmd("flags")[:260], "| target:", re.search(r"target=(\S+)", cmd("state")).group(1), "| mine?:", cmd("mine?")[:100])
    print(f"{key}: dead={dead} after {took:.1f}s, {clicks} clicks, hits (t, dmg) {hits}")
    return took, hits

if __name__ == "__main__":
    keys = sys.argv[1:] or ["wooden_sword", "diamond_sword", "wooden_pickaxe"]
    if "inShipPhase=True" in cmd("state"): print("landing", R.land(0)); time.sleep(5)
    cmd("god 1")
    for i, k in enumerate(keys): fight(k, 3 + i * 2)  # (a fresh spot each time: dead monsters lie where they fell)
