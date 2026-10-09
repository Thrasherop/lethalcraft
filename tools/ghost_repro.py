"""#36 repro: a block message arriving after the moon unloaded (before orbit) made the world block root outside the
moon's scene; nothing cleared it, so its blocks (and every later moon's) stayed for that player.
Lands on Experimentation, leaves, places a moon block in the window after the moon unloads (as a late message would),
lands again, and looks for that block. usage: py tools/ghost_repro.py [old]   (old: rootcheck 0, the old behaviour)"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd

def rc(): return cmd("rootcheck")
def land(i=0):
    cmd(f"route {i}"); time.sleep(5); cmd("land")
    for _ in range(60):
        time.sleep(2)
        if "landed=True" in cmd("leave_check"): break
    time.sleep(6)

print(cmd("rootcheck " + ("0" if "old" in sys.argv else "1")))
cmd("god 1")
if "inShipPhase=True" in cmd("state"): land()
print("landed:", rc())
cmd("leave")
placed = None
for _ in range(400):
    time.sleep(0.25)
    r = rc()
    if "moonLoaded=False" in r and "inShipPhase=False" in r:
        placed = cmd("placeabs stone 0 30 -60")
        print("moon unloaded, still not in orbit; placed:", placed, "|", rc())
        break
for _ in range(120):
    time.sleep(1)
    if "inShipPhase=True" in rc(): break
time.sleep(2)
print("in orbit:", rc())
land()
r = rc()
print("landed again:", r)
# leave again: blocks of this moon must go with it (the old way they stayed in the orphan root: ghosts on every moon after)
cmd("leave")
for _ in range(150):
    time.sleep(1)
    if "inShipPhase=True" in rc(): break
time.sleep(2)
r = rc()
n = int(re.search(r"frame0=(\d+)", r).group(1))
print("RESULT:", "no ghosts (nothing of the moon left in orbit)" if n == 0 else f"GHOSTS: {n} moon blocks still exist in orbit", "|", r)
