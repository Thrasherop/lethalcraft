import sys, os, time; sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd
from PIL import Image
batches=[["grass","dirt","stone","cobblestone","oak_planks","oak_log","glass","sand"],
 ["gravel","bricks","tnt","piston","sticky_piston","redstone_block","redstone_lamp","glowstone"],
 ["obsidian","diamond_block","gold_block","iron_block","leaves","wool_white","wool_red","wool_blue"],
 ["wool_yellow","note_block","jack_o_lantern","ice","slime","bookshelf","stone_bricks","dark_planks"]]
def shot(name):
    os.system(f"py {os.path.dirname(__file__)}/shot.py {name} 640 >nul")
    return Image.open(f"E:/claude/mods/lethal_minecraft/shots/{name}.png")
for bi,batch in enumerate(batches):
    cmd("clearworld"); cmd("tp -68.55 -1.5 -14.77"); time.sleep(1)
    for i,b in enumerate(batch):
        cmd(f"place {b} {(i-4)*2} 0 6 {3 if b=='jack_o_lantern' else 1}")
    time.sleep(0.5)
    ims=[]
    cmd("tp -68.55 -1.5 -14.77"); time.sleep(0.8); cmd("look 0 12"); time.sleep(0.3); ims.append(shot("_s0"))
    cmd("tp -68.55 -1.5 -2"); time.sleep(0.8); cmd("look 180 12"); time.sleep(0.3); ims.append(shot("_s1"))
    cmd("tp -68.55 6 -10"); time.sleep(0.12); cmd("look 0 50"); time.sleep(0.1); ims.append(shot("_s2"))
    c=Image.new("RGB",(640*3,360))
    for i,im in enumerate(ims): c.paste(im,(i*640,0))
    c.save(f"E:/claude/mods/lethal_minecraft/shots/batch{bi}.png"); print("batch",bi)
