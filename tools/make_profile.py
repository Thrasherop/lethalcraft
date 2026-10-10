"""Builds a separate BepInEx profile for testing LethalCraft with other mods (#57, #72, #73): a copy of the test game's
BepInEx (BepInEx, LethalCraft and its dependencies) plus Thunderstore packages, laid out the way r2modman does, in its
own folder. The game's own BepInEx is left alone; tools/launch_profile.sh starts the game with a profile.
usage: py tools/make_profile.py <profile dir> <package zip>...   (zips as downloaded from Thunderstore)"""
import sys, os, shutil, zipfile

GAME_BEPINEX = r"O:\SteamLibrary\steamapps\common\Lethal Company\BepInEx"

def install(zpath, bep):
    name = os.path.basename(zpath)[:-4]
    # (namespace-name, without the version)
    pkg = name.rsplit("-", 1)[0] if name.count("-") >= 2 else name
    with zipfile.ZipFile(zpath) as z:
        for info in z.infolist():
            if info.is_dir(): continue
            path = info.filename.replace("\\", "/")
            low = path.lower()
            if low in ("manifest.json", "readme.md", "changelog.md", "icon.png", "license", "license.md", "license.txt"): continue
            if low.startswith("bepinex/"): rel = path[len("BepInEx/"):]                      # BepInEx/plugins/x -> plugins/x
            elif low.startswith(("plugins/", "patchers/", "config/", "core/")):
                top, rest = path.split("/", 1)
                rel = f"{top}/{pkg}/{rest}" if top.lower() in ("plugins", "patchers") else path
            else: rel = f"plugins/{pkg}/{path}"                                                # loose files: the package's plugin folder
            dest = os.path.join(bep, rel.replace("/", os.sep))
            os.makedirs(os.path.dirname(dest), exist_ok=True)
            with z.open(info) as src, open(dest, "wb") as out: shutil.copyfileobj(src, out)
    print("installed", pkg)

if __name__ == "__main__":
    prof = sys.argv[1]
    bep = os.path.join(prof, "BepInEx")
    if not os.path.isdir(bep):
        shutil.copytree(GAME_BEPINEX, bep, ignore=shutil.ignore_patterns("LogOutput.log*", "cache"))
        print("copied the game's BepInEx into", bep)
    for z in sys.argv[2:]: install(z, bep)
