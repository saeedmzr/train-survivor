"""Render full clips with EEVEE (fast) and re-export the GLB with the rebuilt weights."""
import bpy
import os

BLEND = r"C:\Users\saeed\Downloads\walker_animated.blend"
OUT_DIR = r"C:\Users\saeed\Apps\img3d\out\anim_seq"
os.makedirs(OUT_DIR, exist_ok=True)

bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == "ARMATURE")
if arm.animation_data is None:
    arm.animation_data_create()

scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.render.resolution_x = 300
scene.render.resolution_y = 400
scene.render.fps = 30
scene.render.image_settings.file_format = "PNG"
scene.frame_step = 1


def assign(action):
    arm.animation_data.action = action
    if hasattr(action, "slots") and len(action.slots):
        arm.animation_data.action_slot = action.slots[0]


for name in ("Walk", "Attack", "Death"):
    act = bpy.data.actions.get(name)
    if not act:
        continue
    assign(act)
    f0, f1 = (int(v) for v in act.frame_range)
    scene.frame_start, scene.frame_end = f0, f1
    scene.render.filepath = os.path.join(OUT_DIR, name.lower(), "")
    bpy.ops.render.render(animation=True)
    print("[seq] %s %d frames ->" % (name, f1 - f0 + 1), scene.render.filepath)

# leave the walk active and export everything
assign(bpy.data.actions["Walk"])
scene.frame_start, scene.frame_end = 1, 40
bpy.ops.wm.save_as_mainfile(filepath=BLEND)
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
print("[seq] glb exported", [a.name for a in bpy.data.actions])
