# orbit.py port name cx cy cz dist  -> 6 views (4 sides, high, low) around a point; player teleported each time
import sys, os, time, math
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd
from PIL import Image
port=int(sys.argv[1]); name=sys.argv[2]; cx,cy,cz=map(float,sys.argv[3:6]); dist=float(sys.argv[6])
views=[(0,15),(90,15),(180,15),(270,15),(45,55),(225,-10)]
tiles=[]
for i,(yaw,pitch) in enumerate(views):
    r=math.radians(yaw)
    # camera position: from the target, back along the view direction
    dx,dz=-math.sin(r),-math.cos(r)
    hd=dist*math.cos(math.radians(pitch)); vd=dist*math.sin(math.radians(pitch))
    camx,camy,camz=cx+dx*hd, cy+vd, cz+dz*hd
    cmd(f"tp {camx:.2f} {camy-2.3:.2f} {camz:.2f}",port); time.sleep(0.05)
    cmd(f"look {yaw} {pitch}",port); time.sleep(0.12)
    os.system(f"py {os.path.dirname(__file__)}/shotp.py {port} _o{i} 640 >nul")
    tiles.append(Image.open(f"E:/claude/mods/lethal_minecraft/shots/_o{i}.png"))
c=Image.new("RGB",(640*3,360*2))
for i,t in enumerate(tiles): c.paste(t,((i%3)*640,(i//3)*360))
c.save(f"E:/claude/mods/lethal_minecraft/shots/{name}.png"); print("saved",name)
