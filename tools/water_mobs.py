"""#247: Minecraft mobs by March's lake: spawn some on the shore and log, every 5 s, which are under the water (below the
lake's surface, inside its water volume) and how deep.
usage: py tools/water_mobs.py [secs]"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
from regress import cmd
secs = float(sys.argv[1]) if len(sys.argv) > 1 else 180
MC = ("Cow", "Pig", "Sheep", "Chicken", "Zombie", "Skeleton", "Creeper", "Spider", "Rabbit", "Horse", "Wolf", "Llama", "Witch", "Enderman")
TOP = -2.65
cmd("god 1"); cmd("dayfreeze 0")
cmd("tp 0 3 0.5"); time.sleep(2.0)
for i, name in enumerate(("Cow", "Pig", "Sheep", "Zombie", "Skeleton", "Creeper")):
    cmd(f"look {150 + i * 12} 0"); time.sleep(0.2); print(name, cmd(f"spawnhere {name} 5")[:60])
cmd("tp 0 30 -40"); time.sleep(1.0)  # (out of their way: watching, not luring)
t0 = time.time(); deepest = {}
while time.time() - t0 < secs:
    under = []
    for e in cmd("enemies").split(" ; "):
        m = re.match(r"\s*([A-Za-z ]+?)@([-\d.]+),([-\d.]+),([-\d.]+) .*?dead=(\w+)", e)
        if not m or m.group(5) != "False" or not any(m.group(1).startswith(n) for n in MC): continue
        x, y, z = float(m.group(2)), float(m.group(3)), float(m.group(4))
        wet = re.search(r"wet=([\d.]+)", e); wet = float(wet.group(1)) if wet else 0.0
        hh = re.search(r" h=([\d.]+) lift=([-\d.]+)", e); height = float(hh.group(1)) if hh else 1.0; lift = float(hh.group(2)) if hh else 0.0
        if wet > max(1.0, height * 0.9):  # (its head under)
            mk = re.search(r"mask=(-?\d+)", e); onnav = re.search(r"onNav=(\S+)", e)
            under.append(f"{m.group(1)} y {y:.1f} depth {wet:.1f} (h {height:.1f} lift {lift:.1f}) @{x:.0f},{z:.0f} mask {mk.group(1) if mk else '?'} onNav {onnav.group(1) if onnav else '?'}")
            deepest[m.group(1)] = min(deepest.get(m.group(1), 0), y)
    print(f"{time.time() - t0:5.0f}s under water: {', '.join(under) or '-'}")
    time.sleep(5.0)
print("deepest:", deepest)
# and how many were floating (lifted) at some point

