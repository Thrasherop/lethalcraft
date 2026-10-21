"""1.58's new things with two players (run tools/mp.sh first: host 28771, client 28772): an armored zombie's health the
same on both machines and its hits counted the same; the client puts a head down and breaks it; the client shoots a
crossbow rocket; the Killer Bunny bites the client; a creeper climbs a ladder to the client.
usage: py tools/owner_mp.py"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd

H, C = 28771, 28772
ok = []
def check(name, cond, detail=""):
    ok.append(bool(cond)); print(f"  [{'PASS' if cond else 'FAIL'}] {name}  ({str(detail)[:300]})", flush=True)
def state(port): return cmd("state", port)
def pos(port): return tuple(map(float, re.search(r"pos=([-\d.]+),([-\d.]+),([-\d.]+)", state(port)).groups()))
def hp(port): return int(re.search(r"hp=(\d+)", state(port)).group(1))
def can_act(port, t=6.0):
    t0 = time.time()
    while time.time() - t0 < t and "canAct=True" not in cmd("flags", port): time.sleep(0.2)
def select(port, name):
    hb = re.search(r"slots=\[([^\]]*)\]", state(port)).group(1).split(",")
    i = next((i for i, s in enumerate(hb) if s.startswith(name)), None)
    if i is not None: cmd(f"slot {i}", port); time.sleep(0.4); can_act(port)
    return i
def mob_line(port, name):
    return next((x.strip() for x in cmd("enemies", port).split(" ; ") if x.strip().lower().startswith(name)), None)

print("- land; both players outside, a few metres apart")
def land():
    cmd("route 0", H)
    for _ in range(40):
        time.sleep(3)
        if "landed=True" in cmd("leave_check", H): return True
        if "Experimentation" in cmd("state", H): cmd("land", H)
    return False
check("the ship lands", land(), cmd("leave_check", H))
time.sleep(8)
for port in (H, C): cmd("dayfreeze 1", port); cmd("gamemode survival", port); cmd("clearinv", port); cmd("god 1", port)
cmd("mobspawns 0", H)
cmd("flatspot 6", H); time.sleep(2)
hx, hy, hz = pos(H)
cmd(f"tp {hx + 3:.2f} {hy + 0.3:.2f} {hz:.2f}", C); time.sleep(2)

print("- an armored zombie: the same health on both machines; the client's sword takes as many hits as it shows")
cmd("mobarmor 10", H)
for _ in range(8):
    cmd("clearenemies 300 all", H); cmd("enemy zombie 3", H); time.sleep(1.0)
    a = cmd("mobarmor", H).split("|")[1]
    if "=[" in a and "hp=4/" not in a: break
time.sleep(1.0)
host_a = cmd("mobarmor", H).split("|")[1].strip(); cli_a = cmd("mobarmor", C).split("|")[1].strip()
print("   host:", host_a, "| client:", cli_a)
hh = re.search(r"hp=(\d+)/", host_a); ch = re.search(r"hp=(\d+)/", cli_a)
check("the client sees the zombie's armor and the same health as the host", hh and ch and hh.group(1) == ch.group(1) and host_a.split("#")[1].split(" ")[0][-12:] == cli_a.split("#")[1].split(" ")[0][-12:], (host_a, cli_a))
cmd("invgive iron_sword 1", C); time.sleep(0.8); select(C, "Iron Sword")
hits = 0
for _ in range(30):
    z = mob_line(H, "zombie")
    if not z: break
    m = re.search(r"@([-\d.]+),([-\d.]+),([-\d.]+)", z)
    cx, cy, cz = pos(C)
    cmd(f"enemyto zombie {cx + 1.6:.2f} {cy:.2f} {cz:.2f}", H); time.sleep(0.4)
    z = mob_line(C, "zombie")
    if not z: break
    m = re.search(r"@([-\d.]+),([-\d.]+),([-\d.]+)", z)
    import math
    dx, dz = float(m.group(1)) - cx, float(m.group(3)) - cz
    cmd(f"look {math.degrees(math.atan2(dx, dz)):.1f} 15", C); time.sleep(0.3)
    cmd("mouse left 0.15", C); time.sleep(0.8); hits += 1
want = int(hh.group(1)) if hh else -1
check("killed by the client in as many sword hits as its health (iron sword: 1 a hit)", mob_line(H, "zombie") is None and abs(hits - want) <= 1, f"{hits} hits, health {want}")
cmd("mobarmor 1", H); cmd("clearenemies 300 all", H)

print("- the client puts a zombie head down; the host has it; broken by the client, the head comes back")
cmd("clearinv", C); cmd("invgive zombie_head 1", C); time.sleep(0.8); select(C, "Zombie Head")
cmd("look 90 45", C); time.sleep(0.5); cmd("rmb 0.1", C); time.sleep(1.2)
hn = [e for e in cmd("near 6", H).split(" ; ") if "placed_zombie_head" in e]
cn = [e for e in cmd("near 6", C).split(" ; ") if "placed_zombie_head" in e]
check("put down by the client, the head is a block on both machines", hn and cn, (hn, cn))
cmd("mouse left 2.5", C); time.sleep(1.5)
l = cmd("loose 6", H)
check("broken by the client, it drops the head", "zombie_head" in l and not [e for e in cmd("near 6", H).split(" ; ") if "placed_zombie_head" in e], l[:120])

print("- the client loads a crossbow with a rocket in the next slot and shoots it")
cmd("clearloose 10", H); cmd("clearinv", C); cmd("invgive crossbow 1", C); time.sleep(0.6); cmd("invgive firework_rocket 2", C); time.sleep(0.8)
select(C, "Crossbow")
r0 = int(re.search(r"xbowRockets=(\d+)", cmd("f244g", H)).group(1))
cmd("look 0 -10", C); time.sleep(0.3); cmd("rmb 2.0", C); time.sleep(2.5)
loaded = re.search(r"held=crossbow#(\d+)", cmd("playerheld", H).split("|")[-1])
cmd("rmb 0.1", C); time.sleep(2.5)
r1 = int(re.search(r"xbowRockets=(\d+)", cmd("f244g", H)).group(1))
check("the host shoots the client's rocket", r1 > r0, (r0, r1, cmd("playerheld", H)[-120:]))
check("one rocket used from the client's slot", "Firework Rocketx1" in state(C), state(C)[60:200])

print("- the Killer Bunny goes for the client and bites")
cmd("god 0", C); cmd("hp 100", C)
cx, cy, cz = pos(C)
cmd("enemy killerbunny 6", H); time.sleep(0.4); cmd(f"enemyto killerbunny {cx + 5:.2f} {cy:.2f} {cz:.2f}", H)
t0 = time.time(); bitten = False
while time.time() - t0 < 12:
    if hp(C) < 100: bitten = True; break
    time.sleep(0.2)
cmd("god 1", C); cmd("hp 100", C)
check("the Killer Bunny bites the client", bitten, (hp(C), mob_line(C, "the killer")))
cmd("clearenemies 300 all", H)

print("- a creeper climbs a ladder to the client on a pillar")
cmd("look 0 0", H); time.sleep(0.3)
for y in range(4): cmd(f"place cobblestone 0 {y} 2", H)
for dx, dz in ((1, 2), (0, 3), (1, 3)): cmd(f"place cobblestone {dx} 3 {dz}", H)
for y in range(4): cmd(f"place ladder 0 {y} 1 3", H)
time.sleep(3.0)
hx, hy, hz = pos(H)
cmd(f"tp {hx + 0.7:.2f} {hy + 4 * 1.4 + 0.5:.2f} {hz + 2.5 * 1.4:.2f}", C); time.sleep(2.0)
cp = pos(C)
cmd(f"tp {hx - 6:.2f} {hy:.2f} {hz - 4:.2f}", H); time.sleep(1.0)
cmd("enemy creeper 2", H); time.sleep(0.4)
top = -99
for _ in range(60):
    z = mob_line(C, "creeper")
    if not z: break
    top = max(top, float(re.search(r"@[-\d.]+,([-\d.]+),", z).group(1)))
    time.sleep(0.25)
check("the client sees the creeper climb the ladder towards it", top > cp[1] - 2.5, (top, cp))
cmd("clearenemies 300 all", H)

print(f"\n{sum(ok)}/{len(ok)} checks passed")
