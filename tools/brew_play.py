"""#244 part B1a hand play: the things brewing needs, played as a player would (the crafting screen's own clicks, real
right/left clicks, [E] on a cow, a bottle on a hive). Screenshots in shots/bp_*.png.
usage: py tools/brew_play.py <section> [...]   sections: icons craft glowstone melon mushrooms stews honey milk apple brew drink splash invis wither"""
import sys, os, time, re, math
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from regress import cmd, S
import thorough_play as T
HERE = os.path.dirname(os.path.abspath(__file__))
log = []
def shot(n): cmd(f"screenshot {os.path.join(HERE, '..', 'shots', 'bp_' + n + '.png')}"); time.sleep(0.5)
def step(name, ok, detail=""):
    log.append((name, ok)); print(("OK  " if ok else "BAD ") + name + (f"  ({detail})" if detail else ""), flush=True)
slots, st, give, aim_cell = T.slots, T.st, T.give, T.aim_cell
def count(name):
    m = re.search(re.escape(name) + r"x(\d+)", slots()); return int(m.group(1)) if m else (1 if name in slots() else 0)
def select(name):
    import pilot; pilot.select(name); time.sleep(0.3)

NEW = ["glowstone_dust", "melon_slice", "melon", "melon_seeds", "glistering_melon_slice", "brown_mushroom", "red_mushroom",
       "fermented_spider_eye", "bowl", "mushroom_stew", "rabbit_stew", "honey_bottle", "milk_bucket", "enchanted_golden_apple"]

def icons():
    # each new item in the hotbar (its icon, its name and tips held), and in the creative menu
    for i in range(0, len(NEW), 7):
        group = NEW[i:i + 7]
        give(*[(k, 1) for k in group]); time.sleep(0.5)
        s = slots()
        step(f"in the hotbar: {', '.join(group)}", s.count("-") <= 9 - len(group), s[:200])
        for j in range(len(group)):
            cmd(f"slot {j}"); time.sleep(0.35)
            if j in (0, 3, 6): shot(f"held_{group[j]}")
        shot(f"hotbar_{i // 7}")
    cmd("gamemode creative"); time.sleep(0.5)
    cmd("creativeui open"); time.sleep(0.8)
    where = {}
    for t in range(14):
        cmd(f"creativeui click tabs {t}"); time.sleep(0.3)
        stt = cmd("creativeui state")
        m = re.search(r"items=\[([^\]]*)\]", stt)
        if not m: continue
        tab = (re.search(r"tab=(\S+)", stt) or re.search(r"tab=(\d+)", stt))
        for k in NEW:
            if k in m.group(1).split(",") and k not in where: where[k] = tab.group(1) if tab else t
    missing = [k for k in NEW if k not in where]
    step("every new item is in the creative menu (in Minecraft's tab)", not missing, (where, missing))
    for t in range(14):
        cmd(f"creativeui click tabs {t}"); time.sleep(0.3)
        if "melon_slice" in cmd("creativeui state"):
            items = re.search(r"items=\[([^\]]*)\]", cmd("creativeui state")).group(1).split(",")
            want = max(0, items.index("melon_slice") // 9 - 2); cmd(f"mouse wheel {-want}"); time.sleep(0.6)
            shot("creative_food"); break
    cmd("creativeui close"); cmd("gamemode survival"); time.sleep(0.5)

CRAFTS = [
    ("glowstone from 4 dust", [("glowstone_dust", (0, 1, 3, 4))], "glowstone", 1),
    ("glistering melon slice", [("gold_nugget", (0, 1, 2, 3, 5, 6, 7, 8)), ("melon_slice", (4,))], "glistering_melon_slice", 1),
    ("fermented spider eye", [("spider_eye", (0,)), ("brown_mushroom", (4,)), ("sugar", (8,))], "fermented_spider_eye", 1),
    ("a melon from 9 slices", [("melon_slice", (0, 1, 2, 3, 4, 5, 6, 7, 8))], "melon", 1),
    ("melon seeds from a slice", [("melon_slice", (4,))], "melon_seeds", 1),
    ("bowls from planks", [("oak_planks", (3, 5, 7))], "bowl", 4),
    ("mushroom stew, brown and red", [("brown_mushroom", (0,)), ("red_mushroom", (4,)), ("bowl", (8,))], "mushroom_stew", 1),
    ("mushroom stew, two red", [("red_mushroom", (1, 2)), ("bowl", (7,))], "mushroom_stew", 1),
    ("rabbit stew", [("cooked_rabbit", (0,)), ("carrot", (1,)), ("baked_potato", (2,)), ("brown_mushroom", (4,)), ("bowl", (7,))], "rabbit_stew", 1),
    ("sugar from a honey bottle", [("honey_bottle", (4,))], "sugar", 3),
    ("a honey block from 4 bottles", [("honey_bottle", (0, 1, 3, 4))], "honey_block", 1),
    ("4 honey bottles from a block and 4 bottles", [("honey_block", (4,)), ("glass_bottle", (0, 2, 6, 8))], "honey_bottle", 4),
]

def craft():
    for name, items, want, n in CRAFTS:
        need = {}
        for k, cells in items: need[k] = need.get(k, 0) + len(cells)
        give(*need.items()); time.sleep(0.5)
        cmd("craftui open table"); time.sleep(0.8)
        out = T.grid_craft(items)
        if want in ("mushroom_stew", "fermented_spider_eye", "glistering_melon_slice"): shot("craft_" + want)
        hb = T.take_out(); cmd("craftui close"); time.sleep(0.6)
        got = sum(int(m.group(1)) for m in re.finditer(want + r":(\d+)", hb))
        step(f"crafted {name}", out.startswith(want) and got >= n, f"out={out} hotbar={hb[:120]}")

def glowstone():
    # a glowstone block mined: 2-4 dust (Minecraft's), not the block; and potions II brew on the dust
    fc = R.start_flat(50, [(0, 2)]); sy = R.surface(fc, 0, 2)
    got = []
    for i in range(3):
        cmd("clearinv"); R.hold("iron_pickaxe"); R.place("glowstone", fc, 0, sy + 1, 2); time.sleep(0.5)
        aim_cell(fc, 0, sy + 1, 2); n0 = T.count_loose("glowstone_dust")
        cmd("mouse left 2.0"); R.wait(lambda: R.block_at(fc, 0, sy + 1, 2) is None, 2.5, step=0.1); cmd("mouse release"); time.sleep(1.2)
        lo = cmd("loose 6"); got.append(T.count_loose("glowstone_dust") - n0 + count("Glowstone Dust"))
        cmd("clearinv")
    step("glowstone mined: 2-4 glowstone dust each", all(2 <= g <= 4 for g in got), got)
    shot("glowstone_dust")

def melon():
    # melon seeds planted on wet farmland ([right-click]), the stem grows and sets a melon beside it; the melon broken
    # gives slices; a slice eaten
    import pilot
    fc = R.start_flat(51, [(x, z) for x in range(-1, 3) for z in range(0, 4)])
    f = R.stone_floor(fc, range(-1, 3), range(0, 4)); y = f + 1
    R.place("water", fc, -1, y, 2); R.place("farmland", fc, 0, y, 2); R.place("dirt", fc, 1, y, 2); time.sleep(1.0)
    give(("melon_seeds", 4)); select("Melon Seeds")
    cmd(f"tp {(fc[0] + .5) * S:.2f} {(y + 1.1) * S + R.YO:.2f} {(fc[2] + 0.5) * S:.2f}"); time.sleep(0.8)
    pilot.aim_at((fc[0] + .5) * S, (y + 1.0) * S + R.YO, (fc[2] + 2.5) * S); time.sleep(0.3)
    cmd("rmb"); time.sleep(0.8)
    b = R.block_at(fc, 0, y + 1, 2)
    step("melon seeds planted on wet farmland", b and b[0] == "melon_stem", b)
    shot("melon_stem_young")
    cmd("farm 1"); t0 = time.time()
    R.wait(lambda: R.block_at(fc, 1, y + 1, 2) is not None and R.block_at(fc, 1, y + 1, 2)[0] == "melon", 40, step=1.0)
    cmd("farm 85")
    m = R.block_at(fc, 1, y + 1, 2); stem = R.block_at(fc, 0, y + 1, 2)
    step("the stem grew and set a melon beside it (on the dirt), bent to it", m and m[0] == "melon" and stem and (stem[1] & 8), f"{m} stem={stem} {time.time() - t0:.0f} s")
    cmd(f"tp {(fc[0] + 1.5) * S:.2f} {(y + 1.1) * S + R.YO:.2f} {(fc[2] + 0.5) * S:.2f}"); time.sleep(0.8)
    pilot.aim_at((fc[0] + 1.5) * S, (y + 1.5) * S + R.YO, (fc[2] + 2.5) * S); time.sleep(0.3)
    shot("melon_grown")
    cmd("clearinv"); R.hold("iron_axe")
    pilot.aim_at((fc[0] + 1.5) * S, (y + 1.5) * S + R.YO, (fc[2] + 2.5) * S); time.sleep(0.3)
    n0 = T.count_loose("melon_slice")
    cmd("mouse left 2.5"); R.wait(lambda: R.block_at(fc, 1, y + 1, 2) is None, 3, step=0.1); cmd("mouse release"); time.sleep(1.5)
    n = T.count_loose("melon_slice") - n0 + count("Melon Slice")
    step("the melon broken: 3-7 slices", 3 <= n <= 7, (n, cmd("loose 6")[:120]))
    give(("melon_slice", 1)); cmd("hunger 10 0"); select("Melon Slice"); h0 = st("hunger")
    cmd("mouse right 2.0"); time.sleep(2.4)
    step("a slice eaten: +2 hunger", st("hunger") - h0 == 2, (h0, st("hunger")))

def mushrooms():
    # mushrooms placed on a stone block's top and on the ground, not on a block's side; broken, they drop themselves
    import pilot
    fc = R.start_flat(52, [(x, z) for x in range(-1, 3) for z in range(0, 4)])
    f = R.stone_floor(fc, range(-1, 3), range(0, 4)); y = f + 1
    R.place("stone", fc, 1, y, 2); time.sleep(0.6)
    give(("brown_mushroom", 4), ("red_mushroom", 4)); select("Brown Mushroom")
    R.stand_on(fc, 0, f + 1, 0)
    pilot.aim_at((fc[0] + 1.5) * S, (y + 1.0) * S + R.YO, (fc[2] + 2.5) * S); time.sleep(0.3)
    cmd("rmb"); time.sleep(0.8)
    b = R.block_at(fc, 1, y + 1, 2)
    step("a brown mushroom on a stone block", b and b[0] == "brown_mushroom", b)
    select("Red Mushroom")
    pilot.aim_at((fc[0] + 0.5) * S, (y + 0.0) * S + R.YO + 0.02, (fc[2] + 2.5) * S); time.sleep(0.3)
    cmd("rmb"); time.sleep(0.8)
    r = R.block_at(fc, 0, y, 2)
    step("a red mushroom on the stone floor", r and r[0] == "red_mushroom", r)
    pilot.aim_at((fc[0] + 1.02) * S, (y + 0.5) * S + R.YO, (fc[2] + 2.5) * S); time.sleep(0.3)
    before = len([k for k, v in R.near_blocks(fc).items() if v[0].endswith("mushroom")])
    cmd("rmb"); time.sleep(0.8)
    after = len([k for k, v in R.near_blocks(fc).items() if v[0].endswith("mushroom")])
    step("not on a block's side", after == before, (before, after, cmd("place?")[:100]))
    R.stand_on(fc, 0, f + 1, 0); pilot.aim_at((fc[0] + 1.5) * S, (y + 1.3) * S + R.YO, (fc[2] + 2.5) * S); time.sleep(0.3)
    shot("mushrooms")
    n0 = T.count_loose("brown_mushroom")
    cmd("clearinv"); time.sleep(0.4)
    pilot.aim_at((fc[0] + 1.5) * S, (y + 1.2) * S + R.YO, (fc[2] + 2.5) * S); time.sleep(0.3)
    cmd("mouse left 0.3"); time.sleep(1.5)
    step("broken by hand: it drops itself", T.count_loose("brown_mushroom") > n0 or count("Brown Mushroom") > 0, cmd("loose 6")[:120])

def stews():
    # mushroom stew and rabbit stew eaten: their hunger, the bowl back in hand
    for key, name, hunger in (("mushroom_stew", "Mushroom Stew", 6), ("rabbit_stew", "Rabbit Stew", 10)):
        give((key, 1)); cmd("hunger 4 0"); select(name); h0 = st("hunger")
        cmd("mouse right 2.0"); time.sleep(3.0)
        s = slots()
        step(f"{name} eaten: +{hunger} hunger, a bowl back", st("hunger") - h0 == hunger and "Bowl" in s and name not in s, (h0, st("hunger"), s[:80]))
    shot("stew_bowl")

def honey():
    # a glass bottle on a Circuit Bees' hive: a honey bottle (once); drunk, it cures poison and gives the bottle back
    import pilot
    R.start_flat(53, [(0, 2)]); cmd("clearenemies 200 all")
    give(("glass_bottle", 2)); select("Glass Bottle")
    cmd("look 0 30"); print("   hive:", cmd("lcspawn Hive")[:100]); time.sleep(2.0)
    hv = re.search(r"Hive@([-\d.]+),([-\d.]+),([-\d.]+)", cmd("objs"))
    if hv: pilot.aim_at(float(hv.group(1)), float(hv.group(2)) + 0.1, float(hv.group(3))); time.sleep(0.4)
    cmd("rmb"); time.sleep(1.2)
    step("a glass bottle on the hive: a honey bottle", count("Honey Bottle") == 1 and count("Glass Bottle") == 1, slots()[:120])
    shot("honey_from_hive")
    cmd("rmb"); time.sleep(1.2)
    step("a second time: empty, nothing", count("Honey Bottle") == 1 and count("Glass Bottle") == 1, slots()[:120])
    cmd("potions give potion_poison"); select("Honey Bottle"); h0 = st("hunger")
    cmd("mouse right 2.4"); time.sleep(3.0)
    e = cmd("potions")
    step("drunk: poison gone, +6 hunger, the bottle back", "Poison" not in e and count("Glass Bottle") == 2, (e, h0, st("hunger"), slots()[:80]))

def milk():
    # an empty bucket on a cow ([E]): a bucket of milk; drunk, every effect gone and the bucket back
    R.start_flat(54, [(0, 2)]); cmd("clearenemies 200 all"); cmd("mobspawns 0")
    give(("bucket", 1)); select("Bucket")
    cmd("look 0 0"); cmd("enemy cow 3"); time.sleep(1.2)
    T.held_mob("cow", 2.4); tip = cmd("mob?")
    cmd("keys E 0.1"); time.sleep(1.2)
    step("[E] with a bucket on a cow (tip: Milk): a milk bucket", "Milk" in tip and count("Milk Bucket") == 1, (tip, slots()[:80]))
    shot("milking")
    cmd("enemyrelease cow"); cmd("clearenemies 200 all")
    cmd("potions give potion_poison"); cmd("potions give potion_swiftness"); select("Milk Bucket")
    cmd("hunger 20 5"); cmd("mouse right 2.0"); time.sleep(2.6)
    e = cmd("potions")
    step("drunk (when full too): every effect gone, the bucket back", e == "none" and count("Bucket") == 1 and "Milk" not in slots(), (e, slots()[:80]))

def apple():
    # the enchanted golden apple: its glint; eaten, 8 golden hearts, Resistance and Fire Resistance for 5 minutes,
    # Regeneration II for 20 s; Resistance takes a fifth off a hit
    give(("enchanted_golden_apple", 1)); select("Enchanted Golden Apple"); shot("enchanted_apple_held")
    cmd("god 0"); cmd("hp 50"); cmd("potions clear"); cmd("hunger 20 5")
    cmd("mouse right 2.0"); time.sleep(2.6)
    cmd("hunger 17 0")  # (below 18: no natural healing on top of the apple's)
    e = cmd("potions"); a = st("abs")
    step("eaten (when full): 8 golden hearts, Resistance, Fire Resistance 5 min", a == 80 and "Resistance1:2" in e and "FireResistance1:2" in e, (a, e))
    h0 = st("hp"); time.sleep(5.2); h1 = st("hp")
    step("Regeneration II: about a heart in 5 s", 15 <= h1 - h0 <= 25, (h0, h1))
    cmd("hp 100"); time.sleep(0.3); cmd("hurt 100"); time.sleep(0.5)
    step("a 100 hit with Resistance: 20 off, then the golden hearts take theirs", st("hp") > 0, (st("hp"), st("abs")))
    shot("enchanted_apple_eaten")
    cmd("god 1"); cmd("hp 100"); cmd("potions clear")

SECTIONS = {"icons": icons, "craft": craft, "glowstone": glowstone, "melon": melon, "mushrooms": mushrooms, "stews": stews,
            "honey": honey, "milk": milk, "apple": apple}
CHAINS = [
    ("potion_water", "redstone_dust", "potion_mundane"), ("potion_water", "glowstone_dust", "potion_thick"),
    ("potion_water", "sugar", "potion_mundane"), ("potion_water", "fermented_spider_eye", "potion_weakness"),
    ("potion_awkward", "pufferfish", "potion_water_breathing"), ("potion_night_vision", "fermented_spider_eye", "potion_invisibility"),
    ("potion_swiftness", "fermented_spider_eye", "potion_slowness"), ("potion_slowness", "glowstone_dust", "potion_slowness_strong"),
    ("potion_awkward", "glistering_melon_slice", "potion_healing"), ("potion_healing", "fermented_spider_eye", "potion_harming"),
    ("potion_poison", "fermented_spider_eye", "potion_harming"), ("potion_swiftness", "glowstone_dust", "potion_swiftness_strong"),
    ("potion_healing", "gunpowder", "splash_potion_healing"), ("splash_potion_healing", "glowstone_dust", "splash_potion_healing_strong"),
]

def brew():
    # each new brew in a brewing stand, through its screen ([E], shift-clicks), Minecraft's 26.3 mixes
    import pilot
    fc = R.start_flat(56, [(x, z) for x in range(-1, 2) for z in range(0, 4)])
    f = R.stone_floor(fc, range(-1, 2), range(0, 4)); y = f + 1
    R.place("brewing_stand", fc, 0, y, 2); time.sleep(0.6)
    cmd("brewui time 3")
    def open_stand():
        R.stand_on(fc, 0, f + 1, 0)
        pilot.aim_at((fc[0] + .5) * S, (y + .4) * S + R.YO, (fc[2] + 2.5) * S); time.sleep(0.4)
        cmd("keys E 0.1"); time.sleep(0.8)
        return "open=True" in cmd("brewui state")
    def shift_in(*keys):
        hot = re.search(r"hotbar=\[([^\]]*)\]", cmd("brewui state")).group(1).split(",")
        for i, e in enumerate(hot):
            if any(e.split(":")[0] == k for k in keys): cmd(f"brewui click hot {i} shift"); time.sleep(0.5)
    give(("blaze_powder", 1)); open_stand(); shift_in("blaze_powder"); cmd("brewui close"); time.sleep(0.4)
    for start, ing, want in CHAINS:
        give((start, 1), (ing, 1)); time.sleep(0.3)
        if not open_stand(): step(f"{start} + {ing}: the stand opens", False, cmd("brewui state")[:100]); continue
        shift_in(start, ing)
        R.wait(lambda: re.search(r"b0=" + want + r"x", cmd("brewui state")) is not None, 8, step=0.5)
        s1 = cmd("brewui state")
        if want in ("potion_invisibility", "splash_potion_healing"): shot("brew_" + want)
        cmd("brewui click stand 0 shift"); time.sleep(0.6)
        cmd("brewui close"); time.sleep(0.4)
        m = re.search(r"b0=\S+ b1=\S+ b2=\S+ ing=\S+", s1)
        step(f"{start} + {ing} -> {want}", re.search(r"b0=" + want + r"x", s1) is not None, (m.group(0) if m else s1[:80]) + " | " + slots()[:80])

def drink():
    # drunk: healing heals 4 health (20 here), harming hurts 6 (30); water breathing and invisibility on
    cmd("god 0"); cmd("hunger 17 0"); cmd("potions clear")
    give(("potion_healing", 1)); select("Potion of Healing"); cmd("hp 50"); time.sleep(0.3)
    cmd("mouse right 2.0"); time.sleep(2.4)
    step("a potion of healing drunk: +20 at once", st("hp") == 70, st("hp"))
    give(("potion_harming", 1)); select("Potion of Harming"); cmd("hp 100"); time.sleep(0.3)
    cmd("mouse right 2.0"); time.sleep(2.4)
    step("a potion of harming drunk: -30 at once", st("hp") == 70, st("hp"))
    give(("potion_water_breathing", 1), ("potion_invisibility", 1)); select("Potion of Water Breathing")
    cmd("mouse right 2.0"); time.sleep(2.4); select("Potion of Invisibility")
    cmd("mouse right 2.0"); time.sleep(2.4)
    e = cmd("potions"); shot("effects_hud")
    step("water breathing and invisibility on (3:00 each)", "WaterBreathing1:17" in e and "Invisibility1:17" in e, e)
    cmd("potions clear"); cmd("god 1"); cmd("hp 100")

def splash():
    # thrown: a splash potion of healing at your feet heals you (all of it, close); one thrown far off doesn't; harming
    # at your feet hurts
    R.start_flat(57, [(0, 2), (0, 5)])
    cmd("god 0"); cmd("hunger 17 0"); cmd("potions clear")
    give(("splash_potion_healing", 1), ("splash_potion_healing", 1), ("splash_potion_harming", 1)); time.sleep(0.4)
    print("   hotbar:", slots()); shot("splash_held")
    select("Splash Potion of Healing"); cmd("hp 50"); cmd("look 0 80"); time.sleep(0.3)
    cmd("rmb"); time.sleep(1.5)
    step("splash healing thrown at your feet (lands a block off): about three quarters of +20", 13 <= st("hp") - 50 <= 20, (st("hp"), cmd("potions")))
    shot("splash_broken")
    select("Splash Potion of Healing"); cmd("hp 50"); cmd("look 0 -10"); time.sleep(0.3)
    cmd("rmb"); time.sleep(3.0)
    step("thrown far off: nothing", st("hp") == 50, st("hp"))
    select("Splash Potion of Harming"); cmd("hp 100"); cmd("look 0 80"); time.sleep(0.3)
    cmd("rmb"); time.sleep(1.5)
    step("splash harming at your feet: about three quarters of -30", 18 <= 100 - st("hp") <= 30, st("hp"))
    cmd("god 1"); cmd("hp 100")

def invis():
    # invisible (the owner's rules): a forest giant can't see you at all, not even close; inside the facility a zombie
    # 10 m off can't, up close it can; a coil-head still can; in a full set of armor you're seen from further
    def able(txt, name): i = txt.find(name); return txt[i:i + 110] if i >= 0 else ""
    R.start_flat(58, [(0, 2)]); cmd("clearenemies 200 all"); cmd("mobspawns 0"); cmd("potions clear"); time.sleep(1.0)
    cmd("look 0 0"); print("   giant:", cmd("enemy ForestGiant 20")[:80]); time.sleep(2.5)
    g0 = cmd("invis")
    step("visible: a forest giant can target you", "targetable=True" in able(g0, "ForestGiant"), g0[:220])
    cmd("potions give potion_invisibility"); time.sleep(1.5)
    g = cmd("invis")
    step("invisible: the forest giant can't see you at all", "targetable=False" in able(g, "ForestGiant") and "me=True" in g, g[:220])
    shot("invisible_giant")
    cmd("enemyhold forestgiant 3"); time.sleep(0.5); g2 = cmd("invis"); cmd("enemyrelease forestgiant")
    step("not even 3 m off", "targetable=False" in able(g2, "ForestGiant"), g2[:220])
    cmd("clearenemies 200 all"); cmd("potions clear"); time.sleep(1.2)
    # inside: a zombie and a coil-head
    print("   inside:", cmd("tpinside")[:60]); time.sleep(2.5)
    cmd("look 0 0"); cmd("enemy Zombie 10"); time.sleep(2.5)
    z0 = cmd("invis"); print("   zombie:", able(z0, "Zombie"))
    step("visible: the zombie can target you", "targetable=True" in able(z0, "Zombie"), z0[:220])
    cmd("potions give potion_invisibility"); time.sleep(1.5)
    z1 = cmd("invis")
    zd = float(re.search(r"Zombie d=([\d.]+)", z1).group(1)) if "Zombie d=" in z1 else 0
    step(f"invisible: the zombie {zd:.0f} m off can't", zd > 3.5 and "targetable=False" in able(z1, "Zombie"), z1[:220])
    cmd("enemyhold zombie 1.5"); time.sleep(0.5); z2 = cmd("invis"); cmd("enemyrelease zombie")
    step("up close (1.5 m): it can", "targetable=True" in able(z2, "Zombie"), z2[:220])
    cmd("clearenemies 200 all"); time.sleep(0.5)
    cmd("look 0 0"); print("   coil-head:", cmd("enemy Spring 10")[:80]); time.sleep(2.5)
    c = cmd("invis")
    step("a coil-head still can (it doesn't hunt by sight alone)", "targetable=True" in able(c, "Spring"), c[:220])
    cmd("clearenemies 200 all")
    cmd("wear iron_helmet iron_chestplate iron_leggings iron_boots"); time.sleep(0.5)
    step("in a full set of iron: seen within 35 m", "seenWithin=35.0" in cmd("invis"), cmd("invis")[:80])
    cmd("wear none"); cmd("potions clear"); time.sleep(1.2)
    step("the potion over: visible again", "me=False" in cmd("invis"), cmd("invis")[:60])
    cmd("tpship"); time.sleep(1.0)

def wither():
    # in the Nether, a wither skeleton's hit: Wither for 10 s, its black hearts, half a heart every 2 s (it can kill,
    # unlike poison)
    if "tiles=" not in cmd("nether"): cmd("nether gen"); time.sleep(1.0)
    cmd("nether tp"); time.sleep(1.5); cmd("clearenemies 200 all"); cmd("mobspawns 0"); cmd("potions clear")
    cmd("god 0"); cmd("hp 100"); cmd("hunger 17 0")
    cmd("look 0 0"); print("   ", cmd("enemy wither 3")[:80]); time.sleep(0.5); cmd("provoke wither")
    R.wait(lambda: "Wither1" in cmd("potions"), 15, step=0.2)
    e = cmd("potions"); h0 = st("hp")
    cmd("clearenemies 200 all")
    step("a wither skeleton's hit: Wither for 10 s", "Wither1" in e, (e, h0))
    time.sleep(0.3); shot("wither_hearts")
    time.sleep(4.1); h1 = st("hp")
    step("Wither: half a heart (5) every 2 s", 8 <= h0 - h1 <= 12, (h0, h1))
    cmd("potions clear"); cmd("potions give potion_poison"); time.sleep(0.4); shot("poison_hearts")
    cmd("potions clear"); cmd("god 1"); cmd("hp 100")

SECTIONS.update({"brew": brew, "drink": drink, "splash": splash, "invis": invis, "wither": wither})

if __name__ == "__main__":
    R.grid_yo()
    cmd("god 1"); cmd("dayfreeze 1"); cmd("gamemode survival")
    for s in sys.argv[1:]: print("==", s); SECTIONS[s]()
    print(f"\n{sum(1 for _, ok in log if ok)}/{len(log)} fine")
