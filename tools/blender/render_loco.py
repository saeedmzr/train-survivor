"""Quick 4-view render of the locomotive so its shape and forward axis are clear."""
import bpy
import math
from mathutils import Vector

SRC = r"C:\Users\saeed\Downloads\steam locomotive 3d model\steam_locomotive_3d_model.fbx"
OUT = r"C:\Users\saeed\Apps\img3d\out\loco"
import os
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.object.add(type="EMPTY")
bpy.context.view_layer.objects.active = bpy.context.selected_objects[0]
bpy.ops.import_scene.fbx(filepath=SRC)

body = next(o for o in bpy.context.scene.objects if o.type == "MESH")
for o in bpy.context.scene.objects:
    if o.type == "EMPTY":
        bpy.data.objects.remove(o, do_unlink=True)

# centre on origin, feet at z=0
bpy.context.view_layer.update()
bb = [body.matrix_world @ Vector(c) for c in body.bound_box]
cx = (min(v.x for v in bb) + max(v.x for v in bb)) / 2
cy = (min(v.y for v in bb) + max(v.y for v in bb)) / 2
zmin = min(v.z for v in bb)
body.location.x -= cx
body.location.y -= cy
body.location.z -= zmin
bpy.ops.object.transform_apply(location=True)

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.render.resolution_x = 480
scene.render.resolution_y = 380
scene.render.film_transparent = False
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
cam_data.ortho_scale = 1.6

views = {
    "front_-Y": (0.0, -3.0, 0.45, math.radians(90), 0, 0),
    "right_+X": (3.0, 0.0, 0.45, math.radians(90), 0, math.radians(90)),
    "back_+Y": (0.0, 3.0, 0.45, math.radians(90), 0, math.radians(180)),
    "top": (0.0, 0.0, 3.0, 0, 0, 0),
}
target = Vector((0, 0, 0.3))
for name, (x, y, z, rx, ry, rz) in views.items():
    cam.location = (x, y, z)
    direction = target - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("[loco] rendered", name)
