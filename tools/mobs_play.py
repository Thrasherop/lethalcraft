"""#244 part F hand play: the mobs, as a player meets them (real swings, [E], right-clicks; live mobs).
Screenshots in shots/mf_*.png.  usage: py tools/mobs_play.py <section> [...]
sections: cooked health equip sheep wolf golem drowned creeper blaze water endermite infight breed"""
import sys, os, time, re, math
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from regress import cmd, S
import thorough_play as T
import pilot
HERE = os.path.dirname(os.path.abspath(__file__))
log = []
def shot(n): cmd(f"screenshot {os.path.join(HERE, '..', 'shots', 'mf_' + n + '.png')}"); time.sleep(0.5)
def step(name, ok, detail=""):
    log.append((name, ok)); print(("OK  " if ok else "BAD ") + name + (f"  ({detail})" if detail else ""), flush=True)
def f244(): return dict(kv.split("=", 1) for kv in cmd("f244").split(" ") if "=" in kv)
def select(name): pilot.select(name); time.sleep(0.3)
def give(*kv): T.give(*kv)
def mobs(name):
    out = []
    for x in cmd("enemies").split(" ; "):
        x = x.strip()
        if not x.lower().startswith(name.lower() + "@") or "dead=False" not in x: continue
        m = re.search(r"@([-\d.]+),([-\d.]+),([-\d.]+) d=([\d.]+).*?hp=(-?\d+)", x)
        lift = re.search(r"lift=([-\d.]+)", x)
        if m: out.append(dict(x=float(m.group(1)), y=float(m.group(2)), z=float(m.group(3)), d=float(m.group(4)), hp=int(m.group(5)), lift=float(lift.group(1)) if lift else 0, raw=x))
    return sorted(out, key=lambda m: m["d"])
def near(name):
    m = mobs(name); return m[0] if m else None
def spawn(name, dist=3, hold=True):
    cmd("look 0 0"); time.sleep(0.2); print("  ", cmd(f"enemy {name} {dist}")); time.sleep(1.2)
    if hold: cmd(f"enemyhold {name.split()[0]} {dist}"); time.sleep(0.4)  # (the hold takes one word of the name)
    return near(name)
def aim_at(m, up=0.6): pilot.aim_at(m["x"], m["y"] + up, m["z"]); time.sleep(0.3)
def press_e(m, up=0.6): aim_at(m, up); cmd("keys E 0.1"); time.sleep(0.8)
def swing(m, n=1, gap=0.8):
    for _ in range(n): aim_at(m); cmd("mouse left 0.15"); time.sleep(gap)
def loose(r=8): return cmd(f"loose {r}")
def clear(): cmd("clearenemies 300 all"); cmd("clearloose 12"); time.sleep(0.5)
def spot(i):
    R.start_flat(i, [(dx, dz) for dx in range(-2, 3) for dz in range(0, 5)]); cmd("god 1"); cmd("gamemode survival"); cmd("hunger 20 5"); clear()

def cooked():
    # (96) a cow set on fire by a Fire Aspect sword, then killed while burning: steak, not raw beef
    spot(150)
    give(("iron_sword", 1)); select("Iron Sword"); print("  ", cmd("enchant FireAspect 2"))
    c = spawn("cow", 2.2)
    c0 = int(f244()["cooked"])
    swing(c, 1); time.sleep(0.6); cmd("enemyrelease cow"); time.sleep(0.3)
    c = near("cow")
    if c: swing(c, 2)
    for i in range(12):
        if near("cow") is None: break
        time.sleep(0.5)
    time.sleep(1.0); l = loose(20); print("   ", l)  # (it runs off burning: it dies some way off)
    step("a cow killed burning (Fire Aspect) drops steak, not raw beef", "steak" in l and "raw_beef" not in l and int(f244()["cooked"]) > c0, l[:120])
    shot("cooked")

def health():
    # (100) Minecraft's health over 5: creeper 4, spider 3, wolf 2 (8 tamed), ghast 2, snow golem 1, brute 10, horse 3-6
    spot(151)
    want = [("creeper", 4), ("spider", 3), ("wolf", 2), ("snow golem", 1)]
    for name, hp in want:
        m = spawn(name.replace(" ", "_") if False else name, 4, hold=False)
        m = near(name)
        step(f"a {name}: {hp} health", m is not None and m["hp"] == hp, m["raw"][:90] if m else cmd("enemies")[:120])
        clear()
    w = spawn("wolf", 3); print("  ", cmd("pet tame wolf")); time.sleep(0.8); w = near("wolf")
    step("a tamed wolf: 8 health", w and w["hp"] == 8, w["raw"][:90] if w else "-")
    clear()
    hps = []
    for i in range(4):
        cmd("enemy horse 4"); time.sleep(1.0)
    time.sleep(0.5); hps = [m["hp"] for m in mobs("horse")]
    step("horses: each its own health, 3-6", hps and all(3 <= h <= 6 for h in hps), hps)

def equip():
    # (98) skeletons killed by a player now and then drop their bow, worn; 1-3 more XP for it
    spot(152)
    give(("netherite_sword", 1)); select("Netherite Sword")
    e0 = int(f244()["equipDropped"]); x0 = T.st("xp"); kills = 0
    for i in range(40):
        s = spawn("skeleton", 2.2)
        if not s: continue
        swing(s, 3, 0.7); kills += 1
        cmd("enemyrelease skeleton")
    time.sleep(1.5); l = loose(12); e1 = int(f244()["equipDropped"])
    print("   kills", kills, "bows dropped", e1 - e0, "xp", x0, "->", T.st("xp"), l[:200])
    step(f"{kills} skeletons killed: a few drop their bow (8.5%: about {kills * 0.085:.0f})", 0 < e1 - e0 <= max(8, kills // 4) and "bow" in l, (e1 - e0, l[:120]))
    shot("equip")

def sheep():
    # (102) red dye on a sheep ([E]): red; a red and a yellow sheep bred: an orange lamb
    spot(153)
    give(("red_dye", 1), ("yellow_dye", 1), ("wheat", 2))
    a = spawn("sheep", 2.5); select("Red Dye"); press_e(a)
    st = [x for x in cmd("care").split(" ; ") if "Sheep@" in x][:2]; print("   ", st)
    d1 = int(f244()["sheepDyed"])
    step("[E] with red dye: the sheep is red", d1 >= 1 and "Red Dye" not in T.slots(), (d1, T.slots()))
    cmd("enemyrelease sheep"); time.sleep(0.3)
    cmd("look 90 0"); time.sleep(0.2); cmd("enemy sheep 3"); time.sleep(1.2)
    # (the white one: the sheep that isn't red)
    w = next((m for m in mobs("sheep") if "wool=white" in next((x for x in cmd("care").split(" ; ") if f"Sheep@{m['x']:.2f}" in x), "")), None) or near("sheep")
    select("Yellow Dye"); press_e(w)
    print("   ", [x for x in cmd("care").split(" ; ") if "Sheep@" in x])
    shot("sheep_dyed")
    born0 = int(f244()["born"])
    select("Wheat")
    for m in mobs("sheep")[:2]: press_e(m); time.sleep(0.5)
    time.sleep(4.0)
    print("   ", cmd("care")[:300])
    lamb = [x for x in cmd("care").split(" ; ") if "Lamb@" in x or "baby=True" in x and "Sheep" in x]
    step("the two fed wheat: a lamb", int(f244()["born"]) > born0, lamb[:1])
    par = [re.search(r"wool=(\w+)", x).group(1) for x in cmd("care").split(" ; ") if "Sheep@" in x and "baby=False" in x][:2]
    mix = {frozenset(("red", "yellow")): "orange", frozenset(("red", "white")): "pink", frozenset(("blue", "white")): "light_blue"}.get(frozenset(par))
    step(f"the lamb's colour from its parents ({'+'.join(par)}: {mix or 'one of theirs'})", any(("wool=" + mix) in x if mix else any(("wool=" + c) in x for c in par) for x in lamb), lamb[:1])
    shot("lamb")

def wolf():
    # (103) hit a wild wolf: angry (its face), the wolves by it join; tamed: 8 health, meat heals; never a creeper
    spot(154)
    give(("wooden_sword", 1)); select("Wooden Sword")  # (no punching here: a light sword's hit)
    w = spawn("wolf", 3, hold=False); cmd("enemy wolf 4"); time.sleep(1.0)
    j0 = int(f244()["wolvesJoined"]); w = near("wolf")
    swing(w, 1); time.sleep(1.5)
    st = [m["raw"] for m in mobs("wolf")]
    print("   ", [re.search(r"state=(\d)", s).group(1) for s in st])
    step("hit, it's angry (it comes for me) and the other wolf joins", int(f244()["wolvesJoined"]) > j0 and all("state=1" in s for s in st), st[:2])
    shot("wolf_angry")
    clear()
    w = spawn("wolf", 2.5); cmd("pet tame wolf"); time.sleep(0.8)
    cmd("hitenemy wolf 3"); time.sleep(0.5); h0 = near("wolf")["hp"]
    give(("steak", 2)); select("Steak"); w = near("wolf"); press_e(w); time.sleep(0.5)
    h1 = near("wolf")["hp"]
    step("a hurt tamed wolf fed steak heals", h1 > h0, (h0, h1, f244()["wolfHealed"]))
    shot("wolf_tame_tail")

def golem():
    # (109) an iron golem hurt: cracks; an iron ingot ([E]) mends it a quarter
    spot(155)
    g = spawn("iron golem", 3.5)
    cmd("hitenemy iron golem 12"); time.sleep(0.8); g = near("iron golem")
    print("   ", g["raw"][:120] if g else "-")
    aim_at(g, 1.5); time.sleep(0.5); shot("golem_cracked")
    give(("iron_ingot", 2)); select("Iron Ingot"); h0 = g["hp"]
    press_e(g, 1.2); time.sleep(0.5); h1 = near("iron golem")["hp"]
    step("an iron ingot on the hurt golem: 5 back (a quarter)", h1 == min(20, h0 + 5), (h0, h1))
    shot("golem_mended")
    cmd("enemyrelease iron golem")

def creeper():
    # (114) flint and steel on a creeper ([E]): it swells and goes off
    spot(156)
    c = spawn("creeper", 3.5)
    give(("flint_and_steel", 1)); select("Flint and Steel")
    l0 = int(f244()["creepersLit"]); press_e(c, 0.8)
    step("[E] with flint and steel: the creeper is lit", int(f244()["creepersLit"]) > l0, f244()["creepersLit"])
    cmd("enemyrelease creeper"); time.sleep(2.5)  # (the dev hold keeps it from going off)
    step("it goes off", near("creeper") is None, cmd("enemies")[:100])

def blaze():
    # (117) a blaze rises to my height when I'm up on a pillar
    cmd("nether gen"); time.sleep(2.0); cmd("nether tp"); time.sleep(2.0); cmd("god 1"); cmd("clearenemies 300 all")
    p = re.search(r"pos=([-\d.]+),([-\d.]+),([-\d.]+)", cmd("state")); x, y, z = map(float, p.groups())
    cmd("look 0 0"); print("  ", cmd("enemy blaze 6")); time.sleep(2.0)
    b0 = near("blaze"); print("   ", b0["raw"][:140] if b0 else cmd("enemies")[:100])
    gx, gy, gz = int(x // S), int((y - R.YO) // S), int(z // S)
    for k in range(4): cmd(f"placeabs netherrack {gx} {gy + k} {gz} 1")
    cmd(f"tp {x:.2f} {y + 4.2 * S:.2f} {z:.2f}"); time.sleep(6.0)
    b1 = near("blaze"); print("   ", b1["raw"][:140] if b1 else "-")
    step("it rose toward me (its lift up from half a block)", b0 and b1 and b1["lift"] > b0["lift"] + 0.5, (b0["lift"] if b0 else None, b1["lift"] if b1 else None))
    shot("blaze_up")

def water():
    # (blaze, snow golem, enderman) water hurts them: a snow golem standing in water loses health
    spot(158)
    g = spawn("snow golem", 3)
    gx, gy, gz = int(g["x"] // S), int((g["y"] - R.YO) // S + 0.5), int(g["z"] // S)
    for dx in range(-2, 3):
        for dz in range(-2, 3): cmd(f"placeabs water {gx + dx} {gy} {gz + dz} 1")
    time.sleep(1.0)
    cmd("enemyrelease snow"); time.sleep(0.3)  # (held, a mob's own per-frame logic doesn't run)
    w0 = int(f244()["waterHurt"]); time.sleep(6.0); w1 = int(f244()["waterHurt"])
    step("a snow golem in water is hurt by it", w1 > w0, (w0, w1, near("snow golem")))
    shot("water_golem")
    cmd("enemyrelease snow")

def endermite():
    # (123) an endermite is gone after two minutes
    spot(159)
    cmd("enemy endermite 4"); time.sleep(1.0); e0 = near("endermite")
    cmd("speed 6"); time.sleep(22); cmd("speed 1"); time.sleep(0.5)
    step("an endermite, two minutes on: gone", e0 is not None and near("endermite") is None and int(f244()["endermitesGone"]) >= 1, (e0 is not None, f244()["endermitesGone"]))

def infight():
    # (97) inside, a skeleton shooting at me hits a zombie between us: the zombie goes for the skeleton
    # (dev-spawned, these are the facility's monsters: they only go for a player inside, as the game's own rule has it)
    for node in range(0, 60, 3):
        if not cmd(f"tpnode {node}").startswith("ok"): break
        time.sleep(0.8); clear(); cmd("look 0 0")
        cmd("enemy skeleton 8"); time.sleep(0.6)
        if "blocked=-" not in cmd("enemyhold skeleton 8"): continue
        cmd("enemy zombie 4"); time.sleep(0.6)
        if "blocked=-" not in cmd("enemyhold zombie 4"): continue
        break
    i0 = int(f244()["infights"]); h0 = int(f244()["mobArrowHits"])
    cmd("enemyrelease skeleton")
    for _ in range(20):
        time.sleep(0.5)
        if int(f244()["mobArrowHits"]) > h0: break
    print("   ", f244())
    step("the skeleton's arrows hit the zombie in their way", int(f244()["mobArrowHits"]) > h0, f244()["mobArrowHits"])
    step("the zombie goes for the skeleton", int(f244()["infights"]) > i0, f244()["infights"])
    shot("infight")
    cmd("enemyrelease zombie"); time.sleep(3.0); shot("infight_after")
    clear()

def breed():
    # (99) a pig fed a potato is in love; a baby fed grows up sooner; a baby keeps by a grown-up; a spawn egg on a cow: a calf
    spot(161)
    give(("potato", 2))
    a = spawn("pig", 2.5); select("Potato"); press_e(a); cmd("enemyrelease pig")
    cmd("look 90 0"); b = spawn("pig", 2.5); press_e(b); cmd("enemyrelease pig")
    time.sleep(4.0)
    step("two pigs fed potatoes: a piglet", len([x for x in cmd("care").split(" ; ") if "Piglet@" in x or ("Pig" in x and "baby=True" in x)]) >= 1 or int(f244()["born"]) > 0, cmd("care")[:200])
    give(("wheat", 1))
    # (a cow's spawn egg from the creative menu)
    cmd("gamemode creative"); time.sleep(0.4); cmd("creativeui open"); time.sleep(0.8)
    for t in range(14):
        cmd(f"creativeui click tabs {t}"); time.sleep(0.2)
        if R.creative_click("egg:Cow"): break
    time.sleep(0.5); cmd("creativeui close"); time.sleep(0.3); cmd("gamemode survival"); time.sleep(0.5)
    c = spawn("cow", 2.5)
    print("   egg:", T.slots())
    select("Spawn Egg"); e0 = int(f244()["babiesFromEggs"]); press_e(c)
    step("a cow spawn egg on a cow ([E]): a calf", int(f244()["babiesFromEggs"]) > e0, (f244()["babiesFromEggs"], T.slots()))
    cmd("enemyrelease cow")
    calf = near("calf")
    if calf:
        select("Wheat"); f0 = int(f244()["babiesFed"]); press_e(calf, 0.4)
        step("wheat for the calf: it grows up sooner", int(f244()["babiesFed"]) > f0, f244()["babiesFed"])
    shot("breed")

def inside_spot():
    for node in range(0, 60, 3):
        if cmd(f"tpnode {node}").startswith("ok"): time.sleep(0.8); return True
    return False

def spider():
    # (113) inside, a spider coming at me leaps from a few blocks
    inside_spot(); clear(); cmd("look 0 0")
    l0 = int(f244()["leaps"])
    cmd("enemy spider 8"); time.sleep(0.8)
    for _ in range(16):
        time.sleep(0.4)
        if int(f244()["leaps"]) > l0: break
    shot("spider_leap")
    step("a spider coming for me leaps from 2-4 blocks", int(f244()["leaps"]) > l0, f244()["leaps"])
    clear()

def skeleton():
    # (112) a skeleton shooting at me strafes sideways; with a wolf by it, it runs from the wolf
    inside_spot(); clear(); cmd("look 0 0")
    s0 = int(f244()["strafes"])
    cmd("enemy skeleton 9"); time.sleep(1.0)
    p0 = near("skeleton"); time.sleep(5.0); p1 = near("skeleton")
    moved = p0 and p1 and math.hypot(p1["x"] - p0["x"], p1["z"] - p0["z"])
    step("shooting at me, it strafes (moves about, not just standing)", int(f244()["strafes"]) > s0 and moved and moved > 0.5, (f244()["strafes"], moved))
    f0 = int(f244()["fled"])
    sk = near("skeleton")
    cmd("enemy wolf 6"); time.sleep(3.0)
    step("a wolf near it: it keeps away from it", int(f244()["fled"]) > f0, f244()["fled"])
    clear()

def enderman():
    # (115) an arrow at an enderman: it teleports, the arrow goes by; its anger goes after a while
    inside_spot(); clear(); cmd("look 0 0")
    give(("bow", 1), ("arrow", 8)); select("Bow")
    cmd("enemy enderman 7"); time.sleep(1.0); cmd("enemyhold enderman 7"); time.sleep(0.4)
    e = near("enderman"); aim_at(e, 1.6); d0 = int(f244()["dodged"])
    cmd("mouse right 1.3"); time.sleep(1.5)
    step("shot with an arrow, the enderman teleports away (the arrow misses it)", int(f244()["dodged"]) > d0, (f244()["dodged"], near("enderman")["hp"] if near("enderman") else "-"))
    clear()

def ghast():
    # (118) a ghast charging a shot shows its shooting face
    cmd("nether gen"); time.sleep(2.0); cmd("nether tp"); time.sleep(2.0); cmd("god 1"); clear()
    cmd("look 0 -10"); print("  ", cmd("nethermob ghast 14")); time.sleep(1.0)
    for i in range(12):
        time.sleep(0.25)
        g = near("ghast")
        if g: aim_at(g, 2.0)
        shot(f"ghast_{i}")
    step("a ghast (screenshots mf_ghast_*: its face opens as it charges)", near("ghast") is not None, near("ghast")["raw"][:80] if near("ghast") else "-")

def magma():
    # (120) a big magma cube hops higher than a big slime
    cmd("nether gen"); time.sleep(2.0); cmd("nether tp"); time.sleep(2.0); cmd("god 1"); clear(); cmd("look 0 0")
    print("  ", cmd("nethermob magma4 6")); time.sleep(0.6); shot("magma_a"); time.sleep(0.3); shot("magma_b"); time.sleep(0.3); shot("magma_c")
    step("a big magma cube (screenshots mf_magma_*: its hops)", near("magma") is not None or "Magma" in cmd("enemies"), cmd("enemies")[:100])
    clear()

def rabbit():
    # (121) a rabbit near ripe carrots eats them down a stage
    fc = R.start_flat(162, [(dx, dz) for dx in range(-2, 3) for dz in range(0, 5)]); cmd("god 1"); clear()
    sy = max(R.surface(fc, dx, dz) for dx in range(-2, 3) for dz in range(0, 5)) + 1
    for dx in range(-1, 2):
        for dz in range(2, 4):
            cmd(f"placeabs farmland {fc[0] + dx} {sy} {fc[2] + dz}"); cmd(f"placeabs carrots {fc[0] + dx} {sy + 1} {fc[2] + dz}"); cmd(f"setstate {fc[0] + dx} {sy + 1} {fc[2] + dz} 7")
    time.sleep(0.8); r0 = int(f244()["raids"])
    cmd("enemy rabbit 3"); time.sleep(0.5)
    for _ in range(30):
        time.sleep(1.0)
        if int(f244()["raids"]) > r0: break
    shot("rabbit_raid")
    step("a rabbit eats a ripe carrot down a stage", int(f244()["raids"]) > r0, f244()["raids"])

def zpiglin():
    # (124) a baby zombified piglin, and one on a chicken
    cmd("nether gen"); time.sleep(2.0); cmd("nether tp"); time.sleep(2.0); cmd("god 1"); clear(); cmd("look 0 0")
    print("  ", cmd("enemy baby zombified piglin 4")); time.sleep(1.0)
    b = near("baby zombified piglin")
    if b: aim_at(b, 0.4)
    shot("zpiglin_baby")
    step("a baby zombified piglin", b is not None, cmd("enemies")[:120])
    clear(); print("  ", cmd("enemy chicken jockey 4")); time.sleep(1.0); shot("zpiglin_jockey")

def hunt():
    # (103) a wild wolf goes for a skeleton near it
    spot(163)
    cmd("enemy wolf 3"); time.sleep(0.8); cmd("enemy skeleton 6"); time.sleep(0.5)
    h0 = int(f244()["wolfHunts"])
    for _ in range(12):
        time.sleep(1.0)
        if int(f244()["wolfHunts"]) > h0: break
    step("a wild wolf goes for a skeleton", int(f244()["wolfHunts"]) > h0, f244()["wolfHunts"])
    shot("wolf_hunt"); clear()

SECTIONS = {"cooked": cooked, "health": health, "equip": equip, "sheep": sheep, "wolf": wolf, "golem": golem, "creeper": creeper,
            "blaze": blaze, "water": water, "endermite": endermite, "infight": infight, "breed": breed, "spider": spider, "skeleton": skeleton, "enderman": enderman, "ghast": ghast,
            "magma": magma, "rabbit": rabbit, "zpiglin": zpiglin, "hunt": hunt}

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
