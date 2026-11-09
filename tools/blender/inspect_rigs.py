"""Dump skeleton + action info for the walker blend and the three Mixamo clips."""
import bpy
import os
import sys

TARGETS = [
    r"C:\Users\saeed\Downloads\walker_tripo.blend",
    r"C:\Users\saeed\Downloads\Walking.fbx",
    r"C:\Users\saeed\Downloads\Zombie Attack.fbx",
    r"C:\Users\saeed\Downloads\Zombie Dying.fbx",
]

for path in TARGETS:
    print("\n=====", os.path.basename(path))
    if not os.path.exists(path):
        print("  MISSING")
        continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if path.lower().endswith(".blend"):
        bpy.ops.wm.open_mainfile(filepath=path)
    else:
        bpy.ops.object.add(type="EMPTY")
        bpy.context.view_layer.objects.active = bpy.context.selected_objects[0] if bpy.context.selected_objects else bpy.data.objects[0]
        try:
            bpy.ops.import_scene.fbx(filepath=path)
        except Exception as exc:
            print("  import failed:", exc)
            continue

    objs = list(bpy.context.scene.objects)
    print("  objects:", [(o.name, o.type) for o in objs])
    arms = [o for o in objs if o.type == "ARMATURE"]
    for a in arms:
        bones = a.data.bones
        print("  armature %s bones=%d" % (a.name, len(bones)))
        print("   first:", [b.name for b in bones][:14])
        print("   last :", [b.name for b in bones][-6:])
        act = a.animation_data.action if a.animation_data else None
        if act:
            fr = act.frame_range
            print("   action:", act.name, "frames", round(fr[0]), "-", round(fr[1]),
                  "fcurves", len(act.fcurves))
        else:
            print("   no action assigned")
        mesh_kids = [c.name for c in a.children if c.type == "MESH"]
        print("   skinned meshes:", mesh_kids)
    print("  actions in file:", [(a.name, tuple(round(v) for v in a.frame_range)) for a in bpy.data.actions])
