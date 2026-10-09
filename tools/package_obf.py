"""The one-off obfuscated playtest build: the same mod under another name ("CrewComforts"), with the Minecraft HUD hidden
until someone breaks a block (HideHudUntilBlockBroken defaults on in this build). Not for the main release.

usage: py tools/package_obf.py [copy-to-dir]
writes dist/CrewComforts-<ver>.zip (built with -p:Obfuscated=true, not copied into the game)"""
import json, os, shutil, subprocess, sys, zipfile
from PIL import Image, ImageDraw

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
NAME, VER = "CrewComforts", "1.0.0"
DESC = "A handful of small quality-of-life touches for the crew."
README = f"""# {NAME}

{DESC}

Install it like any other mod (it needs BepInEx, LethalLib and InputUtils). Everyone in the lobby should have it.
"""

def build(out_dir):
    env = dict(os.environ, DOTNET_ROOT=r"E:\tools\dotnet", DOTNET_CLI_HOME=r"E:\tools\dotnet_home",
               NUGET_PACKAGES=r"E:\tools\nuget", DOTNET_CLI_TELEMETRY_OPTOUT="1")
    r = subprocess.run([r"E:\tools\dotnet\dotnet.exe", "build", os.path.join(ROOT, "LethalMinecraft.csproj"), "-c", "Release",
                        "-p:Obfuscated=true", "-p:SkipCopy=true", "-o", out_dir], env=env, capture_output=True, text=True)
    if "Build succeeded" not in r.stdout:
        print(r.stdout[-3000:]); sys.exit("build failed")
    return os.path.join(out_dir, "LethalCraft.dll")

def icon(path):
    # plain and generic: a white plus on a teal tile
    im = Image.new("RGBA", (256, 256), (32, 92, 96, 255))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((28, 28, 228, 228), radius=36, fill=(44, 128, 132, 255))
    d.rectangle((108, 64, 148, 192), fill=(240, 244, 240, 255))
    d.rectangle((64, 108, 192, 148), fill=(240, 244, 240, 255))
    im.save(path)

def main():
    tmp = os.path.join(ROOT, "dist", "obf_build")
    pkg = os.path.join(ROOT, "dist", "obf_package")
    shutil.rmtree(pkg, ignore_errors=True)
    dll = build(tmp)
    os.makedirs(os.path.join(pkg, "plugins", NAME))
    shutil.copy(dll, os.path.join(pkg, "plugins", NAME, NAME + ".dll"))
    deps = json.load(open(os.path.join(ROOT, "dist", "package", "manifest.json")))["dependencies"]
    json.dump({"name": NAME, "version_number": VER, "website_url": "", "description": DESC, "dependencies": deps},
              open(os.path.join(pkg, "manifest.json"), "w"), indent=2)
    open(os.path.join(pkg, "README.md"), "w").write(README)
    open(os.path.join(pkg, "CHANGELOG.md"), "w").write(f"## {VER}\n- First release.\n")
    icon(os.path.join(pkg, "icon.png"))
    out = os.path.join(ROOT, "dist", f"{NAME}-{VER}.zip")
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for d, _, files in os.walk(pkg):
            for f in files:
                full = os.path.join(d, f)
                z.write(full, os.path.relpath(full, pkg).replace(os.sep, "/"))
    with zipfile.ZipFile(out) as z:
        assert z.testzip() is None
        for i in z.infolist(): print(f"  {i.filename} {i.file_size}")
    print("wrote", out)
    if len(sys.argv) > 1:
        dst = os.path.join(sys.argv[1], os.path.basename(out))
        shutil.copy(out, dst)
        with zipfile.ZipFile(dst) as z: assert z.testzip() is None
        print("copied to", dst)

if __name__ == "__main__":
    main()
