"""Find the active window inside each walker take: the frames where the body actually moves.
Mixamo/Tripo takes are 240 frames with long idle tails, which is why an attack or death lasts 10 s.
"""
import bpy
import numpy as np

SRC = r"C:\Users\saeed\Downloads\tripo_convert_dfc747a6-ba7d-4494-a375-982a4fe93742\tripo_convert_dfc747a6-ba7d-4494-a375-982a4fe93742.fbx"
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.object.add(type="EMPTY")
bpy.context.view_layer.objects.active = bpy.context.selected_objects[0]
bpy.ops.import_scene.fbx(filepath=SRC)

arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
scene = bpy.context.scene
watch = ["mixamorig:Hips", "mixamorig:Spine1", "mixamorig:LeftHand", "mixamorig:RightHand",
         "mixamorig:LeftFoot", "mixamorig:RightFoot", "mixamorig:Head"]

for act in bpy.data.actions:
    arm.animation_data.action = act
    if hasattr(act, "slots") and len(act.slots):
        arm.animation_data.action_slot = act.slots[0]
    f0, f1 = (int(v) for v in act.frame_range)
    prev = None
    motion = []
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        bpy.context.view_layer.update()
        cur = np.array([tuple(arm.pose.bones[b].head) for b in watch]).ravel()
        motion.append(0.0 if prev is None else float(np.linalg.norm(cur - prev)))
        prev = cur
    motion = np.array(motion)
    peak = motion.max()
    active = np.nonzero(motion > peak * 0.12)[0]
    a0, a1 = (f0 + int(active.min()), f0 + int(active.max())) if active.size else (f0, f1)
    print("[win] %-70s frames %d-%d  active %d-%d (%d frames, peak %.3f)"
          % (act.name[:70], f0, f1, a0, a1, a1 - a0 + 1, peak))
    # where is the single biggest motion (the hit / the collapse)?
    print("[win]    peak motion at frame %d" % (f0 + int(np.argmax(motion))))
