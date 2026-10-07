"""Visual check of a dug facility tunnel: finds a wall near a facility node, digs a 2-high tunnel through it,
puts a torch in it and photographs it from inside. usage: py tools/tunnel_photo.py [name]"""
import sys, os, time
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
cmd, S = R.cmd, R.S
SHOTS = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "shots"))
name = sys.argv[1] if len(sys.argv) > 1 else "tunnel"
cmd("photolight 1")
w = None
for node in range(3, 40, 2):
    cmd(f"tpnode {node}"); time.sleep(1.0); fc = R.feet_cell()
    w = R.find_wall(fc)
    if w: break
if not w: print("no wall found"); sys.exit(1)
k, dx, dz, yaw = w
res = [cmd(f"digabs {fc[0] + dx * j} {fc[1] + y} {fc[2] + dz * j} force").split(" (")[0] for j in range(1, k + 4) for y in (0, 1)]
print("node", node, "wall", w, res)
cmd(f"placeabs torch {fc[0] + dx * (k + 1)} {fc[1]} {fc[2] + dz * (k + 1)} 1"); time.sleep(0.8)
p = R.pos()
q = ((fc[0] + dx * (k - 1) + 0.5) * S, p[1] + 1.3, (fc[2] + dz * (k - 1) + 0.5) * S)
path = os.path.join(SHOTS, name + ".png")
cmd(f"photo {path} {q[0]:.2f} {q[1]:.2f} {q[2]:.2f} {yaw} 12"); time.sleep(1.5)
print(path)
# the blocks around the tunnel mouth: kind, shape (cube / mold + exposed-face mask)
lat = (dz, dx)  # sideways from the tunnel
for j in (0, 1, 2):
    for y in (-1, 0, 1):
        for s in (-1, 0, 1):
            c = (fc[0] + dx * j + lat[0] * s, fc[1] + y, fc[2] + dz * j + lat[1] * s)
            e = [x for x in cmd(f"near 30 {(c[0] + .5) * S:.2f} {(c[1] + .5) * S:.2f} {(c[2] + .5) * S:.2f}").split(" ; ") if x.strip().startswith(tuple("abcdefghijklmnopqrstuvwxyz")) and f"({c[0]}, {c[1]}, {c[2]})" in x]
            info = cmd(f"cellabs {c[0]} {c[1]} {c[2]}")
            print(f"j{j} y{y:+d} s{s:+d}", info.split(" gridYOff")[0], "facing=" + info.split("facing=")[1].split()[0], "dug=" + info.split("dug=")[1].split()[0], "|", e[0].strip() if e else "-")
q3 = ((fc[0] - dx * 1.2 + 0.5) * S + lat[0] * 1.2, p[1] + 0.6, (fc[2] - dz * 1.2 + 0.5) * S + lat[1] * 1.2)
cmd(f"photo {os.path.join(SHOTS, name + '_mouth.png')} {q3[0]:.2f} {q3[1]:.2f} {q3[2]:.2f} {yaw - 25} 15"); time.sleep(1.5)
