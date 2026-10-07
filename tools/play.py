"""Hands-on play helper: run one action per call and get a screenshot back.

usage:  py tools/play.py "<dev command>" ["<dev command>" ...] [--shot name] [--wait seconds]
Each dev command runs in order (see src/DevServer.cs); --shot saves shots/play_<name>.png after waiting.
"""
import sys, os, time
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd

args = sys.argv[1:]
shot = None; wait = 0.6
if "--shot" in args:
    i = args.index("--shot"); shot = args[i + 1]; del args[i:i + 2]
if "--wait" in args:
    i = args.index("--wait"); wait = float(args[i + 1]); del args[i:i + 2]
for c in args:
    print(">", c, "->", cmd(c)[:300])
time.sleep(wait)
if shot:
    path = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "shots", f"play_{shot}.png"))
    if os.path.exists(path): os.remove(path)
    cmd(f"screenshot {path}")
    for _ in range(40):
        if os.path.exists(path) and os.path.getsize(path) > 0: break
        time.sleep(0.1)
    time.sleep(0.2)
    print("shot", path)
print(cmd("state")[:260])
