"""Inspect the four final Mixamo clips: length, bone names, and whether the hips carry root
translation (i.e. whether 'In Place' was ticked in Mixamo).
"""
import bpy
import os

FILES = [
    r"C:\Users\saeed\Downloads\Zombie Walk.fbx",
    r"C:\Users\saeed\Downloads\Zombie Attack (1).fbx",
    r"C:\Users\saeed\Downloads\Zombie Dying (1).fbx",
    r"C:\Users\saeed\Downloads\Zombie Scream.fbx",
]

for path in FILES:
    print("\n=====", os.path.basename(path))
    if not os.path.exists(path):
        print("  MISSING")
        continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.object.add(type="EMPTY")
    bpy.context.view_layer.objects.active = bpy.context.selected_objects[0]
    bpy.ops.import_scene.fbx(filepath=path)

    arm = next((o for o in bpy.context.scene.objects if o.type == "ARMATURE"), None)
    if arm is None:
        print("  no armature")
        continue
    act = arm.animation_data.action if arm.animation_data else None
    if act is None:
        print("  no action")
        continue
    f0, f1 = act.frame_range
    dur = (f1 - f0) / 30.0
    hips_paths = [c for c in act.fcurves if 'Hips"].location' in c.data_path]
    hips_range = ""
    for c in hips_paths:
        vals = [k.co[1] for k in c.keyframe_points]
        if vals:
            hips_range += " axis%d[%.3f..%.3f]" % (c.array_index, min(vals), max(vals))
    print("  bones %d  action %s  frames %d-%d (%.2fs)" % (len(arm.data.bones), act.name, f0, f1, dur))
    print("  hips location curves:%s" % (hips_range if hips_range else " NONE (in place)"))
    print("  first bone names:", [b.name for b in arm.data.bones][:6])
