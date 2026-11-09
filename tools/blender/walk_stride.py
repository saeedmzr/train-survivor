"""Measure the walk clip's stride: how far the planted foot travels per cycle, and how many steps
it contains. Playback speed can then be derived from the enemy's real world speed so the feet
stop sliding.
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
fps = scene.render.fps
act = next(a for a in bpy.data.actions if "shamble" in a.name.lower())
arm.animation_data.action = act
if hasattr(act, "slots") and len(act.slots):
    arm.animation_data.action_slot = act.slots[0]

f0, f1 = (int(v) for v in act.frame_range)
hips = arm.pose.bones["mixamorig:Hips"]
lt = arm.pose.bones["mixamorig:LeftToeBase"]
rt = arm.pose.bones["mixamorig:RightToeBase"]

forward = []   # foot Y relative to hips (Blender: character faces -Y)
lz, rz = [], []
for f in range(f0, f1 + 1):
    scene.frame_set(f)
    bpy.context.view_layer.update()
    h = hips.head
    fl = lt.head
    fr = rt.head
    forward.append((fl.y - h.y, fr.y - h.y))
    lz.append(fl.z)
    rz.append(fr.z)

fwd = np.array(forward)
lz = np.array(lz); rz = np.array(rz)

# contacts = local minima of foot height
def contacts(z):
    zs = np.convolve(z, np.ones(3) / 3, mode="same")
    idx = [i for i in range(1, len(zs) - 1) if zs[i] <= zs[i - 1] and zs[i] <= zs[i + 1]]
    out = []
    for i in idx:
        if not out or i - out[-1] > 6:
            out.append(i)
    return out

cl, cr = contacts(lz), contacts(rz)
cycle_s = (f1 - f0) / fps
stride_left = float(fwd[:, 0].max() - fwd[:, 0].min())
stride_right = float(fwd[:, 1].max() - fwd[:, 1].min())
steps = len(cl) + len(cr)
travel_per_cycle = (stride_left + stride_right) * (steps / max(1, len(cl) + len(cr)))
print("[stride] cycle %.2fs (%d frames @ %dfps)" % (cycle_s, f1 - f0 + 1, fps))
print("[stride] left foot travel %.3f m, right %.3f m" % (stride_left, stride_right))
print("[stride] foot contacts L=%d R=%d" % (len(cl), len(cr)))
print("[stride] implied ground speed at playback 1.0: %.2f m/s" %
      (((stride_left + stride_right) / 2.0 * (steps / 2.0)) / cycle_s))
