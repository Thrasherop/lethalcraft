"""py tools/slotof.py <item name part>: select the hotbar slot holding it (prints the slots)"""
import sys, os, re
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd
s = cmd("state"); slots = re.search(r"slots=\[([^\]]*)\]", s).group(1).split(",")
i = next((i for i, x in enumerate(slots) if sys.argv[1].lower() in x.lower()), None)
print(slots, i)
if i is not None: cmd(f"slot {i}")
