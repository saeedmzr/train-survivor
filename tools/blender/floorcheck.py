"""Numeric floor check: lowest and highest deformed vertex per clip."""
import bpy

bpy.ops.wm.open_mainfile(filepath=r"C:\Users\saeed\Downloads\walker_animated.blend")
scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == "ARMATURE")
mesh = next(o for o in scene.objects if o.type == "MESH")


def assign(a):
    arm.animation_data.action = a
    if hasattr(a, "slots") and len(a.slots):
        arm.animation_data.action_slot = a.slots[0]


for name in ("Walk", "Attack", "Death"):
    act = bpy.data.actions[name]
    assign(act)
    f0, f1 = (int(v) for v in act.frame_range)
    lows = []
    for f in range(f0, f1 + 1, 4):
        scene.frame_set(f)
        dg = bpy.context.evaluated_depsgraph_get()
        ev = mesh.evaluated_get(dg)
        minz = min((mesh.matrix_world @ v.co).z for v in ev.data.vertices)
        lows.append((f, minz))
    worst = min(lows, key=lambda p: p[1])
    print("[floor] %s frames %d-%d  min z %.3f at frame %d  (start z %.3f, end z %.3f)"
          % (name, f0, f1, worst[1], worst[0], lows[0][1], lows[-1][1]))
