"""Normalise, texture, rig and render a reconstructed character mesh.

    blender -b -P rig_mesh.py -- <mesh.obj> <out_dir> [turn_frames] [walk_frames]

Handles the axis mess of reconstruction exports by finding the long axis and standing the
character up, scales it to 2.0 m, builds the 24-bone humanoid rig at proportional landmark
heights, binds with automatic weights, then renders a turntable and a walk cycle.
"""
import math
import os
import sys

import bpy
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
src = os.path.abspath(argv[0])
out = os.path.abspath(argv[1])
turn_frames = int(argv[2]) if len(argv) > 2 else 12
walk_frames = int(argv[3]) if len(argv) > 3 else 12
os.makedirs(out, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

# the exporter may write OBJ text; import without axis guessing and fix orientation ourselves
if src.lower().endswith(".ply"):
    bpy.ops.wm.ply_import(filepath=src, forward_axis="Y", up_axis="Z")
else:
    bpy.ops.wm.obj_import(filepath=src, forward_axis="Y", up_axis="Z")
meshes = [o for o in scene.objects if o.type == "MESH"]
if not meshes:
    raise SystemExit("no mesh imported")
body = meshes[0]
for extra in meshes[1:]:
    bpy.data.objects.remove(extra, do_unlink=True)

body.name = "Walker"
body.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def bbox():
    pts = [body.matrix_world @ Vector(c) for c in body.bound_box]
    lo = Vector((min(p[i] for p in pts) for i in range(3)))
    hi = Vector((max(p[i] for p in pts) for i in range(3)))
    return lo, hi, (hi - lo)


lo, hi, size = bbox()
print("[mesh] raw size", [round(v, 3) for v in size])

# stand it up: the longest axis becomes Z. Both branches rotate the *negative* way — the
# positive rotation dropped the character head-down (its head ended up at z=0).
if size.y > size.z and size.y > size.x:
    body.rotation_euler = (math.radians(-90), 0, 0)
elif size.x > size.z and size.x > size.y:
    body.rotation_euler = (0, math.radians(-90), 0)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# reconstruction normals point whichever way the exporter felt like; make them outward
import bmesh  # noqa: E402

bm = bmesh.new()
bm.from_mesh(body.data)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.to_mesh(body.data)
bm.free()
body.data.update()

lo, hi, size = bbox()
# scale to 2.0 m tall, feet on the floor, centred on X/Y
scale = 2.0 / size.z if size.z > 1e-6 else 1.0
body.scale = (scale, scale, scale)
bpy.ops.object.transform_apply(scale=True)

lo, hi, size = bbox()
body.location = (body.location.x - (lo.x + hi.x) / 2,
                 body.location.y - (lo.y + hi.y) / 2,
                 body.location.z - lo.z)
bpy.ops.object.transform_apply(location=True)
lo, hi, size = bbox()
print("[mesh] normalised", [round(v, 3) for v in size])

# face the camera: the reconstruction's front pointed along -X, so yaw it 90 deg onto -Y
yaw = float(argv[5]) if len(argv) > 5 else 0.0
if abs(yaw) > 1e-3:
    body.rotation_euler = (0, 0, math.radians(yaw))
    bpy.ops.object.transform_apply(rotation=True)

# simplify: reconstructed meshes are dense (100k+ tris) and never need that for a 160 px sprite
dec = body.modifiers.new("Dec", "DECIMATE")
dec.ratio = min(1.0, 18000.0 / max(1, len(body.data.polygons)))
bpy.ops.object.modifier_apply(modifier="Dec")
bpy.ops.object.shade_smooth()
print("[mesh] after decimate faces=%d" % len(body.data.polygons))

# ---------------------------------------------------------------- material
# the baked atlas from the reconstruction is confetti (thousands of tiny UV islands with black
# gaps), so colour comes from projecting the source render onto the vertices instead
matte_path = argv[4] if len(argv) > 4 else None
existing = body.data.color_attributes if body.data.color_attributes else None

def add_math(nt, op, a_val=None, b_val=None):
    n = nt.nodes.new("ShaderNodeMath")
    n.operation = op
    if a_val is not None:
        n.inputs[0].default_value = a_val
    if b_val is not None:
        n.inputs[1].default_value = b_val
    return n


def projection_uv(nt, sep, axis_socket, axis_scale, other_socket, other_scale, flip=1.0):
    """u = axis*axis_scale*flip + 0.5 ; v = other*other_scale"""
    mul = add_math(nt, "MULTIPLY", None, axis_scale * flip)
    nt.links.new(axis_socket, mul.inputs[0])
    add = add_math(nt, "ADD", 0.5, None)
    add.inputs[0].default_value = 0.5
    nt.links.new(mul.outputs[0], add.inputs[1])
    vm = add_math(nt, "MULTIPLY", None, other_scale)
    nt.links.new(other_socket, vm.inputs[0])
    comb = nt.nodes.new("ShaderNodeCombineXYZ")
    nt.links.new(add.outputs[0], comb.inputs["X"])
    nt.links.new(vm.outputs[0], comb.inputs["Y"])
    return comb


def image_node(nt, path):
    img = bpy.data.images.load(path)
    n = nt.nodes.new("ShaderNodeTexImage")
    n.image = img
    n.extension = "EXTEND"
    return n


if matte_path == "proj":
    # each face takes the source view its normal points at, with UVs computed here in Python:
    # deterministic (the shader-space projection sampled black on the sides) and it keeps the
    # full pixel detail of the renders, which per-vertex colour destroys
    tex = argv[6:10]
    sx = max(body.dimensions.x, 0.2)
    sy = max(body.dimensions.y, 0.2)
    sz = max(body.dimensions.z, 0.2)

    me = body.data
    uvl = me.uv_layers.new(name="UVMap")
    me.materials.clear()
    for i, path in enumerate(tex):
        m = bpy.data.materials.new("WalkerView%d" % i)
        m.use_nodes = True
        mnt = m.node_tree
        mbsdf = mnt.nodes["Principled BSDF"]
        mbsdf.inputs["Roughness"].default_value = 0.8
        img = bpy.data.images.load(path)
        n = mnt.nodes.new("ShaderNodeTexImage")
        n.image = img
        n.extension = "EXTEND"
        mnt.links.new(n.outputs["Color"], mbsdf.inputs["Base Color"])
        me.materials.append(m)

    counts = [0, 0, 0, 0]
    for poly in me.polygons:
        nrm = poly.normal
        if abs(nrm.y) >= abs(nrm.x):
            idx = 0 if nrm.y < 0 else 1
        else:
            idx = 2 if nrm.x < 0 else 3
        poly.material_index = idx
        counts[idx] += 1
        for li in poly.loop_indices:
            v = me.vertices[me.loops[li].vertex_index].co
            if idx == 0:
                u = v.x / sx + 0.5
            elif idx == 1:
                u = -v.x / sx + 0.5
            elif idx == 2:
                u = v.y / sy + 0.5
            else:
                u = -v.y / sy + 0.5
            uvl.data[li].uv = (min(max(u, 0.0), 1.0), min(max(v.z / sz, 0.0), 1.0))
    print("[mesh] per-face projection, faces per view", counts)

elif existing and len(existing) > 0 and (matte_path is None or matte_path == "-"):
    # the PLY already carries per-view colours baked by the hull builder
    layer = existing[0].name
    print("[mesh] using imported colour layer", layer, existing[0].domain)
    vals = [existing[0].data[i].color[0] for i in range(0, len(existing[0].data), max(1, len(existing[0].data) // 200))]
    print("[mesh] colour r: min %.3f mean %.3f max %.3f" % (min(vals), sum(vals) / len(vals), max(vals)))
    mat = bpy.data.materials.new("WalkerSkin")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.85
    vc = nt.nodes.new("ShaderNodeVertexColor")
    vc.layer_name = layer
    # the PLY carries sRGB bytes that Blender linearises on import (mean r ~0.11); put the
    # gamma back so the character renders at the brightness of the reference renders
    gamma = nt.nodes.new("ShaderNodeGamma")
    gamma.inputs["Gamma"].default_value = 0.50
    nt.links.new(vc.outputs["Color"], gamma.inputs["Color"])
    nt.links.new(gamma.outputs["Color"], bsdf.inputs["Base Color"])
elif matte_path and matte_path != "-" and os.path.exists(matte_path):
    img = bpy.data.images.load(matte_path)
    iw, ih = img.size
    px = list(img.pixels)

    def sample(u, v):
        x = min(max(int(u * (iw - 1)), 0), iw - 1)
        y = min(max(int(v * (ih - 1)), 0), ih - 1)
        i = (y * iw + x) * 4
        return px[i], px[i + 1], px[i + 2], px[i + 3]

    col = body.data.color_attributes.new(name="Col", type="FLOAT_COLOR", domain="POINT")
    wspan = max(size.x, 1e-6)
    for i, v in enumerate(body.data.vertices):
        co = body.matrix_world @ v.co
        u = 0.5 + co.x / wspan * (wspan / 2.0) * 2.0 * 0.5
        u = min(max(0.5 + co.x / wspan, 0.0), 1.0)
        vv = min(max(co.z / 2.0, 0.0), 1.0)
        if v.normal.y > 0.0:
            u = 1.0 - u
        r, g, b, a = sample(u, vv)
        if a < 0.5:
            r, g, b = r * 0.5, g * 0.5, b * 0.5
        # the sheet render is dark; lift it so the character reads under the game's cold light
        col.data[i].color = (min(r * 1.45, 1.0), min(g * 1.40, 1.0), min(b * 1.32, 1.0), 1.0)

    mat = bpy.data.materials.new("WalkerSkin")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.85
    vc = nt.nodes.new("ShaderNodeVertexColor")
    vc.layer_name = "Col"
    nt.links.new(vc.outputs["Color"], bsdf.inputs["Base Color"])
    print("[mesh] vertex colours from", os.path.basename(matte_path))
else:
    mat = bpy.data.materials.new("WalkerSkin")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.85
    bsdf.inputs["Base Color"].default_value = (0.5, 0.52, 0.45, 1)
if matte_path != "proj":
    body.data.materials.clear()
    body.data.materials.append(mat)
# ---------------------------------------------------------------- rig
HIP, WAIST, CHEST, SHOULDER = 0.50, 0.565, 0.675, 0.77     # fractions of 2.0 m
NECK, CHIN, TOP = 0.80, 0.83, 1.00
KNEE, ANKLE = 0.26, 0.045

arm_data = bpy.data.armatures.new("WalkerRig")
arm = bpy.data.objects.new("WalkerRig", arm_data)
scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")

BONES = [
    ("root", None, (0, 0, 0.0), (0, 0, 0.10)),
    ("pelvis", "root", (0, 0, HIP), (0, 0, HIP + 0.08)),
    ("spine01", "pelvis", (0, 0, HIP), (0, 0, WAIST)),
    ("spine02", "spine01", (0, 0, WAIST), (0, 0, CHEST)),
    ("spine03", "spine02", (0, 0, CHEST), (0, 0, SHOULDER)),
    ("neck", "spine03", (0, 0, SHOULDER), (0, -0.01, NECK)),
    ("head", "neck", (0, -0.01, NECK), (0, -0.02, TOP - 0.02)),
    ("jaw", "head", (0, -0.04, CHIN + 0.03), (0, -0.10, CHIN - 0.01)),
]
for side, tag in ((-1, "L"), (1, "R")):
    sx = 0.105 * side
    BONES += [
        ("clavicle" + tag, "spine03", (0, 0, SHOULDER - 0.02), (sx, 0, SHOULDER - 0.01)),
        ("upperarm" + tag, "clavicle" + tag, (sx, 0, SHOULDER - 0.01), (sx * 1.45, 0.01, 0.575)),
        ("forearm" + tag, "upperarm" + tag, (sx * 1.45, 0.01, 0.575), (sx * 1.60, 0.01, 0.435)),
        ("hand" + tag, "forearm" + tag, (sx * 1.60, 0.01, 0.435), (sx * 1.66, 0.02, 0.365)),
        ("thigh" + tag, "pelvis", (0.05 * side, 0, HIP - 0.01), (0.055 * side, 0, KNEE)),
        ("shin" + tag, "thigh" + tag, (0.055 * side, 0, KNEE), (0.055 * side, -0.005, ANKLE)),
        ("foot" + tag, "shin" + tag, (0.055 * side, -0.005, ANKLE), (0.055 * side, -0.10, 0.02)),
        ("toe" + tag, "foot" + tag, (0.055 * side, -0.10, 0.02), (0.055 * side, -0.15, 0.018)),
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
sun_data.energy = 6.0
sun_data.color = (1.0, 0.93, 0.82)
sun_data.angle = math.radians(6)
sun = bpy.data.objects.new("sun", sun_data)
sun.rotation_euler = (math.radians(52), 0, math.radians(155))
scene.collection.objects.link(sun)

fill_data = bpy.data.lights.new("fill", type="SUN")
fill_data.energy = 2.4
fill_data.color = (0.45, 0.6, 1.0)
fill = bpy.data.objects.new("fill", fill_data)
fill.rotation_euler = (math.radians(-30), 0, math.radians(20))
scene.collection.objects.link(fill)

world = bpy.data.worlds.new("w")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.10, 0.12, 0.16, 1.0)
world.node_tree.nodes["Background"].inputs[1].default_value = 1.6
scene.world = world

pivot = bpy.data.objects.new("pivot", None)
scene.collection.objects.link(pivot)
for ob in (body, arm):
    ob.parent = pivot
    ob.matrix_parent_inverse = pivot.matrix_world.inverted()

cam_data = bpy.data.cameras.new("cam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = 2.6
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
scene.view_settings.exposure = 0.75

for i in range(turn_frames):
    pivot.rotation_euler = (0, 0, math.radians(360.0 * i / turn_frames))
    bpy.context.view_layer.update()
    scene.render.filepath = os.path.join(out, "turn_%02d.png" % i)
    bpy.ops.render.render(write_still=True)

# ---------------------------------------------------------------- walk
pivot.rotation_euler = (0, 0, math.radians(-20))
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
        rot("thigh" + side, 22.0 * math.sin(ph))
        rot("shin" + side, -26.0 * max(0.0, math.sin(ph + 1.1)))
        rot("foot" + side, 10.0 * math.sin(ph + 2.2))
    rot("upperarmL", -17.0 * math.sin(p) - 6.0)
    rot("forearmL", -12.0 * max(0.0, math.sin(p + 0.6)))
    rot("upperarmR", 13.0 * math.sin(p) - 4.0)
    rot("forearmR", -9.0 * max(0.0, math.sin(p + 2.4)))
    rot("spine02", 0.0, 2.5 * math.sin(p))
    rot("spine03", 0.0, -2.0 * math.sin(p + 0.4))
    rot("head", 2.0 * math.sin(p + 1.2), -2.5 * math.sin(p + 0.8))
    pivot.location = (0, 0, 0.03 * (0.5 - 0.5 * math.cos(2.0 * p)))
    bpy.context.view_layer.update()
    scene.render.filepath = os.path.join(out, "walk_%02d.png" % i)
    bpy.ops.render.render(write_still=True)

bpy.ops.object.mode_set(mode="OBJECT")
print("[rig] done ->", out)
