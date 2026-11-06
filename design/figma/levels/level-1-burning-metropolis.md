# LEVEL 1: BURNING METROPOLIS

- **Series label** — TRAIN SURVIVOR / GAMEPLAY FIELD GUIDE
- **Biome** — Ember-lit avenues. A broken city. One way out.
- **Board key** — 20 STATES  /  1,000 m  /  STATIC STORYBOARD
- **Board key** — IRONCLAD Mk I  ·  CANNON / GATLING / ROCKET / TESLA / FIRE / SHOTGUN
- **Board key** — READ LEFT → RIGHT · TOP ROW 01–10 · BOTTOM ROW 11–20
- **Field notes** — 01–05 NAVIGATE  /  06–09 SURVIVE  /  10–15 ENGAGE  /  16–20 EVOLVE / ■ HP / ARMOR / ■ XP / EVOLUTION / ■ DISTANCE / OBJECTIVE / BIOME 01 / SURVIVAL ATLAS
- **Figma section** — `2:11625` · 20 states

![board](boards/level-1-burning-metropolis.png)

Renders: `states/level-1-burning-metropolis/<NN>-<title>.png` (600×744 @2x). Machine-readable geometry: `figma-spec.json`.

| # | Phase | State | Beat | Input | HP | XP | Distance | Extra |
|---|-------|-------|------|-------|----|----|----------|-------|
| 01 | NAVIGATE | [Start line](states/level-1-burning-metropolis/01-start-line.png) | Full armor. Clear rails. Hold to depart. | HOLD | 100.0% | — | — |  |
| 02 | NAVIGATE | [Train moving](states/level-1-burning-metropolis/02-train-moving.png) | Hold input · wheels engage | HOLD | 100.0% | — | 7.0% |  |
| 03 | NAVIGATE | [A/B Track Split approaching](states/level-1-burning-metropolis/03-a-b-track-split-approaching.png) | Two routes appear ahead | HOLD | 100.0% | 5.0% | 14.0% | Track route markers |
| 04 | NAVIGATE | [Choosing left path (A)](states/level-1-burning-metropolis/04-choosing-left-path-a.png) | Route A · left branch locked | HOLD | 100.0% | 8.0% | 21.0% | Track route markers |
| 05 | NAVIGATE | [Choosing right path (B)](states/level-1-burning-metropolis/05-choosing-right-path-b.png) | Route B · alternate branch locked | HOLD | 100.0% | 8.0% | 25.0% | Track route markers |
| 06 | SURVIVE | [Enemies spawning in the distance](states/level-1-burning-metropolis/06-enemies-spawning-in-the-distance.png) | Contact · silhouettes at the horizon | HOLD | 100.0% | 12.0% | 31.0% |  |
| 07 | SURVIVE | [The Horde swarming the track](states/level-1-burning-metropolis/07-the-horde-swarming-the-track.png) | Track overrun · encirclement begins | HOLD | 91.0% | 18.0% | 37.0% |  |
| 08 | SURVIVE | [The player releasing the screen](states/level-1-burning-metropolis/08-the-player-releasing-the-screen.png) | Lift finger · coast to a stop | RELEASE | 88.0% | 20.0% | 40.0% |  |
| 09 | SURVIVE | [Train braking](states/level-1-burning-metropolis/09-train-braking.png) | Brake FX · metallic spark shower | AUTO | 85.0% | 23.0% | 42.0% |  |
| 10 | ENGAGE | [Turrets auto-targeting](states/level-1-burning-metropolis/10-turrets-auto-targeting.png) | Target acquisition · nearest hostile | AUTO | 83.0% | 26.0% | 44.0% |  |
| 11 | ENGAGE | [Turrets firing](states/level-1-burning-metropolis/11-turrets-firing.png) | Auto-fire · neon tracer volley | AUTO | 80.0% | 35.0% | 48.0% |  |
| 12 | ENGAGE | [An enemy taking damage](states/level-1-burning-metropolis/12-an-enemy-taking-damage.png) | White hit flash · impact confirmed | AUTO | 76.0% | 43.0% | 53.0% |  |
| 13 | ENGAGE | [Enemy defeat](states/level-1-burning-metropolis/13-enemy-defeat.png) | Slime burst · bones and ragdoll | AUTO | 72.0% | 54.0% | 58.0% |  |
| 14 | ENGAGE | [Defeating a fast enemy leaping onto the train](states/level-1-burning-metropolis/14-defeating-a-fast-enemy-leaping-onto-the-train.png) | Sprinter intercepted · airborne impact | AUTO | 66.0% | 63.0% | 64.0% |  |
| 15 | ENGAGE | [Encountering a giant boss enemy](states/level-1-burning-metropolis/15-encountering-a-giant-boss-enemy.png) | Boss encounter · heavy armor threat | AUTO | 60.0% | 69.0% | 71.0% | Boss health, Boss health track |
| 16 | EVOLVE | [Collecting XP gems](states/level-1-burning-metropolis/16-collecting-xp-gems.png) | Magnet pickup · gems stream into XP | AUTO | 60.0% | 92.0% | 79.0% |  |
| 17 | EVOLVE | [Hitting a physical barricade](states/level-1-burning-metropolis/17-hitting-a-physical-barricade.png) | Impact · forward motion blocked | AUTO | 41.0% | 94.0% | 84.0% |  |
| 18 | EVOLVE | [Destructible barricade destroyed by gunfire](states/level-1-burning-metropolis/18-destructible-barricade-destroyed-by-gunfire.png) | Sustained fire · obstacle breaks apart | AUTO | 41.0% | 97.0% | 91.0% |  |
| 19 | EVOLVE | [Level Complete](states/level-1-burning-metropolis/19-level-complete.png) | Red flag reached · route secured | READY | 41.0% | 100.0% | 100.0% | Completion banner |
| 20 | EVOLVE | [Level Up / Upgrade Screen](states/level-1-burning-metropolis/20-level-up-upgrade-screen.png) | Choose one · randomized upgrade reward | — | 41.0% | 100.0% | 100.0% | Distance HUD mini, HP XP row, XP track |

## State detail

### 01 · Start line

Full armor. Clear rails. Hold to depart.  
`figma node 2:11640` · render `states/level-1-burning-metropolis/01-start-line.png`
HUD: input `HOLD` · HP 100.0%

### 02 · Train moving

Hold input · wheels engage  
`figma node 2:11667` · render `states/level-1-burning-metropolis/02-train-moving.png`
HUD: input `HOLD` · HP 100.0% · distance 7.0%

### 03 · A/B Track Split approaching

Two routes appear ahead  
`figma node 2:11695` · render `states/level-1-burning-metropolis/03-a-b-track-split-approaching.png`
HUD: input `HOLD` · HP 100.0% · XP 5.0% · distance 14.0%
Elements: Track route markers (145×30)

### 04 · Choosing left path (A)

Route A · left branch locked  
`figma node 2:11729` · render `states/level-1-burning-metropolis/04-choosing-left-path-a.png`
HUD: input `HOLD` · HP 100.0% · XP 8.0% · distance 21.0%
Elements: Track route markers (145×30)

### 05 · Choosing right path (B)

Route B · alternate branch locked  
`figma node 2:11763` · render `states/level-1-burning-metropolis/05-choosing-right-path-b.png`
HUD: input `HOLD` · HP 100.0% · XP 8.0% · distance 25.0%
Elements: Track route markers (145×30)

### 06 · Enemies spawning in the distance

Contact · silhouettes at the horizon  
`figma node 2:11797` · render `states/level-1-burning-metropolis/06-enemies-spawning-in-the-distance.png`
HUD: input `HOLD` · HP 100.0% · XP 12.0% · distance 31.0%

### 07 · The Horde swarming the track

Track overrun · encirclement begins  
`figma node 2:11826` · render `states/level-1-burning-metropolis/07-the-horde-swarming-the-track.png`
HUD: input `HOLD` · HP 91.0% · XP 18.0% · distance 37.0%

### 08 · The player releasing the screen

Lift finger · coast to a stop  
`figma node 2:11855` · render `states/level-1-burning-metropolis/08-the-player-releasing-the-screen.png`
HUD: input `RELEASE` · HP 88.0% · XP 20.0% · distance 40.0%
On-screen copy: `RELEASE`

### 09 · Train braking

Brake FX · metallic spark shower  
`figma node 2:11885` · render `states/level-1-burning-metropolis/09-train-braking.png`
HUD: input `AUTO` · HP 85.0% · XP 23.0% · distance 42.0%

### 10 · Turrets auto-targeting

Target acquisition · nearest hostile  
`figma node 2:11914` · render `states/level-1-burning-metropolis/10-turrets-auto-targeting.png`
HUD: input `AUTO` · HP 83.0% · XP 26.0% · distance 44.0%

### 11 · Turrets firing

Auto-fire · neon tracer volley  
`figma node 2:11944` · render `states/level-1-burning-metropolis/11-turrets-firing.png`
HUD: input `AUTO` · HP 80.0% · XP 35.0% · distance 48.0%

### 12 · An enemy taking damage

White hit flash · impact confirmed  
`figma node 2:11973` · render `states/level-1-burning-metropolis/12-an-enemy-taking-damage.png`
HUD: input `AUTO` · HP 76.0% · XP 43.0% · distance 53.0%

### 13 · Enemy defeat

Slime burst · bones and ragdoll  
`figma node 2:12002` · render `states/level-1-burning-metropolis/13-enemy-defeat.png`
HUD: input `AUTO` · HP 72.0% · XP 54.0% · distance 58.0%

### 14 · Defeating a fast enemy leaping onto the train

Sprinter intercepted · airborne impact  
`figma node 2:12031` · render `states/level-1-burning-metropolis/14-defeating-a-fast-enemy-leaping-onto-the-train.png`
HUD: input `AUTO` · HP 66.0% · XP 63.0% · distance 64.0%

### 15 · Encountering a giant boss enemy

Boss encounter · heavy armor threat  
`figma node 2:12060` · render `states/level-1-burning-metropolis/15-encountering-a-giant-boss-enemy.png`
HUD: input `AUTO` · HP 60.0% · XP 69.0% · distance 71.0% · boss HP 82.0% (THE INFERNO · BOSS)
Elements: Boss health (190×33), Boss health track (178×6)
On-screen copy: `THE INFERNO · BOSS`

### 16 · Collecting XP gems

Magnet pickup · gems stream into XP  
`figma node 2:12093` · render `states/level-1-burning-metropolis/16-collecting-xp-gems.png`
HUD: input `AUTO` · HP 60.0% · XP 92.0% · distance 79.0%

### 17 · Hitting a physical barricade

Impact · forward motion blocked  
`figma node 2:12122` · render `states/level-1-burning-metropolis/17-hitting-a-physical-barricade.png`
HUD: input `AUTO` · HP 41.0% · XP 94.0% · distance 84.0%

### 18 · Destructible barricade destroyed by gunfire

Sustained fire · obstacle breaks apart  
`figma node 2:12151` · render `states/level-1-burning-metropolis/18-destructible-barricade-destroyed-by-gunfire.png`
HUD: input `AUTO` · HP 41.0% · XP 97.0% · distance 91.0%

### 19 · Level Complete

Red flag reached · route secured  
`figma node 2:12180` · render `states/level-1-burning-metropolis/19-level-complete.png`
HUD: input `READY` · HP 41.0% · XP 100.0% · distance 100.0%
Elements: Completion banner (230×71)
On-screen copy: `1,000 m SURVIVED · ROUTE SECURED` | `LEVEL COMPLETE` | `READY`

### 20 · Level Up / Upgrade Screen

Choose one · randomized upgrade reward  
`figma node 2:12212` · render `states/level-1-burning-metropolis/20-level-up-upgrade-screen.png`
HUD: HP 41.0% · XP 100.0% · distance 100.0%
Elements: Distance HUD mini (284×22), HP XP row (268×14), XP track (94×7), HP track (94×7), Tap choose label (160×22), Upgrade cards row (284×200), Upgrade card Spike Bumper (88×200), card-art-spike (80×120), Upgrade card Fire Trail (88×200), card-art-firetrail (80×120), Upgrade card Multishot (88×200), card-art-multishot (80×120), Level up header (280×46)
On-screen copy: `1000m` | `TAP CHOOSE` | `+35% ram DMG` | `SPIKE BUMPER` | `Burn on move` | `FIRE TRAIL` | `+2 projectiles` | `MULTISHOT` | `LEVEL UP!`
