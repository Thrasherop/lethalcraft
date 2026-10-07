#!/bin/bash
# restart game, land on Experimentation, go outside, get a kit
cd /e/claude/mods/lethal_minecraft
tools/restart.sh | tail -1 || exit 1
py tools/dev.py land >/dev/null
for i in $(seq 1 20); do sleep 3; if py tools/dev.py state | grep -q "inShipPhase=False"; then break; fi; done
sleep 4
py tools/dev.py "tp -12 6 -12" >/dev/null; sleep 2
tools/kit.sh "$@"
