"""Attach the four final Mixamo clips to the walker, trim the walk to one stride via NLA strip
ranges (no keyframe surgery), keep them named, and save. Weights are repaired afterwards by
fix_weights.py, so no clamping happens here.
"""
import bpy
import numpy as np
import os

BLEND = r"C:\Users\saeed\Downloads\walker_tripo.blend"
OUT_BLEND = r"C:\Users\saeed\Downloads\walker_animated.blend"
CLIPS = [
    (r"C:\Users\saeed\Downloads\Zombie Walk.fbx", "Walk"),
    (r"C:\Users\saeed\Downloads\Zombie Attack (1).fbx", "Attack"),
    (r"C:\Users\saeed\Downloads\Zombie Dying (1).fbx", "Death"),
    (r"C:\Users\saeed\Downloads\Zombie Scream.fbx", "Scream"),
]

bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene
scene.render.fps = 30
arm = next(o for o in scene.objects if o.type == "ARMATURE")

if arm.animation_data:
    arm.animation_data.action = None
    for t in list(arm.animation_data.nla_tracks):
        arm.animation_data.nla_tracks.remove(t)
else:
    arm.animation_data_create()
for a in list(bpy.data.actions):
    bpy.data.actions.remove(a)


def assign(action):
    arm.animation_data.action = action
    if action and hasattr(action, "slots") and len(action.slots):
        arm.animation_data.action_slot = action.slots[0]


imported = []
existing = {o.name for o in bpy.data.objects}
for path, label in CLIPS:
    if not os.path.exists(path):
        print("[ap] MISSING", path)
        continue
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = arm
    bpy.ops.import_scene.fbx(filepath=path)
    new_objs = [o for o in bpy.data.objects if o.name not in existing]
    src = next((o for o in new_objs if o.type == "ARMATURE"), None)
    act = src.animation_data.action if src and src.animation_data else None
    if act is None:
        print("[ap] no action for", label)
        continue
    act.name = label
    act.use_fake_user = True
    f0, f1 = (int(v) for v in act.frame_range)
    print("[ap] %s imported: %d frames (%.2fs)" % (label, f1 - f0 + 1, (f1 - f0) / 30.0))
    for o in new_objs:
        bpy.data.objects.remove(o, do_unlink=True)
    existing = {o.name for o in bpy.data.objects}
    imported.append((label, act))


def toe_z(frame):
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    return (arm.matrix_world @ arm.pose.bones["mixamorig:LeftToeBase"].head).z


walk = bpy.data.actions.get("Walk")
stride = None
if walk:
    assign(walk)
    f0, f1 = (int(v) for v in walk.frame_range)
    frames = list(range(f0, min(f1, f0 + 240)))
    zs = np.array([toe_z(f) for f in frames], dtype=np.float64)
    # smooth, then autocorrelate: the left toe rises and falls once per stride
    sm = np.convolve(zs, np.ones(3) / 3.0, mode="same")
    d = sm - sm.mean()
    best_lag, best_corr = None, -2.0
    for lag in range(12, 61):
        a, b = d[:-lag], d[lag:]
        if len(a) < 30:
            break
        c = float(np.corrcoef(a, b)[0, 1])
        if c > best_corr:
            best_corr, best_lag = c, lag
    stride = best_lag
    print("[ap] walk autocorrelation: stride %s frames (%s s, corr %.2f)"
          % (stride, round(stride / 30.0, 2), best_corr))

for label, act in imported:
    f0, f1 = (int(v) for v in act.frame_range)
    span = stride if (label == "Walk" and stride) else (f1 - f0)
    track = arm.animation_data.nla_tracks.new()
    track.name = label
    strip = track.strips.new(label, 1, act)
    strip.name = label
    strip.action_frame_start = f0
    strip.action_frame_end = f0 + span
    strip.frame_start = 1
    strip.frame_end = 1 + span
    track.mute = True
    print("[ap] strip %-7s 1-%d (source %.0f-%.0f of %.0f-%.0f)"
          % (label, span, f0, f0 + span, f0, f1))

assign(bpy.data.actions.get("Walk"))
scene.frame_start, scene.frame_end = 1, stride if stride else 40
bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)
print("[ap] saved", OUT_BLEND, [a.name for a in bpy.data.actions], "stride", stride)
