"""Finalise the locomotive: nose to -Y, scaled by height, exported as FBX for Unity, plus renders.
"""
import bpy
import math
import os
from mathutils import Vector

SRC = r"C:\Users\saeed\Downloads\steam locomotive 3d model\steam_locomotive_3d_model.fbx"
TRACK = r"C:\Users\saeed\projects\train-survivor\art-work\train\final"
UNITY = r"C:\Users\saeed\projects\train-survivor\unity\Assets\Resources\Models"
RENDER = r"C:\Users\saeed\Apps\img3d\out\loco3"
for d in (TRACK, UNITY, RENDER):
    os.makedirs(d, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.object.add(type="EMPTY")
bpy.context.view_layer.objects.active = bpy.context.selected_objects[0]
bpy.ops.import_scene.fbx(filepath=SRC)
body = next(o for o in bpy.context.scene.objects if o.type == "MESH")
for o in list(bpy.context.scene.objects):
    if o.type == "EMPTY":
        bpy.data.objects.remove(o, do_unlink=True)

bpy.context.view_layer.objects.active = body
body.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# the export sits diagonally in its own XY plane: align the long axis with X first (PCA), then
# turn the nose from -X onto -Y (Blender forward)
import numpy as np
co = np.array([tuple(body.matrix_world @ v.co) for v in body.data.vertices])
xy = co[:, :2] - co[:, :2].mean(axis=0)
evals, evecs = np.linalg.eigh(np.cov(xy.T))
main = evecs[:, int(np.argmax(evals))]
angle = math.degrees(math.atan2(main[1], main[0]))
print("[loco2] principal axis %.1f deg" % angle)
body.rotation_euler = (0, 0, math.radians(-angle))
bpy.ops.object.transform_apply(rotation=True)

# +90 puts the -X nose onto -Y (a -90 would send it to +Y)
body.rotation_euler = (0, 0, math.radians(90))
bpy.ops.object.transform_apply(rotation=True)

bpy.context.view_layer.update()
bb = [body.matrix_world @ Vector(c) for c in body.bound_box]
body.location = (-(min(v.x for v in bb) + max(v.x for v in bb)) / 2,
                 -(min(v.y for v in bb) + max(v.y for v in bb)) / 2,
                 -min(v.z for v in bb))
bpy.ops.object.transform_apply(location=True)

d = body.dimensions
target_h = 4.2                      # height, in metres — keeps the length believable
s = target_h / max(d.z, 1e-6)
body.scale = (s, s, s)
bpy.ops.object.transform_apply(scale=True)
print("[loco2] scaled x%.2f -> L %.2f  W %.2f  H %.2f m" % (s, *body.dimensions))

bpy.ops.object.shade_smooth()
bpy.ops.object.select_all(action="DESELECT")
body.select_set(True)

fbx_track = os.path.join(TRACK, "locomotive.fbx")
fbx_unity = os.path.join(UNITY, "locomotive.fbx")
for path in (fbx_track, fbx_unity):
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, path_mode="COPY",
                             embed_textures=False, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y")
    print("[loco2] exported", path)

# renders for the user
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.render.resolution_x = 720
scene.render.resolution_y = 460
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
cam_data.ortho_scale = 11.0
target = Vector((0, 0, body.dimensions.z * 0.5))
for name, loc in {"side": (12, 0, body.dimensions.z * 0.5),
                  "front": (0, -12, body.dimensions.z * 0.5),
                  "three_quarter": (8, -9, body.dimensions.z * 0.9)}.items():
    cam.location = loc
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(RENDER, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("[loco2] rendered", name)

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(TRACK, "locomotive.blend"))
print("[loco2] done")
