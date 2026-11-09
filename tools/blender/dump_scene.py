"""Dump the live Blender session's animation state to JSON (run from the Python console)."""
import bpy
import json

data = {
    "blend": bpy.data.filepath,
    "objects": [(o.name, o.type) for o in bpy.data.objects],
    "actions": [
        {
            "name": a.name,
            "frame_start": round(float(a.frame_range[0]), 2),
            "frame_end": round(float(a.frame_range[1]), 2),
            "fcurves": len(a.fcurves),
            "users": a.users,
            "slots": [s.identifier for s in a.slots] if hasattr(a, "slots") else [],
        }
        for a in bpy.data.actions
    ],
}

for o in bpy.data.objects:
    if o.type != "ARMATURE":
        continue
    entry = {
        "active_action": None,
        "nla_tracks": [],
        "bones": len(o.data.bones),
        "bone_names": [b.name for b in o.data.bones][:4],
        "children": [(c.name, c.type) for c in o.children],
    }
    if o.animation_data:
        if o.animation_data.action:
            entry["active_action"] = o.animation_data.action.name
        for t in o.animation_data.nla_tracks:
            entry["nla_tracks"].append(
                {"name": t.name, "mute": t.mute,
                 "strips": [{"name": s.name, "action": s.action.name if s.action else None,
                             "start": round(s.frame_start, 1), "end": round(s.frame_end, 1)}
                            for s in t.strips]}
            )
    data.setdefault("armatures", []).append((o.name, entry))

# which actions actually drive this armature (channels referencing its bones)
arm = next((o for o in bpy.data.objects if o.type == "ARMATURE"), None)
if arm:
    driven = []
    for a in bpy.data.actions:
        paths = {fc.data_path.split('"')[1] for fc in a.fcurves if fc.data_path.startswith("pose.bones")}
        if paths and paths <= {b.name for b in arm.data.bones}:
            driven.append({"action": a.name, "bones_animated": len(paths)})
    data["actions_matching_armature"] = driven

out = r"C:\Users\saeed\AppData\Local\hermes\cache\scratch\blender_state.json"
with open(out, "w", encoding="utf-8") as fh:
    json.dump(data, fh, indent=1)
print("[DUMP] wrote", out)
print("[DUMP] actions:", [a.name for a in bpy.data.actions])
