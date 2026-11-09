import io

p = r"C:/Users/saeed/Apps/img3d/apply_final_clips.py"
s = io.open(p, encoding="utf-8").read()
s = s.replace('BLEND = r"C:\\Users\\saeed\\Downloads\\walker_animated.blend"',
              'BLEND = r"C:\\Users\\saeed\\Downloads\\walker_tripo.blend"')
marker = "# ---------------------------------------------------------------- floor clamp"
i = s.index(marker)
tail = (
    'assign(bpy.data.actions.get("Walk"))\n'
    'scene.frame_start = 1\n'
    'scene.frame_end = int(bpy.data.actions["Walk"].frame_range[1])\n'
    'OUT_BLEND = r"C:\\Users\\saeed\\Downloads\\walker_animated.blend"\n'
    'bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)\n'
    'print("[ap] saved", OUT_BLEND, [a.name for a in bpy.data.actions])\n'
)
s = s[:i] + tail
io.open(p, "w", encoding="utf-8").write(s)
print("rewrote: attach + trim + drift only, source walker_tripo.blend")
print([l for l in s.splitlines() if l.startswith("BLEND") or "OUT_BLEND" in l][:3])
