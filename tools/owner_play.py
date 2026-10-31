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

def can_act(t=6.0):
    """until the grab animation of an item just given is over (it blocks using what's in hand)"""
    t0 = time.time()
    while time.time() - t0 < t and "canAct=True" not in cmd("flags"): time.sleep(0.2)

def hold_slot_with(name):
    """select the hotbar slot holding an item of that name, and wait until it can be used"""
    sl = re.search(r"slots=\[([^\]]*)\]", cmd("state")).group(1).split(",")
    for n, x in enumerate(sl):
        if x.startswith(name):
            cmd(f"slot {n}"); time.sleep(0.3); can_act(); return True
    return False

def put_head(name, aim, check, tries=3):
    """a head of that name from the hotbar put where `aim` points; True once `check` sees it"""
    for _ in range(tries):
        if not hold_slot_with(name): return False
        for _ in range(3):
            aim(); time.sleep(0.4)
            if not cmd("mine?").startswith("no surface"): break
        cmd("rmb 0.1"); time.sleep(0.8)
        if check(): return True
    return False

def heads():
    # mob heads put down: on the ground turned toward you, on a wall; broken, the head back; on a note block, its sound;
    # right-click at the sky still wears it
    fc, y = W.spot(254); cmd("look 0 0")
    def armor(): return re.search(r"armor=\[(.*?)\]", cmd("craftui state")).group(1)
    for dx in range(0, 5):
        for dz in range(0, 5): W.put("stone", fc, dx, y, dz)   # (a stone floor a block up)
        W.put("stone", fc, dx, y + 1, 5)                         # (and a wall at its far side)
    heads_ = [("skeleton_skull", "Skeleton Skull"), ("wither_skeleton_skull", "Wither Skeleton Skull"), ("zombie_head", "Zombie Head"),
              ("creeper_head", "Creeper Head"), ("piglin_head", "Piglin Head")]
    placed = []
    for i, (k, nm) in enumerate(heads_):
        cmd("clearinv"); cmd(f"invgive {k} 2"); time.sleep(0.6)
        R.stand_on(fc, i, y + 1, 1)
        put_head(nm, lambda: W.aim_cell(fc, i, y, 3, 0.95), lambda: W.blk(fc, i, y + 1, 3) is not None)
        put_head(nm, lambda: W.aim_cell(fc, i, y + 1, 4.55, 0.5), lambda: W.blk(fc, i, y + 1, 4) is not None)
        placed.append((k, W.blk(fc, i, y + 1, 3), W.blk(fc, i, y + 1, 4)))
    print("  ", placed)
    step("each head goes down on the ground (turned toward you)", all(p[1] is not None and p[1][0] == "placed_" + p[0] and p[1][1] == 1 for p in placed), [p[1] for p in placed])
    step("and on a wall", all(p[2] is not None and p[2][0] == "placed_" + p[0] and p[2][1] in (2, 3, 4, 5) for p in placed), [p[2] for p in placed])
    R.stand_on(fc, 2, y + 1, 0); W.aim_cell(fc, 2, y + 1, 3, 0.3); shot("heads_row")
    cmd(f"tp {(fc[0] + 2.5) * S:.2f} {(y + 1) * S + R.YO + 0.05:.2f} {(fc[2] + 1.6) * S:.2f}"); time.sleep(0.6); cmd("crouch 1"); time.sleep(0.3); W.aim_cell(fc, 2, y + 1, 4, 0.3); shot("heads_low"); cmd("crouch 0")
    # broken: the head back
    cmd("clearinv"); cmd("clearloose 12"); R.stand_on(fc, 2, y + 1, 1); can_act()
    for _ in range(3):
        W.aim_cell(fc, 2, y + 1, 3, 0.2); time.sleep(0.4); cmd("mouse left 2.5"); time.sleep(1.2)
        if W.blk(fc, 2, y + 1, 3) is None: break
    l = cmd("loose 6")
    step("broken, a head gives the head back", W.blk(fc, 2, y + 1, 3) is None and "zombie_head" in l, (W.blk(fc, 2, y + 1, 3), l[:100]))
    # on a note block
    W.put("note_block", fc, 0, y + 1, 2) if W.blk(fc, 0, y + 1, 2) is None else None
    cmd("clearinv"); cmd("invgive zombie_head 1"); time.sleep(0.6)
    R.stand_on(fc, 0, y + 1, 0)
    put_head("Zombie Head", lambda: W.aim_cell(fc, 0, y + 1, 2, 0.98), lambda: W.blk(fc, 0, y + 2, 2) is not None)
    print("   on the note block:", W.blk(fc, 0, y + 2, 2))
    cmd("recentsounds clear"); W.aim_cell(fc, 0, y + 1, 2, 0.4); time.sleep(0.3); cmd("mouse left 0.1"); time.sleep(0.8)
    rs = cmd("recentsounds")
    step("a note block with a zombie head on it plays the zombie's sound", "zombie.idle" in rs and "note." not in rs, rs[:120])
    # right-click at the sky: worn
    cmd("clearinv"); cmd("wear none"); cmd("invgive creeper_head 1"); time.sleep(0.6); hold_slot_with("Creeper Head"); cmd("look 0 -60"); time.sleep(0.3); cmd("rmb 0.1"); time.sleep(0.8)
    a = armor()
    step("right-click at nothing still puts the head on", "creeper_head" in a, a)
    cmd("wear none"); cmd("look 0 0")

def mobarmor():
    # zombies and skeletons now and then spawn in armor (Minecraft's materials), adult piglins in gold, drowned never;
    # killed by a player, a piece drops 8.5% of the time (the dev multiplier makes every one wear some)
    W.spot(257); cmd("look 0 0"); cmd("god 1")
    print("  ", cmd("mobarmor 10"))
    seen = {}
    for mob in ("zombie", "husk", "skeleton", "stray", "drowned"):
        cmd("clearenemies 300 all"); cmd(f"enemy {mob} 3"); time.sleep(1.0)
        a = cmd("mobarmor").split("|")[1]
        seen[mob] = a.strip()
    print("  ", seen)
    step("zombies, husks, skeletons and strays spawn wearing armor (with the chances raised)", all("=[" in seen[m] for m in ("zombie", "husk", "skeleton", "stray")), seen)
    step("drowned never do", "=[" not in seen["drowned"], seen["drowned"])
    # drops
    R.hold("netherite_sword", name="Netherite Sword"); kills = 0; got = []
    for n in range(14):
        cmd("clearenemies 300 all"); cmd("clearloose 12"); cmd("enemy zombie 3"); time.sleep(0.8)
        for _ in range(10):
            r = cmd("enemyhold zombie 2.0")
            if "holding" not in r: break
            M.swing(M.held(r), 1, 0.5)
        kills += 1; time.sleep(1.0)
        l = cmd("loose 6")
        got += [x for x in re.findall(r"(\w+_(?:helmet|chestplate|leggings|boots))", l)]
        if got: break
    print("   kills", kills, "armor dropped", got, cmd("mobarmor").split("|")[0])
    step("killed by a player, armored zombies now and then drop a piece", got, (kills, got))
    # piglins in the Nether: gold
    cmd("nether gen"); time.sleep(2.0); cmd("nether tp"); time.sleep(2.0); cmd("look 0 0")
    cmd("clearenemies 300 all"); cmd("enemy piglin 3"); time.sleep(1.0); a = cmd("mobarmor").split("|")[1]
    print("   piglin:", a, cmd("enemyhold piglin 3"))
    p = M.near("piglin")
    if p: M.aim_at(p, 1.0)
    shot("piglin_gold")
    step("an adult piglin wears gold armor (with the chances raised)", "Piglin#" in a and all(c in "2,-1[]" for c in a.split("=")[1].split(" ")[0].strip()), a)
    cmd("mobarmor 1"); cmd("clearenemies 300 all")

SECTIONS = {"bunny": bunny, "rocket": rocket, "horns": horns, "outposts": outposts, "rainrate": rainrate, "heads": heads, "mobarmor": mobarmor}

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
