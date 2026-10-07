#!/bin/bash
# like restart.sh, but builds and deploys the mod from another source tree (e.g. an old git worktree)
# usage: bash tools/restart_at.sh <dir>
dir="${1:-/e/claude/mods/lethal_minecraft}"
cd "$dir" || exit 1
powershell -Command "Stop-Process -Name 'Lethal Company' -Force -ErrorAction SilentlyContinue" ; sleep 2
export DOTNET_ROOT=/e/tools/dotnet DOTNET_CLI_HOME=/e/tools/dotnet_home NUGET_PACKAGES=/e/tools/nuget DOTNET_CLI_TELEMETRY_OPTOUT=1
out=$(/e/tools/dotnet/dotnet.exe build -c Release 2>&1)
if ! echo "$out" | grep -q "Build succeeded"; then echo "$out" | grep -E " error " | sed 's/\[E:.*//' | sort -u | head -30; exit 1; fi
echo "build ok ($dir)"
powershell -Command "Start-Process 'steam://rungameid/1966720'"
for i in $(seq 1 40); do sleep 3; r=$(py /e/claude/mods/lethal_minecraft/tools/dev.py state 2>/dev/null); if echo "$r" | grep -q "pos="; then echo "$r" | cut -c1-80; break; fi; done
