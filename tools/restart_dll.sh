#!/bin/bash
# deploy a prebuilt LethalCraft.dll (e.g. from a release zip) and relaunch the game in dev mode
# usage: bash tools/restart_dll.sh <path to dll>
dll="$1"; [ -f "$dll" ] || { echo "no dll: $dll"; exit 1; }
powershell -Command "Stop-Process -Name 'Lethal Company' -Force -ErrorAction SilentlyContinue" ; sleep 2
rm -rf "/o/SteamLibrary/steamapps/common/Lethal Company/BepInEx/plugins/LethalMinecraft"; mkdir -p "/o/SteamLibrary/steamapps/common/Lethal Company/BepInEx/plugins/LethalCraft"
cp "$dll" "/o/SteamLibrary/steamapps/common/Lethal Company/BepInEx/plugins/LethalCraft/LethalCraft.dll" || exit 1
echo "deployed $(md5sum "$dll" | cut -c1-8)"
# (Steam friends status offline first: launching the game shows friends you're playing, and Steam turns it back on by itself)
powershell -Command "Start-Process 'steam://friends/status/offline'"; sleep 1
powershell -Command "Start-Process 'steam://rungameid/1966720'"
for i in $(seq 1 40); do sleep 3; r=$(py /e/claude/mods/lethal_minecraft/tools/dev.py state 2>/dev/null); if echo "$r" | grep -q "pos="; then echo "$r" | cut -c1-80; break; fi; done
