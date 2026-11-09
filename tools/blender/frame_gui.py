"""GUI-side: after startup, switch the 3D view to Material Preview, frame the walker, and
render one preview still so the result can be checked without touchng the UI.
"""
import bpy
import math
from mathutils import Euler

PREVIEW = r"C:\Users\saeed\Downloads\walker_tripo_preview.png"


def setup():
    body = next((o for o in bpy.context.scene.objects if o.type == "MESH"), None)
    if body is None:
        return None

    for area in bpy.context.window.screen.areas:
        if area.type != "VIEW_3D":
            continue
        for space in area.spaces:
            if space.type != "VIEW_3D":
                continue
            space.shading.type = "MATERIAL"
            space.shading.use_scene_lights = True
            space.shading.use_scene_world = True
            space.overlay.show_floor = True
            r3d = space.region_3d
            r3d.view_perspective = "PERSP"
            r3d.view_location = (0.0, 0.0, 1.05)
            r3d.view_distance = 3.6
            r3d.view_rotation = Euler((math.radians(80), 0.0, math.radians(25)), "XYZ").to_quaternion()

    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    bpy.context.view_layer.objects.active = body

    scene = bpy.context.scene
    scene.render.filepath = PREVIEW
    scene.render.image_settings.file_format = "PNG"
    scene.cycles.samples = 40
    bpy.ops.render.render(write_still=True)
    print("[gui] preview ->", PREVIEW)
    print("[gui] object", body.name, "verts", len(body.data.vertices),
          "materials", [m.name for m in body.data.materials])
    return None


bpy.app.timers.register(setup, first_interval=2.0)
