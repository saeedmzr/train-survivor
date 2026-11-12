#!/usr/bin/env python3
"""Find frames containing warm, bright projectile pixels (muzzle flash / tracer).

    python tools/find_tracer_frames.py <frames_dir> [--top 12]

Scans every frame for pixels that are hot and warm (high R+G, low-ish B) and prints the
frame names ranked by count, so a 400-frame screen recording can be narrowed to the few
frames that actually contain the effect.
"""
import glob
import os
import sys

from PIL import Image


def score(path):
    im = Image.open(path).convert("RGB")
    w, h = im.size
    # gameplay band only: skip the HUD strip
    box = im.crop((0, int(h * 0.18), w, int(h * 0.92)))
    px = box.resize((w // 4, (int(h * 0.92) - int(h * 0.18)) // 4))
    n = 0
    for r, g, b in px.getdata():
        if r > 215 and g > 160 and b < 150 and r - b > 70:
            n += 1
    return n


def main(frames_dir, top=12):
    files = sorted(glob.glob(os.path.join(frames_dir, "*.png")))
    scored = [(score(f), f) for f in files]
    scored.sort(reverse=True)
    for s, f in scored[:top]:
        print("%5d  %s" % (s, os.path.basename(f)))
    print("scanned", len(files), "frames")


if __name__ == "__main__":
    d = sys.argv[1]
    t = 12
    if "--top" in sys.argv:
        t = int(sys.argv[sys.argv.index("--top") + 1])
    main(d, t)
