"""Two-instance check of armor on players' models (#20): the client puts on armor; the host sees it on the client's
model (photographed from the front). Run tools/mp.sh first (host on 28771, client on 28772).
usage: py tools/mp_armor.py"""
import sys, os, time, re, math
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd

H, C = 28771, 28772
ok = []

def check(name, cond, detail=""):
    ok.append(bool(cond))
    print(f"  [{'PASS' if cond else 'FAIL'}] {name}  ({str(detail)[:200]})")

def state(port): return cmd("state", port)
def pos(port): return tuple(map(float, re.search(r"pos=([-\d.]+),([-\d.]+),([-\d.]+)", state(port)).groups()))

print("- the client puts on a set (diamond helmet, iron chestplate, golden leggings, iron boots)")
keys = ["diamond_helmet", "iron_chestplate", "golden_leggings", "iron_boots"]
cmd("craftui close", C); cmd("clearinv", C); time.sleep(0.8)
for k in keys: cmd(f"invgive {k} 1", C)
time.sleep(3)
cmd("keys I 0.08", C); time.sleep(0.8)
for i in range(4): cmd(f"craftui click hot {i} shift", C); time.sleep(0.4)
time.sleep(1); st = cmd("craftui state", C); cmd("craftui close", C)
check("the client wears it", "armor=[diamond_helmet,iron_chestplate,golden_leggings,iron_boots]" in st, st[:140])
time.sleep(2)
seen = cmd("armormodels", H)
check("the host draws it on the client's model", "Player #1: diamond_helmet(1) iron_chestplate(6) golden_leggings(5) iron_boots(2)" in seen, seen)
x, y, z = pos(C)
yaw = float(re.search(r"yaw=([-\d.]+)", state(C)).group(1))
a = math.radians(yaw)
out = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "shots", "mp_armor.png")).replace("\\", "/")
if os.path.exists(out): os.remove(out)
cmd(f"photo {out} {x + math.sin(a) * 2.3:.2f} {y + 1.4:.2f} {z + math.cos(a) * 2.3:.2f} {yaw + 180:.1f} 0 60", H)
time.sleep(2)
print("  photo:", out, os.path.exists(out))
print("- taking it off: gone from the host's view too")
cmd("keys I 0.08", C); time.sleep(0.8)
for i in range(4): cmd(f"craftui click armor {i} shift", C); time.sleep(0.4)
cmd("craftui close", C); time.sleep(2)
seen = cmd("armormodels", H)
check("the host's view of the client is bare again", "Player #1: -(0) -(0) -(0) -(0)" in seen, seen)
print(f"{sum(ok)}/{len(ok)} checks passed")
