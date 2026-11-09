"""Render walk frames at even spacing with frame numbers so a true loop range can be chosen."""
import bpy
import os

BLEND = r"C:\Users\saeed\Downloads\walker_animated.blend"
OUT = r"C:\Users\saeed\Apps\img3d\out\walkpicks"
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == "ARMATURE")
act = bpy.data.actions["Walk"]
arm.animation_data.action = act
if hasattr(act, "slots") and len(act.slots):
    arm.animation_data.action_slot = act.slots[0]

scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.render.resolution_x = 190
scene.render.resolution_y = 280
scene.render.image_settings.file_format = "PNG"

for i, f in enumerate(range(1, 202, 10)):
    scene.frame_set(f)
    scene.render.filepath = os.path.join(OUT, "f%03d.png" % f)
    bpy.ops.render.render(write_still=True)
print("[wp] rendered frames 1-201 step 10")
