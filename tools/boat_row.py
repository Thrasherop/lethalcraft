"""#245: row a boat around on a moon's water with real keys and log what it floats in, to catch it sinking.
usage: py tools/boat_row.py [x z]   (stand on the shore there, look at the water; default March's (0, 0.5))"""
import sys, os, time, re, threading
sys.path.insert(0, os.path.dirname(__file__))
from regress import cmd, pos

def info():
    r = cmd("boatinfo")
    m = re.search(r"boat ([-\d.]+),([-\d.]+),([-\d.]+) status=(\w+) surface=([-\d.]+)", r)
    return (float(m.group(1)), float(m.group(2)), float(m.group(3)), m.group(4), float(m.group(5)), r) if m else None

def main():
    x, z = (float(sys.argv[1]), float(sys.argv[2])) if len(sys.argv) > 2 else (0.0, 0.5)
    cmd("god 1"); cmd("clearinv"); cmd("invgive oak_boat 1"); time.sleep(2.0)
    cmd(f"tp {x} 3 {z}"); time.sleep(2.0); cmd("slot 0"); cmd("look 0 40"); time.sleep(0.5)
    cmd("rmb"); time.sleep(1.5)
    b = info(); print("placed:", b[5][:200] if b else "no boat")
    # get in: look at it and press E
    bx, by, bz = b[0], b[1], b[2]
    import regress as R
    R.look_at(bx, by + 0.3, bz); time.sleep(0.4)
    cmd("keys E 0.15"); time.sleep(1.0)
    print("in:", "inBoat" if abs(pos()[0] - bx) < 1.5 else "NOT in", cmd("state")[:60])
    worst = []
    def watch(label, secs):
        t0 = time.time()
        while time.time() - t0 < secs:
            b = info()
            if b:
                depth = b[4] - b[1]
                flag = "  <-- SUNK" if b[3] == "UnderWater" or (b[3] == "InWater" and depth > 0.84) or (b[3] == "OnLand" and b[1] < -2.9) else ""
                if flag: worst.append((label, b[5][:260]))
                print(f"  {label:10s} {b[0]:7.2f},{b[1]:6.2f},{b[2]:7.2f} {b[3]:10s} surface {b[4]:6.2f} depth {depth:5.2f}{flag}")
            time.sleep(0.5)
    for label, keys, secs in [("forward", "W", 6), ("fwd+left", "W+A", 5), ("fwd+right", "W+D", 6), ("forward", "W", 8), ("left", "A", 3), ("forward", "W", 8), ("back", "S", 3), ("turn", "A", 2.5), ("off bank", "W", 10)]:
        cmd(f"keys {keys} {secs}")
        watch(label, secs)
    watch("drift", 3)
    print("sunk samples:", len(worst))
    for w in worst[:6]: print("   ", w)

main()
