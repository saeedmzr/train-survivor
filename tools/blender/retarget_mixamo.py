"""Attach the three Mixamo clips to the walker rig and render preview frames.

The walker (walker_tripo.blend) is skinned to Mixamo's 65-bone skeleton (mixamorig:*), and the
downloaded clips carry the same armature, so the actions can be moved across verbatim — no
retargeting, no bone mapping. Each action is stashed on its own NLA track so the glTF export
ships all three clips, and one is left active for preview.
"""
import bpy
import math
import os

BLEND = r"C:\Users\saeed\Downloads\walker_tripo.blend"
OUT_BLEND = r"C:\Users\saeed\Downloads\walker_animated.blend"
OUT_DIR = r"C:\Users\saeed\Apps\img3d\out\anim"
CLIPS = [
    (r"C:\Users\saeed\Downloads\Walking.fbx", "Walk"),
    (r"C:\Users\saeed\Downloads\Zombie Attack.fbx", "Attack"),
    (r"C:\Users\saeed\Downloads\Zombie Dying.fbx", "Death"),
]
os.makedirs(OUT_DIR, exist_ok=True)

bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene
scene.render.fps = 30          # Mixamo authors at 30 fps; the blend defaults to 24
arm = next(o for o in scene.objects if o.type == "ARMATURE")
if arm.animation_data is None:
    arm.animation_data_create()
print("[anim] target armature", arm.name, "bones", len(arm.data.bones))


def assign(armature, action):
    """Blender 4.4+ actions are slotted: assigning the action alone leaves the rig static."""
    armature.animation_data.action = action
    if hasattr(action, "slots") and len(action.slots):
        armature.animation_data.action_slot = action.slots[0]

existing = {o.name for o in bpy.data.objects}
for path, name in CLIPS:
    if not os.path.exists(path):
        print("[anim] missing", path)
        continue
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = arm
    bpy.ops.import_scene.fbx(filepath=path)

    new_objs = [o for o in bpy.data.objects if o.name not in existing]
    src = next((o for o in new_objs if o.type == "ARMATURE"), None)
    act = src.animation_data.action if src and src.animation_data else None
    if act is None:
        print("[anim] no action in", path)
        continue

    act.name = name
    act.use_fake_user = True
    f0, f1 = act.frame_range
    print("[anim] %s frames %d-%d (%.2fs) fcurves %d" %
          (name, f0, f1, (f1 - f0) / scene.render.fps, len(act.fcurves)))

    track = arm.animation_data.nla_tracks.new()
    track.name = name
    strip = track.strips.new(name, int(f0), act)
    strip.name = name
    track.mute = True

    # drop everything the clip brought with it (its own rig and duplicate mesh)
    for o in new_objs:
        bpy.data.objects.remove(o, do_unlink=True)
    existing = {o.name for o in bpy.data.objects}

    # preview frames
    assign(arm, act)
    scene.render.resolution_x = 420
    scene.render.resolution_y = 540
    scene.cycles.samples = 24
    scene.frame_start = int(f0)
    scene.frame_end = int(f1)
    n = 6
    for i in range(n):
        f = int(f0 + (f1 - f0) * i / (n - 1))
        scene.frame_set(f)
        scene.render.filepath = os.path.join(OUT_DIR, "%s_%02d.png" % (name.lower(), i))
        bpy.ops.render.render(write_still=True)
    print("[anim] rendered", n, "frames for", name)

# leave the walk cycle active, loop-friendly
walk = bpy.data.actions.get("Walk")
if walk:
    assign(arm, walk)
    scene.frame_start, scene.frame_end = 1, 40

scene.render.resolution_x = 720
scene.render.resolution_y = 900
scene.cycles.samples = 48
scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)
print("[anim] saved", OUT_BLEND)

bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.gltf(
    filepath=r"C:\Users\saeed\Downloads\walker_animated.glb",
    export_format="GLB",
    use_selection=False,
    export_animations=True,
    export_animation_mode="ACTIONS",
    export_frame_range=False,
    export_nla_strips=True,
)
print("[anim] glb exported with", [a.name for a in bpy.data.actions])
