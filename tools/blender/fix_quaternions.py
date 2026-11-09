"""Enforce quaternion continuity on every rotation curve.

A quaternion and its negation are the same rotation, but interpolating between them takes the
long way round - that is the upside-down frames in the attack clip. For each bone, walk the
frames and negate any quaternion whose dot product with the previous one is negative.
"""
import bpy
import numpy as np

BLEND = r"C:\Users\saeed\Downloads\walker_animated.blend"
bpy.ops.wm.open_mainfile(filepath=BLEND)

fixed_total = 0
for act in bpy.data.actions:
    groups = {}
    for fc in act.fcurves:
        if not fc.data_path.endswith("rotation_quaternion"):
            continue
        groups.setdefault(fc.data_path, {})[fc.array_index] = fc

    for path, comps in groups.items():
        if len(comps) != 4:
            continue
        frames = [kp.co[0] for kp in comps[0].keyframe_points]
        n = len(frames)
        if n < 2:
            continue
        vals = np.zeros((n, 4))
        for i in range(4):
            arr = np.zeros(2 * n)
            comps[i].keyframe_points.foreach_get("co", arr)
            vals[:, i] = arr[1::2]

        flips = 0
        for k in range(1, n):
            if float(np.dot(vals[k], vals[k - 1])) < 0.0:
                vals[k] *= -1.0
                flips += 1

        if flips:
            for i in range(4):
                arr = np.zeros(2 * n)
                comps[i].keyframe_points.foreach_get("co", arr)
                arr[1::2] = vals[:, i]
                comps[i].keyframe_points.foreach_set("co", arr)
                comps[i].update()
            fixed_total += flips

    print("[q] %-7s processed" % act.name)

print("[q] quaternion sign flips corrected:", fixed_total)
bpy.ops.wm.save_as_mainfile(filepath=BLEND)

bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.gltf(
    filepath=r"C:\Users\saeed\Downloads\walker_animated.glb",
    export_format="GLB",
    use_selection=False,
    export_animations=True,
    export_animation_mode="NLA_TRACKS",
    export_nla_strips=True,
    export_frame_range=False,
    export_force_sampling=True,
)
print("[q] exported glb")
