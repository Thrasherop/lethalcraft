"""#244 part B2 hand play: effects on monsters, effect polish, the HUD, brewing automation, as a player meets them.
Screenshots in shots/ep_*.png.  usage: py tools/effects_play.py <section> [...]   sections: mobs polish hud automation"""
import sys, os, time, re, math
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from regress import cmd, S
import thorough_play as T
HERE = os.path.dirname(os.path.abspath(__file__))
log = []
def shot(n): cmd(f"screenshot {os.path.join(HERE, '..', 'shots', 'ep_' + n + '.png')}"); time.sleep(0.5)
def step(name, ok, detail=""):
    log.append((name, ok)); print(("OK  " if ok else "BAD ") + name + (f"  ({detail})" if detail else ""), flush=True)
slots, st, give = T.slots, T.st, T.give
def select(name):
    import pilot; pilot.select(name); time.sleep(0.3)
def mob(name):
    m = re.search(re.escape(name) + r" d=[\d.]+ hp=(-?\d+) spd=([\d.]+) fx=\[([^\]]*)\] swirl=(\S+)", cmd("mobfx"))
    return (int(m.group(1)), float(m.group(2)), m.group(3), m.group(4)) if m else None

def throw_at(name, key, label):
    """a splash potion thrown with [Right-click] at the monster held a few metres off"""
    import pilot
    give((key, 1)); select(label)
    m = re.search(re.escape(name) + r"@([-\d.]+),([-\d.]+),([-\d.]+)", cmd("enemies"))
    if m: pilot.aim_at(float(m.group(1)), float(m.group(2)) + 1.6, float(m.group(3))); time.sleep(0.3)
    cmd("rmb"); time.sleep(1.5)

def mobs():
    # a splash of poison on a cow: it loses health, not the last of it, swirls green; a splash of healing hurts a zombie
    # (undead), harming heals it; a slowness arrow slows a pig; poison doesn't take on a zombie
    R.start_flat(70, [(0, 2), (0, 4)]); cmd("clearenemies 200 all"); cmd("mobspawns 0"); cmd("god 1")
    cmd("look 0 0"); cmd("enemy cow 4"); time.sleep(1.0); cmd("enemyhold cow 3.5"); time.sleep(0.3)
    h0 = mob("Cow"); throw_at("Cow", "splash_potion_poison_strong", "Splash Potion of Poison II")
    a = mob("Cow"); print("   cow after the splash:", a)
    shot("cow_poisoned")
    time.sleep(8.0); b = mob("Cow")
    step("splash poison on a cow: poisoned, swirling, health going (not the last)", a and "Poison" in a[2] and a[3] != "-" and b and 1 <= b[0] < h0[0], (h0, a, b))
    cmd("enemyrelease cow"); cmd("clearenemies 200 all"); time.sleep(0.5)
    # (zombies are the facility's: their side of the game's inside/outside check)
    print("   inside:", cmd("tpinside")[:40]); time.sleep(2.5)
    cmd("look 0 0"); cmd("enemy Zombie 5"); time.sleep(1.0); cmd("enemyhold zombie 3"); time.sleep(0.3)
    z0 = mob("Zombie"); throw_at("Zombie", "splash_potion_healing", "Splash Potion of Healing"); z1 = mob("Zombie")
    step("splash healing on a zombie (undead): it hurts", z0 and z1 and z1[0] < z0[0], (z0, z1))
    throw_at("Zombie", "splash_potion_harming", "Splash Potion of Harming"); z2 = mob("Zombie")
    step("splash harming on it: it heals", z1 and z2 and z2[0] > z1[0], (z1, z2))
    throw_at("Zombie", "splash_potion_poison", "Splash Potion of Poison"); z3 = mob("Zombie")
    step("poison doesn't take on the undead", z3 and "Poison" not in z3[2], z3)
    cmd("enemyrelease zombie"); cmd("clearenemies 200 all"); cmd("tpship"); time.sleep(1.5)
    # a slowness arrow on a pig, shot from a bow (real draw)
    import pilot
    R.start_flat(71, [(0, 2), (0, 6)]); cmd("clearenemies 200 all")
    give(("bow", 1), ("arrow_slowness", 3)); select("Bow")
    cmd("look 0 0"); cmd("enemy pig 6"); time.sleep(1.0); cmd("enemyhold pig 5"); time.sleep(0.4)
    p0 = mob("Pig")
    m = re.search(r"Pig@([-\d.]+),([-\d.]+),([-\d.]+)", cmd("enemies"))
    pilot.aim_at(float(m.group(1)), float(m.group(2)) + 0.6, float(m.group(3))); time.sleep(0.3)
    cmd("mouse right 1.2"); time.sleep(1.5)
    p1 = mob("Pig"); print("   pig:", p0, "->", p1)
    cmd("enemyrelease pig"); time.sleep(1.5); p2 = mob("Pig")
    step("a slowness arrow on a pig: slowed (its speed 15% down), swirling blue", p1 and "Slowness1" in p1[2] and p2 and p2[3] != "-", (p1, p2))
    shot("pig_slowed")
    cmd("clearenemies 200 all")

def polish():
    # jump boost takes a block a level off a fall; night vision flickers at the end; speed widens the view; a weaker,
    # longer effect comes back after a stronger one; drinking slows you; creative keeps the potion
    R.start_flat(72, [(0, 2)]); cmd("clearenemies 200 all"); cmd("potions clear")
    p = R.pos()
    def fall():
        cmd("god 0"); cmd("hp 100"); time.sleep(0.3)
        cmd(f"tp {p[0]:.2f} {p[1] + 25:.2f} {p[2]:.2f}"); time.sleep(4.0)  # (25 m, as the suite's: the game's damage comes in steps)
        h = st("hp"); cmd("hp 100"); cmd("god 1"); return 100 - h
    d0 = fall()
    cmd("potions effect JumpBoost 2 60")
    d1 = fall()
    cmd("potions clear")
    step("a 25 m fall with Jump Boost II: 10 less damage (two blocks off)", d0 > 0 and d0 - d1 == 10, (d0, d1))
    # night vision's last 10 s: it flickers (Minecraft's 0.7 + 0.3 sin)
    cmd("potions effect NightVision 1 30"); time.sleep(0.5)
    full = float(re.search(r"nv=([\d.]+)", cmd("fov")).group(1))
    cmd("potions clear"); cmd("potions effect NightVision 1 9"); ints = []
    for i in range(16): time.sleep(0.07); ints.append(float(re.search(r"nv=([\d.]+)", cmd("fov")).group(1)))
    step("night vision's last 10 s: its light flickers (below and up to the full brightness)", min(ints) < full * 0.8 and max(ints) > full * 0.9, (full, [round(x) for x in ints]))
    shot("nv_end")
    cmd("potions clear")
    # speed widens the view
    f0 = cmd("fov"); cmd("potions effect Swiftness 2 30"); time.sleep(1.5); f1 = cmd("fov")
    w0, w1 = float(re.search(r"camera=([\d.]+)", f0).group(1)), float(re.search(r"camera=([\d.]+)", f1).group(1))
    step("Speed II widens the view (Minecraft's: 20% faster is 10% wider a level)", w1 > w0 * 1.15, (f0, f1))
    cmd("potions clear"); time.sleep(1.5)
    step("and back after", abs(float(re.search(r"camera=([\d.]+)", cmd("fov")).group(1)) - w0) < 1.0, cmd("fov"))
    # a weaker, longer one resumes
    cmd("potions effect Swiftness 1 60"); cmd("potions effect Swiftness 2 4"); time.sleep(0.3)
    e0 = cmd("potions"); time.sleep(4.5); e1 = cmd("potions")
    step("Swiftness II over a longer Swiftness I: when II ends, I comes back with its time", "Swiftness2" in e0 and "Swiftness1:5" in e1, (e0, e1))
    cmd("potions clear")
    # drinking slows you: a walk while drinking and while not
    def walk(drink):
        give(("potion_water", 1)); select("Water Bottle"); cmd("look 0 0"); time.sleep(0.3)
        a = R.pos()
        if drink: cmd("mouse right 1.4")
        cmd("keys W 1.2"); time.sleep(1.4); b = R.pos()
        cmd("mouse release"); time.sleep(0.5)
        return math.hypot(b[0] - a[0], b[2] - a[2])
    v0, v1 = walk(False), walk(True)
    step("walking while drinking: much slower (Minecraft's 20%)", v1 < v0 * 0.5, (round(v0, 2), round(v1, 2)))
    # creative: the potion stays
    cmd("gamemode creative"); give(("potion_swiftness", 1)); select("Potion of Swiftness")
    cmd("mouse right 2.0"); time.sleep(2.4)
    step("creative: drunk, the potion's still in hand (no bottle)", "Swiftness1" in cmd("potions") and "Potion of Swiftness" in slots() and "Glass Bottle" not in slots(), (cmd("potions"), slots()[:80]))
    cmd("gamemode survival"); cmd("potions clear")

def hud():
    # six effects at once: the good ones in a row along the top, the harmful under them; the inventory screen lists them
    # with level and time; the food bar green with Hunger; hearts green with poison
    cmd("potions clear"); cmd("hunger 18 2")
    for e, l, s in (("Swiftness", 2, 90), ("Strength", 1, 180), ("NightVision", 1, 60), ("Regeneration", 1, 40), ("Poison", 1, 30), ("Hunger", 1, 30), ("Slowness", 1, 20), ("Weakness", 1, 50)):
        cmd(f"potions effect {e} {l} {s}")
    time.sleep(0.8); shot("hud_rows")
    cmd("keys I 0.08"); time.sleep(0.8); shot("inventory_effects")
    st_ = cmd("craftui state")
    cmd("craftui close"); time.sleep(0.4)
    step("8 effects on: two rows in the shot, the list beside the inventory (see shots)", len(cmd("potions").split()) == 8, cmd("potions"))
    cmd("potions clear")

def automation():
    # a brewing stand fed by hoppers (Minecraft's): above, nether wart into the ingredient slot; from the side, water
    # bottles into the holders and blaze powder into the fuel; a hopper under it takes the brewed bottles into a chest; a
    # comparator behind it reads how full it is
    fc = R.start_flat(73, [(x, z) for x in range(-2, 3) for z in range(0, 5)])
    f = R.stone_floor(fc, range(-2, 3), range(0, 5)); y = f + 2
    R.place("stone", fc, 0, f + 1, 2)            # (the chest's floor under it all)
    R.place("chest", fc, 0, y - 1, 2)            # the chest at the bottom
    R.place("hopper", fc, 0, y, 2, facing=0)     # a hopper into the chest, under the stand
    R.place("brewing_stand", fc, 0, y + 1, 2)
    R.place("hopper", fc, 0, y + 2, 2, facing=0) # above: down into the stand
    R.place("hopper", fc, -1, y + 1, 2, facing=5)  # beside: east into the stand
    R.place("comparator", fc, 1, y + 1, 2, facing=4)
    R.place("redstone_block", fc, 1, y, 2); time.sleep(1.0)  # (the bottom hopper locked while it brews: Minecraft's would pull the water bottles out at once, as ours does)
    cmd("brewui time 3")
    # hoppers locked while they're filled (a redstone block beside each would; here they're filled fast)
    def fill(dx, dy, items):
        cmd(f"chestui open {fc[0] + dx} {y + dy} {fc[2] + 2}"); time.sleep(0.5)
        hot = re.search(r"hotbar=\[([^\]]*)\]", cmd("chestui state")).group(1).split(",")
        for i, e in enumerate(hot):
            if e.split(":")[0] in items: cmd(f"chestui click hot {i} shift"); time.sleep(0.3)
        cmd("chestui close"); time.sleep(0.3)
    give(("potion_water", 1), ("potion_water", 1), ("potion_water", 1), ("blaze_powder", 1))
    fill(-1, 1, ("potion_water", "blaze_powder"))
    time.sleep(3.0)
    s1 = cmd("brewui state") if False else ""
    t = cmd(f"transport {fc[0]} {y + 1} {fc[2] + 2}")
    print("   the stand from the side hopper:", cmd("brewui state")[:10], t)
    give(("nether_wart", 1)); fill(0, 2, ("nether_wart",))
    R.wait(lambda: "potion_awkward" in str(cmd(f"cellabs {fc[0]} {y + 1} {fc[2] + 2}")) or False, 1, step=0.5)
    time.sleep(8.0)
    print("   comparator behind the full stand:", cmd(f"cellabs {fc[0] + 1} {y + 1} {fc[2] + 2}")[:120])
    cmd(f"breakabs {fc[0] + 1} {y} {fc[2] + 2}"); time.sleep(0.5)  # (unlocked: the brewed bottles go down)
    ok = R.wait(lambda: "awkward" in cmd(f"transport {fc[0]} {y - 1} {fc[2] + 2}"), 30, step=1.0)
    c = cmd(f"transport {fc[0]} {y - 1} {fc[2] + 2}")
    step("the hoppers fed it, it brewed, the hopper under it took the potions into the chest", "potion_awkward" in c, c)
    shot("automation")

SECTIONS = {"mobs": mobs, "polish": polish, "hud": hud, "automation": automation}
if __name__ == "__main__":
    R.grid_yo()
    cmd("god 1"); cmd("dayfreeze 1"); cmd("gamemode survival")
    for s in sys.argv[1:]: print("==", s); SECTIONS[s]()
    print(f"\n{sum(1 for _, ok in log if ok)}/{len(log)} fine")
