import sys, time, os
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd
from PIL import Image
name = sys.argv[1] if len(sys.argv) > 1 else "v"
w = int(sys.argv[2]) if len(sys.argv) > 2 else 960
full = f"E:/claude/mods/lethal_minecraft/shots/{name}_full.png"
if os.path.exists(full): os.remove(full)
cmd(f"screenshot {full}")
for _ in range(50):
    time.sleep(0.1)
    if os.path.exists(full) and os.path.getsize(full) > 0:
        time.sleep(0.2); break
im = Image.open(full)
im = im.resize((w, int(im.height * w / im.width)), Image.LANCZOS)
im.save(f"E:/claude/mods/lethal_minecraft/shots/{name}.png")
print("saved", name)
