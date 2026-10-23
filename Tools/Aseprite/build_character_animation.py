from pathlib import Path

from PIL import Image, ImageDraw


CELL = 32
COLS = 6
ROWS = 2
OUT = Path(__file__).resolve().parents[2] / "output" / "aseprite"

INK = (25, 25, 31, 255)
HELMET_DARK = (54, 57, 69, 255)
HELMET = (88, 93, 109, 255)
HELMET_LIGHT = (133, 137, 153, 255)
FACE = (226, 174, 165, 255)
LENS = (163, 108, 123, 255)
LENS_SHADOW = (118, 71, 83, 255)
YELLOW = (246, 177, 12, 255)
YELLOW_LIGHT = (255, 215, 73, 255)
ORANGE = (240, 83, 31, 255)
ORANGE_LIGHT = (255, 124, 47, 255)
SCARF_SHADOW = (165, 55, 43, 255)
BODY = (66, 68, 76, 255)
BODY_LIGHT = (103, 106, 116, 255)
FEET = (36, 39, 46, 255)


def shifted(points, dx=0, dy=0):
    return [(x + dx, y + dy) for x, y in points]


def polygon(draw, points, color):
    draw.polygon(points, fill=color)


def draw_frame(index, running):
    img = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    if running:
        bob = (1, 0, 0, 1, 2, 1)[index]
        tail_lift = (1, 0, -1, -1, 0, 1)[index]
        foot_a = ((9, 29), (10, 28), (12, 27), (15, 28), (17, 29), (13, 29))[index]
        foot_b = ((22, 27), (21, 28), (19, 29), (17, 28), (15, 27), (20, 27))[index]
    else:
        bob = (0, 0, 1, 1, 1, 0)[index]
        tail_lift = (0, 1, 2, 1, 0, -1)[index]
        foot_a = (12, 28)
        foot_b = (21, 28)

    # The scarf tail sits behind the compact torso and trails to the left.
    tail = [
        (16, 16 + tail_lift), (13, 17 + tail_lift), (11, 19 + tail_lift),
        (8, 20 + tail_lift), (5, 21 + tail_lift), (1, 21 + tail_lift),
        (1, 18 + tail_lift), (4, 19 + tail_lift), (8, 18 + tail_lift),
        (10, 16 + tail_lift), (13, 15 + tail_lift), (16, 15 + tail_lift),
    ]
    polygon(d, shifted(tail, dy=bob), INK)
    tail_inner = [
        (15, 16 + tail_lift), (12, 18 + tail_lift), (9, 20 + tail_lift),
        (5, 20 + tail_lift), (2, 19 + tail_lift), (5, 20 + tail_lift),
        (9, 19 + tail_lift), (12, 17 + tail_lift), (15, 15 + tail_lift),
    ]
    polygon(d, shifted(tail_inner, dy=bob), ORANGE)
    d.line(shifted([(2, 18 + tail_lift), (5, 19 + tail_lift), (8, 18 + tail_lift)], dy=bob), fill=ORANGE_LIGHT, width=1)
    d.line(shifted([(5, 21 + tail_lift), (9, 21 + tail_lift), (12, 19 + tail_lift)], dy=bob), fill=SCARF_SHADOW, width=1)

    # Two tiny feet alternate during the run cycle. No hands or arms are drawn.
    if running:
        for x, y in (foot_a, foot_b):
            polygon(d, [(x, y + bob), (x + 3, y + bob), (x + 3, y + 1 + bob), (x, y + 1 + bob)], INK)
            d.rectangle((x + 1, y + bob, x + 2, y + 1 + bob), fill=FEET)
    else:
        for x, y in (foot_a, foot_b):
            polygon(d, [(x, y + bob), (x + 3, y + bob), (x + 3, y + 2 + bob), (x, y + 2 + bob)], INK)
            d.rectangle((x + 1, y + bob, x + 2, y + 1 + bob), fill=FEET)

    torso = [(12, 17), (23, 17), (25, 19), (25, 25), (23, 27), (13, 27), (11, 25), (11, 20)]
    polygon(d, shifted(torso, dy=bob), INK)
    body = [(13, 18), (22, 18), (24, 20), (24, 25), (22, 26), (14, 26), (12, 24), (12, 20)]
    polygon(d, shifted(body, dy=bob), BODY)
    polygon(d, shifted([(14, 20), (21, 20), (22, 22), (14, 22)], dy=bob), BODY_LIGHT)
    d.line(shifted([(13, 24), (15, 25), (21, 25), (23, 24)], dy=bob), fill=HELMET_DARK, width=1)

    helmet = [
        (11, 0), (20, 0), (23, 2), (25, 5), (26, 9), (26, 13),
        (24, 16), (22, 18), (10, 18), (8, 16), (6, 13), (6, 8),
        (8, 4),
    ]
    polygon(d, shifted(helmet, dy=bob), INK)
    helmet_inner = [
        (11, 1), (19, 1), (22, 3), (24, 6), (25, 9), (25, 13),
        (23, 15), (21, 17), (11, 17), (9, 15), (7, 12), (7, 8),
        (9, 4),
    ]
    polygon(d, shifted(helmet_inner, dy=bob), HELMET)
    polygon(d, shifted([(8, 7), (10, 5), (12, 5), (12, 14), (10, 16), (8, 14)], dy=bob), HELMET_DARK)
    polygon(d, shifted([(18, 3), (21, 3), (23, 5), (22, 6), (19, 5)], dy=bob), HELMET_LIGHT)
    d.rectangle((22, 6 + bob, 23, 6 + bob), fill=HELMET_LIGHT)

    # Face and rectangular goggles retain the reference's strongest landmarks.
    d.rectangle((11, 8 + bob, 23, 15 + bob), fill=FACE)
    d.rectangle((9, 7 + bob, 24, 12 + bob), fill=INK)
    d.rectangle((10, 7 + bob, 23, 11 + bob), fill=YELLOW)
    d.rectangle((11, 8 + bob, 22, 10 + bob), fill=LENS_SHADOW)
    d.rectangle((12, 8 + bob, 15, 10 + bob), fill=LENS)
    d.rectangle((18, 8 + bob, 22, 10 + bob), fill=LENS)
    d.rectangle((16, 8 + bob, 17, 10 + bob), fill=INK)
    d.rectangle((11, 7 + bob, 18, 7 + bob), fill=YELLOW_LIGHT)
    d.rectangle((14, 13 + bob, 18, 13 + bob), fill=SCARF_SHADOW)

    # Scarf wrap covers the neck and sits on top of the torso.
    wrap = [(10, 15), (23, 15), (25, 17), (24, 19), (13, 19), (10, 18)]
    polygon(d, shifted(wrap, dy=bob), INK)
    polygon(d, shifted([(11, 15), (22, 15), (24, 17), (23, 18), (13, 18), (11, 17)], dy=bob), ORANGE)
    d.line(shifted([(12, 16), (19, 16), (22, 16)], dy=bob), fill=ORANGE_LIGHT, width=1)
    d.line(shifted([(14, 19), (21, 19), (23, 18)], dy=bob), fill=SCARF_SHADOW, width=1)

    return img


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    sheet = Image.new("RGBA", (CELL * COLS, CELL * ROWS), (0, 0, 0, 0))
    frames = []
    for row in range(ROWS):
        for col in range(COLS):
            running = row == 1
            frame = draw_frame(col, running)
            frames.append(frame)
            sheet.alpha_composite(frame, (col * CELL, row * CELL))
            frame.save(OUT / f"character-frame-{len(frames):02d}.png")
    sheet.save(OUT / "character-animation-v1-sheet.png")
    frames[0].save(
        OUT / "character-animation-v1.gif",
        save_all=True,
        append_images=frames[1:],
        duration=[180] * 6 + [90] * 6,
        loop=0,
        disposal=2,
        transparency=0,
    )


if __name__ == "__main__":
    main()
