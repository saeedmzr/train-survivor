"""Composite an RGBA cutout onto mid grey, trimmed and squared - TripoSR's expected input."""
import sys, os
from PIL import Image

src, dst = sys.argv[1], sys.argv[2]
im = Image.open(src).convert("RGBA")
bbox = im.split()[3].getbbox()
if bbox:
    im = im.crop(bbox)
side = int(max(im.size) * 1.15)
canvas = Image.new("RGBA", (side, side), (128, 128, 128, 255))
canvas.alpha_composite(im, ((side - im.size[0]) // 2, side - im.size[1] - int(side * 0.04)))
os.makedirs(os.path.dirname(dst) or ".", exist_ok=True)
canvas.convert("RGB").save(dst)
print("wrote", dst, canvas.size)
