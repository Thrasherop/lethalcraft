"""#244 part F hand play: the mobs, as a player meets them (real swings, [E], right-clicks; live mobs).
Screenshots in shots/mf_*.png.  usage: py tools/mobs_play.py <section> [...]
sections: cooked health equip sheep wolf golem drowned creeper blaze water endermite infight breed spider skeleton enderman
ghast magma rabbit zpiglin hunt; second half: snowgolem drown golemtoss piglin cat parrot horse2 llama trader"""
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

def in_sight(name, dist):
    """inside: a node where `name` stands in plain sight `dist` m in front of me, held there"""
    for node in range(0, 60, 3):
        if not cmd(f"tpnode {node}").startswith("ok"): break
        time.sleep(0.8); clear(); cmd("look 0 0")
        cmd(f"enemy {name} {dist}"); time.sleep(0.6)
        if not mobs(name): continue
        if "blocked=-" in cmd(f"enemyhold {name.split()[0]} {dist}"): return near(name)
    return None

def spider():
    # (113) inside, a spider coming at me leaps from a few blocks
    in_sight("spider", 7); l0 = int(f244()["leaps"]); cmd("enemyrelease spider")
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
    # (115) an arrow at an enderman: it teleports, the arrow goes by (on open ground; released: a held mob can't teleport)
    spot(164); give(("bow", 1), ("arrow", 8)); select("Bow")
    e = spawn("enderman", 5); cmd("enemyrelease enderman"); time.sleep(1.0)
    d0 = int(f244()["dodged"])
    for up in (1.6, 2.4, 2.0):
        e = near("enderman"); aim_at(e, up); cmd("mouse right 1.3"); time.sleep(2.0)
        if int(f244()["dodged"]) > d0: break
    e2 = near("enderman")
    step("shot with an arrow, the enderman teleports away unhurt", int(f244()["dodged"]) > d0 and e2 and e2["hp"] == 8, (f244()["dodged"], e2["hp"] if e2 else "-"))
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
    for dx in range(-1, 2):
        for dz in range(2, 4):
            sy = R.surface(fc, dx, dz)  # (farmland in the ground, as a farm is)
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

# ------------------------------------------------------------------ part F, second half
def walk_up(line, dist=1.8):
    """step up to a mob (from its f244m/enemies line): beside it, facing it; its position back"""
    m = re.search(r"@([-\d.]+),([-\d.]+),([-\d.]+)", line)
    x, y, z = float(m.group(1)), float(m.group(2)), float(m.group(3))
    cmd(f"tp {x:.2f} {y + 0.1:.2f} {z - dist:.2f}"); time.sleep(0.5)
    return dict(x=x, y=y, z=z)
def lines_of(prefix, extra=""): return [x for x in fm(40).split(" ; ") if x.startswith(prefix) and extra in x]
def f244b(): return dict(kv.split("=", 1) for kv in cmd("f244b").replace(": ", " ").split(" ") if "=" in kv)
def fm(r=40): return cmd(f"f244m {r}")
def fbs(section, key):
    """a counter of one section of f244b ("piglin: ate=1 ... horse: ... ate=2": several share a name)"""
    t = cmd("f244b"); i = t.find(section + ":")
    m = re.search(r"(?<![A-Za-z])" + key + r"=(\d+)", t[i:] if i >= 0 else t)
    return int(m.group(1)) if m else -1
def mob_line(name, r=40):
    for x in fm(r).split(" ; "):
        if x.lower().startswith(name.lower() + "#"): return x
    return None
def hold_item(key, n=1, name=None): return R.hold(key, n, name=name)
def wait_for(f, most, step_s=0.5):
    t = time.time()
    while time.time() - t < most:
        if f(): return True
        time.sleep(step_s)
    return f()

def snowgolem():
    # (110) shears take its pumpkin (its snow head shows); on a snowy moon it leaves snow; on a hot one it melts
    spot(170)
    g = spawn("snow golem", 2.5)
    s0 = int(f244b()["sheared"])
    if hold_item("shears", name="Shears"): press_e(g, 1.4)
    time.sleep(1.0); l = loose(6)
    step("shears take a snow golem's pumpkin: a carved pumpkin drops", int(f244b()["sheared"]) > s0 and "carved_pumpkin" in l.lower().replace(" ", "_"), l[:120])
    aim_at(g, 1.4); shot("snowgolem_sheared")
    step("its own snow head shows (f244m)", "pumpkin=False" in (mob_line("snow golem") or ""), mob_line("snow golem"))
    clear()
    print("  ", cmd("variants snowy")); n0 = int(f244b()["snowLaid"])
    g = spawn("snow golem", 3, hold=False)
    wait_for(lambda: int(f244b()["snowLaid"]) > n0, 20)
    step("on a snowy moon it leaves snow where it walks", int(f244b()["snowLaid"]) > n0, f244b()["snowLaid"])
    shot("snowgolem_trail"); clear()
    print("  ", cmd("variants desert")); m0 = int(f244b()["melted"])
    g = spawn("snow golem", 3, hold=False)
    wait_for(lambda: int(f244b()["melted"]) > m0, 8)
    step("on a hot moon it melts (hurts)", int(f244b()["melted"]) > m0, f244b()["melted"])
    print("  ", cmd("variants moon")); clear()

def drown():
    # (111) a zombie with its head under water 30 s: a drowned. A glass tube round a zombie on the ground, filled with water
    spot(171)
    fc = R.feet_cell(); sy = fc[1]
    cx, cz = 2, 3
    for (dx, dz) in ((cx - 1, cz), (cx + 1, cz), (cx, cz - 1), (cx, cz + 1)):
        for dy in range(0, 4): R.place("glass", fc, dx, sy + dy, dz)
    time.sleep(1.0)
    c0 = int(f244b()["drowned"])
    cmd("enemy zombie 3"); time.sleep(1.0)
    # (the tube's middle, from where its glass is: the four lowest)
    pts = [tuple(map(float, m.groups())) for m in re.finditer(r"@([-\d.]+),([-\d.]+),([-\d.]+)", cmd("where glass 20"))]
    low = sorted(pts, key=lambda q: q[1])[:4]
    mx, my, mz = sum(q[0] for q in low) / 4, low[0][1], sum(q[2] for q in low) / 4
    print("  ", cmd(f"enemypin zombie {mx:.2f} {my - 0.5 * S:.2f} {mz:.2f}")); time.sleep(0.5)  # (held where it stands in the tube, its own logic running)
    for dy in range(0, 3): R.place("water", fc, cx, sy + dy, cz)
    time.sleep(1.0)
    z = near("zombie"); print("   ", z["raw"][:120] if z else "-")
    if z: aim_at(z, 1.0)
    shot("drown_under")
    ok = wait_for(lambda: int(f244b()["drowned"]) > c0, 45, 1.0)
    step("a zombie 30 s under water becomes a drowned", ok and near("drowned") is not None, cmd("enemies")[:160])
    d = near("drowned")
    if d: aim_at(d, 1.0)
    shot("drown_drowned"); clear()

def golemtoss():
    # (109) what an iron golem hits goes up
    spot(172)
    t0 = int(f244b()["tossed"])
    cmd("enemy iron golem 3"); time.sleep(0.8); cmd("enemy zombie 5"); time.sleep(0.5)
    ok = wait_for(lambda: int(f244b()["tossed"]) > t0, 20)
    step("an iron golem's blow throws a zombie up", ok, f244b()["tossed"])
    shot("golem_toss"); clear()

def piglin():
    # (119) in the Nether: loved gold items, porkchops, babies, crossbows, guarded chests and gold, soul light, zombified
    cmd("nether gen"); time.sleep(2.0); cmd("nether tp"); time.sleep(2.0); cmd("god 1"); clear(); cmd("look 0 0")
    print("  ", cmd("wear golden_helmet"))  # (gold on: piglins leave you be, and do their own things)
    pg = spawn("piglin", 3.5)
    # a golden apple thrown to it ([Q]): it looks at it, keeps it; killed, it drops it
    a0 = int(f244b()["admired"]); k0 = int(f244b()["kept"])
    if hold_item("golden_apple", 1, name="Golden Apple"):
        aim_at(pg, 0.3); cmd("keys Q 0.1"); time.sleep(0.8)
    cmd("enemyrelease piglin")
    wait_for(lambda: int(f244b()["admired"]) > a0, 8); time.sleep(1.0); p2 = near("piglin")
    if p2: aim_at(p2, 1.2)
    shot("piglin_admire")
    step("a piglin picks up a golden apple and looks at it", int(f244b()["admired"]) > a0, f244b()["admired"])
    wait_for(lambda: int(f244b()["kept"]) > k0, 10)
    step("it keeps it (no barter: only a gold ingot buys)", int(f244b()["kept"]) > k0 and "golden_apple" in (mob_line("piglin") or ""), mob_line("piglin"))
    # porkchop: eaten
    e0 = fbs("piglin", "ate")
    cmd("enemyhold piglin 3.5"); time.sleep(0.4)  # (in front of me again: it had wandered off with its apple)
    if hold_item("raw_porkchop", 1, name="Raw Porkchop"):
        p2 = near("piglin");
        if p2: aim_at(p2, -0.5)  # (thrown low: it lands by the piglin, not on something behind it)
        cmd("keys Q 0.1"); time.sleep(0.8)
    cmd("enemyrelease piglin")
    wait_for(lambda: fbs("piglin", "ate") > e0, 25)
    step("it eats a porkchop", fbs("piglin", "ate") > e0, fbs("piglin", "ate"))
    # killed: what it kept drops
    if hold_item("netherite_sword", name="Netherite Sword"):
        cmd("enemyhold piglin 2.0"); time.sleep(0.4)
        for _ in range(12):
            p2 = near("piglin")
            if not p2: break
            swing(p2, 1, 0.5)
    time.sleep(1.5); l = loose(10)
    step("killed, it drops the golden apple it kept", "golden_apple" in l.lower().replace(" ", "_"), l[:140])
    clear()
    # a baby: never goes for you (no gold armor on)
    cmd("enemy babypiglin 4"); time.sleep(4.0)
    b = mob_line("baby piglin")
    step("a baby piglin leaves you be (no gold on)", b is not None and "target=-" in b, b)
    if near("baby piglin"): aim_at(near("baby piglin"), 0.3)
    shot("piglin_baby"); clear()
    # a crossbow piglin shoots (no gold on now)
    print("  ", cmd("wear none"))
    cmd("god 0"); cmd("hp 100"); s0 = int(f244b()["xbowShots"])
    cmd("enemy crossbowpiglin 7"); time.sleep(0.5)
    ok = wait_for(lambda: int(f244b()["xbowShots"]) > s0, 25)
    if near("piglin"): aim_at(near("piglin"), 1.2)
    shot("piglin_crossbow")
    step("a crossbow piglin shoots at you", ok, f244b()["xbowShots"])
    cmd("god 1"); clear()
    # a chest opened where a piglin sees you: it's after you
    fc = R.feet_cell(); print("  ", R.place("chest", fc, 0, fc[1], 2, facing=3)); time.sleep(0.8)
    print("  ", cmd("wear golden_helmet"))  # (gold on: it leaves you be... until the chest)
    pg = spawn("piglin", 5); g0 = int(f244b()["guard"])
    pilot.aim_at((fc[0] + 0.5) * S, (fc[1] + 0.5) * S, (fc[2] + 2.5) * S); time.sleep(0.3); cmd("keys E 0.1"); time.sleep(1.0); cmd("keys Escape 0.1")
    cmd("enemyrelease piglin")
    ok = wait_for(lambda: int(f244b()["guard"]) > g0, 4)
    step("opening a chest a piglin sees: it's after you (gold or not)", ok and "target=" in (mob_line("piglin") or "") and "target=-" not in (mob_line("piglin") or ""), mob_line("piglin"))
    clear()
    # a soul torch: it keeps away
    pg = spawn("piglin", 3); r0 = int(f244b()["fledSoul"])
    print("  ", R.place("soul_torch", fc, 1, fc[1], 1)); cmd("enemyrelease piglin")
    ok = wait_for(lambda: int(f244b()["fledSoul"]) > r0, 5)
    step("a piglin keeps away from a soul torch", ok, f244b()["fledSoul"])
    cmd(f"breakabs {fc[0] + 1} {fc[1]} {fc[2] + 1}"); clear()
    # a zombified piglin near: it keeps away
    pg = spawn("piglin", 3); z0 = int(f244b()["fledZomb"]); cmd("enemyrelease piglin")
    cmd("enemy zombified piglin 4"); time.sleep(0.3)
    ok = wait_for(lambda: int(f244b()["fledZomb"]) > z0, 6)
    step("a piglin keeps away from a zombified piglin", ok, f244b()["fledZomb"])
    clear()
    # two piglins and a hoglin: they hunt it
    h0 = int(f244b()["hunts"])
    cmd("enemy piglin 3"); cmd("enemy piglin 4"); time.sleep(0.5); cmd("enemy hoglin 6"); time.sleep(0.5)
    ok = wait_for(lambda: int(f244b()["hunts"]) > h0, 10)
    shot("piglin_hunt")
    step("two piglins go hunting a grown hoglin (Minecraft 26.3)", ok, f244b()["hunts"])
    clear()

def cat():
    # (104) wild: wary, tempted by fish; tamed: sits on a chest (which then won't open), kittens, phantoms, a gift
    spot(173)
    w0 = int(f244b()["wary"]); t0 = int(f244b()["tempted"])
    cmd("enemy cat 5"); time.sleep(0.4)
    hold_item("stick", name="Stick"); pilot.walk(1.0); time.sleep(1.5)
    step("a wild cat keeps away from you", int(f244b()["wary"]) > w0, f244b()["wary"])
    clear(); hold_item("raw_cod", 4, name="Raw Cod"); cmd("enemy cat 6"); time.sleep(5.0)  # (a cat that hasn't just run from me)
    step("holding fish and still, it comes to you", int(f244b()["tempted"]) > t0, f244b()["tempted"])
    c = near("cat")
    if c: aim_at(c, 0.3)
    shot("cat_tempted")
    print("  ", cmd("pet tame cat"))
    fc = R.feet_cell(); print("  ", R.place("chest", fc, 1, fc[1], 2, facing=3)); time.sleep(0.8)
    p0 = int(f244b()["perches"])
    ok = wait_for(lambda: int(f244b()["perches"]) > p0, 40, 1.0)
    step("a tamed cat goes and sits on a chest", ok and "perched=True" in (mob_line("cat") or ""), mob_line("cat"))
    pilot.aim_at((fc[0] + 1.5) * S, (fc[1] + 0.6) * S, (fc[2] + 2.5) * S); time.sleep(0.3); shot("cat_on_chest")
    cmd("keys E 0.1"); time.sleep(0.8); open_ = cmd("chestui state")
    step("a cat on it: the chest won't open", "open=False" in open_ or "open=True" not in open_, open_[:80])
    if "open=True" in open_: cmd("chestui close")  # (Escape with no screen open is the pause menu)
    clear()
    # kittens: two tamed cats fed fish
    b0 = int(f244b()["born"])
    hold_item("raw_cod", 8, name="Raw Cod")  # (fish in hand first: wild cats keep away from you otherwise)
    cmd("enemy cat 2"); cmd("enemy cat 3"); time.sleep(2.0)
    # (tamed the player's way: fish until each one takes, one try in three)
    for _ in range(24):
        wild = lines_of("Cat#", "tamed=False")
        if not wild: break
        hold_item("raw_cod", 1, name="Raw Cod"); c = walk_up(wild[0]); press_e(c, 0.3)
    print("   ", [x[:90] for x in lines_of("Cat#")])
    for line in lines_of("Cat#", "tamed=True")[:2]:
        hold_item("raw_cod", 2, name="Raw Cod"); c = walk_up(line); press_e(c, 0.3)
    ok = wait_for(lambda: int(f244b()["born"]) > b0, 6)
    step("two tamed cats fed fish: a kitten", ok and near("kitten") is not None, cmd("enemies")[:160])
    clear()
    # phantoms keep off a cat
    cmd("enemy cat 2"); time.sleep(0.5); print("  ", cmd("pet tame cat")); h0 = int(f244b()["hiss"])
    print("  ", cmd("phantoms spawn"))
    ok = wait_for(lambda: int(f244b()["hiss"]) > h0, 40, 1.0)
    step("a phantom won't swoop near a cat (it hisses)", ok, f244b())
    # the morning gift
    g0 = int(f244b()["gifts"]); print("  ", cmd("catgift")); time.sleep(1.0)
    step("a tamed cat's morning gift (dev: as the ship lands)", int(f244b()["gifts"]) > g0, loose(4)[:120])
    clear()

def parrot():
    # (105) flies to leaves, glides; tamed: onto your shoulder, off when hurt; a cookie kills it
    spot(174)
    fc = R.feet_cell()
    for dy in range(0, 3): R.place("oak_log", fc, 2, fc[1] + dy, 3)
    for dx in range(1, 4):
        for dz in range(2, 5): R.place("leaves", fc, dx, fc[1] + 3, dz)
    time.sleep(1.0)
    f0 = int(f244b()["flights"]); t0 = int(f244b()["tree"])
    cmd("enemy parrot 2"); time.sleep(0.5)
    # (beside the tree: Minecraft's parrots look for leaves within 3 blocks)
    logs = [tuple(map(float, m.groups())) for m in re.finditer(r"@([-\d.]+),([-\d.]+),([-\d.]+)", cmd("where oak_log 25"))]
    if logs:
        lx, ly, lz = min(logs, key=lambda q: q[1])
        print("  ", cmd(f"enemyto parrot {lx:.2f} {ly - 0.4 * S:.2f} {lz - 1.2 * S:.2f}"))
    ok = wait_for(lambda: int(f244b()["flights"]) > f0, 25)
    pr = near("parrot")
    if pr: aim_at(pr, 0.2)
    shot("parrot_flying")
    step("a parrot flies", ok, mob_line("parrot"))
    wait_for(lambda: int(f244b()["tree"]) > t0, 25)
    pr = near("parrot")
    if pr: aim_at(pr, 0.2)
    shot("parrot_tree")
    step("it sits on leaves or a log", int(f244b()["tree"]) > t0, mob_line("parrot"))
    clear()
    cmd("enemy parrot 2"); time.sleep(0.5); print("  ", cmd("pet tame parrot")); s0 = int(f244b()["shoulder"])
    pr = near("parrot")
    if pr: pilot.go_to(pr["x"], pr["z"], stop=0.3, max_time=6)
    ok = wait_for(lambda: int(f244b()["shoulder"]) > s0, 12)
    step("walking into your tamed parrot: it's on your shoulder", ok and "perched=True" in (mob_line("parrot") or ""), mob_line("parrot"))
    cmd("look 60 10"); shot("parrot_shoulder")
    l0 = int(f244b()["left"]); cmd("god 0"); print("  ", cmd("hurt 5 Mauling")); time.sleep(1.5); cmd("god 1"); cmd("hp 100")
    step("hurt: it leaves your shoulder", int(f244b()["left"]) > l0 and "perched=False" in (mob_line("parrot") or ""), mob_line("parrot"))
    clear()
    cmd("enemy parrot 2.5"); time.sleep(0.5); k0 = int(f244b()["poisoned"])
    pr = near("parrot")
    if hold_item("cookie", name="Cookie") and pr: cmd("enemyhold parrot 2.5"); time.sleep(0.3); press_e(near("parrot"), 0.2)
    time.sleep(1.0)
    step("a cookie kills a parrot", int(f244b()["poisoned"]) > k0 and near("parrot") is None, cmd("enemies")[:120])
    clear()

def horse2():
    # (106) foals inherit speed/jump/health; foods; follows a golden carrot; rears when bucking
    spot(175)
    i0 = int(f244b()["inherited"])
    for d in (3, 6):
        cmd(f"enemy horse {d}"); time.sleep(0.8)
        print("  ", cmd("horse tame")[:40])
    for line in lines_of("Horse#", "baby=False")[:2]:
        # (up to each in turn, a golden carrot)
        hold_item("golden_carrot", 2, name="Golden Carrot"); h = walk_up(line, 2.2); press_e(h, 1.2)
    ok = wait_for(lambda: int(f244b()["inherited"]) > i0, 10)
    print("   ", fm(30)[:400])
    step("two tamed horses fed golden carrots: a foal with its parents' stats", ok, fm(30)[:300])
    a0 = fbs("horse", "ate")
    f = near("foal")
    if f and hold_item("wheat", 3, name="Wheat"): press_e(f, 0.8)
    time.sleep(0.8)
    step("wheat feeds the foal (it grows sooner)", fbs("horse", "ate") > a0, fbs("horse", "ate"))
    if f: aim_at(f, 0.8)
    shot("horse_foal")
    clear()
    # follows a golden carrot
    cmd("enemy horse 4"); time.sleep(0.8); hold_item("golden_carrot", 1, name="Golden Carrot")
    h = near("horse"); d0 = h["d"] if h else 0
    pilot.walk(1.5, "S"); time.sleep(3.0)
    raw = cmd("enemies"); step("a horse follows a golden carrot", "Horse" in raw and ("follow" in raw.lower() or (near("horse") and near("horse")["d"] < d0 + 3)), raw[:160])
    clear()
    # rearing: an untamed horse throws you off and rears
    r0 = int(f244b()["rears"])
    cmd("enemy horse 3"); time.sleep(0.8); cmd("clearinv"); time.sleep(0.5)
    for _ in range(5):
        cmd("enemyhold horse 2.5"); time.sleep(0.3); cmd("enemyrelease horse")
        h = near("horse")
        if not h: break
        press_e(h, 1.0); time.sleep(3.5)
        if int(f244b()["rears"]) > r0: break
    shot("horse_rear")
    step("an untamed horse rears as it throws you off", int(f244b()["rears"]) > r0, f244b()["rears"])
    clear()

def llama():
    # (107) sounds, strength, carpets, caravans, hay breeding, spit
    spot(176)
    print("  ", cmd("trader spawn")); time.sleep(3.0)
    lines = [x for x in fm(60).split(" ; ") if x.lower().startswith("trader llama")]
    step("trader llamas each have a strength (1-5)", lines and all(re.search(r"strength=[1-5]", x) for x in lines), lines[:2])
    # a lead: a llama on it, the others fall in behind
    clear(); cmd("enemy llama 3"); cmd("enemy llama 5"); cmd("enemy llama 7"); time.sleep(1.0)
    j0 = int(f244b()["joined"])
    l = near("llama")
    if l and hold_item("lead", 1, name="Lead"): press_e(l, 1.2)
    pilot.walk(2.0, "S"); time.sleep(4.0)
    step("llamas fall in behind a led one (a caravan)", int(f244b()["joined"]) > j0, fm(20)[:300])
    shot("llama_caravan"); clear()
    # tamed (as a reload gives it back): a carpet on one, then hay breeds two
    c0 = int(f244b()["carpets"]); b0 = int(f244b()["born"])
    cmd("enemy llama 3"); cmd("enemy llama 6"); time.sleep(1.0)
    for n, line in enumerate(lines_of("Llama#")[:2]):
        l = walk_up(line, 2.2)
        print("  ", cmd("mobstate llama 1,0,0"))  # (the nearest: this one; tamed, as a reload gives it back)
        if n == 0 and hold_item("red_carpet", 1, name="Red Carpet"):
            press_e(l, 1.2); time.sleep(0.6); aim_at(l, 1.0); shot("llama_carpet")
            step("a carpet on a tamed llama", int(f244b()["carpets"]) > c0 and "carpet=14" in (mob_line("llama") or ""), mob_line("llama"))
        hold_item("hay_block", 1, name="Hay Bale"); press_e(l, 1.0)
    ok = wait_for(lambda: int(f244b()["born"]) > b0, 6)
    step("two tamed llamas fed hay: a young llama", ok, cmd("enemies")[:160])
    clear()
    # spit: hit one, it spits (1 health)
    cmd("enemy llama 3"); time.sleep(0.8); cmd("enemyhold llama 2.0"); time.sleep(0.3); cmd("enemyrelease llama")
    cmd("god 0"); cmd("hp 100"); s0 = int(f244b()["spitHits"])
    l = near("llama")
    if l and hold_item("wooden_sword", name="Wooden Sword"): swing(l, 1)
    time.sleep(1.5); hp = T.st("hp")
    step("hit, a llama spits at you (1 health: 7 of 100)", int(f244b()["spitHits"]) > s0, f"hp={hp} spits={f244b()['spits']}")
    cmd("god 1"); cmd("hp 100"); clear()

def trader():
    # (108) invisibility at night, milk by day; flees zombies; leaves; killed, its llamas' leads drop
    spot(177)
    print("  ", cmd("trader spawn")); time.sleep(3.0)
    d0 = int(f244b()["drank"])
    print("  ", cmd("daytime 0.85")); ok = wait_for(lambda: "invisible=True" in (mob_line("wandering trader", 60) or ""), 8)
    t = near("wandering trader")
    if t: aim_at(t, 1.2)
    shot("trader_invisible")
    step("at night it drinks invisibility", ok, mob_line("wandering trader", 60))
    print("  ", cmd("daytime 0.3")); ok = wait_for(lambda: "invisible=False" in (mob_line("wandering trader", 60) or ""), 8)
    step("by day, milk: it shows again", ok and int(f244b()["drank"]) >= d0 + 2, mob_line("wandering trader", 60))
    f0 = int(f244b()["fled"]); t = near("wandering trader")
    if t: cmd(f"tp {t['x']:.2f} {t['y'] + 0.2:.2f} {t['z'] + 3:.2f}"); time.sleep(0.5)
    cmd("enemy zombie 4"); ok = wait_for(lambda: int(f244b()["fled"]) > f0, 6)
    step("it keeps away from a zombie", ok, f244b()["fled"])
    # killed: its llamas' leads drop
    if hold_item("netherite_sword", name="Netherite Sword"):
        for _ in range(10):
            t = near("wandering trader")
            if not t: break
            cmd(f"tp {t['x']:.2f} {t['y'] + 0.2:.2f} {t['z'] + 1.6:.2f}"); time.sleep(0.3); swing(t, 1, 0.5)
    time.sleep(2.0); l = loose(16)
    step("killed, its llamas' leads drop", "lead" in l.lower(), l[:140])
    clear()

SECTIONS = {"cooked": cooked, "health": health, "equip": equip, "sheep": sheep, "wolf": wolf, "golem": golem, "creeper": creeper,
            "blaze": blaze, "water": water, "endermite": endermite, "infight": infight, "breed": breed, "spider": spider, "skeleton": skeleton, "enderman": enderman, "ghast": ghast,
            "magma": magma, "rabbit": rabbit, "zpiglin": zpiglin, "hunt": hunt,
            "snowgolem": snowgolem, "drown": drown, "golemtoss": golemtoss, "piglin": piglin, "cat": cat, "parrot": parrot, "horse2": horse2, "llama": llama, "trader": trader}

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
