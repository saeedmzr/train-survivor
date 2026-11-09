"""Rebuild the walker's skin weights.

The Mixamo auto-rig produced unusable weights on this mesh (the whole body island is dominated
by the Head bone, so the jacket and arms get dragged with the head). Weights are recomputed from
bone-segment distance with an inverse-power falloff, limited to 4 influences, and small detached
islands (teeth, eyes, blood blobs) are bound rigidly to the nearest bone so they cannot shear.
"""
import bpy
import numpy as np
from mathutils import Vector

SRC = r"C:\Users\saeed\Downloads\walker_animated.blend"
OUT = r"C:\Users\saeed\Downloads\walker_animated.blend"

bpy.ops.wm.open_mainfile(filepath=SRC)
mesh = next(o for o in bpy.context.scene.objects if o.type == "MESH")
arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
me = mesh.data

# ---------------------------------------------------------------- bone segments (armature space)
segs = []
for b in arm.data.bones:
    if not b.use_deform:
        continue
    head = arm.matrix_world @ b.head_local
    tail = arm.matrix_world @ b.tail_local
    if (tail - head).length < 1e-4:
        continue
    segs.append((b.name, np.array(head), np.array(tail)))
names = [s[0] for s in segs]
print("[fix] deform bones", len(names))

V = np.array([np.array(mesh.matrix_world @ v.co) for v in me.vertices])


def segment_distance(p, a, b):
    ab = b - a
    denom = ab.dot(ab)
    t = 0.0 if denom < 1e-9 else np.clip((p - a).dot(ab) / denom, 0.0, 1.0)
    return np.linalg.norm(p - (a + ab * t))


# ---------------------------------------------------------------- islands
import bmesh
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

# distance matrix (verts x bones) - 5.2k x 50 is small
D = np.zeros((len(V), len(segs)), dtype=np.float32)
for j, (_, a, b) in enumerate(segs):
    ab = b - a
    denom = float(ab.dot(ab))
    if denom < 1e-9:
        D[:, j] = np.linalg.norm(V - a, axis=1)
        continue
    t = np.clip(((V - a) @ ab) / denom, 0.0, 1.0)
    proj = a + t[:, None] * ab
    D[:, j] = np.linalg.norm(V - proj, axis=1)

POWER = 4.0
weights = np.zeros_like(D)
for j in range(len(segs)):
    weights[:, j] = 1.0 / np.power(D[:, j] + 0.012, POWER)

# keep the 4 strongest, drop anything below 8 % of the strongest, renormalise
order = np.argsort(-weights, axis=1)
keep = np.zeros_like(weights)
for i in range(len(V)):
    idx = order[i, :4]
    top = weights[i, idx[0]]
    sel = [j for j in idx if weights[i, j] >= top * 0.08]
    keep[i, sel] = weights[i, sel]
keep /= np.clip(keep.sum(axis=1, keepdims=True), 1e-9, None)

# small detached islands: rigid single-bone binding
rigid = 0
for comp in islands:
    if len(comp) >= 60:
        continue
    centre = V[comp].mean(axis=0)
    dists = [segment_distance(centre, a, b) for _, a, b in segs]
    j = int(np.argmin(dists))
    keep[comp, :] = 0.0
    keep[comp, j] = 1.0
    rigid += 1
print("[fix] rigid islands", rigid)

# ---------------------------------------------------------------- hands: no finger bones
# the hands are a few polys each, so Mixamo's finger bones shred them into strands; bind every
# finger/thumb weight to the wrist bone instead, which keeps the hand solid
import re
finger_re = re.compile(r"^(.*Hand)(Index|Middle|Ring|Pinky|Thumb)\d$")
name_to_idx = {n: i for i, n in enumerate(names)}
merged = 0
for j, n in enumerate(names):
    m = finger_re.match(n)
    if not m:
        continue
    hand_name = m.group(1)
    if hand_name not in name_to_idx:
        continue
    h = name_to_idx[hand_name]
    keep[:, h] += keep[:, j]
    keep[:, j] = 0.0
    merged += 1
if merged:
    order = np.argsort(-keep, axis=1)
    trimmed = np.zeros_like(keep)
    for i in range(len(V)):
        idx = order[i, :4]
        trimmed[i, idx] = keep[i, idx]
    keep = trimmed / np.clip(trimmed.sum(axis=1, keepdims=True), 1e-9, None)
print("[fix] finger bones merged into wrists:", merged)

# ---------------------------------------------------------------- adjacency filter
# a vertex whose dominant bone is Chest must not also be pulled by a ForeArm: only the
# dominant bone plus its parent and children may keep weight, everything else is dropped
parent_of = {}
children_of = {}
for b in arm.data.bones:
    parent_of[b.name] = b.parent.name if b.parent else None
    children_of.setdefault(b.name, [])
    if b.parent:
        children_of.setdefault(b.parent.name, []).append(b.name)

fixed = 0
for i in range(len(V)):
    row = keep[i]
    dom = int(np.argmax(row))
    dom_name = names[dom]
    allowed = {dom}
    p = parent_of.get(dom_name)
    if p in name_to_idx:
        allowed.add(name_to_idx[p])
    for ch in children_of.get(dom_name, []):
        if ch in name_to_idx:
            allowed.add(name_to_idx[ch])
    mask = np.zeros(len(names), dtype=bool)
    mask[list(allowed)] = True
    pruned = np.where(mask, row, 0.0)
    if pruned.sum() < 1e-6:
        continue
    if not np.allclose(pruned, row):
        fixed += 1
    keep[i] = pruned / pruned.sum()
print("[fix] vertices pruned to adjacent bones:", fixed)

# ---------------------------------------------------------------- write vertex groups
for g in list(mesh.vertex_groups):
    mesh.vertex_groups.remove(g)
groups = {n: mesh.vertex_groups.new(name=n) for n in names}
for i, v in enumerate(me.vertices):
    row = keep[i]
    nz = np.nonzero(row > 0.001)[0]
    for j in nz:
        groups[names[j]].add([i], float(row[j]), "REPLACE")

maxinf = int((keep > 0.001).sum(axis=1).max())
print("[fix] wrote weights, max influences", maxinf, "mean top1 %.2f" % keep.max(axis=1).mean())

# make sure the modifier still points at the armature
mods = [m for m in mesh.modifiers if m.type == "ARMATURE"]
if not mods:
    m = mesh.modifiers.new("Armature", "ARMATURE")
    m.object = arm
else:
    mods[0].object = arm
mods = [m for m in mesh.modifiers if m.type == "ARMATURE"]
mods[0].use_deform_preserve_volume = True   # dual-quaternion: stops limbs collapsing

cs = next((m for m in mesh.modifiers if m.type == "CORRECTIVE_SMOOTH"), None)
if cs is None:
    cs = mesh.modifiers.new("CorrectiveSmooth", "CORRECTIVE_SMOOTH")
cs.factor = 0.6
cs.iterations = 12
cs.smooth_type = "LENGTH_WEIGHTED"
cs.rest_source = "ORCO"
print("[fix] armature preserve-volume on, corrective smooth %.2f x%d" % (cs.factor, cs.iterations))

bpy.ops.wm.save_as_mainfile(filepath=OUT)
print("[fix] saved", OUT)
