"""Repro: TNT against the Company wall by the selling window, photos before/after. usage: py tools/company_tnt.py <tag> [x,y,z]"""
import sys, time, os
sys.path.insert(0, r'E:\claude\mods\lethal_minecraft\tools')
import regress as R
cmd = R.cmd
tag = sys.argv[1] if len(sys.argv) > 1 else "old"
S = 1.4
cell = tuple(int(v) for v in (sys.argv[2] if len(sys.argv) > 2 else "-20,-2,-31").split(","))
cx, cz = (cell[0] + 0.5) * S, (cell[2] + 0.5) * S
cam = (cx + 6.0, -0.3, cz + 1.5)
def photo(name):
    path = os.path.join(r"E:\claude\mods\lethal_minecraft\shots", f"cotnt_{tag}_{name}.png")
    cmd(f"photo {path} {cam[0]:.2f} {cam[1]:.2f} {cam[2]:.2f} 260 12"); time.sleep(1.6)
cmd("god 1"); cmd("photolight 1")
cmd(f"tp {cx + 7:.2f} 0 {cz + 3:.2f}"); time.sleep(1.0)
print(cmd(f"placeabs tnt {cell[0]} {cell[1]} {cell[2]} 1"))
time.sleep(0.5); photo("0_before")
s0 = cmd("groundstats"); print(s0)
print(cmd("ignite 20")); time.sleep(5.0)
photo("1_after")
print(cmd("groundstats"))
# which natural blocks exist, and where relative to the wall (x=-21 holds the wall surface; x>=-20 is platform air)
nb = R.near_blocks(cell)
nat = sorted(k for k, v in nb.items() if v[1] & 128)
print("natural blocks:", len(nat), "on the platform side (x>=-20, y>=-2):", [k for k in nat if k[0] >= -20 and k[1] >= -2])
