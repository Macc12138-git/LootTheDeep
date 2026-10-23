"""Local blockouts and composition for the reference-led map revision.

This file does not call an image API. Finished artwork is generated separately
using the installed imagegen CLI with the user's original screenshots.
"""

import argparse
import json
import zipfile
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "output/imagegen/validation-map-v2"
W, H = 1448, 1086
GRASS = (65, 103, 63)
DIRT = (142, 103, 59)
FLOOR = (95, 94, 109)
FINAL_GRASS = (60, 115, 67)
FINAL_DIRT = (159, 113, 61)
SECTORS = {"nw": (0, 0), "ne": (1, 0), "sw": (0, 1), "se": (1, 1)}
PATHS = {
    "nw": [([(300,H),(300,900),(500,780),(640,640),(1000,460),(W,420)],176),
           ([(760,580),(1080,820),(1100,H)],160),
           ([(1080,820),(W,900)],144)],
    "ne": [([(0,420),(400,420),(660,280),(1000,320),(W,350)],176),
           ([(600,H),(600,850),(400,680),(400,420)],160),
           ([(1200,H),(1200,760),(1000,600),(1000,320)],160),
           ([(0,900),(360,900),(600,850)],144)],
    "sw": [([(300,0),(300,280),(100,300),(0,300)],176),
           ([(300,280),(800,420),(W,300)],176),
           ([(1100,0),(1100,220),(800,420)],160),
           ([(300,280),(420,700),(780,800),(W,800)],160)],
    "se": [([(600,0),(600,200),(260,300),(0,300)],176),
           ([(1200,0),(1200,320),(1000,540),(700,700),(0,800)],160),
           ([(260,300),(320,620),(700,700),(950,900),(950,H)],160)],
}
FLOORS = {
    "nw": [[(120,120),(720,120),(720,280),(820,280),(820,540),(120,540)],
           [(720,800),(930,800),(930,950),(720,950)]],
    "ne": [[(790,80),(1330,80),(1330,570),(1160,570),(1160,480),(790,480)],
           [(100,590),(300,590),(300,770),(100,770)]],
    "sw": [[(650,60),(1000,60),(1000,260),(850,260),(850,310),(650,310)]],
    "se": [[(520,330),(820,330),(820,520),(900,520),(900,650),(520,650)],
           [(1100,800),(1330,800),(1330,980),(1100,980)]],
}
WALLS = {
    "nw": [[(120,520),(120,120),(720,120),(720,260)],
           [(120,540),(340,540)],[(510,540),(820,540),(820,360)],
           [(720,800),(720,950),(900,950)]],
    "ne": [[(800,180),(800,80),(1330,80),(1330,570),(1190,570)],
           [(800,370),(800,480),(990,480)],
           [(100,590),(100,770),(300,770)]],
    "sw": [[(650,280),(650,60),(1000,60)],[(1000,160),(1000,260),(900,260)],
           [(160,660),(160,900),(310,900)]],
    "se": [[(520,330),(820,330),(820,450)],[(520,480),(520,650),(700,650)],
           [(1100,800),(1330,800),(1330,980)]],
}
CONTAINERS = {
    "nw": [(220,220,0),(470,230,1),(650,400,2),
           (270,410,0),(790,855,1),(1180,650,2)],
    "ne": [(900,180,0),(1190,180,1),(1180,420,2),
           (900,360,1),(170,650,0),(780,760,2)],
    "sw": [(740,140,0),(920,120,1),(860,230,2),
           (210,770,0),(560,920,1),(1150,620,2)],
    "se": [(600,420,0),(730,530,1),(1170,880,2),
           (1290,900,0),(200,980,1),(1050,650,2)],
}
ROCKS = {
    "nw": [(1060,180,130),(550,1010,85)],
    "ne": [(270,150,100),(1040,940,65)],
    "sw": [(90,80,80),(950,960,110),(1250,820,70)],
    "se": [(160,120,100),(1100,120,70),(180,540,80),(620,950,105)],
}


def guides():
    (OUT / "guides").mkdir(parents=True, exist_ok=True)
    master = Image.new("RGB", (W*2,H*2))
    for name, (col,row) in SECTORS.items():
        image = Image.new("RGB",(W,H),GRASS)
        draw = ImageDraw.Draw(image)
        for points, width in PATHS[name]:
            draw.line(points, fill=DIRT, width=width, joint="curve")
            for x,y in points:
                r=width//2
                draw.ellipse((x-r,y-r,x+r,y+r),fill=DIRT)
        for polygon in FLOORS[name]:
            draw.polygon(polygon, fill=FLOOR)
        for points in WALLS[name]:
            draw.line([(x,y+42) for x,y in points], fill=(36,36,49),width=38)
            draw.line([(x,y+24) for x,y in points], fill=(71,69,92),width=34)
            draw.line(points,fill=(126,126,145),width=34)
        for x,y,r in ROCKS[name]:
            draw.polygon([(x-r,y),(x-r//2,y-r//2),(x+r//2,y-r//2),
                          (x+r,y),(x+r//2,y+r//2),(x-r//2,y+r//2)],
                         fill=(63,63,75))
        for x,y,kind in CONTAINERS[name]:
            colors=[(130,84,48),(106,83,56),(76,91,118)]
            draw.rectangle((x-4,y-4,x+60,y+48),fill=(28,29,36))
            draw.rectangle((x,y,x+56,y+44),fill=colors[kind])
            draw.rectangle((x+24,y+16,x+32,y+26),fill=(173,151,100))
        # Coarse blockout only. AI redraws all contours in the reference style.
        image=image.resize((362,272),Image.Resampling.NEAREST).resize((W,H),Image.Resampling.NEAREST)
        image.save(OUT / "guides" / f"{name}.png")
        master.paste(image,(col*W,row*H))
    master.resize((W,H),Image.Resampling.NEAREST).save(OUT / "guides/layout.png")
    print(OUT / "guides/layout.png")


def harmonize_ground(name, source):
    """Unify flat terrain shades while retaining generated object pixels."""
    image=source.copy()
    pixels=image.load()
    for y in range(H):
        for x in range(W):
            r,g,b=pixels[x,y]
            if g > r+24 and g > b+24 and 60 < g < 160:
                if g >= 126:
                    pixels[x,y]=(75,128,69)
                elif g >= 108:
                    pixels[x,y]=FINAL_GRASS
                elif g >= 95:
                    pixels[x,y]=(51,100,58)
                else:
                    pixels[x,y]=(41,84,49)
            elif r > g+28 and g > b+24 and 70 < g < 150:
                # Leave the artwork inside container silhouettes untouched.
                if any(abs(x-cx-28)<100 and abs(y-cy-22)<90
                       for cx,cy,_ in CONTAINERS[name]):
                    continue
                if g >= 108:
                    pixels[x,y]=FINAL_DIRT
                elif g >= 92:
                    pixels[x,y]=(139,96,51)
                else:
                    pixels[x,y]=(112,85,48)
    # Internal edges contain terrain only. Match their first few pixels exactly,
    # then transition through a short band without resampling or blurring objects.
    guide=Image.open(OUT/"guides"/f"{name}.png").convert("RGB")
    gp=guide.load()
    col,row=SECTORS[name]
    for y in range(H):
        for x in range(W):
            distance=min(W-1-x if col==0 else x,H-1-y if row==0 else y)
            if distance>=32:
                continue
            color=FINAL_GRASS if gp[x,y]==GRASS else FINAL_DIRT if gp[x,y]==DIRT else None
            if color is None:
                continue
            alpha=round(max(0,min(1,(distance-6)/26))*8)/8
            pixels[x,y]=tuple(round(c*alpha+t*(1-alpha))
                              for c,t in zip(pixels[x,y],color))
    return image


def assemble():
    (OUT / "chunks").mkdir(exist_ok=True)
    master=Image.new("RGB",(W*2,H*2))
    records=[]
    for name,(col,row) in SECTORS.items():
        with Image.open(OUT / f"{name}-raw.png") as source:
            original_size=source.size
            image=source.convert("RGB")
        if image.size!=(W,H):
            image=image.resize((W,H),Image.Resampling.NEAREST)
        image=harmonize_ground(name,image)
        image.save(OUT / "chunks" / f"{name}.png")
        master.paste(image,(col*W,row*H))
        records.append({"name":name,"source_size":original_size,
                        "chunk_size":[W,H],"pixel_origin_top_left":[col*W,row*H]})
    master.save(OUT / "complete.png")
    master.resize((W,H),Image.Resampling.NEAREST).save(OUT / "overview.png")
    master.crop((90,55,890,655)).save(OUT / "style-detail.png")
    (OUT / "assembly.json").write_text(json.dumps({"canvas_size":master.size,
        "chunks":records,"scope":"Reference-led map image, not a Unity Tilemap."},
        indent=2)+"\n",encoding="utf-8")
    with zipfile.ZipFile(OUT / "validation-map-v2.zip","w",zipfile.ZIP_DEFLATED) as archive:
        for path in [OUT/"complete.png",OUT/"overview.png",OUT/"style-detail.png",OUT/"assembly.json",OUT/"README.md",
                     *sorted((OUT/"chunks").glob("*.png"))]:
            if path.exists():
                archive.write(path,path.relative_to(OUT))
        for path in sorted((ROOT/"Tools/ImageGen/prompts/validation-map-v2").glob("*.txt")):
            archive.write(path,f"prompts/{path.name}")
    print(json.dumps(records))


if __name__ == "__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action",choices=["guides","assemble"])
    args=parser.parse_args()
    guides() if args.action=="guides" else assemble()
