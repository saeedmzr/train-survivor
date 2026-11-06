# LEVEL 3: TOXIC SWAMP

- **Series label** — TRAIN SURVIVOR / GAMEPLAY FIELD GUIDE
- **Biome** — Poison water. Hungry shadows. Burn a path through the mire.
- **Board key** — 24 STATES  /  1,000 m  /  STATIC STORYBOARD
- **Board key** — IRONCLAD Mk III  ·  CANNON / GATLING / ROCKET / TESLA / ACID / SHOTGUN
- **Board key** — READ LEFT → RIGHT · TOP ROW 01–10 · BOTTOM ROW 11–24 · +4 GROWTH MILESTONES
- **Field notes** — 01–05 NAVIGATE  /  06–09 SURVIVE  /  10–15 ENGAGE  /  16–20 EVOLVE  /  21–24 GROW / ■ HP / ARMOR / ■ XP / EVOLUTION / ■ DISTANCE / OBJECTIVE / ■ TRAIN LV / CONVOY / BIOME 03 / SURVIVAL ATLAS
- **Figma section** — `2:12903` · 24 states

![board](boards/level-3-toxic-swamp.png)

Renders: `states/level-3-toxic-swamp/<NN>-<title>.png` (600×744 @2x). Machine-readable geometry: `figma-spec.json`.

| # | Phase | State | Beat | Input | HP | XP | Distance | Extra |
|---|-------|-------|------|-------|----|----|----------|-------|
| 01 | NAVIGATE | [Start line](states/level-3-toxic-swamp/01-start-line.png) | Full armor. Clear rails. Hold to depart. | HOLD | 100.0% | — | — | Train Level badge |
| 02 | NAVIGATE | [Train moving](states/level-3-toxic-swamp/02-train-moving.png) | Hold input · wheels engage | HOLD | 100.0% | — | 7.0% |  |
| 03 | NAVIGATE | [A/B Track Split approaching](states/level-3-toxic-swamp/03-a-b-track-split-approaching.png) | Deceleration · brake sparks · two route panels appear | HOLD | 100.0% | 5.0% | 14.0% | Path B panel, Path A panel, Brake sparks overlay |
| 04 | NAVIGATE | [Choosing left path (A)](states/level-3-toxic-swamp/04-choosing-left-path-a.png) | Route A · +Health / −Speed · brake sparks | HOLD | 100.0% | 8.0% | 21.0% | Path B dim panel, Path A selected panel, Brake steam |
| 05 | NAVIGATE | [Choosing right path (B)](states/level-3-toxic-swamp/05-choosing-right-path-b.png) | Route B · alternate branch locked | HOLD | 100.0% | 8.0% | 25.0% | Track route markers, Brake steam 05, Path B selected panel 05 |
| 06 | SURVIVE | [Enemies spawning in the distance](states/level-3-toxic-swamp/06-enemies-spawning-in-the-distance.png) | Contact · silhouettes at the horizon | HOLD | 100.0% | 12.0% | 31.0% | Train Level milestone 05 |
| 07 | SURVIVE | [The Horde swarming the track](states/level-3-toxic-swamp/07-the-horde-swarming-the-track.png) | Track overrun · encirclement begins | HOLD | 91.0% | 18.0% | 37.0% |  |
| 08 | SURVIVE | [The player releasing the screen](states/level-3-toxic-swamp/08-the-player-releasing-the-screen.png) | Lift finger · coast to a stop | RELEASE | 88.0% | 20.0% | 40.0% |  |
| 09 | SURVIVE | [Train braking](states/level-3-toxic-swamp/09-train-braking.png) | Brake FX · metallic spark shower | AUTO | 85.0% | 23.0% | 42.0% |  |
| 10 | ENGAGE | [Turrets auto-targeting](states/level-3-toxic-swamp/10-turrets-auto-targeting.png) | Target acquisition · nearest hostile | AUTO | 83.0% | 26.0% | 44.0% |  |
| 11 | ENGAGE | [Turrets firing](states/level-3-toxic-swamp/11-turrets-firing.png) | Auto-fire · neon tracer volley | AUTO | 80.0% | 35.0% | 48.0% | Train Level milestone 10, Level row |
| 12 | ENGAGE | [An enemy taking damage](states/level-3-toxic-swamp/12-an-enemy-taking-damage.png) | White hit flash · impact confirmed | AUTO | 76.0% | 43.0% | 53.0% |  |
| 13 | ENGAGE | [Enemy defeat](states/level-3-toxic-swamp/13-enemy-defeat.png) | Slime burst · bones and ragdoll | AUTO | 72.0% | 54.0% | 58.0% |  |
| 14 | ENGAGE | [Defeating a fast enemy leaping onto the train](states/level-3-toxic-swamp/14-defeating-a-fast-enemy-leaping-onto-the-train.png) | Sprinter intercepted · airborne impact | AUTO | 66.0% | 63.0% | 64.0% |  |
| 15 | ENGAGE | [Encountering a giant boss enemy](states/level-3-toxic-swamp/15-encountering-a-giant-boss-enemy.png) | Boss encounter · heavy armor threat | AUTO | 60.0% | 69.0% | 71.0% | Boss health, Boss health track |
| 16 | EVOLVE | [Collecting XP gems](states/level-3-toxic-swamp/16-collecting-xp-gems.png) | Magnet pickup · gems stream into XP | AUTO | 60.0% | 92.0% | 79.0% |  |
| 17 | EVOLVE | [Hitting a physical barricade](states/level-3-toxic-swamp/17-hitting-a-physical-barricade.png) | Impact · forward motion blocked | AUTO | 41.0% | 94.0% | 84.0% |  |
| 18 | EVOLVE | [Destructible barricade destroyed by gunfire](states/level-3-toxic-swamp/18-destructible-barricade-destroyed-by-gunfire.png) | Sustained fire · obstacle breaks apart | AUTO | 41.0% | 97.0% | 91.0% |  |
| 19 | EVOLVE | [Level Complete](states/level-3-toxic-swamp/19-level-complete.png) | Red flag reached · route secured | READY | 41.0% | 100.0% | 100.0% | Completion banner |
| 20 | EVOLVE | [Level Up / Upgrade Screen](states/level-3-toxic-swamp/20-level-up-upgrade-screen.png) | Choose one · randomized upgrade reward | — | 41.0% | 100.0% | 100.0% |  |
| 21 | GROW | **Train Level 15 — Convoy Growth** *(no render)* | +Turret · +Wagon · camera zooms out · whole train visible | — | 73.5% | 78.4% | — | Player status HUD 21, Damage float 21, Distance HUD 21 |
| 22 | GROW | **Cinematic Slow-Mo Impact Event** *(no render)* | POW! burst · speed lines · glowing debris · zoomed-in camera | — | 53.9% | 58.8% | — | Player status HUD 22, Distance HUD 22, Cinematic damage float |
| 23 | GROW | **Train Level 20 — Ultimate Convoy** *(no render)* | 4 wagons · 4 turrets · neon underglow · max camera zoom | — | 88.2% | 94.1% | — | Player status HUD 23, Distance HUD 23, Damage float 23b |
| 24 | GROW | **Convoy Growth Reference Chart** *(no render)* | Thresholds · turret/wagon schedule · camera zoom progression | — | — | — | — | Growth table, Row 20, Row 15 |

## State detail

### 01 · Start line

Full armor. Clear rails. Hold to depart.  
`figma node 2:12918` · render `states/level-3-toxic-swamp/01-start-line.png`
HUD: input `HOLD` · HP 100.0%
Elements: Train Level badge (120×19)
On-screen copy: `· 1 TURRET` | `TRAIN LV`

### 02 · Train moving

Hold input · wheels engage  
`figma node 2:12945` · render `states/level-3-toxic-swamp/02-train-moving.png`
HUD: input `HOLD` · HP 100.0% · distance 7.0%

### 03 · A/B Track Split approaching

Deceleration · brake sparks · two route panels appear  
`figma node 2:12973` · render `states/level-3-toxic-swamp/03-a-b-track-split-approaching.png`
HUD: input `HOLD` · HP 100.0% · XP 5.0% · distance 14.0%
Elements: Path B panel (88×44), Path A panel (88×44), Brake sparks overlay (180×12), Track route markers (145×30)
On-screen copy: `+ HARDER FOES` | `+ WEAPON UP` | `PATH B` | `- SPEED` | `+ HEALTH` | `PATH A` | `✦ BRAKING ✦ SPARKS ✦ STEAM ✦`

### 04 · Choosing left path (A)

Route A · +Health / −Speed · brake sparks  
`figma node 2:13007` · render `states/level-3-toxic-swamp/04-choosing-left-path-a.png`
HUD: input `HOLD` · HP 100.0% · XP 8.0% · distance 21.0%
Elements: Path B dim panel (92×44), Path A selected panel (92×44), Brake steam (200×10), Track route markers (145×30)
On-screen copy: `+ HARDER FOES` | `+ WEAPON UP` | `PATH B` | `− SPEED PENALTY` | `+ HEALTH BONUS` | `PATH A ◀ LOCKED` | `〰 DECELERATING · STEAM VENTING`

### 05 · Choosing right path (B)

Route B · alternate branch locked  
`figma node 2:13041` · render `states/level-3-toxic-swamp/05-choosing-right-path-b.png`
HUD: input `HOLD` · HP 100.0% · XP 8.0% · distance 25.0%
Elements: Track route markers (145×30), Brake steam 05 (200×10), Path B selected panel 05 (92×44), Path A dim panel 05 (92×44)
On-screen copy: `〰 DECELERATING · STEAM VENTING` | `+ HARDER FOES` | `+ WEAPON UP` | `PATH B ◀ LOCKED` | `− SPEED` | `+ HEALTH` | `PATH A`

### 06 · Enemies spawning in the distance

Contact · silhouettes at the horizon  
`figma node 2:13075` · render `states/level-3-toxic-swamp/06-enemies-spawning-in-the-distance.png`
HUD: input `HOLD` · HP 100.0% · XP 12.0% · distance 31.0%
Elements: Train Level milestone 05 (160×19)
On-screen copy: `· +1 TURRET UNLOCKED` | `TRAIN LV`

### 07 · The Horde swarming the track

Track overrun · encirclement begins  
`figma node 2:13104` · render `states/level-3-toxic-swamp/07-the-horde-swarming-the-track.png`
HUD: input `HOLD` · HP 91.0% · XP 18.0% · distance 37.0%

### 08 · The player releasing the screen

Lift finger · coast to a stop  
`figma node 2:13133` · render `states/level-3-toxic-swamp/08-the-player-releasing-the-screen.png`
HUD: input `RELEASE` · HP 88.0% · XP 20.0% · distance 40.0%
On-screen copy: `RELEASE`

### 09 · Train braking

Brake FX · metallic spark shower  
`figma node 2:13163` · render `states/level-3-toxic-swamp/09-train-braking.png`
HUD: input `AUTO` · HP 85.0% · XP 23.0% · distance 42.0%

### 10 · Turrets auto-targeting

Target acquisition · nearest hostile  
`figma node 2:13192` · render `states/level-3-toxic-swamp/10-turrets-auto-targeting.png`
HUD: input `AUTO` · HP 83.0% · XP 26.0% · distance 44.0%

### 11 · Turrets firing

Auto-fire · neon tracer volley  
`figma node 2:13222` · render `states/level-3-toxic-swamp/11-turrets-firing.png`
HUD: input `AUTO` · HP 80.0% · XP 35.0% · distance 48.0%
Elements: Train Level milestone 10 (175×32), Level row (105×13)
On-screen copy: `↔ CAMERA ZOOMING OUT` | `· +1 WAGON` | `TRAIN LV`

### 12 · An enemy taking damage

White hit flash · impact confirmed  
`figma node 2:13251` · render `states/level-3-toxic-swamp/12-an-enemy-taking-damage.png`
HUD: input `AUTO` · HP 76.0% · XP 43.0% · distance 53.0%

### 13 · Enemy defeat

Slime burst · bones and ragdoll  
`figma node 2:13280` · render `states/level-3-toxic-swamp/13-enemy-defeat.png`
HUD: input `AUTO` · HP 72.0% · XP 54.0% · distance 58.0%

### 14 · Defeating a fast enemy leaping onto the train

Sprinter intercepted · airborne impact  
`figma node 2:13309` · render `states/level-3-toxic-swamp/14-defeating-a-fast-enemy-leaping-onto-the-train.png`
HUD: input `AUTO` · HP 66.0% · XP 63.0% · distance 64.0%

### 15 · Encountering a giant boss enemy

Boss encounter · heavy armor threat  
`figma node 2:13338` · render `states/level-3-toxic-swamp/15-encountering-a-giant-boss-enemy.png`
HUD: input `AUTO` · HP 60.0% · XP 69.0% · distance 71.0% · boss HP 82.0% (THE BLIGHT · BOSS)
Elements: Boss health (190×33), Boss health track (178×6)
On-screen copy: `THE BLIGHT · BOSS`

### 16 · Collecting XP gems

Magnet pickup · gems stream into XP  
`figma node 2:13371` · render `states/level-3-toxic-swamp/16-collecting-xp-gems.png`
HUD: input `AUTO` · HP 60.0% · XP 92.0% · distance 79.0%

### 17 · Hitting a physical barricade

Impact · forward motion blocked  
`figma node 2:13400` · render `states/level-3-toxic-swamp/17-hitting-a-physical-barricade.png`
HUD: input `AUTO` · HP 41.0% · XP 94.0% · distance 84.0%

### 18 · Destructible barricade destroyed by gunfire

Sustained fire · obstacle breaks apart  
`figma node 2:13429` · render `states/level-3-toxic-swamp/18-destructible-barricade-destroyed-by-gunfire.png`
HUD: input `AUTO` · HP 41.0% · XP 97.0% · distance 91.0%

### 19 · Level Complete

Red flag reached · route secured  
`figma node 2:13458` · render `states/level-3-toxic-swamp/19-level-complete.png`
HUD: input `READY` · HP 41.0% · XP 100.0% · distance 100.0%
Elements: Completion banner (230×71)
On-screen copy: `1,000 m SURVIVED · ROUTE SECURED` | `LEVEL COMPLETE` | `READY`

### 20 · Level Up / Upgrade Screen

Choose one · randomized upgrade reward  
`figma node 2:13490` · render `states/level-3-toxic-swamp/20-level-up-upgrade-screen.png`
HUD: HP 41.0% · XP 100.0% · distance 100.0%

### 21 · Train Level 15 — Convoy Growth

+Turret · +Wagon · camera zooms out · whole train visible  
`figma node 21:58` · *no render — node overflows the board frame and the image API returns null for it*
HUD: HP 73.5% · XP 78.4%
Elements: Player status HUD 21 (284×27), Damage float 21 (80×45), Distance HUD 21 (284×50), Train Level milestone 15 (185×32), Level row 15 (139×13)
On-screen copy: `−150` | `−300` | `↔ CAMERA WIDER — CONVOY VISIBLE` | `· +TURRET · +WAGON` | `TRAIN LV`

### 22 · Cinematic Slow-Mo Impact Event

POW! burst · speed lines · glowing debris · zoomed-in camera  
`figma node 21:93` · *no render — node overflows the board frame and the image API returns null for it*
HUD: HP 53.9% · XP 58.8%
Elements: Player status HUD 22 (284×27), Distance HUD 22 (284×50), Cinematic damage float (100×34), Slow mo label (130×16), POW burst container (180×120)
On-screen copy: `−450` | `◉ SLOW-MO IMPACT` | `POW!`

### 23 · Train Level 20 — Ultimate Convoy

4 wagons · 4 turrets · neon underglow · max camera zoom  
`figma node 21:136` · *no render — node overflows the board frame and the image API returns null for it*
HUD: HP 88.2% · XP 94.1%
Elements: Player status HUD 23 (284×27), Distance HUD 23 (284×50), Damage float 23b (70×17), Damage float 23a (70×22), Convoy schematic (284×15), Train Level milestone 20 (200×32), Level row 20 (135×13)
On-screen copy: `−150` | `−300` | `IRON PLATING` | `TURRETS×4` | `WAGON×4` | `ENGINE` | `↔↔ MAX ZOOM — FULL HORDE VISIBLE` | `· ULTIMATE CONVOY` | `TRAIN LV`

### 24 · Convoy Growth Reference Chart

Thresholds · turret/wagon schedule · camera zoom progression  
`figma node 21:180` · *no render — node overflows the board frame and the image API returns null for it*
Elements: Growth table (300×300), Row 20 (280×10), Row 15 (280×10), Row 10 (280×10), Row 05 (280×10), Row 01 (280×10), Table header (280×10), Overview title bar (300×45), Title stack (157×33)
On-screen copy: `★ = GROWTH MILESTONE · EVERY +5 LV ADDS TURRET · EVERY +10 LV ADDS WAGON` | `MAX` | `5 (+ACID)` | `5 (+NEW)` | `20 ★★` | `WIDER` | `4 (+TESLA)` | `4 (+NEW)` | `15 ★` | `WIDE` | `3 (+ROCKET)` | `3 (+NEW)`
