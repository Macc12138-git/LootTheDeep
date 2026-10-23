"""Create shared raster layout guides and assemble four imagegen map sectors.

This helper only handles layout guides and local image composition. Image generation
is performed separately with the installed imagegen skill CLI.
"""

import argparse
from collections import Counter
import json
import math
import random
import zipfile
from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "output/imagegen/validation-map-v1"
GUIDES = OUT / "guides"
W, H = 1448, 1086
DIRT = (180, 148, 99)
ROAD = (124, 122, 121)
FLOOR = (145, 142, 145)
SECTORS = {"nw": (0, 0), "ne": (1, 0), "sw": (0, 1), "se": (1, 1)}


def road(draw, points, width=96):
    draw.line(points, fill=ROAD, width=width, joint="curve")
    radius = width // 2
    for x, y in points:
        draw.ellipse((x-radius, y-radius, x+radius, y+radius), fill=ROAD)


def wall(draw, a, b):
    draw.line([a, b], fill=(48, 47, 58), width=24)
    draw.line([(a[0], a[1]-4), (b[0], b[1]-4)], fill=(99, 99, 109), width=20)
    draw.line([(a[0], a[1]-11), (b[0], b[1]-11)], fill=(170, 166, 171), width=8)


def yard(draw, rect, gates=("south", "east"), enclosed=True):
    x0, y0, x1, y1 = rect
    draw.rectangle(rect, fill=FLOOR)
    for x in range(x0+80, x1, 80):
        draw.line([(x, y0+4), (x, y1-4)], fill=(134, 132, 136), width=2)
    for y in range(y0+80, y1, 80):
        draw.line([(x0+4, y), (x1-4, y)], fill=(134, 132, 136), width=2)
    if not enclosed:
        wall(draw, (x0, y0+80), (x0, y1-40))
        wall(draw, (x0, y1), (x0+150, y1))
        wall(draw, (x1-120, y0), (x1, y0))
        return
    cx, cy = (x0+x1)//2, (y0+y1)//2
    sides = {
        "north": [((x0, y0), (x1, y0))],
        "south": [((x0, y1), (x1, y1))],
        "west": [((x0, y0), (x0, y1))],
        "east": [((x1, y0), (x1, y1))],
    }
    for side in gates:
        if side in ("north", "south"):
            y = y0 if side == "north" else y1
            sides[side] = [((x0, y), (cx-48, y)), ((cx+48, y), (x1, y))]
        else:
            x = x0 if side == "west" else x1
            sides[side] = [((x, y0), (x, cy-48)), ((x, cy+48), (x, y1))]
    for segments in sides.values():
        for a, b in segments:
            wall(draw, a, b)


def container(draw, x, y, kind):
    colors = {"crate": (153, 110, 64), "red": (166, 64, 58),
              "olive": (110, 116, 73), "locker": (81, 105, 127),
              "special": (171, 134, 58)}
    width, height = (28, 52) if kind == "locker" else (40, 32)
    draw.rectangle((x-2, y-2, x+width+2, y+height+2), fill=(42, 42, 51))
    draw.rectangle((x, y, x+width, y+height), fill=colors[kind])
    draw.line([(x+3, y+5), (x+width-3, y+5)], fill=(190, 179, 153), width=3)
    draw.rectangle((x+width//2-3, y+height//2-2,
                    x+width//2+3, y+height//2+4), fill=(65, 66, 75))


def rock(draw, x, y, radius, seed):
    rng = random.Random(seed)
    points = []
    for n in range(8):
        angle = n * math.tau / 8
        r = radius * rng.uniform(.84, 1.08)
        points.append((round((x+math.cos(angle)*r)/2)*2,
                       round((y+math.sin(angle)*r)/2)*2))
    draw.polygon(points, fill=(76, 74, 83), outline=(43, 42, 51), width=3)
    draw.polygon([(x-radius//2, y-radius//3), (x, y-radius*3//4),
                  (x+radius*2//3, y-radius//4), (x+radius//4, y+radius//4),
                  (x-radius//2, y)], fill=(110, 106, 116))


def make_guide(name):
    col, row = SECTORS[name]
    im = Image.new("RGB", (W, H), DIRT)
    d = ImageDraw.Draw(im)
    # All four sectors use the same junction spacing and object scale.
    road(d, [(80, 590), (1360, 590)])
    road(d, [(350, 590), (350, 850), (1120, 850), (1120, 590)])
    if col == 0:
        road(d, [(W, 320), (1360, 320), (1360, 590)])
        road(d, [(W, 850), (1120, 850)])
    else:
        road(d, [(0, 320), (80, 320), (80, 590)])
        road(d, [(0, 850), (350, 850)])
    if row == 0:
        road(d, [(350, 850), (350, H)])
        road(d, [(1120, 850), (1120, H)])
    else:
        road(d, [(350, 0), (350, 80), (80, 80), (80, 590)])
        road(d, [(1120, 0), (1120, 80), (1360, 80), (1360, 590)])
    rooms = {
        "nw": [((170, 160, 620, 460), ("south", "east"), True),
               ((820, 160, 1240, 460), ("south", "west"), True),
               ((500, 720, 960, 1000), ("north", "east"), True)],
        "ne": [((170, 160, 1240, 460), ("south", "west"), True),
               ((500, 720, 960, 1000), ("north", "west"), True)],
        "sw": [((170, 160, 620, 460), ("south", "east"), False),
               ((820, 160, 1240, 460), ("south", "west"), True)],
        "se": [((200, 180, 650, 460), ("south", "east"), False)],
    }
    # Bottom courtyards sit south of the through-road, which runs above them.
    # Restore an alternate lane to each side without crossing the floor area.
    for rect, gates, enclosed in rooms[name]:
        yard(d, rect, gates, enclosed)
    if name == "ne":
        wall(d, (760, 160), (760, 270))
        wall(d, (760, 370), (760, 460))
    if name in ("nw", "ne"):
        road(d, [(350, 650), (1120, 650)], width=80)
        road(d, [(350, 850), (430, 850)], width=72)
        road(d, [(1040, 850), (1120, 850)], width=72)
    placements = {
        "nw": [(250, 220, "crate"), (470, 250, "red"), (250, 370, "locker"),
               (910, 220, "olive"), (1120, 350, "red"),
               (570, 780, "crate"), (830, 900, "locker")],
        "ne": [(250, 220, "locker"), (480, 350, "olive"),
               (910, 230, "red"), (1110, 340, "special"),
               (570, 780, "crate"), (820, 780, "olive"), (760, 920, "special")],
        "sw": [(250, 230, "crate"), (470, 370, "red"),
               (920, 230, "olive"), (1120, 350, "locker"), (760, 960, "crate")],
        "se": [(250, 230, "crate"), (480, 350, "red"),
               (1070, 310, "olive"), (650, 940, "locker"), (850, 950, "crate")],
    }
    for x, y, kind in placements[name]:
        container(d, x, y, kind)
    wall(d, (500, 535), (630, 535))
    wall(d, (850, 535), (980, 535))
    if row == 1:
        for i, (x, y, r) in enumerate([(550, 760, 52), (800, 710, 65),
                                       (950, 970, 48), (600, 1020, 40)]):
            rock(d, x, y, r, i+col*10)
    if name == "se":
        for i, (x, y, r) in enumerate([(900, 210, 70), (980, 230, 54),
                                       (1180, 430, 60), (850, 430, 45)]):
            rock(d, x, y, r, 100+i)
    # Outer boundary only: never enclose an interior shared sector edge.
    if row == 0:
        for x in range(0, W+1, 64):
            rock(d, x, 8, 54, x+col*W)
    else:
        for x in range(0, W+1, 64):
            rock(d, x, H-8, 54, x+col*W)
    if col == 0:
        for y in range(0, H+1, 64):
            if name == "sw" and 690 < y < 900:
                continue
            rock(d, 8, y, 54, y+row*H)
    else:
        for y in range(0, H+1, 64):
            if (name == "ne" and 230 < y < 410) or (name == "se" and 710 < y < 930):
                continue
            rock(d, W-8, y, 54, y+row*H)
    if name == "sw":
        road(d, [(0, 800), (140, 800), (350, 850)])
    if name == "ne":
        road(d, [(W, 320), (1360, 320), (1360, 590)])
    if name == "se":
        road(d, [(W, 820), (1120, 820)])
    return im


def make_guides():
    GUIDES.mkdir(parents=True, exist_ok=True)
    master = Image.new("RGB", (2*W, 2*H))
    for name, (col, row) in SECTORS.items():
        guide = make_guide(name)
        guide.save(GUIDES / f"{name}.png")
        master.paste(guide, (col*W, row*H))
    master.save(GUIDES / "layout.png")
    print(f"Guides: {GUIDES}; stitched canvas {2*W}x{2*H}")


def terrain_kind(pixel, guide_pixel):
    r, g, b = pixel
    if guide_pixel == DIRT and r > g+18 and g > b+18 and 100 < r < 225:
        return 0
    if guide_pixel == ROAD and max(pixel)-min(pixel) < 12 and 70 < g < 140:
        return 1
    return None


def normalize_sectors():
    prepared, anchors = {}, {}
    for name in SECTORS:
        raw = Image.open(OUT / f"{name}-raw.png").convert("RGB")
        original_size = raw.size
        if raw.size != (W, H):
            raw = raw.resize((W, H), Image.Resampling.NEAREST)
        guide = Image.open(GUIDES / f"{name}.png").convert("RGB")
        pixels = list(raw.get_flattened_data())
        guide_pixels = list(guide.get_flattened_data())
        counts = [Counter(), Counter()]
        for pixel, guide_pixel in zip(pixels, guide_pixels):
            kind = terrain_kind(pixel, guide_pixel)
            if kind is not None:
                counts[kind][pixel] += 1
        anchors[name] = [count.most_common(1)[0][0] for count in counts]
        prepared[name] = (raw, pixels, guide_pixels, original_size)
    normalized = {}
    for name, (raw, pixels, guide_pixels, original_size) in prepared.items():
        shifts = [[a-b for a, b in zip(anchors["nw"][kind], anchors[name][kind])]
                  for kind in range(2)]
        corrected = []
        for pixel, guide_pixel in zip(pixels, guide_pixels):
            kind = terrain_kind(pixel, guide_pixel)
            if kind is not None:
                pixel = tuple(max(0, min(255, c+d))
                              for c, d in zip(pixel, shifts[kind]))
            corrected.append(pixel)
        raw.putdata(corrected)
        normalized[name] = (raw, original_size)
    return normalized, anchors


def join_sector(name, raw, terrain_colors):
    guide = Image.open(GUIDES / f"{name}.png").convert("RGB")
    guide.putdata([terrain_colors[0] if p == DIRT else
                   terrain_colors[1] if p == ROAD else p
                   for p in guide.get_flattened_data()])
    col, row = SECTORS[name]
    # Harmonize a narrow connector band without introducing a bright blank stripe.
    # Keep the generated outer rock boundary instead of restoring blockout rocks.
    mask = Image.new("L", (W, H), 255)
    pixels = mask.load()
    for y in range(H):
        for x in range(W):
            if ((col == 0 and x < 64) or (col == 1 and x >= W-64)
                    or (row == 0 and y < 64) or (row == 1 and y >= H-64)):
                continue
            dx = W-1-x if col == 0 else x
            dy = H-1-y if row == 0 else y
            distance = min(dx, dy)
            if distance < 40:
                opacity = max(0, min(1, (distance-6)/34))
                pixels[x, y] = round(opacity*16)*255//16
    joined = Image.composite(raw, guide, mask)
    return joined


def assemble():
    master = Image.new("RGB", (W*2, H*2))
    chunks = OUT / "chunks"
    chunks.mkdir(exist_ok=True)
    records = []
    normalized, anchors = normalize_sectors()
    for name, (col, row) in SECTORS.items():
        raw, original_size = normalized[name]
        joined = join_sector(name, raw, anchors["nw"])
        joined.save(chunks / f"{name}.png")
        master.paste(joined, (col*W, row*H))
        records.append({"name": name, "source_size": original_size,
                        "chunk_size": [W, H], "pixel_origin_top_left": [col*W, row*H]})
    master.save(OUT / "complete.png")
    master.resize((W, H), Image.Resampling.NEAREST).save(OUT / "overview.png")
    (OUT / "assembly.json").write_text(json.dumps({
        "canvas_size": [W*2, H*2], "suggested_grid": [96, 72],
        "terrain_color_anchors_rgb": anchors,
        "chunks": records,
        "note": "AI image layout reference only; not a Unity scene or validated navigation grid."
    }, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    with zipfile.ZipFile(OUT / "validation-map-v1.zip", "w", zipfile.ZIP_DEFLATED) as archive:
        for path in [OUT / "complete.png", OUT / "overview.png", OUT / "assembly.json",
                     OUT / "README.md", *sorted(chunks.glob("*.png"))]:
            if not path.exists():
                continue
            archive.write(path, path.relative_to(OUT))
        for path in sorted((ROOT / "Tools/ImageGen/prompts/validation-map-v1").glob("*.txt")):
            archive.write(path, f"prompts/{path.name}")
    print(json.dumps({"canvas": master.size, "chunks": records}, ensure_ascii=False))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["guides", "assemble"])
    args = parser.parse_args()
    make_guides() if args.action == "guides" else assemble()
