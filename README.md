# Train Survivor

Portrait, one-thumb, "Archero on rails": hold to accelerate and ram, release to brake and
open fire on the nearest zombie. Clear a stage, pick one of three cards, keep rolling.

Unity 6 LTS (6000.0.85f2) · URP · IL2CPP/ARM64 · Android.

## Run it

```bash
# emulator (x86_64 build — the emulator cannot execute Unity's ARM64 BTI code)
tools/run-emulator.sh
tools/deploy.sh --emulator

# phone / shipping build
tools/deploy.sh
```

`tools/deploy.sh` builds, installs and launches on the attached device. Logs land in
`unity/Logs/build.log`; live device log: `adb logcat -s Unity:V`.

Two emulator traps: it runs with `-no-audio`, so audio can only be proven by log
(`dev.audiolog=1`), and it must be started as a long-lived background job — if the job that
launched it is reaped, qemu dies with it (exit 137) and `adb` reports no devices.

## Tests

```bash
~/Unity/Hub/Editor/6000.0.85f2/Editor/Unity -batchmode -projectPath "$PWD/unity" \
  -runTests -testPlatform EditMode -testResults "$PWD/unity/Logs/editmode.xml" \
  -logFile "$PWD/unity/Logs/tests.log"
```

25 EditMode tests cover the sim: movement, brake charge, ram, targeting, blockades, XP
pickups, card effects, stage clear, the 12-species roster and spawn mix, and seeded
determinism. The sim is UnityEngine-free (`TrainSurvivor.Core`) so this runs in about a
second.

## Enemies in game

The 12 painted species are wired end to end. One sprite per species means the motion is
code, not frames — `Game/View/EnemyViewPool.cs` gives each species a gait (bob rate, roll,
sway, lean, hover) that speeds up as it closes, squashes on contact and hovers for the
Glider. Stats and the stage-gated spawn mix live in `SimConfig.Stat` / `SimConfig.PickKind`;
stats are data, behaviours are flags (`IgnoresRam`, `BlastRadius`, `Hover`).

Sound is synthesised, no samples: `tools/make_enemy_sfx.py` writes 12 species voices plus
`fx_*` to `Art/Resources/Audio/` (16-bit mono 44.1 kHz), `Game/Audio/AudioDirector.cs`
plays them on sim events and keeps a low growl bed. Kill voices ride on the event's
`EnemyKind` because the enemy is already removed from the world when views drain the queue.

### Animation and shot FX

Enemies are painted cut-outs, so the movement is generated, not authored:

```
art-work/enemies/<id>/sprite.png     the untouched cutout
art-work/enemies/<id>/walk_0..5.png   six-pose cycle (tools/make_enemy_walk.py)
```

`tools/make_enemy_walk.py` warps the silhouette into a six-pose cycle — body lean with a
phase twist up the sprite, the bottom 40 % swinging the other way as legs, squash on the
plant with the feet anchored, bob on the same curve. Frames share one padded canvas so the
sprite never changes size when the cycle swaps. `tools/pack_enemy_atlas.py` then packs
`en_<id>` (frame 0) plus `en_<id>_f1..f5` into `atlas_enemies.png` (2048², 72 sprites).

Shots are travelling sprites, not a drawn line: `Game/View/Effects.cs` spawns
`bullet_core`, flies it over 45–130 ms, drops a fading glow behind it, bursts
`impact_core` where it lands, and holds the killed enemy's pose white for the flight time
before the gore — so a hit reads as cause and effect. Bullets, muzzle glow and impacts use
the additive `TrainSurvivor/Sprite3DAdd` shader.

```
tools/pack_vfx_atlas.py     projectiles + fx_* + generated bullet/impact/shadow → atlas_vfx.png
```

The kit's `proj_*` art is a faint placeholder (alpha ≈ 22/255), so the bullet, impact and
muzzle sprites the game draws are generated in that tool rather than sampled from it.

### On-device knobs (`tune.txt`)

Push to `files/tune.txt` and relaunch; unknown keys are ignored.

| key | effect |
|---|---|
| `dev.hp=999` | lead wagon max/current HP |
| `dev.noguns=1` | guns off — walk the horde up to the train |
| `dev.lineup=1`, `dev.lineupat=8` | spawn one of every species in a row ahead |
| `dev.pause=1` | freeze the sim (views keep drawing) to photograph a state |
| `dev.audiolog=1` | log every triggered clip — the emulator runs `-no-audio` |
| `dev.wagons=5` | build the full convoy (checks the camera push-back) |
| `dev.timeScale=0.15` | slow motion, sim and view alike |
| `dev.bulletTime=20` | stretch bullet flight for screenshots |
| `cam.distance`, `cam.height`, `cam.fov`, `cam.lateral`, `cam.wagonPush` | live camera framing |

The chase camera sits 15.5 m back / 10.4 m up and pushes back a further 1.4 m per wagon past
the engine, easing toward the new distance rather than snapping — a five-wagon convoy frames
itself without a separate camera preset.

## Painted enemy art

`art-work/enemies/<id>/card.png` (765 × 1024) is the source; `tools/extract_enemy_art.py`
cuts the creature out of the card, `tools/pack_enemy_atlas.py` packs
`unity/.../Art/Resources/Art/atlas_enemies.png` (1024², `en_<id>`) and writes the sprite
table, `tools/write_enemy_meta.py` merges stats + art notes into each `meta.json`.
`Prefabs/Enemies/<id>.prefab` are built from the atlas by
`TrainSurvivor.EditorTools.EnemyPrefabBuilder` (menu *Train Survivor / Art / Rebuild Enemy
Prefabs*, or `-executeMethod ...BuildFromBatch`).

## Art

The art sheets in `images/` are sliced, alpha-keyed and packed into two atlases by
`tools/slice_art.py`; the game loads them at runtime through `ArtSprites`. See
[docs/ART.md](docs/ART.md) for the pipeline and the sprite names.

## Documents

| File | What it is |
|---|---|
| `Game Design Document_ Train Survivor.docx` | **GDD v2.0** — canonical design (loop, stages, progression, 13 enemies, 10 bosses, ammo, HUD, biomes). Generated. |
| `docs/SYSTEM_DESIGN.md` | Engineering doc (v1.2): architecture, budget, numbers, milestones. Defers to the GDD on rules. |
| `docs/ART.md` | Slicer / atlas pipeline. |
| `docs/archive/GDD-v1.0.docx` | Superseded original brief, kept for provenance. |

The GDD is generated from the project data so it cannot drift:

```bash
python3 tools/gen_gdd.py                     # -> docs/gdd-v2.spec.json
python-docx-python tools/format_gdd.py "Game Design Document_ Train Survivor.docx"
```

`tools/gen_gdd.py` is stdlib-only and reads `finalized-for-develop/data/*.json`. The build
step needs python-docx; on this machine that is
`/home/saeed/.hermes/tools/python-3.14.7+*/bin/python3`, and `tools/format_gdd.py` applies
column widths and heading keep-with-next (python-docx cannot set those from the create spec).

## Layout

```
unity/Assets/_Project/
  Art/Resources/Art/     generated atlases + Sprite3D shader
  Scenes/Run.unity       the only scene; everything is built from code
  Scripts/Core/          UnityEngine-free sim (World, SimConfig, TrackPath)
  Scripts/Game/          loop, input, bootstrap, DevTuning
  Scripts/Game/View/     train, enemies, blockades, pickups, scenery, fx, billboards
  Scripts/Game/UI/       HUD, upgrade cards, death screen
  Scripts/Editor/        scene/material builders, build targets, texture import rules
docs/                    design doc + art pipeline
design/figma/            Figma field guide export (boards, 64 state renders, spec JSON, tokens)
finalized-for-develop/   dev asset kit: UI slices, 60 gameplay frames, 10 enemies + 10 bosses,
                         ammo/FX, game data (data/*.json), BESTIARY.md, catalog
art-work/enemies/<id>/   painted enemy cards + their cutouts + meta.json (source art)
tools/                   slicer, deploy, emulator
images/                  source art sheets + sliced sprites
```

## Dev tuning

Drop a `tune.txt` into the app's files dir to override camera/pacing and jump to states
without rebuilding:

```ini
cam.distance=16.5
cam.height=11
cam.fov=50
dev.levelup=1     # open the card screen immediately
dev.blockade=25   # spawn a blockade 25 m ahead
dev.hp=14         # low HP so a bite kills you
```

```bash
adb push tune.txt /sdcard/Android/data/com.trainsurvivor.game/files/tune.txt
adb shell am force-stop com.trainsurvivor.game
adb shell am start -n com.trainsurvivor.game/com.unity3d.player.UnityPlayerActivity
```
