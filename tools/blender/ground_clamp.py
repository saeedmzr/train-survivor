"""Ground-clamp a clip: raise the root so the deformed mesh never sinks below z = 0.

Mixamo's death clip drives the hips below the floor (min z -0.41 m), which reads as the body
sinking through it. The hips' location F-curve is corrected per frame by the exact amount the
lowest vertex is below zero; the axis and scale are measured, not assumed, because Mixamo bones
do not use world axes.
"""
import bpy
import numpy as np

BLEND = r"C:\Users\saeed\Downloads\walker_animated.blend"
CLIPS = ("Death", "Attack")

bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == "ARMATURE")
mesh = next(o for o in scene.objects if o.type == "MESH")


def assign(a):
    arm.animation_data.action = a
    if hasattr(a, "slots") and len(a.slots):
        arm.animation_data.action_slot = a.slots[0]


def min_z():
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    return min((mesh.matrix_world @ v.co).z for v in ev.data.vertices)


for clip in CLIPS:
    act = bpy.data.actions[clip]
    assign(act)
    f0, f1 = (int(v) for v in act.frame_range)
    frames = list(range(f0, f1 + 1))

    lows = []
    for f in frames:
        scene.frame_set(f)
        lows.append(min_z())
    print("[gc] %s before: min z %.3f worst frame %d" % (clip, min(lows), frames[int(np.argmin(lows))]))

    if min(lows) > -0.01:
        print("[gc] %s already on the ground, skipping" % clip)
        continue

    # measure which local location axis lifts the mesh, and by how much per unit
    pb = arm.pose.bones.get("mixamorig:Hips")
    mid = frames[len(frames) // 2]
    scene.frame_set(mid)
    base = min_z()
    axis, scale = 1, 1.0
    best = 0.0
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
    print("[gc] %s root axis %d, %.3f m per unit" % (clip, axis, scale))

    fc = next((c for c in act.fcurves
               if c.data_path == 'pose.bones["mixamorig:Hips"].location' and c.array_index == axis), None)
    if fc is None:
        print("[gc] no hips location curve for axis", axis)
        continue

    corrections = [max(0.0, -z) / scale for z in lows]
    for f, c in zip(frames, corrections):
        if c <= 1e-4:
            continue
        cur = fc.evaluate(f)
        fc.keyframe_points.insert(f, cur + c, options={"FAST"})
    fc.update()

    # verify
    lows2 = []
    for f in frames:
        scene.frame_set(f)
        lows2.append(min_z())
    print("[gc] %s after: min z %.3f (was %.3f)" % (clip, min(lows2), min(lows)))

bpy.ops.wm.save_as_mainfile(filepath=BLEND)
print("[gc] saved", BLEND)

assign(bpy.data.actions["Walk"])
scene.frame_start, scene.frame_end = 1, 40
bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.gltf(
    filepath=r"C:\Users\saeed\Downloads\walker_animated.glb",
    export_format="GLB",
    use_selection=False,
    export_animations=True,
    export_animation_mode="ACTIONS",
    export_nla_strips=True,
    export_frame_range=False,
)
print("[gc] glb re-exported")
