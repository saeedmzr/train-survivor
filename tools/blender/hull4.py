"""Four-view visual hull + per-view vertex colouring for the Walker.

Inputs are single-view orthographic renders (front, back, both sides, A-pose, flat backdrop).
A voxel is inside the body when it projects inside the front and the side silhouettes; colour
comes from whichever view faces the surface, so the sides are no longer a mirrored front.

    python hull4.py <front.jpg> <back.jpg> <sideL.jpg> <sideR.jpg> <out_dir> [voxel_m]
"""
import json
import os
import sys

import numpy as np
import trimesh
from PIL import Image
from scipy import ndimage
from skimage import measure

front_p, back_p, sl_p, sr_p, out = sys.argv[1:6]
vox = float(sys.argv[6]) if len(sys.argv) > 6 else 0.005
os.makedirs(out, exist_ok=True)
H = 2.0


def matte(path):
    """Return (mask, rgb) trimmed to the figure, dropping the WALKER caption."""
    im = Image.open(path).convert("RGB")
    a = np.asarray(im).astype(np.int16)
    h, w = a.shape[:2]
    edge = np.concatenate([a[:, :12], a[:, -12:]], axis=1)
    bg = np.median(edge, axis=1).astype(np.int16)
    dist = np.abs(a - bg[:, None, :]).sum(axis=2)
    m = ndimage.binary_opening(dist > 42, np.ones((5, 5)))

    lab, n = ndimage.label(m)
    sizes = ndimage.sum(m, lab, range(1, n + 1))
    # the figure is the tallest blob; the caption is wide and short
    best, best_h = None, 0
    for i, sl in enumerate(ndimage.find_objects(lab, n), start=1):
        if sl is None:
            continue
        hh = sl[0].stop - sl[0].start
        if hh > best_h and sizes[i - 1] > 0.02 * sizes.sum():
            best, best_h = i, hh
    blob = ndimage.binary_closing(lab == best, np.ones((5, 5)))
    blob = ndimage.binary_fill_holes(blob)

    ys, xs = np.nonzero(blob)
    t, b, l, r = ys.min(), ys.max(), xs.min(), xs.max()
    return blob[t:b + 1, l:r + 1], a[t:b + 1, l:r + 1].astype(np.uint8)


fm, frgb = matte(front_p)
bm, brgb = matte(back_p)
lm, lrgb = matte(sl_p)
rm, rrgb = matte(sr_p)
print("figure px  front %s  back %s  sideL %s  sideR %s" %
      (fm.shape, bm.shape, lm.shape, rm.shape))

# keep the trimmed views as textures: the material projects these per pixel, which is the only
# way the character keeps the detail of the source renders (vertex colour averages it away)
for name, (m, rgb) in {"front": (fm, frgb), "back": (bm, brgb),
                       "left": (lm, lrgb), "right": (rm, rrgb)}.items():
    rgba = np.dstack([rgb, (m * 255).astype(np.uint8)])
    Image.fromarray(rgba, "RGBA").save(os.path.join(out, "view_%s.png" % name))

fh, fw = fm.shape
sh, sw = lm.shape
mpf = H / fh          # metres per pixel, front
mps = H / sh          # metres per pixel, side

# silhouette centres, in pixels
fcx = np.array(np.nonzero(fm)[1]).mean()
scx = np.array(np.nonzero(lm)[1]).mean()

xs = np.arange(-0.46, 0.46, vox)
ys = np.arange(-0.34, 0.34, vox)
zs = np.arange(0.0, H, vox)

occ = np.zeros((len(xs), len(ys), len(zs)), dtype=bool)

# the side silhouettes include the arms hanging in front of the torso, which makes the hull
# throw a plate across the chest; median-filter the per-row depth interval so the body depth
# follows the torso and the arm bulge no longer punches through the whole width
side_rows = []
for k, z in enumerate(zs):
    sr = int(round((H - z) / mps - 0.5))
    if 0 <= sr < sh:
        idx = np.nonzero(lm[sr])[0]
        side_rows.append(((idx.min() - scx) * mps, (idx.max() - scx) * mps) if idx.size else (0.0, 0.0))
    else:
        side_rows.append((0.0, 0.0))

lo_s = np.array([r[0] for r in side_rows])
hi_s = np.array([r[1] for r in side_rows])
win = 21
padw = win // 2
lo_s = ndimage.median_filter(np.pad(lo_s, padw, mode="edge"), size=win)[padw:-padw]
hi_s = ndimage.median_filter(np.pad(hi_s, padw, mode="edge"), size=win)[padw:-padw]

for k, z in enumerate(zs):
    fr = int(round((H - z) / mpf - 0.5))
    if not (0 <= fr < fh):
        continue
    frow = fm[fr]
    fcols = np.clip(np.round(fcx + xs / mpf).astype(int), 0, fw - 1)
    y_ok = (ys >= lo_s[k]) & (ys <= hi_s[k])
    occ[:, :, k] = frow[fcols][:, None] & y_ok[None, :]

filled = int(occ.sum())
print("grid", occ.shape, "filled", filled)
if filled < 2000:
    raise SystemExit("empty occupancy grid")

pad = np.pad(occ.astype(np.float32), 2)
verts, faces, _n, _v = measure.marching_cubes(pad, level=0.5, spacing=(vox, vox, vox))
verts -= 2 * vox
verts[:, 0] += -0.46
verts[:, 1] += -0.34

mesh = trimesh.Trimesh(vertices=verts, faces=faces, process=True)
trimesh.smoothing.filter_taubin(mesh, lamb=0.55, nu=-0.58, iterations=26)
mesh = mesh.simplify_quadric_decimation(face_count=16000)
mesh.fix_normals()
print("mesh verts", len(mesh.vertices), "faces", len(mesh.faces),
      "bounds", mesh.bounds.round(2).tolist())

# ---------------------------------------------------------------- colour by facing view
views = {
    "front": (fm, frgb, mpf, fcx),
    "back": (bm, brgb, None, None),
    "left": (lm, lrgb, mps, scx),
    "right": (rm, rrgb, None, None),
}
v = mesh.vertices
n = mesh.vertex_normals
cols = np.zeros((len(v), 4), dtype=np.uint8)

def sample_view(m, rgb, mpp, z, offset_m):
    """Sample a view at height z, offset_m metres to the right of the silhouette centre."""
    row = int(round((H - z) * m.shape[0] / H - 0.5))
    row = min(max(row, 0), m.shape[0] - 1)
    idx = np.nonzero(m[row])[0]
    if idx.size == 0:
        return (60, 60, 60)
    centre = (idx.min() + idx.max()) * 0.5
    colpix = int(round(centre + offset_m / mpp))
    if colpix < 0 or colpix >= m.shape[1] or not m[row, colpix]:
        # outside this view's silhouette: take the nearest covered pixel in the row
        colpix = int(np.clip(colpix, idx.min(), idx.max()))
    r, g, b = rgb[row, colpix]
    return (int(r), int(g), int(b))


for i in range(len(v)):
    x, y, z = v[i]
    nx, ny, _nz = n[i]
    if abs(ny) >= abs(nx):
        if ny <= 0:
            rgbv = sample_view(fm, frgb, mpf, z, x)
        else:
            rgbv = sample_view(bm, brgb, mpf, z, -x)
    else:
        if nx <= 0:
            rgbv = sample_view(lm, lrgb, mps, z, y)
        else:
            rgbv = sample_view(rm, rrgb, mps, z, -y)
    cols[i] = (*rgbv, 255)

mesh.visual = trimesh.visual.ColorVisuals(mesh=mesh, vertex_colors=cols)
mesh.export(os.path.join(out, "walker_hull.obj"))
mesh.export(os.path.join(out, "walker_hull.ply"))

with open(os.path.join(out, "hull_info.json"), "w", encoding="utf-8") as fh:
    json.dump({"verts": int(len(mesh.vertices)), "faces": int(len(mesh.faces)),
               "bounds": mesh.bounds.round(4).tolist(), "voxel": vox}, fh, indent=1)
print("wrote", os.path.join(out, "walker_hull.ply"))
