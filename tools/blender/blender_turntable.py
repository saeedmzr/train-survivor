"""Render sprite frames from a 3D model in Blender, matching the game's billboard look.

    blender -b -P blender_turntable.py -- model.glb out_dir frames height_px [angle_deg]

The model is normalised to a height of 2.0 Blender units (the rig.json convention), lit with a
single key + fill that matches M0Bootstrap's sun/fill pair, and rendered with a transparent
film so the frames drop straight into the enemy atlas pipeline.

Output: out_dir/turn_00.png ... one PNG per frame, feet on the bottom edge, centred.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
if len(argv) < 4:
    print("usage: -- model.glb out_dir frames height_px [angle_deg]")
    sys.exit(1)

glb_path = os.path.abspath(argv[0])
out_dir = os.path.abspath(argv[1])
frames = int(argv[2])
height_px = int(argv[3])
angle_deg = float(argv[4]) if len(argv) > 4 else 15.0

os.makedirs(out_dir, exist_ok=True)

# --- clean scene -------------------------------------------------------------------------
bpy.ops.wm.read_factory_settings(use_empty=True)

# --- import ------------------------------------------------------------------------------
# xatlas (TripoSR's texture-bake exporter) writes OBJ text even when the path says .glb,
# so sniff the first bytes instead of trusting the extension.
with open(glb_path, "rb") as fh:
    head = fh.read(4)
if head[:4] == b"glTF":
    bpy.ops.import_scene.gltf(filepath=glb_path)
elif head[:2] in (b"v ", b"# "):
    if not glb_path.lower().endswith((".obj", ".mtl")):
        obj_path = glb_path.rsplit(".", 1)[0] + ".obj"
        with open(obj_path, "wb") as out, open(glb_path, "rb") as src:
            out.write(src.read())
        glb_path = obj_path
    bpy.ops.wm.obj_import(filepath=glb_path)
else:
    bpy.ops.wm.obj_import(filepath=glb_path)

meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
if not meshes:
    print("[turntable] no meshes imported")
    sys.exit(2)

# baked texture lives next to the mesh (TripoSR --bake-texture)
tex_path = os.path.join(os.path.dirname(glb_path), "texture.png")
if os.path.exists(tex_path):
    img = bpy.data.images.load(tex_path)
    mat = bpy.data.materials.new("baked")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.8
    bsdf.inputs["Metallic"].default_value = 0.0
    tex_node = mat.node_tree.nodes.new("ShaderNodeTexImage")
    tex_node.image = img
    tex_node.interpolation = "Linear"
    mat.node_tree.links.new(tex_node.outputs["Color"], bsdf.inputs["Base Color"])
    for ob in meshes:
        ob.data.materials.clear()
        ob.data.materials.append(mat)
    print("[turntable] applied", tex_path)

# --- orient: TripoSR's mesh is Y-up, the game (and this camera) is Z-up ---------------------
# Rotate the objects themselves (all origins sit at the world origin) instead of a parent empty:
# the parent approach left matrix_world stale in background mode and produced a lying-down rig.
for ob in meshes:
    ob.rotation_euler = (math.radians(-90), 0.0, 0.0)
bpy.context.view_layer.update()

# --- normalise: height 2.0, centred on X/Y, feet at z=0 -----------------------------------
mins = Vector((1e9, 1e9, 1e9))
maxs = Vector((-1e9, -1e9, -1e9))
for ob in meshes:
    for corner in ob.bound_box:
        world = ob.matrix_world @ Vector(corner)
        mins = Vector((min(mins[i], world[i]) for i in range(3)))
        maxs = Vector((max(maxs[i], world[i]) for i in range(3)))

size = maxs - mins
scale = 2.0 / size.z if size.z > 1e-6 else 1.0
center = Vector(((mins.x + maxs.x) / 2, (mins.y + maxs.y) / 2, mins.z))

for ob in meshes:
    ob.scale = (scale, scale, scale)
    ob.location = (-center.x * scale, -center.y * scale, -center.z * scale)
bpy.context.view_layer.update()

print("[turntable] bbox", tuple(round(v, 3) for v in size), "scale", round(scale, 3))

# --- lighting: matches BuildLighting (sun from 42 deg elevation, cool fill from the front) --
sun_data = bpy.data.lights.new("sun", type="SUN")
sun_data.energy = 4.0
sun_data.color = (1.0, 0.93, 0.82)
sun_data.angle = math.radians(8)
sun = bpy.data.objects.new("sun", sun_data)
sun.rotation_euler = (math.radians(52), 0.0, math.radians(155))
bpy.context.scene.collection.objects.link(sun)

fill_data = bpy.data.lights.new("fill", type="SUN")
fill_data.energy = 1.6
fill_data.color = (0.45, 0.6, 1.0)
fill = bpy.data.objects.new("fill", fill_data)
fill.rotation_euler = (math.radians(-30), 0.0, math.radians(20))
bpy.context.scene.collection.objects.link(fill)

world = bpy.data.worlds.new("w")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.09, 0.11, 0.16, 1.0)
world.node_tree.nodes["Background"].inputs[1].default_value = 1.4
bpy.context.scene.world = world

# --- camera: the game looks down the track from ~14 deg above ---------------------------------
# frame from the *actual* world bbox after normalisation, so a wide model cannot fall out of shot
bpy.context.view_layer.update()
wmin = Vector((1e9, 1e9, 1e9))
wmax = Vector((-1e9, -1e9, -1e9))
for ob in meshes:
    for corner in ob.bound_box:
        w = ob.matrix_world @ Vector(corner)
        wmin = Vector((min(wmin[i], w[i]) for i in range(3)))
        wmax = Vector((max(wmax[i], w[i]) for i in range(3)))
wsize = wmax - wmin
wcenter = (wmin + wmax) * 0.5
print("[turntable] world bbox", tuple(round(v, 3) for v in wsize), "center", tuple(round(v, 3) for v in wcenter))

cam_data = bpy.data.cameras.new("cam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = max(wsize.x, wsize.z, wsize.y) * 1.35

# elevated view like the game camera, aimed with a look-at so the tilt can never slide the
# subject out of frame (a fixed euler tilt does exactly that)
elev = math.radians(angle_deg)
dist = 10.0
cam_offset = Vector((0.0, -dist * math.cos(elev), dist * math.sin(elev)))
cam = bpy.data.objects.new("cam", cam_data)
cam.location = wcenter + cam_offset
cam.rotation_euler = (wcenter - cam.location).to_track_quat("-Z", "Y").to_euler()
bpy.context.scene.collection.objects.link(cam)
bpy.context.scene.camera = cam

# --- render settings ----------------------------------------------------------------------
scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 32
scene.cycles.use_denoising = True
scene.render.resolution_x = height_px
scene.render.resolution_y = height_px
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.view_settings.view_transform = "Standard"

# --- frames -------------------------------------------------------------------------------
for i in range(frames):
    for ob in meshes:
        ob.rotation_euler = (math.radians(-90), 0.0, math.radians(360.0 * i / frames))
    bpy.context.view_layer.update()
    scene.render.filepath = os.path.join(out_dir, "turn_%02d.png" % i)
    bpy.ops.render.render(write_still=True)
    print("[turntable] wrote", scene.render.filepath)

print("[turntable] done:", frames, "frames at", height_px, "px")
