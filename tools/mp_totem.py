"""Two-instance check of the Totem of Undying's effect: the client's totem goes off outside; both instances get the
burst and Minecraft's totem sound where the client was about to die and where it arrives in the ship.
Run tools/mp.sh first (host on 28771, client on 28772). usage: py tools/mp_totem.py"""
import sys, os, time, re, math
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd

H, C = 28771, 28772
ok = []

def check(name, cond, detail=""):
    ok.append(bool(cond))
    print(f"  [{'PASS' if cond else 'FAIL'}] {name}  ({str(detail)[:220]})")

def pos(port): return tuple(map(float, re.search(r"pos=([-\d.]+),([-\d.]+),([-\d.]+)", cmd("state", port)).groups()))
def dist(a, b): return math.dist(a, b)
def pops(port):
    out = []
    for m in re.finditer(r"died=([-\d.]+),([-\d.]+),([-\d.]+) arrive=([-\d.]+),([-\d.]+),([-\d.]+)", cmd("totem pops", port)):
        v = list(map(float, m.groups())); out.append((tuple(v[:3]), tuple(v[3:])))
    return out

print("- the sound is Minecraft's own")
for port in (H, C):
    si = cmd("soundinfo totem", port)
    check(f"{'host' if port == H else 'client'}: the totem sound is loaded from Minecraft", "Minecraft's file" in si, si)

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
for port in (H, C): cmd("dayfreeze 1", port); cmd("gamemode survival", port); cmd("clearinv", port)
cmd("flatspot 4", H); time.sleep(2)
hx, hy, hz = pos(H)
cmd(f"tp {hx + 3:.2f} {hy + 0.3:.2f} {hz:.2f}", C); time.sleep(2)
cmd("look 90 0", H)  # (the host faces the client)
cp0 = pos(C)
print("  host", (hx, hy, hz), "client", cp0)

print("- the client, holding a totem in its hotbar, takes a deadly hit")
cmd("invgive totem_of_undying 1", C); time.sleep(2.5)
check("the client has a totem", "Totem" in cmd("state", C), cmd("state", C)[60:160])
for port in (H, C): cmd("soundinfo recent", port)  # (clears nothing; read below by what's new)
before = {port: len(pops(port)) for port in (H, C)}
cmd("god 0", C); cmd("hurt 200 Mauling", C); time.sleep(0.25)
cmd(f"screenshot {os.path.abspath(os.path.join(os.path.dirname(__file__), '..', 'shots', 'totem_host_deathspot.png'))}", H)
time.sleep(2.0)
st = cmd("state", C)
check("the client lives, at full health", "dead=False" in st and "hp=100" in st, st[:80])
cp1 = pos(C)
check("and is back in the ship", "inShip=True" in st or dist(cp1, cp0) > 15, f"{cp0} -> {cp1}")
for port, who in ((H, "host"), (C, "client")):
    p = pops(port)
    new = p[before[port]:]
    check(f"{who} gets one burst", len(new) == 1, cmd("totem pops", port))
    if not new: continue
    died, arrive = new[0]
    check(f"{who}: it goes off where the client was", dist(died, cp0) < 2.0, f"died={died} client was at {cp0}")
    check(f"{who}: and where it arrives in the ship", dist(arrive, cp1) < 2.5, f"arrive={arrive} client now at {cp1}")
    rec = cmd("soundinfo recent", port)
    sounds = [tuple(map(float, m.groups())) for m in re.finditer(r"totem@([-\d.]+),([-\d.]+),([-\d.]+)", rec)]
    check(f"{who}: the totem sound plays at both places", any(dist(s, died) < 2.0 for s in sounds) and any(dist(s, arrive) < 2.0 for s in sounds), rec[-200:])
cmd("god 1", C)
print(f"{sum(ok)}/{len(ok)} checks passed")
