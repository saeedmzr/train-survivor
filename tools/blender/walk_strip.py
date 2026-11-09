"""Render a strip of frames spread across the whole Zombie Walk clip so its structure is visible."""
import bpy
import os

BLEND = r"C:\Users\saeed\Downloads\walker_animated.blend"
OUT = r"C:\Users\saeed\Apps\img3d\out\walkstrip"
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == "ARMATURE")
act = bpy.data.actions["Walk"]
arm.animation_data.action = act
if hasattr(act, "slots") and len(act.slots):
    arm.animation_data.action_slot = act.slots[0]

scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.render.resolution_x = 200
scene.render.resolution_y = 300
scene.render.image_settings.file_format = "PNG"

f0, f1 = (int(v) for v in act.frame_range)
n = 16
for i in range(n):
    f = int(f0 + (f1 - f0) * i / (n - 1))
    scene.frame_set(f)
    scene.render.filepath = os.path.join(OUT, "f%03d.png" % i)
    bpy.ops.render.render(write_still=True)
print("[ws] rendered", n, "frames across", f0, "-", f1)
