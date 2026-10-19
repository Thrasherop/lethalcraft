"""Before a version goes to main (CLAUDE.md): a general play of the whole build at normal speed, as a player, with real
input where there is one: land, mine a shaft and a tunnel, pillar up, craft at a table, fight a zombie with a sword, put
things in a chest and take them out, take off and land, go through a Nether portal. Screenshots in shots/gp_*.png.
usage: py tools/general_play.py"""
import sys, os, time, re, subprocess
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from regress import cmd, pos, S
HERE = os.path.dirname(os.path.abspath(__file__))
def shot(n): cmd(f"screenshot {os.path.join(HERE, '..', 'shots', 'gp_' + n + '.png')}"); time.sleep(0.5)
def slots(): return re.search(r"slots=\[([^\]]*)\]", cmd("state")).group(1)
def run(*args):
    r = subprocess.run([sys.executable, os.path.join(HERE, args[0])] + list(args[1:]), capture_output=True, text=True, timeout=900)
    out = (r.stdout + r.stderr).strip().splitlines()
    return out[-8:]
log = []
def step(name, ok, detail=""):
    log.append((name, ok, detail)); print(("OK  " if ok else "BAD ") + name + (f"  ({detail})" if detail else ""))

t0 = time.time()
if "inShipPhase=True" in cmd("state"): print("land:", R.land(0)); time.sleep(3)
cmd("speed 1"); cmd("god 0"); cmd("gamemode survival"); cmd("mobspawns 1"); cmd("dayfreeze 0")
shot("landed")
# 1. mine: a shaft 5 down and a tunnel 8 long, real mining with a pickaxe (stripmine gives itself the pick)
out = run("stripmine.py", "5", "8", "21")
step("mined a shaft and a tunnel by hand", any("tunnel" in l for l in out), " | ".join(out[-3:])[:300])
shot("mined")
# 2. build: pillar up with real jump + right-click
out = run("pillar_play.py")
step("pillared up with blocks", any("rise per jump" in l for l in out), " | ".join(out[-4:])[:300])
# 3. craft at a crafting table: planks -> sticks, then a wooden pickaxe (the screen's own clicks)
cmd("clearinv"); cmd("invgive oak_planks 8"); time.sleep(2)
cmd("craftui open table"); time.sleep(0.6)
out = run("craft.py", "oak_planks", "0,3")
out2 = run("craft.py", "oak_planks", "0,1,2", "stick", "4,7")
cmd("craftui close"); time.sleep(0.5)
step("crafted sticks and a wooden pickaxe at a table", "Wooden Pickaxe" in slots() or "pickaxe" in slots().lower(), slots()[:200])
# 4. fight a zombie with an iron sword, health watched
cmd("clearinv"); cmd("invgive iron_sword 1"); time.sleep(2); cmd("slot 0")
cmd("look 0 0"); print(cmd("spawnhere Zombie 6")); time.sleep(1)
out = run("fight.py", "Zombie", "60", "35")
step("fought a zombie with a sword", any("dead" in l.lower() or "killed" in l.lower() for l in out), " | ".join(out[-3:])[:300])
shot("fight")
# 5. a chest: put three stacks in, take them back out (its screen's clicks)
fc = R.start_flat(25, [(0, 1)])
sy = R.surface(fc, 0, 1)
print(R.place("chest", fc, 0, sy + 1, 1, facing=3)); time.sleep(0.8)
cmd("clearinv"); cmd("invgive cobblestone 32"); cmd("invgive bread 5"); cmd("invgive torch 16"); time.sleep(2.5)
before = slots()
print(cmd("chestui open near")[:120]); time.sleep(0.6)
for i in range(3): cmd(f"chestui click hot {i} shift"); time.sleep(0.3)
inside = cmd("chestui state")[:240]; mid = slots()
for i in range(3): cmd(f"chestui click chest {i} shift"); time.sleep(0.3)
after = slots(); cmd("chestui close"); time.sleep(0.5)
step("put things in a chest and took them out", "-,-,-" in mid and after.count("-") <= before.count("-"), f"in: {inside} | hotbar then {mid[:80]} | after {after[:120]}")
# 6. take off and land again: what's held stays
held_before = slots()
cmd("tpship"); time.sleep(1); print(cmd("leave")); time.sleep(25)
print("in orbit:", "inShipPhase=True" in cmd("state"))
print("land:", R.land(0)); time.sleep(3)
step("took off and landed again, inventory kept", slots() == held_before, f"{held_before[:120]} -> {slots()[:120]}")
shot("relanded")
# 7. the Nether: a portal built, lit with flint and steel, walked through (the suite's own test of it)
out = subprocess.run([sys.executable, os.path.join(HERE, "regress.py"), "-t", "t_portal_links"], capture_output=True, text=True, timeout=900)
lines = [l for l in out.stdout.splitlines() if "PASS" in l or "FAIL" in l]
step("went through a Nether portal and back", out.stdout.count("[FAIL]") == 0 and len(lines) > 2, " | ".join(lines[-4:])[:400])
print(f"\n{sum(1 for _, ok, _ in log if ok)}/{len(log)} steps fine, {time.time() - t0:.0f} s")
