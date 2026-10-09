#!/bin/bash
# two-instance LAN test: host on 28771, client on 28772
cd "$(dirname "$0")/.."  # (the checkout this script is in: main, or a worktree)
powershell -Command "Stop-Process -Name 'Lethal Company' -Force -ErrorAction SilentlyContinue" ; sleep 2
export DOTNET_ROOT=/e/tools/dotnet DOTNET_CLI_HOME=/e/tools/dotnet_home NUGET_PACKAGES=/e/tools/nuget DOTNET_CLI_TELEMETRY_OPTOUT=1
out=$(/e/tools/dotnet/dotnet.exe build -c Release 2>&1)
if ! echo "$out" | grep -q "Build succeeded"; then echo "$out" | grep -E " error " | sed 's/\[E:.*//' | sort -u | head -30; exit 1; fi
echo "build ok"
G="O:\SteamLibrary\steamapps\common\Lethal Company"
# (Steam friends status offline first: launching the game shows friends you're playing, and Steam turns it back on by itself)
powershell -Command "Start-Process 'steam://friends/status/offline'"; sleep 1
powershell -Command "Start-Process -FilePath '$G\Lethal Company.exe' -WorkingDirectory '$G' -ArgumentList '-lmc-mode','lanhost','-lmc-port','28771','-screen-fullscreen','0','-screen-width','1280','-screen-height','720'"
for i in $(seq 1 40); do sleep 3; r=$(py -c "import sys;sys.path.insert(0,'tools');from dev import cmd;print(cmd('state',28771))" 2>/dev/null); if echo "$r" | grep -q "pos="; then echo "HOST: $r"; break; fi; done
powershell -Command "Start-Process -FilePath '$G\Lethal Company.exe' -WorkingDirectory '$G' -ArgumentList '-lmc-mode','join','-lmc-port','28772','-screen-fullscreen','0','-screen-width','1280','-screen-height','720'"
for i in $(seq 1 50); do sleep 3; r=$(py -c "import sys;sys.path.insert(0,'tools');from dev import cmd;print(cmd('state',28772))" 2>/dev/null); if echo "$r" | grep -q "pos="; then echo "CLIENT: $r"; break; fi; done
