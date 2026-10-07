#!/bin/bash
# placeat.sh slot yaw pitch  -> select slot, aim, click once
cd /e/claude/mods/lethal_minecraft
py tools/dev.py "slot $1" >/dev/null; sleep 0.35
py tools/dev.py "look $2 $3" >/dev/null; sleep 0.15
py tools/dev.py "lmb 0.08" >/dev/null; sleep 0.45
