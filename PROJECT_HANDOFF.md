# Train Survivor — project handoff

Read this file before changing anything in this project.

## Standing request (eight-item polish)
1. Audible audio (now working end-to-end; keep the proven capture route).
2. Cinematic gunfire: recoil, flash, readable tracer, impact-synced damage.
3. All seven scenery assets upright (warehouse, brickbuilding, rockwall, rubble, crate, barrel, lamp).
4. Gun mounts: unique plates, max 2 per wagon, no hidden/overflow firing.
5/8. Walkers face and approach the nearest convoy hull; gait matches measured displacement.
6. Visible gun animation at gameplay scale.
7. Full audio coverage of player/enemy actions.
8. Frame-time measurement and an error sweep.
Plus: normal Stage 1 clear to Stage 2 on the current build.

## Current code status (working tree, no commits)
- Scenery: every asset uses an explicit -90X import correction on the visual child under a
  neutral pooled root; size/ground/collider are computed in root-local space. The empty
  corridor was a wiring bug: nothing called `DistrictScenery.Sync`, so every pooled prop
  stayed inactive and `Scenery` suppressed its sprite flats. `LateUpdate` now calls it
  (re-placing every 0.5 m), with a footprint-radius clearance clamp and building+yard
  clusters; logs `[env] placed 47 props in view at S 0.0` and renders in the normal chase
  shot.
- Camera: `CameraFollow` fits the whole convoy envelope (tail..nose samples, both flanks,
  roof) with an aspect-aware bisection and UI-safe margins, rear/elevated at 34 deg. It
  zooms smoothly when wagons are added; the old wagon-count pushback is legacy.
- Guns: `aimToleranceDeg` is 1 deg (the 5.5 deg settling shot is gone; runtime spawns
  measure 0.2 deg). The pitch pivot is on the support joint (cut plane y=0.415): pitch is
  parented at +hinge and recoil at -hinge, so the base stays on the plate through 0/90/180
  yaw and the upper stays attached. The gun inspection camera is a fixed bounds-framed
  side/back view (`dev.gunscam`, `dev.gunscam.gun`) that disables other cameras; the walker
  camera frames face+feet and restores cameras on teardown.
- Walker: facingYaw = 0 (180 was backwards and showed the back); gait uses measured
  displacement per body with dt guard; attack freezes walk playback; facing targets the
  nearest hull point (clamped tail..nose), not the locomotive centre. Clip root offset
  measured 0. Close-up face capture confirmed with yaw 0.
- Trains: Build creates locomotive + `Train.Wagons` freight cars; MaxHp includes wagons;
  hull gap uses the same span.
- Guns: mount -> Yaw -> Pitch (clamp -25 up / +70 down) -> Recoil -> upper mesh.
  Turret mesh split in RECOIL-LOCAL space at 34% of its height: base parented to the fixed
  mount, upper under Recoil; muzzle re-anchored to (0.00, 0.61, 1.10); split meshes cached
  by source Mesh; turret import isReadable = 1. Player log confirms
  "[gun] split ok base 10738 upper 10738" with no fallback warning.
- Projectile contract (CORE + DRIVER, ON):
  `World.ViewDrivenProjectiles` is enabled at boot by `ProjectileDriver.Bind`, so timer
  damage is OFF in game. API: `ResolveImpact(projectileId, isBlockade, entityId)`,
  `CancelProjectile(id)`, `SimEvent.ProjectileId` (now set on blockade shots too),
  `World.StageRebuilt` clears pending rounds on any stage/run rebuild.
- `ProjectileDriver` (Game/View): pooled `bullet_core` rounds + warm trail, spawned at
  `TrainRig.MuzzlePosition` along `MuzzleForward`, 170 m/s straight flight, per-frame
  `Physics.SphereCastNonAlloc` (radius 0.22) against enemy/blockade `HitProxy` colliders
  and Scenery-layer colliders. Only a real hit calls ResolveImpact; scenery/range/expiry
  calls CancelProjectile. 60 m / 1.2 s limits, 32 rounds, 96 trail sprites, no per-shot
  Instantiate. Reset/stage change drops every round.
- `HitProxy`: trigger capsule on walker/sprite bodies, trigger box on blockades, rebound
  from the pooled views each frame and disabled on death/free. The legacy homing tracer in
  `Effects` is gated off by `SetTimerTracers(false)` while the driver is bound.
- `SimEventKind.EnemyHit` is the bullet-hurt event (audio `fx_metal` 0.3, impact_core VFX);
  `ContactHit` remains train contact only.
- GameLoop drains events raised in LateUpdate (`DispatchPending`) before Advance clears the
  queue, and clears after each step's dispatch so no Shot is replayed.
- Audio: an AudioListener is created if missing; diagnostics are tune-gated; engine bed
  follows throttle and master volume. Emulator output proven with a sink-monitor capture.

## Verified evidence (keep)
- Tests: EditMode 34/34 pass (latest run unity/Logs/editmode-present.xml).
- APK installed: sha256 adecefd0a05061061ff47f9bd655ef52b6a2ba3a1b8ae04cd9d34b0f03d4bf3f.
- Presentation pass: /home/saeed/train-survivor-evidence/2026-11-09-presentation/
  (sheet-A-scene.jpg, sheet-B-guns-walker.jpg, gameplay-realtime2.mp4 17.45 s 31.29 fps
  with sink-monitor audio; x11grab was black under Wayland, device screenrecord used).
- Runtime projectile trace: /home/saeed/train-survivor-evidence/2026-11-09-projectiles/
  (`proj-trace.txt`, `logcat-normal.txt`, `run-final.png`, `slow/` slow-mo tracer burst).
  Same trace: pid=2 spawns at muzzle, impact enemy 3 hp 30->20 (once); pid=1 misses at
  range, no impact and no damage; every later pid changes hp exactly once.
- Scenery: /home/saeed/train-survivor-evidence/2026-11-09-scenery-zup/
- Walker facing: /home/saeed/train-survivor-evidence/2026-11-09-walkers2/
- Emulator game audio: /home/saeed/train-survivor-evidence/2026-11-09-audio-fix/
- Mount split frame: /home/saeed/train-survivor-evidence/2026-11-09-gunsplit/
- Stage 1 -> Stage 2: /home/saeed/train-survivor-evidence/2026-11-09-final/ (older build)

## Unverified / known gaps
- Driven expiry and dead-target paths are now tested (34/34); the scenery-hit miss path is
  implemented but was not observed at runtime (all observed misses were range/expiry).
- Aim: tracked shots measure 0.2 deg muzzle error and land; the 1 deg gate removes the
  former 5.5 deg settling shot. At 20 deg depression the split face opens a small wedge at
  the hinge (a planar cut, not a ball joint); the base itself never leaves the plate.
- Pedestal split is visually confirmed at rest and 0/90/180 yaw in
  sheet-B-guns-walker.jpg; extreme depression shows the wedge above.
- Normal Stage 1 -> Stage 2 was proven on build 3bda1682..., NOT on the current adecef...
- Frame times were measured earlier (mean 16.9 ms / p95 16.67 ms, 60 fps cap) but not on
  the latest build.
- Walker contact/attack frame and per-frame stride/dt logging still missing.
- Camera/mount inspection cameras were unreliable (timed toggle, under-terrain poses);
  replace with an explicitly positioned Camera.LookAt framed from bounds.
- Missing audio actions: walker attack/hurt, footsteps, distinct explosion vs collapse,
  retry confirm; engine has no mute flag.

## Next work (in order)
1. Fresh normal Stage 1 -> Stage 2 on the current build, frame-time and log sweep.
2. Optional polish: hide the small split wedge at extreme gun depression (shroud or
   overlapping cut), and decide whether the scenery density/scale needs a per-biome pass.

## Rules
- Preserve unrelated changes; do not commit or publish; keep Walker yaw 0, scenery -90X and
  the working audio route.
- Only touch the emulator's own audio/route settings, never unrelated host audio.
