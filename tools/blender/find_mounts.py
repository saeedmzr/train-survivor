"""Find the gun-mount plates on the locomotive and wagon.

The plates are raised discs on the roof; they are modelled as separate islands. Report every
island's centroid, size and vertex count so the plates can be picked out by position and diameter.
"""
import bpy
import bmesh
import os
import sys
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
FILES = argv if argv else [
    r"C:\Users\saeed\projects\train-survivor\art-work\train\final\locomotive.fbx",
    r"C:\Users\saeed\projects\train-survivor\art-work\train\final\wagon.fbx",
]

for path in FILES:
    if not os.path.exists(path):
        print("[mount] missing", path)
        continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.object.add(type="EMPTY")
    bpy.context.view_layer.objects.active = bpy.context.selected_objects[0]
    bpy.ops.import_scene.fbx(filepath=path)
    body = next(o for o in bpy.context.scene.objects if o.type == "MESH")
    for o in list(bpy.context.scene.objects):
        if o.type == "EMPTY":
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.context.view_layer.objects.active = body
    body.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    me = body.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    seen = set()
    islands = []
    for v in bm.verts:
        if v.index in seen:
            continue
        stack = [v]; comp = []
        seen.add(v.index)
        while stack:
            cur = stack.pop(); comp.append(cur.index)
            for e in cur.link_edges:
                o = e.other_vert(cur)
                if o.index not in seen:
                    seen.add(o.index); stack.append(o)
        islands.append(comp)
    bm.free()

    verts = np.array([tuple(me.vertices[i].co) for i in range(len(me.vertices))])
    lo, hi = verts.min(axis=0), verts.max(axis=0)
    size = hi - lo
    print("\n[mount] %s  total %d verts, %d islands, size %.2f x %.2f x %.2f"
          % (os.path.basename(path), len(me.vertices), len(islands), *size))

    rows = []
    for comp in islands:
        c = verts[comp]
        clo, chi = c.min(axis=0), c.max(axis=0)
        dims = chi - clo
        centre = (clo + chi) / 2
        # plate-ish: near-square in XY, thin in Z, sitting near the top of the body
        if len(comp) < 12:
            continue
        flat = dims[2] < max(dims[0], dims[1]) * 0.35
        round_ish = abs(dims[0] - dims[1]) < max(dims[0], dims[1]) * 0.35
        top = centre[2] > lo[2] + size[2] * 0.55
        if flat and round_ish and top:
            rows.append((centre, dims, len(comp)))

    # every island on the top surface, regardless of shape, so plate clusters are visible
    tops = []
    for comp in islands:
        c = verts[comp]
        clo, chi = c.min(axis=0), c.max(axis=0)
        dims = chi - clo
        centre = (clo + chi) / 2
        if centre[2] > lo[2] + size[2] * 0.72 and len(comp) >= 30:
            tops.append((centre, dims, len(comp)))
    tops.sort(key=lambda r: -r[0][1])
    print("[mount]   -- top islands (y sorted) --")
    for centre, dims, n in tops[:14]:
        print("[mount]     top centre (%.2f, %.2f, %.2f) dims %.2f x %.2f x %.2f verts %d"
              % (centre[0], centre[1], centre[2], dims[0], dims[1], dims[2], n))

    rows.sort(key=lambda r: -r[0][2])
    print("[mount]   -- candidates --")
    for centre, dims, n in rows[:12]:
        print("[mount]   plate? centre (%.2f, %.2f, %.2f)  dims %.2f x %.2f x %.2f  verts %d"
              % (centre[0], centre[1], centre[2], dims[0], dims[1], dims[2], n))
