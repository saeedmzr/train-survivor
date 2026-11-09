"""Inspect mesh islands and their weight distribution to find mis-skinned parts."""
import bpy
import bmesh
from mathutils import Vector

bpy.ops.wm.open_mainfile(filepath=r"C:\Users\saeed\Downloads\walker_animated.blend")
mesh = next(o for o in bpy.context.scene.objects if o.type == "MESH")
arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
me = mesh.data
print("[w] verts", len(me.vertices), "polys", len(me.polygons), "vgroups", len(mesh.vertex_groups))

# loose parts
bm = bmesh.new()
bm.from_mesh(me)
bm.verts.ensure_lookup_table()
seen = set()
islands = []
for v in bm.verts:
    if v.index in seen:
        continue
    stack = [v]
    comp = []
    seen.add(v.index)
    while stack:
        cur = stack.pop()
        comp.append(cur.index)
        for e in cur.link_edges:
            o = e.other_vert(cur)
            if o.index not in seen:
                seen.add(o.index)
                stack.append(o)
    islands.append(comp)
bm.free()

print("[w] islands", len(islands))
gnames = {g.index: g.name for g in mesh.vertex_groups}
bone_names = {b.name for b in arm.data.bones}

islands.sort(key=len, reverse=True)
for comp in islands[:14]:
    cos = [me.vertices[i].co for i in comp]
    lo = Vector((min(c.x for c in cos), min(c.y for c in cos), min(c.z for c in cos)))
    hi = Vector((max(c.x for c in cos), max(c.y for c in cos), max(c.z for c in cos)))
    tally = {}
    for i in comp:
        for g in me.vertices[i].groups:
            if g.weight > 0.05:
                n = gnames.get(g.group, "?")
                tally[n] = tally.get(n, 0.0) + g.weight
    top = sorted(tally.items(), key=lambda kv: -kv[1])[:4]
    print("[w] island %5d verts  z %.2f..%.2f  x %.2f..%.2f  bones %s" %
          (len(comp), lo.z, hi.z, lo.x, hi.x,
           [(n.replace("mixamorig:", ""), round(w, 1)) for n, w in top]))

# how many verts have no weight at all
unweighted = sum(1 for v in me.vertices if not [g for g in v.groups if g.weight > 0.01])
print("[w] unweighted verts", unweighted)
maxinf = max(len([g for g in v.groups if g.weight > 0.01]) for v in me.vertices)
print("[w] max influences per vertex", maxinf)
