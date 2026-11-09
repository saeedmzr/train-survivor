"""Align a Tripo FBX for Unity: bake the importer transform, put the long axis on Y, centre it,
drop it on the floor, scale to a target, export to Unity's Resources and the tracking folder.

    blender -b -P align_asset.py -- <src.fbx> <name> <target> <length|height>
"""
import bpy
import math
import os
import sys
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, name, target, mode = argv[0], argv[1], float(argv[2]), argv[3]
# optional 5th arg: tracking subfolder under art-work (default train/final)
track_sub = argv[4] if len(argv) > 4 else "train/final"

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
TRACK = os.path.join(REPO, "art-work", *track_sub.split("/"))
UNITY = os.path.join(REPO, "unity", "Assets", "Resources", "Models")
RENDER = os.path.join(REPO, "..", "img3d-render-check")
for d in (TRACK, UNITY, RENDER):
    os.makedirs(d, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.object.add(type="EMPTY")
bpy.context.view_layer.objects.active = bpy.context.selected_objects[0]
bpy.ops.import_scene.fbx(filepath=src)
body = next(o for o in bpy.context.scene.objects if o.type == "MESH")
for o in list(bpy.context.scene.objects):
    if o.type == "EMPTY":
        bpy.data.objects.remove(o, do_unlink=True)

bpy.context.view_layer.objects.active = body
body.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# long horizontal axis -> X
co = np.array([tuple(body.matrix_world @ v.co) for v in body.data.vertices])
xy = co[:, :2] - co[:, :2].mean(axis=0)
evals, evecs = np.linalg.eigh(np.cov(xy.T))
main = evecs[:, int(np.argmax(evals))]
angle = math.degrees(math.atan2(main[1], main[0]))
body.rotation_euler = (0, 0, math.radians(-angle))
bpy.ops.object.transform_apply(rotation=True)

# +90 so the -X end lands on -Y (forward)
body.rotation_euler = (0, 0, math.radians(90))
bpy.ops.object.transform_apply(rotation=True)

bpy.context.view_layer.update()
bb = [body.matrix_world @ Vector(c) for c in body.bound_box]
body.location = (-(min(v.x for v in bb) + max(v.x for v in bb)) / 2,
                 -(min(v.y for v in bb) + max(v.y for v in bb)) / 2,
                 -min(v.z for v in bb))
bpy.ops.object.transform_apply(location=True)

d = body.dimensions
ref = d.y if mode == "length" else d.z
s = target / max(ref, 1e-6)
body.scale = (s, s, s)
bpy.ops.object.transform_apply(scale=True)
print("[as] %-10s raw %.2f x %.2f x %.2f -> scaled x%.2f -> L %.2f W %.2f H %.2f" %
      (name, d.x, d.y, d.z, s, *body.dimensions))

for path in (os.path.join(TRACK, name + ".fbx"), os.path.join(UNITY, name + ".fbx")):
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, path_mode="COPY",
                             embed_textures=False, axis_forward="-Z", axis_up="Y")
print("[as] exported", name)

# renders
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.render.resolution_x = 560
scene.render.resolution_y = 400
world = bpy.data.worlds.new("W")
scene.world = world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.35, 0.38, 0.45, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 1.6
sun_data = bpy.data.lights.new("Sun", type="SUN")
sun_data.energy = 4.0
sun = bpy.data.objects.new("Sun", sun_data)
sun.rotation_euler = (math.radians(55), 0, math.radians(35))
scene.collection.objects.link(sun)
cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
cam_data.type = "ORTHO"
cam_data.ortho_scale = max(body.dimensions) * 1.5
target_v = Vector((0, 0, body.dimensions.z * 0.5))
for tag, loc in {"front": (0, -8, body.dimensions.z * 0.6),
                 "side": (8, 0, body.dimensions.z * 0.6),
                 "iso": (6, -6, body.dimensions.z * 1.1)}.items():
    cam.location = loc
    cam.rotation_euler = (target_v - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(RENDER, "%s_%s.png" % (name, tag))
    bpy.ops.render.render(write_still=True)
print("[as] done", name)
