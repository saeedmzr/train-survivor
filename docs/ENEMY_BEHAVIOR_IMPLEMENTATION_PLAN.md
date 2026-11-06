# Train Survivor — Enemy Animation & Behavior Plan

Status: implementation plan, based on the 12 painted cards in
`art-work/enemies/*/card.png` and the current runtime in `unity/Assets/_Project/Scripts`.

## 0. The short version

Use **sprite swapping**, not skeletal rigs, for these 12 enemies.

The art is already flattened, side-on cutouts. A six-frame walk cycle exists
(`enemy_walk_0..5`), and the runtime already draws pooled billboards. Retrofitting
skeletal rigs would mean cutting every creature into layered parts and repainting the
hidden overlap areas — a large art job with little payoff at this camera distance.
Skeletal rigging is worth revisiting only for a close-up/codex view or for one hero
enemy, not for the horde.

The real gap is not the walk cycle. It is that `World.StepEnemies` gives every species
the same brain: close distance, stop at `ContactRange`, bite. Only three data flags
currently change anything (`RamImmune`, `BlastRadius`, `Flying`), and `Flying` is not
actually consulted by movement or ramming. The plan below adds a state machine,
logical hit volumes, and one distinct behavior per species.

## 1. Read the cards, then fix the data drift

Card values (HP / SPD / DMG / XP) are below. Armor, pack, lane and threat are not
printed on the cards; those come from `art-work/enemies/*/meta.json`.

| Enemy | HP | SPD | DMG | XP | Armor | Pack | Lane | Threat |
|---|---:|---:|---:|---:|---:|---|---|---|
| The Lantern (`lantern`) | 85 | 2.0 | 6 | 20 | 0 | 1 | rear | high |
| Bloater | 60 | 2.2 | 18 | 12 | 1 | 1–2 | rails | medium |
| Bulwark | 110 | 2.4 | 12 | 18 | 3 | 1–2 | rails | high |
| Crawler | 22 | 4.4 | 4 | 4 | 0 | 3–5 | rails | low |
| Drillmaw | 90 | 3.0 | 15 | 16 | 2 | 1 | underground | high |
| Glider | 35 | 7.0 | 9 | 9 | 0 | 2–4 | air | medium |
| Rimback (`rimeback`) | 140 | 1.8 | 16 | 22 | 4 | 1 | rails | high |
| Shrieker | 70 | 2.6 | 0 | 15 | 0 | 1 | rear | medium |
| Spitter | 45 | 2.8 | 7 | 10 | 0 | 1–3 | embankment | medium |
| Sprinter | 12 | 6.5 | 4 | 6 | 0 | 3–5 | flanks | low |
| Tank | 255 | 1.2 | 18 | 25 | 2 | 1 | rails | high |
| Walker | 30 | 2.2 | 6 | 5 | 0 | 4–8 | rails | low |

The current `SimConfig.Stat` table is not the card table:

| Enemy | Card | Current sim |
|---|---|---|
| Walker | XP 5 | XP 1.5 |
| Sprinter | XP 6 | XP 1 |
| Tank | XP 25 | XP 6 |
| Bloater | DMG 18, XP 12 | DMG 12, XP 4 |
| Shrieker | DMG 0, XP 15 | DMG 3, XP 4 |
| Glider | SPD 7.0, XP 9 | SPD 5.2, XP 3 |
| Crawler | XP 4 | XP 2 |
| Spitter | XP 10 | XP 3.5 |
| Bulwark | XP 18 | XP 5 |
| Drillmaw | XP 16 | XP 4.5 |
| Lantern | XP 20 | XP 5 |
| Rimback | XP 22 | XP 5.5 |

Also missing entirely: `Armor` has no field and no damage-reduction path. `Flying` is
stored but never used by `StepEnemies`. `Pack` and `Lane` are only in metadata; the sim
spawns random X positions and random group sizes.

**Decision:** make one JSON/C# table authoritative. Use the card HP/SPD/DMG values.
Keep the current low XP drops for now and store the card XP as `DesignXp`; do not
change combat and the XP economy in the same pass. Add `Armor`, `PackMin/Max`, `Lane`,
`BodyRadius` and `Height` to the same table. If card XP is used later, re-tune
`XpBase`/`XpPerLevel` at the same time so stages do not level too fast.

## 2. Architecture that fits this project

The game looks 2D but is a 3D scene with billboards. Gameplay lives in the
UnityEngine-free `TrainSurvivor.Core`; views only read state.

```
World (fixed 60 Hz, deterministic)
  Enemy[]  S, X, Hp, Speed, State, timers, shield/armor/status
  Projectile[] / Hazard[] / Corpse[] (fixed arrays, preallocated)
  SimEvents -> EnemyViewPool / Effects / AudioDirector

EnemyViewPool (Unity)
  pooled SpriteBillboard per enemy
  animation clip selection + frame stepping
  VFX hooks from SimEvents
```

Do **not** put gameplay colliders, NavMesh or Rigidbody2D into the sim. It would
break determinism and the EditMode tests. Keep all collision in `(S, X)` track space.
If you later move to real Unity physics, use 3D triggers on the XZ plane, not
Physics2D: the scene's ground plane is XZ.

`Prefabs/Enemies/*.prefab` are not instantiated at runtime. `EnemyViewPool` creates
its own `SpriteBillboard` objects. Adding an Animator or Collider to those prefabs will
not change the game unless you also change the spawn path. Treat them as art/reference
prefabs for now.

## 3. Animation plan

### 3.1 Keep the existing cycle and add state clips

Each species already has six padded walk poses. Add a small state-driven player in
`EnemyViewPool` instead of 48 Animator Controllers:

```
AnimClip { name, firstFrame, frameCount, fps, loop, nextState }
```

Suggested clips:

| Enemy | Locomotion | Telegraph / action | Reaction |
|---|---|---|---|
| Walker | walk_0..5 | walk 4–5 + arm/VFX | hit, dead |
| Sprinter | walk_0..5 at high fps | frames 3–4 + fire trail | hit, dead |
| Tank | walk_0..5 slow | frame 0 + squash, then slam VFX | hit, dead |
| Crawler | walk_0..5 fast | crouch frame 5, then low slide | hit, dead |
| Bloater | walk_0..5 | frame 0 + red pulse for 1.2 s | explode, dead |
| Spitter | walk_0..5 | frame 0, then recoil/frame 2 | hit, dead |
| Shrieker | walk_0..5 | frames 1/3 + sound rings | hit, dead |
| Bulwark | walk_0..5 | shield-forward hold, spark on block | shield break, hit, dead |
| Glider | walk_0..5 loop | swoop frame 3, attach frame 1 | takeoff, hit, dead |
| Drillmaw | walk_0..5 | hide + dust, surface frame 2, lunge 3 | hit, dead |
| Lantern | walk_0..5 | raise frame 1/4 + lantern glow | hit, dead |
| Rimback | walk_0..5 slow | frame 2 + ice flash | armor crack, hit, dead |

The existing `sprite_hit.png` and `sprite_dead.png` are already derived for every
species. Pack them into the atlas (or a second atlas) and use them rather than
generating more art.

Rules:

- Advance walk frames by distance/speed, not wall time alone. A stopped enemy should
  idle, not moonwalk.
- Use one-shot clips for telegraph/attack/death and return to locomotion.
- Keep scale on a shared padded canvas so frames never pop size.
- Reset `Squash` to `(1,1)` explicitly after a one-shot; the current billboard applies
  squash but does not restore it when the value returns to 1.
- Fix pool identity: `Get(used++)` reassigns body slots every frame. If an enemy dies,
  all later enemies shift slots and their animation phase jumps. Key bodies by
  `Enemy.Id` (dictionary or stable slot array), or store animation phase on the enemy.
- Keep animation timing view-only (`Time.deltaTime`); never let it feed back into the
  fixed sim.

### 3.2 If you insist on skeletal animation

Use Unity's 2D Animation package only if you can first produce layered source art:

1. Cut each card cutout into head, torso, upper/lower arms, legs, shield, lantern.
2. Repaint the areas hidden behind those parts.
3. Import as Sprites, open Sprite Editor > Skinning, create a bone hierarchy.
4. Auto-generate weights, then fix shoulders/hips manually.
5. Animate idle/walk/telegraph/attack, and use a Sprite Library for species variants.
6. Keep hitboxes as separate objects; do not parent damage volumes to deforming bones.

Cost: 12 unique rigs, plus cutout/inpainting work. At the current on-screen size, the
six-frame swap will read nearly as well and is far cheaper. Do not mix approaches in
one pooled view: Animator per billboard is unnecessary overhead on the Android target.

## 4. Colliders and hit volumes

### 4.1 In this project

Use logical, deterministic hit volumes:

```csharp
struct EnemyStat {
    float Hp, Speed, Damage, AttackInterval, Xp, Armor;
    float BodyRadius, Height;
    int PackMin, PackMax;
    LaneKind Lane;
    bool RamImmune, Flying, LowProfile;
    float ShieldArc, ShieldHp;
}
```

Add to `Enemy`:

```csharp
EnemyState State;
float StateTime;
float AbilityCd;
float ShieldHp;
int ArmorHits;
bool ArmorBroken;
bool Targetable;
bool IsHusk;
int MasterId;
float ChillTimer;
```

Hit tests use `S` (along track) and `X` (lateral). A body is a circle or capsule in
that space. Bullets are still resolved from the existing nearest-target logic, but
the damage call receives a direction so Bulwark can reject frontal hits. Explosions
use a radius query; acid uses a timed hazard query; the train hurtbox is a capsule
from nose to last wagon.

### 4.2 Conventional 2D recipe (for a from-scratch Unity 2D scene)

- `Rigidbody2D`: Kinematic, gravity 0, freeze rotation.
- Ground body: `CapsuleCollider2D` (or Circle for small enemies), trigger off.
- Damage/contact volumes: separate trigger colliders; resolve with
  `Physics2D.OverlapCircle`/`OverlapBox`, not `OnCollisionStay`.
- Layers: `EnemyBody`, `EnemyShield`, `EnemyProjectile`, `TrainHurtbox`, `Hazard`,
  `AirEnemy`; disable EnemyBody-vs-EnemyBody collision.
- Bulwark: body collider + a child `BoxCollider2D` shield. The shield is a separate
  layer so projectiles can be rejected before body damage.
- Crawler: low capsule, ram layer ignored; swap to a 0.25 m profile when close.
- Glider: air layer, ignores ground/blockade layers; no ram while airborne.
- Drillmaw: disable the body collider while burrowed; dust is a trigger only.
- Bloater: explosion is an `OverlapCircle` query with a recursion guard.
- Spitter projectiles: trigger + Kinematic body; pool acid on impact.

Starting sizes (derive from `heightM`, tune in play):

| Enemy | Body | Special |
|---|---|---|
| Walker | capsule r0.35 × h1.8 | — |
| Sprinter | capsule r0.30 × h1.9 | — |
| Tank | capsule r0.80 × h2.9 | ram immune |
| Crawler | capsule r0.55 × h0.5; 0.25 crouched | ram/blockade immune |
| Bloater | circle r0.80 | 3 m blast |
| Spitter | capsule r0.45 × h1.8 | projectile + acid pool |
| Shrieker | capsule r0.40 × h1.75 | 12 m aura, no contact damage |
| Bulwark | capsule r0.50 × h2.3 | shield box 0.15 × 1.8 in front |
| Glider | circle r0.35 | airborne, no ground collision |
| Drillmaw | circle r0.55 | disabled underground |
| Lantern | capsule r0.35 × h2.2 | — |
| Rimback | capsule r0.90 × h2.4 | armor/ice |

## 5. Shared AI framework

Add a small state machine and run it per enemy in `World.StepEnemies`:

```
Spawn -> Approach -> Telegraph -> Execute -> Recover -> Approach
                  -> Fuse -> Detonate
                  -> Burrow -> Surface -> Lunge
                  -> Swoop -> Attach -> Takeoff
                  -> Stunned -> Approach
```

Each state has a timer, ability cooldown, target lane and target range. One
`switch (kind)` dispatches to a behavior function; do not create a MonoBehaviour per
enemy and do not use NavMesh.

Steering:

```
desiredS = train.S + preferredRange(kind);
desiredX = laneX(kind) + separation;
step toward desired, clamped by speed;
```

- Melee preferred range: ~1.5–3 m.
- Spitter: 25 m.
- Shrieker/Lantern: 18–30 m (rear support).
- Drillmaw: train.S + 8 m when surfacing.
- Glider: sine-wave X, altitude 1.5–3 m, swoop at <10 m.

Add a light deterministic separation pass (64 enemies, O(n²) is fine): push apart
only when two bodies are within `r1 + r2`, clamp back into the lane. Without this,
the horde stacks into one silhouette and the new behaviors are unreadable.

Centralize damage and death:

```csharp
ApplyDamage(ref Enemy e, float amount, DamageType type, int sourceId, float fromS, float fromX)
Kill(ref Enemy e, KillCause cause, bool allowLoot, bool allowBlast, bool allowHusk)
```

This is required for Bulwark shield, Rimback armor, fire bypass, Bloater chains and
Lantern husks. Do not keep scattering `e.Hp -=` through `StepGuns`, `StepMovement`,
fire trail and ram. A single pipeline also prevents the current Bloater bug where the
blast sets a neighbour's HP below zero but never marks it dead.

Use event kinds for the view/audio: `Telegraph`, `Explode`, `ShieldBlock`,
`ShieldBreak`, `Summon`, `HuskSpawn`, `Projectile`, `Hazard`, `Burrow`, `Surface`,
`GunDisabled`, `Chill`, `Stun`, `Burn`.

## 6. Per-enemy behavior

| Enemy | Ability and AI |
|---|---|
| **Walker** | Standard shambler. Approach with separation, bite at contact. On taking damage, drop an acid puddle every ~1.5 s (2 m, 2 s). Packs of 4–8. |
| **Sprinter** | Flank lane, speed 6.5. At <6 m, leap; on contact apply a 3 s burning DoT to the train and die. It is a fast expendable threat, not a sustained biter. |
| **Tank** | Slow, 255 HP, armor 2, ram-immune. At <5 m, 1 s ground-pound windup, then 4 m AoE: 18 damage + ~1.2 s train stun (guns offline). Otherwise melee. It must not be killable by a single ram. |
| **Crawler** | Fast 4.4. At <8 m, crouch to 0.25 m, become ram-immune and ignore barricades. If it reaches the train, latch underneath for ~3 s, deal 4 dps, then despawn. Kill it before it goes under; splash can still hit it. |
| **Bloater** | At <3 m or on death, 1.2 s fuse, then 3 m blast for 18 to train and enemies. Kill it early to chain-react the pack. Use a death queue so a chain cannot recurse forever. |
| **Spitter** | Stop at 25 m on the embankment. 0.8 s telegraph, then lob an acid glob in an arc over barricades, leading the train; impact leaves a 2 s pool. If the train brakes, it repositions. This is the brake-counter enemy. |
| **Rimback** | Slow 1.8, armor 4. On contact, chill the throttle: −35% speed for 2 s. Ice armor cracks after 3 hits; cracked armor takes double damage. Fire should bypass/reduce armor fastest. |
| **Shrieker** | Rear support, 0 damage. Aura +15% speed to allies within 12 m (do not stack). Every 8 s, 1.5 s scream telegraph, then spawn 6 walkers at the rear. Focus target. |
| **Bulwark** | Shield toward the train. Front cone ±50° blocks bullets; shield has 600 HP and breaks permanently. Turn rate is slow. Body armor 3. Fire bypasses the plate. If the shield never breaks, the train's centreline guns make it unkillable, so shield break is mandatory. |
| **Glider** | Fly at 7.0 in a sine wave, ignore ground/barricades, cannot be rammed airborne. At <10 m, swoop onto a wagon and disable one gun slot for 3 s. Take off and re-approach; kill it before it reaches the convoy. |
| **Drillmaw** | Burrow at 5 m/s, untargetable, dust trail. Surface 8 m ahead with a 0.6 s telegraph, then lunge 6 m. While underground it is removed from gun targeting; splash at the surface point is the counter. |
| **Lantern** | Rear necromancer. Every 6 s raise a nearby corpse as a husk at 50% HP, max 3 active. Husks drop no loot and collapse when the Lantern dies. It stays at 20–30 m and is always visible by its lamp glow. |

Implementation order by dependency:

1. **Wave 1:** Walker, Sprinter, Crawler, Tank — melee, low profile and stun.
2. **Wave 2:** Bloater, Spitter, Rimback — blast, projectile/hazard, armor/status.
3. **Wave 3:** Bulwark, Glider, Drillmaw — directional shield, flight/gun disable, burrow.
4. **Wave 4:** Shrieker, Lantern — aura/summon, corpse/husk lifecycle.

## 7. Step-by-step build order

### Phase 0 — Freeze the data contract

- Extend `EnemyStat` with armor, radius, height, pack, lane, ability tuning.
- Merge card values and metadata into one source; add a small generator or a manual
  table in `SimConfig`. Keep `DesignXp` separate from drop XP.
- Add a debug overlay showing state, HP, armor, shield, cooldown and target.
- Add tests that every species has non-zero HP/speed and valid pack/lane.

### Phase 1 — Make animation state-driven

- Add `AnimState`, `AnimClip`, `ClipFor(kind, state)` in `EnemyViewPool`.
- Fix body identity by `Enemy.Id`.
- Add hit/dead frames; add telegraph one-shots.
- Add speed-driven walk playback and explicit squash reset.
- Pack any new frames into a second 2048 atlas if the existing 2048 is full; use ASTC
  on Android rather than a 4096 RGBA atlas.

### Phase 2 — Central damage/death and hit volumes

- Add `DamageType`, `ApplyDamage`, `Kill`, `ApplyArmor`, `ApplyShield`.
- Move gun, ram, fire-trail and blast damage onto the pipeline.
- Add `Targetable` filtering to `FindNearestTarget` and `NearestAhead`.
- Add `BodyRadius` checks where needed; keep the existing nearest-target behavior for
  normal enemies.
- Add tests for armor, shield block/break, ram immunity and blast chains.

### Phase 3 — Common AI plus Wave 1

- Add `EnemyState`, `StateTime`, `AbilityCd`, `LaneX`, `Facing`.
- Add approach/telegraph/execute/recover and separation.
- Implement Walker, Sprinter, Crawler, Tank.
- Update the existing ram test: Tank is now ram-immune, so use a Walker to assert the
  ram event and add a separate Tank test.

### Phase 4 — Wave 2

- Add `Projectile` and `Hazard` arrays with fixed capacities.
- Add acid pool, arc projectile, burn and chill statuses on `TrainState`.
- Implement Bloater, Spitter, Rimback.
- Add VFX/audio events; verify pool caps.

### Phase 5 — Wave 3

- Add `GunDisable[]` to the train state and `StepGuns` skip logic.
- Add burrow/targetable lifecycle and dust events.
- Implement Bulwark, Glider, Drillmaw.
- Add a debug view for shield arc and burrow state.

### Phase 6 — Wave 4

- Add `Corpse` ring buffer and `IsHusk/MasterId`.
- Add aura pre-pass (max one aura, no stacking) and summon caps.
- Implement Shrieker, Lantern.
- Test summon/husk cleanup and no-loot rules.

### Phase 7 — Polish and device verification

- Tune telegraph lengths, cooldowns and pack sizes on device.
- Verify with `dev.lineup=1`, `dev.timeScale=0.15`, `dev.noguns=1` and screenshots.
- Confirm `adb logcat` has zero exceptions and the horde stays at 60 fps.
- Update README/design docs to match the new behavior; the README still says one
  sprite per species.

## 8. Test and acceptance checklist

Sim tests (EditMode, no Unity objects):

- Bulwark blocks frontal shots, takes side damage, breaks at 600 shield damage.
- Crawler survives a ram, goes low, and cannot be targeted after going under.
- Bloater blast damages neighbours and cannot infinite-chain.
- Spitter projectile lands/pools; braking changes the impact point.
- Shrieker aura is capped and summons stop at the cap.
- Glider disables exactly one gun for 3 s and re-enables it.
- Drillmaw is untargetable while burrowed and targetable after surfacing.
- Lantern raises at most 3 husks; husks die with the Lantern and drop no loot.
- Rimback applies chill and takes double damage after 3 armor hits.
- Tank is ram-immune and its slam stuns for the intended window.
- Sprinter applies burn and dies on contact.
- Walker drops an acid hazard on damage.
- Same seed still produces the same run over 600 ticks.

Acceptance for each enemy:

1. It reads as a different silhouette and motion at horde distance.
2. Its ability has a visible/audible telegraph.
3. The stated counterplay works (brake, focus, flank, burn, splash, etc.).
4. It cannot soft-lock the run (Crawler under train, Glider with one gun,
   Bulwark with unbreakable shield).
5. It adds no per-frame allocations and no new Unity physics dependency in Core.

## 9. Main risks

- **Bulwark's front immunity vs. centreline guns.** All train guns are effectively
  frontal. Without the 600-damage shield break, it is only killable by fire/ram.
  Make shield break explicit and test it.
- **Lantern corpse lifecycle.** Corpses, husks and master death need clear ownership;
  otherwise husks leak or keep spawning.
- **Bloater chain recursion.** Use a pending-death queue and a chain-depth cap.
- **Crawler under-train.** If it passes the train it becomes untargetable; give it a
  short undercarriage attack and a despawn timer.
- **Glider + one gun.** Disabling the only gun for 3 s is intended, but must not
  prevent all counterplay; allow the player to shoot it while attached.
- **Atlas memory.** The current 2048 enemy atlas is nearly full; do not jump to a
  4096 RGBA texture on mobile without compression.
- **Prefab trap.** Editing `Prefabs/Enemies` does not affect the pooled runtime.
