"""#17: the creative menu's Lethal Company tab; clicks items in sequence and looks for items held by nobody's slot.
usage: py tools/creative_vanilla.py [gap seconds] [rounds] [item names, comma-separated]"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd

def slots(): return re.search(r"slots=\[([^\]]*)\]", cmd("state")).group(1).split(",")

def orphans(names):
    """held=True copies beyond what the hotbar shows"""
    sl = slots(); out = {}
    for n in names:
        held = sum(1 for e in cmd(f"find {n}").split(" ; ") if e.startswith(n + "@") and "held=True" in e)
        mine = sum(1 for e in sl if e == n)
        if held != mine: out[n] = (held, mine)
    return out

def run(gap=1.2, rounds=3, names=("Shotgun", "Stun grenade", "Ammo", "Pro-flashlight")):
    if "gm=creative" not in cmd("state"): cmd("gamemode creative")
    for r in range(rounds):
        cmd("clearinv"); time.sleep(1.5)
        if "--monsters" not in sys.argv: cmd("clearenemies 300")  # (a monster grabbing the player mid-pickup is another matter)
        cmd("keys I 0.08"); time.sleep(0.6); cmd("creativeui click tabs 4")
        items = re.search(r"items=\[([^\]]*)\]", cmd("creativeui state")).group(1).split(",")
        for n in names:
            cmd(f"creativeui click items {items.index('lc:' + n)}"); time.sleep(gap)
        time.sleep(3); cmd("keys I 0.08"); time.sleep(0.5)
        print(f"round {r}: slots {slots()[:5]} strays {orphans(names)}")

if __name__ == "__main__":
    a = [x for x in sys.argv[1:] if not x.startswith("--")]
    run(float(a[0]) if a else 1.2, int(a[1]) if len(a) > 1 else 3, tuple(a[2].split(",")) if len(a) > 2 else ("Shotgun", "Stun grenade", "Ammo", "Pro-flashlight"))
