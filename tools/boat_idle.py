"""#245: a boat on March's lake, sitting still (empty, then with you in it), 40 s each; where it is every 4 s.
usage: py tools/boat_idle.py"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from regress import cmd, pos
def info():
    m = re.search(r"boat ([-\d.]+),([-\d.]+),([-\d.]+) status=(\w+)", cmd("boatinfo")); return m.groups() if m else None
cmd("god 1"); cmd("clearenemies 60 all"); cmd("clearinv"); cmd("invgive oak_boat 1"); time.sleep(2.0); cmd("slot 0")
cmd("tp 0 3 0.5"); time.sleep(2.0); cmd("look 0 40"); time.sleep(0.5); cmd("rmb"); time.sleep(1.5)
for label in ("empty", "with me in it"):
    if label != "empty":
        b = info(); R.look_at(float(b[0]), float(b[1]) + 0.3, float(b[2])); time.sleep(0.4); cmd("keys E 0.15"); time.sleep(1.0)
    ys = []
    for i in range(11):
        b = info(); ys.append(f"{b[1]}{b[3][0]}" if b else "-"); time.sleep(4.0)
    print(f"{label}: y every 4 s:", " ".join(ys), "| me", f"{pos()[1]:.2f}")
