"""Render every clip from its NLA strip range so the previews show exactly what the game gets."""
import bpy
import os

BLEND = r"C:\Users\saeed\Downloads\walker_animated.blend"
OUT = r"C:\Users\saeed\Apps\img3d\out\final_seq"
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == "ARMATURE")

scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.render.resolution_x = 260
scene.render.resolution_y = 340
scene.render.fps = 30
scene.render.image_settings.file_format = "PNG"
scene.frame_step = 2

spans = {}
for track in arm.animation_data.nla_tracks:
    s = track.strips[0]
    spans[s.action.name] = (int(s.frame_start), int(s.frame_end))

for name, act in [(a.name, a) for a in bpy.data.actions]:
    if name not in spans:
        continue
    arm.animation_data.action = act
    if hasattr(act, "slots") and len(act.slots):
        arm.animation_data.action_slot = act.slots[0]
    start, end = spans[name]
    scene.frame_start, scene.frame_end = start, end
    scene.render.filepath = os.path.join(OUT, name.lower(), "")
    bpy.ops.render.render(animation=True)
    print("[rf] %-7s %d-%d rendered" % (name, start, end))
