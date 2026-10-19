"""#244 part C, two instances: an enchantment the client gets at the table goes by the host's registry (Minecraft 26.3's
kinds don't fit the old 3-a-number packing): the client's item comes out enchanted, the host reads the same enchantments,
and enchanted armor the client wears glints on its model in the host's view. Run tools/mp.sh first (host 28771, client
28772). usage: py tools/ench_mp.py"""
import sys, os, time, re, math
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd

H, C = 28771, 28772
ok = []
def check(name, cond, detail=""):
    ok.append(bool(cond)); print(f"  [{'PASS' if cond else 'FAIL'}] {name}  ({str(detail)[:300]})", flush=True)
def state(port): return cmd("state", port)
def pos(port): return tuple(map(float, re.search(r"pos=([-\d.]+),([-\d.]+),([-\d.]+)", state(port)).groups()))
def hot(st, key):
    for i, x in enumerate(re.search(r"hotbar=\[([^\]]*)\]", st).group(1).split(",")):
        if x.split(":")[0].split("#")[0] == key: return i
HERE = os.path.dirname(os.path.abspath(__file__))

print("- the client enchants a bow at a table in the ship (the host placed it)")
for p in (H, C): cmd("god 1", p); cmd("gamemode survival", p)
# (out on the moon: the host goes to a flat spot and the table goes down there; the client is brought next to it)
import regress as R
fc = R.start_flat(100, [(0, 2), (0, 3)]); sy = R.surface(fc, 0, 3)
R.place("enchanting_table", fc, 0, sy + 1, 3); time.sleep(1.0)
hx, hy, hz = pos(H); cmd(f"tp {hx:.2f} {hy + 0.3:.2f} {hz:.2f}", C); time.sleep(1.5)
print("   client near:", [b for b in cmd("near 6", C).split(" ; ") if b.startswith("enchanting_table")])
cmd("clearinv", C); time.sleep(0.5); cmd("invgive bow 1", C); cmd("invgive lapis_lazuli 10", C); cmd("xplevel 30", C); time.sleep(2.5)
st = cmd("enchantui open near", C); print("  ", st[:120])
cmd(f"enchantui click hot {hot(st, 'bow')}", C); cmd("enchantui click item 0", C)
st = cmd("enchantui state", C); cmd(f"enchantui click hot {hot(st, 'lapis_lazuli')}", C); cmd("enchantui click lapis 0", C); time.sleep(0.5)
st = cmd("enchantui state", C); print("   offers:", re.search(r"offers=\[([^\]]*)\]", st).group(1))
sets0 = int(re.search(r"sets=(\d+)", cmd("playerheld", H)).group(1))
item = None
for k in range(6):
    cmd("enchantui click offer 0", C); time.sleep(0.6)
    st = cmd("enchantui state", C)
    m = re.search(r"item=(bow#\d+)", st)
    if m: item = m.group(1); break
print("   tries:", k + 1, "item:", item)
check("the client's bow comes out enchanted (after the host gave its list a number, if it had to wait)", item is not None, st[:200])
cmd("enchantui click item 0", C); time.sleep(0.3); cmd("enchantui close", C); time.sleep(2.0)
hb = re.search(r"slots=\[([^\]]*)\]", state(C)).group(1).split(",")
cmd(f"slot {next(i for i, s in enumerate(hb) if s.startswith('Bow'))}", C); time.sleep(1.5)
ch = cmd("itemdata", C); print("   client reads:", ch)
hh = cmd("playerheld", H); print("   host reads:", hh)
cl = re.search(r"ench=([^ ]+(?: [IVX]+)?(?:, [^ ]+(?: [IVX]+)?)*) key", ch)
check("the host reads the client's bow with the same enchantments", cl and ("ench=[" + cl.group(1) + "]") in hh, (ch, hh))
check("the registry on the host has the list", int(re.search(r"sets=(\d+)", hh).group(1)) >= max(1, sets0), hh[:60])

print("- the client wears an enchanted chestplate: the host draws it glinting")
cmd("invgive iron_chestplate 1", C); time.sleep(2.0)
hb = re.search(r"slots=\[([^\]]*)\]", state(C)).group(1).split(",")
cmd(f"slot {next(i for i, s in enumerate(hb) if s.startswith('Iron Chestplate'))}", C); time.sleep(0.5); cmd("rmb", C); time.sleep(1.0)  # (right-click: put on)
print("  ", cmd("wornench 1 Protection 4 Thorns 3 Unbreaking 3 Mending 1", C))
for k in range(5):
    r = cmd("wornench 1 Protection 4 Thorns 3 Unbreaking 3 Mending 1", C)
    if "pending" not in r: break
    time.sleep(0.6)
print("   client:", r)
time.sleep(2.0)
hh = cmd("playerheld", H); print("   host:", hh)
check("the host sees the client's chestplate with its four enchantments", "Protection IV" in hh and "Thorns III" in hh and "Mending" in hh, hh)
x, y, z = pos(C)
yaw = float(re.search(r"yaw=([-\d.]+)", state(C)).group(1)); a = math.radians(yaw)
out = os.path.join(HERE, "..", "shots", "ec_mp_glint.png").replace("\\", "/")
if os.path.exists(out): os.remove(out)
cmd(f"photo {out} {x + math.sin(a) * 2.3:.2f} {y + 1.4:.2f} {z + math.cos(a) * 2.3:.2f} {yaw + 180:.1f} 0 60", H); time.sleep(2)
print("   photo:", out, os.path.exists(out))
print(f"{sum(ok)}/{len(ok)} checks passed")
