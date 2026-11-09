"""Re-export the GLB in NLA_TRACKS mode so each animation uses its strip range (the trimmed walk
loop) instead of the action's full length."""
import bpy

BLEND = r"C:\Users\saeed\Downloads\walker_animated.blend"
OUT_GLB = r"C:\Users\saeed\Downloads\walker_animated.glb"

bpy.ops.wm.open_mainfile(filepath=BLEND)
arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
for t in arm.animation_data.nla_tracks:
    s = t.strips[0]
    print("[ex] track %-7s strip %.0f-%.0f action %.0f-%.0f"
          % (t.name, s.frame_start, s.frame_end, s.action_frame_start, s.action_frame_end))

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
print("[ex] exported", OUT_GLB)
