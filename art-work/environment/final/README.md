# Environment — abandoned industrial railway district

Seven Tripo models, normalised in Blender and imported into Unity. Original ZIPs stay in
`C:\Users\saeed\Downloads`; nothing here is overwritten by the pipeline.

| File base | Source model | Normalised size | Used as |
|---|---|---|---|
| `warehouse` | industrial warehouse | 14 m long, ~7.6 m tall | background building, 19–27 m out |
| `brickbuilding` | ruined brick building | 11 m long, ~9.9 m tall | background building, 19–27 m out |
| `rockwall` | cracked rock wall | 6 m long | boundary walls at 7.7–10.3 m |
| `rubble` | rock rubble | 3.2 m wide | scattered debris at 6.7–9.1 m |
| `crate` | reinforced wooden crate | 1.2 m | prop clusters at 6.8–8.8 m |
| `barrel` | rusted steel barrel | 0.95 m | prop clusters at 6.8–8.8 m |
| `lamp` | industrial lamp post | 5.5 m | lighting at 6.7–7.9 m, amber emissive |

Each `<name>.fbx` carries its `<name>.fbm/` texture folder (Blender export with copy-path).
Albedos are also copied to `<name>_albedo.jpg` for `Resources.Load`.

## Unity side

- Models: `unity/Assets/Resources/Models/{warehouse,brickbuilding,rockwall,rubble,crate,barrel,lamp}.fbx`
- Albedos: same folder, `<name>_albedo.jpg`
- Placement: `Scripts/Game/View/DistrictScenery.cs`, created by `M0Bootstrap` as the `District` object
- Sprite prop billboards in `Scenery.cs` disable themselves when the district is present

### Placement rules

Slots every **7 m** along the track, **both sides**, deterministic from `seed` (default 1337):
`Hash(slot, side, seed)` picks type, lateral distance, yaw and ±15 % uniform scale. Re-running with
the same seed reproduces the district exactly.

Nothing is placed inside `combatClear` (6.5 m) of the rails, so zombies keep open approaches on
both sides and the locomotive corridor stays clear.

Pools are fixed (6 + 6 buildings, 12 walls, 14 rubble, 18 crates, 18 barrels, 12 lamps = 86 objects);
every frame the pools are re-assigned to the slots inside a window of 110 m ahead / 35 m behind the
train. Object count never grows, so the district cannot accumulate over a long stage.

### Inspector settings (`DistrictScenery`)

`slotStep`, `combatClear`, `windowAhead`, `windowBehind`, `buildingLateral`, `seed`, `scaleJitter`,
`maxLampLights`, `lampColour`.

### Known issues

- No contact shadows on props — they read as slightly detached against the dark ground.
- Buildings at 19–27 m can still crop at the top of the frame when one lands beside the camera.
- The ground is still the original flat track mesh; no gravel/concrete variation yet.
- Lamp lights are capped at 3 point lights, no shadows.
