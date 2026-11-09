"""Extract clean view mattes + a per-height width/depth profile from the Walker design sheets.

The sheet is a studio render on a vignetted grey backdrop, so the background is estimated
per-row from the outer columns (the vignette is vertical), not from one global colour. The
three tall blobs are the turnaround views; the title lettering is dropped by a height filter.

    python analyze_refs.py <sheet.jpg> <out_dir>

Writes <out>/front.png, side.png, back.png (trimmed, alpha) and <out>/profile.json.
"""
import json
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

src = sys.argv[1]
out = sys.argv[2]
os.makedirs(out, exist_ok=True)

im = Image.open(src).convert("RGB")
W, H = im.size
a = np.asarray(im).astype(np.int16)

# per-row background: median of the outermost 15 px on each side (the vignette runs vertically)
edge = np.concatenate([a[:, :15], a[:, -15:]], axis=1)
bg_row = np.median(edge, axis=1).astype(np.int16)
dist = np.abs(a - bg_row[:, None, :]).sum(axis=2)
mask = ndimage.binary_opening(dist > 45, np.ones((3, 3)))

lab, n = ndimage.label(mask)
if n == 0:
    raise SystemExit("no blobs found")

cands = []
for i, sl in enumerate(ndimage.find_objects(lab, n), start=1):
    if sl is None:
        continue
    h = sl[0].stop - sl[0].start
    w = sl[1].stop - sl[1].start
    if h >= H * 0.33 and w >= W * 0.12:
        cands.append((i, sl, w))

if len(cands) < 3:
    raise SystemExit("expected 3 views, found %d" % len(cands))

cands.sort(key=lambda t: -t[2])
cands = sorted(cands[:3], key=lambda t: t[1][1].start)   # left -> right
names = ["side", "front", "back"]

profile = {"views": {}, "profile": {}}
matte = {}
for name, (idx, _sl, _w) in zip(names, cands):
    blob = lab == idx
    # the sheet has thin annotation leader lines crossing the figure; an opening with a 7x7
    # kernel removes them while leaving the body silhouette intact
    blob = ndimage.binary_opening(blob, np.ones((7, 7)))
    m = ndimage.binary_fill_holes(ndimage.binary_closing(blob, np.ones((7, 7))))
    ys, xs = np.nonzero(m)
    top, bot, left, right = ys.min(), ys.max(), xs.min(), xs.max()
    rgb = a[top:bot + 1, left:right + 1].astype(np.uint8)
    alpha = (m[top:bot + 1, left:right + 1] * 255).astype(np.uint8)
    Image.fromarray(np.dstack([rgb, alpha]), "RGBA").save(os.path.join(out, name + ".png"))
    profile["views"][name] = {"w": int(right - left + 1), "h": int(bot - top + 1)}
    matte[name] = (m, top, bot, left, right)

rows = 96
prof = {"width": [], "depth": [], "cx": []}
f_m, f_top, f_bot, f_l, f_r = matte["front"]
s_m, s_top, s_bot, s_l, s_r = matte["side"]
fh = f_bot - f_top + 1
sh = s_bot - s_top + 1

for i in range(rows):
    t = i / (rows - 1.0)
    frow = np.nonzero(f_m[int(f_top + t * (fh - 1))])[0]
    srow = np.nonzero(s_m[int(s_top + t * (sh - 1))])[0]
    prof["width"].append(round((frow.max() - frow.min() + 1) / fh, 4) if frow.size else 0.0)
    prof["depth"].append(round((srow.max() - srow.min() + 1) / sh, 4) if srow.size else 0.0)
    prof["cx"].append(round(((frow.min() + frow.max()) * 0.5 - f_l) / fh, 4) if frow.size else 0.0)

profile["profile"] = prof
profile["aspect"] = {
    "front": round((f_r - f_l + 1) / fh, 4),
    "side": round((s_r - s_l + 1) / sh, 4),
    "back": round((matte["back"][4] - matte["back"][3] + 1) /
                  (matte["back"][2] - matte["back"][1] + 1), 4),
}

with open(os.path.join(out, "profile.json"), "w", encoding="utf-8") as fh_out:
    json.dump(profile, fh_out, indent=1)

print("views", {k: (v["w"], v["h"]) for k, v in profile["views"].items()})
print("aspect", profile["aspect"])
print("width", prof["width"][::6])
print("depth", prof["depth"][::6])
