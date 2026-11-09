"""Align the locomotive: find its long axis (PCA in XY), rotate it onto -Y (Blender's forward),
drop it on the floor, scale to a real locomotive length, save .blend and re-render for checking.
"""
import bpy
import math
import os
import numpy as np
from mathutils import Vector

SRC = r"C:\Users\saeed\Downloads\steam locomotive 3d model\steam_locomotive_3d_model.fbx"
OUT = r"C:\Users\saeed\Apps\img3d\out\loco2"
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.object.add(type="EMPTY")
bpy.context.view_layer.objects.active = bpy.context.selected_objects[0]
bpy.ops.import_scene.fbx(filepath=SRC)
body = next(o for o in bpy.context.scene.objects if o.type == "MESH")
for o in list(bpy.context.scene.objects):
    if o.type == "EMPTY":
        bpy.data.objects.remove(o, do_unlink=True)

# bake the importer's axis conversion first: overwriting rotation_euler without this tips the
# model onto its side (FBX import carries an X rotation)
bpy.context.view_layer.objects.active = body
body.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

bpy.context.view_layer.update()
co = np.array([tuple(body.matrix_world @ v.co) for v in body.data.vertices])
xy = co[:, :2] - co[:, :2].mean(axis=0)
cov = np.cov(xy.T)
evals, evecs = np.linalg.eigh(cov)
main = evecs[:, np.argmax(evals)]
angle = math.degrees(math.atan2(main[1], main[0]))
print("[align] principal axis %.1f deg, extents %s" % (angle, np.ptp(xy, axis=0).round(2)))

# rotate the long axis onto X, then the nose onto -Y
body.rotation_euler = (0, 0, math.radians(-angle))
bpy.ops.object.transform_apply(rotation=True)

# centre on X/Y, floor it
bpy.context.view_layer.update()
bb = [body.matrix_world @ Vector(c) for c in body.bound_box]
cx = (min(v.x for v in bb) + max(v.x for v in bb)) / 2
cy = (min(v.y for v in bb) + max(v.y for v in bb)) / 2
zmin = min(v.z for v in bb)
body.location = (-cx, -cy, -zmin)
bpy.ops.object.transform_apply(location=True)

d = body.dimensions
print("[align] after: length(X) %.2f width(Y) %.2f height(Z) %.2f" % (d.x, d.y, d.z))

# scale so the loco is 11 m long — roughly the game's locomotive scale
target_len = 11.0
s = target_len / max(d.x, 0.001)
body.scale = (s, s, s)
bpy.ops.object.transform_apply(scale=True)
print("[align] scaled by %.3f -> %.1f x %.1f x %.1f m" % (s, *body.dimensions))

# render side and front to check which end is the cowcatcher
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.render.resolution_x = 640
scene.render.resolution_y = 420
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
cam_data.ortho_scale = 13.0
target = Vector((0, 0, body.dimensions.z * 0.5))

for name, loc in {"side_+X": (16, 0, body.dimensions.z * 0.55),
                  "nose_-Y": (0, -16, body.dimensions.z * 0.55),
                  "nose_+Y": (0, 16, body.dimensions.z * 0.55)}.items():
    cam.location = loc
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("[align] rendered", name)

bpy.ops.wm.save_as_mainfile(filepath=r"C:\Users\saeed\Apps\img3d\out\loco2\loco_aligned.blend")
print("[align] saved blend")
