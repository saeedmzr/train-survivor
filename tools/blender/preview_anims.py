"""Render preview frames for each stashed clip from the current walker_animated.blend."""
import bpy
import os

BLEND = r"C:\Users\saeed\Downloads\walker_animated.blend"
OUT_DIR = r"C:\Users\saeed\Apps\img3d\out\anim2"
os.makedirs(OUT_DIR, exist_ok=True)

bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == "ARMATURE")
if arm.animation_data is None:
    arm.animation_data_create()


def assign(action):
    arm.animation_data.action = action
    if hasattr(action, "slots") and len(action.slots):
        arm.animation_data.action_slot = action.slots[0]


scene.render.resolution_x = 420
scene.render.resolution_y = 540
scene.cycles.samples = 24

for name in ("Walk", "Attack", "Death"):
    act = bpy.data.actions.get(name)
    if act is None:
        print("[prev] missing action", name)
        continue
    assign(act)
    f0, f1 = act.frame_range
    n = 6
    for i in range(n):
        f = int(f0 + (f1 - f0) * i / (n - 1))
        scene.frame_set(f)
        scene.render.filepath = os.path.join(OUT_DIR, "%s_%02d.png" % (name.lower(), i))
        bpy.ops.render.render(write_still=True)
    print("[prev] %s frames %d-%d rendered" % (name, f0, f1))
