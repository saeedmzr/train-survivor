"""Render close-ups of the walk cycle to identify what is stretching, then test two fixes:
dual-quaternion skinning (Preserve Volume) and a Corrective Smooth modifier.
"""
import bpy
import os

BLEND = r"C:\Users\saeed\Downloads\walker_animated.blend"
OUT = r"C:\Users\saeed\Apps\img3d\out\fixcheck"
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == "ARMATURE")
mesh = next(o for o in scene.objects if o.type == "MESH")
act = bpy.data.actions["Walk"]
arm.animation_data.action = act
if hasattr(act, "slots") and len(act.slots):
    arm.animation_data.action_slot = act.slots[0]

scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.render.resolution_x = 640
scene.render.resolution_y = 820
scene.render.image_settings.file_format = "PNG"

amod = next(m for m in mesh.modifiers if m.type == "ARMATURE")
print("[fc] armature modifier use_deform_preserve_volume", amod.use_deform_preserve_volume)
print("[fc] modifiers", [(m.name, m.type) for m in mesh.modifiers])

FRAMES = (25, 45, 65)


def shoot(tag):
    for f in FRAMES:
        scene.frame_set(f)
        scene.render.filepath = os.path.join(OUT, "%s_%03d.png" % (tag, f))
        bpy.ops.render.render(write_still=True)
    print("[fc] rendered", tag)


shoot("a_raw")

amod.use_deform_preserve_volume = True
cs = mesh.modifiers.new("CorrectiveSmooth", "CORRECTIVE_SMOOTH")
cs.factor = 0.6
cs.iterations = 12
cs.smooth_type = "LENGTH_WEIGHTED"
cs.rest_source = "ORCO"
shoot("b_fixed")

print("[fc] done")
