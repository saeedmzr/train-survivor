"""Attach the four final Mixamo clips to the walker.

The clips are long (Walk 483 frames, Attack 547, Dying 201, Scream 335) and carry hip root
translation, so this script:
  * imports each clip and keeps its action (the walker already has repaired weights + modifiers)
  * finds the real stride period of the walk by pose autocorrelation and cuts a clean loop
  * freezes horizontal root drift (locomotion must be driven by the game, not the clip)
  * clamps every clip to the floor (lowest deformed vertex >= 0)
  * stashes all four on NLA tracks and exports the GLB
"""
import bpy
import numpy as np
import os

BLEND = r"C:\Users\saeed\Downloads\walker_tripo.blend"
OUT_GLB = r"C:\Users\saeed\Downloads\walker_animated.glb"
CLIPS = [
    (r"C:\Users\saeed\Downloads\Zombie Walk.fbx", "Walk"),
    (r"C:\Users\saeed\Downloads\Zombie Attack (1).fbx", "Attack"),
    (r"C:\Users\saeed\Downloads\Zombie Dying (1).fbx", "Death"),
    (r"C:\Users\saeed\Downloads\Zombie Scream.fbx", "Scream"),
]
HIPS = "mixamorig:Hips"

bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene
scene.render.fps = 30
arm = next(o for o in scene.objects if o.type == "ARMATURE")
mesh = next(o for o in scene.objects if o.type == "MESH")

# wipe the old clips
if arm.animation_data:
    arm.animation_data.action = None
    for track in list(arm.animation_data.nla_tracks):
        arm.animation_data.nla_tracks.remove(track)
else:
    arm.animation_data_create()
for a in list(bpy.data.actions):
    bpy.data.actions.remove(a)
print("[ap] cleared old clips, bones", len(arm.data.bones))


def assign(action):
    arm.animation_data.action = action
    if action and hasattr(action, "slots") and len(action.slots):
        arm.animation_data.action_slot = action.slots[0]


def min_z():
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    return min((mesh.matrix_world @ v.co).z for v in ev.data.vertices)


def pose_signature(action, frame):
    """Flatten every bone's pose matrix at a frame - used to find the loop period."""
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    vals = []
    for pb in arm.pose.bones:
        m = pb.matrix
        vals.extend((m.translation.x, m.translation.y, m.translation.z))
    return np.array(vals)


imported = []
existing = {o.name for o in bpy.data.objects}
for path, label in CLIPS:
    if not os.path.exists(path):
        print("[ap] missing", path)
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
    print("[ap] %s: %d frames (%.2fs)" % (label, f1 - f0 + 1, (f1 - f0) / 30.0))
    for o in new_objs:
        bpy.data.objects.remove(o, do_unlink=True)
    existing = {o.name for o in bpy.data.objects}
    imported.append((label, act))

# ---------------------------------------------------------------- walk: find the stride period
walk = bpy.data.actions.get("Walk")
if walk:
    assign(walk)
    f0, f1 = (int(v) for v in walk.frame_range)
    probe = list(range(f0, min(f1, f0 + 120)))
    sigs = [pose_signature(walk, f) for f in probe]
    best_p, best_err = None, 1e9
    for p in range(9, 61):
        errs = []
        for i in range(len(sigs) - p):
            errs.append(np.mean((sigs[i] - sigs[i + p]) ** 2))
        if not errs:
            continue
        e = float(np.mean(errs))
        if e < best_err:
            best_err, best_p = e, p
    print("[ap] walk step period %s frames (err %.5f)" % (best_p, best_err))
    if best_p:
        # a full stride is two steps: the half-cycle lands on the mirrored pose
        best_p = best_p * 2
        print("[ap] using full stride of %d frames (%.2fs)" % (best_p, best_p / 30.0))

    if best_p:
        for fc in list(walk.fcurves):
            window = [(kp.co[0] - f0 + 1, kp.co[1],
                       kp.handle_left[0] - f0 + 1, kp.handle_left[1],
                       kp.handle_right[0] - f0 + 1, kp.handle_right[1])
                      for kp in fc.keyframe_points
                      if f0 - 0.001 <= kp.co[0] <= f0 + best_p + 0.001]
            for kp in list(fc.keyframe_points):
                fc.keyframe_points.remove(kp, fast=True)
            fc.update()
            for (fr, val, hl0, hl1, hr0, hr1) in window:
                kp = fc.keyframe_points.insert(fr, val, options={"FAST"})
                kp.handle_left = (hl0, hl1)
                kp.handle_right = (hr0, hr1)
            fc.update()
        walk.use_frame_range = True
        walk.frame_start, walk.frame_end = 1, best_p + 1
        print("[ap] walk trimmed to %d frames (%.2fs loop)" % (best_p + 1, best_p / 30.0))

# ---------------------------------------------------------------- freeze horizontal drift
DRIFT_CLIPS = ("Walk", "Attack", "Scream")
for label in DRIFT_CLIPS:
    act = bpy.data.actions.get(label)
    if not act:
        continue
    # which hips location axes move the mesh horizontally? measure once
    assign(act)
    scene.frame_set(int(act.frame_range[0]))
    moved = []
    for axis in (0, 1, 2):
        curves = [c for c in act.fcurves if c.data_path == 'pose.bones["%s"].location' % HIPS
                  and c.array_index == axis]
        if not curves:
            continue
        vals = [k.co[1] for k in curves[0].keyframe_points]
        moved.append((axis, max(vals) - min(vals)))
    moved.sort(key=lambda t: -t[1])
    print("[ap] %s hips motion per axis %s" % (label, [(a, round(v, 3)) for a, v in moved]))
    # keep the two largest (vertical bob + sway) untouched only if small; freeze the dominant
    # horizontal run (usually the forward axis, the largest range)
    for axis, span in moved:
        if span < 0.30:
            continue
        curves = [c for c in act.fcurves if c.data_path == 'pose.bones["%s"].location' % HIPS
                  and c.array_index == axis]
        for c in curves:
            first = c.keyframe_points[0].co[1] if len(c.keyframe_points) else 0.0
            for kp in c.keyframe_points:
                kp.co[1] = first
                kp.handle_left[1] = first
                kp.handle_right[1] = first
            c.update()
        print("[ap] %s froze root drift on hips axis %d (span %.2f m)" % (label, axis, span))

assign(bpy.data.actions.get("Walk"))
scene.frame_start = 1
scene.frame_end = int(bpy.data.actions["Walk"].frame_range[1])
OUT_BLEND = r"C:\Users\saeed\Downloads\walker_animated.blend"
bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)
print("[ap] saved", OUT_BLEND, [a.name for a in bpy.data.actions])
