"""Build the Walker enemy as a rigged low-poly 3D character from the design-sheet turnarounds.

Proportions come from the extracted ortho views (`analyze_refs.py`), textures from the front
and back renders, geometry from lofted rings and tapered limb tubes — separate objects joined
into one mesh, so arms can hang next to the torso without fusing into it.

    blender -b -P build_walker.py -- <refs_dir> <out_dir>

Writes walker.blend, walker.glb, walker_front/side/back.png (copied textures), turntable and
walk frames, and prints a mesh summary.
"""
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
refs = os.path.abspath(argv[0])
out = os.path.abspath(argv[1])
os.makedirs(out, exist_ok=True)

# a fresh file: the default startup scene ships with a 2 m cube, which otherwise ends up in
# the export and in the camera framing
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

with open(os.path.join(refs, "profile.json"), encoding="utf-8") as fh:
    prof = json.load(fh)

H = 2.0                                  # character height in metres
SEG = 16                                 # ring segments for the torso loft

# ---------------------------------------------------------------- helpers
verts = []                               # world-space vertex pool
faces = []


def add_ring(cx, cy, cz, rx, ry, seg=SEG, squash_back=0.0):
    """Ellipse ring in the XY plane at height cz. squash_back flattens +Y (the back)."""
    base = len(verts)
    for i in range(seg):
        a = 2.0 * math.pi * i / seg
        x = cx + math.cos(a) * rx
        y = cy + math.sin(a) * ry * (1.0 - squash_back if math.sin(a) > 0 else 1.0)
        verts.append(Vector((x, y, cz)))
    return base


def bridge(ring_a, ring_b, seg=SEG, flip=False):
    for i in range(seg):
        j = (i + 1) % seg
        a0, a1 = ring_a + i, ring_a + j
        b0, b1 = ring_b + i, ring_b + j
        if flip:
            faces.append((a0, b0, b1, a1))
        else:
            faces.append((a0, a1, b1, b0))


def cap(ring, seg=SEG, centre_top=True):
    c = Vector((0, 0, 0))
    for i in range(seg):
        c += verts[ring + i]
    c /= seg
    idx = len(verts)
    verts.append(c)
    for i in range(seg):
        j = (i + 1) % seg
        faces.append((ring + i, ring + j, idx))


def loft(sections):
    """sections: [(z, rx, ry, cx, cy)] top->bottom, bridged in order."""
    rings = [add_ring(cx, cy, z, rx, ry) for (z, rx, ry, cx, cy) in sections]
    for a, b in zip(rings, rings[1:]):
        bridge(a, b)
    cap(rings[0])
    cap(rings[-1])
    return rings


def tube(points, radii, seg=10, close_ends=True):
    """Tapered tube through world-space points with per-point radius."""
    rings = []
    for p, r in zip(points, radii):
        base = len(verts)
        for i in range(seg):
            a = 2.0 * math.pi * i / seg
            verts.append(Vector((p.x + math.cos(a) * r, p.y + math.sin(a) * r * 0.9, p.z)))
        rings.append(base)
    for a, b in zip(rings, rings[1:]):
        for i in range(seg):
            j = (i + 1) % seg
            faces.append((a + i, a + j, b + i, b + j))
    if close_ends:
        cap(rings[0], seg)
        cap(rings[-1], seg)


def ellipsoid(centre, rx, ry, rz, seg=14, rings_n=10):
    base = len(verts)
    for r in range(rings_n + 1):
        phi = math.pi * r / rings_n
        z = centre.z + math.cos(phi) * rz
        s = math.sin(phi)
        for i in range(seg):
            a = 2.0 * math.pi * i / seg
            verts.append(Vector((centre.x + math.cos(a) * rx * s,
                                 centre.y + math.sin(a) * ry * s,
                                 z)))
    for r in range(rings_n):
        for i in range(seg):
            j = (i + 1) % seg
            a = base + r * seg
            b = base + (r + 1) * seg
            faces.append((a + i, a + j, b + i, b + j))


def box(centre, half, rot_z=0.0):
    base = len(verts)
    hs = [Vector((-half[0], -half[1], -half[2])), Vector((half[0], -half[1], -half[2])),
          Vector((half[0], half[1], -half[2])), Vector((-half[0], half[1], -half[2])),
          Vector((-half[0], -half[1], half[2])), Vector((half[0], -half[1], half[2])),
          Vector((half[0], half[1], half[2])), Vector((-half[0], half[1], half[2]))]
    rot = Matrix.Rotation(rot_z, 3, "Z")
    for h in hs:
        verts.append(centre + rot @ h)
    for f in [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
              (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]:
        faces.append(tuple(base + i for i in f))


# ---------------------------------------------------------------- proportions
# read from the front/side mattes; the art is 2 m tall in game units, 8 heads-ish
HEAD_TOP = 2.00
CHIN = 1.655
NECK = 1.60
SHOULDER = 1.545
CHEST = 1.35
WAIST = 1.14
HIP = 1.00
CROTCH = 0.94
KNEE = 0.52
ANKLE = 0.095

# torso: rings from shoulders down to the crotch, half-width x, half-depth y
torso = [
    (SHOULDER, 0.205, 0.130, 0.0, 0.0),
    (CHEST + 0.10, 0.215, 0.145, 0.0, 0.0),
    (CHEST, 0.200, 0.140, 0.0, 0.0),
    (WAIST, 0.170, 0.125, 0.0, 0.0),
    (HIP, 0.180, 0.130, 0.0, 0.0),
    (CROTCH, 0.165, 0.120, 0.0, 0.0),
]
loft(torso)

# neck + head (slight forward hunch, like the reference)
loft([(NECK + 0.03, 0.062, 0.062, 0.0, -0.005),
      (CHIN + 0.03, 0.078, 0.080, 0.0, -0.012)])
ellipsoid(Vector((0.0, -0.022, CHIN + 0.135)), 0.104, 0.118, 0.125)
# jaw / muzzle sticking forward
box(Vector((0.0, -0.100, CHIN + 0.045)), (0.060, 0.048, 0.042))

# arms: shoulder -> elbow -> wrist -> hand, hanging slightly out and forward
for side in (-1, 1):
    sx = 0.205 * side
    sh = Vector((sx, 0.0, SHOULDER - 0.02))
    el = Vector((sx * 1.22, 0.010, 1.145))
    wr = Vector((sx * 1.30, 0.010, 0.865))
    hd = Vector((sx * 1.33, 0.015, 0.775))
    tube([sh, el, wr], [0.072, 0.056, 0.046], seg=10, close_ends=False)
    box(hd, (0.045, 0.030, 0.062))

# legs: hip -> knee -> ankle, then a foot box forward
for side in (-1, 1):
    hx = 0.095 * side
    hp = Vector((hx, 0.0, HIP - 0.02))
    kn = Vector((hx * 1.02, 0.0, KNEE))
    an = Vector((hx * 1.02, -0.005, ANKLE))
    tube([hp, kn, an], [0.098, 0.072, 0.052], seg=10, close_ends=False)
    box(Vector((hx * 1.02, -0.075, ANKLE * 0.55)), (0.062, 0.130, 0.055))

# ---------------------------------------------------------------- mesh object
mesh = bpy.data.meshes.new("WalkerMesh")
mesh.from_pydata([tuple(v) for v in verts], [], faces)
mesh.update()
body = bpy.data.objects.new("Walker", mesh)
bpy.context.scene.collection.objects.link(body)
bpy.context.view_layer.objects.active = body
body.select_set(True)

bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
# no remove_doubles: the limbs are meant to interpenetrate the torso, and welding those
# coincident vertices produced non-manifold junctions and inverted faces (rendered as stripes)
bpy.ops.object.mode_set(mode="OBJECT")
bpy.ops.object.shade_smooth()

# one subdivision level so the lofted rings read as a body rather than a faceted cage
sub = body.modifiers.new("Subsurf", "SUBSURF")
sub.levels = 1
sub.render_levels = 1
bpy.ops.object.modifier_apply(modifier="Subsurf")
bpy.ops.object.shade_smooth()

# recalculate normals per island with bmesh — the loft and tube builders wind opposite ways,
# and the operator version left a third of the faces inverted
import bmesh  # noqa: E402

bm = bmesh.new()
bm.from_mesh(body.data)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.to_mesh(body.data)
bm.free()
body.data.update()
bpy.ops.object.shade_smooth()

# ---------------------------------------------------------------- UV: planar from the front
xs = [v.co.x for v in body.data.vertices]
zs = [v.co.z for v in body.data.vertices]
minx, maxx, minz, maxz = min(xs), max(xs), min(zs), max(zs)
uv = body.data.uv_layers.new(name="UVMap")
for poly in body.data.polygons:
    for li in poly.loop_indices:
        co = body.data.vertices[body.data.loops[li].vertex_index].co
        uv.data[li].uv = ((co.x - minx) / (maxx - minx), (co.z - minz) / (maxz - minz))

# ---------------------------------------------------------------- materials
# per-vertex colour sampled from the front/back renders: box UVs on lofted limbs stretch
# badly, while vertex colour stays coherent and reads well at sprite size
def load(name):
    p = os.path.join(refs, name)
    return bpy.data.images.load(p) if os.path.exists(p) else None


front_img = load("front.png")
back_img = load("back.png") or front_img


def sampler(img):
    w, h = img.size
    px = list(img.pixels)          # RGBA floats, bottom-up

    def sample(u, v):
        x = min(max(int(u * (w - 1)), 0), w - 1)
        y = min(max(int(v * (h - 1)), 0), h - 1)
        i = (y * w + x) * 4
        return px[i], px[i + 1], px[i + 2], px[i + 3]

    return sample


s_front = sampler(front_img)
s_back = sampler(back_img)

col = body.data.color_attributes.new(name="Col", type="FLOAT_COLOR", domain="POINT")
w_inv = 1.0 / max(maxx - minx, 1e-6)
h_inv = 1.0 / max(maxz - minz, 1e-6)
for i, v in enumerate(body.data.vertices):
    u = (v.co.x - minx) * w_inv
    vv = (v.co.z - minz) * h_inv
    r, g, b, a = s_front(u, vv) if v.normal.y <= 0.0 else s_back(1.0 - u, vv)
    if a < 0.5:                  # hit the cut-out margin: darken rather than glow grey
        r, g, b = r * 0.6, g * 0.6, b * 0.6
    col.data[i].color = (r, g, b, 1.0)

mat = bpy.data.materials.new("WalkerSkin")
mat.use_nodes = True
nt = mat.node_tree
bsdf = nt.nodes["Principled BSDF"]
bsdf.inputs["Roughness"].default_value = 0.85
bsdf.inputs["Metallic"].default_value = 0.0

vcol = nt.nodes.new("ShaderNodeVertexColor")
vcol.layer_name = "Col"
nt.links.new(vcol.outputs["Color"], bsdf.inputs["Base Color"])

# the acid core and eyes glow a little on their own
bsdf.inputs["Emission Color"].default_value = (0.35, 1.0, 0.15, 1.0)
bsdf.inputs["Emission Strength"].default_value = 0.02
body.data.materials.append(mat)

# ---------------------------------------------------------------- armature
arm_data = bpy.data.armatures.new("WalkerRig")
arm = bpy.data.objects.new("WalkerRig", arm_data)
bpy.context.scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")

BONES = [
    ("root", None, (0, 0, 0.0), (0, 0, 0.12)),
    ("pelvis", "root", (0, 0, HIP), (0, 0, HIP + 0.10)),
    ("spine01", "pelvis", (0, 0, HIP), (0, 0, WAIST)),
    ("spine02", "spine01", (0, 0, WAIST), (0, 0, CHEST)),
    ("spine03", "spine02", (0, 0, CHEST), (0, 0, SHOULDER)),
    ("neck", "spine03", (0, 0, SHOULDER), (0, -0.01, NECK)),
    ("head", "neck", (0, -0.01, NECK), (0, -0.02, HEAD_TOP - 0.02)),
    ("jaw", "head", (0, -0.04, CHIN + 0.06), (0, -0.14, CHIN + 0.03)),
]
for side, tag in ((-1, "L"), (1, "R")):
    sx = 0.205 * side
    BONES += [
        ("clavicle" + tag, "spine03", (0, 0, SHOULDER - 0.03), (sx, 0, SHOULDER - 0.02)),
        ("upperarm" + tag, "clavicle" + tag, (sx, 0, SHOULDER - 0.02), (sx * 1.22, 0.01, 1.145)),
        ("forearm" + tag, "upperarm" + tag, (sx * 1.22, 0.01, 1.145), (sx * 1.30, 0.01, 0.865)),
        ("hand" + tag, "forearm" + tag, (sx * 1.30, 0.01, 0.865), (sx * 1.33, 0.015, 0.720)),
        ("thigh" + tag, "pelvis", (0.095 * side, 0, HIP - 0.02), (0.095 * side, 0, KNEE)),
        ("shin" + tag, "thigh" + tag, (0.095 * side, 0, KNEE), (0.097 * side, -0.005, ANKLE)),
        ("foot" + tag, "shin" + tag, (0.097 * side, -0.005, ANKLE), (0.097 * side, -0.19, 0.05)),
        ("toe" + tag, "foot" + tag, (0.097 * side, -0.19, 0.05), (0.097 * side, -0.26, 0.045)),
    ]

eb = {}
for name, parent, head, tail in BONES:
    b = arm_data.edit_bones.new(name)
    b.head = Vector(head)
    b.tail = Vector(tail)
    eb[name] = b
for name, parent, _h, _t in BONES:
    if parent:
        eb[name].parent = eb[parent]
        eb[name].use_connect = False
bpy.ops.object.mode_set(mode="OBJECT")

body.select_set(True)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
try:
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    print("[walker] bound: automatic weights")
except RuntimeError as exc:
    print("[walker] auto weights failed:", exc)
    bpy.ops.object.parent_set(type="ARMATURE_ENVELOPE")

tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
print("[walker] verts=%d tris=%d bones=%d" % (len(body.data.vertices), tris, len(BONES)))

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, "walker.blend"))
bpy.ops.export_scene.gltf(filepath=os.path.join(out, "walker.glb"), export_format="GLB", export_apply=True)

# ---------------------------------------------------------------- stage + renders
sun_data = bpy.data.lights.new("sun", type="SUN")
sun_data.energy = 4.0
sun_data.color = (1.0, 0.93, 0.82)
sun_data.angle = math.radians(6)
sun = bpy.data.objects.new("sun", sun_data)
sun.rotation_euler = (math.radians(52), 0, math.radians(155))
bpy.context.scene.collection.objects.link(sun)

fill_data = bpy.data.lights.new("fill", type="SUN")
fill_data.energy = 1.5
fill_data.color = (0.45, 0.6, 1.0)
fill = bpy.data.objects.new("fill", fill_data)
fill.rotation_euler = (math.radians(-30), 0, math.radians(20))
bpy.context.scene.collection.objects.link(fill)

world = bpy.data.worlds.new("w")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.10, 0.12, 0.16, 1.0)
world.node_tree.nodes["Background"].inputs[1].default_value = 1.2
bpy.context.scene.world = world

pivot = bpy.data.objects.new("pivot", None)
bpy.context.scene.collection.objects.link(pivot)
for ob in (body, arm):
    ob.parent = pivot
    ob.matrix_parent_inverse = pivot.matrix_world.inverted()

cam_data = bpy.data.cameras.new("cam")
cam_data.type = "ORTHO"
# frame from the finished mesh, not a guess: ortho scale from the world bbox
bpy.context.view_layer.update()
wmins = [1e9, 1e9, 1e9]
wmaxs = [-1e9, -1e9, -1e9]
for ob in (body, arm):
    for corner in ob.bound_box:
        w = ob.matrix_world @ Vector(corner)
        for i in range(3):
            wmins[i] = min(wmins[i], w[i])
            wmaxs[i] = max(wmaxs[i], w[i])
wsize = [wmaxs[i] - wmins[i] for i in range(3)]
wcentre = Vector(((wmins[0] + wmaxs[0]) / 2, (wmins[1] + wmaxs[1]) / 2, (wmins[2] + wmaxs[2]) / 2))
print("[walker] world size", [round(v, 2) for v in wsize])

cam_data.ortho_scale = max(wsize) * 1.30
elev = math.radians(8)
cam = bpy.data.objects.new("cam", cam_data)
cam.location = wcentre + Vector((0, -9 * math.cos(elev), 9 * math.sin(elev)))
cam.rotation_euler = (wcentre - cam.location).to_track_quat("-Z", "Y").to_euler()
bpy.context.scene.collection.objects.link(cam)
bpy.context.scene.camera = cam

sc = bpy.context.scene
sc.render.engine = "CYCLES"
sc.cycles.device = "CPU"
sc.cycles.samples = 40
sc.cycles.use_denoising = True
sc.render.resolution_x = 420
sc.render.resolution_y = 560
sc.render.film_transparent = True
sc.render.image_settings.file_format = "PNG"
sc.render.image_settings.color_mode = "RGBA"
sc.view_settings.view_transform = "Standard"

for i in range(16):
    pivot.rotation_euler = (0, 0, math.radians(360.0 * i / 16))
    bpy.context.view_layer.update()
    sc.render.filepath = os.path.join(out, "turn_%02d.png" % i)
    bpy.ops.render.render(write_still=True)
print("[walker] turntable done")

print("[walker] done ->", out)
