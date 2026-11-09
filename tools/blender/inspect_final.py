"""Inspect the final walker FBX: takes, bones, size, texture link."""
import bpy
import os

SRC = r"C:\Users\saeed\Downloads\tripo_convert_dfc747a6-ba7d-4494-a375-982a4fe93742\tripo_convert_dfc747a6-ba7d-4494-a375-982a4fe93742.fbx"
TEX = r"C:\Users\saeed\Downloads\tripo_convert_dfc747a6-ba7d-4494-a375-982a4fe93742\tripo_convert_dfc747a6-ba7d-4494-a375-982a4fe93742.fbm\tripo_rgb_0a31bdea-b2b1-4fb7-89eb-f065f4930fbd.jpg"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.object.add(type="EMPTY")
bpy.context.view_layer.objects.active = bpy.context.selected_objects[0]
bpy.ops.import_scene.fbx(filepath=SRC)

objs = [(o.name, o.type) for o in bpy.context.scene.objects]
print("[in] objects:", objs)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
arms = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
for m in meshes:
    d = m.dimensions
    print("[in] mesh %s verts=%d tris=%d dims=%.2f x %.2f x %.2f uv=%d vgroups=%d mats=%s"
          % (m.name, len(m.data.vertices), len(m.data.polygons), d.x, d.y, d.z,
             len(m.data.uv_layers), len(m.vertex_groups), [s.name for s in m.material_slots]))
for a in arms:
    print("[in] armature %s bones=%d first=%s" % (a.name, len(a.data.bones), [b.name for b in a.data.bones][:3]))
    act = a.animation_data.action if a.animation_data else None
    print("[in]   active action:", act.name if act else None,
          "range", tuple(round(v) for v in act.frame_range) if act else None)
    if a.animation_data:
        for t in a.animation_data.nla_tracks:
            print("[in]   nla track %s strips %s" % (t.name, [(s.name, s.action.name if s.action else None,
                                                              round(s.frame_start), round(s.frame_end)) for s in t.strips]))
print("[in] actions in file:", [(a.name, tuple(round(v) for v in a.frame_range)) for a in bpy.data.actions])
print("[in] texture exists:", os.path.exists(TEX))
print("[in] scene fps", bpy.context.scene.render.fps)
