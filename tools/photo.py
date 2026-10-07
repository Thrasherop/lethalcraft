# photo.py name cx cy cz dist [port] -> 6-view orbit sheet rendered by free camera
import sys, os, time, math
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd
from PIL import Image
name=sys.argv[1]; cx,cy,cz=map(float,sys.argv[2:5]); dist=float(sys.argv[5]); port=int(sys.argv[6]) if len(sys.argv)>6 else 28771
views=[(0,20),(90,20),(180,20),(270,20),(45,60),(225,-25)]
tiles=[]
for i,(yaw,pitch) in enumerate(views):
    r=math.radians(yaw); hd=dist*math.cos(math.radians(pitch)); vd=dist*math.sin(math.radians(pitch))
    x,y,z=cx-math.sin(r)*hd, cy+vd, cz-math.cos(r)*hd
    out=f"E:/claude/mods/lethal_minecraft/shots/_p{i}.png"
    if os.path.exists(out): os.remove(out)
    cmd(f"photo {out} {x:.2f} {y:.2f} {z:.2f} {yaw} {pitch}",port)
    for _ in range(60):
        time.sleep(0.05)
        if os.path.exists(out) and os.path.getsize(out)>0: time.sleep(0.1); break
    tiles.append(Image.open(out).resize((640,360)))
c=Image.new("RGB",(640*3,360*2))
for i,t in enumerate(tiles): c.paste(t,((i%3)*640,(i//3)*360))
c.save(f"E:/claude/mods/lethal_minecraft/shots/{name}.png"); print("saved",name)
