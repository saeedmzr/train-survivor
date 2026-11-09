"""Set the walk loop to 30 frames (the numeric best), re-export the GLB, and render the walk
frames for a preview GIF."""
import bpy
import os

BLEND = r"C:\Users\saeed\Downloads\walker_animated.blend"
OUT_GLB = r"C:\Users\saeed\Downloads\walker_animated.glb"
LOOP = 30

bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == "ARMATURE")

for track in arm.animation_data.nla_tracks:
    s = track.strips[0]
    if track.name == "Walk":
        s.action_frame_start = 1
        s.action_frame_end = 1 + LOOP
        s.frame_start = 1
        s.frame_end = 1 + LOOP
    print("[fx] %-7s strip %d-%d" % (track.name, s.frame_start, s.frame_end))

scene.frame_start, scene.frame_end = 1, LOOP
bpy.ops.wm.save_as_mainfile(filepath=BLEND)

bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.gltf(
    filepath=OUT_GLB,
    export_format="GLB",
    use_selection=False,
    export_animations=True,
    export_animation_mode="NLA_TRACKS",
    export_nla_strips=True,
    export_frame_range=False,
    export_force_sampling=True,
)
print("[fx] exported", OUT_GLB)

# walk preview frames
act = bpy.data.actions["Walk"]
arm.animation_data.action = act
if hasattr(act, "slots") and len(act.slots):
    arm.animation_data.action_slot = act.slots[0]
out = r"C:\Users\saeed\Apps\img3d\out\final_seq\walk"
os.makedirs(out, exist_ok=True)
scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.render.resolution_x = 260
scene.render.resolution_y = 340
scene.render.image_settings.file_format = "PNG"
scene.frame_step = 1
for f in range(1, LOOP + 2):
    scene.frame_set(f)
    scene.render.filepath = os.path.join(out, "%04d.png" % f)
    bpy.ops.render.render(write_still=True)
print("[fx] walk preview frames rendered 1-%d" % (LOOP + 1))
