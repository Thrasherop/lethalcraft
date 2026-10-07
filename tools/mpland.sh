#!/bin/bash
cd /e/claude/mods/lethal_minecraft
py tools/dv.py 28771 land >/dev/null
until py tools/dv.py 28771 leave_check | grep -q "landed=True"; do sleep 2; done
