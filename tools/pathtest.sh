#!/bin/bash
# pathtest.sh "<MinecraftDirectory value>" "<MinecraftVersion value>"
C="/o/SteamLibrary/steamapps/common/Lethal Company/BepInEx/config/thrasherop.lethalminecraft.cfg"
L="/o/SteamLibrary/steamapps/common/Lethal Company/BepInEx/LogOutput.log"
py - "$C" "$1" "$2" <<'PY'
import sys,re
p,d,v=sys.argv[1],sys.argv[2],sys.argv[3]
s=open(p,encoding='utf-8-sig').read()
s=re.sub(r'(?m)^MinecraftDirectory = .*$', lambda m:'MinecraftDirectory = '+d, s)
s=re.sub(r'(?m)^MinecraftVersion = .*$', lambda m:'MinecraftVersion = '+v, s)
open(p,'w',encoding='utf-8').write(s)
PY
powershell -Command "Stop-Process -Name 'Lethal Company' -Force -ErrorAction SilentlyContinue"; sleep 2
rm -f "$L"
G="O:\SteamLibrary\steamapps\common\Lethal Company"
# (Steam friends status offline first: launching the game shows friends you're playing, and Steam turns it back on by itself)
powershell -Command "Start-Process 'steam://friends/status/offline'"; sleep 1
powershell -Command "Start-Process -FilePath '$G\Lethal Company.exe' -WorkingDirectory '$G' -ArgumentList '-screen-fullscreen','0','-screen-width','640','-screen-height','360'"
for i in $(seq 1 30); do sleep 2; grep -q "Loaded .* Minecraft sound clips\|using built-in art" "$L" 2>/dev/null && break; done
echo "== Dir=[$1] Ver=[$2]"
grep -E "LethalMinecraft\] (LethalMinecraft 1|Atlas built|Loaded .* sound|MinecraftDirectory|MinecraftVersion|No local|Scanning)|Warning.*LethalMinecraft" "$L" | cut -c1-260
powershell -Command "Stop-Process -Name 'Lethal Company' -Force -ErrorAction SilentlyContinue"
