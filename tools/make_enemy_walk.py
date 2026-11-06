#!/usr/bin/env python3
"""Turn each painted enemy cutout into a six-frame walk cycle.

One painted sprite per species reads as a sliding picture in game. There is no rig to
animate, so this builds the cycle the way 2D games have always done it cheaply: warp the
silhouette into six poses that, played in order, read as a step.

What each frame does
  * leans the body forward and back (a wave up the sprite, so the head leads the hips)
  * swings the bottom 40 % of the silhouette the other way — that band is the legs
  * squashes vertically on the plant and stretches at the pass, feet anchored
  * floats the whole sprite on the bob curve that matches the squash

Frames are written next to the source so pack_enemy_atlas.py can pick them up:

    art-work/enemies/<id>/walk_0..5.png     the cycle, all six on one shared canvas
    (sprite.png stays the untouched cutout and is no longer what the game draws)
"""
import math
import os

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ART = os.path.join(ROOT, "art-work/enemies")

FRAMES = 6

# per species: (lean, leg swing, bob, anchor lift) as fractions of width / height.
# Heavy things sway slowly and low, runners kick high. Values are tuned against the
# painted silhouettes, not a rig.
GAIT = {
    "walker":   (0.070, 0.100, 0.024),
    "sprinter": (0.085, 0.125, 0.032),
    "tank":     (0.042, 0.055, 0.013),
    "crawler":  (0.055, 0.115, 0.011),
    "bloater":  (0.045, 0.058, 0.019),
    "spitter":  (0.062, 0.088, 0.021),
    "shrieker": (0.080, 0.105, 0.029),
    "bulwark":  (0.030, 0.038, 0.011),
    "glider":   (0.100, 0.045, 0.046),   # barely walks: it drifts, so the body swings
    "drillmaw": (0.068, 0.095, 0.015),
    "lantern":  (0.050, 0.062, 0.017),
    "rimback":  (0.040, 0.048, 0.013),
}
DEFAULT = (0.062, 0.085, 0.021)


def shear_rows(src, offsets):
    """Shift each row by offsets[y] px. Rows are pasted whole, so this is cheap."""
    w, h = src.size
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    for y in range(h):
        dx = int(round(offsets[y]))
        if dx == 0:
            out.paste(src.crop((0, y, w, y + 1)), (0, y))
        else:
            out.paste(src.crop((0, y, w, y + 1)), (dx, y))
    return out


def make_frame(src, t, lean, leg, bob):
    w, h = src.size

    # Padding: the warp pushes rows sideways, so give it room or the silhouette gets a
    # straight cut down the edge. The margin is the worst case of the two waves.
    margin = int(math.ceil((lean + leg) * w)) + 2
    pw = w + margin * 2
    p = 2.0 * math.pi * t / FRAMES

    # squash: feet planted at the bottom, so scale then paste bottom-aligned
    squash = 1.0 + 0.035 * math.cos(2.0 * p)
    sh = max(2, int(round(h * squash)))
    body = src.resize((w, sh), Image.LANCZOS)

    # the stretch keeps the feet on the ground: a squash pushes the body down, not up
    lift = int(round((sh - h) * 0.5 + bob * h * math.sin(2.0 * p)))
    canvas = Image.new("RGBA", (pw, h), (0, 0, 0, 0))
    canvas.paste(body, (margin, max(0, h - sh) - lift), body)

    # the warp: hips lead one way, legs the other, with a phase twist up the body
    offsets = []
    leg_top = 0.40
    for y in range(h):
        u = 1.0 - y / float(h - 1)          # 0 at the feet, 1 at the head
        dx = lean * w * u * math.sin(p + 0.6 * u)
        if u < leg_top:
            swing = 1.0 - u / leg_top        # strongest at the feet
            dx += leg * w * swing * math.sin(p + math.pi)
        offsets.append(dx)
    return shear_rows(canvas, offsets)


def main():
    ids = [d for d in sorted(os.listdir(ART)) if os.path.isdir(os.path.join(ART, d))]
    made = 0
    for eid in ids:
        path = os.path.join(ART, eid, "sprite.png")
        if not os.path.exists(path):
            print("skip (no sprite):", eid)
            continue
        src = Image.open(path).convert("RGBA")
        lean, leg, bob = GAIT.get(eid, DEFAULT)
        # frame 0 goes through the same warp with zero phase, so every frame shares the
        # padded canvas and the sprite never changes size when the cycle swaps frames
        for t in range(FRAMES):
            frame = make_frame(src, t, lean, leg, bob)
            frame.save(os.path.join(ART, eid, "walk_%d.png" % t))
            made += 1
        print("%-10s %dx%d  %d frames" % (eid, src.size[0], src.size[1], FRAMES))
    print("wrote", made, "frames")


if __name__ == "__main__":
    main()
