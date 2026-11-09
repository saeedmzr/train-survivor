import bpy, sys
p = sys.argv[-1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=p)
for o in bpy.data.objects:
    if o.type == "MESH":
        me = o.data
        print("[uv]", o.name, "verts", len(me.vertices), "uv_layers", len(me.uv_layers),
              [l.name for l in me.uv_layers], "mats", [m.name for m in me.materials if m])
