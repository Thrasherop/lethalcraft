"""Repro: tunnel into the wall beside the first door of Experimentation's start room, photograph it.
The collision gets cut; the bug was that the wall's visual mesh (StartRoomElevator, no collider, 67 m tall) wasn't.
usage: py tools/startroom_repro.py <tag>"""
import sys, os, time
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from dev import cmd
tag = sys.argv[1] if len(sys.argv) > 1 else "x"
if "inShipPhase=True" in R.state(): print(R.land(0)); time.sleep(4)
cmd("god 1")
print(cmd("tp -10.23 -219.5 66.0")); time.sleep(1.5)
print(R.cmd("state")[:60])
for y in (-157, -156):
    for z in (50, 51, 52):
        print(z, y, cmd(f"digabs -8 {y} {z} force")[:60])
time.sleep(1.0)
path = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "shots", f"startroom_{tag}.png"))
cmd(f"photo {path} -10.2 -217.9 64.5 0 10"); time.sleep(1.6)
print(path, cmd("carvedinfo StartRoomElevator")[:80])
