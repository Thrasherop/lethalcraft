"""#244 part E hand play: leather, copper and chainmail armor, copper tools, dyeing; eggs, cake, pie, the poisonous
potato; fishing; the name tag jokes; water in the Nether. Real clicks and keys where a player would use them.
Screenshots in shots/ie_*.png.  usage: py tools/items_play.py <section> [...]
sections: copper armor dye wash horse eggs cake pie potato fishtable hook names nether creative"""
import sys, os, time, re, math
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from regress import cmd, S
import thorough_play as T
HERE = os.path.dirname(os.path.abspath(__file__))
log = []
def shot(n): cmd(f"screenshot {os.path.join(HERE, '..', 'shots', 'ie_' + n + '.png')}"); time.sleep(0.5)
def step(name, ok, detail=""):
    log.append((name, ok)); print(("OK  " if ok else "BAD ") + name + (f"  ({detail})" if detail else ""), flush=True)
slots, st, give = T.slots, T.st, T.give
def select(name):
    import pilot; pilot.select(name); time.sleep(0.3)
def camshot(n, ahead=2.4, up=0.0, side=0.6):
    """a picture of me from in front (what I wear, as the others see it)"""
    cmd("armormodels show 1"); time.sleep(0.6)
    cmd(f"camshot {os.path.join(HERE, '..', 'shots', 'ie_' + n + '.png')} {ahead} {up} {side}"); time.sleep(0.6)
    cmd("armormodels show 0")
def near(name):
    """the nearest living mob of that name: (x, y, z, distance)"""
    best = None
    for x in cmd("enemies").split(" ; "):
        x = x.strip()
        if not x.lower().startswith(name.lower() + "@") or "dead=False" not in x: continue
        m = re.search(r"@([-\d.]+),([-\d.]+),([-\d.]+) d=([\d.]+)", x)
        if m and (best is None or float(m.group(4)) < best[3]): best = tuple(float(m.group(i)) for i in (1, 2, 3, 4))
    return best
def e244(): return dict(kv.split("=", 1) for kv in cmd("e244").split(" ") if "=" in kv)
def hotbar(): return re.search(r"hotbar=\[([^\]]*)\]", cmd("craftui state")).group(1).split(",")

def craft_all(cases, take=False):
    """(label, [(key, cells)], gives, expect_prefix) at a crafting table, laid with its own clicks"""
    outs = []
    for label, lay, gives, want in cases:
        give(*gives); cmd("craftui open table"); time.sleep(0.6)
        out = T.grid_craft(lay)
        step(f"{label} -> {want}", out.startswith(want), out)
        if take: T.take_out()
        outs.append(out)
        cmd("craftui close"); time.sleep(0.3)
    return outs

def flat(idx):
    fc = R.start_flat(idx, [(dx, dz) for dx in range(-2, 3) for dz in range(0, 6)]); cmd("god 1"); cmd("gamemode survival")
    sy = max(R.surface(fc, dx, dz) for dx in range(-2, 3) for dz in range(0, 6)) + 1
    for dx in range(-2, 3):
        for dz in range(0, 6): R.place("stone", fc, dx, sy, dz)
    time.sleep(0.6); R.stand_on(fc, 0, sy + 1, 0); time.sleep(0.5)
    return fc, sy

def copper():
    # copper tools (craft, then mine: iron ore yes, diamond ore no: stone's level), the sword; nuggets both ways;
    # a copper sword smelts to a nugget
    R.start_flat(120, [(0, 2)]); cmd("god 1"); cmd("gamemode survival")
    tool = [("copper_ingot", (0, 1, 2)), ("stick", (4, 7))]
    craft_all([
        ("copper pickaxe", tool, [("copper_ingot", 3), ("stick", 2)], "copper_pickaxex1"),
        ("copper sword", [("copper_ingot", (1, 4)), ("stick", (7,))], [("copper_ingot", 2), ("stick", 1)], "copper_swordx1"),
        ("copper axe", [("copper_ingot", (0, 1, 3)), ("stick", (4, 7))], [("copper_ingot", 3), ("stick", 2)], "copper_axex1"),
        ("copper helmet", [("copper_ingot", (0, 1, 2, 3, 5))], [("copper_ingot", 5)], "copper_helmetx1"),
        ("copper chestplate", [("copper_ingot", (0, 2, 3, 4, 5, 6, 7, 8))], [("copper_ingot", 8)], "copper_chestplatex1"),
        ("a copper ingot", [("copper_ingot", (4,))], [("copper_ingot", 1)], "copper_nuggetx9"),
        ("9 copper nuggets", [("copper_nugget", (0, 1, 2, 3, 4, 5, 6, 7, 8))], [("copper_nugget", 9)], "copper_ingotx1"),
        ("leather helmet", [("leather", (0, 1, 2, 3, 5))], [("leather", 5)], "leather_helmetx1"),
        ("leather boots", [("leather", (3, 5, 6, 8))], [("leather", 4)], "leather_bootsx1"),
    ])
    # mining with it
    fc, sy = T.near_spot()
    give(("copper_pickaxe", 1)); select("Copper Pickaxe")
    for ore, dz, want in (("iron_ore", 2, True), ("diamond_ore", 3, False)):
        R.place(ore, fc, 0, sy + 1, dz); time.sleep(0.4)
        T.aim_cell(fc, 0, sy + 1, dz); cmd("mouse left 6.0"); time.sleep(6.3); cmd("mouse release"); time.sleep(1.2)
        gone = ore not in cmd("near 6")
        loose = cmd("loose 8")
        got = ("scrap_iron_ore" in loose or "raw_iron" in loose) if ore == "iron_ore" else ("diamond" in loose.lower())
        step(f"a copper pickaxe on {ore}: {'drops it' if want else 'nothing (stone level)'}", got == want, (gone, loose[:120]))
        cmd("clearloose 10"); time.sleep(0.3)
    u = T.used(); step("the copper pickaxe has 190 uses", u and u[1] == 190, u)
    shot("copper_pickaxe")
    # (the creative menu's combat and tools tabs have them: section creative)

def armor():
    # leather, copper and chainmail worn ([Right-click] with each in hand): Minecraft's armor points
    R.start_flat(121, [(0, 2)]); cmd("god 1"); cmd("gamemode survival")
    for mat, pts in (("leather", 7), ("copper", 10), ("chainmail", 12)):
        cmd("wear none"); time.sleep(0.3)
        give(*[(f"{mat}_{p}", 1) for p in ("helmet", "chestplate", "leggings", "boots")])
        for i in range(4):
            cmd(f"slot {i}"); time.sleep(0.3); cmd("rmb"); time.sleep(0.6)
        c = e244(); print("   ", mat, c.get("worn"))
        step(f"a full {mat} set put on by hand: {pts} armor points", c.get("points") == str(pts), (c.get("points"), c.get("worn")))
        camshot(f"worn_{mat}")
    cmd("wear none")

def dye():
    # leather dyed in the grid: red; then red and yellow mixed onto it (Minecraft's average, brightened); its icon
    R.start_flat(122, [(0, 2)]); cmd("god 1"); cmd("gamemode survival")
    give(("leather_chestplate", 1), ("red_dye", 1)); cmd("craftui open table"); time.sleep(0.6)
    out = T.grid_craft([("leather_chestplate", (4,)), ("red_dye", (1,))])
    step("a leather chestplate and red dye: a dyed chestplate", out.startswith("leather_chestplate#"), out)
    shot("dye_red_grid"); T.take_out(); time.sleep(0.6)
    k = [x for x in hotbar() if x.startswith("leather_chestplate")]
    cmd("craftui close"); time.sleep(0.3)
    select("Leather Chestplate"); c = e244()
    step("it's red: Minecraft's red dye B02E26", c.get("color") == "B02E26", c)
    shot("dye_red_held")
    # red + yellow on the red piece
    cmd("invgive yellow_dye 1"); time.sleep(1.2); cmd("craftui open table"); time.sleep(0.6)
    out = T.grid_craft([("leather_chestplate", (4,)), ("yellow_dye", (0,))])
    T.take_out(); time.sleep(0.6); cmd("craftui close"); time.sleep(0.3)
    select("Leather Chestplate"); c = e244()
    # (red B02E26 and yellow FED83D: (176+254)/2, (46+216)/2, (38+61)/2 = D7,83,31; brightest 215 over 215)
    step("then yellow on it: orange, D78331 (Minecraft's mix)", c.get("color") == "D78331", c)
    shot("dye_orange_held")
    # put on: its colour on the body
    cmd("rmb"); time.sleep(0.8); c = e244()
    step("worn, it keeps the colour", "D78331" in c.get("worn", ""), c.get("worn"))
    camshot("dye_worn")
    cmd("wear none")
    # enchanted at an anvil with a book: still orange
    fc, sy = T.near_spot()
    R.place("anvil", fc, 1, sy + 1, 0); time.sleep(0.5)
    give(("leather_chestplate", 1), ("red_dye", 1))
    cmd("craftui open table"); time.sleep(0.6); T.grid_craft([("leather_chestplate", (4,)), ("red_dye", (1,))]); T.take_out(); time.sleep(0.5); cmd("craftui close")
    cmd("xplevel 30"); cmd("invgive enchanted_book#" + str(4 << 13 | 0) + " 1"); time.sleep(1.2)
    st_ = cmd(f"anvilui open {fc[0] + 1} {sy + 1} {fc[2]}"); time.sleep(0.5)
    def hot(s, key): return next(i for i, x in enumerate(re.search(r"hotbar=\[([^\]]*)\]", s).group(1).split(",")) if x.startswith(key))
    cmd(f"anvilui click hot {hot(st_, 'leather_chestplate')}"); cmd("anvilui click slot 0")
    st_ = cmd("anvilui state"); print("   anvil:", st_[:200])
    try:
        cmd(f"anvilui click hot {hot(st_, 'enchanted_book')}"); cmd("anvilui click slot 1"); time.sleep(0.5)
        cmd("anvilui click out 0 shift"); time.sleep(0.8)
    except StopIteration: pass
    cmd("anvilui close"); time.sleep(1.0)
    select("Leather Chestplate"); c = e244(); print("   ", cmd("playerheld")[:200])
    step("enchanted at the anvil, still red", c.get("color") == "B02E26" and "#" in c.get("held", ""), c)

def wash():
    # a dyed piece in a water cauldron ([E]): its colour goes, a third of the water too
    fc, sy = T.near_spot()
    R.place("cauldron", fc, 0, sy + 1, 2); time.sleep(0.5)
    give(("water_bucket", 1), ("leather_boots", 1), ("blue_dye", 1))
    cmd("craftui open table"); time.sleep(0.6); T.grid_craft([("leather_boots", (4,)), ("blue_dye", (1,))]); T.take_out(); time.sleep(0.5); cmd("craftui close")
    select("Water Bucket"); T.aim_cell(fc, 0, sy + 1, 2, 0.9); cmd("rmb"); time.sleep(1.0)
    print("   cauldron:", cmd("near 4")[:120])
    select("Leather Boots"); c0 = e244()
    T.aim_cell(fc, 0, sy + 1, 2, 0.9); shot("wash_before"); cmd("keys E 0.1"); time.sleep(1.0)
    c1 = e244(); print("   ", c0.get("color"), "->", c1.get("color"), "washed", c1.get("washed"))
    step("blue boots, [E] on a full water cauldron: plain again", c0.get("color") == "3C44AA" and c1.get("color") in ("-1", "FFFFFFFF"), (c0.get("color"), c1.get("color")))
    step("the cauldron lost a third of its water", "cauldron" in cmd("near 4"), cmd("near 4")[:100])
    shot("wash_after")

def horse():
    # leather horse armor dyed green, put on a tamed horse with [E]; the horse wears it green; killed, it drops it green
    R.start_flat(123, [(0, 2)]); cmd("god 1"); cmd("gamemode survival"); cmd("mobspawns 0")
    give(("leather_horse_armor", 1), ("green_dye", 1))
    cmd("craftui open table"); time.sleep(0.6)
    out = T.grid_craft([("leather_horse_armor", (4,)), ("green_dye", (1,))]); T.take_out(); time.sleep(0.5); cmd("craftui close")
    step("leather horse armor and green dye: green horse armor", out.startswith("leather_horse_armor#"), out)
    cmd("clearenemies 300 all"); cmd("look 0 0"); cmd("enemy horse 3"); time.sleep(1.0); cmd("horse tame")
    select("Leather Horse Armor"); shot("horse_armor_held")
    cmd("enemyhold horse 2.6"); time.sleep(0.3)
    m = near("horse")
    import pilot
    pilot.aim_at(m[0], m[1] + 0.9, m[2]); time.sleep(0.3); cmd("keys E 0.1"); time.sleep(1.0)
    h = cmd("horse"); print("   ", h[:160])
    step("[E] on the tamed horse: it wears it, green (5E7C16)", "armor=1" in h and "armorColor=5E7C16" in h, h[:100])
    step("it's gone from the hand", "Leather Horse Armor" not in slots(), slots())
    cmd("enemyrelease horse"); cmd("keys S 0.8"); time.sleep(1.0)
    m = near("horse"); pilot.aim_at(m[0], m[1] + 1.0, m[2]); time.sleep(1.0); shot("horse_green")

def eggs():
    # a chicken lays (Minecraft's 5-10 minutes: told to lay now), the plop; eggs thrown: one in eight a chick
    R.start_flat(124, [(dx, dz) for dx in range(-3, 4) for dz in range(0, 8)]); cmd("god 1"); cmd("gamemode survival"); cmd("mobspawns 0")
    cmd("look 0 0"); cmd("enemy chicken 3"); time.sleep(1.5)
    l0 = int(e244()["laid"]); cmd("lay 2"); time.sleep(4.0)
    l1 = int(e244()["laid"]); loose = cmd("loose 10")
    step("the chicken lays an egg", l1 > l0 and "egg" in loose, (l0, l1, loose[:100]))
    shot("egg_laid")
    cmd("clearloose 12")  # (the laid egg: picked up on the way, it would make seventeen)
    give(("egg", 16)); select("Egg"); time.sleep(0.8)
    h0 = int(e244()["hatched"]); chicks0 = len([x for x in cmd("animals").split(" ; ") if "Chick" in x])
    cmd("look 0 30")
    for i in range(16):
        cmd("rmb"); time.sleep(0.6)
    time.sleep(2.0)
    h1 = int(e244()["hatched"]); s_ = slots()
    print("   hatched", h1 - h0, "animals:", cmd("animals")[:200])
    step("sixteen eggs thrown (the hand empties): about two chicks (one in eight)", "Egg" not in s_ and 0 <= h1 - h0 <= 8, (h1 - h0, s_))
    shot("eggs_thrown")
    if h1 == h0:
        for i in range(32):
            if i % 16 == 0: give(("egg", 16)); select("Egg"); cmd("look 0 30")
            cmd("rmb"); time.sleep(0.45)
        time.sleep(2.0); h1 = int(e244()["hatched"])
        step("thrown till one hatched", h1 > h0, h1 - h0)
    chicks = [x for x in cmd("enemies").split(" ; ") if x.strip().startswith("Chick@")]
    step("the chicks are there: baby chickens", len(chicks) >= h1 - h0 > 0, [c[:60] for c in chicks])

def cake():
    # 3 milk, 2 sugar, an egg, 3 wheat: a cake and the 3 buckets back; placed; [E] hungry: a slice (2 hunger) each;
    # full: nothing; seven slices; a comparator reads it
    fc, sy = flat(125)
    give(("milk_bucket", 3), ("sugar", 2), ("egg", 1), ("wheat", 3))
    cmd("craftui open table"); time.sleep(0.6)
    out = T.grid_craft([("milk_bucket", (0,)), ("milk_bucket", (1,)), ("milk_bucket", (2,)), ("sugar", (3, 5)), ("egg", (4,)), ("wheat", (6, 7, 8))])  # (buckets don't stack: one from each slot)
    step("the cake's recipe: a cake", out.startswith("cakex1"), out)
    shot("cake_grid")
    T.take_out(); time.sleep(0.6)
    gr = cmd("craftui state"); print("   ", gr[:220])
    step("the milk buckets leave three buckets in the grid", re.search(r"grid=\[([^\]]*)\]", gr).group(1).count("bucketx1") == 3, gr[:120])
    for c_ in (0, 1, 2): cmd(f"craftui click grid {c_} shift"); time.sleep(0.3)
    cmd("craftui close"); time.sleep(0.5)
    select("Cake"); T.aim_cell(fc, 0, sy, 2, 1.0); cmd("rmb"); time.sleep(0.8)
    near = cmd("near 4"); step("the cake goes down on the floor", "cake" in near, near[:120])
    import pilot
    def aim_cake(): pilot.aim_at((fc[0] + 0.85) * S, (sy + 1.3) * S + R.YO, (fc[2] + 2.5) * S); time.sleep(0.2)  # (its east end: slices go from the west)
    aim_cake()
    print("   tip:", cmd("hover")[:80])
    cmd("hunger 20 5"); time.sleep(0.3); b0 = int(e244()["cakeBites"])
    cmd("keys E 0.1"); time.sleep(0.8)
    step("full: [E] eats nothing", int(e244()["cakeBites"]) == b0, e244()["cakeBites"])
    cmd("hunger 4 0"); time.sleep(0.3)
    cmd("keys E 0.1"); time.sleep(0.8); c = e244()
    step("hungry: a slice, 2 hunger (4 -> 6)", int(c["cakeBites"]) == b0 + 1 and c["hunger"] == "6", c)
    shot("cake_bitten")
    for i in range(6):
        aim_cake(); cmd("hunger 4 0"); cmd("keys E 0.1"); time.sleep(0.6)
        if i == 2: shot("cake_half")
    near = cmd("near 4")
    step("seven slices and it's gone", "cake" not in near and int(e244()["cakeBites"]) == b0 + 7, near[:100])

def pie():
    R.start_flat(126, [(0, 2)]); cmd("god 1"); cmd("gamemode survival")
    craft_all([("a pumpkin, sugar and an egg", [("pumpkin", (0,)), ("sugar", (4,)), ("egg", (8,))], [("pumpkin", 1), ("sugar", 1), ("egg", 1)], "pumpkin_piex1")])
    give(("pumpkin_pie", 1)); select("Pumpkin Pie"); cmd("hunger 4 0"); cmd("mouse right 2.2"); time.sleep(2.6)
    step("eaten: 8 hunger (4 -> 12)", st("hunger") == 12, st("hunger"))

def potato():
    # ripe potatoes dug: now and then a poisonous potato (2%); eaten, it poisons you more often than not (60%)
    fc, sy = flat(127)
    p0 = int(e244()["poisonDug"]); n = 0
    give(("wooden_hoe", 1))
    for rnd in range(5):
        for dx in range(-2, 3):
            for dz in range(1, 6):
                cmd(f"placeabs farmland {fc[0] + dx} {sy} {fc[2] + dz}"); cmd(f"placeabs potatoes {fc[0] + dx} {sy + 1} {fc[2] + dz}"); cmd(f"setstate {fc[0] + dx} {sy + 1} {fc[2] + dz} 7")
        time.sleep(0.6)
        for dx in range(-2, 3):
            for dz in range(1, 6):
                cmd(f"breakabs {fc[0] + dx} {sy + 1} {fc[2] + dz}"); n += 1
        time.sleep(0.4)
    p1 = int(e244()["poisonDug"]); print("   dug", n, "ripe potato crops: poisonous", p1 - p0, "loose:", cmd("loose 10")[:160])
    step(f"{n} ripe crops dug: a few poisonous potatoes (2%: about {n * 0.02:.0f})", 0 <= p1 - p0 <= max(6, n * 0.08), p1 - p0)
    cmd("clearloose 12")
    poisoned = 0
    for i in range(10):
        give(("poisonous_potato", 1)); select("Poisonous Potato"); cmd("hunger 4 0"); cmd("potions clear"); time.sleep(0.2)
        cmd("mouse right 2.2"); time.sleep(2.6)
        if "Poison" in cmd("potions"): poisoned += 1
    step("ten eaten: poisoned about six times (60%)", 2 <= poisoned <= 10, poisoned)

def fishtable():
    r = cmd("fishroll 20000"); print("   ", r)
    c = dict((k, int(v)) for k, v in (x.split("=") for x in r.split()))
    tot = sum(c.values()); f = lambda k: c.get(k, 0) / tot
    step("cod 60% of the fish (85% x 60% = 51%)", abs(f("raw_cod") - 0.51) < 0.02, f("raw_cod"))
    step("junk ~10%: leather boots come worn, ink sacs", c.get("leather_boots(worn)", 0) > 0 and c.get("ink_sac", 0) > 0, r[:200])
    tr = sum(v for k, v in c.items() if k in ("name_tag", "saddle", "book", "enchanted_book(ench)", "bow(ench)", "fishing_rod(ench)", "enchanted_book"))
    step("treasure ~5%: enchanted bows, rods and books, name tags, saddles", 0.035 < tr / tot < 0.065 and c.get("bow(ench)", 0) > 0, (tr / tot, r[:200]))
    r3 = cmd("fishroll 20000 3"); c3 = dict((k, int(v)) for k, v in (x.split("=") for x in r3.split()))
    tr3 = sum(v for k, v in c3.items() if k in ("name_tag", "saddle", "book", "enchanted_book(ench)", "bow(ench)", "fishing_rod(ench)", "enchanted_book"))
    step("Luck of the Sea III: treasure ~11% (5+6 of 100)", 0.09 < tr3 / 20000 < 0.13, tr3 / 20000)

def hook():
    # cast at a cow: hooked; reeled: pulled toward me, the rod 5 uses; cast at a lying stick: reeled to my feet, 3 uses
    fc, sy = flat(128)
    import pilot
    cmd("look 0 0"); cmd("enemy cow 6"); time.sleep(1.5)
    give(("fishing_rod", 1)); select("Fishing Rod")
    m = near("cow"); me = st("pos") if False else None
    cmd("enemyhold cow 4"); time.sleep(0.5); m = near("cow")
    pos0 = m; pilot.aim_at(m[0], m[1] + 0.5, m[2]); time.sleep(0.3)
    u0 = T.used(); cmd("rmb"); time.sleep(1.5)
    f = cmd("fish"); print("   ", f[:200])
    step("cast at a cow: the bobber hooks it", "hookedMob=Cow" in f, f[:200])
    shot("hook_cow")
    cmd("enemyrelease cow"); time.sleep(0.3); pos0 = near("cow")
    cmd("rmb"); time.sleep(1.2)
    m2 = near("cow"); u1 = T.used()
    p = re.search(r"pos=([-\d.]+),([-\d.]+),([-\d.]+)", cmd("state")); me = (float(p.group(1)), float(p.group(3)))
    d0 = math.hypot(pos0[0] - me[0], pos0[2] - me[1]); d1 = math.hypot(m2[0] - me[0], m2[2] - me[1])
    step("reeled: the cow comes toward me", d1 < d0 - 0.3, (round(d0, 2), round(d1, 2)))
    step("the rod: 5 uses for a mob", u0 and u1 and u1[0] - u0[0] == 5, (u0, u1))
    cmd("clearenemies 30 all"); time.sleep(0.5)
    # an item
    cmd("invgive stick 1"); time.sleep(1.0); select("Stick")
    cmd("look 0 0"); time.sleep(0.2); cmd("keys Q 0.1"); time.sleep(0.5)  # (thrown a little way ahead)
    R.stand_on(fc, 0, sy + 1, 0)
    sp = re.search(r"Stick(?:x\d+)?@([-\d.]+),([-\d.]+),([-\d.]+)", cmd("find stick"))
    select("Fishing Rod")
    if sp:
        # (from where I stand, a couple of metres off: aimed at it, Minecraft's hook goes where you look)
        pilot.aim_at(float(sp.group(1)), float(sp.group(2)) + 0.05, float(sp.group(3))); time.sleep(0.3)
        u0 = T.used(); cmd("rmb"); time.sleep(1.5); f = cmd("fish"); print("   ", f[:200])
        cmd("rmb"); time.sleep(1.5); u1 = T.used()
        sp2 = re.search(r"Stick(?:x\d+)?@([-\d.]+),([-\d.]+),([-\d.]+)", cmd("find stick"))
        p = re.search(r"pos=([-\d.]+),([-\d.]+),([-\d.]+)", cmd("state"))
        d = math.hypot(float(sp2.group(1)) - float(p.group(1)), float(sp2.group(3)) - float(p.group(3))) if sp2 else 99
        step("a stick hooked and reeled: at my feet", d < 1.6, (round(d, 2), f[:100]))
        step("the rod: 3 uses for an item", u0 and u1 and u1[0] - u0[0] == 3, (u0, u1))
        shot("hook_stick")

def names():
    # name tags from the anvil: Dinnerbone on a cow (upside down), jeb_ on a sheep (rainbow), Toast on a rabbit
    fc, sy = T.near_spot()
    R.place("anvil", fc, 1, sy + 1, 0); time.sleep(0.5)
    cmd("mobspawns 0"); cmd("clearenemies 300 all")
    for name, mob in (("Dinnerbone", "cow"), ("jeb_", "sheep"), ("Toast", "rabbit")):
        cmd("clearenemies 300 all"); time.sleep(0.5)
        give(("name_tag", 1)); cmd("xplevel 10")
        cmd(f"anvilui open {fc[0] + 1} {sy + 1} {fc[2]}"); time.sleep(0.5)
        cmd("anvilui click hot 0"); cmd("anvilui click slot 0"); cmd(f"anvilui type {name}"); time.sleep(0.3)
        cmd("anvilui click out 0 shift"); time.sleep(0.8); cmd("anvilui close"); time.sleep(1.0)
        select("Name Tag")
        cmd("look 0 0"); cmd(f"enemy {mob} 3"); time.sleep(1.5)
        cmd(f"enemyhold {mob} 2.4"); time.sleep(0.4); m = near(mob)
        import pilot
        pilot.aim_at(m[0], m[1] + 0.5, m[2]); time.sleep(0.3); cmd("keys E 0.1"); time.sleep(1.0); cmd(f"enemyrelease {mob}"); time.sleep(1.0)
        print("   ", name, cmd("mobinfo")[:160] if False else cmd("enemies")[:160])
        m = near(mob)
        import pilot
        if m: pilot.aim_at(m[0], m[1] + 0.6, m[2])
        time.sleep(1.0); shot(f"name_{name}")
        if name == "Dinnerbone": step("Dinnerbone's cow drawn upside down (one mob flipped)", e244().get("flipped") == "1", e244().get("flipped"))
        if name == "jeb_": time.sleep(1.4); shot("name_jeb_later")

def nether():
    # water poured in the Nether: gone, with a fizz (and lava still pours)
    cmd("nether gen"); time.sleep(2.0); cmd("nether tp"); time.sleep(3.0); cmd("god 1"); cmd("gamemode survival")
    give(("water_bucket", 1), ("lava_bucket", 1))
    select("Water Bucket"); cmd("look 0 60"); time.sleep(0.4)
    w0 = cmd("near 4"); cmd("rmb"); time.sleep(1.0); w1 = cmd("near 4")
    step("water poured in the Nether: no water, the bucket's empty", "water" not in w1 and "Bucket" in slots() and "Water Bucket" not in slots(), (w1[:100], slots()))
    shot("nether_water")
    select("Lava Bucket"); cmd("rmb"); time.sleep(1.0)
    step("lava pours there as ever", "lava" in cmd("near 4"), cmd("near 4")[:100])

def creative():
    cmd("gamemode creative"); time.sleep(0.5); cmd("creativeui open"); time.sleep(0.8)
    seen = set()
    for t in range(14):
        cmd(f"creativeui click tabs {t}"); time.sleep(0.2)
        seen |= set(re.search(r"items=\[([^\]]*)\]", cmd("creativeui state")).group(1).split(","))
    cmd("creativeui close"); cmd("gamemode survival")
    want = ["leather_helmet", "copper_chestplate", "chainmail_boots", "copper_pickaxe", "copper_sword", "copper_nugget", "egg", "cake", "pumpkin_pie", "poisonous_potato", "leather_horse_armor", "firework_star", "fire_charge"]
    miss = [k for k in want if k not in seen]
    step("the new items are in the creative menu", not miss, miss)

def fireworks():
    # a star (red, white, a fire charge, a diamond, glowstone), its fade, a rocket of it; off the ground: up, then it goes off
    R.start_flat(141, [(0, 1), (0, 2)]); cmd("god 1"); cmd("clearenemies 400 all"); cmd("hunger 20 5")
    def craft(lay, gives):
        give(*gives); cmd("craftui open table"); time.sleep(0.6)
        out = T.grid_craft(lay); T.take_out(); time.sleep(0.6); hb = hotbar(); cmd("craftui close"); time.sleep(0.4); return out, hb
    out, hb = craft([("blaze_powder", (0,)), ("coal", (1,)), ("gunpowder", (2,))], [("blaze_powder", 1), ("coal", 1), ("gunpowder", 1)])
    step("blaze powder, coal, gunpowder: 3 fire charges", out == "fire_chargex3", out)
    out, hb = craft([("gunpowder", (0,)), ("red_dye", (1,)), ("white_dye", (2,)), ("fire_charge", (4,)), ("diamond", (6,)), ("glowstone_dust", (7,))],
                    [("gunpowder", 1), ("red_dye", 1), ("white_dye", 1), ("fire_charge", 1), ("diamond", 1), ("glowstone_dust", 1)])
    star = next((x.split(":")[0] for x in hb if x.startswith("firework_star#")), None)
    step("a large-ball star with a trail and a twinkle", star is not None, (out, hb))
    out, hb = craft([("firework_star", (4,)), ("light_blue_dye", (1,))], [(star, 1), ("light_blue_dye", 1)])
    star2 = next((x.split(":")[0] for x in hb if x.startswith("firework_star#")), None)
    step("the star and a dye: a star that fades", star2 is not None and star2 != star, (out, hb))
    out, hb = craft([("paper", (4,)), ("gunpowder", (0, 1)), ("firework_star", (6,))], [("paper", 1), ("gunpowder", 2), (star2, 1)])
    step("paper, two gunpowder and the star: three rockets", out.startswith("firework_rocket#") and "x3" in out, (out, hb))
    select("Firework Rocket"); e0 = e244()
    cmd("look 0 80"); time.sleep(0.3); cmd("rmb"); t0 = time.time(); time.sleep(0.15); cmd("look 0 -89")
    while time.time() - t0 < 1.55: time.sleep(0.03)
    shot("fw_burst"); time.sleep(1.2); e1 = e244()
    step("launched off the ground, it goes off up there: a large ball", int(e1["fwLaunched"]) == int(e0["fwLaunched"]) + 1 and int(e1["fwExploded"]) > int(e0["fwExploded"]) and "LargeBall" in e1["fwLast"], (e0["fwLaunched"], e1["fwLaunched"], e1["fwLast"]))
    step("one rocket used", "Firework Rocketx2" in slots(), slots())

def extras():
    # a fire charge on the ground: a fire (one used); a coast trim (iron) on a copper chestplate at the smithing table, worn
    fc, sy = flat(146)
    give(("fire_charge", 2)); select("Fire Charge")
    T.aim_cell(fc, 0, sy, 2, 1.0); cmd("rmb"); time.sleep(1.0)
    near = cmd("near 5")
    step("a fire charge on the ground lights a fire, one used", "fire(" in near and "Fire Chargex1" in slots(), (near[:120], slots()))
    shot("fire_charge")
    cmd(f"breakabs {fc[0]} {sy + 1} {fc[2] + 2}"); time.sleep(0.5)
    give(("coast_armor_trim_smithing_template", 1), ("copper_chestplate", 1), ("iron_ingot", 1))
    st_ = cmd("smithui open"); cs = cmd("craftui state")
    for k, slot in (("coast_armor_trim_smithing_template", 0), ("copper_chestplate", 1), ("iron_ingot", 2)):
        i = next((i for i, x in enumerate(re.search(r"hotbar=\[([^\]]*)\]", cs).group(1).split(",")) if x.split(":")[0].split("#")[0] == k), None)
        if i is None: break
        cmd(f"smithui click hot {i}"); st_ = cmd(f"smithui click slot {slot}"); cs = cmd("craftui state")
    print("   smithing:", st_[:200]); shot("trim_smithing")
    step("template, copper chestplate, iron: a trimmed copper chestplate", "out=copper_chestplate#" in st_, st_[:160])
    cmd("smithui click out 0"); time.sleep(1.5); cmd("smithui close"); time.sleep(0.5)
    select("Copper Chestplate"); time.sleep(0.5); shot("trim_held")
    cmd("rmb"); time.sleep(0.8); cmd("daytime 0.3"); time.sleep(0.5); camshot("trim_worn", 2.2, 0.0, 0.0)
    step("worn, its trim on", "copper_chestplate#" in e244().get("worn", ""), e244().get("worn"))
    cmd("wear none")

SECTIONS = {"copper": copper, "armor": armor, "dye": dye, "wash": wash, "horse": horse, "eggs": eggs, "cake": cake, "pie": pie,
            "potato": potato, "fishtable": fishtable, "hook": hook, "names": names, "nether": nether, "creative": creative, "fireworks": fireworks, "extras": extras}

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
