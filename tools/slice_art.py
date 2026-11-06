#!/usr/bin/env python3
"""Slice the generated art sheets into game sprites.

Pipeline per sprite:
  crop a generous cell -> key the flat sheet background to alpha -> tight trim ->
  pack into a power-of-two atlas -> emit a C# sprite table (name, uv rect, pivot).

The cells below are hand-measured once against the sheets; tight trimming makes
small errors harmless as long as a cell never overlaps its neighbour.

Usage:  python3 tools/slice_art.py
Outputs: images/sliced/*.png, unity/Assets/_Project/Art/Resources/Art/*.png,
         unity/Assets/_Project/Scripts/Game/View/ArtAtlas.Generated.cs,
         tools/out/atlas_preview.png
"""
import json
import os
from PIL import Image, ImageChops, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHEET_UPGRADES = os.path.join(ROOT, "images/Gemini_Generated_Image_20iy0i20iy0i20iy.jpeg")
SHEET_ZOMBIES = os.path.join(ROOT, "images/Gemini_Generated_Image_20iy0i20iy0i20iy (1).jpeg")
OUT_SLICED = os.path.join(ROOT, "images/sliced")
OUT_UNITY = os.path.join(ROOT, "unity/Assets/_Project/Art/Resources/Art")
OUT_CS = os.path.join(ROOT, "unity/Assets/_Project/Scripts/Game/View/ArtAtlas.Generated.cs")
OUT_PREVIEW = os.path.join(ROOT, "tools/out/atlas_preview.png")

# name, sheet, x, y, w, h, atlas, pivot(x,y in 0..1, default bottom-centre)
W = "world"
U = "ui"
SPRITES = [
    # ---- zombies: 4 walk frames each, a hit frame, a death splat -------------
    ("walker_0", SHEET_ZOMBIES, 90, 240, 245, 340, W, (0.5, 0.0)),
    ("walker_1", SHEET_ZOMBIES, 395, 240, 250, 340, W, (0.5, 0.0)),
    ("walker_2", SHEET_ZOMBIES, 700, 240, 235, 340, W, (0.5, 0.0)),
    ("walker_3", SHEET_ZOMBIES, 1000, 240, 250, 340, W, (0.5, 0.0)),
    ("walker_hit", SHEET_ZOMBIES, 1640, 240, 295, 340, W, (0.5, 0.0)),
    ("walker_dead", SHEET_ZOMBIES, 1985, 380, 250, 200, W, (0.5, 0.0)),

    ("sprinter_0", SHEET_ZOMBIES, 95, 665, 260, 300, W, (0.5, 0.0)),
    ("sprinter_1", SHEET_ZOMBIES, 420, 665, 235, 300, W, (0.5, 0.0)),
    ("sprinter_2", SHEET_ZOMBIES, 690, 665, 240, 300, W, (0.5, 0.0)),
    ("sprinter_3", SHEET_ZOMBIES, 950, 665, 270, 300, W, (0.5, 0.0)),
    ("sprinter_hit", SHEET_ZOMBIES, 1225, 680, 340, 285, W, (0.5, 0.0)),
    ("sprinter_dead", SHEET_ZOMBIES, 1985, 380, 250, 200, W, (0.5, 0.0)),

    ("tank_0", SHEET_ZOMBIES, 100, 1020, 315, 360, W, (0.5, 0.0)),
    ("tank_1", SHEET_ZOMBIES, 475, 1020, 375, 360, W, (0.5, 0.0)),
    ("tank_hit", SHEET_ZOMBIES, 890, 1020, 460, 360, W, (0.5, 0.0)),
    ("tank_dead", SHEET_ZOMBIES, 1985, 380, 250, 200, W, (0.5, 0.0)),

    # ---- pickups -------------------------------------------------------------
    ("gem_green", SHEET_ZOMBIES, 90, 1495, 128, 200, W, (0.5, 0.0)),
    ("gem_blue", SHEET_ZOMBIES, 218, 1495, 130, 200, W, (0.5, 0.0)),
    ("gem_purple", SHEET_ZOMBIES, 350, 1495, 120, 200, W, (0.5, 0.0)),
    ("health_pack", SHEET_ZOMBIES, 555, 1505, 200, 200, W, (0.5, 0.0)),
    ("crate", SHEET_ZOMBIES, 830, 1500, 185, 200, W, (0.5, 0.0)),
    ("barrel_rust", SHEET_ZOMBIES, 1030, 1495, 140, 200, W, (0.5, 0.0)),
    ("barrel_toxic", SHEET_ZOMBIES, 1185, 1495, 140, 200, W, (0.5, 0.0)),
    ("currency", SHEET_ZOMBIES, 1335, 1500, 260, 200, W, (0.5, 0.0)),

    # ---- blockades (world) ---------------------------------------------------
    ("block_wreck", SHEET_UPGRADES, 69, 1195, 750, 480, W, (0.5, 0.0)),
    ("block_barrels", SHEET_UPGRADES, 870, 1195, 435, 460, W, (0.5, 0.0)),
    ("block_barrier", SHEET_UPGRADES, 1708, 1214, 250, 198, W, (0.5, 0.0)),
    ("block_debris", SHEET_UPGRADES, 1995, 1515, 305, 140, W, (0.5, 0.0)),

    # ---- UI: upgrade cards ---------------------------------------------------
    ("up_multishot", SHEET_UPGRADES, 91, 303, 217, 217, U, (0.5, 0.5)),
    ("up_firetrail", SHEET_UPGRADES, 341, 303, 217, 217, U, (0.5, 0.5)),
    ("up_spikebumper", SHEET_UPGRADES, 592, 303, 217, 217, U, (0.5, 0.5)),
    ("up_piercing", SHEET_UPGRADES, 841, 303, 217, 217, U, (0.5, 0.5)),
    ("up_boiler", SHEET_UPGRADES, 1092, 303, 217, 217, U, (0.5, 0.5)),
    ("up_rapidfire", SHEET_UPGRADES, 1342, 303, 217, 217, U, (0.5, 0.5)),
    ("up_incendiary", SHEET_UPGRADES, 1592, 303, 217, 217, U, (0.5, 0.5)),
    ("up_extragun", SHEET_UPGRADES, 1842, 303, 217, 217, U, (0.5, 0.5)),
    ("up_extrawagon", SHEET_UPGRADES, 2092, 303, 217, 217, U, (0.5, 0.5)),

    # ---- UI: banners, bars, warnings, flags ---------------------------------
    ("banner_levelup", SHEET_UPGRADES, 80, 730, 550, 160, U, (0.5, 0.5)),
    ("banner_victory", SHEET_UPGRADES, 80, 890, 550, 160, U, (0.5, 0.5)),
    ("bar_frame", SHEET_UPGRADES, 668, 744, 564, 102, U, (0.5, 0.5)),
    ("bar_fill_green", SHEET_UPGRADES, 758, 883, 397, 38, U, (0.5, 0.5)),
    ("bar_fill_blue", SHEET_UPGRADES, 771, 987, 309, 38, U, (0.5, 0.5)),
    ("icon_heart", SHEET_UPGRADES, 670, 860, 83, 86, U, (0.5, 0.5)),
    ("warn_0", SHEET_UPGRADES, 1270, 735, 160, 145, U, (0.5, 0.5)),
    ("warn_1", SHEET_UPGRADES, 1440, 735, 160, 145, U, (0.5, 0.5)),
    ("flag_green", SHEET_UPGRADES, 2078, 730, 105, 155, U, (0.5, 0.0)),
    ("flag_red", SHEET_UPGRADES, 2208, 730, 100, 155, U, (0.5, 0.0)),
    ("flag_yellow", SHEET_UPGRADES, 2055, 895, 120, 155, U, (0.5, 0.0)),
    ("marker_a", SHEET_ZOMBIES, 2040, 1495, 120, 175, U, (0.5, 0.5)),
    ("marker_b", SHEET_ZOMBIES, 2198, 1495, 105, 175, U, (0.5, 0.5)),

    # ---- UI: obstacle thumbnails (upgrade card art for blockade cards) ------
    ("icon_blockade", SHEET_UPGRADES, 1700, 1215, 260, 200, U, (0.5, 0.5)),
]

PAD = 6
ALPHA_LO, ALPHA_HI = 8, 34          # soft ramp on the distance-from-background

# Sprites that touch a neighbouring object on the sheet: drop pixels matching that
# neighbour's hue after keying (the heart is flush against the green bar).
CLEAN = {
    "icon_heart": lambda r, g, b: g > r + 8 and g > b + 8,
    "bar_fill_green": lambda r, g, b: r > g + 20,
    "bar_fill_blue": lambda r, g, b: abs(int(r) - int(g)) < 14 and r < 90,
}


def sheet_bg(img):
    w, h = img.size
    pts = [(2, 2), (w - 3, 2), (2, h - 3), (w - 3, h - 3), (w // 2, 1)]
    return tuple(sorted(img.getpixel(p)[i] for p in pts)[2] for i in range(3))


def cut(sheet, bg, rect, clean=None):
    x, y, w, h = rect
    crop = sheet.crop((x, y, x + w, y + h)).convert("RGB")
    # The sheets are not flat: the zombie sheet's interior is much lighter than its
    # corners, so the background is estimated from this crop's own border.
    from collections import Counter
    border = []
    for xx in range(0, crop.width, 2):
        border.append(crop.getpixel((xx, 0)))
        border.append(crop.getpixel((xx, crop.height - 1)))
    for yy in range(0, crop.height, 2):
        border.append(crop.getpixel((0, yy)))
        border.append(crop.getpixel((crop.width - 1, yy)))
    cell_bg = Counter(border).most_common(1)[0][0]
    diff = ImageChops.difference(crop, Image.new("RGB", crop.size, cell_bg)).convert("L")
    lut = []
    for v in range(256):
        t = (v - ALPHA_LO) / float(ALPHA_HI - ALPHA_LO)
        lut.append(max(0, min(255, int(t * 255))))
    alpha = diff.point(lut)
    out = crop.convert("RGBA")
    out.putalpha(alpha)
    if clean is not None:
        px = out.load()
        for yy in range(out.height):
            for xx in range(out.width):
                r, g, b, a = px[xx, yy]
                if a and clean(r, g, b):
                    px[xx, yy] = (r, g, b, 0)
    bbox = out.getchannel("A").point(lambda v: 255 if v > 12 else 0).getbbox()
    if bbox:
        out = out.crop((max(0, bbox[0] - 2), max(0, bbox[1] - 2),
                        min(out.width, bbox[2] + 2), min(out.height, bbox[3] + 2)))
    return out


def pack(images, size):
    """Shelf pack. Returns {name: (x, y)} and True if everything fit."""
    order = sorted(images.items(), key=lambda kv: (-kv[1].height, -kv[1].width))
    x = y = PAD
    shelf_h = 0
    placed = {}
    for name, im in order:
        if x + im.width + PAD > size:
            x = PAD
            y += shelf_h + PAD
            shelf_h = 0
        if y + im.height + PAD > size:
            return None
        placed[name] = (x, y)
        x += im.width + PAD
        shelf_h = max(shelf_h, im.height)
    return placed


def main():
    sheets = {}
    for path in (SHEET_UPGRADES, SHEET_ZOMBIES):
        im = Image.open(path).convert("RGB")
        sheets[path] = (im, sheet_bg(im))

    os.makedirs(OUT_SLICED, exist_ok=True)
    os.makedirs(OUT_UNITY, exist_ok=True)
    os.makedirs(os.path.dirname(OUT_PREVIEW), exist_ok=True)

    sprites = {}
    for name, sheet_path, x, y, w, h, atlas, pivot in SPRITES:
        sheet, bg = sheets[sheet_path]
        img = cut(sheet, bg, (x, y, w, h), CLEAN.get(name))
        img.save(os.path.join(OUT_SLICED, name + ".png"))
        sprites[name] = {"img": img, "atlas": atlas, "pivot": pivot, "sheet": os.path.basename(sheet_path)}

    entries = {}
    for atlas_name, target in (("world", "atlas_world.png"), ("ui", "atlas_ui.png")):
        items = {n: s["img"] for n, s in sprites.items() if s["atlas"] == atlas_name}
        for size in (1024, 2048, 4096):
            placed = pack(items, size)
            if placed:
                break
        else:
            raise SystemExit("sprites do not fit in 4096")
        atlas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        for n, (px, py) in placed.items():
            atlas.paste(items[n], (px, py))
        atlas.save(os.path.join(OUT_UNITY, target))
        atlas.save(os.path.join(OUT_SLICED, target))
        for n, (px, py) in placed.items():
            im = items[n]
            entries[n] = {
                "atlas": atlas_name, "x": px, "y": py, "w": im.width, "h": im.height,
                "atlasW": size, "atlasH": size, "pivot": sprites[n]["pivot"],
            }
        print(f"packed {atlas_name}: {len(placed)} sprites -> {target} {size}x{size}")

    # ---- C# table ------------------------------------------------------------
    lines = [
        "// <auto-generated> by tools/slice_art.py — do not edit by hand.",
        "using UnityEngine;",
        "",
        "namespace TrainSurvivor.Game.View",
        "{",
        "    /// <summary>Sprite table generated from the art sheets in images/.</summary>",
        "    public static class ArtAtlas",
        "    {",
        "        public const int WorldAtlasSize = 0;",
        "        public const int UiAtlasSize = 0;",
        "        public struct Entry",
        "        {",
        "            public string Name, Atlas;",
        "            public int X, Y, W, H, AtlasW, AtlasH;",
        "            public Vector2 Pivot;",
        "        }",
        "",
        "        public static readonly Entry[] Entries =",
        "        {",
    ]
    for n, e in sorted(entries.items()):
        lines.append(
            '            new Entry {{ Name = "{0}", Atlas = "{1}", X = {2}, Y = {3}, W = {4}, H = {5}, '
            'AtlasW = {6}, AtlasH = {7}, Pivot = new Vector2({8}f, {9}f) }},'
            .format(n, e["atlas"], e["x"], e["y"], e["w"], e["h"], e["atlasW"], e["atlasH"],
                    e["pivot"][0], e["pivot"][1]))
    lines += ["        };", "    }", "}", ""]
    with open(OUT_CS, "w") as f:
        f.write("\n".join(lines))
    with open(os.path.join(OUT_SLICED, "sprites.json"), "w") as f:
        json.dump(entries, f, indent=1)

    # ---- preview contact sheet ----------------------------------------------
    cols = 8
    cell = 190
    rows = (len(sprites) + cols - 1) // cols
    prev = Image.new("RGB", (cols * cell, rows * (cell + 18)), (30, 32, 36))
    d = ImageDraw.Draw(prev)
    try:
        font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 11)
    except OSError:
        font = ImageFont.load_default()
    for i, (n, s) in enumerate(sorted(sprites.items())):
        cx, cy = (i % cols) * cell, (i // cols) * (cell + 18)
        for yy in range(cy, cy + cell, 16):      # checkerboard so alpha is visible
            for xx in range(cx, cx + cell, 16):
                if ((xx // 16) + (yy // 16)) % 2 == 0:
                    d.rectangle([xx, yy, xx + 15, yy + 15], fill=(52, 55, 60))
        im = s["img"]
        k = min((cell - 10) / im.width, (cell - 10) / im.height)
        im = im.resize((max(1, int(im.width * k)), max(1, int(im.height * k))))
        prev.paste(im, (cx + (cell - im.width) // 2, cy + (cell - im.height) // 2), im)
        d.text((cx + 4, cy + cell + 2), n, fill=(220, 220, 220), font=font)
    prev.save(OUT_PREVIEW)
    print(f"{len(sprites)} sprites -> {OUT_SLICED}")
    print(f"preview -> {OUT_PREVIEW}")


if __name__ == "__main__":
    main()
