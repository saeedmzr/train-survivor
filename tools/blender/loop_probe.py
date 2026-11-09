"""Find the walk loop period by comparing deformed vertex positions (robust to pose noise)."""
import bpy
import numpy as np

BLEND = r"C:\Users\saeed\Downloads\walker_animated.blend"
bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == "ARMATURE")
mesh = next(o for o in scene.objects if o.type == "MESH")
act = bpy.data.actions["Walk"]
arm.animation_data.action = act
if hasattr(act, "slots") and len(act.slots):
    arm.animation_data.action_slot = act.slots[0]


def verts(frame):
    """Local pose channels only: root translation is excluded so a travelling clip still loops."""
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    vals = []
    for pb in arm.pose.bones:
        m = pb.matrix_basis
        vals.extend((m.translation.x, m.translation.y, m.translation.z,
                     m.to_quaternion().w, m.to_quaternion().x, m.to_quaternion().y, m.to_quaternion().z))
    return np.array(vals)


base = verts(1)
print("[lp] lag  rms(m)")
best = (None, 1e9)
for lag in range(20, 91, 2):
    v = verts(1 + lag)
    rms = float(np.sqrt(np.mean((v - base) ** 2)))
    if rms < best[1]:
        best = (lag, rms)
    if lag % 10 == 0 or rms < 0.02:
        print("[lp] %3d  %.4f" % (lag, rms))
print("[lp] best lag %d (%.2f s) rms %.4f" % (best[0], best[0] / 30.0, best[1]))
