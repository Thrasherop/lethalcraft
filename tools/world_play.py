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
    aim_cell(fc, 1, y - 1, 3, 0.98); cmd("mouse right 0.1"); time.sleep(0.8)
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
    for dx in range(0, 5):
        put("stone", fc, dx, y, 2); put("stone", fc, dx, y, 4)            # (its walls)
        if dx < 4 and blk(fc, dx, y - 1, 3) is None: put("stone", fc, dx, y - 1, 3)  # (its floor)
    put("stone", fc, 0, y, 3)                                              # (the dead end)
    R.dig(fc, 4, y - 1, 3)                                                 # (the hole)
    put("water", fc, 2, y, 3)
    time.sleep(3.0)
    print("  ", [blk(fc, dx, y, 3) for dx in range(0, 5)], [blk(fc, dx, y - 1, 3) for dx in range(0, 5)])
    west, east = blk(fc, 1, y, 3), blk(fc, 3, y, 3)
    R.stand_on(fc, 1, y + 1, 2); aim_cell(fc, 3, y, 3, 0.1); shot("water_drop")  # (on its wall, looking along it)
    step("water runs toward the drop, not the other way", east is not None and east[0] == "water" and (west is None or west[0] != "water"), (west, east))

def fire():
    # (146) fire on soul sand is soul fire
    fc, y = spot(212)
    put("soul_sand", fc, 2, y, 3)
    R.hold("flint_and_steel", name="Flint and Steel")
    near_cell(fc, 2, y, 3); aim_cell(fc, 2, y, 3, 0.98); cmd("mouse right 0.1"); time.sleep(1.0)
    f = blk(fc, 2, y + 1, 3)
    if f: aim_cell(fc, 2, y + 1, 3, 0.3)
    shot("soul_fire")
    step("fire on soul sand is soul fire", f is not None and f[0] == "soul_fire", f)

def sand():
    # (147) sand falling onto a torch breaks into its item
    fc, y = spot(213)
    put("torch", fc, 2, y, 3); put("sand", fc, 2, y + 3, 3)
    b0 = num("brokeFalling"); time.sleep(2.5)
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
    for dz in range(2, 11): put("packed_ice", fc, 2, y - 1, dz) if True else None
    cmd(f"tp {(fc[0] + 2.5) * S:.2f} {y * S + 0.05:.2f} {(fc[2] + 2.5) * S:.2f}"); time.sleep(0.6)
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
    put("exposed_copper", fc, 2, y, 3); put("lightning_rod", fc, 2, y + 1, 3, 1)
    s0 = num("scrapes")
    print("  ", cmd("lightning 30")); time.sleep(2.0)
    c = blk(fc, 2, y, 3)
    step("a strike on the rod scrapes the copper under it", num("scrapes") > s0 and c is not None and c[0] == "copper_block", c)

SECTIONS = {"dust": dust, "lock": lock, "ore": ore, "note": note, "dispenser": dispenser, "crafter": crafter, "rails": rails, "chest": chest,
            "door": door, "bed": bed, "farm": farm, "water": water, "fire": fire, "sand": sand, "tnt": tnt, "ice": ice, "honey": honey, "lightning": lightning}

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
