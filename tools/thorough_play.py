"""#244 part A hand play: each of Minecraft 26.3's numbers and rules put in, tried in the game as a player would (the
screens' own clicks, real right/left clicks, real mobs). Screenshots in shots/th_*.png.
usage: py tools/thorough_play.py <section> [...]   sections: craft stacks furnace food piston"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from regress import cmd, S
HERE = os.path.dirname(os.path.abspath(__file__))
log = []
def shot(n): cmd(f"screenshot {os.path.join(HERE, '..', 'shots', 'th_' + n + '.png')}"); time.sleep(0.5)
def step(name, ok, detail=""):
    log.append((name, ok)); print(("OK  " if ok else "BAD ") + name + (f"  ({detail})" if detail else ""), flush=True)
def slots(): return re.search(r"slots=\[([^\]]*)\]", cmd("state")).group(1)
def st(k): m = re.search(k + r"=([-\d.]+)", cmd("state")); return float(m.group(1)) if m else None
def give(*kv):
    cmd("clearinv"); time.sleep(0.3)
    for k, n in kv: cmd(f"invgive {k} {n}")
    time.sleep(2.0)

def grid_craft(items):
    """items: [(key, cells)]: lay them into the open table's grid with its own clicks; returns what the output shows"""
    for key, cells in items:
        hb = re.search(r"hotbar=\[([^\]]*)\]", cmd("craftui state")).group(1).split(",")
        i = next((i for i, x in enumerate(hb) if x.split(":")[0].split("#")[0] == key), None)
        if i is None: return f"no {key} in the hotbar {hb}"
        cmd(f"craftui click hot {i}")
        for c in cells: cmd(f"craftui click grid {c} right")
        cmd(f"craftui click hot {i}")
    time.sleep(0.4)
    return re.search(r"out=(\S+)", cmd("craftui state")).group(1)

def take_out():
    cmd("craftui click out 0 shift"); time.sleep(0.4)
    return re.search(r"hotbar=\[([^\]]*)\]", cmd("craftui state")).group(1)

def craft():
    # (items 3, 4, 6) at a crafting table: the observer and comparator with quartz, the copper trapdoor 2x2 -> 1, and
    # Minecraft's shapeless ones laid out scattered (a book, magma cream, a rocket, a netherite ingot)
    cases = [
        ("observer: cobble / redstone redstone quartz / cobble", [("cobblestone", (0, 1, 2, 6, 7, 8)), ("redstone_dust", (3, 4)), ("quartz", (5,))], "observer"),
        ("comparator: torch / torch quartz torch / stone x3", [("redstone_torch", (1, 3, 5)), ("quartz", (4,)), ("stone", (6, 7, 8))], "comparator"),
        ("copper trapdoor: 4 ingots in a square", [("copper_ingot", (0, 1, 3, 4))], "copper_trapdoor"),
        ("book: 3 paper and leather, scattered", [("paper", (0, 4, 8)), ("leather", (6,))], "book"),
        ("magma cream: blaze powder and slime ball, apart", [("blaze_powder", (2,)), ("slime_ball", (6,))], "magma_cream"),
        ("rocket: paper over gunpowder, in a corner", [("paper", (8,)), ("gunpowder", (0,))], "firework_rocket"),
        ("netherite ingot: 4 scrap and 4 gold, mixed", [("netherite_scrap", (0, 2, 4, 6)), ("gold_ingot", (1, 3, 5, 7))], "netherite_ingot"),
    ]
    for name, items, want in cases:
        need = {}
        for k, cells in items: need[k] = need.get(k, 0) + len(cells)
        give(*need.items())
        cmd("craftui open table"); time.sleep(0.8)
        out = grid_craft(items)
        shot("craft_" + want)
        hb = take_out()
        cmd("craftui close"); time.sleep(0.5)
        n = re.search(want + r":(\d+)", hb)
        step(f"crafted {name}", out.split("x")[0].split(":")[0] == want and n is not None,
             f"out={out} hotbar after={hb[:120]}")
        if want == "copper_trapdoor": step("  ...and it's one trapdoor (Minecraft 26.3), not two", n and n.group(1) == "1", n and n.group(1))

def stacks():
    # (item 7) stack sizes: goat horns and banner patterns 1, a bow, shears, a fishing rod 1, golden apples 64; given as a
    # store delivery or a shift-craft gives them (several at once), and picked up one by one
    for key, n, want in [("goat_horn_ponder", 3, 1), ("bow", 2, 1), ("shears", 2, 1), ("fishing_rod", 2, 1), ("flint_and_steel", 2, 1),
                         ("crossbow", 2, 1), ("golden_apple", 64, 64), ("flower_banner_pattern", 2, 1)]:
        give((key, n)); time.sleep(1.5); s = slots()
        filled = [x for x in s.split(",") if x != "-"]
        counts = [int(m.group(1)) if (m := re.search(r"x(\d+)$", x)) else 1 for x in filled]
        step(f"{key}: {n} given, stacks of {want}", sum(counts) == n and max(counts) == want, s[:140])
    # one at a time, dropped and picked up: a second bow goes to its own slot
    give(("bow", 1)); cmd("invgive bow 1"); time.sleep(3.0); s = slots()
    step("two bows given one after the other: two slots", s.count("Bow") == 2 and "Bowx2" not in s, s[:100])
    # shift-crafting two bows at a table
    give(("stick", 6), ("string", 6))
    cmd("craftui open table"); time.sleep(0.8)
    out = grid_craft([("stick", (1, 1, 3, 3, 7, 7)), ("string", (2, 2, 5, 5, 8, 8))])
    hb = take_out(); cmd("craftui close"); time.sleep(1.5); s = slots()
    step("shift-crafted two bows: two slots of one", s.count("Bow") == 2 and "Bowx2" not in s, f"out={out} {s[:120]}")
    give(("golden_apple", 64)); cmd("slot 0"); time.sleep(0.5); shot("stack_golden_apples")

def near_spot():
    """a flat cell in front of me (a few blocks out on open ground) and its surface height"""
    fc = R.start_flat(30, [(0, 2), (1, 2), (-1, 2), (0, 3)])
    return fc, R.surface(fc, 0, 2)

def furnace():
    # (items 5, 14) a block of coal burns 80 smelts (320 s), and taking smelted iron gives Minecraft's 0.7 XP each
    fc, sy = near_spot()
    print(R.place("furnace", fc, 0, sy + 1, 2, facing=3)); time.sleep(0.8)
    give(("scrap_iron_ore", 8), ("coal_block", 1))
    print(cmd("furnaceui open near")[:200]); time.sleep(0.6)
    st0 = cmd("furnaceui state")
    hb = re.search(r"hotbar=\[([^\]]*)\]", st0).group(1).split(",") if "hotbar=" in st0 else []
    print("ui:", st0[:300])
    cmd("furnaceui click hot 0"); cmd("furnaceui click furnace 0")
    cmd("furnaceui click hot 1"); cmd("furnaceui click furnace 1"); time.sleep(1.0)
    s1 = cmd("furnaceui state"); print("lit:", s1[:300]); shot("furnace_lit")
    xp0 = st("xp")
    cmd("speed 8"); time.sleep(5.0); cmd("speed 1"); time.sleep(0.5)
    s2 = cmd("furnaceui state"); print("after 40 s:", s2[:300])
    cmd("furnaceui click furnace 2 shift"); time.sleep(0.8)
    xp1 = st("xp")
    shot("furnace_done")
    cmd("furnaceui close"); time.sleep(0.4)
    step("8 iron ore smelted on a block of coal, 5-6 XP taking it out (0.7 each)", xp1 is not None and 5 <= xp1 - xp0 <= 6, f"xp {xp0} -> {xp1}")
    burn = re.search(r"burn=([\d.]+)/([\d.]+)", s2)
    step("the block of coal burns 320 s (80 smelts), most of it still left", burn and float(burn.group(2)) == 320, burn and burn.group(0))

def eat(key, n=1):
    give((key, n)); cmd("slot 0"); time.sleep(0.4)
    for _ in range(n):
        cmd("hunger 4 0"); time.sleep(0.2)
        cmd("mouse right 2.2"); time.sleep(2.6)

def food():
    # (items 23, 24) rotten flesh 80% Hunger, raw chicken 30%, a pufferfish Poison II and Hunger III; golden apple: absorption 2 min
    cmd("god 0"); cmd("potions clear")
    hits = 0
    for i in range(5):
        cmd("potions clear"); eat("rotten_flesh"); e = cmd("potions"); hits += "Hunger" in e
    step("rotten flesh: Hunger most times (80%)", hits >= 2, f"{hits}/5 bites")
    shot("rotten_flesh_hunger")
    cmd("potions clear"); eat("pufferfish"); e = cmd("potions"); print("pufferfish:", e)
    step("pufferfish: Poison II and Hunger III", "Poison" in e and "Hunger" in e, e)
    shot("pufferfish")
    cmd("potions clear"); cmd("hp 60"); time.sleep(0.5)
    eat("golden_apple"); a0 = st("abs"); h0 = st("hp"); time.sleep(5.5); h1 = st("hp")
    step("golden apple: 2 golden hearts, heals 4 half hearts over 5 s (Regeneration II: one each 1.25 s)", a0 == 20 and h1 is not None and 15 <= h1 - h0 <= 25, f"abs={a0} hp {h0} -> {h1}")
    shot("golden_apple")
    cmd("speed 8"); time.sleep(15.5); cmd("speed 1"); time.sleep(0.5)
    step("the golden hearts are gone after 2 minutes", st("abs") == 0, f"abs={st('abs')}")
    cmd("god 1"); cmd("hp 100")

SECTIONS = {"craft": craft, "stacks": stacks, "furnace": furnace, "food": food}
def aim_cell(fc, dx, y, dz, face_y=0.5):
    import pilot
    pilot.aim_at((fc[0] + dx + .5) * S, (y + face_y) * S + R.YO, (fc[2] + dz + .5) * S); time.sleep(0.35)

def piston():
    # (item 1) a piston leaves a chest, a furnace, a hopper, a lectern where they are (with what's in them); it breaks a bed,
    # a decorated pot, a mob head, dropping them; it still pushes cobblestone
    fc = R.start_flat(31, [(x, z) for x in range(-1, 4) for z in range(0, 5)])
    f = R.stone_floor(fc, range(-1, 4), range(0, 5))
    for i, (key, kind) in enumerate([("cobblestone", "pushed"), ("chest", "stays"), ("furnace", "stays"), ("hopper", "stays"), ("lectern", "stays"), ("jukebox", "stays"),
                                     ("red_bed", "breaks"), ("decorated_pot", "breaks")]):
        y = f + 1
        for dx in range(0, 4): cmd(f"breakabs {fc[0] + dx} {y} {fc[2] + 2}"); cmd(f"breakabs {fc[0] + dx} {y + 1} {fc[2] + 2}")
        time.sleep(0.5)
        R.place("piston", fc, 0, y, 2, 5); R.place(key, fc, 1, y, 2, 3); time.sleep(0.5)
        before = R.block_at(fc, 1, y, 2)
        loose0 = cmd("loose 8")
        R.place("redstone_block", fc, 0, y + 1, 2); time.sleep(1.5)
        at1, at2 = R.block_at(fc, 1, y, 2), R.block_at(fc, 2, y, 2)
        loose1 = cmd("loose 8")
        if i in (1, 5): R.stand_on(fc, 0, f + 1, 0); aim_cell(fc, 1, y, 2); shot("piston_" + key)
        if kind == "pushed": ok = at2 and at2[0] == key and at1 and at1[0] == "piston_head"
        elif kind == "stays": ok = at1 and at1[0] == key and not (at2 and at2[0] == key)
        else:
            def n_of(l): m = re.search(key + r"\(NOT in ship\) x(\d+)", l); return int(m.group(1)) if m else 0
            ok = before and before[0] == key and not (at1 and at1[0] == key) and not (at2 and at2[0] == key) and n_of(loose1) > n_of(loose0)
        step(f"piston and a {key}: {kind}", ok, f"before={before} after: next={at1} beyond={at2} | {loose1[:160]}")
        cmd(f"breakabs {fc[0]} {y + 1} {fc[2] + 2}"); time.sleep(0.8)

def dispenser():
    # (item 2) a dispenser with three buckets facing water: one bucket fills, the full one goes into another of its slots
    fc = R.start_flat(32, [(x, z) for x in range(-1, 4) for z in range(0, 4)])
    f = R.stone_floor(fc, range(-1, 4), range(0, 4))
    y = f + 1
    R.place("dispenser", fc, 0, y, 2, 5); R.place("water", fc, 1, y, 2); time.sleep(0.8)
    give(("bucket", 3))
    R.stand_on(fc, 0, f + 1, 0); aim_cell(fc, 0, y, 2)
    cmd("keys E 0.1"); time.sleep(0.8)
    print("opened:", cmd("chestui state")[:200])
    cmd("chestui click hot 0 shift"); time.sleep(0.5)
    cmd("chestui close"); time.sleep(0.5)
    print(" in it:", cmd(f"transport {fc[0]} {y} {fc[2] + 2}"))
    R.place("redstone_block", fc, 0, y + 1, 2); time.sleep(1.2)
    t = cmd(f"transport {fc[0]} {y} {fc[2] + 2}")
    step("powered once: two empty buckets left, a water bucket beside them (not three full ones)", "bucketx2" in t and "water_bucketx1" in t, t)
    shot("dispenser_bucket")
    cmd(f"breakabs {fc[0]} {y + 1} {fc[2] + 2}"); time.sleep(0.6)

def pearl():
    # (item 9) ender pearls: one a second (Minecraft's cooldown), real right-clicks
    give(("ender_pearl", 4)); cmd("slot 0"); cmd("look 0 -30"); time.sleep(0.4)
    c0 = cmd("state")
    n0 = int(re.search(r"Ender Pearlx(\d+)", c0).group(1))
    cmd("rmb"); time.sleep(0.6); cmd("rmb"); time.sleep(0.2)
    n1 = int(re.search(r"Ender Pearlx(\d+)", cmd("state")).group(1))
    step("two right-clicks 0.6 s apart: one pearl thrown", n0 - n1 == 1, f"{n0} -> {n1}")
    time.sleep(3.0)
    cmd("rmb"); time.sleep(0.5)
    n2 = int(re.search(r"Ender Pearlx(\d+)", cmd("state")).group(1))
    step("a second later: the next one goes", n1 - n2 == 1, f"{n1} -> {n2}")

def panes():
    # (item 27) a glass pane joins a cobblestone wall beside it; a nether brick fence doesn't join an oak fence
    fc = R.start_flat(33, [(x, z) for x in range(-1, 4) for z in range(0, 4)])
    f = R.stone_floor(fc, range(-1, 4), range(0, 4))
    y = f + 1
    R.place("cobblestone_wall", fc, 0, y, 2); R.place("glass_pane", fc, 1, y, 2); R.place("glass_pane", fc, 2, y, 2)
    R.place("oak_fence", fc, 0, y, 3); R.place("nether_brick_fence", fc, 1, y, 3); R.place("nether_brick_fence", fc, 2, y, 3)
    time.sleep(1.0)
    R.stand_on(fc, 1, f + 1, 0); aim_cell(fc, 1, y, 2); shot("panes_walls")
    pane, wall = R.block_at(fc, 1, y, 2), R.block_at(fc, 0, y, 2)
    nb, oak = R.block_at(fc, 1, y, 3), R.block_at(fc, 0, y, 3)
    print("pane", pane, "wall", wall, "nether fence", nb, "oak fence", oak)
    return fc, y

SECTIONS.update({"piston": piston, "dispenser": dispenser, "pearl": pearl, "panes": panes})

def mob_at(name):
    c = [x for x in cmd("enemies").split(" ; ") if x.lower().startswith(name.lower()) and "dead=False" in x]
    if not c: return None
    return tuple(map(float, re.search(r"@([-\d.]+),([-\d.]+),([-\d.]+)", c[0]).group(1, 2, 3)))

def held_mob(name, dist=2.2):
    """a mob held in front of me, still (dev enemyhold), looked at"""
    import pilot
    cmd("look 0 0"); time.sleep(0.2); cmd(f"enemyhold {name} {dist}"); time.sleep(0.4)
    m = mob_at(name)
    if m: pilot.aim_at(m[0], m[1] + 0.7, m[2]); time.sleep(0.3)
    return m

def used():
    m = re.search(r"used (\d+)/(\d+)", cmd("tooluse")); return (int(m.group(1)), int(m.group(2))) if m else None

def wear():
    # (item 12) a sword wears 1 a hit and 2 a block mined, every other tool 2 a hit; shears 238 uses, worn by shearing;
    # shears break leaves fast. Real left clicks on a real zombie, a real [E] on a sheep.
    import pilot
    fc = R.start_flat(34, [(x, z) for x in range(-1, 3) for z in range(0, 5)])
    cmd("gamemode survival"); cmd("mobspawns 0"); cmd("clearenemies 200 all")
    for tool, per_hit in (("iron_sword", 1), ("iron_pickaxe", 2), ("iron_axe", 2), ("iron_shovel", 2)):
        R.hold(tool); cmd("clearenemies 200 all"); time.sleep(0.3)
        cmd("look 0 0"); cmd("enemy Zombie 3"); time.sleep(1.2)
        held_mob("zombie", 1.6); u0 = used()
        def zhp(): m = re.search(r"hp=(\d+)", next((x for x in cmd("enemies").split(" ; ") if x.startswith("Zombie")), "")); return int(m.group(1)) if m else None
        h0 = zhp(); cmd("mouse left 0.04"); time.sleep(1.0)
        u1 = used(); h1 = zhp()
        step(f"{tool}: one hit on a zombie wears it {per_hit}", u0 and u1 and u1[0] - u0[0] == per_hit and h1 is not None and h0 is not None and h1 < h0, f"{u0} -> {u1}, zombie hp {h0} -> {h1}")
    cmd("enemyrelease zombie"); cmd("clearenemies 200 all")
    # a sword mining a block: 2
    sy = R.surface(fc, 0, 2)
    R.hold("iron_sword"); R.place("dirt", fc, 0, sy + 1, 2); time.sleep(0.5)
    aim_cell(fc, 0, sy + 1, 2); u0 = used()
    cmd("mouse left 3.0"); time.sleep(0.2)
    R.wait(lambda: R.block_at(fc, 0, sy + 1, 2) is None, 3.0, step=0.1); cmd("mouse release"); time.sleep(0.5)
    u1 = used()
    step("a sword breaking a dirt block wears 2", u0 and u1 and u1[0] - u0[0] == 2, f"{u0} -> {u1}")
    # shears: 238 uses; a sheep sheared wears them 1; leaves go fast
    R.hold("shears", name="Shears"); u0 = used()
    step("shears have 238 uses", u0 and u0[1] == 238, u0)
    cmd("look 0 0"); cmd("enemy sheep 3"); time.sleep(1.2)
    held_mob("sheep", 2.5); cmd("keys E 0.1"); time.sleep(1.0)
    u1 = used(); care = cmd("care")
    step("shearing a sheep wears the shears 1", u0 and u1 and u1[0] - u0[0] == 1 and "sheared=True" in care, f"{u0} -> {u1} {care[:80]}")
    shot("sheared")
    cmd("enemyrelease sheep"); cmd("clearenemies 200 all")
    R.place("oak_leaves", fc, 1, sy + 1, 2); time.sleep(0.5)
    aim_cell(fc, 1, sy + 1, 2); t0 = time.time()
    cmd("mouse left 2.0")
    R.wait(lambda: R.block_at(fc, 1, sy + 1, 2) is None, 2.5, step=0.05); t = time.time() - t0; cmd("mouse release")
    step("shears cut leaves at once (Minecraft's 15x: 0.02 s)", t < 0.6, f"{t:.2f} s")

def ore_xp():
    # (item 13) ores give Minecraft's XP: coal 0-2, lapis and quartz 2-5, redstone 1-5, diamond 3-7, iron none (smelting gives it)
    import pilot
    fc = R.start_flat(35, [(x, 2) for x in range(0, 4)])
    sy = R.surface(fc, 0, 2)
    R.hold("iron_pickaxe"); cmd("gamemode survival")
    for i, (ore, lo, hi) in enumerate((("coal_ore", 0, 2), ("diamond_ore", 3, 7), ("redstone_ore", 1, 5), ("lapis_ore", 2, 5), ("iron_ore", 0, 0))):
        got = []
        for k in range(3):
            R.place(ore, fc, 0, sy + 1, 2); time.sleep(0.5)
            aim_cell(fc, 0, sy + 1, 2); x0 = st("xp")
            cmd("mouse left 4.0"); R.wait(lambda: R.block_at(fc, 0, sy + 1, 2) is None, 4.0, step=0.1); cmd("mouse release")
            time.sleep(1.2); got.append(int(st("xp") - x0))
        step(f"{ore}: {lo}-{hi} XP a block", all(lo <= g <= hi for g in got), got)

SECTIONS.update({"wear": wear, "ore_xp": ore_xp})

def trader():
    # (items 15, 20) the wandering trader: Minecraft 26.3's offers (two it buys, five common, two uncommon, from what this
    # world has), and 3-6 XP a trade. Three traders looked at; one traded with by [E] and a click.
    import pilot
    R.start_flat(36, [(0, 2)])
    cmd("gamemode survival"); cmd("mobspawns 0"); cmd("clearinv"); cmd("clearenemies 200 all")
    seen = []
    for k in range(3):
        cmd("clearenemies 200 all"); time.sleep(0.5); cmd("trader spawn"); time.sleep(2.0)
        offers = re.search(r"offers=\[([^\]]*)\]", cmd("trader")).group(1).split(" ; ")
        buys = [o for o in offers if "scrap_emerald_ore" not in o.split("->")[0]]
        print(f"   trader {k + 1}: {len(offers)} offers, {len(buys)} it buys:", " | ".join(offers))
        seen.append((len(offers), len(buys)))
    step("each trader: nine offers, two of them it buys (Minecraft 26.3's trade set)", all(n == 9 and b == 2 for n, b in seen), seen)
    cmd("clearinv"); cmd("invgive scrap_emerald_ore 6"); time.sleep(2.0)
    cmd("look 0 0"); cmd("enemyhold wandering 2.2"); time.sleep(0.4)
    c = [x for x in cmd("enemies").split(" ; ") if "wandering" in x.split("@")[0].lower() and "dead=False" in x]
    x, y, z = map(float, re.search(r"@([-\d.]+),([-\d.]+),([-\d.]+)", c[0]).group(1, 2, 3))
    R.look_at(x, y + 1.0, z); time.sleep(0.3)
    for _ in range(3):
        R.wait(lambda: "canAct=True" in cmd("mob?"), 5, step=0.25)
        cmd("keys E 0.1"); time.sleep(1.0)
        if "ui=True" in cmd("trader"): break
        cmd("keys Escape 0.1"); time.sleep(0.5); R.look_at(x, y + 1.0, z); time.sleep(0.3)
    tr = cmd("trader"); shot("trader_screen")
    print("   screen open:", "ui=True" in tr, "| looking at:", cmd("mob?"))
    offers = re.search(r"offers=\[([^\]]*)\]", tr).group(1).split(" ; ")
    i = next((k for k, o in enumerate(offers) if o.split("->")[0].split()[1] == "scrap_emerald_ore" and int(o.split()[0]) <= 20), None)
    x0 = st("xp")
    cmd(f"trader click {i}"); time.sleep(1.5)
    x1 = st("xp")
    step("a trade: 3-6 XP (Minecraft's)", 3 <= x1 - x0 <= 6, f"{offers[i]}: xp {x0} -> {x1}")
    def ntr(): return int(re.search(r"trades=(\d+)", cmd("trader")).group(1))
    def em(): return slots().count("Emerald")
    # too few emeralds: refused, none taken
    j = max(range(len(offers)), key=lambda k: int(offers[k].split()[0]) if "scrap_emerald_ore" in offers[k].split("->")[0] else 0)
    cmd("clearinv"); cmd("invgive scrap_emerald_ore 1"); time.sleep(2.0)
    t0 = ntr(); cmd(f"trader click {j}"); time.sleep(1.5)
    step("too few emeralds: refused, the one kept", ntr() == t0 and em() == 1, f"{offers[j]}: trades {t0} -> {ntr()}, {slots()[:80]}")
    # it buys a water bucket for two emeralds
    k = next((k for k, o in enumerate(offers) if "scrap_emerald_ore" not in o.split("->")[0]), None)
    give_key = offers[k].split()[1].split("->")[0]; give_n = int(offers[k].split()[0])
    cmd(f"invgive {give_key} {give_n}"); time.sleep(2.5)
    e0 = em(); cmd(f"trader click {k}"); time.sleep(2.0)
    step(f"sold it {give_n} {give_key}: paid in emeralds", em() > e0 and give_key.split("_")[0].lower() not in slots().lower(), f"{offers[k]}: {slots()[:100]}")
    shot("trader_after")
    cmd("keys Escape 0.1"); cmd("enemyrelease wandering"); time.sleep(0.5)

SECTIONS.update({"trader": trader})

def care(): return cmd("care")
def press_e_on(name, dist=2.5):
    import pilot
    cmd("look 0 0"); time.sleep(0.2); cmd(f"enemyhold {name} {dist}"); time.sleep(0.2)
    m = mob_at(name)
    if not m: return False
    pilot.aim_at(m[0], m[1] + 0.6, m[2]); time.sleep(0.3); cmd("keys E 0.1"); time.sleep(1.0)
    return True

def count_loose(key):
    m = re.search(key + r"(?:\(NOT in ship\))? x(\d+)", cmd("loose 14")); return int(m.group(1)) if m else 0

def drops():
    # (items 16-18) a calf killed gives no XP (an animal's baby), its parents do; a spider's eye only from a player's kill;
    # a rabbit always one raw rabbit
    R.start_flat(37, [(0, 2)])
    cmd("gamemode survival"); cmd("mobspawns 0"); cmd("clearenemies 200 all"); cmd("god 1")
    cmd("care grow 900")
    b0 = int(re.search(r"born=(\d+)", care()).group(1))
    R.hold("wheat", 2, name="Wheat")
    cmd("look 0 0"); cmd("enemy cow 3"); time.sleep(1.0); press_e_on("cow"); time.sleep(0.5)
    cmd("enemyhold cow 6"); time.sleep(0.3)
    cmd("look 0 0"); cmd("enemy cow 3"); time.sleep(1.0); press_e_on("cow"); time.sleep(1.5)
    cmd("enemyrelease cow"); cmd("enemyrelease cow"); time.sleep(1.0)
    step("two cows fed wheat by [E]: a calf", int(re.search(r"born=(\d+)", care()).group(1)) > b0, care()[:200])
    shot("calf")
    R.hold("iron_sword")
    got = []
    for who in ("calf", "cow", "cow"):
        x0 = st("xp")
        cmd(f"hitenemy {who} 20"); time.sleep(1.2)
        got.append((who, int(st("xp") - x0)))
    for name, want in (("Zombie", 5), ("spider", 5)):
        cmd("look 0 0"); cmd(f"enemy {name} 3"); time.sleep(1.0); x0 = st("xp")
        cmd(f"hitenemy {name} 30"); time.sleep(1.2)
        step(f"a {name} killed: {want} XP (Minecraft's)", st("xp") - x0 == want, st("xp") - x0)
    step("killing the calf gives no XP, each cow 1-3", all((x == 0) if w == "calf" else (1 <= x <= 3) for w, x in got) and any(w == "calf" for w, _ in got), got)
    cmd("clearenemies 200 all"); cmd("care grow 300"); time.sleep(1.0)
    # spider eyes: 15 killed by me, then 15 by an explosion
    e0 = count_loose("spider_eye")
    for _ in range(15):
        cmd("look 0 0"); cmd("enemy spider 3"); time.sleep(0.6); cmd("hitenemy spider 30"); time.sleep(0.6)
    time.sleep(1.0); e1 = count_loose("spider_eye")
    step("15 spiders I killed: spider eyes (a third of them)", e1 - e0 >= 1, e1 - e0)
    cmd("clearenemies 200 all")
    import pilot
    # rabbits: one raw rabbit each
    r0 = count_loose("raw_rabbit")
    for _ in range(6):
        cmd("look 0 0"); cmd("enemy rabbit 3"); time.sleep(0.6); cmd("hitenemy rabbit 30"); time.sleep(0.6)
    time.sleep(1.0); r1 = count_loose("raw_rabbit")
    step("6 rabbits: 6 raw rabbit (Minecraft 26.3: always one)", r1 - r0 == 6, f"{r0} -> {r1} | {cmd('loose 14')[:200]}")
    cmd("tprel 40 0 0"); time.sleep(2.0)  # (somewhere else: the blasts would blow the first eyes away)
    e1 = count_loose("spider_eye")
    for _ in range(15):
        cmd("look 0 0"); cmd("enemy spider 4"); time.sleep(0.8)
        m = mob_at("spider")
        if m: cmd(f"boom {m[0]:.2f} {m[1] + 0.5:.2f} {m[2]:.2f}"); time.sleep(0.8)
        cmd("clearenemies 200 all")
    time.sleep(1.0); e2 = count_loose("spider_eye")
    step("15 spiders blown up (no player): no eyes", e2 == e1, f"{e1} -> {e2}")
    shot("spider_eyes")
SECTIONS.update({"drops": drops})

def blocks():
    # (items 28-32) door sounds by material; the composter; sugar cane on mud; the furnace minecart takes only coal, 8 at most
    import pilot
    fc = R.start_flat(38, [(x, z) for x in range(-2, 4) for z in range(0, 6)])
    f = R.stone_floor(fc, range(-2, 4), range(0, 6)); y = f + 1
    cmd("gamemode survival"); cmd("mobspawns 0"); cmd("clearenemies 200 all")
    for i, (door, want) in enumerate((("oak_door", "door.open"), ("copper_door", "copper_door.toggle"), ("iron_door", "iron_door.open"))):
        dx = i - 1
        R.place(door, fc, dx, y, 4, facing=2); time.sleep(0.6)
        if door == "iron_door": R.place("stone", fc, dx + 1, y, 4); R.place("lever", fc, dx + 1, y, 3, facing=3); time.sleep(0.4)
        cmd(f"tp {(fc[0] + dx + .5) * S:.2f} {(y + .1) * S + R.YO:.2f} {(fc[2] + 2.0) * S:.2f}"); time.sleep(0.8)
        tx = dx + 1.5 if door == "iron_door" else dx + 0.5; tz = 3.95 if door == "iron_door" else 4.4
        pilot.aim_at((fc[0] + tx) * S, (y + .5) * S + R.YO, (fc[2] + tz) * S); time.sleep(0.3)
        cmd("keys E 0.1"); time.sleep(0.8)
        rec = cmd("soundinfo recent")
        step(f"{door} opened ([E]{' on its lever' if door == 'iron_door' else ''}): Minecraft's {want}", want in rec[-400:], rec[-200:])
        if i == 1: shot("copper_door_open")
    # composter: wheat by [E] until it's ready, bone meal out
    R.place("composter", fc, -2, y, 2); time.sleep(0.4)
    cmd(f"tp {(fc[0] - 1.5) * S:.2f} {(y + .1) * S + R.YO:.2f} {(fc[2] + 0.5) * S:.2f}"); time.sleep(0.6)
    R.hold("wheat", 40, name="Wheat"); n = 0
    def lvl():
        b = R.block_at(fc, -2, y, 2); return b[1] if b else -1
    for n in range(1, 41):
        pilot.aim_at((fc[0] - 1.5) * S, (y + .8) * S + R.YO, (fc[2] + 2.5) * S); time.sleep(0.2); cmd("keys E 0.1"); time.sleep(0.4)
        if n == 1: first = lvl()
        if lvl() >= 7: break
    time.sleep(1.5)
    step("the first wheat always counts (an empty composter)", first == 1, first)
    step(f"wheat fills it (65% a level: {n} wheat for 7 levels)", lvl() == 8 and 7 <= n <= 25, (n, lvl()))
    cmd("keys E 0.1"); time.sleep(1.0)
    step("[E]: bone meal out", "bone_meal" in cmd("loose 6") or "Bone Meal" in cmd("state"), cmd("loose 6")[:120])
    # sugar cane on mud with water beside it (a real right-click)
    R.place("mud", fc, 3, y, 2); R.place("water", fc, 3, y, 3); time.sleep(0.6)
    R.hold("sugar_cane", 2, name="Sugar Cane")
    cmd(f"tp {(fc[0] + 3.5) * S:.2f} {(y + .1) * S + R.YO:.2f} {(fc[2] + 0.5) * S:.2f}"); time.sleep(0.6)
    pilot.aim_at((fc[0] + 3.5) * S, (y + 1.0) * S + R.YO, (fc[2] + 2.5) * S); time.sleep(0.3)
    cmd("rmb"); time.sleep(0.8)
    b = R.block_at(fc, 3, y + 1, 2)
    step("sugar cane planted on mud by water (Minecraft 26.3's soils)", b and b[0] == "sugar_cane", b)
    shot("cane_on_mud")
    # furnace minecart: coal, at most 8 pieces (Minecraft's 1600 s); planks aren't its fuel
    cmd("clearenemies 200 all"); cmd("look 0 0"); cmd("enemy minecart with furnace 3"); time.sleep(1.2)
    R.hold("oak_planks", 4, name="Oak Planks")
    cmd("look 0 0"); time.sleep(0.2); cmd("enemyhold furnace 2.4"); time.sleep(0.3)
    m = mob_at("minecart with furnace")
    if m: pilot.aim_at(m[0], m[1] + 0.5, m[2]); time.sleep(0.3)
    tip_planks = cmd("mob?")
    give(("coal", 12)); cmd("slot 0"); time.sleep(0.4)
    if m: pilot.aim_at(m[0], m[1] + 0.5, m[2]); time.sleep(0.3)
    tip_coal = cmd("mob?")
    for _ in range(12): cmd("keys E 0.1"); time.sleep(0.5)
    left = re.search(r"Coalx(\d+)", cmd("state"))
    step("holding planks: no 'Fuel it' on the furnace minecart; coal: 'Fuel it'", "Fuel it" not in tip_planks and "Fuel it" in tip_coal, f"{tip_planks} | {tip_coal}")
    step("12 coal offered: it takes 8 (1600 s at most)", left and int(left.group(1)) == 4, left and left.group(0))
    cmd("enemyrelease furnace"); cmd("clearenemies 200 all")

SECTIONS.update({"blocks": blocks})

def torch():
    # (item 28) a redstone torch flicked more than 8 times in 3 s burns out, and comes back 8 s later (Minecraft's 160 ticks;
    # it had been 5 s): a lever on a block, the torch on its far side, the lever flicked by [E]
    import pilot
    fc = R.start_flat(39, [(x, z) for x in range(-1, 2) for z in range(0, 5)])
    f = R.stone_floor(fc, range(-1, 2), range(0, 5)); y = f + 1
    R.place("stone", fc, 0, y, 3); R.place("lever", fc, 0, y, 2, facing=3); R.place("redstone_torch", fc, 0, y, 4, facing=2); time.sleep(1.0)
    cmd(f"tp {(fc[0] + .5) * S:.2f} {(y + .1) * S + R.YO:.2f} {(fc[2] + 0.6) * S:.2f}"); time.sleep(0.8)
    pilot.aim_at((fc[0] + .5) * S, (y + .5) * S + R.YO, (fc[2] + 2.95) * S); time.sleep(0.3)
    def lit():
        b = R.block_at(fc, 0, y, 4); return b and (b[1] & 1) == 0
    print("   torch lit at first:", lit(), R.block_at(fc, 0, y, 4))
    for _ in range(24): cmd("keys E 0.05"); time.sleep(0.12)
    # (the lever left off: the torch would be lit, but it's burnt out)
    if R.block_at(fc, 0, y, 2) and (R.block_at(fc, 0, y, 2)[1] & 1): time.sleep(0.3); cmd("keys E 0.05"); time.sleep(0.3)
    t0 = time.time() - 0.6
    out = not lit() and not (R.block_at(fc, 0, y, 2)[1] & 1); shot("torch_burnt")
    step("the lever flicked 12 times in 3 s: the torch burns out", out, R.block_at(fc, 0, y, 4))
    back = R.wait(lambda: lit(), 14, step=0.25); t = time.time() - t0
    step("it lights again 8 s later (Minecraft's 160 ticks)", back and 7.0 <= t <= 10.0, f"{t:.1f} s")

SECTIONS.update({"torch": torch})

def witch():
    # (item 33) a witch: hurt, it drinks Healing (5% a tick); you far off (over 11 blocks), Swiftness, and it chases quicker
    R.start_flat(40, [(0, 2)])
    def n(k): return int(re.search(k + r"=(\d+)", cmd("nethermob")).group(1))
    def wl(): return next((e for e in cmd("enemies").split(" ; ") if e.startswith("Witch")), "")
    cmd("god 1"); cmd("mobspawns 0"); cmd("clearenemies 200 all"); time.sleep(0.5)
    cmd("look 0 0"); cmd("nethermob witch 8"); time.sleep(1.5); cmd("provoke witch"); time.sleep(0.5)
    cmd("tprel 0 0 -20"); d0 = n("witchDrinks")
    ok = R.wait(lambda: n("witchDrinks") > d0, 8, step=0.25)
    step("20 m off: it drinks (Swiftness)", ok, wl()[:120])
    time.sleep(2.5)
    spd = re.search(r"spd=([\d.]+)", wl())
    step("and comes on quicker (Swiftness: 3.4 -> 4.1)", spd and float(spd.group(1)) > 4.0, wl()[:200])
    shot("witch_swift")
    cmd("clearenemies 200 all")

def arrows():
    # (item 8) a tipped arrow shot into a wall comes back as itself (it had come back plain)
    import pilot
    fc = R.start_flat(41, [(x, z) for x in range(-1, 2) for z in range(0, 7)])
    f = R.stone_floor(fc, range(-1, 2), range(0, 7)); y = f + 1
    for dx in (-1, 0, 1):
        for dy in (0, 1, 2): R.place("stone", fc, dx, y + dy, 6)
    cmd("bows recover 1")  # (this session only: back to the config's after)
    cmd(f"tp {(fc[0] + .5) * S:.2f} {(y + .1) * S + R.YO:.2f} {(fc[2] + 0.5) * S:.2f}"); time.sleep(0.8)
    give(("bow", 1), ("arrow_slowness", 2)); cmd("slot 0"); time.sleep(0.5)
    pilot.aim_at((fc[0] + .5) * S, (y + 1.5) * S + R.YO, (fc[2] + 6.0) * S); time.sleep(0.3)
    cmd("mouse right 1.3"); time.sleep(2.0)
    lo = cmd("loose 14")
    step("a slowness arrow shot into a wall: a slowness arrow lies there to pick up", "arrow_slowness" in lo, lo[:160])
    shot("tipped_arrow_back")
    cmd("bows recover -1")
    print("   bows:", cmd("bows")[:120])

def magma():
    # (item 22) a small magma cube hits for 3 hearts (15 of 100), Minecraft's size + 2
    R.start_flat(42, [(0, 2)])
    cmd("god 0"); cmd("hp 100"); cmd("clearenemies 200 all"); time.sleep(0.5)
    cmd("look 0 0"); cmd("nethermob magma1 3"); time.sleep(0.5); cmd("provoke magma")
    h0 = st("hp")
    ok = R.wait(lambda: st("hp") < h0, 15, step=0.1)
    h1 = st("hp"); shot("magma_small_hit")
    step("a small magma cube's hit: 15 (3 hearts)", ok and h0 - h1 == 15, f"{h0} -> {h1}")
    cmd("clearenemies 200 all"); cmd("god 1"); cmd("hp 100")

def enderman():
    # (item 21) an enderman picks up dirt, not netherrack, soul sand or soul soil any more (Minecraft 26.3's list; a jack
    # o'lantern isn't on it either, but its light keeps monsters off)
    fc = R.start_flat(43, [(x, z) for x in range(-2, 3) for z in range(0, 5)])
    f = R.stone_floor(fc, range(-2, 3), range(0, 5)); y = f + 1
    cmd("mobspawns 0"); cmd("clearenemies 200 all"); cmd("endermen haste")
    for dx, dz, key in ((-1, 2, "netherrack"), (1, 2, "soul_sand"), (0, 1, "soul_soil"), (0, 3, "netherrack")):
        R.place(key, fc, dx, y, dz)
    cmd(f"tp {(fc[0] + .5) * S:.2f} {(y + .1) * S + R.YO:.2f} {(fc[2] - 3) * S:.2f}"); cmd("look 180 0"); time.sleep(0.6)
    cmd("enemy enderman 4"); time.sleep(0.5)
    e = mob_at("enderman")
    if e: cmd(f"tp {(fc[0] + .5) * S:.2f} {(y + .1) * S + R.YO:.2f} {(fc[2] + 2.5) * S:.2f}"); time.sleep(0.1); cmd("look 180 0"); cmd("enemyhold enderman 0.1"); cmd("enemyrelease enderman"); cmd(f"tp {(fc[0] + .5) * S:.2f} {(y + .1) * S + R.YO:.2f} {(fc[2] - 4) * S:.2f}"); cmd("look 180 0")
    time.sleep(8.0)
    left = [R.block_at(fc, dx, y, dz) for dx, dz in ((-1, 2), (1, 2), (0, 1), (0, 3))]
    step("8 s beside netherrack, soul sand, soul soil: it takes none", all(left), (left, cmd("endermen")[:200]))
    # (a dirt block put down right beside it, wherever it wandered to)
    e = mob_at("enderman"); p0 = int(re.search(r"picked=(\d+)", cmd("endermen")).group(1))
    if e:
        cx, cy, cz = int(e[0] // S), int((e[1] - R.YO) // S), int(e[2] // S)
        print("  ", cmd(f"placeabs dirt {cx + 1} {cy} {cz} 1"))
    R.wait(lambda: int(re.search(r"picked=(\d+)", cmd("endermen")).group(1)) > p0, 8, step=0.5)
    en = cmd("endermen")
    step("a dirt block put beside it: taken", int(re.search(r"picked=(\d+)", en).group(1)) > p0 and "carrying=dirt" in en, en[:220])
    shot("enderman_dirt")
    cmd("endermen haste off"); cmd("clearenemies 200 all")

SECTIONS.update({"witch": witch, "arrows": arrows, "magma": magma, "enderman": enderman})

def nether():
    # (items 18, 19, 22) in the Nether: a small magma cube hits for 3 hearts; piglins barter Minecraft 26.3's table (iron
    # nuggets, string, leather, blackstone, the nether brick item among it); a hoglin drops 2-4 porkchops
    import pilot
    cmd("craftui close"); cmd("gamemode survival"); cmd("god 1"); cmd("mobspawns 0")
    if "tiles=" not in cmd("nether"): cmd("nether gen"); time.sleep(1.0)
    cmd("nether tp"); time.sleep(1.5); cmd("clearenemies 200 all"); time.sleep(0.5)
    # magma cube
    cmd("god 0"); cmd("hp 100"); time.sleep(0.5)
    hits = []
    cmd("look 0 0"); cmd("nethermob magma1 3"); time.sleep(0.5); cmd("provoke magma")
    t0 = time.time(); last = st("hp")
    while time.time() - t0 < 12 and len(hits) < 2:
        h = st("hp")
        if h != last: hits.append(last - h); last = h
        time.sleep(0.05)
    cmd("god 1"); cmd("hp 100")
    step("a small magma cube's hits: 15 each (3 hearts)", hits and all(x == 15 for x in hits), hits)
    cmd("clearenemies 200 all")
    # hoglins
    p0 = count_loose("raw_porkchop"); n = 5
    for _ in range(n):
        cmd("look 0 0"); cmd("enemy hoglin 3"); time.sleep(0.8); cmd("hitenemy hoglin 60"); time.sleep(0.8)
    time.sleep(1.0); p1 = count_loose("raw_porkchop")
    step(f"{n} hoglins: 2-4 porkchops each ({2 * n}-{4 * n})", 2 * n <= p1 - p0 <= 4 * n, p1 - p0)
    cmd("clearenemies 200 all")
    # piglin bartering: a golden helmet on, gold ingots thrown to it with [Q]
    cmd("clearinv"); cmd("invgive golden_helmet 1"); time.sleep(1.5)
    cmd("keys I 0.08"); time.sleep(0.6)
    hot = re.search(r"hotbar=\[([^\]]*)\]", cmd("craftui state")).group(1).split(",")
    hi = next((i for i, e in enumerate(hot) if e.startswith("golden_helmet")), 0)
    cmd(f"craftui click hot {hi}"); cmd("craftui click armor 0"); time.sleep(0.5); cmd("craftui close"); time.sleep(0.5)
    cmd("look 0 0"); cmd("enemy piglin 4"); time.sleep(1.0)
    log0 = len(open("O:/SteamLibrary/steamapps/common/Lethal Company/BepInEx/LogOutput.log", encoding="utf-8", errors="ignore").read().splitlines())
    R.hold("gold_ingot", 24, name="Gold Ingot")
    cmd("enemyhold piglin 3.0"); time.sleep(0.4)
    m = mob_at("piglin")
    if m: pilot.aim_at(m[0], m[1] + 0.3, m[2]); time.sleep(0.3)
    for _ in range(24): cmd("keys Q 0.05"); time.sleep(0.15)
    cmd("enemyrelease piglin"); cmd("speed 6")
    R.wait(lambda: int(re.search(r"barters=(\d+)", cmd("piglins")).group(1)) >= 24, 40, step=1.0)
    cmd("speed 1"); time.sleep(0.5)
    lines = open("O:/SteamLibrary/steamapps/common/Lethal Company/BepInEx/LogOutput.log", encoding="utf-8", errors="ignore").read().splitlines()[log0:]
    got = [l.split("bartered:")[-1].strip() for l in lines if "[piglin] bartered:" in l]
    print("   barters:", " | ".join(got))
    keys = " ".join(got)
    step(f"{len(got)} barters, from 26.3's table (none of the old iron ingots)", len(got) >= 10 and "iron_ingot" not in keys, len(got))
    shot("piglin_barter")
    cmd("clearenemies 200 all")

SECTIONS.update({"nether": nether})

def brew():
    # (items 25, 26) the brewing stand's ingredient hint lists the rabbit's foot and phantom membrane, and fits on the
    # screen (wrapped); the leaping potion and tipped arrows in Minecraft's colours. The mouse put on each slot.
    import pilot, os
    fc = R.start_flat(45, [(x, z) for x in range(-1, 2) for z in range(0, 4)])
    f = R.stone_floor(fc, range(-1, 2), range(0, 4)); y = f + 1
    R.place("brewing_stand", fc, 0, y, 2); time.sleep(0.6)
    give(("potion_leaping", 1), ("arrow_slowness", 1), ("arrow_poison", 1), ("potion_slow_falling", 1), ("rabbit_foot", 1), ("phantom_membrane", 1))
    R.stand_on(fc, 0, f + 1, 0)
    pilot.aim_at((fc[0] + .5) * S, (y + .4) * S + R.YO, (fc[2] + 2.5) * S); time.sleep(0.4)
    cmd("keys E 0.1"); time.sleep(0.8)
    for name, area, i in (("brew_hint", "stand", 3), ("brew_fuel", "stand", 4), ("brew_leaping", "hot", 0), ("brew_membrane", "hot", 5)):
        r = cmd(f"brewui pos {area} {i}")
        x, yy = map(float, r.split())
        cmd(f"mouse moveto {x} {yy}"); time.sleep(0.6); shot(name)
    cmd("keys Escape 0.1"); time.sleep(0.4)
    print("   (look at shots/th_brew_*.png)")

SECTIONS.update({"brew": brew})

if __name__ == "__main__":
    R.grid_yo()
    cmd("god 1"); cmd("dayfreeze 1")
    for s in sys.argv[1:]: print("==", s); SECTIONS[s]()
    print(f"\n{sum(1 for _, ok in log if ok)}/{len(log)} fine")
