"""Visual-hull mesh from the Walker turnaround mattes.

A voxel is inside the body when its (x,z) projection lies inside the front silhouette and its
(y,z) projection lies inside the side silhouette. Marching cubes turns that occupancy grid into
a surface, so the mesh reproduces the measured silhouette exactly instead of approximating it
with primitives. Colour is baked per vertex from the front/back renders.

    python make_hull.py <refs_dir> <out_dir> [voxel_m]

Writes <out>/walker_hull.obj (+ .mtl, vertex colours) and <out>/hull_info.json.
"""
import json
import os
import sys

import numpy as np
import trimesh
from PIL import Image
from skimage import measure
from scipy import ndimage

refs = sys.argv[1]
out = sys.argv[2]
vox = float(sys.argv[3]) if len(sys.argv) > 3 else 0.008
os.makedirs(out, exist_ok=True)

H = 2.0  # character height, metres


def mask_of(name):
    im = Image.open(os.path.join(refs, name)).convert("RGBA")
    a = np.asarray(im)
    m = a[:, :, 3] > 128
    # the matte keeps a soft edge; close it so the shell has no pin-holes
    m = ndimage.binary_closing(m, np.ones((5, 5)))
    m = ndimage.binary_fill_holes(m)
    return m, np.asarray(im.convert("RGB"))


front_m, front_rgb = mask_of("front.png")
side_m, _ = mask_of("side.png")
back_m, back_rgb = mask_of("back.png")

fh, fw = front_m.shape
sh, sw = side_m.shape

# metres per pixel, per view, so both silhouettes agree on height
mpf = H / fh
mps = H / sh

xs = np.arange(-0.45, 0.45, vox)
ys = np.arange(-0.30, 0.30, vox)
zs = np.arange(0.0, H, vox)

# front: u = x, v = z ; side: u = y (side view faces +x, so depth maps to y)
fu = np.clip(((xs + 0.45) * 0 + (xs / mpf) + fw * 0.5).astype(int), 0, fw - 1)
# simpler: map metre offsets to pixels around the silhouette's own centre
front_cx = np.array(np.nonzero(front_m)[1]).mean()
side_cx = np.array(np.nonzero(side_m)[1]).mean()

def front_col(x):
    return int(round(front_cx + x / mpf))

def side_col(y):
    return int(round(side_cx + y / mps))

occ = np.zeros((len(xs), len(ys), len(zs)), dtype=bool)
for k, z in enumerate(zs):
    row = int(round((H - z) / mpf))
    if row < 0 or row >= fh:
        continue
    frow = front_m[row]
    col_idx = np.clip([front_col(x) for x in xs], 0, fw - 1)
    x_ok = frow[col_idx]

    srow = side_m[min(max(int(round((H - z) / mps)), 0), sh - 1)]
    scol = np.clip([side_col(y) for y in ys], 0, sw - 1)
    y_ok = srow[scol]

    occ[:, :, k] = x_ok[:, None] & y_ok[None, :]

print("voxels", occ.shape, "filled", int(occ.sum()))
if occ.sum() < 1000:
    raise SystemExit("occupancy grid is empty — check the mattes")

# pad so the surface closes at the feet and the crown
pad = np.pad(occ.astype(np.float32), 2, mode="constant")
verts, faces, _n, _v = measure.marching_cubes(pad, level=0.5, spacing=(vox, vox, vox))
verts -= 2 * vox
verts[:, 0] += -0.45
verts[:, 1] += -0.30

mesh = trimesh.Trimesh(vertices=verts, faces=faces, process=True)
# the occupancy grid is binary, so the raw surface is stair-stepped; smooth it hard before
# decimating, otherwise the quadric pass flattens the steps into visible plates
trimesh.smoothing.filter_taubin(mesh, lamb=0.55, nu=-0.58, iterations=30)
mesh = mesh.simplify_quadric_decimation(face_count=14000)
mesh.fix_normals()

print("mesh verts", len(mesh.vertices), "faces", len(mesh.faces), "bounds", mesh.bounds.round(2).tolist())

# per-vertex colour from the front render, mirrored on the back faces. The back matte in the
# sheet is cropped at the knees, so it cannot be mapped by height — front is used both ways.
v = mesh.vertices
n = mesh.vertex_normals
cols = np.zeros((len(v), 4), dtype=np.uint8)
for i in range(len(v)):
    x, _y, z = v[i]
    row = int(round((H - z) / mpf))
    row = min(max(row, 0), fh - 1)
    if n[i][1] <= 0.0:
        col = int(round(front_cx + x / mpf))
    else:
        col = int(round(front_cx - x / mpf))
    col = min(max(col, 0), fw - 1)
    r, g, b = front_rgb[row, col]
    cols[i] = (r, g, b, 255)

mesh.visual = trimesh.visual.ColorVisuals(mesh=mesh, vertex_colors=cols)
mesh.export(os.path.join(out, "walker_hull.obj"))
mesh.export(os.path.join(out, "walker_hull.ply"))   # PLY keeps vertex colours into Blender

info = {
    "verts": int(len(mesh.vertices)),
    "faces": int(len(mesh.faces)),
    "bounds": mesh.bounds.round(4).tolist(),
    "voxel_m": vox,
}
with open(os.path.join(out, "hull_info.json"), "w", encoding="utf-8") as fh:
    json.dump(info, fh, indent=1)
print("wrote", os.path.join(out, "walker_hull.obj"))
