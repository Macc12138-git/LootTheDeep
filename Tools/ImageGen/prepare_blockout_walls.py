"""Slice AI-generated master sprites, align their ports, and pack 64px tiles."""

from pathlib import Path
import json
import zipfile

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'output/imagegen/blockout-walls-v1'
TILE_SIZE = 64
LOW, HIGH = 20, 44


def rotated(image, clockwise):
    return image.rotate(-clockwise, resample=Image.Resampling.NEAREST)


def blend_ports(tile, ports, horizontal_profile, vertical_profile):
    tile = tile.copy()
    for side in ports:
        profile = horizontal_profile if side in 'EW' else vertical_profile
        for depth in range(6):
            amount = (1 - depth / 6) ** 2
            for index in range(TILE_SIZE):
                if side == 'N':
                    pos = (index, depth)
                elif side == 'S':
                    pos = (index, TILE_SIZE - 1 - depth)
                elif side == 'E':
                    pos = (TILE_SIZE - 1 - depth, index)
                else:
                    pos = (depth, index)
                old, target = tile.getpixel(pos), profile[index]
                if target[3]:
                    pixel = tuple(round(old[c] * (1 - amount) + target[c] * amount) for c in range(3)) + (255,)
                else:
                    pixel = (0, 0, 0, 0)
                tile.putpixel(pos, pixel)
    return tile


def edge(tile, side):
    if side == 'N':
        return [tile.getpixel((i, 0)) for i in range(TILE_SIZE)]
    if side == 'S':
        return [tile.getpixel((i, TILE_SIZE - 1)) for i in range(TILE_SIZE)]
    if side == 'E':
        return [tile.getpixel((TILE_SIZE - 1, i)) for i in range(TILE_SIZE)]
    return [tile.getpixel((0, i)) for i in range(TILE_SIZE)]


def main():
    (OUT / 'tiles').mkdir(parents=True, exist_ok=True)
    source = Image.open(OUT / 'master-sprites.png').convert('RGBA')
    horizontal = source.crop((0, 0, 64, 64))
    corner = source.crop((64, 0, 128, 64))
    end = source.crop((128, 0, 192, 64))

    # The generated north-facing shapes are 23px wide; normalize to 24px.
    end_bounds = end.getbbox()
    normalized_end = Image.new('RGBA', (64, 64))
    normalized_end.paste(end.crop(end_bounds).resize((24, end_bounds[3]), Image.Resampling.NEAREST), (LOW, 0))
    end = normalized_end
    for y in range(LOW):
        corner.putpixel((HIGH - 1, y), corner.getpixel((HIGH - 2, y)))

    horizontal_profile = [horizontal.getpixel((32, y)) for y in range(64)]
    vertical_profile = [end.getpixel((x, 3)) for x in range(64)]
    vertical = Image.new('RGBA', (64, 64))
    # Repeat a generated vertical cross section without adding new artwork.
    for y in range(64):
        for x in range(LOW, HIGH):
            vertical.putpixel((x, y), vertical_profile[x])

    specs = [
        ('straight_ew', 'EW', horizontal, '横向直墙'),
        ('straight_ns', 'NS', vertical, '纵向直墙'),
        ('corner_ne', 'NE', corner, '上右转角'),
        ('corner_es', 'ES', rotated(corner, 90), '右下转角'),
        ('corner_sw', 'SW', rotated(corner, 180), '下左转角'),
        ('corner_wn', 'WN', rotated(corner, 270), '左上转角'),
        ('end_n', 'N', end, '向上连接的收口'),
        ('end_e', 'E', rotated(end, 90), '向右连接的收口'),
        ('end_s', 'S', rotated(end, 180), '向下连接的收口'),
        ('end_w', 'W', rotated(end, 270), '向左连接的收口'),
    ]
    atlas = Image.new('RGBA', (320, 128))
    tiles, manifest = {}, []
    for index, (name, ports, sprite, label) in enumerate(specs):
        tile = blend_ports(sprite, ports, horizontal_profile, vertical_profile)
        tile.save(OUT / 'tiles' / f'{name}.png')
        for side in 'NESW':
            actual = edge(tile, side)
            if side in ports:
                expected = horizontal_profile if side in 'EW' else vertical_profile
                assert actual == expected, (name, side, 'connection profile mismatch')
                assert [i for i, color in enumerate(actual) if color[3]] == list(range(LOW, HIGH))
            else:
                assert all(color[3] == 0 for color in actual), (name, side, 'unexpected connection')
        tiles[name] = tile
        x, y = index % 5 * 64, index // 5 * 64
        atlas.paste(tile, (x, y))
        manifest.append({'name': name, 'label': label, 'row_from_top': index // 5, 'column': index % 5, 'connections': list(ports)})
    atlas.save(OUT / 'walls-atlas-64.png')

    preview = Image.new('RGBA', atlas.size, (176, 176, 176, 255))
    draw = ImageDraw.Draw(preview)
    for y in range(0, 128, 8):
        for x in range(0, 320, 8):
            if (x // 8 + y // 8) % 2:
                draw.rectangle((x, y, x + 7, y + 7), fill=(193, 193, 193, 255))
    preview.alpha_composite(atlas)
    for x in range(64, 320, 64):
        draw.line((x, 0, x, 127), fill=(135, 135, 135, 255))
    draw.line((0, 64, 319, 64), fill=(135, 135, 135, 255))
    preview.resize((960, 384), Image.Resampling.NEAREST).save(OUT / 'atlas-preview.png')

    floor = Image.open(ROOT / 'output/imagegen/blockout-floor-v1-64.png').convert('RGBA')
    sample = Image.new('RGBA', (768, 576))
    for y in range(9):
        for x in range(12):
            sample.paste(floor, (x * 64, y * 64))

    def place(x, y, name):
        sample.alpha_composite(tiles[name], (x * 64, y * 64))

    for x in range(2, 10):
        place(x, 1, 'straight_ew')
    for y in range(2, 7):
        place(1, y, 'straight_ns')
        place(10, y, 'straight_ns')
    for x in (2, 3, 8, 9):
        place(x, 7, 'straight_ew')
    for x, y, name in [
        (1, 1, 'corner_es'), (10, 1, 'corner_sw'),
        (1, 7, 'corner_ne'), (10, 7, 'corner_wn'),
        (4, 7, 'end_w'), (7, 7, 'end_e'),
        (4, 3, 'end_e'), (5, 3, 'straight_ew'), (6, 3, 'corner_sw'),
        (6, 4, 'straight_ns'), (6, 5, 'end_n'),
    ]:
        place(x, y, name)
    sample.save(OUT / 'assembly-preview.png')
    info = {'tile_size': 64, 'wall_width': 24, 'atlas_size': [320, 128], 'columns': 5, 'rows': 2, 'tiles': manifest}
    (OUT / 'tiles.json').write_text(json.dumps(info, ensure_ascii=False, indent=2) + '\n')
    with zipfile.ZipFile(OUT / 'blockout-walls-v1.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
        for path in [OUT / 'walls-atlas-64.png', OUT / 'tiles.json', OUT / 'README.md', *sorted((OUT / 'tiles').glob('*.png'))]:
            archive.write(path, path.relative_to(OUT))
    print('Packed 10 RGBA tiles; each is 64 x 64, all open ports match exactly and are 24px wide.')
    print('Atlas:', OUT / 'walls-atlas-64.png')


if __name__ == '__main__':
    main()
