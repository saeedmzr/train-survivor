# Train Survivor — System Design Document

Version 1.2 · Unity 6 LTS · Single-player · Portrait mobile · Offline
Source: `Game Design Document: Train Survivor` **v2.0** (this doc's design parent)

> v1.1 change: engine is **Unity 6 + URP**, and the view is a real behind-the-train 3D
> camera rather than faked 2.5D. Game design, tuning numbers, and the juice spec are
> unchanged — they are engine-agnostic.
>
> **v1.2 change (2026-11-06):** the design moved. GDD v2.0 now owns the rules; this
> document owns the engineering. Superseded here: the gun/wagon formula (§8.2), the enemy
> roster (§8.1), the card count (§8.4), the scope table (§5) and the milestone trigger
> (§4.4). Machine-readable canon lives in `finalized-for-develop/data/*.json`.

---

## 1. What we are building, in one paragraph

A one-thumb, portrait-mode action roguelite. The train rolls forward while your finger is
down; lift the finger and the train brakes and every gun on the train opens fire on the
nearest zombie. You push forward when the road is clear, you stop when it isn't, and every
120 seconds you pick 1 of 3 upgrades that visibly make the train bigger and louder. A run is
20–30 stages. No second currency, no gacha, no energy, no ads.

Two things carry the entire game: **the brake**, and **the noise it makes**.

---

## 2. Design pillars

| Pillar | Rule it imposes |
|---|---|
| **Every input is a power move** | Pressing = brutal acceleration + cowcatcher violence. Releasing = guns + sparks + a stop you *feel*. Neither state is downtime. |
| **The screen answers in < 80 ms** | Any hit, kill, or brake produces visual + audio + haptic response within one or two frames. |
| **Numbers go up, visibly** | Guns, wagons, multipliers are physically attached to the train, not buried in a menu. |
| **Failure is 2 seconds long** | Death → stats → retry. No menus, no confirmation, no loading. |
| **Deterministic sim, interpolated render** | Gameplay runs on a fixed 60 Hz step, so tuning numbers mean what they say and 90/120 Hz phones look better without changing balance. |

---

## 3. Core loop and the two GDD changes I recommend

### 3.1 Loop

```
   hold ──► ACCELERATE (0→100% in 0.6s)
            │  + cowcatcher ram (frontal damage)
            │  + brake-charge meter fills
            ▼
   release ─► BRAKE BURST (0.18s decel, sparks, screech)
            │  + guns live immediately (no windup)
            │  + charge spent as bonus fire rate
            ▼
   combat ──► auto-target nearest → kills → XP orbs vacuum in
            │  adrenaline stacks, streak pitch climbs
            ▼
   clear ───► hold again (blockade ahead → must stop → destroy → clear)
            ▼
   gate ────► STAGE CLEAR slow-mo → 3 upgrade cards → next stage
```

### 3.2 Change A — the train rams while moving

As written, moving means zero offense, which makes holding the screen feel like a penalty
and pushes players into stop-start inching. Fix: above 60% speed the cowcatcher deals
**35 damage** to anything within 3 m, knocks it aside with a chunky impact, and the train
takes no damage from it. It cannot kill a Tank, so Tanks still force a stop, and ramming
through a Sprinter swarm to escape is still a real gamble. Both states stay fun; the rhythm
becomes *charge → burst* instead of *creep → wait*.

### 3.3 Change B — the brake charge

While moving, a meter fills (0→100% over 4 s). On release the first 2 s of firing get **+40%
fire rate** in proportion to the charge held. Moving stops being "not shooting" and becomes
*loading the gun* — the anticipation hook that makes the release pay off.

### 3.4 State machine (authoritative)

```
MOVING    input held, speed → target; guns OFF, ram ON, charge += dt
          blockade within 4 m → clamp speed to 0 → BLOCKED
BRAKING   input released, speed → 0 over 0.18s, guns ON at t=0
STOPPED   speed 0, guns ON, targeting active
BLOCKED   input still held against a blockade; speed 0; guns OFF (player must release)
CLEAR     track end → 300ms slow-mo → reward
DEAD      lead wagon HP <= 0 → 250ms shake → stats → retry
```

Note vs the GDD: `Moving` is driven by *press held*, not `TouchPhase.Stationary` — Stationary
fires on a held-but-still finger and is unreliable across Android devices.

---

## 4. Dopamine architecture (the actual product)

Every row is an implementable rule with a number.

### 4.1 The kill (5–15×/second at peak)

| Beat | Timing | Implementation |
|---|---|---|
| Muzzle flash + recoil | 0 ms | 6-frame additive sprite; gun barrel kicks 4 cm back, returns in 60 ms |
| Tracer | 0 ms | stretched quad beam, fades 45 ms, additive, no light |
| Hit flash | 0 ms | per-material `_FlashAmount` property block, 0→1 over 40 ms (never a material instance) |
| Damage number | 0 ms | sprite digits rise 14 px over 380 ms, scale 1.25→1.0 in 110 ms |
| Hitstop | 45 ms | view-only pause; sim + input keep running |
| Kill | — | 6–10 gore chunks scaled by enemy size; 45 ms hitstop; light haptic |
| Streak | combo ≥ 5 | SFX pitch +1 semitone per 5 kills, cap +12 |
| Adrenaline | per kill | +1 stack, cap 25, −1 per 0.8 s idle; **+2% fire rate per stack** |

Gore is stylized: chunky coloured chunks, no realism, 4 gore palettes (one per biome).

### 4.2 The brake (the signature sensation, every 2–6 s)

- 18–24 wheel sparks, 700 px/s, gravity, 0.5 s life, additive.
- Metallic screech 0.35 s (pitch ±4%) + **CLUNK** transient 120 ms later.
- Cinemachine impulse: `trauma += 0.25` (`offset = trauma² · 14 px`, decay 1.8/s).
- 40 ms hitstop on the deceleration kick so the stop has weight.
- Light haptic.

### 4.3 Perfect Brake (skill expression, v1)

A marker sweeps a small bar while at speed. Releasing within ±0.18 s of it = **PERFECT
BRAKE**: 90 ms slow-mo, gold edge flash, +60% fire rate for 1.5 s, deeper CLUNK, medium
haptic. Auto-aim makes aiming skill irrelevant, so mastery lives here. Marker appears every
5–7 s of continuous movement; whiffing costs nothing.

### 4.4 Stage clear (the payoff beat)

1. Slow-mo 0.3× for 300 ms, bloom pulse, music ducked.
2. `STAGE 7 CLEARED` slams in (scale 1.4→1.0, 260 ms) with 0.45 trauma.
3. XP orbs vacuum to the train with eased seek — the most satisfying thing in the game.
4. Three cards rise, staggered 0/70/140 ms. Tap → card flies into the train → 200 ms later
   the effect is visible (gun clamps on, wagon hitches with a metal CLANK).
5. Milestones reuse the sequence: `+1 WAGON` at LV 5 / 10 / 15 / 20, and `+1 GUN` only when
   an *Extra Gun* card fills a wagon's second slot (max 2 guns per wagon, 10 per convoy).

### 4.5 Variable-ratio reward

1 in 8 kills drops a **Surge**: 3× fire rate 6 s, screen desaturates except the train, music
gains a layer, camera FOV −2°. One active at a time. Cheapest, strongest lever in the design.

### 4.6 Tension as dopamine

HP < 25%: red vignette at 60 bpm, heartbeat, music drops to a drone, baseline shake 0.08.
Clearing a stage under 25% → `SURVIVED` banner + guaranteed rare card. Relief needs a build-up.

### 4.7 Failure

`DERAILED` in 250 ms, engine hiss, four stats (stage, kills, best combo, damage), one large
**RETRY** that restarts in under 1 s with no re-load. Meta guns/wagons persist; run upgrades
don't. No lives, no shaming, no confirmation dialogs.

### 4.8 Feedback budget (per frame, hard caps)

| System | Cap |
|---|---|
| Particles alive | 700 (pooled, oldest dropped) |
| Damage numbers | 60 (aggregate above 12 at one spot) |
| Hitstop events | 1 per 120 ms (coalesce) |
| Bullets | 300 |
| Concurrent enemies | 60 |

**Rule: the screen may be loud, but it must never hide the nearest enemy.** Enemies above
particles, damage numbers above enemies, HUD above all. The nearest threat gets a thin
outline so it reads through the noise.

---

## 5. Scope

### v1
- 1 train, 6 gun variants (7 ammo types), 5 wagons × 2 gun slots, 24 upgrade cards
  (9 have art today — see `finalized-for-develop/ui/cards/`).
- **3 biomes** (burning metropolis / frozen canyon / toxic swamp) × 20 storyboard beats each,
  **13 enemies**, **10 bosses**. The desert biome stays cut until it has its own enemy set.
- 30 stages + soft prestige (bank wagons, restart at stage 1 with higher scaling).
- Local best, save file, haptics, fully offline.

### Out
Multiplayer, PvP, gacha, ads, IAP, cloud save, social, skins, procedural track generation
(hand-built segments are better and 10× faster to tune).

---

## 6. Tech stack

### 6.1 Decision: Unity 6 LTS + URP (3D), C#, IL2CPP

| Layer | Choice |
|---|---|
| Engine | **Unity 6 LTS (6000.x)** |
| Render pipeline | **URP 17**, Linear color space, mobile quality tier |
| Language | C# (.NET Standard 2.1), `#nullable enable` |
| Scripting backend | **IL2CPP, ARM64 only**, Release builds (never Mono on device) |
| Camera | Cinemachine 3 (follow rig + impulse listener) |
| Track | Unity Splines 2.x (rail spline + fork branches) |
| Input | Input System 1.11 (touch press + drag) |
| Assets | Addressables 2.x (biome bundles preloaded behind the splash) |
| Audio | AudioSource pool + AudioMixer ("Best latency" DSP buffer) |
| UI | UGUI + TextMeshPro, split canvases (static HUD vs. mutating values) |
| Tween | DOTween (free) for UI; hand-written springs in gameplay for zero GC |
| Tests | Unity Test Framework — EditMode for sim, PlayMode for integration |
| CI | Unity Cloud Build, or GitHub Actions with `-batchmode -runTests` |

### 6.2 Why Unity — and the honest cost

**Your call is the better one for a shipped game.** The reasons are engineering, not taste:

1. **Frame pacing.** A native IL2CPP binary on a fixed-step loop holds 60 fps far more
   reliably on mid-range Android than a WebView: no browser compositor variance, no
   JavaScript GC pause in the middle of a 300-bullet frame, no OS throttling of a "tab".
   "Smooth" means *consistent* frame times, and native wins that.
2. **Audio latency.** Android Chrome's WebAudio path adds 20–40 ms of output latency and can
   drift; a native build with a short DSP buffer gives the immediate, punchy gunfire this
   game is built out of. The core reward here is *sound* — this gap is enormous.
3. **Native feel.** Real haptics, safe-area handling, 90/120 Hz displays, Play Store
   delivery, and profilers (Frame Debugger, Memory Profiler) that actually see the GPU.
4. **Real 3D at near-zero asset cost.** The GDD wants a camera behind the train. Unity gives
   a true perspective camera, a spline rail, and low-poly props assembled from primitives —
   no art team needed for v1, with a clean upgrade path when art arrives.
5. **Store reality.** Ads, IAP, analytics, attribution, and console ports exist here. Web
   wrappers make all of that someone else's problem.

**The cost, stated plainly:** iteration is slower. Script reload and play mode are seconds,
not milliseconds, and "does it feel right?" takes longer to answer than a browser hot
reload — that is exactly what I was optimizing for when I first picked the web stack. Three
mitigations are built into this plan:

- **The sim lives in a UnityEngine-free assembly** (`TrainSurvivor.Core`), so balance rules
  and combat math are EditMode tests that run in ~1 s instead of a play session.
- **A live tuning overlay** bound to ScriptableObjects, so numbers are edited while the game
  runs on the phone, not by rebuilding.
- **Tuning values re-read at stage start**, so a balance pass needs no domain reload.

### 6.3 Environment prerequisites (the actual blockers)

Status: resolved. Unity 6000.0.85f2 (Hub, Linux) with Android Build Support is installed,
the Personal licence is active, and `tools/deploy.sh` produces and installs an APK. EditMode
tests run headless (`unity/Logs/editmode.xml`). The original blocker table is kept below for
provenance.

This machine at planning time: RTX 5070 Max-Q, 30 GB RAM, Node 22. No Unity, no .NET, no
Android SDK.

| Step | Size | Who |
|---|---|---|
| Unity Hub (AppImage → `~/Applications`, no sudo) | ~250 MB | I can install |
| Unity 6 LTS editor, Linux | ~6–8 GB | I can install |
| Android Build Support (SDK 34, NDK, OpenJDK 17) | ~8–12 GB | I can install |
| **License activation** | — | **You** — free Personal license, signed in via Hub. I will not handle or ask for credentials. |
| iOS build | — | Needs a Mac; defer to v1.1 |

Until the license is active, `-batchmode -runTests` and player builds fail, so I can write
and structure the whole project but cannot *verify* runtime behaviour. That gate is yours.

---

## 7. Architecture

### 7.1 Project layout

```
Assets/_Project/
  Art/
    Resources/Art/         atlas_world.png, atlas_ui.png, Sprite3D.shader
  Content/                 ScriptableObjects — the tuning surface
    Enemies/ Guns/ Upgrades/ Stages/ Biomes/
  Prefabs/  Train/ Enemies/ Vfx/ Blockades/ Props/
  Scenes/   Boot.unity  MainMenu.unity  Run.unity
  Scripts/
    Core/      ← asmdef with NO UnityEngine reference: pure C#, unit-testable
      Sim/     World.cs TrainState.cs CombatSolver.cs SpawnDirector.cs RunState.cs
      Rng/     Mulberry32.cs
      Data/    EnemyDef.cs GunDef.cs UpgradeDef.cs StageDef.cs (POCOs)
    Game/      ← asmdef refs Core + InputSystem + URP
      GameLoop.cs  InputRouter.cs  SimBridge.cs  ContentBinder.cs  DevTuning.cs
    View/
      TrainRig.cs  EnemyPool.cs  BlockadeView.cs  PickupView.cs  Scenery.cs
      TrackBuilder.cs  ImpactVfx.cs  SpriteBillboard.cs  ArtSprites.cs
    Juice/     HitStop.cs  TraumaShake.cs  SlowMo.cs  FlashQueue.cs  CardTween.cs
    Audio/     AudioDirector.cs  SfxPool.cs  MusicLayers.cs
    UI/        HudView.cs  UiFactory.cs  CardPanel  GameOverScreen
    Save/      SaveFile.cs  Migrations.cs
  Settings/    URP asset + renderer, QualityTiers, AudioMixer
Assets/Tests/
  EditMode/    SimTests  MilestoneTests  SaveMigrationTests  ReplayTests
  PlayMode/    BootSmokeTests  StageFlowTests
```

The assembly split is not bureaucracy: it is what makes `Core` compile in a second and what
keeps a 3,000-line MonoBehaviour from becoming untestable.

### 7.2 Runtime loop — where "smooth" is actually earned

```
InputRouter  (Update)        touch press/drag → intent {held, steerX}
GameLoop     (FixedUpdate)   accumulator, 1/60, max 3 catch-up steps → sim.Advance(1/60f)
SimBridge    (Update)        copies sim state into views
View         (Update)        interpolates: pos = lerp(prev, cur, alpha)
Camera       (LateUpdate)    Cinemachine follow + impulse; never in Update
```

- **Fixed 60 Hz sim, interpolated render.** On a 90/120 Hz phone the train glides instead of
  stuttering — the single biggest "premium feel" win available, for one lerp.
- **No physics engine for gameplay.** Position is `(splineDistance, lateralOffset)`; hits are
  circle/segment math in a uniform spatial hash (32 m cells). PhysX is non-deterministic
  across devices, allocates, and buys nothing on a one-lane track.
- **Zero-allocation hot path.** Pools for bullets, enemies, gore, orbs, damage digits, audio
  voices. No `Instantiate`/`Destroy` during a stage. No LINQ, no closures, no string
  concatenation, no `Camera.main`, no `GetComponent` in `Update`, no boxing.
- **Deterministic and replayable.** Seeded RNG for card offers and drops; a run is
  `{seed, inputTimeline}`. EditMode replay tests hash sim state at t=60 s, so a balance
  change that silently breaks a stage fails CI.

### 7.3 Track and camera (real 3D)

- Rail is a Unity Spline; forks are two branch splines that rejoin. Train transform comes from
  spline distance `s` + lateral offset `x`, with `SmoothDamp` 0.12 s on lane changes so they
  feel hydraulic, not twitchy.
- Train: locomotive + wagon chain, each wagon pinned at `s − length·i` with a small yaw lag.
  This is the point of the camera angle — you watch your own train bend into a curve, and it
  sells *mass*.
- Camera: 6 m behind the lead car, 3.2 m up, looking 22 m ahead on the spline, FOV 55.
  Cinemachine keeps it composed through forks; impulse sources drive the juice.
- Blockade at contact sits ~8 m ahead: big, close, unmissable.
- Culling: 120 m draw distance, enemies disabled past 110 m, 60 concurrent cap.

### 7.4 Rendering budget (URP mobile tier)

- Plain Forward (not Forward+), **SRP Batcher on**, dynamic batching for small props, GPU
  instancing for repeated track furniture and hordes.
- Lights: 1 directional + ambient gradient. **No real-time shadows on enemies** — pooled blob
  quads instead. One soft shadow under the train only.
- Post: Bloom at 1/4 resolution (threshold 1.05), Vignette (driven by HP), Color Adjustments.
  No SSAO, motion blur, DoF, or TAA on mobile.
- Textures ASTC 6×6, mipmaps on, 1024 for props, 2048 for train/track atlas.
- 3 shared lit materials (train / enemy / environment) + 1 additive VFX material. Hit flash
  goes through `MaterialPropertyBlock`, never a material instance.
- Enemies use no skinned meshes: rigid part hierarchies with code-driven lurch/bob. At 60 fps
  on a 6-inch screen it reads better than skinning and costs a fraction.
- HUD: static canvas never rebuilt; a second canvas for mutating values; **damage numbers as
  sprite digits** from an atlas (no TextMeshPro mesh regeneration in the hot path).

### 7.5 Audio

`SfxPool` of 24 AudioSources, round-robin, **mono** clips, `DecompressOnLoad`, DSP buffer
"Best latency", pitch ±3% per shot so bursts never sound looped. Music: 3 stems on one mixer
group (base / combat / surge), cross-faded on game state over 0.8 s. Master −3 dB, SFX −6,
music −12; duck music 6 dB on reward screens.

### 7.6 Data model (Core, plain C#)

```csharp
// EnemyKind stayed as a coarse category for spawning/FX routing only. The behaviour comes
// from a data-driven id: see finalized-for-develop/data/enemies.json (13 ids) and
// bosses.json (10 ids, each with 3 phases).
public enum EnemyKind { Walker, Sprinter, Tank, Ranged, Support, Brute, Flyer, Burrower, Swarm, Boss }

public struct Enemy {
    public int Id; public EnemyKind Kind; public EnemyId Species;
    public float S;          // distance along spline
    public float X;          // lateral offset
    public float Hp, MaxHp, Speed, Dmg, AtkCd, Burn, Slow, FlashT;
    public bool Alive;
}

public struct GunSlot {
    public int Id, WagonIndex;   // wagon 0 = engine
    public bool Unlocked;        // slot 1 starts locked; Extra Gun unlocks it
    public float OffsetX, Dmg, Rate, Cd, Pierce, Spread;
    public Element Element;      // None, Fire, Shock, Acid
    public ProjectileId Ammo;    // typed ammo — see data/projectiles.json
}

// Invariant: a wagon owns exactly 2 slots, slot 0 unlocked on hitch, slot 1 locked.
// Convoy cap: 5 wagons, therefore 10 guns maximum.
```

Definitions live in ScriptableObjects for designers, but are flattened into these structs at
stage start so the sim never touches Unity objects.

---

## 8. Numbers

### 8.1 Enemies (stage 1, before scaling)

The roster is now 13 types, not 4. The three v1 baselines below still hold; the ten new
types and every boss live in `finalized-for-develop/data/enemies.json` / `bosses.json`
(readable: `finalized-for-develop/BESTIARY.md`). Boss HP ranges 1,900–4,200 across ten
bosses and is no longer a single `900 ×1.6^boss` formula.

| | HP | Speed m/s | Dmg | Atk interval | XP |
|---|---|---|---|---|---|
| Walker | 30 | 2.2 | 6 | 1.2 s | 1 |
| Sprinter | 12 | 6.5 | 4 | 0.6 s | 1 |
| Tank | 255 | 1.2 | 18 | 2.0 s | 6 |
| Crawler | 22 | 4.4 | 4 | 0.9 s | 4 |
| Bloater | 60 | 2.2 | 18 | contact | 12 |
| Rimeback | 140 | 1.8 | 16 | 1.6 s | 22 |

### 8.2 Player curve

- Base gun **10 dmg / 0.12 s = 83 DPS**, 1 gun at stage 1.
- **Superseded by v1.2:** guns are no longer `1 + floor(stage/3)`. Wagons hitch at LV 1,
  5, 10, 15, 20 (5 max, +35 HP each); each wagon owns 2 gun slots and arrives with slot 0
  filled and slot 1 locked. An *Extra Gun* card fills a locked slot, so the convoy caps at
  **10 guns**, not 12.
- Lead wagon 100 HP; stage 1,000 m; base speed 12 m/s (~75 s rolling, ~110 s with fights).

### 8.3 Scaling (v1 baseline — needs a re-tune pass)

Enemy HP ×1.15^(stage−1), player output ×1.06^(stage−1).

**The Guns column below is the v1 formula and is wrong as of v1.2** (10-slot convoy, plus
typed ammo with different per-type damage). Treat the TTK columns as the *shape* the curve
should have — 0.2–0.35 s for a walker, 1.6–3 s for a tank — and re-derive the numbers once
the ten new enemy types are in the spawn budget.

| Stage | Guns | DPS | Walker HP | Walker TTK | Tank TTK |
|---|---|---|---|---|---|
| 1 | 1 | 83 | 30 | 0.36 s | 3.1 s |
| 5 | 2 | 210 | 52 | 0.25 s | 2.1 s |
| 10 | 4 | 561 | 106 | 0.19 s | 1.6 s |
| 15 | 6 | 1126 | 212 | 0.19 s | 1.6 s |
| 20 | 7 | 1758 | 427 | 0.24 s | 2.1 s |
| 25 | 9 | 3025 | 859 | 0.28 s | 2.4 s |
| 30 | 11 | 4947 | 1727 | 0.35 s | 3.0 s |

Stages 8–18 are deliberately a power fantasy (TTK dips); 20+ re-tightens. That dip is what
produces "one more stage".

### 8.4 Upgrade pool (24 cards; 9 have art)

*Art today:* Multishot, Fire Trail, Spike Bumper, Piercing Rounds, Reinforced Boiler,
Rapid Fire Engine, Incendiary Shells, Extra Gun, Extra Wagon
(`finalized-for-develop/ui/cards/`). The rest are paper-only and reuse the same frame.
**Extra Gun** and **Extra Wagon** are new in v1.2 and are the only cards that change convoy
capacity.

| Category | Card | Effect | Rarity |
|---|---|---|---|
| Weapon | Piercing Rounds | pierce 1 target, 50% dmg (+1 pierce, +10% carry per stack) | 2 |
| Weapon | Hollow Points | +25% dmg, −8% fire rate | 1 |
| Weapon | Ricochet | 20% chance to bounce at 40% dmg | 2 |
| Weapon | Twin Barrels | +1 projectile/gun, −20% dmg each | 2 |
| Weapon | Cryo Rounds | 15% slow 30% for 2 s | 1 |
| Output | Rapid Fire Engine | +15% global fire rate | 1 |
| Output | Overpressure | +12% all-gun damage | 1 |
| Output | Belt Fed | +25% fire rate after 3 s sustained fire | 2 |
| Output | Adrenaline Pump | adrenal cap 25 → 40 | 2 |
| Output | Hair Trigger | brake-charge bonus +40% → +70% | 3 |
| Elemental | Incendiary Shells | 20% ignite, 8 dmg/s 3 s, spreads on death | 2 |
| Elemental | Arc Coil | every 8th shot chains to 3 enemies | 3 |
| Elemental | Toxic Load | 15% poison 5 dmg/s 5 s, −10% enemy speed | 2 |
| Train | Reinforced Boiler | +20% max HP, heals 20% of new max | 1 |
| Train | Steel Plating | −15% incoming damage | 2 |
| Train | Cowcatcher Blades | ram 35 → 75 dmg, +1 m range | 1 |
| Train | Repair Crew | heal 25%, +0.5 HP/s while stopped | 1 |
| Train | Extra Wagon | +1 wagon now (if under cap) | 3 |
| Train | Boiler Vents | +30% fire rate under 30% HP, once/stage | 2 |
| Train | Armored Wheels | immune to sprinter chip while > 60% speed | 2 |
| Utility | XP Magnet | orb radius ×3 | 1 |
| Utility | Scavenger | Surge 1-in-8 → 1-in-4 | 2 |
| Utility | Momentum | +20% move speed, +10% ram | 1 |
| Utility | Second Wind | revive once at 40% HP, clears screen | 3 |

Weights 60/32/8. Offer rule: always include one card touching the weakest stat (HP under 35%,
else output); never three of the same category.

### 8.5 Spawn director

Budget `18 + stage·6` points (walker 1, sprinter 1 in fours, tank 8). Pattern
`[fill, ambush, fill, ambush, finale]`. Spawn 45–85 m ahead on the active branch, never within
8 m, max 5 concurrent. Ambushes telegraph for 0.8 s (dust + growl). **Fairness rule: nothing
touches the train without a visible tell.**

---

## 9. Smoothness checklist (the specific engineering)

Frame consistency is a checklist, not a hope. Each item is an M4 build gate.

1. `Application.targetFrameRate = 60`, `QualitySettings.vSyncCount = 0` on mobile; no
   `Time.deltaTime` gameplay math (fixed step only).
2. IL2CPP + ARM64, Managed Stripping Medium, `Development Build` off, API Compatibility
   .NET Standard 2.1.
3. **GC Alloc reads 0 B** in a 30 s combat capture in the Profiler. Pools prewarmed at load:
   bullets 300, enemies 60, gore 700, orbs 80, digits 60, voices 24.
4. Shader variant collection preloaded → no hitch on a new effect's first shot.
5. Addressables preload stage 1 behind a 1.5 s splash; later stages load from memory in ~0 ms.
6. Shake and slow-mo are view-only and never touch the sim timestep.
7. Input applied in the same frame it is read — no buffering, no smoothing beyond one frame.
8. 30-minute thermal soak on device: no sustained drop below 55 fps.
9. Memory ceiling 300 MB RSS, 60 MB managed heap after a stage.
10. Device matrix: Redmi Note 9 class (low), Pixel 7a (mid), iPhone 12. **Feel is tuned on a
    phone, never in the editor.**

---

## 10. Testing & QA

- **EditMode (Core, ~1 s)**: wagon hitch at LV 5/10/15/20 and the 10-gun cap; slot 1 stays
  locked until an Extra Gun is spent; ammo type per gun; enemy behaviour ids (crawler
  ram-immunity, bulwark frontal immunity, glider slot disable, drillmaw burrow state);
  pierce stacking; burn
  ticks; gun-slot assignment when a wagon is added; spawn budget; save migration v1→v2;
  seeded determinism (same seed + input timeline → identical state hash at t=60 s).
- **PlayMode**: boot → clear stage 1 → reward → stage 2; death → retry restores meta; no
  missing references; no console errors.
- **Device smoke**: install APK, play 3 stages, capture `adb logcat` + frame-time graph; fail
  on any spike above 8 ms in the 30 s window.
- **Blind playtest gate** before any content work (see milestones).

---

## 11. Milestones

| M | Deliverable | Done when |
|---|---|---|
| **M0** ✅ | Unity project, asmdefs, Core sim skeleton, spline train moving/stopping, Cinemachine follow, one gun firing at a dummy | 60 fps in editor, EditMode tests green, train accelerates and brakes on touch |
| **M1** 🟡 | Enemies + pools, auto-target, HP, ram, blockades, progress bar, stage gate | One playable 90 s stage, winnable and losable |
| **M2** | **Juice pass**: hitstop, trauma, sparks, damage digits, orbs, flash, SFX, haptics | Blind playtest: unprompted "do that again" |
| **M3** | Cards, milestones (wagon LV 5/10/15/20, 2 gun slots), forks, 3 biomes, save/prestige, retry loop | 10-stage run end-to-end, zero soft-locks |
| **M4** | Android IL2CPP build, safe areas, §9 gates, CI | Installs on a phone, 55+ fps sustained on low-end |
| **M5** | Balance on real sessions, 24 cards, 10 bosses, the 10 new enemy types wired | First-time player reaches stage 20+ within 45 min |

M1 status: zombie hordes, auto-target, HP, ram, destructible blockades, XP gems, a real
HUD (stage/HP/XP bars from the art sheet), stage clear → 3 upgrade cards → next stage,
death → retry, and the generated art wired through both atlases. Still open for M1:
ambush telegraphs beyond the blockade warning, brake-charge UI, forks.

**Gate rule: no content (M3) until M2 passes the blind playtest.** Content cannot rescue feel.

---

## 12. Risks

| Risk | Mitigation |
|---|---|
| Stop-and-shoot feels like a chore | Ram + brake charge, measured at the M2 gate |
| Auto-aim removes skill | Perfect Brake, fork choices, the *when to stop* decision |
| Visual noise hides threats | §4.8 caps + nearest-enemy outline |
| Unity iteration is slow | Core asmdef tests, live tuning overlay, SO re-read per stage |
| Low-end Android drops frames | §9 gates at M4; quality tier auto-detect (particles, bloom, render scale) |
| Art bottleneck | v1 from primitives + Kenney CC0 3D kits; biomes = palette + 3 props |
| Scope creep | §5 cut list is contractual |

---

## 13. Open decisions

Resolved: portrait only ✅ · prestige at 30 ✅ · 3D low-poly + neon accents ✅ · no monetisation at v1 ✅.

Still open (carried into GDD v2 §11):

1. **Desert biome** — build as a v2.1 act with its own enemy set, or drop it?
2. **Extra Gun targeting** — player picks the wagon, or auto-fill the lowest-index locked slot?
3. **Boss cadence** — one per stage at beat 15, or dedicated boss stages between runs?
4. **Ten-gun budget** — 10 simultaneous guns against a 300-bullet cap; verify on low-end
   before committing to the 2-slot rule in code.

---

## 14. Immediate next steps

M0 is done and M1 shipped. What is actually next, in order:

1. **Encodings, not art**: wire `EnemyId` (13 types), `ProjectileId` (7 ammo), `bosses.json`
   phases and the 2-slot wagon struct into `TrainSurvivor.Core`, each with an EditMode test.
2. **HUD v2**: replace the XP label with the level chip (`hud_xp_level_row.png`), retire the
   TRAIN LV badge, add the wagon slot plaque.
3. **Spawn budget re-tune** for 13 enemy types (§8.3), then re-derive the TTK table.
4. Then the M2 juice gate: blind playtest before any more content.
