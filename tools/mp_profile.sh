#!/bin/bash
# Two-instance LAN test with a BepInEx profile (tools/make_profile.py), like tools/mp.sh with the game's own BepInEx:
# host on 28771, client on 28772, both started through doorstop with the profile's BepInEx.
# usage: bash tools/mp_profile.sh <profile dir> [plugin dll to copy in first]
PROF="$1"
[ -f "$PROF/BepInEx/core/BepInEx.Preloader.dll" ] || { echo "no profile at $PROF"; exit 1; }
if [ -n "$2" ]; then cp "$2" "$PROF/BepInEx/plugins/LethalCraft/LethalCraft.dll" && echo "LethalCraft.dll copied in"; fi
cd "$(dirname "$0")/.."
powershell -Command "Stop-Process -Name 'Lethal Company' -Force -ErrorAction SilentlyContinue"; sleep 2
G="O:\SteamLibrary\steamapps\common\Lethal Company"
WIN=$(cygpath -w "$PROF/BepInEx/core/BepInEx.Preloader.dll")
# (Steam friends status offline first, as in restart.sh)
powershell -Command "Start-Process 'steam://friends/status/offline'"; sleep 1
start() {
    powershell -Command "Start-Process -FilePath '$G\Lethal Company.exe' -WorkingDirectory '$G' -ArgumentList '--doorstop-enabled','true','--doorstop-target-assembly','\"$WIN\"','-lmc-mode','$1','-lmc-port','$2','-screen-fullscreen','0','-screen-width','1280','-screen-height','720'"
}
start lanhost 28771
for i in $(seq 1 60); do sleep 3; r=$(py -c "import sys;sys.path.insert(0,'tools');from dev import cmd;print(cmd('state',28771))" 2>/dev/null); if echo "$r" | grep -q "pos="; then echo "HOST: $r" | cut -c1-120; break; fi; done
start join 28772
for i in $(seq 1 70); do sleep 3; r=$(py -c "import sys;sys.path.insert(0,'tools');from dev import cmd;print(cmd('state',28772))" 2>/dev/null); if echo "$r" | grep -q "pos="; then echo "CLIENT: $r" | cut -c1-120; break; fi; done
grep -a -E "Exception|\[Error" "$PROF/BepInEx/LogOutput.log" | sort | uniq -c | sort -rn | head -10 | cut -c1-200
