"""Finalise the four clips: pick a sensible walk loop, set NLA strip ranges, clamp every clip to
the floor (lowest deformed vertex >= 0), save and export the GLB.
"""
import bpy
import numpy as np

BLEND = r"C:\Users\saeed\Downloads\walker_animated.blend"
OUT_GLB = r"C:\Users\saeed\Downloads\walker_animated.glb"
HIPS = "mixamorig:Hips"

bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene
scene.render.fps = 30
arm = next(o for o in scene.objects if o.type == "ARMATURE")
mesh = next(o for o in scene.objects if o.type == "MESH")


def assign(a):
    arm.animation_data.action = a
    if a and hasattr(a, "slots") and len(a.slots):
        arm.animation_data.action_slot = a.slots[0]


def min_z():
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    return min((mesh.matrix_world @ v.co).z for v in ev.data.vertices)


def signature(f):
    scene.frame_set(f)
    bpy.context.view_layer.update()
    vals = []
    for pb in list(arm.pose.bones)[::4]:
        m = pb.matrix
        vals.extend((m.translation.x, m.translation.y, m.translation.z))
    return np.array(vals)


# ---------------------------------------------------------------- walk loop
walk = bpy.data.actions["Walk"]
assign(walk)
f0, f1 = (int(v) for v in walk.frame_range)
sigs = [signature(f) for f in range(f0, min(f1, f0 + 165))]
errs = []
for lag in range(20, 51):
    e = float(np.mean([np.mean((sigs[i] - sigs[i + lag]) ** 2) for i in range(len(sigs) - lag)]))
    errs.append((lag, e))
errs.sort(key=lambda t: t[1])
print("[fn] best walk lags:", [(l, round(e, 5)) for l, e in errs[:5]])
stride = errs[0][0]
print("[fn] walk loop = %d frames (%.2fs)" % (stride, stride / 30.0))

# ---------------------------------------------------------------- strip ranges
for track in arm.animation_data.nla_tracks:
    act = track.strips[0].action
    span = stride if act.name == "Walk" else int(act.frame_range[1] - act.frame_range[0])
    s = track.strips[0]
    s.action_frame_start = act.frame_range[0]
    s.action_frame_end = act.frame_range[0] + span
    s.frame_start = 1
    s.frame_end = 1 + span
    print("[fn] %-7s strip 1-%d" % (act.name, span))

# ---------------------------------------------------------------- floor clamp
for name in ("Walk", "Attack", "Death", "Scream"):
    act = bpy.data.actions.get(name)
    if not act:
        continue
    assign(act)
    a0, a1 = (int(v) for v in act.frame_range)
    span = stride if name == "Walk" else (a1 - a0)
    frames = list(range(a0, a0 + span + 1))
    lows = []
    for f in frames:
        scene.frame_set(f)
        lows.append(min_z())
    low = min(lows)
    print("[fn] %-7s floor min z %.3f over %d frames" % (name, low, len(frames)))
    if low > -0.012:
        continue
    pb = arm.pose.bones[HIPS]
    scene.frame_set(frames[len(frames) // 4])
    base = min_z()
    axis, best = 1, 0.0
    for i in range(3):
        old = pb.location[i]
        pb.location[i] = old + 0.2
        bpy.context.view_layer.update()
        d = (min_z() - base) / 0.2
        pb.location[i] = old
        bpy.context.view_layer.update()
        if abs(d) > abs(best):
            best, axis = d, i
    scale = best if abs(best) > 1e-4 else 1.0
    fc = next((c for c in act.fcurves
               if c.data_path == 'pose.bones["%s"].location' % HIPS and c.array_index == axis), None)
    if fc is None:
        print("[fn] %s: no hips curve, cannot clamp" % name)
        continue
    for f, z in zip(frames, lows):
        c = max(0.0, -z) / scale
        if c > 1e-4:
            fc.keyframe_points.insert(f, fc.evaluate(f) + c, options={"FAST"})
    fc.update()
    lows2 = [(scene.frame_set(f), min_z())[1] for f in frames]
    print("[fn] %-7s clamped %.3f -> %.3f" % (name, low, min(lows2)))

assign(bpy.data.actions["Walk"])
scene.frame_start, scene.frame_end = 1, stride
bpy.ops.wm.save_as_mainfile(filepath=BLEND)

bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.gltf(
    filepath=OUT_GLB,
    export_format="GLB",
    use_selection=False,
    export_animations=True,
    export_animation_mode="ACTIONS",
    export_nla_strips=True,
    export_frame_range=False,
)
print("[fn] exported", OUT_GLB, [a.name for a in bpy.data.actions])
