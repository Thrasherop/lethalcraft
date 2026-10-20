"""#244 part G hand play: redstone, utility blocks and the world's rules, as a player meets them (real [E], clicks and
right-clicks where there are some; blocks laid with dev placeabs). Screenshots in shots/wg_*.png.
usage: py tools/world_play.py <section> [...]
sections: dust lock ore note dispenser crafter rails chest door bed farm water fire sand tnt ice honey lightning"""
import math, sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from regress import cmd, S
import pilot
HERE = os.path.dirname(os.path.abspath(__file__))
log = []
def shot(n): cmd(f"screenshot {os.path.join(HERE, '..', 'shots', 'wg_' + n + '.png')}"); time.sleep(0.5)
def step(name, ok, detail=""):
    log.append((name, ok)); print(("OK  " if ok else "BAD ") + name + (f"  ({detail})" if detail else ""), flush=True)
def g(): return dict(kv.split("=", 1) for kv in cmd("f244g").split(" ") if "=" in kv)
def num(k): v = g()[k]; return int(v.split("/")[0])
def wait_for(f, most, s=0.5):
    t = time.time()
    while time.time() - t < most:
        if f(): return True
        time.sleep(s)
    return f()

def spot(i):
    fc = R.start_flat(i, [(dx, dz) for dx in range(0, 5) for dz in range(0, 9)])
    cmd("god 1"); cmd("gamemode survival"); cmd("hunger 20 5")
    sy = R.surface(fc, 2, 2)
    return fc, (sy if sy is not None else fc[1] - 1) + 1  # (the cells on the ground)

def put(key, fc, dx, y, dz, facing=1): return R.place(key, fc, dx, y, dz, facing)
def blk(fc, dx, y, dz):
    """(key, facing, state) of the block at that cell, or None"""
    x, z = fc[0] + dx, fc[2] + dz
    for e in cmd("near 30").split(" ; "):
        m = re.match(r"\s*([a-z_0-9]+)\((-?\d+), (-?\d+), (-?\d+)\)y-?\d+ f(\d+) s(\d+)", e)
        if m and int(m.group(2)) == x and int(m.group(3)) == y and int(m.group(4)) == z: return (m.group(1), int(m.group(5)), int(m.group(6)))
    return None
def aim_cell(fc, dx, y, dz, up=0.5): pilot.aim_at((fc[0] + dx + 0.5) * S, (y + up) * S + R.YO, (fc[2] + dz + 0.5) * S); time.sleep(0.3)
def near_cell(fc, dx, y, dz, back=2):
    """within reach of that cell: stood two cells south of it unless already that near"""
    p = R.pos(); cx, cz = (fc[0] + dx + 0.5) * S, (fc[2] + dz + 0.5) * S
    if math.hypot(p[0] - cx, p[2] - cz) > 3.5: R.stand_on(fc, dx, y, dz - back)
def press_e(fc, dx, y, dz, up=0.5): near_cell(fc, dx, y, dz); aim_cell(fc, dx, y, dz, up); cmd("keys E 0.1"); time.sleep(0.6)
def lit(fc, dx, y, dz): b = blk(fc, dx, y, dz); return b is not None and (b[2] & 1) == 1

def dust():
    # (125) dust powers only where it points; [E] makes a lone dust a dot
    fc, y = spot(200)
    put("redstone_block", fc, 0, y, 2)
    for dx in (1, 2, 3): put("redstone_dust", fc, dx, y, 2)
    put("redstone_lamp", fc, 2, y, 3)   # (beside the line's middle: not pointed at)
    put("redstone_lamp", fc, 4, y, 2)   # (at its end: pointed at)
    time.sleep(1.5)
    aim_cell(fc, 2, y, 2, 0.1); shot("dust_line")
    step("a lamp beside a line of dust stays dark", not lit(fc, 2, y, 3), blk(fc, 2, y, 3))
    step("a lamp at the line's end lights", lit(fc, 4, y, 2), blk(fc, 4, y, 2))
    # (a lone dust powered from under it, nothing beside it to join: Minecraft's cross, all four ways; one neighbour that
    # joins (a redstone block beside it) would make it a line instead)
    put("redstone_block", fc, 1, y, 7); put("redstone_dust", fc, 1, y + 1, 7); put("stone", fc, 2, y, 7); put("redstone_lamp", fc, 2, y + 1, 7)
    time.sleep(1.5)
    aim_cell(fc, 1, y + 1, 7, 0.05); shot("dust_cross")
    step("a lone dust (a cross) lights the lamp beside it", lit(fc, 2, y + 1, 7), cmd(f"dot {fc[0] + 1} {y + 1} {fc[2] + 7}"))
    press_e(fc, 1, y + 1, 7, 0.05); time.sleep(1.0)
    shot("dust_dot")
    step("[E] makes it a dot: the lamp goes out", not lit(fc, 2, y + 1, 7), cmd(f"dot {fc[0] + 1} {y + 1} {fc[2] + 7}"))
    press_e(fc, 1, y + 1, 7, 0.05); time.sleep(1.0)
    step("[E] again: a cross, the lamp lights", lit(fc, 2, y + 1, 7), cmd(f"dot {fc[0] + 1} {y + 1} {fc[2] + 7}"))

def lock():
    # (126) a repeater locked by a powered repeater at its side
    fc, y = spot(201)
    put("repeater", fc, 1, y, 2, 5); put("redstone_lamp", fc, 2, y, 2)            # A: east into a lamp
    put("repeater", fc, 1, y, 3, 3); put("redstone_block", fc, 1, y, 4)           # B: south into A's side, powered
    time.sleep(1.5)
    a = blk(fc, 1, y, 2)
    step("a powered repeater into its side locks it (the bar shows)", a is not None and (a[2] & 8) != 0, a)
    put("redstone_block", fc, 0, y, 2); time.sleep(1.5)                            # power behind A
    step("locked, it doesn't pass on what's behind it", not lit(fc, 2, y, 2), blk(fc, 2, y, 2))
    aim_cell(fc, 1, y, 2, 0.1); shot("repeater_locked")
    cmd(f"breakabs {fc[0] + 1} {y} {fc[2] + 4}"); time.sleep(1.5)
    step("unlocked, it does", lit(fc, 2, y, 2), blk(fc, 2, y, 2))

def ore():
    # (129) redstone ore lights when stood on or hit
    fc, y = spot(202)
    put("redstone_ore", fc, 2, y, 3)
    o0 = num("oreLit")
    cmd(f"tp {(fc[0] + 2.5) * S:.2f} {(y + 1.05) * S:.2f} {(fc[2] + 3.5) * S:.2f}"); time.sleep(1.0)
    step("stepping on redstone ore lights it", num("oreLit") > o0 and (blk(fc, 2, y, 3) or ("",))[0].endswith("_lit"), blk(fc, 2, y, 3))
    cmd("look 0 80"); time.sleep(0.3); shot("ore_lit")

def note():
    # (131) the instrument from the block under it; a punch plays it; a block on top silences it
    fc, y = spot(203)
    put("sand", fc, 2, y, 3); put("note_block", fc, 2, y + 1, 3)
    n0 = num("notes")
    aim_cell(fc, 2, y + 1, 3); cmd("mouse left 0.1"); time.sleep(0.8)
    step("a punch plays the note block (snare on sand)", num("notes") > n0, g()["notes"])
    put("stone", fc, 2, y + 2, 3); n1 = num("notes")
    press_e(fc, 2, y + 1, 3); aim_cell(fc, 2, y + 1, 3); cmd("mouse left 0.1"); time.sleep(0.8)
    step("a block on top: no note", num("notes") == n1, g()["notes"])

def dispenser():
    # (135) flint and steel lights TNT; armor goes onto you
    fc, y = spot(204)
    put("dispenser", fc, 1, y, 3, 5); put("tnt", fc, 2, y, 3)
    R.hold("flint_and_steel", name="Flint and Steel")
    print("  ", cmd(f"chestui open {fc[0] + 1} {y} {fc[2] + 3}")); time.sleep(0.5)
    cmd("chestui click hot 0 shift"); time.sleep(0.4); print("  ", cmd("chestui state")[:200]); cmd("chestui close")
    t0 = int(g()["tnt"].split("/")[0])
    put("redstone_block", fc, 1, y, 2); time.sleep(1.5)
    step("a dispenser's flint and steel lights the TNT in front (lit TNT, a thing of its own)", int(g()["tnt"].split("/")[0]) > t0, g()["tnt"])
    shot("dispenser_tnt"); time.sleep(5.0)

def crafter():
    # (136) a crafter's empty slot clicked off
    fc, y = spot(205)
    put("crafter", fc, 2, y, 3, 2)
    print("  ", cmd(f"chestui open {fc[0] + 2} {y} {fc[2] + 3}")); time.sleep(0.5)
    cmd("chestui click chest 4"); time.sleep(0.6); stt = cmd("chestui state")
    shot("crafter_off")
    step("an empty crafter slot clicked: off", "off" in stt, stt[:160])
    cmd("chestui close")

def rails():
    # (137) a T-junction turns with a signal; an activator rail puts the rider out
    fc, y = spot(206)
    for dx in (0, 1, 2): put("rail", fc, dx, y, 3)
    put("rail", fc, 1, y, 4); time.sleep(1.0)
    before = blk(fc, 1, y, 3)
    put("redstone_block", fc, 1, y, 2); time.sleep(1.5)
    after = blk(fc, 1, y, 3)
    aim_cell(fc, 1, y, 3, 0.05); shot("rail_t")
    step("a redstone signal turns a T-junction the other way", before and after and (before[2] & 15) != (after[2] & 15), (before, after))

def chest():
    # (139) a chest with a solid block on top won't open
    fc, y = spot(207)
    put("chest", fc, 2, y, 3, 3); put("stone", fc, 2, y + 1, 3)
    press_e(fc, 2, y, 3, 0.3)
    stt = cmd("chestui state")
    step("a chest with stone on it won't open", "open=False" in stt or "open=True" not in stt, stt[:80])
    cmd("chestui close")

def door():
    # (141) two doors side by side mirror
    fc, y = spot(208)
    for dx in range(0, 4):
        for dz in range(2, 5): put("stone", fc, dx, y - 1, dz)  # (a floor for them)
    R.hold("oak_door", 2, name="Oak Door")
    R.stand_on(fc, 1, y, 1)  # (in reach, facing them)
    # (Minecraft's getHinge: the second put on the first one's right (as you face them) mirrors it; on its left it doesn't)
    # (the first clicked on its left half: its hinge on the left, as you face it; Minecraft's)
    aim_cell(fc, 0.75, y - 1, 3, 0.98); cmd("mouse right 0.1"); time.sleep(0.8)
    aim_cell(fc, 2, y - 1, 3, 0.98); cmd("mouse right 0.1"); time.sleep(0.8)
    a, b = blk(fc, 2, y, 3), blk(fc, 1, y, 3)
    aim_cell(fc, 1, y, 3, 1.0); shot("doors")
    step("two doors side by side: their hinges mirror", a and b and (a[2] & 4) != (b[2] & 4), (a, b))

def bed():
    # (142) landing on a bed bounces
    fc, y = spot(209)
    put("red_bed", fc, 2, y, 3, 2)
    b0 = num("bedBounces")
    cmd(f"tp {(fc[0] + 2.5) * S:.2f} {(y + 4) * S:.2f} {(fc[2] + 3.5) * S:.2f}"); time.sleep(2.5)
    step("falling onto a bed bounces", num("bedBounces") > b0, g()["bedBounces"])

def farm():
    # (143) a block on farmland turns it to dirt; a jump onto it tramples it
    fc, y = spot(210)
    put("farmland", fc, 2, y - 1, 3); put("stone", fc, 2, y, 3)
    time.sleep(2.0)
    step("farmland under a block turns to dirt", (blk(fc, 2, y - 1, 3) or ("",))[0] == "dirt", blk(fc, 2, y - 1, 3))

def water():
    # (144) water runs toward the nearest drop: a channel walled in, a dead end west, a hole east (Minecraft's getSpread)
    fc, y = spot(211)
    y += 1  # (a block up, clear of the ground's ups and downs)
    for dx in range(0, 5):
        put("stone", fc, dx, y, 2); put("stone", fc, dx, y, 4)            # (its walls)
        if dx < 4 and blk(fc, dx, y - 1, 3) is None: put("stone", fc, dx, y - 1, 3)  # (its floor)
    put("stone", fc, 0, y, 3)                                              # (the dead end)
    R.dig(fc, 4, y - 1, 3)                                                 # (the hole)
    if blk(fc, 4, y, 3) is not None: R.dig(fc, 4, y, 3)                     # (the ground there may be a block higher)
    R.hold("water_bucket", name="Water Bucket")                            # (poured from a bucket, as a player does)
    R.stand_on(fc, 1, y + 1, 2); aim_cell(fc, 2, y - 1, 3, 0.98); cmd("mouse right 0.1")
    time.sleep(3.0)
    print("  ", [blk(fc, dx, y, 3) for dx in range(0, 5)], [blk(fc, dx, y - 1, 3) for dx in range(0, 5)])
    west, east = blk(fc, 1, y, 3), blk(fc, 3, y, 3)
    aim_cell(fc, 3, y, 3, 0.1); shot("water_drop")  # (on its wall, looking along it)
    step("water runs toward the drop, not the other way", east is not None and east[0] == "water" and (west is None or west[0] != "water"), (west, east))

def fire():
    # (146) fire on soul sand is soul fire
    fc, y = spot(212)
    put("soul_sand", fc, 2, y, 3)
    R.hold("flint_and_steel", name="Flint and Steel")
    put("stone", fc, 2, y, 1); R.stand_on(fc, 2, y + 1, 1); aim_cell(fc, 2, y, 3, 0.9); cmd("mouse right 0.1"); time.sleep(1.0)  # (from a block up: onto its top)
    f = blk(fc, 2, y + 1, 3)
    if f: aim_cell(fc, 2, y + 1, 3, 0.3)
    shot("soul_fire")
    step("fire on soul sand is soul fire", f is not None and f[0] == "soul_fire", f)

def sand():
    # (147) sand falling onto a torch breaks into its item
    fc, y = spot(213)
    put("torch", fc, 2, y, 3); b0 = num("brokeFalling"); put("sand", fc, 2, y + 3, 3)
    time.sleep(2.5)
    step("sand falling onto a torch breaks into an item", num("brokeFalling") > b0 and "sand" in cmd("loose 6").lower(), cmd("loose 6")[:120])

def tnt():
    # (148) lit TNT is a thing: it hops, falls and goes off
    fc, y = spot(214)
    put("tnt", fc, 2, y + 2, 3); put("stone", fc, 2, y + 1, 3)
    R.hold("flint_and_steel", name="Flint and Steel")
    t0 = int(g()["tnt"].split("/")[0])
    press_e(fc, 2, y + 2, 3) if False else None
    aim_cell(fc, 2, y + 2, 3); cmd("mouse right 0.1"); time.sleep(0.6)
    step("flint and steel on TNT: lit TNT (Primed TNT)", int(g()["tnt"].split("/")[0]) > t0 and "Primed TNT" in cmd("enemies"), g()["tnt"])
    cmd(f"breakabs {fc[0] + 2} {y + 1} {fc[2] + 3}")
    cmd(f"tp {(fc[0] + 2.5) * S:.2f} {y * S:.2f} {(fc[2] - 6) * S:.2f}"); aim_cell(fc, 2, y + 1, 3); shot("tnt_lit")
    time.sleep(4.0)
    step("it goes off", int(g()["tnt"].split("/")[1]) > 0, g()["tnt"])

def ice():
    # (149) ice is slippery: let go of the keys and you slide on
    fc, y = spot(215)
    for dz in range(0, 9): put("packed_ice", fc, 2, y, dz)  # (a walkway a block up)
    cmd(f"tp {(fc[0] + 2.5) * S:.2f} {(y + 1) * S + R.YO + 0.05:.2f} {(fc[2] + 0.5) * S:.2f}"); time.sleep(0.6)
    pilot.face((fc[0] + 2.5) * S, (fc[2] + 10) * S)
    pilot.walk(1.5, "W"); p1 = pilot.pos(); time.sleep(1.0); p2 = pilot.pos()
    slid = abs(p2[2] - p1[2])
    step("on ice you slide on after letting go", slid > 0.3 * S, f"{slid / S:.2f} blocks")

def honey():
    # (149) falling past honey you slide down slowly
    fc, y = spot(216)
    for dy in range(0, 5): put("honey_block", fc, 3, y + dy, 3)
    w0 = num("honeyWall")
    cmd(f"tp {(fc[0] + 2.6) * S:.2f} {(y + 6) * S + R.YO:.2f} {(fc[2] + 3.5) * S:.2f}"); time.sleep(3.0)  # (against its west side)
    step("you slide down the side of honey", num("honeyWall") > w0, g()["honeyWall"])

def lightning():
    # (150) a strike on a rod scrapes the copper under it
    fc, y = spot(217)
    put("exposed_copper", fc, 2, y, 3); put("lightning_rod", fc, 2, y + 1, 3, 1); put("redstone_lamp", fc, 3, y, 3)
    s0 = num("scrapes")
    print("  ", cmd("lightning 30"))
    seen = False
    for _ in range(20):
        time.sleep(0.1)
        if lit(fc, 3, y, 3): seen = True; break
    step("a struck rod strongly powers the block under it (the lamp beside that flashes on)", seen, blk(fc, 3, y, 3))
    time.sleep(1.0)
    c = blk(fc, 2, y, 3)
    step("a strike on the rod scrapes the copper under it", num("scrapes") > s0 and c is not None and c[0] == "copper_block", c)

def into(fc, dx, y, dz, key, n, name=None):
    """n of key put into the container at that cell, by hand: in the hand, its screen opened, shift-clicked in"""
    R.hold(key, n, name=name); near_cell(fc, dx, y, dz)
    cmd(f"chestui open {fc[0] + dx} {y} {fc[2] + dz}"); time.sleep(0.4)
    cmd("chestui click hot 0 shift"); time.sleep(0.4); cmd("chestui close"); time.sleep(0.3)
def contents(fc, dx, y, dz): return cmd(f"transport {fc[0] + dx} {y} {fc[2] + dz}")
def power(fc, dx, y, dz): b = blk(fc, dx, y, dz); return None if b is None else b[2] & 15

def comp():
    # (127) a comparator reads a chest through a block; (130) a jukebox's disc, and a playing jukebox powers what's beside it
    fc, y = spot(218)
    put("chest", fc, 0, y, 3, 3); put("stone", fc, 1, y, 3); put("comparator", fc, 2, y, 3, 5); put("redstone_lamp", fc, 3, y, 3)
    into(fc, 0, y, 3, "cobblestone", 64, "Cobblestone")
    time.sleep(1.0)
    aim_cell(fc, 2, y, 3, 0.1); shot("comparator_through")
    step("a comparator reads a chest through a block (its lamp lights)", lit(fc, 3, y, 3) and (power(fc, 2, y, 3) or 0) >= 1, (blk(fc, 2, y, 3), contents(fc, 0, y, 3)[:60]))
    put("jukebox", fc, 0, y, 6); put("comparator", fc, 1, y, 6, 5); put("redstone_lamp", fc, 0, y, 7)
    R.hold("music_disc_cat", 1, name="Music Disc"); press_e(fc, 0, y, 6, 0.8); time.sleep(1.5)
    c = power(fc, 1, y, 6)
    step("a comparator reads the disc in a jukebox (cat: 2)", c == 2, (blk(fc, 1, y, 6), cmd("jukebox")[:80]))
    step("a playing jukebox powers the lamp beside it", lit(fc, 0, y, 7), blk(fc, 0, y, 7))
    aim_cell(fc, 0, y, 6, 0.5); shot("jukebox_playing")
    press_e(fc, 0, y, 6, 0.8); time.sleep(1.0)  # (the disc out again)

def strong():
    # (128) a detector rail with a cart on it strongly powers the block under it: a lamp beside that block lights
    fc, y = spot(219)
    put("stone", fc, 2, y, 3); put("detector_rail", fc, 2, y + 1, 3); put("redstone_lamp", fc, 3, y, 3)
    time.sleep(1.0)
    before = lit(fc, 3, y, 3)
    R.hold("minecart", 1, name="Minecart"); near_cell(fc, 2, y + 1, 3); aim_cell(fc, 2, y + 1, 3, 0.1); cmd("mouse right 0.1"); time.sleep(1.5)
    after = lit(fc, 3, y, 3)
    aim_cell(fc, 2, y, 3, 0.6); shot("detector_strong")
    step("a cart on a detector rail strongly powers the block under it (a lamp beside that lights)", not before and after, (before, after, cmd("carts")[:100]))

def notes():
    # (131) each block under a note block its own instrument (Minecraft 26.3's)
    fc, y = spot(220)
    want = [("stone", "basedrum"), ("sand", "snare"), ("glass", "hat"), ("wool_white", "guitar"), ("bone_block", "xylophone"),
            ("iron_block", "iron_xylophone"), ("soul_sand", "cow_bell"), ("pumpkin", "didgeridoo"), ("emerald_block", "bit"), ("hay_block", "banjo")]
    got = []
    for i, (under, inst) in enumerate(want):
        dx, dz = i % 5, 3 + 2 * (i // 5)
        put(under, fc, dx, y, dz); put("note_block", fc, dx, y + 1, dz)
    time.sleep(0.5)
    for i, (under, inst) in enumerate(want):
        dx, dz = i % 5, 3 + 2 * (i // 5)
        near_cell(fc, dx, y + 1, dz); cmd("recentsounds clear"); aim_cell(fc, dx, y + 1, dz); cmd("mouse left 0.1"); time.sleep(0.6)
        got.append((under, inst, "note." + inst in cmd("recentsounds")))
    shot("note_blocks")
    step("ten instruments from the block under (basedrum ... banjo)", all(g_ for _, _, g_ in got), [f"{u}:{i}" for u, i, g_ in got if not g_])

def arrows():
    # (132) an arrow presses a wooden button; a thrown snowball hits a target; tripwire: shears disarm it
    fc, y = spot(221)
    put("stone", fc, 2, y, 6); put("stone", fc, 2, y + 1, 6); put("oak_button", fc, 2, y + 1, 5, 3); put("redstone_lamp", fc, 1, y + 1, 6)
    R.hold("bow", 1, name="Bow"); cmd("invgive arrow 4"); time.sleep(0.6)
    R.stand_on(fc, 2, y, 1); aim_cell(fc, 2, y + 1, 5, 0.5)
    a0 = num("arrowPresses"); cmd("rmb 1.2"); time.sleep(2.0)
    step("an arrow presses a wooden button", num("arrowPresses") > a0, (g()["arrowPresses"], blk(fc, 2, y + 1, 5)))
    shot("arrow_button")
    put("target", fc, 0, y, 6); put("redstone_lamp", fc, 0, y, 7)
    t0 = num("targetHits"); R.hold("snowball", 4, name="Snowball")
    R.stand_on(fc, 0, y, 2); aim_cell(fc, 0, y, 6, 0.5); cmd("rmb 0.1"); time.sleep(1.5)
    step("a snowball hits a target (it lights the lamp behind)", num("targetHits") > t0, (g()["targetHits"], blk(fc, 0, y, 6)))
    # tripwire: two hooks facing each other, string between; shears cut it without tripping
    put("tripwire_hook", fc, 4, y, 2, 3); put("tripwire_hook", fc, 4, y, 6, 2)
    for dz in (3, 4, 5): put("tripwire", fc, 4, y, dz)
    time.sleep(1.0); w0 = num("wireTrips")
    R.hold("shears", 1, name="Shears"); R.stand_on(fc, 3, y, 4); aim_cell(fc, 4, y, 4, 0.05); cmd("mouse left 0.4"); time.sleep(1.0)
    step("shears cut tripwire without setting it off", num("wireTrips") == w0, (g()["wireTrips"], blk(fc, 4, y, 4)))

def pot():
    # (133) a piston pushing a decorated pot breaks it (its contents drop)
    fc, y = spot(222)
    put("piston", fc, 1, y, 3, 5); put("decorated_pot", fc, 2, y, 3)
    into(fc, 2, y, 3, "apple", 3, "Apple") if False else None
    put("redstone_block", fc, 0, y, 3); time.sleep(1.5)
    p = blk(fc, 2, y, 3); l = cmd("loose 8")
    shot("pot_pushed")
    step("a piston pushing a decorated pot breaks it into its item", (p is None or p[0] != "decorated_pot") and "pot" in l.lower(), (p, l[:100]))

def hoppers():
    # (134) a hopper under a double chest empties both halves; a hopper into a jukebox plays the disc; composter only from the top
    fc, y = spot(223)
    put("chest", fc, 1, y + 1, 3, 3); put("chest", fc, 2, y + 1, 3, 3); put("hopper", fc, 2, y, 3, 0); put("chest", fc, 2, y - 1, 3, 3) if False else None
    into(fc, 1, y + 1, 3, "cobblestone", 2, "Cobblestone")
    time.sleep(3.0)
    h = contents(fc, 2, y, 3)
    step("a hopper under one half of a double chest takes from the other half too", "cobblestone" in h, h[:120])
    put("jukebox", fc, 0, y, 6); put("hopper", fc, 0, y + 1, 6, 0)
    into(fc, 0, y + 1, 6, "music_disc_13", 1, "Music Disc"); time.sleep(2.0)
    step("a hopper puts a disc into a jukebox: it plays", "13" in cmd("jukebox"), cmd("jukebox")[:100])
    put("composter", fc, 3, y, 6); put("hopper", fc, 4, y, 6, 4)
    into(fc, 4, y, 6, "wheat_seeds", 8, "Wheat Seeds"); time.sleep(2.0)
    hs = contents(fc, 4, y, 6)
    step("a hopper from the side doesn't fill a composter", "wheat_seeds" in hs and power(fc, 3, y, 6) == 0, (hs[:80], blk(fc, 3, y, 6)))
    put("hopper", fc, 3, y + 1, 6, 0); into(fc, 3, y + 1, 6, "wheat_seeds", 16, "Wheat Seeds"); time.sleep(5.0)
    step("from the top it does (the composter fills)", (power(fc, 3, y, 6) or 0) > 0, (contents(fc, 3, y + 1, 6)[:60], blk(fc, 3, y, 6)))
    aim_cell(fc, 1, y + 1, 3, 0.5); shot("hoppers")

def dispenser2():
    # (135) armor onto the player in front; bone meal on stone kept; a minecart put onto the rail in front
    fc, y = spot(224)
    put("dispenser", fc, 2, y, 3, 2)                     # (facing north: out toward dz=4)
    into(fc, 2, y, 3, "iron_chestplate", 1, "Iron Chestplate")
    cmd("clearinv"); cmd("wear none")  # (nothing on: it fills an empty slot only)
    if blk(fc, 2, y - 1, 4) is None: put("stone", fc, 2, y - 1, 4)  # (something to stand on level with it)
    R.stand_on(fc, 2, y, 4)
    e0 = num("equipped"); put("redstone_block", fc, 1, y, 3); time.sleep(1.5)
    step("a dispenser puts armor on the player in front of it", num("equipped") > e0, (g()["equipped"], cmd("state").split(" slots=")[0][-60:]))
    cmd("wear none"); cmd(f"breakabs {fc[0] + 1} {y} {fc[2] + 3}")
    put("dispenser", fc, 0, y, 6, 2); put("stone", fc, 0, y, 7)
    into(fc, 0, y, 6, "bone_meal", 2, "Bone Meal")
    k0 = num("kept"); put("redstone_block", fc, 1, y, 6); time.sleep(1.5)
    c = contents(fc, 0, y, 6)
    step("bone meal on stone fails: the dispenser keeps it", num("kept") > k0 and "bone_meal" in c, (g()["kept"], c[:60]))
    put("dispenser", fc, 3, y, 6, 2); put("rail", fc, 3, y, 7)
    into(fc, 3, y, 6, "minecart", 1, "Minecart")
    p0 = num("placed"); put("redstone_block", fc, 4, y, 6); time.sleep(1.5)
    step("a dispenser puts a minecart onto the rail in front", num("placed") > p0 and "Minecart" in cmd("carts"), (g()["placed"], cmd("carts")[:100]))
    aim_cell(fc, 3, y, 7, 0.2); shot("dispenser_cart")

def rails2():
    # (137) a rail joined at both ends isn't re-bent; a powered activator rail puts its rider out; a moving cart picks a mob up
    fc, y = spot(225)
    for dx in (0, 1, 2): put("rail", fc, dx, y, 3)
    time.sleep(0.8); before = blk(fc, 1, y, 3)
    put("rail", fc, 1, y, 4); time.sleep(0.8); after = blk(fc, 1, y, 3)
    step("a rail joined at both ends isn't re-bent by a new one beside it", before and after and before[2] == after[2], (before, after))
    put("activator_rail", fc, 4, y, 6); put("redstone_block", fc, 4, y - 1, 6) if False else put("redstone_block", fc, 3, y, 6)
    R.hold("minecart", 1, name="Minecart"); near_cell(fc, 4, y, 6); aim_cell(fc, 4, y, 6, 0.1); cmd("mouse right 0.1"); time.sleep(1.0)
    j0 = num("ejected"); cmd("clearinv"); aim_cell(fc, 4, y, 6, 0.4); cmd("keys E 0.1"); time.sleep(1.5)
    step("on a powered activator rail the rider is put out", num("ejected") > j0, (g()["ejected"], cmd("carts")[:100]))
    # a cart launched along a line runs into a pig: the pig gets in (the line north-south: a stop, a powered rail, rails)
    cmd(f"breakabs {fc[0] + 3} {y} {fc[2] + 6}")
    put("stone", fc, 1, y, 0); put("powered_rail", fc, 1, y, 1); put("redstone_block", fc, 0, y, 1)
    for dz in range(2, 9): put("rail", fc, 1, y, dz)
    cmd("enemy pig 3"); time.sleep(0.8)
    m0 = num("mobsTaken")
    cmd(f"enemypin pig {(fc[0] + 1.5) * S:.2f} {y * S + R.YO:.2f} {(fc[2] + 6.5) * S:.2f}"); time.sleep(1.0)
    R.hold("minecart", 1, name="Minecart"); R.stand_on(fc, 2, y, 1); aim_cell(fc, 1, y, 1, 0.1); cmd("mouse right 0.1")
    time.sleep(0.5); cmd("enemypin pig off"); time.sleep(2.5)
    step("a moving empty cart picks up a pig in its way", num("mobsTaken") > m0, (g()["mobsTaken"], cmd("carts")[:120]))
    aim_cell(fc, 1, y, 7, 0.4); shot("cart_pig")

def furnace():
    # (138) out of fuel, a furnace's progress cools back down, not straight to nothing
    fc, y = spot(226)
    put("furnace", fc, 2, y, 3, 3); near_cell(fc, 2, y, 3)
    cmd("invgive cobblestone 4"); cmd("invgive stick 1"); time.sleep(0.6)
    print("  ", cmd("furnaceui open near")[:80]); time.sleep(0.4)
    st = cmd("furnaceui state"); hot = re.search(r"hotbar=\[(.*?)\]", st).group(1).split(",")
    for i, h in enumerate(hot):
        if h.startswith("cobblestone") or h.startswith("stick"): cmd(f"furnaceui click hot {i} shift"); time.sleep(0.3)
    print("  ", cmd("furnaceui state")[:200])
    ps = []
    for _ in range(16):
        time.sleep(0.5); m = re.search(r"burn=([\d.]+)/[\d.]+ progress=([\d.]+)", cmd("furnaceui state"))
        ps.append((float(m.group(1)), float(m.group(2))) if m else None)
    shot("furnace_cooling"); cmd("furnaceui close")
    out = [p for p in ps if p and p[0] == 0]
    step("out of fuel, its progress goes down bit by bit (not at once to 0)", len(out) >= 2 and out[0][1] > 0 and any(0 < b[1] < a[1] for a, b in zip(out, out[1:])), ps)

def gate():
    # (141) a fence gate swings away from whoever opens it
    fc, y = spot(227)
    put("oak_fence_gate", fc, 2, y, 3, 2)
    R.stand_on(fc, 2, y, 1); press_e(fc, 2, y, 3, 0.5); a = blk(fc, 2, y, 3)
    press_e(fc, 2, y, 3, 0.5)  # (shut)
    R.stand_on(fc, 2, y, 5); aim_cell(fc, 2, y, 3, 0.5); cmd("keys E 0.1"); time.sleep(0.6); b = blk(fc, 2, y, 3)
    shot("gate_away")
    step("a gate opened from either side swings away from you", a and b and (a[2] & 1) and (b[2] & 1) and a[1] != b[1], (a, b))

def farm2():
    # (143) a jump onto farmland tramples it back to dirt
    fc, y = spot(228)
    put("farmland", fc, 2, y - 1, 3)
    t0 = num("trampled")
    cmd(f"tp {(fc[0] + 2.5) * S:.2f} {(y + 1.6) * S + R.YO:.2f} {(fc[2] + 3.5) * S:.2f}"); time.sleep(2.0)
    step("falling onto farmland tramples it to dirt", num("trampled") > t0 and (blk(fc, 2, y - 1, 3) or ("",))[0] == "dirt", (g()["trampled"], blk(fc, 2, y - 1, 3)))

def channel(fc, y, z, length=5):
    """a stone channel one wide along x at row z: floor, walls, a stop at its west end"""
    for dx in range(0, length):
        put("stone", fc, dx, y, z - 1); put("stone", fc, dx, y, z + 1)
        if blk(fc, dx, y - 1, z) is None or blk(fc, dx, y - 1, z)[0] != "stone": put("stone", fc, dx, y - 1, z)
        if blk(fc, dx, y, z) is not None: cmd(f"breakabs {fc[0] + dx} {y} {fc[2] + z}")
    put("stone", fc, 0, y, z)  # (its west end shut)

def pour(fc, dx, y, z, what="water_bucket", name="Water Bucket"):
    """a bucket poured onto the floor at that cell from the wall beside it (a second go if the first missed)"""
    for _ in range(2):
        R.hold(what, name=name); R.stand_on(fc, dx, y + 1, z - 1); aim_cell(fc, dx, y - 1, z, 0.98); cmd("mouse right 0.1"); time.sleep(1.0)
        b = blk(fc, dx, y, z)
        if b is not None and b[0] in ("water", "lava"): return True
    return False

def current():
    # (144) flowing water pushes you along, carries items, washes a torch away
    fc, y = spot(229)
    y += 1  # (the channel a block up, clear of the ground's ups and downs)
    channel(fc, y, 3, 5)
    put("torch", fc, 3, y, 3)
    print("   poured:", pour(fc, 1, y, 3)); time.sleep(2.0)
    t = blk(fc, 3, y, 3); l = cmd("loose 10")
    step("water running over a torch washes it off (an item)", (t is None or t[0] != "torch") and "torch" in l.lower(), (t, l[:100]))
    c0 = num("itemsCarried"); cmd("invgive cobblestone 1"); time.sleep(0.4)
    cmd("clearloose 12"); x0 = (fc[0] + 2.5) * S
    cmd(f"giveat {x0:.2f} {y * S + R.YO + 0.2:.2f} {(fc[2] + 3.5) * S:.2f} cobblestone 1"); time.sleep(2.5)  # (an item dropped into the flow)
    print("   item:", cmd("loose 12")[:120], "from x", round(x0, 2))
    step("an item in the flow drifts along", num("itemsCarried") > c0, (g()["itemsCarried"], cmd("loose 10")[:100]))
    cmd(f"tp {(fc[0] + 1.5) * S:.2f} {y * S + R.YO + 0.05:.2f} {(fc[2] + 3.5) * S:.2f}"); time.sleep(0.4)
    x0 = R.pos()[0]; time.sleep(2.0); x1 = R.pos()[0]
    step("standing in the flow, it pushes you along (east)", x1 - x0 > 0.3 * S, f"{(x1 - x0) / S:.2f} blocks")
    R.stand_on(fc, 1, y + 1, 2); aim_cell(fc, 3, y, 3, 0.1); shot("current")

def lava():
    # (145) lava sets fire to burnable blocks near it (Minecraft's random ticks: the game sped up to see it)
    fc, y = spot(230)
    for dx in range(0, 5):
        for dz in (2, 4): put("oak_planks", fc, dx, y, dz)
        put("stone", fc, dx, y - 1, 3)
        if blk(fc, dx, y, 3) is not None: cmd(f"breakabs {fc[0] + dx} {y} {fc[2] + 3}")
    for dx in range(0, 5): put("lava", fc, dx, y, 3)
    l0 = num("lavaFires"); cmd("speed 8"); time.sleep(12.0); cmd("speed 1")
    shot("lava_fires")
    step("lava beside planks sets them alight now and then", num("lavaFires") > l0, g()["lavaFires"])
    for dx in range(0, 5): cmd(f"breakabs {fc[0] + dx} {y} {fc[2] + 3}")

def fire2():
    # (146) fire on netherrack burns on; on planks it spreads and burns them away
    fc, y = spot(231)
    put("netherrack", fc, 0, y, 3)
    R.hold("flint_and_steel", name="Flint and Steel")
    for _ in range(2):
        if blk(fc, 0, y + 1, 3) is None: R.stand_on(fc, 0, y, 1); aim_cell(fc, 0, y, 3, 0.9); cmd("mouse right 0.1"); time.sleep(0.5)
    if blk(fc, 0, y + 1, 3) is None: put("fire", fc, 0, y + 1, 3)  # (the flint's aim missed on this ground: the fire put there)
    print("   netherrack lit:", blk(fc, 0, y + 1, 3))
    for dx in range(2, 5):
        for dz in range(2, 5): put("oak_planks", fc, dx, y, dz)
    R.stand_on(fc, 3, y, 0); aim_cell(fc, 3, y, 2, 0.9); cmd("mouse right 0.1"); time.sleep(0.5)
    s0, b0 = num("fireSpread"), num("burned")
    cmd("speed 8"); time.sleep(10.0); cmd("speed 1")
    n = blk(fc, 0, y + 1, 3)
    shot("fire_spread")
    step("fire on netherrack still burns (after over a minute)", n is not None and "fire" in n[0], n)
    step("on planks it spreads and burns them away", num("fireSpread") > s0 and num("burned") > b0, (g()["fireSpread"], g()["burned"]))
    for dx in range(0, 5):
        for dz in range(1, 6):
            for dy in (0, 1): cmd(f"breakabs {fc[0] + dx} {y + dy} {fc[2] + dz}")

def gravel():
    # (147) gravel falling onto a rail breaks into an item
    fc, y = spot(232)
    put("rail", fc, 2, y, 3); b0 = num("brokeFalling"); put("gravel", fc, 2, y + 3, 3)
    time.sleep(2.5)
    step("gravel falling onto a rail breaks into an item", num("brokeFalling") > b0 and "gravel" in cmd("loose 6").lower(), cmd("loose 6")[:120])

def tntwater():
    # (148) TNT going off in water breaks no blocks (planks round it stay)
    fc, y = spot(233)
    y += 1  # (a block up)
    channel(fc, y, 3, 5)
    for dx in (1, 2, 3, 4):
        for z in (2, 4): put("oak_planks", fc, dx, y, z)
    put("tnt", fc, 3, y, 3)
    print("   poured:", pour(fc, 1, y, 3)); time.sleep(1.0)
    w0 = num("wet")
    cmd(f"igniteabs {fc[0] + 3} {y} {fc[2] + 3}"); time.sleep(0.3)
    cmd(f"tp {(fc[0] + 2.5) * S:.2f} {(y + 1) * S:.2f} {(fc[2] - 5) * S:.2f}"); time.sleep(3.0)
    walls = [blk(fc, dx, y, z) for dx in (2, 3, 4) for z in (2, 4)]
    step("lit TNT in water goes off without breaking the blocks round it", num("wet") > w0 and all(w is not None and w[0] == "oak_planks" for w in walls), (g()["wet"], g()["tnt"], walls))

def ice2():
    # (149) ice broken over a block turns to water; slime slows your walk
    fc, y = spot(234)
    put("stone", fc, 2, y, 3); put("ice", fc, 2, y + 1, 3)
    R.hold("iron_pickaxe", name="Iron Pickaxe"); near_cell(fc, 2, y + 1, 3); aim_cell(fc, 2, y + 1, 3, 0.5); cmd("mouse left 1.5"); time.sleep(1.0)
    w = blk(fc, 2, y + 1, 3)
    step("ice broken over a block leaves water", w is not None and w[0] == "water", w)
    cmd(f"breakabs {fc[0] + 2} {y + 1} {fc[2] + 3}")
    for dz in range(1, 9): put("stone", fc, 0, y, dz); put("slime", fc, 4, y, dz)  # (walkways a block up, clear of the ground)
    def walk(dx):
        cmd(f"tp {(fc[0] + dx + 0.5) * S:.2f} {(y + 1) * S + R.YO + 0.05:.2f} {(fc[2] + 1.5) * S:.2f}"); time.sleep(0.6)
        pilot.face((fc[0] + dx + 0.5) * S, (fc[2] + 9) * S); z0 = R.pos()[2]; cmd("keys W 1.0"); time.sleep(1.2); return (R.pos()[2] - z0) / S
    a, b = walk(0), walk(4)
    step("walking on slime is slower than on stone", b < a * 0.7, f"stone {a:.2f} slime {b:.2f} blocks")

def hot_index(state, key):
    h = re.search(r"hotbar=\[(.*?)\]", state).group(1).split(",")
    for i, x in enumerate(h):
        if x.split(":")[0] == key: return i
    return None

def cauldron():
    # (140) a patterned banner washed in a water cauldron loses its top pattern
    fc, y = spot(235)
    put("loom", fc, 1, y, 3); put("cauldron", fc, 3, y, 3)
    R.hold("water_bucket", name="Water Bucket"); near_cell(fc, 3, y, 3); aim_cell(fc, 3, y, 3, 0.8); cmd("mouse right 0.1"); time.sleep(0.8)
    print("   cauldron:", blk(fc, 3, y, 3))
    cmd("clearinv"); cmd("invgive white_banner 1"); cmd("invgive red_dye 1"); time.sleep(0.6)
    near_cell(fc, 1, y, 3); cmd(f"loomui open {fc[0] + 1} {y} {fc[2] + 3}"); time.sleep(0.4)
    for k in ("white_banner", "red_dye"):
        i = hot_index(cmd("loomui state"), k)
        if i is not None: cmd(f"loomui click hot {i} shift"); time.sleep(0.3)
    cmd("loomui click pick 1"); time.sleep(0.3); print("  ", cmd("loomui state")[:160])
    cmd("loomui click out 0 shift"); time.sleep(0.4); cmd("loomui close"); time.sleep(0.4)
    i = hot_index(cmd("chestui state"), "patterned_banner#1") or hot_index(cmd("chestui state").replace("#", ","), "patterned_banner")
    st = cmd("chestui state"); print("   inventory:", re.search(r"hotbar=\[(.*?)\]", st).group(1))
    h = re.search(r"hotbar=\[(.*?)\]", st).group(1).split(",")
    i = next((n for n, x in enumerate(h) if x.startswith("patterned_banner")), None)
    w0 = num("bannersWashed")
    if i is not None: cmd(f"slot {i}"); time.sleep(0.4); press_e(fc, 3, y, 3, 0.8); time.sleep(0.6)
    after = re.search(r"hotbar=\[(.*?)\]", cmd("chestui state")).group(1); print("   after:", after, blk(fc, 3, y, 3))
    aim_cell(fc, 3, y, 3, 0.8); shot("banner_washed")
    step("a patterned banner in a water cauldron: its top pattern washed off (a plain banner back, a third of the water)", num("bannersWashed") > w0 and "white_banner" in after, (g()["bannersWashed"], after))

def crafter2():
    # (136) a crafter making a cake keeps the empty buckets in its grid
    fc, y = spot(236)
    put("crafter", fc, 2, y, 3, 2)
    cmd("clearinv")
    for k, n in (("milk_bucket", 3), ("sugar", 2), ("egg", 1), ("wheat", 3)): cmd(f"invgive {k} {n}")
    time.sleep(0.6); near_cell(fc, 2, y, 3)
    cmd(f"chestui open {fc[0] + 2} {y} {fc[2] + 3}"); time.sleep(0.4)
    grid = ["milk_bucket"] * 3 + ["sugar", "egg", "sugar"] + ["wheat"] * 3
    for slot, k in enumerate(grid):
        i = hot_index(cmd("chestui state"), k)
        if i is None: continue
        cmd(f"chestui click hot {i}"); time.sleep(0.2); cmd(f"chestui click chest {slot} right"); time.sleep(0.2)
        if "cursor=-" not in cmd("chestui state"): cmd(f"chestui click hot {i}"); time.sleep(0.2)
    print("  ", cmd("chestui state")[:220]); cmd("chestui close"); time.sleep(0.3)
    put("redstone_block", fc, 1, y, 3); time.sleep(1.5)
    c = contents(fc, 2, y, 3); l = cmd("loose 8")
    shot("crafter_cake")
    step("the crafter makes a cake and keeps the three empty buckets", "cake" in l and c.count("bucket") >= 1 and "milk" not in c, (c[:120], l[:80]))

def brewing():
    # (134) hoppers feed a brewing stand (wart from above, blaze powder from the side); (130) a comparator reads it
    fc, y = spot(237)
    put("brewing_stand", fc, 2, y, 3); put("hopper", fc, 2, y + 1, 3, 0); put("hopper", fc, 1, y, 3, 5); put("comparator", fc, 3, y, 3, 5); put("redstone_lamp", fc, 4, y, 3)
    into(fc, 2, y + 1, 3, "nether_wart", 1, "Nether Wart"); into(fc, 1, y, 3, "blaze_powder", 1, "Blaze Powder")
    time.sleep(2.0)
    b = contents(fc, 2, y, 3)
    step("hoppers put the wart in from above and the blaze powder in from the side", "nether_wart" in b and ("powder=" in b and "powder=0" not in b or "fuel=" in b and "fuel=0" not in b), b[:160])
    step("a comparator reads the brewing stand (its lamp lights)", lit(fc, 4, y, 3), (blk(fc, 3, y, 3), b[:80]))
    aim_cell(fc, 2, y, 3, 0.5); shot("brewing_hoppers")

def rain():
    # (146) rain puts out fire under the open sky; (140) a cauldron under the sky fills in the rain. Lands again, raining.
    if "inShipPhase=False" in cmd("state"):
        cmd("tpship"); time.sleep(1); cmd("leave"); R.wait(lambda: "inShipPhase=True" in cmd("state"), 120); time.sleep(2)
    cmd("deadline"); cmd("route 0"); print("  ", cmd("weather Rainy"))
    for _ in range(4):
        time.sleep(8); R.wait(lambda: not cmd("land").startswith("not ready"), 60)
        if R.wait(lambda: "landed=True" in cmd("leave_check"), 90): break
    time.sleep(3); cmd("mobspawns 0"); cmd("dayfreeze 1")
    fc, y = spot(238)
    for dx in (0, 2, 4): put("netherrack", fc, dx, y, 3) if False else put("oak_planks", fc, dx, y, 3)
    for dx in (0, 2, 4): put("fire", fc, dx, y + 1, 3)
    put("cauldron", fc, 2, y, 6)
    r0 = num("fireRained"); f0 = num("rainFills"); cmd("rainfill 0.5")
    time.sleep(8.0)
    fires = [blk(fc, dx, y + 1, 3) for dx in (0, 2, 4)]
    near_cell(fc, 2, y, 6); aim_cell(fc, 2, y, 6, 0.8); shot("rain_cauldron")
    step("rain puts out fire under the open sky", num("fireRained") > r0, (g()["fireRained"], fires))
    step("a cauldron under the sky fills in the rain", num("rainFills") > f0 and (power(fc, 2, y, 6) or 0) > 0, (g()["rainFills"], blk(fc, 2, y, 6)))
    cmd("rainfill -1")

def dispenser3():
    # (135) a dispenser's shears shear the sheep in front; a carved pumpkin onto two snow blocks makes a snow golem
    fc, y = spot(239)
    put("dispenser", fc, 1, y, 3, 2)
    into(fc, 1, y, 3, "shears", 1, "Shears")
    cmd("enemy sheep 3"); time.sleep(0.8)
    cmd(f"enemypin sheep {(fc[0] + 1.5) * S:.2f} {y * S + R.YO:.2f} {(fc[2] + 4.5) * S:.2f}"); time.sleep(1.0)
    put("redstone_block", fc, 0, y, 3); time.sleep(1.5)
    l = cmd("loose 10")
    step("a dispenser's shears shear the sheep in front (wool drops)", "wool" in l, (l[:100], cmd("animals")[:100]))
    cmd("enemypin sheep off"); cmd("clearenemies 300 all")
    put("snow_block", fc, 3, y, 7); put("snow_block", fc, 3, y + 1, 7); put("dispenser", fc, 3, y + 2, 6, 2)
    into(fc, 3, y + 2, 6, "carved_pumpkin", 1, "Carved Pumpkin")
    put("redstone_block", fc, 4, y + 2, 6); time.sleep(2.0)
    e = cmd("enemies")
    aim_cell(fc, 3, y + 1, 7, 0.5); shot("dispenser_golem")
    step("a dispenser's carved pumpkin on two snow blocks makes a snow golem", "Snow Golem" in e, e[:120])

SECTIONS = {"dust": dust, "lock": lock, "ore": ore, "note": note, "dispenser": dispenser, "crafter": crafter, "rails": rails, "chest": chest,
            "door": door, "bed": bed, "farm": farm, "water": water, "fire": fire, "sand": sand, "tnt": tnt, "ice": ice, "honey": honey, "lightning": lightning,
            "comp": comp, "strong": strong, "notes": notes, "arrows": arrows, "pot": pot, "hoppers": hoppers,
            "dispenser2": dispenser2, "rails2": rails2, "furnace": furnace, "gate": gate, "farm2": farm2,
            "current": current, "lava": lava, "fire2": fire2, "gravel": gravel, "tntwater": tntwater, "ice2": ice2,
            "cauldron": cauldron, "crafter2": crafter2, "brewing": brewing, "dispenser3": dispenser3}
LAST = {"rain": rain}  # (asked for by name: it lands again, in the rain)

if __name__ == "__main__":
    t0 = time.time()
    if "inShipPhase=True" in cmd("state"): print("land:", R.land(0)); time.sleep(3)
    cmd("mobspawns 0"); cmd("dayfreeze 1")
    for s in sys.argv[1:] or list(SECTIONS):
        print("==", s, flush=True)
        try: {**SECTIONS, **LAST}[s]()
        except Exception as e: import traceback; traceback.print_exc(); step(f"{s}: ran to the end", False, repr(e))
    bad = [n for n, ok in log if not ok]
    print(f"\n{len(log) - len(bad)}/{len(log)} ok in {time.time() - t0:.0f} s")
    for n in bad: print("  BAD:", n)
