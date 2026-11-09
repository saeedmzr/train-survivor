# Walker — finalized animated model

Final hand-off asset for the walker enemy. This folder is the tracking copy: the source of truth
for what ships, independent of any Blender/Tripo working files.

| file | what it is |
|---|---|
| `walker.fbx` | Tripo export, 5,185 verts / 5,980 tris, Mixamo-named rig (65 bones), 3 animation takes |
| `walker_albedo.jpg` | 2048 px albedo, sRGB |

## Clips (all 240 frames, source take name in brackets)

| clip | take |
|---|---|
| Walk | "A slow zombie shamble in place, facing forward…" |
| Attack | "A standing zombie performs one vicious melee attack…" |
| Death | "A standing zombie is struck by a fatal hit from the front…" |

All three are **in place** (no forward root travel) — movement is driven by the game, not the clip.

## Imported into Unity as

- `unity/Assets/Resources/Models/walker3d.fbx` + `walker3d_albedo.jpg`
- Legacy animation clips renamed `Walk` / `Attack` / `Death` by `Assets/Editor/WalkerModelSetup.cs`
- Material: `Assets/Resources/Models/WalkerMat.mat` (URP/Lit, albedo bound to `_BaseMap`)
- Runtime view: `TrainSurvivor.Game.View.WalkerModelView`, spawned by `dev.models` tune flag

## Stats that matter to the game

- world height: scaled at runtime to **2.0 m** from the renderer bounds
- roster entry: `walker`, hp 30, speed 2.2, dmg 6 (see `finalized-for-develop/data/enemies.json`)
- origin at the feet, facing +Z after the runtime facing fix-up

## Provenance

Generated in Tripo from the painted walker art, animations authored in Mixamo on the Tripo rig,
exported 2026-11-08. Re-downloading from Tripo gives a new UUID and a new texture; keep this
folder as the pinned version.
