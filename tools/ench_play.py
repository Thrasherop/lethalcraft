"""#244 part C hand play: every enchantment doing its thing, XP orbs, durability, trims, as a player meets them.
Screenshots in shots/ec_*.png.  usage: py tools/ench_play.py <section> [...]
sections: table anvil sword bane looting mining armor water orbs mending worn bow rod flint grid trims"""
import sys, os, time, re, math
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from regress import cmd, S
import thorough_play as T
HERE = os.path.dirname(os.path.abspath(__file__))
log = []
def shot(n): cmd(f"screenshot {os.path.join(HERE, '..', 'shots', 'ec_' + n + '.png')}"); time.sleep(0.5)
def step(name, ok, detail=""):
    log.append((name, ok)); print(("OK  " if ok else "BAD ") + name + (f"  ({detail})" if detail else ""), flush=True)
slots, st, give, held_mob, aim_cell = T.slots, T.st, T.give, T.held_mob, T.aim_cell
def select(name):
    import pilot; pilot.select(name); time.sleep(0.3)
def mob(name):
    m = re.search(re.escape(name) + r" d=([\d.]+) hp=(-?\d+) spd=([\d.]+) fx=\[([^\]]*)\]", cmd("mobfx"))
    return (int(m.group(2)), m.group(4), float(m.group(1))) if m else None
def stats(): return dict(kv.split("=", 1) for kv in cmd("ench244").split(" ") if "=" in kv)
def enchant(*kl): return cmd("enchant " + " ".join(str(x) for x in kl))
def hit(n=1, gap=0.9):
    for _ in range(n): cmd("mouse left 0.04"); time.sleep(gap)
def fresh_zombie(dist=1.8):
    cmd("clearenemies 200 all"); time.sleep(0.3); cmd("look 0 0"); cmd("enemy Zombie 3"); time.sleep(1.2)
    return held_mob("zombie", dist)

def sword():
    # Smite on a zombie (the undead) against a plain sword; Fire Aspect sets it burning, flames on it, health going;
    # Knockback pushes it off; Sweeping Edge catches the zombie next to the one struck. Real left clicks, inside.
    cmd("god 1"); cmd("mobspawns 0"); cmd("gamemode survival")
    print("   inside:", cmd("tpinside")[:40]); time.sleep(2.5)
    give(("iron_sword", 1)); select("Iron Sword")
    fresh_zombie(); z0 = mob("Zombie"); hit(); z1 = mob("Zombie")
    plain = z0[0] - z1[0] if z0 and z1 else None
    print("   plain iron sword on a zombie:", z0, "->", z1)
    enchant("Smite", 5)
    fresh_zombie(); s0 = mob("Zombie"); hit(); s1 = mob("Zombie")
    smite = s0[0] - (s1[0] if s1 else 0) if s0 else None
    step("Smite V hits a zombie harder than a plain sword (+2.5 a hit here: Minecraft's +12.5 / 5)", plain is not None and smite is not None and smite > plain, f"plain {plain}, smite {smite} ({s0} -> {s1})")
    enchant("FireAspect", 2)
    fresh_zombie(); f0 = mob("Zombie"); hit(); f1 = mob("Zombie")
    time.sleep(0.5); shot("zombie_burning")
    time.sleep(3.5); f2 = mob("Zombie")
    step("Fire Aspect II: the zombie burns (Fire on it, 8 s), flames on it, health going after the hit", f1 and "Fire" in f1[1] and f2 and f2[0] < f1[0], (f0, f1, f2))
    enchant("Knockback", 2)
    fresh_zombie(); cmd("enemyrelease zombie"); time.sleep(0.2)
    k0 = mob("Zombie"); hit(gap=0.6); k1 = mob("Zombie")
    step("Knockback II pushes the zombie off (further from me)", k0 and k1 and k1[2] - k0[2] > 1.0, (k0, k1))
    cmd("enemyrelease zombie"); cmd("clearenemies 200 all"); cmd("tpship"); time.sleep(1.5)

def sweep():
    # Sweeping Edge: the cow beside the one struck is caught by the sweep (outside: zombies are the facility's, and a
    # second one walks into the swing itself)
    import pilot
    R.start_flat(82, [(0, 2), (1, 2), (0, 4)]); cmd("clearenemies 200 all"); cmd("mobspawns 0"); cmd("god 1")
    give(("iron_sword", 1)); select("Iron Sword")
    enchant("SweepingEdge", 3)
    # (one held 3.4 m off, behind the one struck: past the swing's arc, within Minecraft's sweep of a block around it)
    cmd("look 0 0"); cmd("enemy cow 3"); time.sleep(1.2); cmd("enemyhold cow 3.4"); time.sleep(0.3)
    cmd("enemy cow 2"); time.sleep(1.2); z = held_mob("cow", 1.8)
    sw0 = int(stats()["swept"]); a = cmd("mobfx"); hit(gap=0.3); b = cmd("mobfx")
    sw1 = int(stats()["swept"])
    print("   before:", a); print("   after:", b)
    step("Sweeping Edge III: the cow beside the one struck is caught by the sweep", sw1 > sw0, f"swept {sw0} -> {sw1}")
    shot("sweep")
    cmd("enemyrelease cow"); cmd("clearenemies 200 all")

def bane():
    # Bane of Arthropods on a spider: harder than plain, and it's slowed (Slowness IV) for a moment
    R.start_flat(80, [(0, 2), (0, 4)]); cmd("clearenemies 200 all"); cmd("mobspawns 0"); cmd("god 1")
    give(("iron_sword", 1)); select("Iron Sword")
    cmd("look 0 0"); cmd("enemy spider 3"); time.sleep(1.2); held_mob("spider", 2.0)
    p0 = mob("Spider"); hit(); p1 = mob("Spider")
    plain = p0[0] - p1[0] if p0 and p1 else None
    enchant("BaneOfArthropods", 1)  # (V kills a spider outright: I, to see it slowed)
    cmd("clearenemies 200 all"); time.sleep(0.3); cmd("look 0 0"); cmd("enemy spider 3"); time.sleep(1.2); held_mob("spider", 2.0)
    b0 = mob("Spider"); hit(gap=0.2); b1 = mob("Spider")
    print("   spider plain:", p0, "->", p1, " bane:", b0, "->", b1)
    step("Bane of Arthropods I: a spider takes more than from a plain sword, and is slowed (Slowness IV)", plain is not None and b0 and b1 and b0[0] - b1[0] > plain and "Slowness4" in b1[1], (plain, b0, b1))
    cmd("enemyrelease spider"); cmd("clearenemies 200 all")

def looting():
    # Looting III on cows: more leather and beef than a plain sword, on the whole (Minecraft's: up to 3 more of each)
    def kills(n):
        extra = 0
        for _ in range(n):
            cmd("clearenemies 200 all"); time.sleep(0.3); cmd("look 0 0"); cmd("enemy cow 3"); time.sleep(1.2); held_mob("cow", 2.0)
            l0 = int(stats()["looted"])
            for _ in range(6):
                if not mob("Cow"): break
                hit(gap=0.7)
            time.sleep(1.0); extra += int(stats()["looted"]) - l0
        return extra / n
    R.start_flat(81, [(0, 2), (0, 4)]); cmd("clearenemies 200 all"); cmd("mobspawns 0"); cmd("god 1")
    give(("iron_sword", 1)); select("Iron Sword")
    plain = kills(4)
    l0 = int(stats()["looted"])
    enchant("Looting", 3)
    loot = kills(4)
    l1 = int(stats()["looted"])
    step("Looting III: a cow killed drops more (up to 3 more of each: about 2.5 more a cow), none more to a plain sword", plain == 0 and loot >= 1.5, f"extra a cow: plain {plain:.1f}, looting {loot:.1f}")
    cmd("clearenemies 200 all")

def mine_one(fc, key, dx, y, dz, secs=6.0):
    R.place(key, fc, dx, y, dz); time.sleep(0.5)
    aim_cell(fc, dx, y, dz)
    cmd(f"mouse left {secs}"); R.wait(lambda: R.block_at(fc, dx, y, dz) is None, secs, step=0.1); cmd("mouse release"); time.sleep(1.2)

def loose_named(q):
    return sum(int(m.group(1)) if m.group(1) else 1 for m in re.finditer(r"x?(\d+)?@\S+ held=False", cmd("find " + q)))

def mining():
    # Silk Touch: an ore, grass and glass come as themselves, no XP from the ore; Fortune off by default (the owner's):
    # coal 1 a block; turned on, more
    fc = R.start_flat(83, [(x, 2) for x in range(0, 4)]); sy = R.surface(fc, 0, 2)
    cmd("gamemode survival"); cmd("god 1")
    give(("iron_pickaxe", 1)); select("Iron Pickaxe"); enchant("SilkTouch", 1)
    s0 = int(stats()["silk"]); x0 = st("xp")
    for i, key in enumerate(("iron_ore", "coal_ore", "grass", "glass")): mine_one(fc, key, i % 4, sy + 1, 2)
    time.sleep(2.0)
    got = {q: loose_named(q) for q in ("raw iron", "coal ore", "grass", "glass")}
    print("   silk drops:", got, "xp", x0, "->", st("xp"))
    step("Silk Touch: coal ore, grass, glass drop as the block itself; iron ore its raw iron (the mod's ore scrap)", all(v >= 1 for v in got.values()) and int(stats()["silk"]) - s0 == 3, got)
    step("Silk Touch: no XP from the coal ore", st("xp") == x0, (x0, st("xp")))
    shot("silk_drops")
    give(("iron_pickaxe", 1)); select("Iron Pickaxe"); enchant("Fortune", 3)
    def coal_from(n):
        c0 = loose_named("coal")
        for k in range(n): mine_one(fc, "coal_ore", k % 4, sy + 1, 2)
        time.sleep(1.5); return loose_named("coal") - c0
    off = coal_from(4)
    step("Fortune III with the config off (the default): 1 coal a block", off == 4, off)
    cmd("fortune 1"); f0 = int(stats()["fortune"])
    on = coal_from(6)
    cmd("fortune 0")
    step("Fortune III with the config on: more coal (Minecraft's: 2.2 a block on average)", on > 6 and int(stats()["fortune"]) - f0 == 6, on)

def pool(fc, f, size=3, deep=3):
    """a pool `deep` blocks deep at floor height f: stone walls round it, water sources inside"""
    for y in range(f, f + deep):
        for dx in range(-1, size + 1):
            for dz in range(-1, size + 1):
                if 0 <= dx < size and 0 <= dz < size: cmd(f"placeabs water {fc[0] + dx} {y} {fc[2] + dz} 1")
                else: R.place("stone", fc, dx, y, dz)
    time.sleep(1.5)

def armor():
    # Protection on all four: a zombie's real hits do less; Thorns hurts it back; the armor wears with the hits (its bar)
    # and a worn-out piece breaks; Feather Falling on a real fall
    cmd("god 0"); cmd("hp 100"); cmd("mobspawns 0"); cmd("gamemode survival")
    print("   inside:", cmd("tpinside")[:40]); time.sleep(2.5)
    cmd("wear iron_helmet iron_chestplate iron_leggings iron_boots"); time.sleep(0.5)
    LOG = r"O:/SteamLibrary/steamapps/common/Lethal Company/BepInEx/LogOutput.log"
    def log_hits():
        # (the armor's own log of each hit it took: the zombie's real blows, before and after)
        return re.findall(r"\[dev\] armor \d+ pts: Bludgeoning damage (\d+) -> (\d+)", open(LOG, encoding="utf-8", errors="replace").read())
    def zombie_hits(secs=9.0):
        cmd("clearenemies 200 all"); time.sleep(0.3); cmd("look 0 0"); cmd("hp 100"); cmd("hunger 20 5"); n0 = len(log_hits())
        cmd("enemy Zombie 3"); time.sleep(1.2); cmd("enemyhold zombie 1.5"); time.sleep(0.2); cmd("enemyrelease zombie")
        t0 = time.time()
        while time.time() - t0 < secs: time.sleep(0.8); cmd("hp 100")  # (kept alive through the blows)
        return log_hits()[n0:]
    plain = zombie_hits()
    for s_ in range(4): cmd(f"wornench {s_} Protection 4")
    prot = zombie_hits()
    print("   a zombie's blows on iron armor:", plain, "then with Protection IV x4:", prot)
    # (the log is written a moment late: the first zombie's last blows can show up after the enchanting)
    step("Protection IV on all four: a zombie's real blows do less (20 -> 15 in iron; with it 64% less again, Minecraft's: 5)", plain and len(prot) >= 3 and all(b == "15" for _, b in plain) and all(b == "5" for _, b in prot[-3:]), (plain, prot))
    w = stats()["worn"]; print("   worn:", w)
    used = sum(int(x) for x in re.findall(r"\((\d+)/\d+\)", w))
    step("the armor wore from the blows (by sixteenths, by chance: Minecraft's life for it)", used > 0, w)
    for s_ in range(4): cmd(f"wornench {s_} Thorns 3")
    t0 = int(stats()["thorned"]); th = zombie_hits(12.0)
    t1 = int(stats()["thorned"]); zz = mob("Zombie")
    step("Thorns III on all four: the zombie gets hurt back (Minecraft's 45% a piece)", t1 > t0, f"blows {len(th)}, thorns {t0} -> {t1}, zombie {zz}")
    cmd("clearenemies 200 all")
    # a piece worn nearly out breaks on the next hit (the Thorns off: it would kill the zombie first)
    for s_ in range(4): cmd(f"wornench {s_}")
    cmd("wornwear 0 160"); b0 = int(stats()["armorBroken"])
    cmd("look 0 0"); cmd("hp 100"); cmd("enemy Zombie 3"); time.sleep(1.2); cmd("enemyhold zombie 1.5"); time.sleep(0.2); cmd("enemyrelease zombie")
    for _ in range(60):  # (its last sixteenth: about 10 blows, a point each, as Minecraft's last 10 durability)
        time.sleep(0.8); cmd("hp 100")
        if int(stats()["armorBroken"]) > b0: break
    shot("helmet_broke")
    step("a helmet worn nearly out breaks on a hit (gone from the head, a toast and the break sound)", int(stats()["armorBroken"]) > b0 and stats()["worn"].startswith("-"), stats()["worn"])
    cmd("clearenemies 200 all"); cmd("hp 100"); cmd("tpship"); time.sleep(1.5)
    # a real fall: Feather Falling IV on the boots (12% a level off), 25 m
    R.start_flat(84, [(0, 2)]); cmd("god 0")
    p = R.pos()
    def fall():
        cmd("hp 100"); cmd("hunger 20 5"); time.sleep(0.3); cmd(f"tp {p[0]:.2f} {p[1] + 25:.2f} {p[2]:.2f}"); time.sleep(4.0)
        return 100 - st("hp")
    cmd("wear iron_helmet iron_chestplate iron_leggings iron_boots"); time.sleep(0.4)
    falls = lambda: re.findall(r"\[dev\] fall epf (\d+): damage (\d+) -> (\d+)", open(LOGF, encoding="utf-8", errors="replace").read())
    n0 = len(falls()); d0 = fall()
    cmd("wornench 3 FeatherFalling 4"); d1 = fall()
    R.wait(lambda: len(falls()) >= n0 + 2, 8.0, step=0.5)
    fl = falls()[n0:]
    print("   25 m falls:", d0, "plain,", d1, "with Feather Falling IV; the armor's log:", fl)
    # (the game's own fall damage differs from fall to fall: the log's before and after for each)
    step("Feather Falling IV: a real 25 m fall hurts 48% less (Minecraft's 12% a level)", fl and fl[-1][0] == "12" and abs(int(fl[-1][2]) - round(int(fl[-1][1]) * 0.52)) <= 1, fl)
    cmd("hp 100"); cmd("god 1"); cmd("wear none")

def water():
    # Respiration keeps your breath longer; Aqua Affinity mines at the usual speed under water (5 times slower without);
    # Depth Strider walks through water as on land
    fc = R.start_flat(85, [(dx, dz) for dx in range(-1, 5) for dz in range(-1, 5)]); cmd("god 1"); cmd("gamemode survival")
    f = R.stone_floor(fc, range(-1, 5), range(-1, 5), lift=1) + 1
    pool(fc, f, size=4, deep=3)
    R.stand_on(fc, 1, f, 1); time.sleep(2.0)
    cmd("look 0 60"); time.sleep(1.0)
    def drain(secs=4.0):
        a = float(stats()["drown"]); time.sleep(secs); b = float(stats()["drown"]); return a - b
    print("   in the pool:", {k: stats()[k] for k in ("drown", "headIn", "underwater")})
    cmd("wear iron_helmet"); time.sleep(0.5)
    d0 = drain()
    cmd("wornench 0 Respiration 3"); d1 = drain()
    print("   breath lost in 4 s:", d0, "then with Respiration III:", d1)
    step("Respiration III: breath goes a quarter as fast under water", d0 > 0 and d1 < d0 * 0.4, (d0, d1))
    shot("underwater")
    give(("iron_pickaxe", 1)); select("Iron Pickaxe")
    def mine_time():
        R.place("stone", fc, 2, f, 2); time.sleep(0.6)
        aim_cell(fc, 2, f, 2); t0 = time.time()
        cmd("mouse left 12"); R.wait(lambda: (R.block_at(fc, 2, f, 2) or ("water",))[0] == "water", 12, step=0.1); cmd("mouse release")
        return time.time() - t0
    print("   underwater factor now:", stats()["uwf"], "head in:", stats()["headIn"])
    cmd("wornench 0"); m0 = mine_time()
    cmd("wornench 0 AquaAffinity 1"); m1 = mine_time()
    print("   stone under water:", round(m0, 2), "s, with Aqua Affinity", round(m1, 2), "s")
    step("Aqua Affinity: mining under water much faster than without (Minecraft's 5 times)", m1 < m0 / 2.5, (round(m0, 2), round(m1, 2)))
    cmd(f"fluidmap clear 0 {fc[0] + 1} {f} {fc[2] + 1} 8"); time.sleep(1.0)
    # (Depth Strider: in water a block deep, walking along the bottom, Minecraft's case for it)
    for dx in range(-1, 5):
        for dz in range(-1, 5):
            for y in (f + 1, f + 2): cmd(f"breakabs {fc[0] + dx} {y} {fc[2] + dz}")
    pool(fc, f, size=4, deep=1)
    cmd("wear iron_boots"); time.sleep(0.3)
    def walk():
        R.stand_on(fc, 0, f, 0); cmd("look 45 0"); time.sleep(1.0); a = R.pos(); cmd("keys W 1.0"); time.sleep(0.3); b = R.pos()
        return round(math.hypot(b[0] - a[0], b[2] - a[2]), 2), stats()["hinder"]
    w0 = walk()
    cmd("wornench 3 DepthStrider 3"); time.sleep(0.5)
    w1 = walk()
    print("   a second's walk in water a block deep:", w0, "then with Depth Strider III:", w1)
    step("Depth Strider III: you walk through the water further a second (its slowing gone)", w1[0] > w0[0] * 1.3 and w1[1] == "1.00", (w0, w1))
    cmd("wear none"); cmd(f"fluidmap clear 0 {fc[0] + 1} {f} {fc[2] + 1} 8")

def orbs():
    # XP comes as Minecraft's orbs: out of a cow killed, an ore mined, a thrown bottle; they fly to you and give their XP
    fc = R.start_flat(86, [(0, 2), (0, 4)]); cmd("clearenemies 200 all"); cmd("mobspawns 0"); cmd("god 1"); cmd("gamemode survival")
    give(("iron_sword", 1)); select("Iron Sword")
    cmd("look 0 0"); cmd("enemy cow 4"); time.sleep(1.2); held_mob("cow", 2.2)
    x0 = st("xp"); o0 = int(stats()["orbs"])
    for _ in range(6):
        if not mob("Cow"): break
        hit(gap=0.5)
    time.sleep(0.25); shot("orbs_from_cow"); live = int(stats()["liveOrbs"])
    time.sleep(2.5)
    print("   orbs made", int(stats()["orbs"]) - o0, "seen flying", live, "xp", x0, "->", st("xp"))
    step("a cow killed: XP orbs come out of it, fly to me, and my XP goes up (1-3)", int(stats()["orbs"]) > o0 and 1 <= st("xp") - x0 <= 3, (live, x0, st("xp")))
    sy = R.surface(fc, 0, 2)
    give(("iron_pickaxe", 1)); select("Iron Pickaxe")
    x0 = st("xp"); o0 = int(stats()["orbs"])
    for _ in range(3): mine_one(fc, "diamond_ore", 0, sy + 1, 2)
    time.sleep(1.5)
    step("diamond ore mined: orbs, and its XP (3-7 a block)", int(stats()["orbs"]) > o0 and 9 <= st("xp") - x0 <= 21, (x0, st("xp")))
    give(("experience_bottle", 3)); select("Bottle o' Enchanting")
    x0 = st("xp"); cmd("look 0 50"); time.sleep(0.3)
    for _ in range(3): cmd("rmb"); time.sleep(0.6)
    time.sleep(0.3); shot("orbs_from_bottles"); time.sleep(3.0)
    step("three bottles o' enchanting thrown at my feet: orbs, 9-33 XP", 9 <= st("xp") - x0 <= 33, (x0, st("xp")))

def mending():
    # Mending: orbs repair the held pickaxe (2 durability a point) before they give XP; and a worn chestplate
    fc = R.start_flat(87, [(x, 2) for x in range(0, 3)]); cmd("god 1"); cmd("gamemode survival")
    sy = R.surface(fc, 0, 2)
    give(("iron_pickaxe", 1)); select("Iron Pickaxe"); enchant("Mending", 1)
    cmd("tooluse 200"); u0 = T.used(); x0 = st("xp")
    for _ in range(3): mine_one(fc, "diamond_ore", 0, sy + 1, 2)
    time.sleep(2.0); u1 = T.used(); x1 = st("xp")
    print("   pickaxe used", u0, "->", u1, "xp", x0, "->", x1)
    step("Mending: the diamond ores' orbs repair the held pickaxe (its uses down), little or no XP left over", u0 and u1 and u1[0] < u0[0] - 10 and x1 - x0 <= 3, (u0, u1, x0, x1))
    cmd("wear iron_chestplate"); time.sleep(0.4); cmd("wornench 1 Mending 1"); cmd("wornwear 1 120")
    select("Iron Pickaxe"); cmd("tooluse 0"); time.sleep(0.3)
    w0 = stats()["worn"]
    for _ in range(3): mine_one(fc, "diamond_ore", 1, sy + 1, 2)
    time.sleep(2.0); w1 = stats()["worn"]
    print("   chestplate", w0, "->", w1)
    d = lambda w: int(re.search(r"iron_chestplate[^(]*\((\d+)/", w).group(1))
    step("Mending on the worn chestplate: the orbs repair it", d(w1) < d(w0), (w0, w1))
    cmd("wear none")

def durability():
    # the bars: armor's in the inventory, the elytra's; two worn pickaxes in the grid make one with both's durability and
    # a twentieth more
    R.start_flat(88, [(0, 2)]); cmd("god 1"); cmd("gamemode survival")
    cmd("wear iron_boots elytra"); time.sleep(0.3); cmd("wornwear 1 200"); cmd("wornwear 3 100")
    cmd("craftui open"); time.sleep(1.0); shot("bars_in_inventory"); cmd("craftui close")
    print("   worn:", stats()["worn"])
    cmd("wear none")
    cmd("clearinv"); time.sleep(0.3)
    give(("iron_pickaxe", 1)); select("Iron Pickaxe"); cmd("tooluse 100")
    cmd("invgive iron_pickaxe 1"); time.sleep(1.5)
    hb = slots().split(","); j = [i for i, x in enumerate(hb) if "pickaxe" in x.lower()]
    cmd(f"slot {j[-1]}"); time.sleep(0.4); cmd("tooluse 200"); time.sleep(0.3)
    cmd("craftui open"); time.sleep(0.8)
    st_ = cmd("craftui state"); print("   ", st_[:200])
    hot = re.search(r"hotbar=\[([^\]]*)\]", st_).group(1).split(",")
    idx = [i for i, x in enumerate(hot) if x.startswith("iron_pickaxe")]
    for n, i in enumerate(idx[:2]): cmd(f"craftui click hot {i}"); cmd(f"craftui click grid {n}")
    time.sleep(0.4); out = re.search(r"out=(\S+)", cmd("craftui state")).group(1)
    shot("grid_repair")
    g0 = int(stats()["gridRepairs"]); cmd("craftui click out 0 shift"); time.sleep(0.6); cmd("craftui close")
    print("   grid out:", out)
    # (150 + 50 left, + 12 (a twentieth of 250) = 212 left: 38 used)
    step("two worn iron pickaxes in the grid: one, 38 used (both's 200 left + 12)", out.startswith("iron_pickaxe#38") and int(stats()["gridRepairs"]) == g0 + 1, out)

def bow():
    # the bow wears a use a shot (384); Power hits harder, Flame sets the target burning, Punch knocks it back, Infinity
    # keeps the plain arrow. Real draws with the right mouse button at cows.
    import pilot
    R.start_flat(89, [(0, 2), (0, 6)]); cmd("clearenemies 200 all"); cmd("mobspawns 0"); cmd("god 1"); cmd("gamemode survival")
    give(("bow", 1), ("arrow", 16)); select("Bow")
    def shoot_cow(dist=7, hold=True):
        # (as t_bows: a cow spawned close and held out there, the bow drawn a full second with the right button)
        cmd("clearenemies 200 all"); time.sleep(0.3); cmd("look 0 0"); cmd("enemy cow 2"); time.sleep(1.0); cmd(f"enemyhold cow {dist}"); time.sleep(0.3)
        m = re.search(r"Cow@([-\d.]+),([-\d.]+),([-\d.]+)", cmd("enemies"))
        pilot.aim_at(float(m.group(1)), float(m.group(2)) + 1.0, float(m.group(3))); time.sleep(0.3)
        if not hold: cmd("enemyrelease cow"); time.sleep(0.1)
        c0 = mob("Cow"); cmd("mouse right 1.2"); time.sleep(1.4); return c0, mob("Cow")
    u0 = T.used(); a = shoot_cow(); u1 = T.used()
    step("a plain bow: 384 uses, a shot wears it 1", u0 and u1 and u1[1] == 384 and u1[0] - u0[0] == 1, (u0, u1))
    enchant("Power", 5); b = shoot_cow()
    print("   plain:", a, " power V:", b)
    pl = (a[0][0] - (a[1][0] if a[1] else 0)) if a[0] else 0
    pw = (b[0][0] - (b[1][0] if b[1] else 0)) if b[0] else 0
    step("Power V: an arrow hurts the cow more than a plain bow's", pw > pl, (pl, pw))
    enchant("Flame", 1); c = shoot_cow(); time.sleep(0.2); shot("flame_cow")
    step("Flame: the cow struck burns", c[1] and "Fire" in c[1][1], c)
    # (Punch: the cow held out there, then let go as the bow is drawn; the push takes a moment after the hit)
    enchant("Punch", 2)
    cmd("clearenemies 200 all"); time.sleep(0.3); cmd("look 0 0"); cmd("enemy cow 2"); time.sleep(1.0); cmd("enemyhold cow 7"); time.sleep(0.3)
    m = re.search(r"Cow@([-\d.]+),([-\d.]+),([-\d.]+)", cmd("enemies"))
    pilot.aim_at(float(m.group(1)), float(m.group(2)) + 1.0, float(m.group(3))); time.sleep(0.3); cmd("enemyrelease cow"); time.sleep(0.3)
    k0 = mob("Cow"); cmd("mouse right 1.2"); time.sleep(2.4); k = (k0, mob("Cow"))
    step("Punch II: the cow is knocked back (further off)", k[0] and k[1] and k[1][2] - k[0][2] > 1.5, k)
    enchant("Infinity", 1); n0 = slots(); shoot_cow(); shoot_cow(); n1 = slots()
    a0 = re.search(r"Arrow[^,]*", n0); a1 = re.search(r"Arrow[^,]*", n1)
    step("Infinity: two shots, the arrows' count the same", a0 and a1 and a0.group(0) == a1.group(0), (n0, n1))
    cmd("clearenemies 200 all")

def rod():
    # the rod: a catch wears it 1 (64 uses); Lure III: bites come sooner (5 s less a level, Minecraft's)
    fc = R.start_flat(90, [(dx, dz) for dx in range(-1, 5) for dz in range(-1, 8)]); cmd("god 1"); cmd("gamemode survival")
    f = R.stone_floor(fc, range(-1, 5), range(-1, 8), lift=0) + 1
    for dx in range(-1, 5):
        for dz in range(2, 8):
            if 0 <= dx < 4 and 3 <= dz < 7: cmd(f"placeabs water {fc[0] + dx} {f} {fc[2] + dz} 1")
            else: R.place("stone", fc, dx, f, dz)
    time.sleep(1.5)
    R.stand_on(fc, 1, f, 1); time.sleep(1.0)
    give(("fishing_rod", 1)); select("Fishing Rod")
    def fish(maxwait=40):
        cmd("look 0 25"); time.sleep(0.3); cmd("rmb"); t0 = time.time()
        while time.time() - t0 < maxwait:
            if "biting=True" in cmd("fish"): cmd("rmb"); time.sleep(1.5); return round(time.time() - t0, 1)
            time.sleep(0.1)
        cmd("rmb"); return None
    u0 = T.used(); t_plain = fish(); u1 = T.used()
    step("a plain rod: 64 uses, a catch wears it 1", t_plain and u1 and u1[1] == 64 and u1[0] - u0[0] == 1, (t_plain, u0, u1))
    enchant("Lure", 3); ts = [fish(25) for _ in range(3)]
    print("   bites: plain", t_plain, "lure III", ts)
    step("Lure III: each bite within 15 s (Minecraft's 5-30 s less 15)", all(t is not None and t <= 16.5 for t in ts), ts)
    shot("fishing")
    cmd(f"fluidmap clear 0 {fc[0] + 1} {f} {fc[2] + 4} 8")

def flint():
    # flint and steel: 64 uses, a strike a use; it lights a fire
    fc = R.start_flat(91, [(0, 2), (1, 2), (2, 2)]); cmd("god 1"); cmd("gamemode survival")
    sy = R.surface(fc, 0, 2)
    give(("flint_and_steel", 1)); select("Flint and Steel")
    u0 = T.used()
    for dx in range(3):
        R.place("stone", fc, dx, sy + 1, 2); time.sleep(0.4); aim_cell(fc, dx, sy + 1, 2, face_y=1.0); cmd("mouse right 0.1"); time.sleep(0.8)
    u1 = T.used()
    print("   flint and steel", u0, "->", u1)
    step("flint and steel: 64 uses, three strikes wear it 3", u0 and u1 and u1[1] == 64 and u1[0] - u0[0] == 3, (u0, u1))
    shot("fires")

def hot(st, key):
    for i, x in enumerate(re.search(r"hotbar=\[([^\]]*)\]", st).group(1).split(",")):
        if x.split(":")[0].split("#")[0] == key: return i

def table():
    # the table, Minecraft's: 15 bookshelves round it (a block of air between), glyphs drifting from them to it; a bow
    # and a book enchanted there by real [E] and clicks; the book comes back an enchanted book; at an anvil the book's
    # enchantment goes onto a sword
    import pilot
    fc = R.start_flat(93, [(dx, dz) for dx in range(-2, 3) for dz in range(0, 7)]); cmd("god 1"); cmd("gamemode survival")
    sy = max(R.surface(fc, dx, dz) for dx in range(-2, 3) for dz in range(0, 7)) + 1
    for dx in range(-2, 3):
        for dz in range(0, 7): R.place("stone", fc, dx, sy, dz)
    time.sleep(0.5)
    R.place("enchanting_table", fc, 0, sy + 1, 3)
    for dx in range(-2, 3):
        for dz in range(1, 6):
            if (abs(dx) == 2 or abs(dz - 3) == 2) and (dx, dz) != (0, 1): R.place("bookshelf", fc, dx, sy + 1, dz)
    R.place("anvil", fc, 1, sy + 1, 0)
    time.sleep(0.8)
    R.stand_on(fc, 0, sy + 1, 1); time.sleep(0.6)
    give(("bow", 1), ("book", 2), ("lapis_lazuli", 20))
    cmd("xplevel 40"); g0 = int(stats()["glyphs"])
    pilot.aim_at((fc[0] + .5) * S, (sy + 1.9) * S + R.YO, (fc[2] + 3.5) * S); time.sleep(3.0)
    shot("table_glyphs")
    step("glyphs drift from the shelves to the table (a block of air between them)", int(stats()["glyphs"]) > g0 + 5, int(stats()["glyphs"]) - g0)
    cmd("keys E 0.1"); time.sleep(0.8)
    st_ = cmd("enchantui state")
    step("[E] on the table: the enchanting screen, 15 shelves", "open=True" in st_ and "shelves=15" in st_, st_[:120])
    cmd(f"enchantui click hot {hot(st_, 'bow')}"); cmd("enchantui click item 0")
    st_ = cmd("enchantui state"); cmd(f"enchantui click hot {hot(st_, 'lapis_lazuli')}"); cmd("enchantui click lapis 0"); time.sleep(0.5)
    st_ = cmd("enchantui state"); print("   bow's offers:", re.search(r"offers=\[([^\]]*)\]", st_).group(1))
    shot("table_bow_offers")
    offers = re.search(r"offers=\[([^\]]*)\]", st_).group(1).split(",")
    step("a bow gets three offers, each a bow's enchantment (Power, Punch, Flame, Infinity, Unbreaking)", len(offers) == 3 and all(re.search(r":(Power|Punch|Flame|Infinity|Unbreaking)\d", o) for o in offers), offers)
    cmd("enchantui click offer 2"); time.sleep(0.4)
    cmd("enchantui click item 0"); time.sleep(0.3)  # (back to the hotbar)
    st_ = cmd("enchantui state")
    cmd(f"enchantui click hot {hot(st_, 'book')}"); cmd("enchantui click item 0 right"); time.sleep(0.5)
    st_ = cmd("enchantui state"); print("   a book's offers:", re.search(r"offers=\[([^\]]*)\]", st_).group(1))
    cmd("enchantui click offer 2"); time.sleep(0.5)
    st_ = cmd("enchantui state")
    step("a book enchanted at the table comes back an enchanted book", re.search(r"item=enchanted_book#\d+", st_) is not None, st_[:160])
    cmd("enchantui click item 0"); time.sleep(0.3); cmd("enchantui close"); time.sleep(1.5)
    sl = slots(); print("   hotbar:", sl)
    step("the enchanted book in the hotbar, its name 'Enchanted Book'", "Enchanted Book" in sl, sl)
    select("Enchanted Book"); time.sleep(0.5); shot("enchanted_book_held")
    select("Bow"); d = cmd("itemdata"); print("   bow:", d)
    step("the bow comes back enchanted", re.search(r"ench=\w", d) is not None and "ench=-" not in d, d)
    # the anvil: the book's enchantment onto something that takes it (a book can roll any enchantment)
    select("Enchanted Book"); bd = cmd("itemdata"); print("   book:", bd)
    first = re.search(r"ench=([A-Za-z ]+?)(?: [IVX]+)?(?:,|$| key)", bd).group(1).replace(" ", "")
    target = {"Sharpness": "iron_sword", "Smite": "iron_sword", "BaneofArthropods": "iron_sword", "BaneOfArthropods": "iron_sword", "Knockback": "iron_sword", "FireAspect": "iron_sword", "Looting": "iron_sword", "SweepingEdge": "iron_sword",
              "Protection": "iron_chestplate", "FireProtection": "iron_chestplate", "BlastProtection": "iron_chestplate", "ProjectileProtection": "iron_chestplate", "Thorns": "iron_chestplate",
              "FeatherFalling": "iron_boots", "DepthStrider": "iron_boots", "Respiration": "iron_helmet", "AquaAffinity": "iron_helmet",
              "Power": "bow", "Punch": "bow", "Flame": "bow", "Infinity": "bow", "Lure": "fishing_rod", "LuckoftheSea": "fishing_rod", "LuckOfTheSea": "fishing_rod",
              "QuickCharge": "crossbow", "Multishot": "crossbow", "Piercing": "crossbow"}.get(first, "iron_pickaxe")
    print("   its first enchantment", first, "-> onto a", target)
    cmd(f"invgive {target} 1"); time.sleep(1.5)
    st_ = cmd(f"anvilui open {fc[0] + 1} {sy + 1} {fc[2]}"); time.sleep(0.5)
    cmd(f"anvilui click hot {hot(st_, target)}"); cmd("anvilui click slot 0")
    st_ = cmd("anvilui state"); cmd(f"anvilui click hot {hot(st_, 'enchanted_book')}"); cmd("anvilui click slot 1"); time.sleep(0.5)
    st_ = cmd("anvilui state"); print("   anvil:", st_[:220])
    shot("anvil_book")
    out = re.search(r"out=(\S+)", st_).group(1); cost = int(re.search(r"cost=(\d+)", st_).group(1))
    step(f"{target} + the enchanted book at the anvil: it, enchanted, for some levels", out.startswith(target + "#") and cost > 0, (out, cost))
    lv0 = st("lvl"); cmd("anvilui click out 0 shift"); time.sleep(0.8); cmd("anvilui close"); time.sleep(1.5)
    sl = slots(); print("   hotbar:", sl)
    k = [x for x in re.search(r"hotbar=\[([^\]]*)\]", cmd("craftui state")).group(1).split(",") if x.startswith(target + "#")]
    step("it's in the hotbar with the book's enchantment, the levels paid, the book gone", k and st("lvl") == lv0 - cost and "Enchanted Book" not in sl, (k, lv0, st("lvl"), cost))

def trims():
    # the eighteen templates in the creative menu; a template copied (7 diamonds, it, its block: 2)
    R.start_flat(94, [(0, 2)]); cmd("god 1"); cmd("gamemode survival")
    cmd("gamemode creative"); time.sleep(0.5); cmd("creativeui open"); time.sleep(0.8)
    seen = set()
    for t in range(14):
        cmd(f"creativeui click tabs {t}"); time.sleep(0.2)
        seen |= set(re.search(r"items=\[([^\]]*)\]", cmd("creativeui state")).group(1).split(","))
    shot("creative_templates"); cmd("creativeui close"); cmd("gamemode survival")
    tpl = sorted(k for k in seen if k.endswith("_armor_trim_smithing_template"))
    step("all eighteen trim templates in the creative menu", len(tpl) == 18, tpl)
    give(("diamond", 7), ("dune_armor_trim_smithing_template", 1), ("sandstone", 1))
    cmd("craftui open table"); time.sleep(0.8)
    out = T.grid_craft([("diamond", (0, 2, 3, 5, 6, 7, 8)), ("dune_armor_trim_smithing_template", (1,)), ("sandstone", (4,))])
    shot("dune_copy")
    step("7 diamonds + the dune template + sandstone: 2 dune templates", out.startswith("dune_armor_trim_smithing_template") and "x2" in out, out)
    cmd("craftui close")

def death():
    # dying (keepInventory off): 7 XP a level, 100 at most, drops as orbs; the rest is gone. (The round ends: last.)
    def chat(text):
        cmd("keys Slash 0.08"); time.sleep(0.5); cmd("chattext " + text.replace(" ", "_")); time.sleep(0.2); cmd("keys Enter 0.08"); time.sleep(1.5)
    R.start_flat(95, [(0, 2)]); cmd("gamemode survival")
    chat("/keepInventory false"); print("   keepInventory now:", stats()["keepInv"])
    cmd("xplevel 10"); x0 = st("xp"); d0 = int(stats()["dropped"])
    cmd("god 0"); cmd("hurt 300 Mauling"); time.sleep(1.0)
    s_ = stats(); shot("death_orbs")
    print("   died:", cmd("state")[:60], "xp", x0, "->", s_["xp"], "dropped", int(s_["dropped"]) - d0, "orbs", s_["liveOrbs"])
    step("dying at level 10: 70 XP drops as orbs where I fell, the rest gone (XP 0)", int(s_["dropped"]) - d0 == 70 and int(float(s_["xp"])) == 0 and int(s_["liveOrbs"]) > 0, s_)
    chat("/keepInventory true"); cmd("god 1")

SECTIONS = {"sword": sword, "sweep": sweep, "bane": bane, "looting": looting, "mining": mining, "armor": armor, "water": water, "orbs": orbs, "mending": mending, "durability": durability, "bow": bow, "rod": rod, "flint": flint, "table": table, "trims": trims, "death": death}

def free_slot(st_):
    return next(i for i, x in enumerate(re.search(r"hotbar=\[([^\]]*)\]", st_).group(1).split(",")) if x == "-")

def books():
    # two books enchanted at the table, then one onto the other at the anvil (Minecraft's book on book: both lists, for
    # less than an item would cost); shears won't go in the table (Minecraft: anvil only); a golden pickaxe's offers
    # against an iron one's at the same table (gold's enchantability 22 against iron's 14)
    import pilot
    fc = R.start_flat(101, [(dx, dz) for dx in range(-2, 3) for dz in range(0, 7)]); cmd("god 1"); cmd("gamemode survival")
    sy = max(R.surface(fc, dx, dz) for dx in range(-2, 3) for dz in range(0, 7)) + 1
    for dx in range(-2, 3):
        for dz in range(0, 7): R.place("stone", fc, dx, sy, dz)
    time.sleep(0.5)
    R.place("enchanting_table", fc, 0, sy + 1, 3)
    for dx in range(-2, 3):
        for dz in range(1, 6):
            if (abs(dx) == 2 or abs(dz - 3) == 2) and (dx, dz) != (0, 1): R.place("bookshelf", fc, dx, sy + 1, dz)
    R.place("anvil", fc, 1, sy + 1, 0); time.sleep(0.8)
    R.stand_on(fc, 0, sy + 1, 1); time.sleep(0.6)
    give(("book", 2), ("lapis_lazuli", 30), ("shears", 1), ("golden_pickaxe", 1), ("iron_pickaxe", 1))
    cmd("xplevel 60")
    st_ = cmd(f"enchantui open {fc[0]} {sy + 1} {fc[2] + 3}"); time.sleep(0.5)
    cmd(f"enchantui click hot {hot(st_, 'shears')}"); cmd("enchantui click item 0"); time.sleep(0.3)
    st_ = cmd("enchantui state")
    step("shears won't go into the enchanting table (Minecraft's: the anvil only)", "item=-" in st_ and "shears:1" in st_, st_[:160])
    cmd("enchantui click outside 0"); time.sleep(0.5)  # (thrown back? no: back into the hotbar below)
    st_ = cmd("enchantui state")
    if "cursor=shears" in st_: cmd(f"enchantui click hot 8"); time.sleep(0.3)
    st_ = cmd("enchantui state"); cmd(f"enchantui click hot {hot(st_, 'lapis_lazuli')}"); cmd("enchantui click lapis 0"); time.sleep(0.3)
    def offers_for(key):
        st_ = cmd("enchantui state"); cmd(f"enchantui click hot {hot(st_, key)}"); cmd("enchantui click item 0"); time.sleep(0.4)
        o = re.search(r"offers=\[([^\]]*)\]", cmd("enchantui state")).group(1)
        cmd("enchantui click item 0"); time.sleep(0.2); st_ = cmd("enchantui state")
        cmd(f"enchantui click hot {free_slot(st_)}"); time.sleep(0.3)
        return o
    og, oi = offers_for("golden_pickaxe"), offers_for("iron_pickaxe")
    print("   golden pickaxe:", og); print("   iron pickaxe:  ", oi)
    step("a golden and an iron pickaxe get their own offers at the same table (gold rolls with more enchantability)", og != oi, (og, oi))
    got = []
    for k in range(2):
        # (the second may already be in the slot: taking the first out swapped in the book left on the mouse, Minecraft's)
        st_ = cmd("enchantui state")
        if "item=book " not in st_: cmd(f"enchantui click hot {hot(st_, 'book')}"); cmd("enchantui click item 0 right"); time.sleep(0.4)
        cmd(f"enchantui click offer {2 - k}"); time.sleep(0.4)
        st_ = cmd("enchantui state"); got.append(re.search(r"item=(\S+)", st_).group(1))
        cmd("enchantui click item 0"); time.sleep(0.2); st_ = cmd("enchantui state")
        if "cursor=enchanted_book" in st_: cmd(f"enchantui click hot {free_slot(st_)}"); time.sleep(1.0)  # (into the hotbar)
        else:  # (the book went to the cursor, swapped for the one there)
            pass
    cmd("enchantui close"); time.sleep(1.5)
    print("   books:", got)
    st_ = cmd(f"anvilui open {fc[0] + 1} {sy + 1} {fc[2]}"); time.sleep(0.5)
    hb = re.search(r"hotbar=\[([^\]]*)\]", st_).group(1).split(",")
    bi = [i for i, x in enumerate(hb) if x.startswith("enchanted_book#")]
    cmd(f"anvilui click hot {bi[0]}"); cmd("anvilui click slot 0"); cmd(f"anvilui click hot {bi[1]}"); cmd("anvilui click slot 1"); time.sleep(0.5)
    st_ = cmd("anvilui state"); print("   anvil:", st_[:200]); shot("book_on_book")
    out = re.search(r"out=(\S+)", st_).group(1); cost = int(re.search(r"cost=(\d+)", st_).group(1))
    step("enchanted book on enchanted book at the anvil: an enchanted book, for some levels", out.startswith("enchanted_book#") and cost > 0, (out, cost))
    cmd("anvilui click out 0 shift"); time.sleep(0.8); cmd("anvilui close"); time.sleep(1.5)
    select("Enchanted Book"); d = cmd("itemdata"); print("   the book now:", d)
    step("the combined book holds more than either book did", len(re.findall(r"[A-Z][a-z]+(?: [a-z]+)*(?: [IVX]+)?", d.split("ench=")[1].split(" key=")[0])) >= 2, d)

LOGF = r"O:/SteamLibrary/steamapps/common/Lethal Company/BepInEx/LogOutput.log"
def armor_log(cause):
    return re.findall(r"\[dev\] armor \d+ pts: " + cause + r" damage (\d+) -> (\d+)", open(LOGF, encoding="utf-8", errors="replace").read())

def protections():
    # Fire Protection in real fire, Blast Protection by a real TNT blast, Projectile Protection against a real skeleton's
    # arrows: each piece's 2 points a level against its own kind (the armor's own log of each hit, before and after)
    import pilot
    fc = R.start_flat(97, [(dx, dz) for dx in range(-1, 3) for dz in range(0, 6)]); cmd("gamemode survival"); cmd("hunger 20 5")
    sy = R.surface(fc, 0, 2)
    cmd("wear iron_helmet iron_chestplate iron_leggings iron_boots"); time.sleep(0.5)
    def in_fire(secs=2.2):
        n0 = len(armor_log("Burning"))
        R.place("netherrack", fc, 0, sy, 3); R.place("fire", fc, 0, sy + 1, 3); time.sleep(0.4)
        cmd("god 0"); R.stand_on(fc, 0, sy + 1, 3); t0 = time.time()
        while time.time() - t0 < secs: time.sleep(0.4); cmd("hp 100")
        cmd("god 1"); R.stand_on(fc, 0, sy + 1, 0); time.sleep(0.8); cmd("hp 100")
        return armor_log("Burning")[n0:]
    a = in_fire()
    for s_ in range(4): cmd(f"wornench {s_} FireProtection 4")
    b = in_fire()
    print("   in fire, iron armor:", a, " with Fire Protection IV x4:", b)
    step("Fire Protection IV on all four: real fire hurts less (5 -> 4 in iron; with it Minecraft's 80% cap: 1)", a and b and all(x[1] == "4" for x in a) and all(x[1] == "1" for x in b[-2:]), (a, b))
    shot("in_fire")
    cmd(f"breakabs {fc[0]} {sy + 1} {fc[2] + 3}")
    def blast():
        n0 = len(armor_log("Blast"))
        R.place("tnt", fc, 0, sy + 1, 4); time.sleep(0.4)
        cmd("god 0"); R.stand_on(fc, 0, sy + 1, 1); cmd("hp 100"); time.sleep(0.3)
        give(("flint_and_steel", 1)); select("Flint and Steel")
        aim_cell(fc, 0, sy + 1, 4); cmd("mouse right 0.1"); time.sleep(5.0)
        cmd("god 1"); cmd("hp 100")
        R.wait(lambda: len(armor_log("Blast")) > n0, 8.0, step=0.5)  # (the log is written a little late)
        return armor_log("Blast")[n0:]
    c = blast()
    for s_ in range(4): cmd(f"wornench {s_} BlastProtection 4")
    d = blast()
    print("   a TNT blast 3 blocks off, iron armor:", c, " with Blast Protection IV x4:", d)
    # (blasts differ with where you stand: the ratio of each hit's before to after: iron alone 1.3, with it 1.8)
    ratio = lambda xs: [round(int(a_) / max(1, int(b_)), 2) for a_, b_ in xs]
    step("Blast Protection IV on all four: a blast's damage cut more (iron alone /1.3; with it Minecraft's 80% more: /6.5)", c and d and all(abs(r - 1.3) < 0.08 for r in ratio(c)) and abs(ratio(d)[-1] - 6.5) < 1.0, (c, d, ratio(c), ratio(d)))
    shot("after_blast")
    cmd("tpship"); time.sleep(1.5)
    # a skeleton's arrows (Stabbing): skeletons are the facility's; one held off 6 m and let go
    print("   inside:", cmd("tpinside")[:40]); time.sleep(2.5); cmd("hunger 20 5")
    def arrows(secs=10.0):
        n0 = len(armor_log("Stabbing"))
        cmd("clearenemies 200 all"); time.sleep(0.3); cmd("look 0 0"); cmd("enemy skeleton 2"); time.sleep(1.2); cmd("enemyhold skeleton 6"); time.sleep(0.3); cmd("enemyrelease skeleton")
        cmd("god 0"); t0 = time.time()
        while time.time() - t0 < secs: time.sleep(0.5); cmd("hp 100")
        cmd("god 1"); cmd("hp 100"); cmd("clearenemies 200 all")
        R.wait(lambda: len(armor_log("Stabbing")) > n0, 8.0, step=0.5)
        return armor_log("Stabbing")[n0:]
    e = arrows()
    for s_ in range(4): cmd(f"wornench {s_} ProjectileProtection 4")
    f = arrows()
    print("   skeleton arrows, iron armor:", e, " with Projectile Protection IV x4:", f)
    step("Projectile Protection IV on all four: a skeleton's arrows hurt less (15 -> 12 in iron; with it the 80% cap: 2)", e and f and all(x[1] == "12" for x in e) and all(x[1] == "2" for x in f[-2:]), (e, f))
    cmd("wear none"); cmd("tpship"); time.sleep(1.5)

def glint():
    # enchanted armor glints on the player's model (seen by the others; the dev view shows one's own)
    R.start_flat(99, [(0, 2)]); cmd("god 1")
    cmd("wear iron_helmet iron_chestplate iron_leggings iron_boots"); time.sleep(0.4)
    cmd("wornench 1 Protection 4"); cmd("wornench 2 Protection 4"); time.sleep(0.5)
    cmd("armormodels show 1"); cmd("look 0 80"); time.sleep(1.5); shot("glint_armor")
    cmd("armormodels show 0"); cmd("look 0 0"); cmd("wear none")

SECTIONS.update({"protections": protections, "glint": glint, "books": books})

if __name__ == "__main__":
    t0 = time.time()
    for s in sys.argv[1:]:
        print("==", s); SECTIONS[s]()
    print(f"-- {sum(1 for _, ok in log if ok)}/{len(log)} ok, {time.time() - t0:.0f} s")
    for n, ok in log:
        if not ok: print("BAD", n)
