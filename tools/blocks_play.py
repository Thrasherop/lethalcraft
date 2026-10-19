"""#244 part D hand play: the new blocks, recipes, smelting, fuels and drops, as a player meets them.
Screenshots in shots/bd_*.png.  usage: py tools/blocks_play.py <section> [...]
sections: storage smelt colours ice paths lanterns drops grid stairs pillars nether creative"""
import sys, os, time, re, math
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from regress import cmd, S
import thorough_play as T
HERE = os.path.dirname(os.path.abspath(__file__))
log = []
def shot(n): cmd(f"screenshot {os.path.join(HERE, '..', 'shots', 'bd_' + n + '.png')}"); time.sleep(0.5)
def step(name, ok, detail=""):
    log.append((name, ok)); print(("OK  " if ok else "BAD ") + name + (f"  ({detail})" if detail else ""), flush=True)
slots, st, give, aim_cell = T.slots, T.st, T.give, T.aim_cell
def select(name):
    import pilot; pilot.select(name); time.sleep(0.3)

def craft_all(cases):
    """(label, [(key, cells)], gives, expect_prefix) at a crafting table; each laid with real clicks"""
    for label, lay, gives, want in cases:
        give(*gives); cmd("craftui open table"); time.sleep(0.6)
        out = T.grid_craft(lay)
        step(f"{label} -> {want}", out.startswith(want), out)
        cmd("craftui close"); time.sleep(0.3)

def flat(idx, n=6):
    fc = R.start_flat(idx, [(dx, dz) for dx in range(-2, 3) for dz in range(0, n)]); cmd("god 1"); cmd("gamemode survival")
    sy = max(R.surface(fc, dx, dz) for dx in range(-2, 3) for dz in range(0, n)) + 1
    for dx in range(-2, 3):
        for dz in range(0, n): R.place("stone", fc, dx, sy, dz)
    time.sleep(0.6); R.stand_on(fc, 0, sy + 1, 0); time.sleep(0.5)
    return fc, sy

def storage():
    # (#66, #70) storage blocks both ways; bone, magma and nether wart blocks; gold from nuggets; copper from the waxed
    # block; polished basalt; sandstone and quartz shapes from cut and chiseled; TNT from red sand; the name tag
    R.start_flat(110, [(0, 2)]); cmd("god 1"); cmd("gamemode survival")
    nine = (0, 1, 2, 3, 4, 5, 6, 7, 8)
    craft_all([
        ("9 lapis", [("lapis_lazuli", nine)], [("lapis_lazuli", 9)], "lapis_blockx1"),
        ("a lapis block", [("lapis_block", (4,))], [("lapis_block", 1)], "lapis_lazulix9"),
        ("9 emeralds", [("scrap_emerald_ore", nine)], [("scrap_emerald_ore", 9)], "emerald_blockx1"),
        ("9 netherite ingots", [("netherite_ingot", nine)], [("netherite_ingot", 9)], "netherite_blockx1"),
        ("9 bone meal", [("bone_meal", nine)], [("bone_meal", 9)], "bone_blockx1"),
        ("4 magma cream", [("magma_cream", (0, 1, 3, 4))], [("magma_cream", 4)], "magmax1"),
        ("9 nether wart", [("nether_wart", nine)], [("nether_wart", 9)], "nether_wart_blockx1"),
        ("9 gold nuggets", [("gold_nugget", nine)], [("gold_nugget", 9)], "gold_ingotx1"),
        ("a waxed copper block", [("waxed_copper_block", (4,))], [("waxed_copper_block", 1)], "copper_ingotx9"),
        ("4 basalt", [("basalt", (0, 1, 3, 4))], [("basalt", 4)], "polished_basaltx4"),
        ("cut sandstone in stairs", [("cut_sandstone", (0, 3, 4, 6, 7, 8))], [("cut_sandstone", 6)], "sandstone_stairsx4"),
        ("quartz pillars in a row", [("quartz_pillar", (3, 4, 5))], [("quartz_pillar", 3)], "quartz_slabx6"),
        ("TNT with red sand", [("gunpowder", (0, 2, 4, 6, 8)), ("red_sand", (1, 3, 5, 7))], [("gunpowder", 5), ("red_sand", 4)], "tntx1"),
        ("paper and a gold nugget", [("gold_nugget", (1,)), ("paper", (4,))], [("gold_nugget", 1), ("paper", 1)], "name_tagx1"),
        ("8 ice? no: 9 ice", [("ice", nine)], [("ice", 9)], "packed_icex1"),
        ("9 packed ice", [("packed_ice", nine)], [("packed_ice", 9)], "blue_icex1"),
        ("dirt and gravel", [("dirt", (0, 4)), ("gravel", (1, 3))], [("dirt", 2), ("gravel", 2)], "coarse_dirtx4"),
        ("3 snow blocks", [("snow_block", (3, 4, 5))], [("snow_block", 3)], "snowx6"),
        ("iron nuggets round a torch", [("iron_nugget", (0, 1, 2, 3, 5, 6, 7, 8)), ("torch", (4,))], [("iron_nugget", 8), ("torch", 1)], "lanternx1"),
        ("charcoal, stick, soul soil", [("charcoal", (1,)), ("stick", (4,)), ("soul_soil", (7,))], [("charcoal", 1), ("stick", 1), ("soul_soil", 1)], "soul_torchx4"),
        ("charcoal on a stick", [("charcoal", (1,)), ("stick", (4,))], [("charcoal", 1), ("stick", 1)], "torchx4"),
    ])

def furnace_run(inp, n, fuel, fn, secs=6.0):
    """a furnace: `n` of `inp` on `fn` of `fuel`, sped up; what came out, what's left in the fuel slot"""
    fc, sy = T.near_spot()
    R.place("furnace", fc, 0, sy + 1, 2, facing=3); time.sleep(0.8)
    give((inp, n), (fuel, fn))
    cmd("furnaceui open near"); time.sleep(0.6)
    cmd("furnaceui click hot 0"); cmd("furnaceui click furnace 0")
    cmd("furnaceui click hot 1"); cmd("furnaceui click furnace 1"); time.sleep(0.5)
    cmd("speed 8"); time.sleep(secs); cmd("speed 1"); time.sleep(0.5)
    s = cmd("furnaceui state"); cmd("furnaceui close"); time.sleep(0.3)
    cmd(f"breakabs {fc[0]} {sy + 1} {fc[2] + 2}"); time.sleep(0.5)
    return s

def smelt():
    # (#67, #68, #69) basalt smooth; an iron sword to a nugget; a coal ore block (Silk Touch's) to coal; a log to
    # charcoal; a lava bucket burns and leaves its bucket; a blaze rod burns
    R.start_flat(111, [(0, 2)]); cmd("god 1"); cmd("gamemode survival")
    for inp, fuel, want in (("basalt", "coal", "smooth_basalt"), ("iron_sword", "coal", "iron_nugget"), ("coal_ore", "coal", "coal"), ("oak_log", "coal", "charcoal"), ("golden_helmet", "coal", "gold_nugget")):
        s = furnace_run(inp, 1, fuel, 1)
        step(f"smelting {inp}: {want}", re.search(r"out=" + want + r"\b", s) is not None or (want + "x") in s, s[:200])
    s = furnace_run("cobblestone", 8, "lava_bucket", 1, secs=8.0)
    print("   lava:", s[:260])
    step("a lava bucket burns (its bucket left in the fuel slot)", "bucket" in s and "lava_bucket" not in s.split("fuel=")[1][:30] if "fuel=" in s else "bucket" in s, s[:260])
    s = furnace_run("cobblestone", 4, "blaze_rod", 1)
    step("a blaze rod burns", re.search(r"out=stone", s) is not None, s[:200])

def colours():
    # (#71) carpets, stained glass and panes, beds in colours: crafted, placed, looked at; a carpet and a bed re-dyed
    fc, sy = flat(112, 7)
    craft_all([
        ("2 blue wool", [("wool_blue", (3, 4))], [("wool_blue", 2)], "blue_carpetx3"),
        ("glass round a lime dye", [("glass", (0, 1, 2, 3, 5, 6, 7, 8)), ("lime_dye", (4,))], [("glass", 8), ("lime_dye", 1)], "lime_stained_glassx8"),
        ("6 lime stained glass", [("lime_stained_glass", (0, 1, 2, 3, 4, 5))], [("lime_stained_glass", 6)], "lime_stained_glass_panex16"),
        ("glass panes round a red dye", [("glass_pane", (0, 1, 2, 3, 5, 6, 7, 8)), ("red_dye", (4,))], [("glass_pane", 8), ("red_dye", 1)], "red_stained_glass_panex8"),
        ("3 yellow wool, 3 birch planks", [("wool_yellow", (0, 1, 2)), ("birch_planks", (3, 4, 5))], [("wool_yellow", 3), ("birch_planks", 3)], "yellow_bedx1"),
        ("a blue carpet and a red dye", [("blue_carpet", (0,)), ("red_dye", (1,))], [("blue_carpet", 1), ("red_dye", 1)], "red_carpetx1"),
        ("a yellow bed and a purple dye", [("yellow_bed", (0,)), ("purple_dye", (4,))], [("yellow_bed", 1), ("purple_dye", 1)], "purple_bedx1"),
    ])
    # placed: carpets in four colours, stained glass, panes, two beds
    for i, c in enumerate(("white", "orange", "light_blue", "black")): R.place(c + "_carpet", fc, i - 2, sy + 1, 2)
    for i, c in enumerate(("red", "lime", "blue", "purple")): R.place(c + "_stained_glass", fc, i - 2, sy + 1, 4)
    for i, c in enumerate(("cyan", "magenta", "yellow")): R.place(c + "_stained_glass_pane", fc, i - 1, sy + 2, 4)
    R.place("blue_bed", fc, -2, sy + 1, 6, facing=2); R.place("green_bed", fc, 1, sy + 1, 6, facing=2)
    time.sleep(1.5)
    cmd("look 0 25"); time.sleep(0.6); shot("colours")
    got = [R.block_at(fc, -1, sy + 1, 2), R.block_at(fc, -1, sy + 1, 4), R.block_at(fc, 0, sy + 2, 4), R.block_at(fc, -2, sy + 1, 6)]
    step("placed: carpets, stained glass and panes, colour beds", [g and g[0] for g in got] == ["orange_carpet", "lime_stained_glass", "magenta_stained_glass_pane", "blue_bed"], got)
    # the stained glass breaks into nothing (Minecraft's)
    give(("iron_pickaxe", 1)); select("Iron Pickaxe")
    b0 = cmd("find stained glass")
    aim_cell(fc, -2, sy + 1, 4); cmd("mouse left 2"); R.wait(lambda: R.block_at(fc, -2, sy + 1, 4) is None, 2.5, step=0.1); cmd("mouse release"); time.sleep(1.2)
    step("red stained glass broken: gone, nothing drops", R.block_at(fc, -2, sy + 1, 4) is None and "Red Stained Glass" not in cmd("find stained glass"), cmd("find stained glass")[:150])

def ice():
    # (#72) packed and blue ice placed; ice and glass break into nothing (Silk Touch keeps them); snow layers stacked
    fc, sy = flat(113, 5)
    give(("iron_pickaxe", 1)); select("Iron Pickaxe")
    for key, dx in (("ice", -1), ("glass", 0), ("packed_ice", 1)):
        R.place(key, fc, dx, sy + 1, 2); time.sleep(0.4)
        aim_cell(fc, dx, sy + 1, 2); cmd("mouse left 3"); R.wait(lambda: R.block_at(fc, dx, sy + 1, 2) is None, 3.0, step=0.1); cmd("mouse release"); time.sleep(1.0)
        step(f"{key} broken with a plain pickaxe: nothing drops", R.block_at(fc, dx, sy + 1, 2) is None and not re.search(re.escape(key.replace('_', ' ').title()) + r"x?\d*@", cmd("find " + key.replace('_', ' '))), cmd("find " + key.replace('_', ' '))[:120])
    cmd("enchant SilkTouch 1")
    R.place("glass", fc, 0, sy + 1, 2); time.sleep(0.4); aim_cell(fc, 0, sy + 1, 2); cmd("mouse left 3"); R.wait(lambda: R.block_at(fc, 0, sy + 1, 2) is None, 3.0, step=0.1); cmd("mouse release"); time.sleep(1.2)
    step("glass with Silk Touch: the glass", "Glass" in cmd("find glass"), cmd("find glass")[:120])
    R.place("blue_ice", fc, -1, sy + 1, 4); R.place("packed_ice", fc, 1, sy + 1, 4); time.sleep(0.6)
    # snow layers: placed and stacked by real right clicks
    give(("snow", 8)); select("Snow")
    aim_cell(fc, 0, sy, 3, face_y=1.0); cmd("mouse right 0.1"); time.sleep(0.6)
    for k in range(3):
        # (on the layer's top: 2 px a layer)
        aim_cell(fc, 0, sy + 1, 3, face_y=(2 * (k + 1)) / 16 - 0.02); cmd("mouse right 0.1"); time.sleep(0.6)
    b = R.block_at(fc, 0, sy + 1, 3)
    cmd("look 0 30"); time.sleep(0.5); shot("ice_snow")
    step("snow placed, then three more on it: a stack of four layers", b and b[0] == "snow" and b[1] == 3, b)
    give(("iron_shovel", 1)); select("Iron Shovel")
    s0 = len(re.findall(r"Snowball", cmd("find snowball")))
    aim_cell(fc, 0, sy + 1, 3, face_y=0.3); cmd("mouse left 2"); R.wait(lambda: R.block_at(fc, 0, sy + 1, 3) is None, 2.0, step=0.1); cmd("mouse release"); time.sleep(1.2)
    n = sum(int(x) for x in re.findall(r"Snowballx(\d+)", cmd("find snowball")))
    step("four layers dug: four snowballs", n >= 4, cmd("find snowball")[:150])

def paths():
    # (#73) a shovel's use button on grass makes a dirt path; a hoe on coarse dirt makes dirt
    fc, sy = flat(114, 5)
    R.place("grass", fc, 0, sy, 2); R.place("coarse_dirt", fc, 1, sy, 2); time.sleep(0.6)
    give(("iron_shovel", 1), ("iron_hoe", 1)); select("Iron Shovel")
    aim_cell(fc, 0, sy, 2, face_y=1.0); cmd("mouse right 0.1"); time.sleep(0.8)
    b = R.block_at(fc, 0, sy, 2)
    step("a shovel on grass: a dirt path", b and b[0] == "dirt_path", b)
    select("Iron Hoe"); aim_cell(fc, 1, sy, 2, face_y=1.0); cmd("mouse right 0.1"); time.sleep(0.8)
    b = R.block_at(fc, 1, sy, 2)
    step("a hoe on coarse dirt: dirt", b and b[0] == "dirt", b)
    cmd("look 0 45"); time.sleep(0.5); shot("path")

def lanterns():
    # (#76) a lantern standing and one hanging under a block; a soul lantern and a soul torch
    fc, sy = flat(115, 5)
    R.place("stone", fc, 0, sy + 3, 3); R.place("stone", fc, 0, sy + 2, 3); time.sleep(0.4)
    cmd(f"breakabs {fc[0]} {sy + 2} {fc[2] + 3}"); time.sleep(0.4)
    give(("lantern", 2), ("soul_lantern", 1), ("soul_torch", 2)); select("Lantern")
    aim_cell(fc, -1, sy, 3, face_y=1.0); cmd("mouse right 0.1"); time.sleep(0.6)
    aim_cell(fc, 0, sy + 3, 3, face_y=0.0); cmd("mouse right 0.1"); time.sleep(0.6)
    select("Soul Lantern"); aim_cell(fc, 1, sy, 3, face_y=1.0); cmd("mouse right 0.1"); time.sleep(0.6)
    select("Soul Torch"); aim_cell(fc, 2, sy, 3, face_y=1.0); cmd("mouse right 0.1"); time.sleep(0.8)
    near = [b for b in cmd("near 6").split(" ; ") if "lantern" in b or "soul_torch" in b]
    print("   ", near)
    step("a lantern stands, one hangs (facing down), a soul lantern and a soul torch placed", len(near) >= 4 and any(" f0 " in b + " " and b.startswith("lantern") for b in near), near)
    cmd("time 18"); time.sleep(1.0); cmd("look 0 15"); time.sleep(0.6); shot("lanterns"); cmd("time 9")

def drops():
    # (#79) a bookshelf drops 3 books; lapis ore 4-9; gilded blackstone mostly itself, now and then nuggets
    fc, sy = flat(61, 5)
    give(("iron_axe", 1)); select("Iron Axe")
    R.place("bookshelf", fc, 0, sy + 1, 2); time.sleep(0.4)
    aim_cell(fc, 0, sy + 1, 2); cmd("mouse left 4"); R.wait(lambda: R.block_at(fc, 0, sy + 1, 2) is None, 4.0, step=0.1); cmd("mouse release"); time.sleep(1.2)
    n = sum(int(x) for x in re.findall(r"Bookx(\d+)", cmd("find book")))
    step("a bookshelf broken: 3 books", n == 3, cmd("find book")[:150])
    give(("iron_pickaxe", 1)); select("Iron Pickaxe")
    got = []
    for k in range(3):
        before = sum(int(x) for x in re.findall(r"Lapis Lazulix(\d+)", cmd("find lapis")))
        R.place("lapis_ore", fc, 0, sy + 1, 2); time.sleep(0.4)
        aim_cell(fc, 0, sy + 1, 2); cmd("mouse left 4"); R.wait(lambda: R.block_at(fc, 0, sy + 1, 2) is None, 4.0, step=0.1); cmd("mouse release"); time.sleep(1.2)
        got.append(sum(int(x) for x in re.findall(r"Lapis Lazulix(\d+)", cmd("find lapis"))) - before)
    step("lapis ore: 4-9 lapis a block", all(4 <= g <= 9 for g in got), got)
    blocks = nuggets = 0
    for k in range(6):
        R.place("gilded_blackstone", fc, 0, sy + 1, 2); time.sleep(0.4)
        aim_cell(fc, 0, sy + 1, 2); cmd("mouse left 4"); R.wait(lambda: R.block_at(fc, 0, sy + 1, 2) is None, 4.0, step=0.1); cmd("mouse release"); time.sleep(1.0)
    f = cmd("find gilded") + cmd("find nugget")
    blocks = sum(int(x) for x in re.findall(r"Gilded Blackstonex(\d+)", f)); nuggets = sum(int(x) for x in re.findall(r"Gold Nuggetx(\d+)", f))
    step("gilded blackstone: mostly the block itself (Minecraft's 90%)", blocks >= 3, (blocks, nuggets))

def grid():
    # (#82) a written book and two books and quills: two copies, the original stays
    R.start_flat(62, [(0, 2)]); cmd("god 1"); cmd("gamemode survival")
    give(("writable_book", 3)); select("Book and Quill")
    cmd("rmb"); time.sleep(0.8)  # (the use button opens it)
    print(cmd("book type Hello_from_the_play_test")[:120]); print(cmd("book sign Copied")[:120])
    time.sleep(1.0); print("   ", slots())
    cmd("craftui open table"); time.sleep(0.6)
    st_ = cmd("craftui state"); print("   ", st_[:300])
    out = T.grid_craft([("written_book", (0,)), ("writable_book", (1, 2))])
    step("a written book and two books and quills: two written copies", out.startswith("written_book#") and "x2" in out, out)
    cmd("craftui click out 0 shift"); time.sleep(0.6)
    st_ = cmd("craftui state"); cmd("craftui close")
    step("the original stays in the grid, the copies come out", st_.count("written_book#") >= 2, st_[:300])

def stairs():
    # (#77) stairs meet in corners: an L of stairs makes an outer corner, the inside of an L an inner one; upside-down too
    fc, sy = flat(63, 7)
    # (facing: 2 north, 3 south, 4 west, 5 east; the facing is toward the low side)
    for dz in range(1, 5): R.place("oak_stairs", fc, -2, sy + 1, dz, facing=4)
    for dx in range(-1, 2): R.place("oak_stairs", fc, dx, sy + 1, 5, facing=3)
    R.place("stone_brick_stairs", fc, 1, sy + 1, 1, facing=5); R.place("stone_brick_stairs", fc, 2, sy + 1, 1, facing=5); R.place("stone_brick_stairs", fc, 2, sy + 1, 2, facing=3)
    time.sleep(1.5)
    cmd("look 0 45"); time.sleep(0.5); shot("stair_corners")
    cmd("look 180 45"); time.sleep(0.5); shot("stair_corners_back")
    v = cmd("stairsv")
    print("   ", v)
    step("corner stairs take Minecraft's corner shapes (variants other than straight)", re.search(r"v[1-4]", v) is not None, v)

def pillars():
    # (#78) basalt, bone block and stems placed sideways lie along the face they're put on
    fc, sy = flat(64, 5)
    R.place("stone", fc, 0, sy + 1, 3); time.sleep(0.4)
    give(("basalt", 1), ("bone_block", 1), ("crimson_stem", 1))
    for i, (key, name) in enumerate((("basalt", "Basalt"), ("bone_block", "Bone Block"), ("crimson_stem", "Crimson Stem"))):
        select(name); R.place("stone", fc, i - 1, sy + 1, 4); time.sleep(0.3)
        aim_cell(fc, i - 1, sy + 1, 4, face_y=0.5); cmd("mouse right 0.1"); time.sleep(0.6)
    near = [b for b in cmd("near 6").split(" ; ") if b.startswith(("basalt", "bone_block", "crimson_stem"))]
    print("   ", near)
    step("placed against a wall: on their side (facing a side, not up)", near and all(" f1 " not in b + " " for b in near), near)
    cmd("look 0 20"); time.sleep(0.5); shot("pillars")

def nether():
    # (#75) the crimson and warped woods: planks from a stem, hyphae, stripped by an axe, doors, signs; they don't burn
    fc, sy = flat(65, 5)
    craft_all([
        ("a crimson stem", [("crimson_stem", (4,))], [("crimson_stem", 1)], "crimson_planksx4"),
        ("4 warped stems", [("warped_stem", (0, 1, 3, 4))], [("warped_stem", 4)], "warped_hyphaex3"),
        ("6 crimson planks", [("crimson_planks", (0, 1, 3, 4, 6, 7))], [("crimson_planks", 6)], "crimson_doorx3"),
        ("crimson planks and a stick", [("crimson_planks", (0, 1, 2, 3, 4, 5)), ("stick", (7,))], [("crimson_planks", 6), ("stick", 1)], "crimson_signx3"),
        ("warped planks for a crafting table", [("warped_planks", (0, 1, 3, 4))], [("warped_planks", 4)], "crafting_tablex1"),
    ])
    for i, k in enumerate(("crimson_planks", "warped_planks", "crimson_hyphae", "stripped_warped_stem", "crimson_door", "warped_trapdoor")):
        R.place(k, fc, i - 2, sy + 1, 3, facing=3)
    time.sleep(1.0)
    R.place("warped_stem", fc, 0, sy + 1, 1); time.sleep(0.4)
    give(("iron_axe", 1)); select("Iron Axe")
    aim_cell(fc, 0, sy + 1, 1); cmd("mouse right 0.1"); time.sleep(0.8)
    b = R.block_at(fc, 0, sy + 1, 1)
    step("an axe's use button on a warped stem: stripped", b and b[0] == "stripped_warped_stem", b)
    cmd("look 0 25"); time.sleep(0.5); shot("nether_woods")
    s = furnace_run("cobblestone", 1, "crimson_planks", 4)
    step("crimson planks don't go in as fuel (Minecraft's: the Nether's woods don't burn)", "crimson_planks" in s.split("hotbar=")[-1] or "fuel=-" in s or "fuel=" not in s, s[:200])

def creative():
    R.start_flat(66, [(0, 2)]); cmd("gamemode creative"); time.sleep(0.4); cmd("creativeui open"); time.sleep(0.8)
    seen = set()
    for t in range(14):
        cmd(f"creativeui click tabs {t}"); time.sleep(0.2)
        seen |= set(re.search(r"items=\[([^\]]*)\]", cmd("creativeui state")).group(1).split(","))
    cmd("creativeui click tabs 0"); time.sleep(0.4); shot("creative")
    cmd("creativeui close"); cmd("gamemode survival")
    want = ["lapis_block", "emerald_block", "netherite_block", "polished_basalt", "packed_ice", "blue_ice", "snow", "coarse_dirt", "dirt_path", "lantern", "soul_lantern", "soul_torch", "white_carpet", "black_stained_glass", "pink_stained_glass_pane", "cyan_bed", "crimson_planks", "warped_door", "crimson_sign", "warped_shelf", "charcoal"]
    missing = [w for w in want if w not in seen]
    step("the new blocks and items are in the creative menu", not missing, missing)

SECTIONS = {k: v for k, v in globals().items() if k in ("storage", "smelt", "colours", "ice", "paths", "lanterns", "drops", "grid", "stairs", "pillars", "nether", "creative")}

if __name__ == "__main__":
    t0 = time.time()
    for s in sys.argv[1:]:
        print("==", s); SECTIONS[s]()
    print(f"-- {sum(1 for _, ok in log if ok)}/{len(log)} ok, {time.time() - t0:.0f} s")
    for n, ok in log:
        if not ok: print("BAD", n)
