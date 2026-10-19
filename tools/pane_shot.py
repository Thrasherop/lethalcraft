"""Glass panes in a row between two blocks, a lone one, and stained ones, by day: screenshot shots/pane_<tag>.png.
usage: py tools/pane_shot.py <tag>"""
import sys, os, time
sys.path.insert(0, os.path.dirname(__file__))
import regress as R
from regress import cmd
import thorough_play as T
tag = sys.argv[1]
fc = R.start_flat(67, [(dx, dz) for dx in range(-3, 4) for dz in range(0, 5)]); cmd("god 1"); cmd("time 10")
sy = max(R.surface(fc, dx, dz) for dx in range(-3, 4) for dz in range(0, 5)) + 1
for dx in range(-3, 4):
    for dz in range(0, 5): R.place("stone", fc, dx, sy, dz)
R.place("stone", fc, -3, sy + 1, 3); R.place("stone", fc, 1, sy + 1, 3)
for dx in (-2, -1, 0): R.place("glass_pane", fc, dx, sy + 1, 3, facing=2)
R.place("glass_pane", fc, 3, sy + 1, 3, facing=2)
for dx, c in ((-2, "light_blue"), (-1, "light_blue"), (0, "light_blue")): R.place(c + "_stained_glass_pane", fc, dx, sy + 2, 3, facing=2)
for dx in range(-3, 4):
    for dy in (1, 2, 3): R.place("stone", fc, dx, sy + dy, 4)
time.sleep(1.5)
R.stand_on(fc, -1, sy + 1, 0); time.sleep(0.6)
T.aim_cell(fc, -1, sy + 1, 3, face_y=1.0); time.sleep(0.6)
cmd(f"screenshot {os.path.join(os.path.dirname(__file__), '..', 'shots', 'pane_' + tag + '.png')}"); time.sleep(0.5)
print("ok")
