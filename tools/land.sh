#!/bin/bash
# land on level index $1 (default 0)
cd /e/claude/mods/lethal_minecraft
py tools/dev.py "route ${1:-0}" >/dev/null; sleep 5; py tools/dev.py land >/dev/null
for i in $(seq 1 40); do sleep 3; r=$(py tools/dev.py leave_check); echo "$r" | grep -q "landed=True" && break; done
echo $r; sleep 6
