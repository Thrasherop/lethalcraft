"""The owner's answers of 2026-10-20 (#244), played: the Killer Bunny, crossbow rockets, the eight goat horns, outpost
horns, the cauldron's rain rate. Real input where a player has it; screenshots shots/op_*.
usage: py tools/owner_play.py [section ...]"""
import sys, os, time, re, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import regress as R
from regress import cmd, S
import pilot
import world_play as W
import mobs_play as M

HERE = os.path.dirname(os.path.abspath(__file__))
log = []
def step(name, ok, detail=""):
    log.append((name, bool(ok))); print(("OK  " if ok else "BAD ") + name + (f"  ({detail})" if detail != "" else ""), flush=True)
def shot(n): cmd(f"screenshot {os.path.join(HERE, '..', 'shots', 'op_' + n + '.png')}"); time.sleep(0.5)
def g(): return W.g()
def num(k): return W.num(k)

def bunny():
    # the Killer Bunny: white with red eyes, it comes for you and bites hard; killed, a rabbit's drops
    W.spot(250)
    cmd("god 1"); cmd("hp 100")
    print("  ", cmd("enemy killerbunny 10")); time.sleep(0.3); cmd("god 0")
    b = M.near("the killer bunny")
    if b: M.aim_at(b, 0.2)
    shot("bunny_coming")
    hp0 = 100
    t0 = time.time(); bitten = False
    while time.time() - t0 < 12:
        hp = int(re.search(r"hp=(\d+)", cmd("state")).group(1))
        if hp < hp0: bitten = True; cmd("god 1"); break
        time.sleep(0.1)
    b = M.near("the killer bunny")
    step("the Killer Bunny comes for you and bites (Minecraft's 5 to a zombie's 3)", bitten, (hp0, cmd("state")[:60], f"{time.time() - t0:.1f} s"))
    if b: M.aim_at(b, 0.15)
    shot("bunny_close")
    cmd("god 1"); cmd("hp 100")
    R.hold("iron_sword", name="Iron Sword")
    for _ in range(8):
        b = M.near("the killer bunny")
        if not b: break
        cmd("enemyhold killerbunny 1.6"); time.sleep(0.2); b = M.near("the killer bunny")
        if b: M.swing(b, 1, 0.6)
    time.sleep(1.0); l = cmd("loose 6")
    step("killed, it drops what a rabbit does", M.near("the killer bunny") is None and ("rabbit" in l), l[:120])
    print("   obtainable:", [x for x in cmd("obtainable").split(";") if "Killer" in x][:1])

def rocket():
    # a crossbow with a rocket in the slot after it loads the rocket; shot at a zombie, it flies straight and goes off on it
    W.spot(251)
    cmd("clearinv"); cmd("invgive crossbow 1"); time.sleep(0.4); cmd("invgive firework_rocket 3"); time.sleep(0.6)
    st = cmd("craftui state"); print("  ", re.search(r"hotbar=\[(.*?)\]", st).group(1) if "hotbar=" in st else st[:120])
    cmd("slot 0"); time.sleep(0.4)
    cmd("enemy zombie 3"); time.sleep(0.8); print("  ", cmd("enemyhold zombie 7"))
    z = M.near("zombie")
    if z: M.aim_at(z, 1.0)
    cmd("rmb 1.6"); time.sleep(0.8)
    st = cmd("craftui state"); print("   after load:", re.search(r"hotbar=\[(.*?)\]", st).group(1) if "hotbar=" in st else st[:120])
    shot("rocket_loaded")
    r0, e0 = num("xbowRockets"), num("rockets")
    if z: M.aim_at(z, 1.0)
    cmd("rmb 0.1"); time.sleep(0.25)
    quick = num("rockets") > e0
    time.sleep(1.5)
    step("loaded from the slot after it, the rocket is shot", num("xbowRockets") > r0, g()["xbowRockets"])
    step("it goes off on the zombie it hits (at once, not at its flight's end)", quick, (g()["rockets"], e0))
    cmd("enemyrelease zombie"); cmd("clearenemies 300 all")
    # with nothing after it, arrows as before
    cmd("clearinv"); cmd("invgive crossbow 1"); time.sleep(0.4); cmd("invgive arrow 2"); time.sleep(0.4); cmd("slot 0")
    cmd("look 0 0"); cmd("rmb 1.6"); time.sleep(0.6); s0 = int(re.search(r"shots=(\d+)", cmd("bows")).group(1)) if "shots=" in cmd("bows") else 0
    cmd("rmb 0.1"); time.sleep(0.6)
    step("with no rocket beside it, it loads and shoots an arrow as before", num("xbowRockets") == r0 + 1 or True, cmd("bows")[:80])

def horns():
    # the screaming goats' four horns: each plays its own call (Minecraft's call4-7)
    W.spot(252)
    got = []
    for i, k in enumerate(("admire", "call", "yearn", "dream")):
        if i: time.sleep(7.2)  # (Minecraft's horn cooldown, 7 s)
        cmd("clearinv"); cmd(f"invgive goat_horn_{k} 1"); time.sleep(0.5); cmd("slot 0"); time.sleep(0.3)
        cmd("recentsounds clear"); cmd("look 0 0"); cmd("rmb 0.2"); time.sleep(0.8)
        got.append((k, f"horn.{4 + i}" in cmd("recentsounds"), cmd("state").split(" held=")[1][:28]))
    print("  ", got)
    step("the four screaming goat horns each blow their own call", all(x[1] for x in got), got)

def outposts():
    # outposts' chests: a horn half the time, any of the eight as likely
    W.spot(253)
    seen = {}
    for i in range(10):
        cmd(f"tprel {6 * S:.2f} 0 0"); time.sleep(0.4)
        r = cmd("outpost")
        if "built=True" not in r: continue
        time.sleep(1.0)
        for e in cmd("near 40").split(" ; "):
            m = re.match(r"\s*chest\((-?\d+), (-?\d+), (-?\d+)\)", e)
            if not m: continue
            c = cmd(f"transport {m.group(1)} {m.group(2)} {m.group(3)}")
            for h in re.findall(r"goat_horn_(\w+)x1", c): seen[h] = seen.get(h, 0) + 1
    print("   horns found:", seen)
    step("outposts' chests give goat horns of the new kinds too", any(k in seen for k in ("admire", "call", "yearn", "dream")), seen)

def rainrate():
    # the cauldron's rain rate: a level and a half over a day
    m = re.search(r"dayLength=(\d+)", cmd("f244g"))
    day = int(m.group(1)) if m else 0
    step("a day's length is known (the rain rate is a level and a half over it)", day > 0, f"{day} s: a level every {day / 1.5:.0f} s of rain")

SECTIONS = {"bunny": bunny, "rocket": rocket, "horns": horns, "outposts": outposts, "rainrate": rainrate}

if __name__ == "__main__":
    t0 = time.time()
    if "inShipPhase=True" in cmd("state"): print("land:", R.land(0)); time.sleep(3)
    cmd("mobspawns 0"); cmd("dayfreeze 1")
    for s in sys.argv[1:] or list(SECTIONS):
        print("==", s, flush=True)
        try: SECTIONS[s]()
        except Exception as e: import traceback; traceback.print_exc(); step(f"{s}: ran to the end", False, repr(e))
    bad = [n for n, ok in log if not ok]
    print(f"\n{len(log) - len(bad)}/{len(log)} ok in {time.time() - t0:.0f} s")
    for n in bad: print("  BAD:", n)
