#!/bin/bash
# restart, land, flat spot, pickaxe
cd /e/claude/mods/lethal_minecraft
tools/restart.sh | grep -o "build ok\|pos=[^ ]*\| error .*"
py tools/dev.py land >/dev/null
for i in $(seq 1 40); do sleep 2; py tools/dev.py leave_check | grep -q "landed=True" && break; done
sleep 6
py tools/dev.py flatspot >/dev/null; sleep 1
py tools/dev.py "tprel 0 0 5" >/dev/null; sleep 0.5
tools/kit.sh "pickaxe 1" "torch 16" >/dev/null
