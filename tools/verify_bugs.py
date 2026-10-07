"""Checks the user-reported bugs against whichever build is running, so the same script shows 'bug present'
on an old build and 'fixed' on a new one. usage: py tools/verify_bugs.py [label]"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
cmd = R.cmd
label = sys.argv[1] if len(sys.argv) > 1 else "build"
out = []
def report(name, ok, detail):
    out.append(f"  [{'OK ' if ok else 'BUG'}] {name}: {detail}")

# 1) store names
r = cmd("termparse buy stone pickaxe")
report("'buy stone pickaxe' buys the stone pickaxe", r.endswith("item=Stone Pickaxe"), r)

# 2) crafting screen: crouch key must not crouch
cmd("craftui close"); cmd("crouch 0"); time.sleep(0.3)
cmd("craftui open"); time.sleep(0.4)
cmd("presskey LeftCtrl"); time.sleep(0.6)
f = cmd("flags2")
cmd("craftui close"); cmd("crouch 0"); time.sleep(0.3)
report("crouch key ignored while crafting", "crouching=False" in f, f)

# 3) wooden pickaxe recipe (3 planks across the top, 2 sticks down the middle)
cmd("clearinv"); time.sleep(0.4)
cmd("give oak_planks 3"); cmd("give stick 2"); time.sleep(1.5); R.grab_all(); time.sleep(0.8)
st = cmd("craftui open table")
pi, _ = R.slot_of(st, "oak_planks"); si, _ = R.slot_of(st, "stick")
if pi is not None and si is not None:
    cmd(f"craftui click hot {pi}")
    for c in (0, 1, 2): cmd(f"craftui click grid {c} right")
    cmd(f"craftui click hot {pi}")
    cmd(f"craftui click hot {si}")
    for c in (4, 7): cmd(f"craftui click grid {c} right")
    cmd(f"craftui click hot {si}")
    time.sleep(1.0)
    st = cmd("craftui state")
    m = re.search(r"out=(\S+)", st)
    report("wooden pickaxe can be crafted", m and m.group(1).startswith("wooden_pickaxe"), "output=" + (m.group(1) if m else "?"))
else:
    report("wooden pickaxe can be crafted", False, "ingredients didn't reach the hotbar: " + st[:120])
cmd("craftui close"); time.sleep(0.8); cmd("clearinv")

print(f"== {label}")
print("\n".join(out))
