# Bestiary & combat sheet

Generated from `data/*.json` — that is the authoritative copy, this is the readable one.

## Normal enemies (10 new)

| # | Enemy | Role | HP | SPD | DMG | XP | Armour | Pack |
|---|-------|------|----|-----|-----|----|--------|------|
| 01 | **Crawler** — rails | low quadruped that slides under the cowcatcher — ram misses, shoot it | 22 | 4.4 | 4 | 4 | 0 | 3–5 |
| 02 | **Bloater** — rails | bloated suicide bomber — detonates on contact for area damage | 60 | 2.2 | 18 | 12 | 1 | 1–2 |
| 03 | **Spitter** — embankment | ranged acid lobber on the embankment — arcs globs over barricades | 45 | 2.8 | 7 | 10 | 0 | 1–3 |
| 04 | **Shrieker** — rear | support screamer — buffs nearby enemies and calls the horde | 70 | 2.6 | 0 | 15 | 0 | 1 |
| 05 | **Bulwark** — rails | carries a rail-sign plate — frontally immune, hit it from the side | 110 | 2.4 | 12 | 18 | 3 | 1–2 |
| 06 | **Glider** — air | ragged-wing leaper — vaults barricades and lands on a wagon | 35 | 7.0 | 9 | 9 | 0 | 2–4 |
| 07 | **Drillmaw** — underground | burrower — tunnels under the track and surfaces ahead of the train | 90 | 3.0 | 15 | 16 | 2 | 1 |
| 08 | **Rustmites** — rails | swarm of six — chews armour, needs splash to clear | 6 | 5.2 | 2 | 1 | 0 | 6 |
| 09 | **The Lantern** — rear | necromancer — raises fallen zombies as husks until killed | 85 | 2.0 | 6 | 20 | 0 | 1 |
| 10 | **Rimeback** — rails | armoured brute — its hit chills the throttle for 2 s | 140 | 1.8 | 16 | 22 | 4 | 1 |

### 01 · Crawler

*low quadruped that slides under the cowcatcher — ram misses, shoot it*  

- **Stats** — HP 22 · speed 4.4 m/s · damage 4 · XP 4 · armour 0 · threat **low**
- **Lane** — rails · spawns in packs of 3–5
- lowers to 0.25 m tall when the train is within 8 m
- slides under the cowcatcher — ram does nothing
- sprints straight down the rails, ignores barricades
- **Counters** — any bullet, shotgun close range, splash
- Assets — `enemies/en_crawler.png` · card `enemies/cards/card_crawler.png`

### 02 · Bloater

*bloated suicide bomber — detonates on contact for area damage*  

- **Stats** — HP 60 · speed 2.2 m/s · damage 18 · XP 12 · armour 1 · threat **medium**
- **Lane** — rails · spawns in packs of 1–2
- fuse glows 1.2 s before detonation
- explodes on contact OR on death — 3 m radius
- blast also damages other enemies
- **Counters** — kill it early (chain reaction), rocket from range
- Assets — `enemies/en_bloater.png` · card `enemies/cards/card_bloater.png`

### 03 · Spitter

*ranged acid lobber on the embankment — arcs globs over barricades*  

- **Stats** — HP 45 · speed 2.8 m/s · damage 7 · XP 10 · armour 0 · threat **medium**
- **Lane** — embankment · spawns in packs of 1–3
- stops at 25 m and lobs acid globs in an arc over barricades
- repositions when the train brakes
- globs leave a pool for 2 s
- **Counters** — shoot at range, ram it before it settles, brake to break its lead
- Assets — `enemies/en_spitter.png` · card `enemies/cards/card_spitter.png`

### 04 · Shrieker

*support screamer — buffs nearby enemies and calls the horde*  

- **Stats** — HP 70 · speed 2.6 m/s · damage 0 · XP 15 · armour 0 · threat **medium**
- **Lane** — rear · spawns in packs of 1
- aura: +15 % movement speed to enemies within 12 m
- screams every 8 s, spawning 6 walkers
- scream is a 1.5 s telegraph you can hear
- **Counters** — focus it first, rocket into the spawn point
- Assets — `enemies/en_shrieker.png` · card `enemies/cards/card_shrieker.png`

### 05 · Bulwark

*carries a rail-sign plate — frontally immune, hit it from the side*  

- **Stats** — HP 110 · speed 2.4 m/s · damage 12 · XP 18 · armour 3 · threat **high**
- **Lane** — rails · spawns in packs of 1–2
- carries a rail sign plate: immune to damage from the front cone (±50°)
- turns slowly — flank or let it pass
- plate breaks under sustained fire (600 dmg) and stays broken
- **Counters** — bullets that curve in from a side turret, ram from the side, fire DoT ignores the plate
- Assets — `enemies/en_bulwark.png` · card `enemies/cards/card_bulwark.png`

### 06 · Glider

*ragged-wing leaper — vaults barricades and lands on a wagon*  

- **Stats** — HP 35 · speed 7.0 m/s · damage 9 · XP 9 · armour 0 · threat **medium**
- **Lane** — air · spawns in packs of 2–4
- ignores barricades and rails, flies in a sine wave
- lands on a wagon and chews a gun slot (3 s disable)
- cannot be rammed while airborne
- **Counters** — tesla chain, shotgun as it lands, kill before it reaches the convoy
- Assets — `enemies/en_glider.png` · card `enemies/cards/card_glider.png`

### 07 · Drillmaw

*burrower — tunnels under the track and surfaces ahead of the train*  

- **Stats** — HP 90 · speed 3.0 m/s · damage 15 · XP 16 · armour 2 · threat **high**
- **Lane** — underground · spawns in packs of 1
- burrows at 5 m/s, untargetable, dust trail visible
- surfaces 8 m ahead of the train
- on surfacing it lunges 6 m — hit it during the 0.6 s wind-up
- **Counters** — time the brake, watch the dust trail, splash at the surface point
- Assets — `enemies/en_drillmaw.png` · card `enemies/cards/card_drillmaw.png`

### 08 · Rustmites

*swarm of six — chews armour, needs splash to clear*  

- **Stats** — HP 6 · speed 5.2 m/s · damage 2 · XP 1 · armour 0 · threat **low**
- **Lane** — rails · spawns in packs of 6
- six bodies, one spawn
- each bite stacks armour shred (−10 % for 5 s)
- spread around the train instead of attacking one point
- **Counters** — splash (rocket, fire trail), shotgun, ram kills one per hit
- Assets — `enemies/en_mites.png` · card `enemies/cards/card_mites.png`

### 09 · The Lantern

*necromancer — raises fallen zombies as husks until killed*  

- **Stats** — HP 85 · speed 2.0 m/s · damage 6 · XP 20 · armour 0 · threat **high**
- **Lane** — rear · spawns in packs of 1
- raises one dead enemy every 6 s as a husk at 50 % HP
- husks collapse when it dies
- lamp glow lights the area — it is always visible in fog
- **Counters** — rush it, piercing rounds through the husks, fire trail denies the corpse line
- Assets — `enemies/en_lantern.png` · card `enemies/cards/card_lantern.png`

### 10 · Rimeback

*armoured brute — its hit chills the throttle for 2 s*  

- **Stats** — HP 140 · speed 1.8 m/s · damage 16 · XP 22 · armour 4 · threat **high**
- **Lane** — rails · spawns in packs of 1
- on hit: chills the throttle (−35 % for 2 s)
- ice armour cracks after 3 hits, then takes double damage
- slow turn, wide sweep — the ram is a real option
- **Counters** — fire (melts armour fastest), ram then shoot the cracked plates
- Assets — `enemies/en_rimeback.png` · card `enemies/cards/card_rimeback.png`

## Bosses (10)

| # | Boss | Biome | HP | Phases | Reward |
|---|------|-------|----|--------|--------|
| 01 | **The Inferno** — Giant infected juggernaut: molten seams open in its chest between attacks. | burning-metropolis | 2400 | 3 | +1 wagon slot, 400 XP |
| 02 | **Railmaw** — A worm that lives under the ballast and wears the track as teeth. | burning-metropolis | 1900 | 3 | repair kit, 350 XP |
| 03 | **Slagheart** — Walking furnace: the grate is the weak point, and it knows it. | burning-metropolis | 2600 | 3 | fire card, 450 XP |
| 04 | **The Conductor** — Undead stationmaster with a signal lamp; calls trains that were never built. | burning-metropolis | 2100 | 3 | card choice ×2, 500 XP |
| 05 | **Glacier Maw** — A jaw the size of a tunnel, with icicle teeth that regrow every phase. | frozen-canyon | 3000 | 3 | tesla coil, 550 XP |
| 06 | **Rime Engine** — A train like yours, grown over with ice and aimed the wrong way. | frozen-canyon | 3200 | 3 | +1 gun slot per wagon, 600 XP |
| 07 | **Mireheart** — Fungal colossus; its cap breathes spore clouds that hide the horde. | toxic-swamp | 2800 | 3 | acid turret, 500 XP |
| 08 | **Hollow Queen** — A sac on legs that births swarmlings until you break the eggs. | toxic-swamp | 2500 | 3 | rapid fire, 480 XP |
| 09 | **Iron Choir** — Three bell-torsos on one frame: kill them in order or the dead one sings. | frozen-canyon | 3600 | 3 | tesla + rocket, 700 XP |
| 10 | **The Nightmail** — A ghost locomotive that phases through the swamp — and through you. | toxic-swamp | 4200 | 3 | nightmail skin + 900 XP |

### 01 · The Inferno

*Giant infected juggernaut: molten seams open in its chest between attacks.*  

- **Biome** — burning-metropolis · **HP** 2400 · **arena** straight avenue, 2 barricade lanes
- **Entry** — stand-off at 30 m; it walks, never runs
- **Weakness** — chest vent only in phase 2+
- **Tell** — every attack has a 0.8 s wind-up you can brake out of
- **Reward** — +1 wagon slot, 400 XP

**Phase 1 — Molten Walk** (from 100 % HP)
- sweeping arm slam (2.5 m)
- ember spit: 3 slow fireballs

**Phase 2 — Vent** (from 55 % HP)
- chest vents open for 6 s — weak point ×3
- lava trail follows its feet

**Phase 3 — Overheat** (from 25 % HP)
- enraged: double slam, embers become a ring
- arena starts burning from the edges

Assets — `bosses/boss_the_inferno.png` · card `bosses/cards/card_the_inferno.png`

### 02 · Railmaw

*A worm that lives under the ballast and wears the track as teeth.*  

- **Biome** — burning-metropolis · **HP** 1900 · **arena** long straight, rails twist
- **Entry** — burrows ahead, surfaces behind the train
- **Weakness** — teeth ring: shoot the mouth during the coil
- **Tell** — it never attacks from the front — watch the sides
- **Reward** — repair kit, 350 XP

**Phase 1 — Coil** (from 100 % HP)
- bursts from the ballast beside the track
- rail-shard volley, 5 shards in a fan

**Phase 2 — Squeeze** (from 60 % HP)
- coils across the track, blocks the lane for 4 s
- telegraph: rails bend before it rises

**Phase 3 — Collapse** (from 30 % HP)
- digs under and takes the track with it — forced brake
- spawns 4 crawlers from the hole

Assets — `bosses/boss_railmaw.png` · card `bosses/cards/card_railmaw.png`

### 03 · Slagheart

*Walking furnace: the grate is the weak point, and it knows it.*  

- **Biome** — burning-metropolis · **HP** 2600 · **arena** furnace yard, floor vents
- **Entry** — strides in from a side street, slower than it looks
- **Weakness** — the grate
- **Tell** — slag rain is telegraphed by the chimney glowing white
- **Reward** — fire card, 450 XP

**Phase 1 — Bellows** (from 100 % HP)
- stomp shockwave (4 m)
- vent bursts: 4 flame jets on a timer

**Phase 2 — Stoke** (from 50 % HP)
- opens the grate core — ×4 damage for 4 s, then it slams shut
- chimney drops slag rain

**Phase 3 — Meltdown** (from 20 % HP)
- core exposed permanently but it chases at double speed
- leaves a lava pool wherever it stands

Assets — `bosses/boss_slagheart.png` · card `bosses/cards/card_slagheart.png`

### 04 · The Conductor

*Undead stationmaster with a signal lamp; calls trains that were never built.*  

- **Biome** — burning-metropolis · **HP** 2100 · **arena** station platform, two switches
- **Entry** — stands on the platform; the fight starts when the signal turns red
- **Weakness** — lamp staff arm — break it to stop the blind
- **Tell** — the semaphore never lies: red means move
- **Reward** — card choice ×2, 500 XP

**Phase 1 — Timetable** (from 100 % HP)
- semaphore arm signals which lane is safe
- summons a ghost train that runs the opposite lane

**Phase 2 — Delay** (from 60 % HP)
- lamp staff blinds a gun slot for 4 s
- husks rise from the platform

**Phase 3 — Last Call** (from 25 % HP)
- switches the tracks under you — forced A/B pick
- attacks twice as fast, stops signalling

Assets — `bosses/boss_the_conductor.png` · card `bosses/cards/card_the_conductor.png`

### 05 · Glacier Maw

*A jaw the size of a tunnel, with icicle teeth that regrow every phase.*  

- **Biome** — frozen-canyon · **HP** 3000 · **arena** frozen canyon, ice walls
- **Entry** — blocks the canyon mouth; you have to break in
- **Weakness** — between plates at the jaw hinge
- **Tell** — frost breath is a 1 s inhale — brake out of the cone
- **Reward** — tesla coil, 550 XP

**Phase 1 — Bite** (from 100 % HP)
- lunges the width of the track
- frost breath cone that freezes the nearest turret

**Phase 2 — Shell** (from 60 % HP)
- ice plates: 3 plates, 1 breaks per 400 damage
- falling icicles from the ceiling

**Phase 3 — Thaw** (from 25 % HP)
- armour gone, speed doubled
- the floor becomes slick — braking is halved

Assets — `bosses/boss_glacier_maw.png` · card `bosses/cards/card_glacier_maw.png`

### 06 · Rime Engine

*A train like yours, grown over with ice and aimed the wrong way.*  

- **Biome** — frozen-canyon · **HP** 3200 · **arena** head-on duel on a single track
- **Entry** — the mirror fight: it comes at you on your own rails
- **Weakness** — the headlight — shoot it to blind its aim
- **Tell** — it mirrors your speed: brake and it overshoots
- **Reward** — +1 gun slot per wagon, 600 XP

**Phase 1 — Head-on** (from 100 % HP)
- rams — the only way out is a side branch
- side turrets fire in sync with yours

**Phase 2 — Boiler Burst** (from 60 % HP)
- vents steam that hides the track for 3 s
- drops ice caltrops behind it

**Phase 3 — Derail** (from 25 % HP)
- loses a wheel, drifts across both lanes
- turret fire becomes a continuous beam

Assets — `bosses/boss_rime_engine.png` · card `bosses/cards/card_rime_engine.png`

### 07 · Mireheart

*Fungal colossus; its cap breathes spore clouds that hide the horde.*  

- **Biome** — toxic-swamp · **HP** 2800 · **arena** swamp clearing, water on both sides
- **Entry** — rooted at first; it pulls itself free at 70 %
- **Weakness** — the stalk under the cap
- **Tell** — spore clouds do not damage — they hide; shoot blind into them
- **Reward** — acid turret, 500 XP

**Phase 1 — Rooted** (from 100 % HP)
- spore clouds: 4 clouds that hide enemies
- tendril sweep across the track

**Phase 2 — Uproot** (from 70 % HP)
- walks on roots, spawns 2 spitters per 10 s
- acid rain pools on the rails

**Phase 3 — Bloom** (from 30 % HP)
- cap bursts: 8 spore mines
- tendrils grab the train and hold it for 2 s

Assets — `bosses/boss_mireheart.png` · card `bosses/cards/card_mireheart.png`

### 08 · Hollow Queen

*A sac on legs that births swarmlings until you break the eggs.*  

- **Biome** — toxic-swamp · **HP** 2500 · **arena** nest of egg sacs on the track
- **Entry** — the nest is already on the track; she is inside it
- **Weakness** — eggs — they are the real health bar
- **Tell** — she cannot move; the danger is the swarm
- **Reward** — rapid fire, 480 XP

**Phase 1 — Brood** (from 100 % HP)
- births 3 swarmlings every 5 s
- shell hardens while birthing (×0.5 damage)

**Phase 2 — Rupture** (from 60 % HP)
- sac bursts — 6 mites, shell softens ×1.5
- web slows the train in a 10 m patch

**Phase 3 — Last Eggs** (from 30 % HP)
- guards 3 eggs; each egg killed removes a swarm wave
- body attacks with 4 arms

Assets — `bosses/boss_hollow_queen.png` · card `bosses/cards/card_hollow_queen.png`

### 09 · Iron Choir

*Three bell-torsos on one frame: kill them in order or the dead one sings.*  

- **Biome** — frozen-canyon · **HP** 3600 · **arena** bridge with three bell towers
- **Entry** — three torsos on one frame, singing in tune
- **Weakness** — the chain links between torsos (breaks the sync)
- **Tell** — listen for which bell rings first
- **Reward** — tesla + rocket, 700 XP

**Phase 1 — Three Voices** (from 100 % HP)
- each torso has its own attack pattern: left shockwave, middle beam, right swarm
- kill order is left → right → middle

**Phase 2 — Discord** (from 60 % HP)
- a dead torso keeps attacking for 8 s unless the next is killed fast
- bells ring: screen shake, aim drifts

**Phase 3 — Silence** (from 20 % HP)
- last torso absorbs the others: bigger, faster, all three patterns
- the frame collapses, arena narrows

Assets — `bosses/boss_iron_choir.png` · card `bosses/cards/card_iron_choir.png`

### 10 · The Nightmail

*A ghost locomotive that phases through the swamp — and through you.*  

- **Biome** — toxic-swamp · **HP** 4200 · **arena** flooded swamp line, ghost rails
- **Entry** — it phases through the trees and arrives behind you
- **Weakness** — the headlight lanterns — break both to stop the phasing
- **Tell** — the illusion never touches the rails
- **Reward** — nightmail skin + 900 XP

**Phase 1 — Phantom** (from 100 % HP)
- phases out for 2 s, untargetable, then reappears beside the train
- lantern beams that burn the nearest gun slot

**Phase 2 — Twin** (from 65 % HP)
- splits into two: one is an illusion — the real one casts a shadow
- ghost rails force an A/B choice mid-fight

**Phase 3 — Terminus** (from 30 % HP)
- stops phasing, opens both boilers
- chain lightning across the wagons, 3 s telegraph

Assets — `bosses/boss_nightmail.png` · card `bosses/cards/card_nightmail.png`

## Ammo → gun mapping

| Gun | Ammo | Damage type |
|---|---|---|
| CANNON | Tracer round (`projectiles/proj_bullet_tracer.png`) | kinetic |
| GATLING | Tracer round (`projectiles/proj_bullet_tracer.png`) | kinetic |
| ROCKET | Rocket (`projectiles/proj_rocket.png`) | blast |
| TESLA | Tesla arc (`projectiles/proj_tesla_arc.png`) | electric |
| FIRE | Fireball (`projectiles/proj_fireball.png`) | fire |
| SHOTGUN | Pellet spread (`projectiles/proj_shotgun_pellet.png`) | kinetic |
| ACID | Acid glob (`projectiles/proj_acid_glob.png`) | acid |

Enemy ranged attacks fire `bone_spike` (spitter, conductor, queen) unless noted.

## Kill & impact FX

| Damage type | Impact | | Enemy | Death FX |
|---|---|---|---|---|
| `kinetic` | `impact_kinetic` || `walker` | `death_goo` |
| `fire` | `fire_burst` || `sprinter` | `death_bone` |
| `blast` | `blast_ring` || `tank` | `death_metal` |
| `acid` | `acid_splash` || `crawler` | `death_goo` |
| `electric` | `electric_burst` || `bloater` | `blast_ring` |
| | || `spitter` | `acid_splash` |
| | || `shrieker` | `death_bone` |
| | || `bulwark` | `death_metal` |
| | || `glider` | `death_goo` |
| | || `drillmaw` | `death_goo` |
| | || `mites` | `death_swarm` |
| | || `lantern` | `death_bone` |
| | || `rimeback` | `death_ice` |

## Wagon gun rule

- Each wagon holds **at most 2 guns** and arrives with **1**.
- Second slot is unlocked by spending an *Extra Gun* upgrade on that wagon.
- Unlock schedule — wagon 1 @ LV 1, wagon 2 @ LV 5, wagon 3 @ LV 10, wagon 4 @ LV 15, wagon 5 @ LV 20
- Consequence for balance: a 5-wagon convoy caps at 10 guns, not 5 — gun count is no longer the convoy size, so turret DPS can scale without adding wagons.

