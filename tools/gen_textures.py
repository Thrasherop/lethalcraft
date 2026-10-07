"""
Procedural, original 16x16 pixel-art textures in a blocky voxel style.
Nothing here is copied from Minecraft - every tile is generated from noise/rules.

Outputs (Assets/generated):
  atlas.png        256x256 RGBA, 16x16 tiles, tile index = row*16+col (row 0 at TOP of image)
  atlas_emit.png   same layout, emission mask (black = no glow)
  icons/<name>.png 128x128 inventory icons (isometric block renders or item sprites)
  tiles.json       name -> tile index
"""
import json, math, os, random
from PIL import Image, ImageDraw
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "generated")
os.makedirs(os.path.join(OUT, "icons"), exist_ok=True)

T = 16
TILES = {}
atlas = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
emit = Image.new("RGBA", (256, 256), (0, 0, 0, 255))
next_index = [0]


def put(name, img, emission=None):
    idx = next_index[0]
    next_index[0] += 1
    TILES[name] = idx
    x, y = (idx % 16) * T, (idx // 16) * T
    atlas.paste(img, (x, y))
    if emission is not None:
        emit.paste(emission, (x, y))
    return idx


def clamp(v):
    return max(0, min(255, int(v)))


def shade(c, f):
    return tuple(clamp(ch * f) for ch in c[:3]) + ((c[3],) if len(c) == 4 else (255,))


def lerp(a, b, t):
    return tuple(clamp(a[i] + (b[i] - a[i]) * t) for i in range(3)) + (255,)


def new():
    return Image.new("RGBA", (T, T), (0, 0, 0, 0))


def noise_tile(rng, palette, weights=None):
    img = new()
    px = img.load()
    for y in range(T):
        for x in range(T):
            px[x, y] = rng.choices(palette, weights=weights)[0]
    return img


def value_noise(rng, scale=4):
    """Smooth-ish tileable noise in [0,1] (16x16)."""
    g = scale
    grid = [[rng.random() for _ in range(g)] for _ in range(g)]
    out = np.zeros((T, T))
    for y in range(T):
        for x in range(T):
            fx, fy = x / T * g, y / T * g
            x0, y0 = int(fx) % g, int(fy) % g
            x1, y1 = (x0 + 1) % g, (y0 + 1) % g
            tx, ty = fx - int(fx), fy - int(fy)
            tx = tx * tx * (3 - 2 * tx)
            ty = ty * ty * (3 - 2 * ty)
            a = grid[y0][x0] * (1 - tx) + grid[y0][x1] * tx
            b = grid[y1][x0] * (1 - tx) + grid[y1][x1] * tx
            out[y, x] = a * (1 - ty) + b * ty
    return out


# ---------------------------------------------------------------- palettes
DIRT = [(134, 96, 67), (121, 85, 58), (150, 108, 74), (108, 76, 52), (96, 68, 46), (160, 118, 82)]
DIRT_W = [30, 25, 15, 15, 8, 7]
GRASS = [(94, 157, 52), (106, 170, 60), (82, 141, 44), (120, 183, 70), (71, 125, 38)]
GRASS_W = [30, 22, 20, 12, 10]
STONE = [(125, 125, 125), (116, 116, 116), (134, 134, 134), (105, 105, 105), (143, 143, 143)]


def make_dirt(seed=1):
    rng = random.Random(seed)
    img = noise_tile(rng, DIRT, DIRT_W)
    px = img.load()
    # a few pebbles
    for _ in range(5):
        x, y = rng.randrange(T), rng.randrange(T)
        px[x, y] = (88, 62, 42, 255)
        if x + 1 < T:
            px[x + 1, y] = (168, 128, 92, 255)
    return img


def make_grass_top(seed=2):
    rng = random.Random(seed)
    img = noise_tile(rng, GRASS, GRASS_W)
    px = img.load()
    # little blade highlights
    for _ in range(14):
        x, y = rng.randrange(T), rng.randrange(T - 1)
        px[x, y] = (138, 196, 84, 255)
        px[x, y + 1] = (76, 130, 40, 255)
    return img


def make_grass_side(seed=3):
    rng = random.Random(seed)
    img = make_dirt(seed + 100)
    px = img.load()
    depth = [rng.choice([2, 3, 3, 4, 4, 5]) for _ in range(T)]
    # smooth a bit so the fringe looks like hanging grass
    for x in range(T):
        d = depth[x]
        for y in range(d):
            px[x, y] = rng.choices(GRASS, GRASS_W)[0]
        if d < T:
            px[x, d] = shade(px[x, d], 0.8)
    return img


def make_stone(seed=4):
    rng = random.Random(seed)
    n = value_noise(rng, 4)
    img = new()
    px = img.load()
    for y in range(T):
        for x in range(T):
            v = 112 + n[y, x] * 30 + rng.randint(-8, 8)
            px[x, y] = (clamp(v), clamp(v), clamp(v + 1), 255)
    # darker cracks/streaks
    for _ in range(4):
        x, y = rng.randrange(T), rng.randrange(T)
        for i in range(rng.randint(2, 4)):
            if 0 <= x < T and 0 <= y < T:
                px[x, y] = (92, 92, 94, 255)
            x += rng.choice([1, 1, 0])
            y += rng.choice([0, 1])
    return img


def voronoi_stones(rng, n_cells, base, mortar, light_f=1.18, dark_f=0.75, jitter=14):
    """Cells with outlines = cobblestone-like."""
    pts = [(rng.uniform(0, T), rng.uniform(0, T)) for _ in range(n_cells)]
    tones = [rng.uniform(0.85, 1.12) for _ in range(n_cells)]
    owner = np.zeros((T, T), dtype=int)
    for y in range(T):
        for x in range(T):
            best, bi = 1e9, 0
            for i, (cx, cy) in enumerate(pts):
                # tileable distance
                dx = min(abs(x + 0.5 - cx), T - abs(x + 0.5 - cx))
                dy = min(abs(y + 0.5 - cy), T - abs(y + 0.5 - cy))
                d = dx * dx + dy * dy
                if d < best:
                    best, bi = d, i
            owner[y, x] = bi
    img = new()
    px = img.load()
    for y in range(T):
        for x in range(T):
            o = owner[y, x]
            edge = owner[(y + 1) % T, x] != o or owner[y, (x + 1) % T] != o
            top_edge = owner[(y - 1) % T, x] != o or owner[y, (x - 1) % T] != o
            if edge:
                c = mortar
            else:
                c = shade(base, tones[o])
                if top_edge:
                    c = shade(c, light_f)
                elif owner[(y + 2) % T, x] != o or owner[y, (x + 2) % T] != o:
                    c = shade(c, dark_f + 0.15)
                v = rng.randint(-jitter // 2, jitter // 2)
                c = (clamp(c[0] + v), clamp(c[1] + v), clamp(c[2] + v), 255)
            px[x, y] = c
    return img


def make_cobble(seed=5):
    rng = random.Random(seed)
    return voronoi_stones(rng, 11, (128, 128, 128), (72, 72, 74, 255))


def make_planks(seed=6, base=(162, 130, 78)):
    rng = random.Random(seed)
    img = new()
    px = img.load()
    for y in range(T):
        board = y // 4
        yy = y % 4
        seam = (board * 7 + 3) % T
        for x in range(T):
            grain = 1.0 + 0.06 * math.sin((x + board * 5) * 0.9 + board) + rng.uniform(-0.05, 0.05)
            c = shade(base, grain)
            if yy == 3:
                c = shade(base, 0.62)  # gap between boards
            elif yy == 0:
                c = shade(c, 1.08)
            if x == seam and yy != 3:
                c = shade(base, 0.7)
            px[x, y] = c
        # grain streaks
        for _ in range(2):
            gx = rng.randrange(T)
            gy = board * 4 + rng.randrange(3)
            for i in range(rng.randint(2, 5)):
                if gx + i < T:
                    px[gx + i, gy] = shade(base, 0.86)
    return img


def make_log_side(seed=7):
    rng = random.Random(seed)
    base = (104, 82, 50)
    img = new()
    px = img.load()
    cols = [rng.uniform(0.8, 1.1) for _ in range(T)]
    for x in range(T):
        for y in range(T):
            f = cols[x] + rng.uniform(-0.06, 0.06)
            if x % 4 == 0:
                f *= 0.78
            px[x, y] = shade(base, f)
    for _ in range(6):
        x, y = rng.randrange(T), rng.randrange(T - 2)
        px[x, y] = shade(base, 0.6)
        px[x, y + 1] = shade(base, 0.6)
    return img


def make_log_top(seed=8):
    rng = random.Random(seed)
    img = new()
    px = img.load()
    for y in range(T):
        for x in range(T):
            d = math.hypot(x - 7.5, y - 7.5)
            if x in (0, T - 1) or y in (0, T - 1):
                c = (104, 82, 50, 255)
            else:
                ring = int(d * 1.15) % 2
                c = (182, 148, 92, 255) if ring == 0 else (156, 124, 74, 255)
                c = shade(c, 1 + rng.uniform(-0.04, 0.04))
            px[x, y] = c
    return img


def make_glass():
    img = new()
    px = img.load()
    frame = (210, 232, 236, 255)
    frame_d = (150, 186, 196, 255)
    for i in range(T):
        px[i, 0] = frame
        px[0, i] = frame
        px[i, T - 1] = frame_d
        px[T - 1, i] = frame_d
    # streaks
    for (sx, sy, ln) in [(3, 4, 3), (4, 3, 1), (10, 9, 4), (11, 8, 1), (5, 12, 2)]:
        for i in range(ln):
            if 0 < sx + i < T - 1 and 0 < sy - i < T - 1:
                px[sx + i, sy - i] = (236, 248, 250, 255)
    return img


def make_sand(seed=9):
    rng = random.Random(seed)
    pal = [(219, 207, 163), (210, 197, 152), (226, 216, 175), (199, 186, 141), (232, 223, 186)]
    return noise_tile(rng, pal, [30, 25, 20, 12, 13])


def make_gravel(seed=10):
    rng = random.Random(seed)
    return voronoi_stones(rng, 18, (128, 122, 120), (96, 90, 88, 255), 1.2, 0.8, 30)


def make_bricks(seed=11):
    rng = random.Random(seed)
    img = new()
    px = img.load()
    mortar = (168, 160, 152, 255)
    for y in range(T):
        row = y // 4
        for x in range(T):
            off = 0 if row % 2 == 0 else 4
            if y % 4 == 3 or (x + off) % 8 == 7:
                px[x, y] = shade(mortar, rng.uniform(0.92, 1.05))
            else:
                b = (150, 74, 58)
                f = rng.uniform(0.86, 1.08)
                if y % 4 == 0:
                    f *= 1.12
                px[x, y] = shade(b, f)
    return img


FONT = {  # 3x5 pixel font, original glyphs
    "T": ["###", ".#.", ".#.", ".#.", ".#."],
    "N": ["#..#", "##.#", "#.##", "#..#", "#..#"],
}


def make_tnt_side(seed=12):
    rng = random.Random(seed)
    img = new()
    px = img.load()
    for y in range(T):
        for x in range(T):
            red = (196, 52, 36)
            c = shade(red, rng.uniform(0.85, 1.05))
            if x % 4 == 3:
                c = shade(red, 0.7)
            if 5 <= y <= 10:
                c = shade((228, 228, 222), rng.uniform(0.93, 1.03))
            if y in (4, 11):
                c = (60, 40, 34, 255)
            px[x, y] = c
    # "TNT" text
    xs = 2
    for ch in "TNT":
        g = FONT[ch]
        for gy, line in enumerate(g):
            for gx, cc in enumerate(line):
                if cc == "#":
                    px[xs + gx, 5 + gy] = (24, 24, 24, 255)
        xs += len(g[0]) + 1
    return img


def make_tnt_top(seed=13):
    rng = random.Random(seed)
    img = new()
    px = img.load()
    for y in range(T):
        for x in range(T):
            c = shade((196, 56, 40), rng.uniform(0.85, 1.05))
            px[x, y] = c
    # fuse holes
    for (cx, cy) in [(4, 4), (11, 4), (4, 11), (11, 11), (7, 7)]:
        for dx in (0, 1):
            for dy in (0, 1):
                px[cx + dx, cy + dy] = (52, 40, 32, 255)
    px[7, 7] = (200, 200, 200, 255)
    return img


def make_tnt_bottom(seed=14):
    rng = random.Random(seed)
    img = new()
    px = img.load()
    for y in range(T):
        for x in range(T):
            px[x, y] = shade((176, 48, 36), rng.uniform(0.85, 1.0))
    return img


def make_piston_side(seed=15):
    rng = random.Random(seed)
    img = make_cobble(seed + 3)
    px = img.load()
    planks = make_planks(seed + 4)
    pp = planks.load()
    for y in range(4):
        for x in range(T):
            px[x, y] = pp[x, y]
    for x in range(T):
        px[x, 4] = (58, 58, 60, 255)
    # iron band
    for y in range(6, 11):
        for x in range(6, 10):
            px[x, y] = shade((150, 150, 150), 1.0 + rng.uniform(-0.05, 0.05)) if x in (7, 8) else (90, 90, 92, 255)
    return img


def make_piston_top(seed=16):
    img = make_planks(seed)
    px = img.load()
    for i in range(T):
        px[i, 0] = (110, 88, 52, 255)
        px[0, i] = (110, 88, 52, 255)
        px[i, T - 1] = (92, 72, 42, 255)
        px[T - 1, i] = (92, 72, 42, 255)
    for y in range(6, 10):
        for x in range(6, 10):
            px[x, y] = (128, 128, 130, 255)
    return img


def make_sticky_top(seed=17):
    rng = random.Random(seed)
    img = make_piston_top(seed)
    px = img.load()
    for y in range(2, 14):
        for x in range(2, 14):
            if (x - 7.5) ** 2 + (y - 7.5) ** 2 < 32 + rng.uniform(-6, 6):
                g = rng.choice([(108, 182, 82), (122, 196, 94), (92, 160, 70), (140, 210, 110)])
                px[x, y] = g + (255,)
    return img


def make_piston_bottom(seed=18):
    img = make_cobble(seed)
    px = img.load()
    for y in range(5, 11):
        for x in range(5, 11):
            px[x, y] = (68, 68, 70, 255)
    return img


def make_piston_inner(seed=19):
    img = make_cobble(seed)
    px = img.load()
    for y in range(5, 11):
        for x in range(5, 11):
            px[x, y] = (150, 150, 152, 255) if (x + y) % 2 else (128, 128, 130, 255)
    return img


def make_redstone_block(seed=20):
    rng = random.Random(seed)
    img = new()
    px = img.load()
    for y in range(T):
        for x in range(T):
            c = shade((176, 24, 12), rng.uniform(0.8, 1.15))
            if x in (0, T - 1) or y in (0, T - 1):
                c = shade((120, 12, 6), 1.0)
            px[x, y] = c
    for _ in range(14):
        x, y = rng.randrange(1, T - 1), rng.randrange(1, T - 1)
        px[x, y] = (255, 96, 70, 255)
    return img


def make_lamp(on, seed=21):
    rng = random.Random(seed)
    img = new()
    em = Image.new("RGBA", (T, T), (0, 0, 0, 255))
    px = img.load()
    ep = em.load()
    for y in range(T):
        for x in range(T):
            frame = x in (0, T - 1) or y in (0, T - 1) or x in (5, 10) or y in (5, 10)
            if frame:
                c = (98, 64, 40, 255) if not on else (130, 86, 50, 255)
            else:
                if on:
                    c = shade((246, 202, 128), rng.uniform(0.85, 1.08))
                    ep[x, y] = c
                else:
                    c = shade((120, 80, 52), rng.uniform(0.85, 1.1))
            px[x, y] = c
    return img, (em if on else None)


def make_glowstone(seed=22):
    rng = random.Random(seed)
    img = voronoi_stones(rng, 9, (220, 168, 88), (150, 108, 52, 255), 1.25, 0.8, 26)
    px = img.load()
    for _ in range(14):
        x, y = rng.randrange(T), rng.randrange(T)
        px[x, y] = (255, 240, 190, 255)
    return img, img.copy()


def make_obsidian(seed=23):
    rng = random.Random(seed)
    img = new()
    px = img.load()
    n = value_noise(rng, 4)
    for y in range(T):
        for x in range(T):
            v = n[y, x]
            c = lerp((16, 10, 26), (52, 34, 82), v)
            if rng.random() < 0.06:
                c = (92, 70, 140, 255)
            px[x, y] = c
    return img


def make_ore(spot_cols, seed, base_seed=4):
    rng = random.Random(seed)
    img = make_stone(base_seed + seed)
    px = img.load()
    for _ in range(5):
        cx, cy = rng.randrange(2, 13), rng.randrange(2, 13)
        shape = rng.choice([[(0, 0), (1, 0), (0, 1), (1, 1)], [(0, 0), (1, 0), (1, 1), (2, 1)], [(0, 0), (0, 1), (1, 1)]])
        for (dx, dy) in shape:
            col = rng.choice(spot_cols)
            px[cx + dx, cy + dy] = col + (255,)
        px[cx + shape[0][0], cy + shape[0][1]] = shade(spot_cols[0] + (255,), 1.25)
    return img


def make_solid(col, seed, outline=None, spark=None):
    rng = random.Random(seed)
    img = new()
    px = img.load()
    for y in range(T):
        for x in range(T):
            f = rng.uniform(0.92, 1.06)
            if y < 2 or x < 2:
                f *= 1.12
            if y > 13 or x > 13:
                f *= 0.86
            px[x, y] = shade(col, f)
    if outline:
        for i in range(T):
            px[i, 0] = outline
            px[0, i] = outline
            px[i, T - 1] = shade(outline, 0.6)
            px[T - 1, i] = shade(outline, 0.6)
    if spark:
        for _ in range(8):
            x, y = rng.randrange(2, 14), rng.randrange(2, 14)
            px[x, y] = spark + (255,)
    return img


def make_leaves(seed=24):
    rng = random.Random(seed)
    img = new()
    px = img.load()
    for y in range(T):
        for x in range(T):
            if rng.random() < 0.18:
                continue
            px[x, y] = rng.choice([(58, 120, 36), (70, 138, 44), (48, 102, 30), (82, 150, 54)]) + (255,)
    return img


def make_wool(col, seed):
    rng = random.Random(seed)
    img = new()
    px = img.load()
    for y in range(T):
        for x in range(T):
            f = rng.uniform(0.9, 1.05)
            if (x + y * 3) % 5 == 0:
                f *= 0.9
            px[x, y] = shade(col, f)
    return img


def make_crack(stage, seed=40):
    """Transparent crack overlay; stage 0..9 grows the crack network."""
    rng = random.Random(seed)
    img = new()
    px = img.load()
    # pre-generate crack walks then reveal progressively
    walks = []
    for k in range(10):
        x, y = rng.uniform(3, 12), rng.uniform(3, 12)
        pts = []
        ang = rng.uniform(0, math.tau)
        for i in range(12):
            pts.append((int(x) % T, int(y) % T))
            ang += rng.uniform(-0.9, 0.9)
            x += math.cos(ang)
            y += math.sin(ang)
        walks.append(pts)
    reveal = (stage + 1) / 10.0
    for k, pts in enumerate(walks):
        if k > stage:
            break
        n = max(2, int(len(pts) * min(1, reveal * 1.4)))
        for (x, y) in pts[:n]:
            px[x, y] = (20, 20, 20, 210)
    return img


def make_ladder(seed=25):
    img = new()
    px = img.load()
    wood = (140, 106, 64)
    for y in range(T):
        for x in (2, 3, 12, 13):
            px[x, y] = shade(wood, 0.85 if x in (3, 13) else 1.0)
        if y % 4 == 1:
            for x in range(2, 14):
                px[x, y] = shade(wood, 1.05)
            for x in range(4, 12):
                px[x, y + 1] = shade(wood, 0.75)
    return img


def make_redstone_dust(on):
    img = new()
    px = img.load()
    c1 = (255, 50, 30, 255) if on else (110, 10, 6, 255)
    c2 = (200, 20, 10, 255) if on else (80, 6, 4, 255)
    for i in range(T):
        for w in (7, 8):
            px[i, w] = c1 if i % 3 else c2
            px[w, i] = c1 if i % 3 else c2
    for (x, y) in [(5, 6), (10, 9), (6, 10), (9, 5), (3, 8), (12, 7)]:
        px[x, y] = c2
    em = None
    if on:
        em = Image.new("RGBA", (T, T), (0, 0, 0, 255))
        ep = em.load()
        for y in range(T):
            for x in range(T):
                if px[x, y][3] > 0:
                    ep[x, y] = (120, 10, 4, 255)
    return img, em


def make_note_block(seed=26):
    img = make_planks(seed, (120, 82, 58))
    px = img.load()
    # little speaker grill
    for y in range(4, 12):
        for x in range(4, 12):
            if (x + y) % 2 == 0:
                px[x, y] = (40, 28, 22, 255)
    return img


def make_crafting(seed=27):
    img = make_planks(seed)
    return img


def make_pumpkin_face(seed=28):
    rng = random.Random(seed)
    img = new()
    px = img.load()
    for y in range(T):
        for x in range(T):
            f = rng.uniform(0.9, 1.05) * (0.85 if x % 4 == 0 else 1)
            px[x, y] = shade((214, 120, 24), f)
    # carved face (original) + emission
    em = Image.new("RGBA", (T, T), (0, 0, 0, 255))
    ep = em.load()
    face = [(4, 5), (5, 5), (4, 6), (10, 5), (11, 5), (11, 6), (7, 8), (8, 8),
            (3, 10), (4, 11), (5, 11), (6, 11), (7, 11), (8, 11), (9, 11), (10, 11), (11, 11), (12, 10), (6, 10), (9, 10)]
    for (x, y) in face:
        px[x, y] = (255, 210, 90, 255)
        ep[x, y] = (255, 190, 60, 255)
    return img, em


def make_pumpkin_side(seed=29):
    rng = random.Random(seed)
    img = new()
    px = img.load()
    for y in range(T):
        for x in range(T):
            f = rng.uniform(0.9, 1.05) * (0.85 if x % 4 == 0 else 1)
            px[x, y] = shade((206, 114, 22), f)
    return img


def make_pumpkin_top(seed=30):
    img = make_pumpkin_side(seed)
    px = img.load()
    for y in range(6, 10):
        for x in range(7, 9):
            px[x, y] = (86, 112, 40, 255)
    return img


def make_ice(seed=31):
    rng = random.Random(seed)
    img = new()
    px = img.load()
    for y in range(T):
        for x in range(T):
            px[x, y] = shade((140, 182, 250), rng.uniform(0.9, 1.06))
    for (sx, sy) in [(3, 12), (9, 7), (12, 13)]:
        for i in range(4):
            if sx + i < T and sy - i >= 0:
                px[sx + i, sy - i] = (220, 236, 255, 255)
    return img


def make_slime(seed=32):
    rng = random.Random(seed)
    img = new()
    px = img.load()
    for y in range(T):
        for x in range(T):
            c = shade((112, 192, 90), rng.uniform(0.92, 1.05))
            if x in (0, T - 1) or y in (0, T - 1):
                c = (82, 150, 66, 255)
            elif 3 <= x <= 12 and 3 <= y <= 12 and (x in (3, 12) or y in (3, 12)):
                c = (92, 168, 74, 255)
            px[x, y] = c
    return img


def make_bookshelf(seed=33):
    rng = random.Random(seed)
    img = make_planks(seed)
    px = img.load()
    cols = [(150, 40, 40), (40, 70, 150), (60, 120, 50), (160, 130, 50), (110, 60, 140), (60, 60, 60)]
    for shelf_y in (1, 9):
        x = 1
        while x < T - 1:
            w = 1 if rng.random() < 0.7 else 2
            c = rng.choice(cols)
            h = rng.randint(5, 6)
            for dx in range(w):
                for dy in range(h):
                    if x + dx < T - 1:
                        px[x + dx, shelf_y + (6 - h) + dy] = shade(c, 1.1 if dy == 0 else 1)
            x += w
    return img


# ------------------------------------------------------------------ build
put("grass_top", make_grass_top())
put("grass_side", make_grass_side())
put("dirt", make_dirt())
put("stone", make_stone())
put("cobblestone", make_cobble())
put("oak_planks", make_planks())
put("log_side", make_log_side())
put("log_top", make_log_top())
put("glass", make_glass())
put("sand", make_sand())
put("gravel", make_gravel())
put("bricks", make_bricks())
put("tnt_side", make_tnt_side())
put("tnt_top", make_tnt_top())
put("tnt_bottom", make_tnt_bottom())
put("piston_side", make_piston_side())
put("piston_top", make_piston_top())
put("sticky_top", make_sticky_top())
put("piston_bottom", make_piston_bottom())
put("piston_inner", make_piston_inner())
put("redstone_block", make_redstone_block(), make_redstone_block())  # faint glow handled in material
img, em = make_lamp(False); put("lamp_off", img)
img, em = make_lamp(True); put("lamp_on", img, em)
img, em = make_glowstone(); put("glowstone", img, em)
put("obsidian", make_obsidian())
put("diamond_ore", make_ore([(92, 220, 214), (60, 190, 186), (170, 245, 240)], 50))
put("gold_ore", make_ore([(250, 210, 60), (220, 170, 30), (255, 240, 140)], 51))
put("iron_ore", make_ore([(216, 176, 148), (190, 150, 120), (236, 210, 190)], 52))
put("coal_ore", make_ore([(40, 40, 40), (24, 24, 24), (70, 70, 70)], 53))
put("emerald_ore", make_ore([(60, 210, 100), (30, 170, 70), (150, 250, 170)], 54))
put("diamond_block", make_solid((96, 222, 214), 60, (180, 250, 245, 255), (220, 255, 252)))
put("gold_block", make_solid((246, 206, 64), 61, (255, 240, 150, 255), (255, 250, 200)))
put("iron_block", make_solid((214, 214, 214), 62, (240, 240, 240, 255)))
put("leaves", make_leaves())
put("wool_white", make_wool((232, 232, 232), 70))
put("wool_red", make_wool((170, 44, 40), 71))
put("wool_blue", make_wool((52, 66, 160), 72))
put("wool_yellow", make_wool((230, 196, 50), 73))
put("ladder", make_ladder())
img, em = make_redstone_dust(False); put("dust_off", img)
img, em = make_redstone_dust(True); put("dust_on", img, em)
put("note_block", make_note_block())
img, em = make_pumpkin_face(); put("jack_face", img, em)
put("pumpkin_side", make_pumpkin_side())
put("pumpkin_top", make_pumpkin_top())
put("ice", make_ice())
put("slime", make_slime())
put("bookshelf", make_bookshelf())
put("oak_planks_dark", make_planks(80, (94, 66, 40)))
put("stone_bricks", voronoi_stones(random.Random(81), 1, (122, 122, 122), (80, 80, 82, 255)))
# Fix stone bricks: regular grid
sb = new(); p = sb.load(); r = random.Random(82)
for y in range(T):
    for x in range(T):
        row = y // 8
        off = 0 if row == 0 else 4
        if y % 8 == 7 or (x + off) % 8 == 7:
            p[x, y] = (78, 78, 80, 255)
        else:
            f = r.uniform(0.9, 1.06) * (1.12 if y % 8 == 0 else 1)
            p[x, y] = shade((124, 124, 124), f)
TILES.pop("stone_bricks"); next_index[0] -= 1
put("stone_bricks", sb)
for s in range(10):
    put(f"crack_{s}", make_crack(s))

def make_bedrock(seed=90):
    rng = random.Random(seed)
    img = voronoi_stones(rng, 14, (70, 70, 70), (30, 30, 30, 255), 1.3, 0.7, 40)
    px = img.load()
    for _ in range(18):
        x, y = rng.randrange(T), rng.randrange(T)
        px[x, y] = rng.choice([(20, 20, 20, 255), (120, 120, 120, 255)])
    return img

def make_snow(seed=91):
    rng = random.Random(seed)
    return noise_tile(rng, [(240, 248, 250), (230, 240, 245), (250, 252, 255), (220, 232, 240)], [40, 30, 20, 10])

put("bedrock", make_bedrock())
put("snow", make_snow())

# ----------------------------------------------------- item sprites (16x16)
def sprite_from(rows, pal):
    img = new()
    px = img.load()
    for y, line in enumerate(rows):
        for x, ch in enumerate(line):
            if ch != ".":
                px[x, y] = pal[ch]
    return img


PICKAXE = [
    "................",
    "...cccccc.......",
    "..cbbbbbbc......",
    ".cbaaaaaabc.....",
    "cbaa....cbbc....",
    "cba.....#cbc....",
    "cc.....#w.cbc...",
    "......#w...cc...",
    ".....#w.........",
    "....#w..........",
    "...#w...........",
    "..#w............",
    ".#w.............",
    "#w..............",
    "w...............",
    "................",
]
PICK_PAL = {"a": (210, 230, 240, 255), "b": (130, 200, 220, 255), "c": (40, 70, 90, 255),
            "#": (90, 62, 34, 255), "w": (140, 100, 56, 255)}

FLINT = [
    "................",
    "..........ss....",
    ".........sSSs...",
    "........sSSSs...",
    "........sSSs....",
    ".........ss.....",
    "................",
    "...ff...........",
    "..fFFf..........",
    "..fFFFf.........",
    "...fFFf.........",
    "....ff..........",
    "................",
    "................",
    "................",
    "................",
]
FLINT_PAL = {"s": (90, 90, 96, 255), "S": (200, 200, 210, 255), "f": (30, 30, 34, 255), "F": (80, 80, 86, 255)}

TORCH = [
    "................",
    "......yy........",
    ".....yOOy.......",
    ".....yOOy.......",
    "......ww........",
    "......ww........",
    "......ww........",
    "......ww........",
    "......ww........",
    "......ww........",
    "......ww........",
    "......ww........",
    "......ww........",
    "................",
    "................",
    "................",
]
TORCH_PAL = {"y": (255, 220, 90, 255), "O": (255, 250, 210, 255), "w": (130, 96, 54, 255)}

LEVER = [
    "................",
    "..........r.....",
    ".........rr.....",
    "........ww......",
    ".......ww.......",
    "......ww........",
    ".....ww.........",
    "....ssssss......",
    "...sSSSSSSs.....",
    "...sSSSSSSs.....",
    "...ssssssss.....",
    "................",
    "................",
    "................",
    "................",
    "................",
]
LEVER_PAL = {"r": (90, 70, 40, 255), "w": (140, 100, 56, 255), "s": (70, 70, 72, 255), "S": (130, 130, 132, 255)}

DUST_ITEM = [
    "................",
    "................",
    "......r.........",
    ".....rRr..r.....",
    "....rRRRrrRr....",
    "...rRRRRRRRr....",
    "..rRRRRRRRRRr...",
    "..rRRRRRRRRRRr..",
    "...rrRRRRRRrr...",
    ".....rrrrrr.....",
    "................",
    "................",
    "................",
    "................",
    "................",
    "................",
]
DUST_PAL = {"r": (120, 10, 6, 255), "R": (230, 40, 24, 255)}

BUTTON = [
    "................",
    "................",
    "................",
    "................",
    "................",
    ".....bbbbbb.....",
    "....bBBBBBBb....",
    "....bBBBBBBb....",
    "....bbbbbbbb....",
    "................",
    "................",
    "................",
    "................",
    "................",
    "................",
    "................",
]
BUTTON_PAL = {"b": (80, 80, 82, 255), "B": (150, 150, 152, 255)}

PLATE = [
    "................",
    "................",
    "................",
    "................",
    "................",
    "................",
    "................",
    "................",
    "...pppppppppp...",
    "..pPPPPPPPPPPp..",
    "..pppppppppppp..",
    "................",
    "................",
    "................",
    "................",
    "................",
]
PLATE_PAL = {"p": (80, 80, 82, 255), "P": (150, 150, 152, 255)}

sprites = {
    "pickaxe": sprite_from(PICKAXE, PICK_PAL),
    "flint_and_steel": sprite_from(FLINT, FLINT_PAL),
    "torch": sprite_from(TORCH, TORCH_PAL),
    "lever": sprite_from(LEVER, LEVER_PAL),
    "redstone_dust": sprite_from(DUST_ITEM, DUST_PAL),
    "button": sprite_from(BUTTON, BUTTON_PAL),
    "pressure_plate": sprite_from(PLATE, PLATE_PAL),
}
for name, spr in sprites.items():
    put("item_" + name, spr)


# ---------------------------------------------------------------- crafting & tools (fallback art)
def make_crafting_top(seed=95):
    img = make_planks(seed)
    px = img.load()
    for i in range(T):
        for e in (0, 15):
            px[i, e] = (90, 62, 34, 255); px[e, i] = (90, 62, 34, 255)
    for i in range(2, 14):
        px[i, 7] = (110, 80, 44, 255); px[7, i] = (110, 80, 44, 255)
    return img

def make_crafting_side(seed=96, tools=False):
    img = make_planks(seed)
    px = img.load()
    for x in range(T):
        for y in range(3):
            px[x, y] = (120, 86, 50, 255)
    if tools:
        for y in range(5, 13):
            px[4, y] = (110, 80, 44, 255)      # saw handle
            px[11, y] = (130, 130, 130, 255)   # hammer
        for x in range(9, 14):
            px[x, 5] = (150, 150, 150, 255)
    return img

def make_furnace(face, seed=97, lit=False):
    img = make_cobble(seed)
    px = img.load()
    if face == "front":
        for y in range(8, 14):
            for x in range(4, 12):
                if lit:
                    px[x, y] = (255, 170 - (13 - y) * 12, 40, 255) if (x + y) % 3 else (255, 230, 120, 255)
                else:
                    px[x, y] = (30, 30, 30, 255)
        for x in range(3, 13):
            px[x, 7] = (60, 60, 60, 255)
    if face == "top":
        for y in range(5, 11):
            for x in range(5, 11):
                px[x, y] = (90, 90, 90, 255)
    return img

TOOL_PICK = [
    "................",
    "...hhhhhh.......",
    "..hHHHHHHh......",
    ".hHh....hHh.....",
    "hHh......#Hh....",
    "hh......#..hh...",
    ".......#........",
    "......#.........",
    ".....#..........",
    "....#...........",
    "...#............",
    "..#.............",
    ".#..............",
    "#...............",
    "................",
    "................",
]
TOOL_SHOVEL = [
    "................",
    "...........hhh..",
    "..........hHHHh.",
    ".........hHHHHh.",
    "........hHHHHh..",
    ".......##hHHh...",
    "......#...hh....",
    ".....#..........",
    "....#...........",
    "...#............",
    "..#.............",
    ".#..............",
    "#...............",
    "................",
    "................",
    "................",
]
TOOL_AXE = [
    "................",
    "........hhh.....",
    ".......hHHHh....",
    "......hHHHHH#...",
    "......hHHH#.....",
    ".......hh#......",
    "........#.......",
    ".......#........",
    "......#.........",
    ".....#..........",
    "....#...........",
    "...#............",
    "..#.............",
    ".#..............",
    "................",
    "................",
]
TIERS = {"wooden": ((96, 70, 40), (168, 132, 82)), "stone": ((80, 80, 80), (150, 150, 150)), "iron": ((110, 110, 115), (225, 225, 225)), "diamond": ((40, 120, 120), (130, 235, 225))}
def tool_sprite(rows, tier):
    dark, light = TIERS[tier]
    return sprite_from(rows, {"h": dark + (255,), "H": light + (255,), "#": (120, 88, 48, 255)})

STICK = ["................"] * 16
STICK = [("." * (14 - i)) + "#w" + ("." * i) if 1 <= i <= 13 else "................" for i in range(16)]
def coal_sprite(seed=98):
    rng = random.Random(seed)
    img = new(); px = img.load()
    for y in range(4, 13):
        for x in range(3, 13):
            if (x - 7.5) ** 2 / 25 + (y - 8.5) ** 2 / 20 < 1:
                px[x, y] = rng.choice([(30, 30, 30, 255), (45, 45, 45, 255), (20, 20, 20, 255), (70, 70, 70, 255)])
    return img
def ingot_sprite(base):
    img = new(); px = img.load()
    for y in range(6, 12):
        for x in range(2 + (11 - y) // 2, 14 - (11 - y) // 2):
            f = 1.15 if y == 6 else (0.75 if y == 11 else 1.0)
            px[x, y] = shade(base + (255,), f)
    return img

put("crafting_table_top", make_crafting_top())
put("crafting_table_front", make_crafting_side(tools=True))
put("crafting_table_side", make_crafting_side(97))
put("furnace_front", make_furnace("front"))
put("furnace_front_on", make_furnace("front", lit=True))
put("furnace_side", make_furnace("side", 99))
put("furnace_top", make_furnace("top", 100))
for tier in ("wooden", "stone", "iron"):
    put(f"item_{tier}_pickaxe", tool_sprite(TOOL_PICK, tier))
for tier in ("wooden", "stone", "iron", "diamond"):
    put(f"item_{tier}_shovel", tool_sprite(TOOL_SHOVEL, tier))
    put(f"item_{tier}_axe", tool_sprite(TOOL_AXE, tier))
put("item_stick", sprite_from(STICK, {"#": (90, 62, 34, 255), "w": (140, 100, 56, 255)}))
put("item_coal", coal_sprite())
put("item_iron_ingot", ingot_sprite((215, 215, 215)))
put("item_gold_ingot", ingot_sprite((245, 205, 60)))

def pearl_sprite():
    """dark teal sphere with a lighter core, like Minecraft's ender pearl"""
    img = new(); px = img.load()
    for y in range(3, 13):
        for x in range(3, 13):
            d = ((x - 7.5) ** 2 + (y - 7.5) ** 2) ** 0.5
            if d < 5.0:
                if d < 1.6: c = (120, 220, 200, 255)
                elif d < 3.0: c = (30, 140, 125, 255)
                elif d < 4.2: c = (14, 85, 80, 255)
                else: c = (8, 45, 45, 255)
                if (x, y) in ((6, 5), (5, 6)): c = (190, 250, 235, 255)
                px[x, y] = c
    return img
put("item_ender_pearl", pearl_sprite())

atlas.save(os.path.join(OUT, "atlas.png"))
emit.save(os.path.join(OUT, "atlas_emit.png"))


# ------------------------------------------------------------------- icons
def tile(name):
    i = TILES[name]
    return atlas.crop(((i % 16) * T, (i // 16) * T, (i % 16) * T + T, (i // 16) * T + T))


def iso_icon(top, left, right, size=128):
    """Render an isometric cube from three 16x16 faces using affine transforms."""
    S = size
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    scale = 4  # 16px -> 64px faces
    t = tile(top).resize((T * scale, T * scale), Image.NEAREST)
    l = tile(left).resize((T * scale, T * scale), Image.NEAREST)
    r = tile(right).resize((T * scale, T * scale), Image.NEAREST)
    cx = S / 2
    w = 55.0  # half width of the cube
    h = w * 0.5  # top diamond half height
    top_y = 8.0
    side_h = 62.0

    def warp(src, quad, shade_f):
        # quad: target points for src corners (tl, tr, br, bl) -> use PIL's QUAD (inverse)
        src = Image.eval(src, lambda v: v)
        if shade_f != 1.0:
            a = np.array(src).astype(float)
            a[..., :3] *= shade_f
            src = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))
        # Build via perspective coefficients mapping output -> input
        (x0, y0), (x1, y1), (x2, y2), (x3, y3) = quad
        n = src.size[0]
        coeffs = find_coeffs([(x0, y0), (x1, y1), (x2, y2), (x3, y3)], [(0, 0), (n, 0), (n, n), (0, n)])
        warped = src.transform((S, S), Image.PERSPECTIVE, coeffs, Image.NEAREST)
        out.alpha_composite(warped)

    top_quad = [(cx, top_y), (cx + w, top_y + h), (cx, top_y + 2 * h), (cx - w, top_y + h)]
    left_quad = [(cx - w, top_y + h), (cx, top_y + 2 * h), (cx, top_y + 2 * h + side_h), (cx - w, top_y + h + side_h)]
    right_quad = [(cx, top_y + 2 * h), (cx + w, top_y + h), (cx + w, top_y + h + side_h), (cx, top_y + 2 * h + side_h)]
    warp(l, left_quad, 0.72)
    warp(r, right_quad, 0.86)
    warp(t, top_quad, 1.0)
    return out


def find_coeffs(pa, pb):
    matrix = []
    for p1, p2 in zip(pa, pb):
        matrix.append([p1[0], p1[1], 1, 0, 0, 0, -p2[0] * p1[0], -p2[0] * p1[1]])
        matrix.append([0, 0, 0, p1[0], p1[1], 1, -p2[1] * p1[0], -p2[1] * p1[1]])
    A = np.matrix(matrix, dtype=float)
    B = np.array(pb).reshape(8)
    res = np.dot(np.linalg.inv(A.T * A) * A.T, B)
    return np.array(res).reshape(8)


ICONS = {
    "grass": ("grass_top", "grass_side", "grass_side"),
    "dirt": ("dirt", "dirt", "dirt"),
    "stone": ("stone", "stone", "stone"),
    "cobblestone": ("cobblestone", "cobblestone", "cobblestone"),
    "oak_planks": ("oak_planks", "oak_planks", "oak_planks"),
    "oak_log": ("log_top", "log_side", "log_side"),
    "glass": ("glass", "glass", "glass"),
    "sand": ("sand", "sand", "sand"),
    "gravel": ("gravel", "gravel", "gravel"),
    "bricks": ("bricks", "bricks", "bricks"),
    "tnt": ("tnt_top", "tnt_side", "tnt_side"),
    "piston": ("piston_top", "piston_side", "piston_side"),
    "sticky_piston": ("sticky_top", "piston_side", "piston_side"),
    "redstone_block": ("redstone_block", "redstone_block", "redstone_block"),
    "redstone_lamp": ("lamp_on", "lamp_on", "lamp_on"),
    "glowstone": ("glowstone", "glowstone", "glowstone"),
    "obsidian": ("obsidian", "obsidian", "obsidian"),
    "diamond_ore": ("diamond_ore", "diamond_ore", "diamond_ore"),
    "gold_ore": ("gold_ore", "gold_ore", "gold_ore"),
    "iron_ore": ("iron_ore", "iron_ore", "iron_ore"),
    "coal_ore": ("coal_ore", "coal_ore", "coal_ore"),
    "emerald_ore": ("emerald_ore", "emerald_ore", "emerald_ore"),
    "diamond_block": ("diamond_block", "diamond_block", "diamond_block"),
    "gold_block": ("gold_block", "gold_block", "gold_block"),
    "iron_block": ("iron_block", "iron_block", "iron_block"),
    "leaves": ("leaves", "leaves", "leaves"),
    "wool_white": ("wool_white", "wool_white", "wool_white"),
    "wool_red": ("wool_red", "wool_red", "wool_red"),
    "wool_blue": ("wool_blue", "wool_blue", "wool_blue"),
    "wool_yellow": ("wool_yellow", "wool_yellow", "wool_yellow"),
    "note_block": ("note_block", "note_block", "note_block"),
    "jack_o_lantern": ("pumpkin_top", "jack_face", "pumpkin_side"),
    "ice": ("ice", "ice", "ice"),
    "slime": ("slime", "slime", "slime"),
    "bookshelf": ("oak_planks", "bookshelf", "bookshelf"),
    "stone_bricks": ("stone_bricks", "stone_bricks", "stone_bricks"),
    "dark_planks": ("oak_planks_dark", "oak_planks_dark", "oak_planks_dark"),
    "ladder": ("ladder", "ladder", "ladder"),
    "bedrock": ("bedrock", "bedrock", "bedrock"),
    "crafting_table": ("crafting_table_top", "crafting_table_front", "crafting_table_side"),
    "furnace": ("furnace_top", "furnace_front", "furnace_side"),
    "snow_block": ("snow", "snow", "snow"),
}
for name, (a, b, c) in ICONS.items():
    iso_icon(a, b, c).save(os.path.join(OUT, "icons", name + ".png"))
for name, spr in sprites.items():
    big = spr.resize((112, 112), Image.NEAREST)
    canvas = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    canvas.alpha_composite(big, (8, 8))
    canvas.save(os.path.join(OUT, "icons", name + ".png"))

with open(os.path.join(OUT, "tiles.json"), "w") as f:
    json.dump(TILES, f, indent=1)

# preview sheet (for humans)
prev = atlas.resize((1024, 1024), Image.NEAREST)
bg = Image.new("RGBA", prev.size, (40, 40, 48, 255))
bg.alpha_composite(prev)
bg.save(os.path.join(OUT, "_atlas_preview.png"))
print("tiles:", len(TILES))
