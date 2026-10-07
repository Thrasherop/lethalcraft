import sys, os, time; sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd
from PIL import Image
batches=[["grass","dirt","stone","cobblestone","oak_planks","oak_log","glass","sand"],
 ["gravel","bricks","tnt","piston","sticky_piston","redstone_block","redstone_lamp","glowstone"],
 ["obsidian","diamond_block","gold_block","iron_block","leaves","wool_white","wool_red","wool_blue"],
 ["wool_yellow","note_block","jack_o_lantern","ice","slime","bookshelf","stone_bricks","dark_planks"]]
def photo(name,x,y,z,yaw,pitch):
    out=f"E:/claude/mods/lethal_minecraft/shots/{name}.png"
    if os.path.exists(out): os.remove(out)
    cmd(f"photo {out} {x:.2f} {y:.2f} {z:.2f} {yaw} {pitch} 60")
    for _ in range(60):
        time.sleep(0.05)
        if os.path.exists(out) and os.path.getsize(out)>0: time.sleep(0.1); break
    return Image.open(out).resize((960,540))
rows=[]
for bi,batch in enumerate(batches):
    cmd("clearworld"); cmd("tp -68.55 -1.5 -14.77"); time.sleep(1)
    for i,b in enumerate(batch):
        cmd(f"place {b} {(i-4)*2} 0 6 {3 if b=='jack_o_lantern' else 1}")
    time.sleep(0.6)
    c=cmd("aim "+batch[4]).replace("ok ","").split(",")
    cx,cy,cz=float(c[0]),float(c[1]),float(c[2])
    cx-=1.4  # center of the row
    a=photo("_ab0",cx,cy+3,cz-7.5,0,18)
    b=photo("_ab1",cx,cy+3,cz+7.5,180,18)
    row=Image.new("RGB",(1920,540)); row.paste(a,(0,0)); row.paste(b,(960,0)); rows.append(row)
sheet=Image.new("RGB",(1920,540*len(rows)))
for i,r in enumerate(rows): sheet.paste(r,(0,i*540))
sheet.resize((1600,int(540*len(rows)*1600/1920))).save("E:/claude/mods/lethal_minecraft/shots/allblocks.png"); print("ok")
