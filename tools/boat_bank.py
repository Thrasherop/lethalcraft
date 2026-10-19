"""#245: put a boat on March's far bank and row it straight off into the lake (where the old build's boat went under and
sank to the bottom with its rider), a few times; log its height and what it floats in.
usage: py tools/boat_bank.py [tries]"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from regress import cmd, pos

def info():
    r = cmd("boatinfo")
    m = re.search(r"boat ([-\d.]+),([-\d.]+),([-\d.]+) status=(\w+) surface=([-\d.]+)", r)
    return (float(m.group(1)), float(m.group(2)), float(m.group(3)), m.group(4), float(m.group(5))) if m else None

tries = int(sys.argv[1]) if len(sys.argv) > 1 else 3
cmd("god 1"); results = []
for t in range(tries):
    cmd("clearenemies 60 all"); cmd("clearinv"); cmd("invgive oak_boat 1"); time.sleep(2.0); cmd("slot 0")
    cmd("tp -21.5 2 48.5"); time.sleep(2.0)
    cmd("look 205 55"); time.sleep(0.5); cmd("rmb"); time.sleep(1.5)
    b = info()
    if not b: print("no boat placed"); continue
    R.look_at(b[0], b[1] + 0.3, b[2]); time.sleep(0.4); cmd("keys E 0.15"); time.sleep(1.0)
    print(f"try {t + 1}: boat on the bank at {b[0]:.2f},{b[1]:.2f},{b[2]:.2f} {b[3]}; in it: {abs(pos()[0] - b[0]) < 1.5}")
    cmd("keys W 9"); t0 = time.time(); low = 99.0; under = 0; ys = []
    while time.time() - t0 < 12:
        b = info()
        if b and b[4] != 0.0:
            low = min(low, b[1]); under += b[3] == "UnderWater"
        if b: ys.append(f"{b[1]:.2f}{b[3][0]}")
        time.sleep(0.4)
    me = pos()
    print("   heights:", " ".join(ys))
    print(f"   lowest in water {low:.2f}, under-water samples {under}, me at y {me[1]:.2f}")
    results.append((low, under))
    cmd("keys LeftCtrl 0.15"); time.sleep(0.8)
print("results:", results)
