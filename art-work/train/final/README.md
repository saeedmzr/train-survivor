# Locomotive — finalized model

Steam locomotive that replaces the primitive box-built loco. This folder is the tracking copy:
the pinned source of truth for what ships.

| file | what it is |
|---|---|
| `locomotive.fbx` | Tripo export, aligned and scaled, 5,261 verts / 5,430 faces |
| `locomotive_albedo.jpg` | 4096 px base colour, sRGB |
| `locomotive.blend` | Blender working file after alignment |

## What was done to the raw export

The Tripo FBX arrives **diagonally in its own XY plane** (~-135°) and with the importer's axis
rotation on the object. Alignment baked in Blender:

1. apply the importer's transform (otherwise overwriting `rotation_euler` tips it on its side —
   the height axis ends up 0.94 instead of 0.60)
2. PCA on the XY vertices → rotate the long axis onto X
3. rotate +90° so the nose (cowcatcher) points −Y = Blender forward
4. centre on X/Y, floor at Z = 0
5. scale by height to **4.2 m** → final **8.24 × 2.92 × 4.20 m**, which is believable for a chunky
   toy-proportioned steamer

## Imported into Unity as

- `unity/Assets/Resources/Models/locomotive.fbx` + `locomotive_albedo.jpg`
- Material built at runtime (URP/Lit, albedo on `_BaseMap`) — the FBX material reference dies
  without the `.fbm` folder
- `TrainRig.BuildModelledLocomotive()` instantiates it, fits it to `LocoLength = 8.2 m`, drops it
  onto the rail and keeps the primitive loco as a fallback if the resource is missing
- `dev.locoYaw` in `tune.txt` corrects the model's forward axis without a rebuild; the baked
  default is **0**, because the export points its cab at −Z — the side the chase camera looks at.
  With the old default of 180 the player saw the smoke-box door and coupler instead of the cab.

## Notes

- Wheels are part of the mesh, so they do not spin — the primitive loco had static wheels too.
- Guns moved to the loco roof (`y = 4.15`) because the modelled loco is ~4 m tall and they were
  previously placed at 2.0 m, i.e. inside the boiler.
- The source zip's texture lives in `steam_locomotive_3d_model.fbm/`; re-downloading the model
  gives a new UUID, so keep this folder as the pinned version.

## Wagon and turret

| file | what it is |
|---|---|
| `wagon.fbx` + `wagon_albedo.jpg` | steampunk armoured wagon, scaled to **6.6 m** long, 3.1 m tall |
| `turret.fbx` + `turret_albedo.jpg` | deck turret, scaled to **1.8 m** tall / 3.25 m long, barrel on −Y |

Both went through the same alignment as the loco (`tools`-side script `align_asset.py` in the
img3d workspace): bake the importer transform, PCA the XY vertices onto the long axis, rotate the
long axis onto Y, centre, floor, scale.

In Unity, `TrainRig.PlaceModel()` does the same job at runtime — it measures the bounds, stands the
asset up, puts its long axis down the track, scales it to a target length or height, and sits it on
the floor of its parent. `BuildFreightWagon` and `BuildGun` use it and fall back to the old
primitives if the resource is missing.

Dev flags for inspecting them without grinding stages:
`dev.wagons=2` (builds the convoy), `dev.guns=2` (bolts turrets on), `dev.locoPitch` (flips the
loco if an export lands upside down), `dev.locoYaw` (facing).

## Gun mounts

The plates are the raised discs on the roofs. Their centres are stored as **fractions of each
model's world bounds** (`TrainRig.LocoMounts`, `TrainRig.WagonMounts`), not as local coordinates —
the loco's pivot is 1.4 m above its base, and a wagon is not positioned when its guns are built, so
local-space numbers land in the wrong place. Measured from top-down renders with markers:

| model | plates (x, up, forward fraction) |
|---|---|
| locomotive | (0.50, 0.933, 0.198), (0.50, 0.807, 0.493) |
| wagon | (0.50, 0.939, 0.359), (0.50, 0.923, 0.703) |

`AimGuns()` runs every frame: each turret slerps its yaw toward the nearest living enemy, so the
barrels sweep as the horde moves. Shots are simulated by the sim, not the barrel, so the visuals
lag the hits by nothing that matters.

## Audio

`fx_shot` is synthesised by `tools/make_enemy_sfx.py` (differenced noise crack + short thump +
metal ring) and played by `AudioDirector` on every `SimEventKind.Shot` at low volume with pitch
jitter — the guns fire constantly, so a fixed sample would drone. Verify on the `-no-audio`
emulator with `dev.audiolog=1`, which logs `play fx_shot vol=… pitch=…`.
