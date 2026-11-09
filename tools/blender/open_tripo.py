"""Open the Tripo-converted Walker FBX in the Blender GUI: import, stand it on the floor,
scale to 2 m, wire the texture if the .fbm mat did not link, and frame it in the viewport.
"""
import bpy
import math
import os
from mathutils import Euler

SRC = r"C:\Users\saeed\Downloads\tripo_convert_ac3aed88-dc8f-4cb1-8042-a9e4b93d3126\tripo_convert_ac3aed88-dc8f-4cb1-8042-a9e4b93d3126.fbx"
TEX_DIR = os.path.join(os.path.dirname(SRC), "tripo_convert_ac3aed88-dc8f-4cb1-8042-a9e4b93d3126.fbm")

# fresh scene
bpy.ops.wm.read_factory_settings(use_empty=True)

# the FBX importer builds armature hierarchy with mode_set(), which needs an active object;
# an empty scene has none, so park a temporary one before importing
before = set(bpy.data.objects)
bpy.ops.object.add(type="EMPTY")
new_obj = (set(bpy.data.objects) - before).pop()
bpy.context.view_layer.objects.active = new_obj
bpy.ops.import_scene.fbx(filepath=SRC)
bpy.data.objects.remove(new_obj, do_unlink=True)

meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
if not meshes:
    raise SystemExit("no mesh in fbx")
for o in bpy.context.scene.objects:
    o.select_set(o in meshes)

# join so the transform maths is on one object
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

d = body.dimensions
print("[open] dims before %.3f %.3f %.3f" % (d.x, d.y, d.z))
if d.z <= max(d.x, d.y):
    body.rotation_euler = (math.radians(90), 0, 0)
    bpy.ops.object.transform_apply(rotation=True)
    print("[open] rotated upright")

d = body.dimensions
scale = 2.0 / max(d.z, 1e-6)
body.scale = (scale, scale, scale)
bpy.ops.object.transform_apply(scale=True)

# centre in X/Y, feet on Z=0
bpy.context.view_layer.update()
bb = [body.matrix_world @ __import__("mathutils").Vector(c) for c in body.bound_box]
minx = min(v.x for v in bb); maxx = max(v.x for v in bb)
miny = min(v.y for v in bb); maxy = max(v.y for v in bb)
minz = min(v.z for v in bb)
body.location.x -= (minx + maxx) / 2
body.location.y -= (miny + maxy) / 2
body.location.z -= minz
bpy.ops.object.transform_apply(location=True)

# texture: the fbx references .fbm/…; if the node is missing or unlinked, wire it ourselves
tex_files = []
if os.path.isdir(TEX_DIR):
    tex_files = [os.path.join(TEX_DIR, f) for f in os.listdir(TEX_DIR)
                 if f.lower().endswith((".jpg", ".png", ".jpeg"))]
print("[open] textures found", [os.path.basename(t) for t in tex_files])

for slot in body.material_slots:
    m = slot.material
    if not m:
        continue
    m.use_nodes = True
    nt = m.node_tree
    bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
    linked = bsdf and any(l.from_node.type == "TEX_IMAGE" for l in nt.links if l.to_socket == bsdf.inputs["Base Color"])
    if not linked and tex_files:
        img = bpy.data.images.load(tex_files[0], check_existing=True)
        n = nt.nodes.new("ShaderNodeTexImage")
        n.image = img
        if bsdf:
            nt.links.new(n.outputs["Color"], bsdf.inputs["Base Color"])
        print("[open] wired texture into", m.name)

# viewport: material preview, framed on the model
for area in bpy.context.screen.areas:
    if area.type == "VIEW_3D":
        for space in area.spaces:
            if space.type == "VIEW_3D":
                space.shading.type = "MATERIAL"
                space.shading.use_scene_lights = False
                space.shading.use_scene_world = True
                space.overlay.show_floor = True
                space.overlay.show_axis_x = True
                space.overlay.show_axis_y = True
                r3d = space.region_3d
                r3d.view_perspective = "PERSP"
                r3d.view_location = (0.0, 0.0, 1.0)
                r3d.view_distance = 4.2
                r3d.view_rotation = Euler((math.radians(78), 0.0, math.radians(22)), "XYZ").to_quaternion()

bpy.ops.object.select_all(action="DESELECT")
body.select_set(True)
bpy.context.view_layer.objects.active = body
print("[open] verts %d tris %d height %.2f" % (len(body.data.vertices), len(body.data.polygons), body.dimensions.z))
bpy.ops.wm.save_as_mainfile(filepath=r"C:\Users\saeed\Downloads\walker_tripo.blend")
print("[open] saved walker_tripo.blend")
