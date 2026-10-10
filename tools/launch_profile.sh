#!/bin/bash
# Start the game with a separate BepInEx profile (made by tools/make_profile.py), the way r2modman does: doorstop loads
# that profile's BepInEx instead of the game folder's. The game's own BepInEx is left alone.
# usage: bash tools/launch_profile.sh <profile dir> [plugin dll to copy in first]
# e.g.:  bash tools/launch_profile.sh E:/claude/mods/profiles/moons bin/Release/netstandard2.1/LethalCraft.dll
PROF="$1"
[ -f "$PROF/BepInEx/core/BepInEx.Preloader.dll" ] || { echo "no profile at $PROF"; exit 1; }
if [ -n "$2" ]; then cp "$2" "$PROF/BepInEx/plugins/LethalCraft/LethalCraft.dll" && echo "LethalCraft.dll copied in"; fi
powershell -Command "Stop-Process -Name 'Lethal Company' -Force -ErrorAction SilentlyContinue"; sleep 2
# (Steam friends status offline first, as in restart.sh)
powershell -Command "Start-Process 'steam://friends/status/offline'"; sleep 1
WIN=$(cygpath -w "$PROF/BepInEx/core/BepInEx.Preloader.dll")
"/c/Program Files (x86)/Steam/steam.exe" -applaunch 1966720 --doorstop-enabled true --doorstop-target-assembly "$WIN" &
cd "$(dirname "$0")/.."
for i in $(seq 1 60); do sleep 3; r=$(py tools/dev.py state 2>/dev/null); if echo "$r" | grep -q "pos="; then echo "$r" | cut -c1-80; break; fi; done
py tools/dev.py "res 1280 720" > /dev/null 2>&1
grep -E "Exception|\[Error" "$PROF/BepInEx/LogOutput.log" | head -10
