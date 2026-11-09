"""GUI: frame the animated walker in Material Preview and start playback so the clips can be
judged live in the viewport.
"""
import bpy
import math
from mathutils import Euler


def setup():
    scene = bpy.context.scene
    arm = next((o for o in scene.objects if o.type == "ARMATURE"), None)
    if arm is None:
        return None

    walk = bpy.data.actions.get("Walk")
    if walk and arm.animation_data:
        arm.animation_data.action = walk
        if hasattr(walk, "slots") and len(walk.slots):
            arm.animation_data.action_slot = walk.slots[0]
        scene.frame_start, scene.frame_end = (int(v) for v in walk.frame_range)

    for area in bpy.context.window.screen.areas:
        if area.type != "VIEW_3D":
            continue
        for space in area.spaces:
            if space.type != "VIEW_3D":
                continue
            space.shading.type = "MATERIAL"
            space.overlay.show_floor = True
            r3d = space.region_3d
            r3d.view_perspective = "PERSP"
            r3d.view_location = (0.0, 0.0, 1.05)
            r3d.view_distance = 3.4
            r3d.view_rotation = Euler((math.radians(82), 0.0, math.radians(18)), "XYZ").to_quaternion()

    bpy.ops.screen.animation_play()
    print("[gui] playing clip; actions:", [a.name for a in bpy.data.actions])
    return None


bpy.app.timers.register(setup, first_interval=2.0)
