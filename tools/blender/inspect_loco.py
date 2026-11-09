"""Inspect the steam locomotive FBX: objects, tri count, size, orientation, materials."""
import bpy

SRC = r"C:\Users\saeed\Downloads\steam locomotive 3d model\steam_locomotive_3d_model.fbx"
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.object.add(type="EMPTY")
bpy.context.view_layer.objects.active = bpy.context.selected_objects[0]
bpy.ops.import_scene.fbx(filepath=SRC)

objs = [(o.name, o.type) for o in bpy.context.scene.objects]
print("[loco] objects:", objs)

meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
total_v = total_t = 0
for m in meshes:
    total_v += len(m.data.vertices)
    total_t += len(m.data.polygons)
    d = m.dimensions
    print("[loco] mesh %-28s verts=%-6d faces=%-6d dims=%.2f x %.2f x %.2f mats=%s"
          % (m.name, len(m.data.vertices), len(m.data.polygons), d.x, d.y, d.z,
             [s.name for s in m.material_slots]))

print("[loco] totals verts=%d faces=%d" % (total_v, total_t))

# overall bounds in world space
xs, ys, zs = [], [], []
for m in meshes:
    for c in m.bound_box:
        w = m.matrix_world @ __import__("mathutils").Vector(c)
        xs.append(w.x); ys.append(w.y); zs.append(w.z)
if xs:
    print("[loco] world bounds X %.2f..%.2f  Y %.2f..%.2f  Z %.2f..%.2f"
          % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
    print("[loco] size  L(x)=%.2f  W(y)=%.2f  H(z)=%.2f" % (max(xs)-min(xs), max(ys)-min(ys), max(zs)-min(zs)))

for m in meshes:
    for slot in m.material_slots:
        mat = slot.material
        if not mat:
            continue
        print("[loco] material", mat.name, "nodes", mat.use_nodes)
        if mat.use_nodes:
            for n in mat.node_tree.nodes:
                if n.type == "TEX_IMAGE" and n.image:
                    print("[loco]   image", n.image.name, n.image.size[:], n.image.filepath)

print("[loco] actions:", [a.name for a in bpy.data.actions])
print("[loco] armatures:", [o.name for o in bpy.context.scene.objects if o.type == "ARMATURE"])
