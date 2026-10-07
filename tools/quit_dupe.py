"""Repro: quitting mid-round must roll chests back with the rest of the ship (vanilla only saves in orbit).
usage: py tools/quit_dupe.py setup   (orbit: drop 20 dirt on the floor, save, land, move the dirt into the empty chest by hand)
       py tools/quit_dupe.py check   (after menu-quit + relaunch: count dirt on the floor and in the chest)"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from pilot import *
from chest_save import chests, open_by_hand

def floor_dirt():
    return len(re.findall(r"Dirt@", cmd("objs")))

def quit_by_menu():
    press("Escape", 0.1); time.sleep(0.8)
    cmd("mouse moveto 280 128"); time.sleep(0.3); cmd("mouse left 0.08"); time.sleep(0.8)
    cmd("mouse moveto 340 218"); time.sleep(0.3); cmd("mouse left 0.08"); time.sleep(5)

mode = sys.argv[1] if len(sys.argv) > 1 else "setup"
cmd("god 1")
if mode == "setup":
    cmd("clearinv"); cmd("give dirt 20"); time.sleep(1.5)
    print("dirt items on the floor:", floor_dirt())
    print("orbit save:", cmd("save")); time.sleep(1)
    print("land:", R.land(int(sys.argv[2]) if len(sys.argv) > 2 else 12)); time.sleep(3)
    cmd("tpship"); time.sleep(1.5)
    m = re.search(r"Dirt@([-\d.]+),([-\d.]+),([-\d.]+)", cmd("objs"))
    pick_up_all(float(m.group(1)), float(m.group(3)), tries=4, y=float(m.group(2)))
    print("hotbar:", slots())
    c = sorted(chests())[0]
    print("chest:", open_by_hand(c))
    di = next(i for i, s in enumerate(slots()) if s.startswith("Dirt"))
    print(click_slot("chestui", "hot", di, shift=True))
    press("Escape", 0.1); time.sleep(0.5)
    print("floor dirt now:", floor_dirt(), "state:", state()[5][-80:])
    quit_by_menu()
    print("after quit:", cmd("state")[:40])
else:
    print("floor dirt:", floor_dirt())
    for c in chests(): print(c, open_by_hand(c)); press("Escape", 0.1); time.sleep(0.4)
