"""Headless: import the Tripo FBX, normalise it, wire the texture, save .blend + .glb.

Run with:  blender -b -P convert_fbx.py
The GUI import path fails in this Blender build (the FBX importer calls mode_set while the
startup context has no active object), so the conversion happens headless and the GUI only
opens the finished .blend.
"""
import bpy
import math
import os
from mathutils import Vector

SRC = r"C:\Users\saeed\Downloads\tripo_convert_ac3aed88-dc8f-4cb1-8042-a9e4b93d3126\tripo_convert_ac3aed88-dc8f-4cb1-8042-a9e4b93d3126.fbx"
OUT_DIR = r"C:\Users\saeed\Downloads"
TEX_DIR = os.path.join(os.path.dirname(SRC), "tripo_convert_ac3aed88-dc8f-4cb1-8042-a9e4b93d3126.fbm")

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.object.add(type="EMPTY")
bpy.context.view_layer.objects.active = bpy.context.selected_objects[0] if bpy.context.selected_objects else bpy.data.objects[0]
bpy.ops.import_scene.fbx(filepath=SRC)
print("[cv] imported objects:", [(o.name, o.type) for o in bpy.context.scene.objects])

for o in [o for o in bpy.context.scene.objects if o.type == "EMPTY" and not o.children]:
    bpy.data.objects.remove(o, do_unlink=True)

meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
if not meshes:
    raise SystemExit("[cv] no mesh imported")
print("[cv] meshes:", [(o.name, len(o.data.vertices)) for o in meshes])

# bake world transform of each mesh and join into one object for predictable maths
bpy.ops.object.select_all(action="DESELECT")
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

d = body.dimensions
print("[cv] dims %.3f %.3f %.3f" % (d.x, d.y, d.z))
if d.z <= max(d.x, d.y):
    body.rotation_euler = (math.radians(90), 0, 0)
    bpy.ops.object.transform_apply(rotation=True)
    print("[cv] rotated upright, dims now", tuple(round(v, 3) for v in body.dimensions))

d = body.dimensions
s = 2.0 / max(d.z, 1e-6)
body.scale = (s, s, s)
bpy.ops.object.transform_apply(scale=True)

bpy.context.view_layer.update()
bb = [body.matrix_world @ Vector(c) for c in body.bound_box]
body.location.x -= (min(v.x for v in bb) + max(v.x for v in bb)) / 2
body.location.y -= (min(v.y for v in bb) + max(v.y for v in bb)) / 2
body.location.z -= min(v.z for v in bb)
bpy.ops.object.transform_apply(location=True)

tex_files = []
if os.path.isdir(TEX_DIR):
    tex_files = sorted(os.path.join(TEX_DIR, f) for f in os.listdir(TEX_DIR)
                       if f.lower().endswith((".jpg", ".png", ".jpeg")))
print("[cv] textures:", [os.path.basename(t) for t in tex_files])

if not body.material_slots:
    m = bpy.data.materials.new("WalkerTripo")
    body.data.materials.append(m)

for slot in body.material_slots:
    m = slot.material
    if not m:
        continue
    m.use_nodes = True
    nt = m.node_tree
    bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
    linked = bsdf and any(l.from_node.type == "TEX_IMAGE" for l in nt.links if l.to_socket == bsdf.inputs["Base Color"])
    print("[cv] material", m.name, "texture linked:", bool(linked))
    if not linked and tex_files:
        img = bpy.data.images.load(tex_files[0], check_existing=True)
        n = nt.nodes.new("ShaderNodeTexImage")
        n.image = img
        if bsdf:
            nt.links.new(n.outputs["Color"], bsdf.inputs["Base Color"])
        print("[cv] wired", os.path.basename(tex_files[0]))

# smooth shading + a touch of spec restraint, then a camera and lights so the blend opens framed
bpy.ops.object.shade_smooth()
world = bpy.data.worlds.new("W")
bpy.context.scene.world = world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.055, 0.07, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 1.0

cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
bpy.context.scene.collection.objects.link(cam)
cam.location = (1.9, -3.4, 1.55)
cam.rotation_euler = (math.radians(78), 0, math.radians(29))
cam_data.lens = 62
bpy.context.scene.camera = cam

key_data = bpy.data.lights.new("Key", type="AREA")
key_data.energy = 420
key_data.size = 3.0
key = bpy.data.objects.new("Key", key_data)
key.location = (2.6, -3.0, 3.4)
key.rotation_euler = (math.radians(45), 0, math.radians(40))
bpy.context.scene.collection.objects.link(key)

fill_data = bpy.data.lights.new("Fill", type="AREA")
fill_data.energy = 130
fill_data.size = 4.0
fill = bpy.data.objects.new("Fill", fill_data)
fill.location = (-3.0, -1.8, 1.8)
fill.rotation_euler = (math.radians(70), 0, math.radians(-60))
bpy.context.scene.collection.objects.link(fill)

bpy.context.scene.render.engine = "CYCLES"
bpy.context.scene.cycles.samples = 48
bpy.context.scene.render.resolution_x = 720
bpy.context.scene.render.resolution_y = 900
bpy.context.scene.view_settings.view_transform = "Standard"
bpy.context.scene.view_settings.exposure = 0.5

print("[cv] final verts %d tris %d dims %.2f %.2f %.2f" %
      (len(body.data.vertices), len(body.data.polygons), *body.dimensions))
blend = os.path.join(OUT_DIR, "walker_tripo.blend")
bpy.ops.wm.save_as_mainfile(filepath=blend)
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT_DIR, "walker_tripo.glb"),
                          export_format="GLB", use_selection=False)
print("[cv] saved", blend)
