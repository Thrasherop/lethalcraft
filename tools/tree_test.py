"""Chopping a moon tree: stand by the nearest tree, aim at its trunk, hold left-click (bare hand or a tool) until it
shatters; check the tree is gone and oak logs dropped.  usage: py tools/tree_test.py [moonIdx] [tool]"""
import sys, os, time, re, math
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
import pilot
from regress import cmd

def trees(r=200):
    out = []
    for m in re.finditer(r"(tree[^;]*?) tag=Tree CapsuleCollider at ([-\d.]+),([-\d.]+),([-\d.]+) d=([\d.]+)", cmd(f"trees {r}")):
        out.append((m.group(1), float(m.group(2)), float(m.group(3)), float(m.group(4)), float(m.group(5))))
    return sorted(out, key=lambda t: t[4])

def logs_near(x, y, z, r=6):
    """oak log stacks lying near a point: [(x, y, z)]"""
    out = []
    for m in re.finditer(r"Oak Log@([-\d.]+),([-\d.]+),([-\d.]+) d=[\d.]+ held=False", cmd("objs")):
        p = tuple(float(v) for v in m.groups())
        if math.dist(p, (x, y, z)) < r: out.append(p)
    return out

def chop(tool=None, skip=0, named=None, dist=2.0):
    cmd("clearinv")
    if tool: cmd(f"invgive {tool} 1"); time.sleep(1.5); cmd("slot 0")
    ts = trees(400)
    if named: ts = [t for t in ts if t[0] == named]
    if len(ts) <= skip: print("no trees"); return None
    name, x, y, z, _ = ts[skip]
    # stand 2 m off the trunk (on the side toward the ship), at the trunk's ground
    cmd("tpship"); time.sleep(1)
    px, _, pz, *_ = pilot.state()
    d = math.hypot(px - x, pz - z); ux, uz = (px - x) / d, (pz - z) / d
    cmd(f"tp {x + ux * dist:.2f} {y + 2.5:.2f} {z + uz * dist:.2f}"); time.sleep(2.0)
    fy = pilot.state()[1]
    pilot.aim_at(x, max(y, fy) + 1.6, z)  # (chest height, on a slope too)
    time.sleep(0.4)
    print(" ", name, "mine?:", cmd("mine?")[:90])
    standing = lambda: "tag=Tree" in cmd(f"trees 0.5 {x:.2f} {y:.2f} {z:.2f}")
    n_logs = len(logs_near(x, y, z))
    t0 = time.time()
    cmd("lmb down")
    gone = R.wait(lambda: not standing(), 25, step=0.25)
    took = time.time() - t0
    cmd("lmb up")
    time.sleep(2.5)
    got = logs_near(x, y, z)
    print(f"{tool or 'hand'}: tree {'down' if gone else 'STILL STANDING'} after {took:.1f}s; log stacks near it {n_logs} -> {len(got)} {got}")
    return gone, took, len(got) - n_logs

if __name__ == "__main__":
    idx = int(sys.argv[1]) if len(sys.argv) > 1 else 2
    if "level=" not in cmd("state") or "inShipPhase=True" in cmd("state"): print("landing", R.land(idx)); time.sleep(6)
    cmd("god 1")
    chop(sys.argv[2] if len(sys.argv) > 2 else "wooden_axe")
