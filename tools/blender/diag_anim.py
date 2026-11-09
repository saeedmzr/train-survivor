"""Diagnose why the assigned action does not move the rig."""
import bpy

bpy.ops.wm.open_mainfile(filepath=r"C:\Users\saeed\Downloads\walker_animated.blend")
scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == "ARMATURE")
mesh = next(o for o in scene.objects if o.type == "MESH")

print("[d] mesh", mesh.name, "modifiers", [(m.name, m.type, getattr(m, "object", None).name if getattr(m, "object", None) else None) for m in mesh.modifiers])
print("[d] vertex groups", len(mesh.vertex_groups), [g.name for g in mesh.vertex_groups][:5])
print("[d] parent", mesh.parent.name if mesh.parent else None, "parent_type", mesh.parent_type)

act = bpy.data.actions.get("Walk")
print("[d] action", act.name if act else None, "fcurves", len(act.fcurves) if act else 0)
print("[d] slots", [s.identifier for s in act.slots] if hasattr(act, "slots") else "no-slots-api")
print("[d] assigned", arm.animation_data.action.name if arm.animation_data and arm.animation_data.action else None)
print("[d] action_slot", getattr(arm.animation_data, "action_slot", None))

def probe(tag):
    pb = arm.pose.bones.get("mixamorig:LeftUpLeg")
    print("   %s frame %d LeftUpLeg head %s" % (tag, scene.frame_current, tuple(round(v, 3) for v in pb.head)))

arm.animation_data.action = act
probe("no-slot")
for f in (1, 30, 60):
    scene.frame_set(f)
    probe("no-slot")

if hasattr(act, "slots") and len(act.slots):
    arm.animation_data.action_slot = act.slots[0]
    print("[d] set slot ->", act.slots[0].identifier)
    for f in (1, 30, 60):
        scene.frame_set(f)
        probe("with-slot")

# does the mesh actually follow the bones?
dg = bpy.context.evaluated_depsgraph_get()
ev = mesh.evaluated_get(dg)
import mathutils
co = ev.data.vertices[0].co.copy()
scene.frame_set(1); dg.update()
c1 = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get()).data.vertices[0].co.copy()
scene.frame_set(40); dg.update()
c2 = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get()).data.vertices[0].co.copy()
print("[d] mesh vert0 frame1", tuple(round(v, 4) for v in c1), "frame40", tuple(round(v, 4) for v in c2),
      "moved", round((c2 - c1).length, 5))
print("[d] scene fps", scene.render.fps)
