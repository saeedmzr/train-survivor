"""Measure the supplied rail and sleeper models and write them into the Unity project.

Rail: samples the cross-section (min/max X per Z slice) so the game can sweep that profile
along the route instead of tiling 1 m pieces (no seams through curves).
Sleeper: reports the metal baseplate positions so the prefab can be scaled to the real gauge,
and exports a decimated copy for the runtime instancing budget.
"""
import bpy, sys, json, os
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
RAIL, SLEEPER, OUT = argv[0], argv[1], argv[2]

def load(p):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=p)
    return [o for o in bpy.data.objects if o.type == "MESH"]

def world_verts(objs):
    vs = []
    for o in objs:
        vs += [o.matrix_world @ v.co for v in o.data.vertices]
    return vs

# ---------------- rail ----------------
load(RAIL)
vs = world_verts([o for o in bpy.data.objects if o.type == "MESH"])
zs = [v.z for v in vs]; z0, z1 = min(zs), max(zs)
BINS = 60
prof = []
for i in range(BINS + 1):
    z = z0 + (z1 - z0) * i / BINS
    tol = (z1 - z0) * 0.022
    band = [v for v in vs if abs(v.z - z) <= tol]
    if not band: continue
    xl, xr = min(v.x for v in band), max(v.x for v in band)
    # the web is millimetres thin: a band can miss both walls, and a zero-width ring
    # produces degenerate triangles. Keep a floor and stay symmetric about the centre.
    c, hw = (xl + xr) * 0.5, max((xr - xl) * 0.5, 0.0035)
    prof.append([round(z - z0, 5), round(c - hw, 5), round(c + hw, 5)])
rail_len = max(v.y for v in vs) - min(v.y for v in vs)
rail = {"length": round(rail_len, 4), "height": round(z1 - z0, 4),
        "width": round(max(v.x for v in vs) - min(v.x for v in vs), 4), "profile": prof}

# ---------------- sleeper ----------------
load(SLEEPER)
objs = [o for o in bpy.data.objects if o.type == "MESH"]
vs = world_verts(objs)
xs = [v.x for v in vs]; ys = [v.y for v in vs]; zs = [v.z for v in vs]
sx0, sx1 = min(xs), max(xs); sz0, sz1 = min(zs), max(zs)
top = sz1 - (sz1 - sz0) * 0.18
plate = [v for v in vs if v.z > top]
left = [v.x for v in plate if v.x < 0]; right = [v.x for v in plate if v.x >= 0]
sleep = {"length": round(sx1 - sx0, 4), "width": round(max(ys) - min(ys), 4),
         "height": round(sz1 - sz0, 4),
         "plate_span": round((sum(right) / max(1, len(right))) - (sum(left) / max(1, len(left))), 4),
         "plate_x_left": round(sum(left) / max(1, len(left)), 4),
         "plate_x_right": round(sum(right) / max(1, len(right)), 4),
         "plate_top_z": round(sz1 - sz0, 4)}
sleep["tris_before"] = sum(len(p.vertices) - 2 for o in objs for p in o.data.polygons)

# decimate for runtime
for o in objs:
    m = o.modifiers.new("dec", "DECIMATE"); m.ratio = 0.045
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=m.name)
sleep["tris_after"] = sum(len(p.vertices) - 2 for o in objs for p in o.data.polygons)
for o in objs:
    o.select_set(True)
bpy.context.view_layer.objects.active = objs[0]
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "railway_sleeper.fbx"),
                         use_selection=True, apply_scale_options="FBX_SCALE_ALL",
                         object_types={"MESH"}, path_mode="COPY")

json.dump({"rail": rail, "sleeper": sleep}, open(os.path.join(OUT, "track_spec.json"), "w"), indent=1)
print("[spec]", json.dumps(rail)[:180])
print("[sleeper]", json.dumps(sleep))
