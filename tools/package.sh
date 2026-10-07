#!/bin/bash
# build the release zip from the last Release build: bash tools/package.sh [copy-to-dir]
# (run tools/restart.sh / restart_at.sh first so bin/Release has the current DLL)
cd /e/claude/mods/lethal_minecraft || exit 1
ver=$(py -c "import json;print(json.load(open('dist/package/manifest.json'))['version_number'])")
cp README.md dist/package/README.md && cp bin/Release/netstandard2.1/LethalMinecraft.dll dist/package/plugins/LethalMinecraft/LethalMinecraft.dll || exit 1
out="dist/LethalMinecraft-$ver.zip"
py - "$out" <<'PY'
import zipfile, os, sys
root = r'E:\claude\mods\lethal_minecraft\dist\package'
out = sys.argv[1]
with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
    for d, _, files in os.walk(root):
        for f in files:
            full = os.path.join(d, f)
            z.write(full, os.path.relpath(full, root).replace(os.sep, '/'))
with zipfile.ZipFile(out) as z:
    for i in z.infolist(): print(f"  {i.filename} {i.file_size}")
PY
md5sum "$out"
if [ -n "$1" ]; then cp "$out" "$1/" && echo "copied to $1" && md5sum "$1/$(basename "$out")"; fi
