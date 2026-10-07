#!/bin/bash
# give a kit of items and pick them all up
cd /e/claude/mods/lethal_minecraft
n=0
for it in "$@"; do py tools/dev.py "give $it" >/dev/null; n=$((n+1)); done
sleep 1.5
for i in $(seq 1 $n); do py tools/dev.py grab >/dev/null; sleep 1.2; done
py tools/dev.py state | tr ' ' '\n' | grep slots
