"""#244: a real 25 m fall in boots with Feather Falling IV (put on in the [I] screen), and bare; the damage each time.
usage: py tools/ff_fall.py"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from regress import cmd
def hp(): return int(re.search(r"hp=(\d+)", cmd("state")).group(1))
R.start_flat(74, [(0, 2)]); cmd("clearenemies 200 all"); cmd("potions clear")
p = R.pos()
def fall():
    cmd("god 0"); cmd("hp 100"); time.sleep(0.3)
    cmd(f"tp {p[0]:.2f} {p[1] + 25:.2f} {p[2]:.2f}"); time.sleep(4.0)
    h = hp(); cmd("hp 100"); cmd("god 1"); return 100 - h
bare = fall()
cmd("clearinv"); cmd("invgive diamond_boots 1"); time.sleep(2.0)
import pilot; pilot.select("Diamond Boots"); time.sleep(0.3)
print(cmd("enchant FeatherFalling 4"))
cmd("keys I 0.08"); time.sleep(0.6)
hot = re.search(r"hotbar=\[([^\]]*)\]", cmd("craftui state")).group(1).split(",")
hi = next((i for i, e in enumerate(hot) if e.startswith("diamond_boots")), 0)
cmd(f"craftui click hot {hi}"); cmd("craftui click armor 3"); time.sleep(0.5)
print("armor:", re.search(r"armor=\[[^\]]*\]", cmd("craftui state")).group(0)[:200])
cmd("craftui close"); time.sleep(0.5)
ff = fall()
print(f"bare fall: {bare}  in Feather Falling IV boots: {ff}  (Minecraft's: 40% of it: {round(bare * 0.4)})")
cmd("wear none")
