"""Repro: a right-click made inside a crafting screen fires as a block placement once the screen closes.
Hold torches, open the pocket grid (I), right-click an empty grid cell with the real mouse, close (I), count torches
placed. usage: py tools/latch_repro.py <tag>   (works on old builds too: only needs the 'keys'/'mouse' dev commands)"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from dev import cmd
if "inShipPhase=True" in R.state(): print(R.land(0)); time.sleep(4)
cmd("god 1"); cmd("clearinv"); time.sleep(0.4)
R.start_flat(5)
cmd("give torch 5"); time.sleep(1.5); R.grab_all(); time.sleep(0.8)
print("before:", re.search(r"slots=\[[^\]]*\]", cmd("state")).group(0))
cmd("look 0 45"); time.sleep(0.4)
torches = lambda: sum(1 for e in cmd("near 6").split(" ; ") if e.strip().startswith("torch"))
t0 = torches()
cmd("keys I 0.08"); time.sleep(0.8)
cmd("mouse moveto 280 201"); time.sleep(0.2)
cmd("mouse right 0.08"); time.sleep(0.6)
cmd("keys I 0.08"); time.sleep(1.2)
t1 = torches()
print("after:", re.search(r"slots=\[[^\]]*\]", cmd("state")).group(0))
print(f"torches placed in the world by closing the screen: {t1 - t0}  -> {'BUG' if t1 > t0 else 'ok'}")
