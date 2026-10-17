"""Hand-play helper: lay a recipe into the open crafting table's grid from the hotbar (the screen's own click handling,
dev craftui clicks), print what comes out, and take it all (shift-click).
py tools/craft.py <item key> <grid cells, comma-separated 0-8> [<item key> <cells>]..."""
import sys, os, re, time
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd

def hot(key):
    st = cmd("craftui state"); hb = re.search(r"hotbar=\[([^\]]*)\]", st).group(1).split(",")
    return next((i for i, x in enumerate(hb) if x.split(":")[0].split("#")[0] == key), None)

args = sys.argv[1:]
for key, cells in zip(args[0::2], args[1::2]):
    i = hot(key)
    if i is None: print("not in the hotbar:", key); sys.exit(1)
    cmd(f"craftui click hot {i}")
    for c in cells.split(","): cmd(f"craftui click grid {c} right")
    cmd(f"craftui click hot {i}")
time.sleep(0.3)
st = cmd("craftui state")
print("out:", re.search(r"out=(\S+)", st).group(1))
cmd("craftui click out 0 shift"); time.sleep(0.3)
st = cmd("craftui state")
print("grid left:", re.search(r"grid=\[([^\]]*)\]", st).group(1), "| hotbar:", re.search(r"hotbar=\[([^\]]*)\]", st).group(1))
