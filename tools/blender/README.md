# Blender pipeline

Headless Blender scripts used to turn Tripo exports and Mixamo clips into the FBX files under
`unity/Assets/Resources/Models`. Nothing here is modelling work — it is import, orientation, scale,
rig repair and export.

Blender **4.5.9 LTS**. Run headless:

```bash
blender -b -P tools/blender/<script>.py -- <args>
```

`align_asset.py` derives its output paths from its own location, so it works from any clone and on
Linux. The older animation scripts still carry Windows paths in constants near the top (see the
`OUT = …` lines) — edit those when running them on another machine.

## Import and normalise (used for every model)

| Script | What it does |
|---|---|
| `align_asset.py` | The workhorse. Imports a Tripo FBX, bakes the importer transform, puts the long axis on Y, centres, drops it on the floor, scales to a target size, exports to `Unity/.../Models` and to the tracking folder. |
| `convert_fbx.py` | Tripo FBX → `.blend` + `.glb` + preview. Needed because the Tripo bridge's FBX importer crashes inside the GUI. |
| `open_tripo.py` | Inspect a Tripo download and open it headless. |
| `checkuv.py` | Print mesh/UV/material summary — catches assets with a missing UV layer (they render as one flat texel). |
| `dump_scene.py`, `frame_gui.py` | Scene dump and viewport framing helpers. |

Usage:

```bash
blender -b -P tools/blender/align_asset.py -- \
  "path/to/asset.fbx" crate 1.2 height environment/final
```

`target` is metres; `height` or `length` picks the axis to fit; the optional last argument is the
subfolder under `art-work/` that receives the tracked copy (default `train/final`).

## Locomotive and train

`inspect_loco.py` (stats) → `render_loco.py` (4-view check) → `align_loco.py` (PCA long axis,
baked import rotation) → `finalize_loco.py` (nose to −Y, scale, export) →
`find_mounts.py` / `mark_mounts.py` (top-down render with red markers that locate the turret
mounting plates).

## Walker rig and animation

| Script | What it does |
|---|---|
| `retarget_mixamo.py`, `attach_clips.py`, `apply_final_clips.py`, `finalize_clips.py` | Bring Mixamo FBMs onto the Tripo skeleton and produce one file with Walk/Attack/Death. |
| `fix_weights.py` | Prune weight bleed (dominant bone + parent + children, max 3 influences), merge finger bones. |
| `fix_quaternions.py` | Clean rotation curves after retargeting. |
| `floorcheck.py`, `ground_clamp.py` | Measure and remove floor penetration per frame (death clip sank 0.407 m). |
| `clip_windows.py` | Find the useful motion window inside a long take. |
| `render_clips.py`, `walk_stride.py`, `loop_probe.py` | Render clip sequences, measure foot contacts and stride length. |
| `export_nla.py`, `finalize_walk.py` | NLA export and final walk cleanup. |
| `inspect_*.py`, `render_final.py`, `frame_anim.py` | Inspection and render helpers. |

## Superseded experiments (kept for reference, not part of the pipeline)

- `hull4.py`, `make_hull.py`, `analyze_refs.py`, `rig_hull.py` — four-view visual hull. Abandoned:
  the result did not look like the reference art.
- `make_walker.py`, `render_walker_anim.py` — procedural box character. Abandoned for the same reason.
- `blender_turntable.py`, `prep_input.py` — turntable renders of TripoSR output.
- `rewrite_apply*.py`, `rewrite_attach.py`, `*_strip.py`, `walk_picks.py`, `fixcheck.py`,
  `diag_anim.py`, `preview_anims.py` — one-off console patches from single debugging sessions.
- `img23d.bat` — Windows helper that wires TripoSR's image-to-3D run; not used for the final assets.
