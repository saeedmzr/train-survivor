#!/usr/bin/env python3
"""Pack projectiles, impact FX and a soft ground shadow into one runtime atlas.

Reads   finalized-for-develop/projectiles/proj_*.png
        finalized-for-develop/fx/fx_*.png
Writes  unity/Assets/_Project/Art/Resources/Art/atlas_vfx.png
        unity/Assets/_Project/Scripts/Game/View/ArtVfx.Generated.cs

The game draws shots as travelling sprites now, not stretched cubes, so the bullet and
impact art has to be in an atlas the Sprite3D billboard can sample. The shadow is generated
here: every enemy needs one drawn blob under its feet or the horde reads as cut-outs
hovering over the ballast.
"""
import glob
import math
import os

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
UNITY = os.path.join(ROOT, "unity/Assets/_Project")
ATLAS_DIR = os.path.join(UNITY, "Art/Resources/Art")
CS = os.path.join(UNITY, "Scripts/Game/View/ArtVfx.Generated.cs")

PIX = 192       # square sprites, packed at this size
PAD = 2
PIVOT = {
    # name: (pivotX, pivotY)
    "shadow": (0.5, 0.5),
}


def _blend(px, color, a):
    r, g, b, _ = px
    cr, cg, cb = color
    return (int(r + (cr - r) * a), int(g + (cg - g) * a), int(b + (cb - b) * a), int(min(255, px[3] + a * 255)))


def glow_dot(size=192, core=(255, 250, 235), halo=(255, 176, 64)):
    """Additive-friendly round glow: white-hot centre, warm falloff, zero at the edge."""
    im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    px = im.load()
    cx = cy = size / 2.0
    for y in range(size):
        for x in range(size):
            d = math.hypot(x - cx, y - cy) / (size * 0.5)
            if d >= 1.0:
                continue
            hot = max(0.0, 1.0 - d / 0.34)
            warm = max(0.0, 1.0 - d) ** 2.2
            col = tuple(core[i] * hot + halo[i] * (1 - hot) for i in range(3))
            px[x, y] = (int(col[0]), int(col[1]), int(col[2]), int(255 * min(1.0, warm * 1.15)))
    return im


def tracer(size=192):
    """Horizontal hot streak: a bright slug with a tapering tail, pointing +X."""
    im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    px = im.load()
    cy = size / 2.0
    head = size * 0.72
    tail = size * 0.20
    for x in range(int(tail), int(head)):
        u = max(0.0, min(1.0, (x - tail) / (head - tail)))
        half = size * (0.055 + 0.075 * u ** 2.2)
        for y in range(int(cy - half), int(cy + half) + 1):
            if y < 0 or y >= size:
                continue
            v = max(0.0, 1.0 - abs(y - cy) / max(1.0, half))
            a = min(1.0, (v ** 0.7) * (u ** 0.85))   # taper to nothing at the tail
            hot = min(1.0, a * 1.25)
            col = (255, int(200 + 55 * hot), int(120 + 110 * hot))
            px[x, y] = (col[0], col[1], col[2], int(255 * a))
    # hot nose
    for y in range(int(cy - size * 0.10), int(cy + size * 0.10) + 1):
        for x in range(int(head - size * 0.10), int(head)):
            v = 1.0 - abs(y - cy) / (size * 0.10)
            if v > 0:
                px[x, y] = (255, 250, 225, int(255 * v))
    return im


def impact(size=192):
    """Radial burst: bright core plus spokes, reads as a hit at 20-40 px."""
    im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    cx = cy = size / 2.0
    for i in range(16):
        a = i * (math.pi * 2 / 16) + 0.18
        r0, r1 = size * 0.06, size * (0.30 + 0.10 * (i % 3))
        d.line([cx + math.cos(a) * r0, cy + math.sin(a) * r0,
                cx + math.cos(a) * r1, cy + math.sin(a) * r1],
               fill=(255, 236, 190, 210), width=max(2, size // 48))
    # soft hot core: a radial gradient, not stacked ellipses (those band visibly)
    core = glow_dot(size, core=(255, 250, 235), halo=(255, 205, 120))
    cw = int(size * 0.62)
    core = core.resize((cw, cw), Image.LANCZOS)
    im.alpha_composite(core, (int(cx - cw / 2), int(cy - cw / 2)))
    return im


def shadow(size=192):
    """Soft elliptical blob, darkest in the middle, fading to nothing."""
    im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    cx, cy = size / 2.0, size / 2.0
    rx, ry = size * 0.46, size * 0.30
    steps = 26
    for i in range(steps, 0, -1):
        t = i / float(steps)
        a = int(120 * (1.0 - t) ** 1.6)
        if a <= 0:
            continue
        d.ellipse([cx - rx * t, cy - ry * t, cx + rx * t, cy + ry * t],
                  fill=(0, 0, 0, a))
    # flatten hard edges with a tiny blur-free dither pass
    return im


def shelf_pack(items, size):
    x = y = 0
    row_h = 0
    out = []
    for name, im in items:
        w, h = im.size
        if w + PAD * 2 > size:
            return None
        if x + w + PAD * 2 > size:
            x = 0
            y += row_h + PAD * 2
            row_h = 0
        if y + h + PAD * 2 > size:
            return None
        out.append((name, x + PAD, y + PAD, w, h))
        x += w + PAD * 2
        row_h = max(row_h, h)
    return out


def main():
    sprites = []
    for p in sorted(glob.glob(os.path.join(ROOT, "finalized-for-develop/projectiles/proj_*.png"))):
        im = Image.open(p).convert("RGBA").resize((PIX, PIX), Image.LANCZOS)
        sprites.append((os.path.splitext(os.path.basename(p))[0], im))
    for p in sorted(glob.glob(os.path.join(ROOT, "finalized-for-develop/fx/fx_*.png"))):
        im = Image.open(p).convert("RGBA").resize((PIX, PIX), Image.LANCZOS)
        sprites.append((os.path.splitext(os.path.basename(p))[0], im))
    # The kit's projectiles are faint placeholders (alpha ~22/255), so the shot FX the game
    # actually draws are generated here: a hot tracer, a pellet dot, an impact burst and a
    # muzzle glow. All additive-friendly.
    sprites.append(("bullet_core", tracer()))
    sprites.append(("bullet_pellet", glow_dot(core=(255, 252, 240), halo=(255, 205, 110))))
    sprites.append(("impact_core", impact()))
    sprites.append(("muzzle_core", glow_dot(core=(255, 250, 235), halo=(255, 150, 60))))
    sprites.append(("shadow", shadow()))
    sprites.append(("dot", glow_dot()))

    # soft dot for the particle systems: they sample a whole texture, not an atlas UV.
    # Warm, not cyan: the spark material is amber, and a blue texture read as blue specks.
    spark = glow_dot(64, core=(255, 250, 235), halo=(255, 160, 60))
    spark.save(os.path.join(ATLAS_DIR, "spark_dot.png"))

    place = None
    size = 512
    while place is None:
        size *= 2
        place = shelf_pack(sprites, size)

    atlas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    for (name, im), (_, x, y, w, h) in zip(sprites, place):
        atlas.paste(im, (x, y), im)

    os.makedirs(ATLAS_DIR, exist_ok=True)
    atlas.save(os.path.join(ATLAS_DIR, "atlas_vfx.png"))

    rows = []
    for (name, im), (_, x, y, w, h) in zip(sprites, place):
        px, py = PIVOT.get(name, (0.5, 0.5))
        rows.append('            new ArtAtlas.Entry { Name = "%s", Atlas = "vfx", X = %d, Y = %d, '
                    'W = %d, H = %d, AtlasW = %d, AtlasH = %d, Pivot = new Vector2(%.2ff, %.2ff) },'
                    % (name, x, y, w, h, size, size, px, py))

    cs = """// <auto-generated>
//   generated by tools/pack_vfx_atlas.py from finalized-for-develop/{projectiles,fx}
//   do not edit: rerun the tool instead.
// </auto-generated>
using UnityEngine;

namespace TrainSurvivor.Game.View
{
    /// <summary>Projectiles, impact FX and the ground shadow, in Resources/Art/atlas_vfx.png.</summary>
    public static class ArtVfx
    {
        public const int AtlasSize = %d;

        public static readonly ArtAtlas.Entry[] Entries =
        {
%s
        };
    }
}
""" % (size, "\n".join(rows))
    open(CS, "w").write(cs)
    print("atlas %dx%d, %d sprites -> Art/Resources/Art/atlas_vfx.png" % (size, size, len(sprites)))
    print("table ->", os.path.relpath(CS, ROOT))


if __name__ == "__main__":
    main()
