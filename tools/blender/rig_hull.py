"""Rig, colour and render the visual-hull Walker.

    blender -b -P rig_hull.py -- <hull.obj> <refs_dir> <out_dir> [turn_frames] [walk_frames]

Colour is sampled per vertex from the front render (mirrored on the back), the armature uses
proportions measured off the same turnaround, and the output is a .blend/.glb plus rendered
turntable and walk frame sequences.
"""
import json
import math
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
hull = os.path.abspath(argv[0])
refs = os.path.abspath(argv[1])
out = os.path.abspath(argv[2])
turn_frames = int(argv[3]) if len(argv) > 3 else 8
walk_frames = int(argv[4]) if len(argv) > 4 else 12
os.makedirs(out, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

# trimesh writes Z-up vertices; Blender's OBJ importer assumes Y-up by default and would lay
# the character on its back, so the axes are stated explicitly
bpy.ops.wm.obj_import(filepath=hull, forward_axis="Y", up_axis="Z")
body = [o for o in scene.objects if o.type == "MESH"][0]
body.name = "Walker"
body.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.shade_smooth()

# ---------------------------------------------------------------- per-vertex colour
img = bpy.data.images.load(os.path.join(refs, "front.png"))
iw, ih = img.size
px = list(img.pixels)
H = 2.0


def sample(u, v):
    x = min(max(int(u * (iw - 1)), 0), iw - 1)
    y = min(max(int(v * (ih - 1)), 0), ih - 1)
    i = (y * iw + x) * 4
    return px[i], px[i + 1], px[i + 2], px[i + 3]


col = body.data.color_attributes.new(name="Col", type="FLOAT_COLOR", domain="POINT")
for i, v in enumerate(body.data.vertices):
    u = 0.5 + v.co.x / 0.90
    vv = v.co.z / H
    if v.normal.y > 0.0:
        u = 1.0 - u
    r, g, b, a = sample(u, vv)
    if a < 0.5:
        r, g, b = r * 0.55, g * 0.55, b * 0.55
    col.data[i].color = (r, g, b, 1.0)

mat = bpy.data.materials.new("WalkerSkin")
mat.use_nodes = True
nt = mat.node_tree
bsdf = nt.nodes["Principled BSDF"]
bsdf.inputs["Roughness"].default_value = 0.85
vc = nt.nodes.new("ShaderNodeVertexColor")
vc.layer_name = "Col"
nt.links.new(vc.outputs["Color"], bsdf.inputs["Base Color"])
bsdf.inputs["Emission Color"].default_value = (0.35, 1.0, 0.15, 1.0)
bsdf.inputs["Emission Strength"].default_value = 0.02
body.data.materials.append(mat)

# ---------------------------------------------------------------- armature
HIP, CROTCH, WAIST, CHEST, SHOULDER = 1.00, 0.94, 1.13, 1.35, 1.54
NECK, CHIN, TOP = 1.60, 1.66, 2.00
KNEE, ANKLE = 0.52, 0.09

arm_data = bpy.data.armatures.new("WalkerRig")
arm = bpy.data.objects.new("WalkerRig", arm_data)
scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")

BONES = [
    ("root", None, (0, 0, 0.0), (0, 0, 0.10)),
    ("pelvis", "root", (0, 0, HIP), (0, 0, HIP + 0.10)),
    ("spine01", "pelvis", (0, 0, HIP), (0, 0, WAIST)),
    ("spine02", "spine01", (0, 0, WAIST), (0, 0, CHEST)),
    ("spine03", "spine02", (0, 0, CHEST), (0, 0, SHOULDER)),
    ("neck", "spine03", (0, 0, SHOULDER), (0, -0.01, NECK)),
    ("head", "neck", (0, -0.01, NECK), (0, -0.02, TOP - 0.03)),
    ("jaw", "head", (0, -0.05, CHIN + 0.05), (0, -0.13, CHIN)),
]
for side, tag in ((-1, "L"), (1, "R")):
    sx = 0.205 * side
    BONES += [
        ("clavicle" + tag, "spine03", (0, 0, SHOULDER - 0.03), (sx, 0, SHOULDER - 0.02)),
        ("upperarm" + tag, "clavicle" + tag, (sx, 0, SHOULDER - 0.02), (sx * 1.22, 0.01, 1.14)),
        ("forearm" + tag, "upperarm" + tag, (sx * 1.22, 0.01, 1.14), (sx * 1.30, 0.01, 0.86)),
        ("hand" + tag, "forearm" + tag, (sx * 1.30, 0.01, 0.86), (sx * 1.33, 0.02, 0.72)),
        ("thigh" + tag, "pelvis", (0.095 * side, 0, HIP - 0.02), (0.10 * side, 0, KNEE)),
        ("shin" + tag, "thigh" + tag, (0.10 * side, 0, KNEE), (0.10 * side, -0.005, ANKLE)),
        ("foot" + tag, "shin" + tag, (0.10 * side, -0.005, ANKLE), (0.10 * side, -0.16, 0.04)),
        ("toe" + tag, "foot" + tag, (0.10 * side, -0.16, 0.04), (0.10 * side, -0.24, 0.035)),
    ]

eb = {}
for name, parent, head, tail in BONES:
    b = arm_data.edit_bones.new(name)
    b.head, b.tail = Vector(head), Vector(tail)
    eb[name] = b
for name, parent, _h, _t in BONES:
    if parent:
        eb[name].parent = eb[parent]
bpy.ops.object.mode_set(mode="OBJECT")

body.select_set(True)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
try:
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    print("[rig] automatic weights")
except RuntimeError as exc:
    print("[rig] auto weights failed:", exc)
    bpy.ops.object.parent_set(type="ARMATURE_ENVELOPE")

tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
print("[rig] verts=%d tris=%d bones=%d" % (len(body.data.vertices), tris, len(BONES)))

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, "walker.blend"))
bpy.ops.export_scene.gltf(filepath=os.path.join(out, "walker.glb"), export_format="GLB", export_apply=True)

# ---------------------------------------------------------------- stage
sun_data = bpy.data.lights.new("sun", type="SUN")
sun_data.energy = 4.0
sun_data.color = (1.0, 0.93, 0.82)
sun_data.angle = math.radians(6)
sun = bpy.data.objects.new("sun", sun_data)
sun.rotation_euler = (math.radians(52), 0, math.radians(155))
scene.collection.objects.link(sun)

fill_data = bpy.data.lights.new("fill", type="SUN")
fill_data.energy = 1.5
fill_data.color = (0.45, 0.6, 1.0)
fill = bpy.data.objects.new("fill", fill_data)
fill.rotation_euler = (math.radians(-30), 0, math.radians(20))
scene.collection.objects.link(fill)

world = bpy.data.worlds.new("w")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.10, 0.12, 0.16, 1.0)
world.node_tree.nodes["Background"].inputs[1].default_value = 1.2
scene.world = world

pivot = bpy.data.objects.new("pivot", None)
scene.collection.objects.link(pivot)
for ob in (body, arm):
    ob.parent = pivot
    ob.matrix_parent_inverse = pivot.matrix_world.inverted()

cam_data = bpy.data.cameras.new("cam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = 2.7
target = Vector((0, 0, 1.0))
elev = math.radians(8)
cam = bpy.data.objects.new("cam", cam_data)
cam.location = target + Vector((0, -9 * math.cos(elev), 9 * math.sin(elev)))
cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
scene.collection.objects.link(cam)
scene.camera = cam

scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 40
scene.cycles.use_denoising = True
scene.render.resolution_x = 420
scene.render.resolution_y = 560
scene.render.film_transparent = True
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.view_settings.view_transform = "Standard"

for i in range(turn_frames):
    pivot.rotation_euler = (0, 0, math.radians(360.0 * i / turn_frames))
    bpy.context.view_layer.update()
    scene.render.filepath = os.path.join(out, "turn_%02d.png" % i)
    bpy.ops.render.render(write_still=True)

# ---------------------------------------------------------------- walk
pivot.rotation_euler = (0, 0, math.radians(-18))
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")


def rot(name, x=0.0, z=0.0):
    pb = arm.pose.bones.get(name)
    if pb is None:
        return
    pb.rotation_mode = "XYZ"
    pb.rotation_euler = (math.radians(x), 0.0, math.radians(z))


for i in range(walk_frames):
    p = 2.0 * math.pi * i / walk_frames
    for side, off in (("L", 0.0), ("R", math.pi)):
        ph = p + off
        rot("thigh" + side, 20.0 * math.sin(ph))
        rot("shin" + side, -24.0 * max(0.0, math.sin(ph + 1.1)))
        rot("foot" + side, 10.0 * math.sin(ph + 2.2))
    rot("upperarmL", -16.0 * math.sin(p) - 5.0)
    rot("forearmL", -10.0 * max(0.0, math.sin(p + 0.6)))
    rot("upperarmR", 12.0 * math.sin(p) - 4.0)
    rot("forearmR", -8.0 * max(0.0, math.sin(p + 2.4)))
    rot("spine02", 0.0, 2.5 * math.sin(p))
    rot("spine03", 0.0, -2.0 * math.sin(p + 0.4))
    rot("head", 2.0 * math.sin(p + 1.2), -2.5 * math.sin(p + 0.8))
    pivot.location = (0, 0, 0.03 * (0.5 - 0.5 * math.cos(2.0 * p)))
    bpy.context.view_layer.update()
    scene.render.filepath = os.path.join(out, "walk_%02d.png" % i)
    bpy.ops.render.render(write_still=True)

bpy.ops.object.mode_set(mode="OBJECT")
print("[rig] done ->", out)
