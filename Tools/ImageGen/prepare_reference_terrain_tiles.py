"""Align and pack AI-generated opaque terrain tiles; verify matching edges."""

from pathlib import Path
from statistics import median
import json
import zipfile

from PIL import Image, ImageFilter


ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'output/imagegen/reference-terrain-tiles-v1'
SIZE = 64
WALL_COLOR = (31, 26, 35)
SPECS = [
    ('floor_01', 15), ('floor_02', 15), ('floor_03', 15), ('wall_fill', 0),
    ('edge_floor_n', 3), ('edge_floor_e', 6), ('edge_floor_s', 12), ('edge_floor_w', 9),
    ('outer_floor_nw', 1), ('outer_floor_ne', 2), ('outer_floor_se', 4), ('outer_floor_sw', 8),
    ('inner_wall_nw', 14), ('inner_wall_ne', 13), ('inner_wall_se', 11), ('inner_wall_sw', 7),
]


def is_floor(color):
    return color[0] > 48 and color[0] - color[2] >= 2


def corners(mask):
    return [bool(mask & bit) for bit in (1, 2, 4, 8)]


def boundary_position(tile, axis, coordinate, initial_floor):
    samples = []
    for position in range(SIZE):
        values = []
        for delta in range(-2, 3):
            point = (position, coordinate + delta) if axis == 'x' else (coordinate + delta, position)
            values.append(is_floor(tile.getpixel(point)))
        samples.append(sum(values) >= 3)
    return min(range(20, 45), key=lambda boundary: sum(
        actual != (initial_floor if position < boundary else not initial_floor)
        for position, actual in enumerate(samples)
    ))


def align_tile(tile, mask):
    nw, ne, se, sw = corners(mask)
    x_positions, y_positions = [], []
    for coord, first, second in ((4, nw, ne), (59, sw, se)):
        if first != second:
            x_positions.append(boundary_position(tile, 'x', coord, first))
    for coord, first, second in ((4, nw, sw), (59, ne, se)):
        if first != second:
            y_positions.append(boundary_position(tile, 'y', coord, first))
    x_boundary = round(median(x_positions)) if x_positions else 32
    y_boundary = round(median(y_positions)) if y_positions else 32

    def mapped(position, boundary):
        if position < 32:
            return min(63, int(position * boundary / 32))
        return min(63, int(boundary + (position - 32) * (64 - boundary) / 32))

    aligned = Image.new('RGB', (64, 64))
    floor_mask = Image.new('L', (64, 64))
    for y in range(SIZE):
        for x in range(SIZE):
            aligned.putpixel((x, y), tile.getpixel((mapped(x, x_boundary), mapped(y, y_boundary))))
            bit = 1 if x < 32 and y < 32 else 2 if y < 32 else 8 if x < 32 else 4
            if mask & bit:
                floor_mask.putpixel((x, y), 255)
    near_floor = floor_mask.filter(ImageFilter.MaxFilter(25))
    for y in range(SIZE):
        for x in range(SIZE):
            if near_floor.getpixel((x, y)) == 0:
                aligned.putpixel((x, y), WALL_COLOR)
    return aligned, {'source_boundary_x': x_boundary, 'source_boundary_y': y_boundary}


def periodic_floor(tile):
    tile = tile.copy()
    for y in range(SIZE):
        color = tuple(round((tile.getpixel((0, y))[c] + tile.getpixel((63, y))[c]) / 2) for c in range(3))
        for depth in range(5):
            amount = (1 - depth / 5) ** 2
            for x in (depth, 63 - depth):
                old = tile.getpixel((x, y))
                tile.putpixel((x, y), tuple(round(old[c] * (1 - amount) + color[c] * amount) for c in range(3)))
    for x in range(SIZE):
        color = tuple(round((tile.getpixel((x, 0))[c] + tile.getpixel((x, 63))[c]) / 2) for c in range(3))
        for depth in range(5):
            amount = (1 - depth / 5) ** 2
            for y in (depth, 63 - depth):
                old = tile.getpixel((x, y))
                tile.putpixel((x, y), tuple(round(old[c] * (1 - amount) + color[c] * amount) for c in range(3)))
    return tile


def edge(tile, side):
    if side == 'N':
        return [tile.getpixel((i, 0)) for i in range(SIZE)]
    if side == 'S':
        return [tile.getpixel((i, 63)) for i in range(SIZE)]
    if side == 'W':
        return [tile.getpixel((0, i)) for i in range(SIZE)]
    return [tile.getpixel((63, i)) for i in range(SIZE)]


def side_bits(mask, side):
    nw, ne, se, sw = corners(mask)
    return {'N': (nw, ne), 'E': (ne, se), 'S': (sw, se), 'W': (nw, sw)}[side]


def standardize_edges(tile, mask, profiles):
    tile = tile.copy()
    for side in 'NESW':
        profile = profiles[('h' if side in 'NS' else 'v', side_bits(mask, side))]
        for depth in range(5):
            amount = (1 - depth / 5) ** 2
            for i, color in enumerate(profile):
                pos = (i, depth) if side == 'N' else (i, 63 - depth) if side == 'S' else (depth, i) if side == 'W' else (63 - depth, i)
                old = tile.getpixel(pos)
                tile.putpixel(pos, tuple(round(old[c] * (1 - amount) + color[c] * amount) for c in range(3)))
    # The final pass preserves exact cross-sections at every cell boundary.
    for side in 'NESW':
        profile = profiles[('h' if side in 'NS' else 'v', side_bits(mask, side))]
        for i, color in enumerate(profile):
            pos = (i, 0) if side == 'N' else (i, 63) if side == 'S' else (0, i) if side == 'W' else (63, i)
            tile.putpixel(pos, color)
    return tile


def main():
    (OUT / 'tiles').mkdir(parents=True, exist_ok=True)
    source = Image.open(OUT / 'generated-source.png').convert('RGB').resize((256, 256), Image.Resampling.NEAREST)
    aligned, alignment = [], []
    for index, (name, mask) in enumerate(SPECS):
        x, y = index % 4 * 64, index // 4 * 64
        tile, offsets = align_tile(source.crop((x, y, x + 64, y + 64)), mask)
        aligned.append(tile)
        alignment.append(offsets)

    floor = periodic_floor(aligned[0])
    profiles = {}
    for axis, side in (('h', 'N'), ('v', 'W')):
        profiles[(axis, (True, True))] = edge(floor, side)
        profiles[(axis, (False, False))] = [WALL_COLOR] * SIZE
    mixed_sources = [('h', 7, 'N'), ('h', 5, 'N'), ('v', 4, 'W'), ('v', 6, 'W')]
    for axis, index, side in mixed_sources:
        bits = side_bits(SPECS[index][1], side)
        profile = edge(aligned[index], side)
        floor_profile = profiles[(axis, (True, True))]
        for position in range(SIZE):
            is_ground = bits[0] if position < 32 else bits[1]
            if is_ground:
                profile[position] = floor_profile[position]
        profile[0] = floor_profile[0] if bits[0] else WALL_COLOR
        profile[-1] = floor_profile[-1] if bits[1] else WALL_COLOR
        profiles[(axis, bits)] = profile

    atlas = Image.new('RGB', (256, 256))
    tiles, by_mask, metadata = {}, {}, []
    for index, ((name, mask), tile) in enumerate(zip(SPECS, aligned)):
        tile = standardize_edges(tile, mask, profiles)
        for side in 'NESW':
            expected = profiles[('h' if side in 'NS' else 'v', side_bits(mask, side))]
            assert edge(tile, side) == expected, (name, side)
        assert tile.convert('RGBA').getchannel('A').getextrema() == (255, 255)
        tile.save(OUT / 'tiles' / f'{name}.png')
        tiles[name] = tile
        by_mask.setdefault(mask, tile)
        atlas.paste(tile, (index % 4 * SIZE, index // 4 * SIZE))
        metadata.append({'name': name, 'row_from_top': index // 4, 'column': index % 4, 'floor_corner_mask': mask, **alignment[index]})
    atlas.save(OUT / 'terrain-atlas-64.png')
    atlas.resize((768, 768), Image.Resampling.NEAREST).save(OUT / 'atlas-preview.png')

    def ground_vertex(x, y):
        in_room = 2 <= x <= 14 and 2 <= y <= 10
        bottom_notch = 5 <= x <= 8 and 8 <= y <= 10
        solid_island = 10 <= x <= 11 and 5 <= y <= 6
        return in_room and not bottom_notch and not solid_island

    assembled = Image.new('RGB', (16 * SIZE, 12 * SIZE), WALL_COLOR)
    used = set()
    for y in range(12):
        for x in range(16):
            mask = sum(bit for bit, vx, vy in ((1, x, y), (2, x + 1, y), (4, x + 1, y + 1), (8, x, y + 1)) if ground_vertex(vx, vy))
            assert mask in by_mask, (x, y, mask)
            used.add(mask)
            tile = tiles[f'floor_0{(x * 3 + y * 5) % 3 + 1}'] if mask == 15 else by_mask[mask]
            assembled.paste(tile, (x * SIZE, y * SIZE))
    assembled.save(OUT / 'assembly-preview.png')
    info = {'tile_size': SIZE, 'atlas_size': [256, 256], 'columns': 4, 'rows': 4, 'opaque': True, 'tiles': metadata}
    (OUT / 'tiles.json').write_text(json.dumps(info, ensure_ascii=False, indent=2) + '\n')
    with zipfile.ZipFile(OUT / 'reference-terrain-tiles-v1.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
        for path in [OUT / 'terrain-atlas-64.png', OUT / 'tiles.json', OUT / 'README.md', *sorted((OUT / 'tiles').glob('*.png'))]:
            archive.write(path, path.relative_to(OUT))
    print('16 opaque 64x64 RGB tiles packed; all 64 cell edges match their terrain cross sections exactly.')
    print('Assembly preview uses', len(used), 'of the 14 distinct terrain masks.')


if __name__ == '__main__':
    main()
