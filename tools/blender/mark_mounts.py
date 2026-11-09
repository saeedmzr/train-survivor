"""Render top views of the loco and wagon with candidate gun mounts marked, so the plates can be
matched to coordinates by eye.
"""
import bpy
import math
import os
from mathutils import Vector

TRACK = r"C:\Users\saeed\projects\train-survivor\art-work\train\final"
OUT = r"C:\Users\saeed\Apps\img3d\out\mounts"
os.makedirs(OUT, exist_ok=True)

CANDIDATES = {
    "locomotive": [
        ("A", (0.02, 2.49, 3.92)),
        ("B", (0.08, 0.06, 3.39)),
        ("C", (0.03, -1.91, 3.55)),
        ("D", (0.00, -2.80, 3.30)),
    ],
    "wagon": [
        ("A", (0.00, 0.93, 2.93)),
        ("B", (0.00, -1.34, 2.88)),
    ],
}

for name, marks in CANDIDATES.items():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.object.add(type="EMPTY")
    bpy.context.view_layer.objects.active = bpy.context.selected_objects[0]
    bpy.ops.import_scene.fbx(filepath=os.path.join(TRACK, name + ".fbx"))
    body = next(o for o in bpy.context.scene.objects if o.type == "MESH")
    for o in list(bpy.context.scene.objects):
        if o.type == "EMPTY":
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.context.view_layer.objects.active = body
    body.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    for tag, pos in marks:
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.22, location=pos)
        s = bpy.context.object
        mat = bpy.data.materials.new("mark" + tag)
        mat.use_nodes = True
        mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (1, 0, 0, 1)
        s.data.materials.append(mat)

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 620
    scene.render.resolution_y = 780
    world = bpy.data.worlds.new("W")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.35, 0.38, 0.45, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 1.8
    sun_data = bpy.data.lights.new("Sun", type="SUN")
    sun_data.energy = 4.0
    sun = bpy.data.objects.new("Sun", sun_data)
    sun.rotation_euler = (math.radians(50), 0, math.radians(30))
    scene.collection.objects.link(sun)

    cam_data = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = max(body.dimensions.y, body.dimensions.x) * 1.15
    cam.location = (0, 0, 14)
    cam.rotation_euler = (0, 0, 0)
    scene.render.filepath = os.path.join(OUT, name + "_top.png")
    bpy.ops.render.render(write_still=True)
    print("[mnt] rendered", name, [t for t, _ in marks])
