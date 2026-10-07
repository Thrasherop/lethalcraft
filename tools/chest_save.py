"""Play check: chests in the ship keep their contents across a quit (menu) and relaunch.
usage: py tools/chest_save.py place   (in orbit: place a chest by hand, store cobblestone + planks with the mouse)
       py tools/chest_save.py check   (after relaunch: open the same chest with E and list it)"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
from pilot import *
S = 1.4

def chests():
    return [tuple(map(int, m)) for m in re.findall(r"chest\((-?\d+), (-?\d+), (-?\d+)\)", cmd("near 12"))]

def where(c):
    for e in cmd("where chest").split(" ; "):
        m = re.match(r"\((-?\d+), (-?\d+), (-?\d+)\)@([-\d.]+),([-\d.]+),([-\d.]+)", e.strip())
        if m and tuple(map(int, m.groups()[:3])) == c: return tuple(map(float, m.groups()[3:]))

def open_by_hand(c):
    aim_at(*where(c))
    press("E", 0.15); time.sleep(0.8)
    return cmd("chestui state")

def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "place"
    cmd("god 1")
    if mode == "place":
        print(state())
        cmd("clearinv"); time.sleep(0.3)
        cmd("give chest 1"); cmd("give cobblestone 10"); cmd("give oak_planks 7"); time.sleep(1.5)
        for t in range(4):
            ms = re.findall(r"(Chest|Cobblestone|Oak Planks)@([-\d.]+),([-\d.]+),([-\d.]+) d=[\d.]+ held=False", cmd("objs"))
            if not ms: break
            pick_up_all(float(ms[0][1]), float(ms[0][3]), tries=3, y=float(ms[0][2]))
        print(slots())
        before = set(chests())
        select("Chest"); set_pitch(45); use(); time.sleep(1.0)
        new = [c for c in chests() if c not in before]
        print("placed:", new)
        if not new: sys.exit(1)
        c = new[0]
        st = open_by_hand(c)
        print("open:", st)
        hot = slots()
        ci = next(i for i, s in enumerate(hot) if s.startswith("Cobblestone"))
        pi = next(i for i, s in enumerate(hot) if s.startswith("Oak Planks"))
        click_slot("chestui", "hot", ci); print(click_slot("chestui", "chest", 0))
        print(click_slot("chestui", "hot", pi, shift=True))
        shot("chest_save_stored")
        press("Escape", 0.1); time.sleep(0.5)
        print("closed:", cmd("chestui state")[:40], "| hotbar:", slots())
    else:
        cs = chests(); print("chests near:", cs)
        if not cs: sys.exit(1)
        for c in cs:
            st = open_by_hand(c); print(c, "->", st)
            shot("chest_save_%s_%d" % (mode, c[2]))
            press("Escape", 0.1); time.sleep(0.4)

if __name__ == "__main__": main()
