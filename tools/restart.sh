#!/bin/bash
# kill game, rebuild mod, relaunch with auto-host, wait until in the ship
cd /e/claude/mods/lethal_minecraft
powershell -Command "Stop-Process -Name 'Lethal Company' -Force -ErrorAction SilentlyContinue" ; sleep 2
export DOTNET_ROOT=/e/tools/dotnet DOTNET_CLI_HOME=/e/tools/dotnet_home NUGET_PACKAGES=/e/tools/nuget DOTNET_CLI_TELEMETRY_OPTOUT=1
out=$(/e/tools/dotnet/dotnet.exe build -c Release 2>&1)
if ! echo "$out" | grep -q "Build succeeded"; then echo "$out" | grep -E " error " | sed 's/\[E:.*//' | sort -u | head -30; exit 1; fi
echo "build ok"
powershell -Command "Start-Process 'steam://rungameid/1966720'"
for i in $(seq 1 40); do sleep 3; r=$(py tools/dev.py state 2>/dev/null); if echo "$r" | grep -q "pos="; then echo "$r"; break; fi; done
grep -E "Exception|\[Error" "/o/SteamLibrary/steamapps/common/Lethal Company/BepInEx/LogOutput.log" | head -10
