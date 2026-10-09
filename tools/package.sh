#!/bin/bash
# build the release zip from the last Release build: bash tools/package.sh [copy-to-dir]
# (run tools/restart.sh / restart_at.sh first so bin/Release has the current DLL)
cd /e/claude/mods/lethal_minecraft || exit 1
ver=$(py -c "import json;print(json.load(open('dist/package/manifest.json'))['version_number'])")
cp README.md dist/package/README.md && rm -rf dist/package/plugins/LethalMinecraft && mkdir -p dist/package/plugins/LethalCraft && cp bin/Release/netstandard2.1/LethalCraft.dll dist/package/plugins/LethalCraft/LethalCraft.dll || exit 1
out="dist/LethalCraft-$ver.zip"
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
if [ -n "$1" ]; then
  dst="$1/$(basename "$out")"
  rm -f "$dst"; cp "$out" "$1/" || exit 1
  # flush the drive's write cache (a USB stick pulled right after a plain copy got a corrupt zip), then check the copy
  drive=$(cd "$1" && pwd -W | cut -c1)
  powershell -Command "Write-VolumeCache -DriveLetter $drive" && echo "flushed $drive:"
  a=$(md5sum < "$out"); b=$(md5sum < "$dst")
  py -c "import zipfile,sys; sys.exit(zipfile.ZipFile(sys.argv[1]).testzip() is not None)" "$dst" && [ "$a" = "$b" ] && echo "copied to $1: verified ($a)" || { echo "COPY TO $1 IS BAD"; exit 1; }
fi
