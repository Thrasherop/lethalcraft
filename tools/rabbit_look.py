"""#239: a rabbit held still in front of you, looked at from its side and its front. usage: py tools/rabbit_look.py <prefix>"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from regress import cmd, pos, S
tag = sys.argv[1] if len(sys.argv) > 1 else "rabbit"
if "inShipPhase=True" in cmd("state"): print("land:", R.land(0)); time.sleep(2)
cmd("craftui close"); cmd("god 1"); cmd("mobspawns 1"); cmd("clearenemies 100 all"); cmd("dayfreeze 1"); cmd("daytime 0.4")
fc = R.start_flat(int(os.environ.get("SPOT", "9")), [(dx, dz) for dx in (-1, 0, 1) for dz in range(0, 4)])
cmd("look 0 0"); print(cmd("spawnhere Rabbit 2.5")); time.sleep(1.0)
for name, yaw in (("side", 90), ("front", 180), ("quarter", 135)):
    cmd("look 0 25"); cmd("enemyhold rabbit 1.6"); time.sleep(0.6)
    m = re.search(r"Rabbit@([-\d.]+),([-\d.]+),([-\d.]+)", cmd("enemies"))
    x, y, z = map(float, m.groups())
    print(cmd(f"enemyfacing rabbit {yaw}")[:60] if True else "")
    time.sleep(0.4); R.look_at(x, y + 0.3, z); time.sleep(0.4)
    cmd(f"screenshot E:/claude/mods/lmc_rabbit239/shots/{tag}_{name}.png"); time.sleep(0.5)
cmd("enemyrelease rabbit")
print("done")
