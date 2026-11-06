# Asset index — finalized for development

210 files. Browse visually first: `sheets/`.

| Sheet | Shows |
|---|---|
| `sheets/enemies.png` | the 10 new enemies |
| `sheets/bosses.png` | the 10 bosses |
| `sheets/projectiles.png` | the 7 ammo types |
| `sheets/fx.png` | impact + death bursts |
| `sheets/hud-v2.png` | new level/XP row, wagon slots |
| `sheets/ui.png` | HUD, cards, chips |
| `sheets/items.png` | legacy sprites |
| `sheets/screens-*.png` | all frames of one biome |

| I need | Path |
|---|---|
| HUD bars, heart, flag | `ui/hud/` |
| HUD spacing over gameplay | `ui/reference_hud_layout.png` |
| XP bar with the level in it (v2) | `ui/hud/hud_xp_level_row.png` |
| wagon gun slots (max 2) | `ui/hud/hud_wagon_slots.png` |
| upgrade cards + level-up modal | `ui/cards/` |
| touch chip per input state | `ui/control/` |
| NEW normal enemies (10) | `enemies/` |
| NEW enemy design cards | `enemies/cards/` |
| NEW bosses (10) | `bosses/` |
| NEW boss design cards | `bosses/cards/` |
| projectile ammo (7) | `projectiles/` |
| kill + impact FX (10) | `fx/` |
| game data: rosters, rules | `data/` |
| legacy sprites (walker/sprinter/tank...) | `items/` |
| full gameplay frames | `screens/` |
| biome palettes | `biomes/` |
| machine search | `meta/catalog.json` |

- `screens/level-1-burning-metropolis/` — 20 frames (`01-start-line.png` … `20-level-up-upgrade-screen.png`)
- `screens/level-2-frozen-canyon/` — 20 frames (`01-start-line.png` … `20-level-up-upgrade-screen.png`)
- `screens/level-3-toxic-swamp/` — 20 frames (`01-start-line.png` … `20-level-up-upgrade-screen.png`)

## ui/hud

| file | px | tags | role |
|---|---|---|---|
| `ui/hud/hud_bar_hp_full.png` | 648×76 | hud, bar | HP bar, full: dark track + green fill |
| `ui/hud/hud_bar_xp_full.png` | 653×77 | hud, bar | XP bar, full: dark track + blue fill |
| `ui/hud/hud_distance_header.png` | 407×47 | hud, icon | wordmark for the distance bar |
| `ui/hud/hud_distance_track.png` | 586×62 | hud, icon | empty distance track between heart and flag |
| `ui/hud/hud_fill_hp.png` | 602×67 | hud, bar | HP green fill only |
| `ui/hud/hud_fill_xp.png` | 489×68 | hud, bar | XP blue fill only |
| `ui/hud/hud_flag.png` | 94×88 | hud, icon | goal flag, right cap of the distance bar |
| `ui/hud/hud_heart.png` | 92×90 | hud, icon | HP icon, left cap of the distance bar |
| `ui/hud/hud_label_hp.png` | 132×76 | hud, icon | HP label glyph |
| `ui/hud/hud_label_xp.png` | 134×80 | hud, icon | XP label glyph |
| `ui/hud/hud_wagon_slots.png` | 360×140 | hud, wagon, slots | wagon with 2 gun slots, slot 1 filled, slot 2 locked |
| `ui/hud/hud_xp_level_row.png` | 560×96 | hud, xp, level | XP bar with the level number fused into its left cap |
| `ui/hud/hud_xp_level_row_lv15.png` | 560×96 | hud, xp, level | same row at LV 15 / 78 % |

## ui/cards

| file | px | tags | role |
|---|---|---|---|
| `ui/cards/up_arrow_left.png` | 124×122 | upgrade, modal | prev/next arrow button |
| `ui/cards/up_arrow_right.png` | 124×122 | upgrade, modal | prev/next arrow button, mirrored |
| `ui/cards/up_art_firetrail.png` | 130×130 | upgrade, modal | card art only, no frame |
| `ui/cards/up_art_multishot.png` | 130×130 | upgrade, modal | card art only, no frame |
| `ui/cards/up_art_spikebumper.png` | 130×130 | upgrade, modal | card art only, no frame |
| `ui/cards/up_card_firetrail.png` | 162×206 | upgrade, modal | full card: frame + label + art |
| `ui/cards/up_card_multishot.png` | 162×206 | upgrade, modal | full card: frame + label + art |
| `ui/cards/up_card_row_mini.png` | 404×188 | upgrade, modal | condensed 3-card row under the wordmark |
| `ui/cards/up_card_spikebumper.png` | 162×206 | upgrade, modal | full card: frame + label + art |
| `ui/cards/up_gauge_distance.png` | 146×140 | upgrade, modal | circular DISTANCE TO SURVIVE gauge |
| `ui/cards/up_instruction.png` | 435×48 | upgrade, modal | swipe/tap instruction line |
| `ui/cards/up_levelup_burst.png` | 518×142 | upgrade, modal | LEVEL UP! wordmark with its spark burst |
| `ui/cards/up_levelup_wordmark.png` | 404×92 | upgrade, modal | LEVEL UP! wordmark, letters plus outline |
| `ui/cards/up_tap_choose.png` | 196×54 | upgrade, modal | TAP CHOOSE prompt |

## ui/control

| file | px | tags | role |
|---|---|---|---|
| `ui/control/control_auto.png` | 68×86 | touch, input | touch-control chip in the AUTO state |
| `ui/control/control_hold.png` | 68×86 | touch, input | touch-control chip in the HOLD state |
| `ui/control/control_ready.png` | 68×86 | touch, input | touch-control chip in the READY state |
| `ui/control/control_release.png` | 68×116 | touch, input | touch-control chip in the RELEASE state |

## ui

| file | px | tags | role |
|---|---|---|---|
| `ui/reference_hud_layout.png` | 600×600 | reference, hud, layout | HUD composed over gameplay at design proportions (2x) |

## enemies

| file | px | tags | role |
|---|---|---|---|
| `enemies/en_bloater.png` | 512×512 | enemy, bloater | Bloater |
| `enemies/en_bulwark.png` | 512×512 | enemy, bulwark | Bulwark |
| `enemies/en_crawler.png` | 512×512 | enemy, crawler | Crawler |
| `enemies/en_drillmaw.png` | 512×512 | enemy, drillmaw | Drillmaw |
| `enemies/en_glider.png` | 512×512 | enemy, glider | Glider |
| `enemies/en_lantern.png` | 512×512 | enemy, lantern | The Lantern |
| `enemies/en_mites.png` | 512×512 | enemy, mites | Rustmites |
| `enemies/en_rimeback.png` | 512×512 | enemy, rimeback | Rimeback |
| `enemies/en_shrieker.png` | 512×512 | enemy, shrieker | Shrieker |
| `enemies/en_spitter.png` | 512×512 | enemy, spitter | Spitter |

## enemies/cards

| file | px | tags | role |
|---|---|---|---|
| `enemies/cards/card_bloater.png` | 640×906 | card, enemy, bloater | design card for Bloater |
| `enemies/cards/card_bulwark.png` | 640×906 | card, enemy, bulwark | design card for Bulwark |
| `enemies/cards/card_crawler.png` | 640×906 | card, enemy, crawler | design card for Crawler |
| `enemies/cards/card_drillmaw.png` | 640×906 | card, enemy, drillmaw | design card for Drillmaw |
| `enemies/cards/card_glider.png` | 640×906 | card, enemy, glider | design card for Glider |
| `enemies/cards/card_lantern.png` | 640×906 | card, enemy, lantern | design card for The Lantern |
| `enemies/cards/card_mites.png` | 640×906 | card, enemy, mites | design card for Rustmites |
| `enemies/cards/card_rimeback.png` | 640×906 | card, enemy, rimeback | design card for Rimeback |
| `enemies/cards/card_shrieker.png` | 640×906 | card, enemy, shrieker | design card for Shrieker |
| `enemies/cards/card_spitter.png` | 640×906 | card, enemy, spitter | design card for Spitter |

## bosses

| file | px | tags | role |
|---|---|---|---|
| `bosses/boss_glacier_maw.png` | 768×768 | boss, glacier_maw, frozen-canyon | Glacier Maw |
| `bosses/boss_hollow_queen.png` | 768×768 | boss, hollow_queen, toxic-swamp | Hollow Queen |
| `bosses/boss_iron_choir.png` | 768×768 | boss, iron_choir, frozen-canyon | Iron Choir |
| `bosses/boss_mireheart.png` | 768×768 | boss, mireheart, toxic-swamp | Mireheart |
| `bosses/boss_nightmail.png` | 768×768 | boss, nightmail, toxic-swamp | The Nightmail |
| `bosses/boss_railmaw.png` | 768×768 | boss, railmaw, burning-metropolis | Railmaw |
| `bosses/boss_rime_engine.png` | 768×768 | boss, rime_engine, frozen-canyon | Rime Engine |
| `bosses/boss_slagheart.png` | 768×768 | boss, slagheart, burning-metropolis | Slagheart |
| `bosses/boss_the_conductor.png` | 768×768 | boss, the_conductor, burning-metropolis | The Conductor |
| `bosses/boss_the_inferno.png` | 768×768 | boss, the_inferno, burning-metropolis | The Inferno |

## bosses/cards

| file | px | tags | role |
|---|---|---|---|
| `bosses/cards/card_glacier_maw.png` | 640×906 | card, boss, glacier_maw | design card for Glacier Maw |
| `bosses/cards/card_hollow_queen.png` | 640×906 | card, boss, hollow_queen | design card for Hollow Queen |
| `bosses/cards/card_iron_choir.png` | 640×906 | card, boss, iron_choir | design card for Iron Choir |
| `bosses/cards/card_mireheart.png` | 640×906 | card, boss, mireheart | design card for Mireheart |
| `bosses/cards/card_nightmail.png` | 640×906 | card, boss, nightmail | design card for The Nightmail |
| `bosses/cards/card_railmaw.png` | 640×906 | card, boss, railmaw | design card for Railmaw |
| `bosses/cards/card_rime_engine.png` | 640×906 | card, boss, rime_engine | design card for Rime Engine |
| `bosses/cards/card_slagheart.png` | 640×906 | card, boss, slagheart | design card for Slagheart |
| `bosses/cards/card_the_conductor.png` | 640×906 | card, boss, the_conductor | design card for The Conductor |
| `bosses/cards/card_the_inferno.png` | 640×906 | card, boss, the_inferno | design card for The Inferno |

## projectiles

| file | px | tags | role |
|---|---|---|---|
| `projectiles/proj_acid_glob.png` | 256×256 | projectile, acid, acid_glob | Acid glob |
| `projectiles/proj_bone_spike.png` | 256×256 | projectile, kinetic, bone_spike | Bone spike |
| `projectiles/proj_bullet_tracer.png` | 256×256 | projectile, kinetic, bullet_tracer | Tracer round |
| `projectiles/proj_fireball.png` | 256×256 | projectile, fire, fireball | Fireball |
| `projectiles/proj_rocket.png` | 256×256 | projectile, blast, rocket | Rocket |
| `projectiles/proj_shotgun_pellet.png` | 256×256 | projectile, kinetic, shotgun_pellet | Pellet spread |
| `projectiles/proj_tesla_arc.png` | 256×256 | projectile, electric, tesla_arc | Tesla arc |

## fx

| file | px | tags | role |
|---|---|---|---|
| `fx/fx_acid_splash.png` | 256×256 | fx, impact, acid_splash | acid splash |
| `fx/fx_blast_ring.png` | 256×256 | fx, impact, blast_ring | blast / rocket |
| `fx/fx_death_bone.png` | 256×256 | fx, death, death_bone | skeletal burst |
| `fx/fx_death_goo.png` | 256×256 | fx, death, death_goo | flesh / goo death |
| `fx/fx_death_ice.png` | 256×256 | fx, death, death_ice | frozen shatter |
| `fx/fx_death_metal.png` | 256×256 | fx, death, death_metal | armoured death |
| `fx/fx_death_swarm.png` | 256×256 | fx, death, death_swarm | swarm scatter |
| `fx/fx_electric_burst.png` | 256×256 | fx, impact, electric_burst | electric burst |
| `fx/fx_fire_burst.png` | 256×256 | fx, impact, fire_burst | fire burst |
| `fx/fx_impact_kinetic.png` | 256×256 | fx, impact, impact_kinetic | kinetic impact |

## data

| file | px | tags | role |
|---|---|---|---|
| `data/bosses.json` | — | data, bosses | boss roster with phases and attacks |
| `data/enemies.json` | — | data, enemies | machine-readable enemy roster |
| `data/fx.json` | — | data, fx | impact/death FX mapping |
| `data/hud-v2.json` | — | data, hud | HUD v2 change spec |
| `data/projectiles.json` | — | data, projectiles | projectile roster + gun mapping |
| `data/wagons.json` | — | data, rules | wagon gun-slot rule (max 2, start 1) |

## biomes

| file | px | tags | role |
|---|---|---|---|
| `biomes/level-1-burning-metropolis/palette.png` | 720×90 | palette, biome, l1 | area-weighted palette of LEVEL 1: BURNING METROPOLIS, from states 01/07/15/19 |
| `biomes/level-2-frozen-canyon/palette.png` | 720×90 | palette, biome, l2 | area-weighted palette of LEVEL 2: FROZEN CANYON, from states 01/07/15/19 |
| `biomes/level-3-toxic-swamp/palette.png` | 720×90 | palette, biome, l3 | area-weighted palette of LEVEL 3: TOXIC SWAMP, from states 01/07/15/19 |

## items/banners

| file | px | tags | role |
|---|---|---|---|
| `items/banners/banner_levelup.png` | 541×148 | sprite, banner, levelup, modal | LEVEL UP banner (full-screen) |
| `items/banners/banner_victory.png` | 540×148 | sprite, banner, victory, stage | VICTORY banner (full-screen) |

## items/enemies

| file | px | tags | role |
|---|---|---|---|
| `items/enemies/sprinter_0.png` | 244×266 | sprite, sprinter, enemy, idle, frame | sprinter zombie — fast leaper — idle cycle frame 0 |
| `items/enemies/sprinter_1.png` | 200×267 | sprite, sprinter, enemy, idle, frame | sprinter zombie — fast leaper — idle cycle frame 1 |
| `items/enemies/sprinter_2.png` | 219×257 | sprite, sprinter, enemy, idle, frame | sprinter zombie — fast leaper — idle cycle frame 2 |
| `items/enemies/sprinter_3.png` | 257×249 | sprite, sprinter, enemy, idle, frame | sprinter zombie — fast leaper — idle cycle frame 3 |
| `items/enemies/sprinter_dead.png` | 236×192 | sprite, sprinter, enemy, dead | sprinter zombie — fast leaper — death frame |
| `items/enemies/sprinter_hit.png` | 330×244 | sprite, sprinter, enemy, hit, flash | sprinter zombie — fast leaper — hit flash frame |
| `items/enemies/tank_0.png` | 301×346 | sprite, tank, enemy, idle, frame | tank zombie — armored heavy — idle cycle frame 0 |
| `items/enemies/tank_1.png` | 351×345 | sprite, tank, enemy, idle, frame | tank zombie — armored heavy — idle cycle frame 1 |
| `items/enemies/tank_dead.png` | 236×192 | sprite, tank, enemy, dead | tank zombie — armored heavy — death frame |
| `items/enemies/tank_hit.png` | 432×346 | sprite, tank, enemy, hit, flash | tank zombie — armored heavy — hit flash frame |
| `items/enemies/walker_0.png` | 233×326 | sprite, walker, enemy, idle, frame | walker zombie — slow shambler — idle cycle frame 0 |
| `items/enemies/walker_1.png` | 230×326 | sprite, walker, enemy, idle, frame | walker zombie — slow shambler — idle cycle frame 1 |
| `items/enemies/walker_2.png` | 182×321 | sprite, walker, enemy, idle, frame | walker zombie — slow shambler — idle cycle frame 2 |
| `items/enemies/walker_3.png` | 224×331 | sprite, walker, enemy, idle, frame | walker zombie — slow shambler — idle cycle frame 3 |
| `items/enemies/walker_dead.png` | 236×192 | sprite, walker, enemy, dead | walker zombie — slow shambler — death frame |
| `items/enemies/walker_hit.png` | 280×331 | sprite, walker, enemy, hit, flash | walker zombie — slow shambler — hit flash frame |

## items/hero

| file | px | tags | role |
|---|---|---|---|
| `items/hero/boss_inferno-giant.png` | 445×395 | reference, crop | the L1 boss creature (giant infected juggernaut) |
| `items/hero/train_burning-metropolis.png` | 297×860 | reference, crop | engine + full convoy, top-down rear view |

## items/hud_art

| file | px | tags | role |
|---|---|---|---|
| `items/hud_art/bar_fill_blue.png` | 309×36 | sprite, hud, 9slice, xp, fill | XP fill, 9-slice source |
| `items/hud_art/bar_fill_green.png` | 397×38 | sprite, hud, 9slice, hp, fill | HP fill, 9-slice source |
| `items/hud_art/bar_frame.png` | 564×99 | sprite, hud, 9slice, frame | bar frame, 9-slice source |
| `items/hud_art/icon_heart.png` | 83×86 | sprite, hud, icon, heart, hp | heart icon — HP |

## items/markers

| file | px | tags | role |
|---|---|---|---|
| `items/markers/flag_green.png` | 97×147 | sprite, flag, goal, green | goal flag, green |
| `items/markers/flag_red.png` | 94×148 | sprite, flag, goal, red | goal flag, red — stage end |
| `items/markers/flag_yellow.png` | 116×148 | sprite, flag, goal, yellow | goal flag, yellow — checkpoint |
| `items/markers/marker_a.png` | 115×126 | sprite, route, split, a | route A branch marker |
| `items/markers/marker_b.png` | 95×125 | sprite, route, split, b | route B branch marker |
| `items/markers/warn_0.png` | 152×138 | sprite, warn, hazard, alert | warning triangle |
| `items/markers/warn_1.png` | 152×138 | sprite, warn, hazard, alert | warning triangle, variant |

## items/obstacles

| file | px | tags | role |
|---|---|---|---|
| `items/obstacles/block_barrels.png` | 433×423 | sprite, barrels, blockade, ram | stacked barrel blockade |
| `items/obstacles/block_barrier.png` | 238×188 | sprite, barrier, blockade, ram | striped road barrier blockade |
| `items/obstacles/block_debris.png` | 301×140 | sprite, debris, blockade, ram | rubble pile blockade |
| `items/obstacles/block_wreck.png` | 750×471 | sprite, wreck, blockade, ram | wrecked car blockade |

## items/pickups

| file | px | tags | role |
|---|---|---|---|
| `items/pickups/barrel_rust.png` | 123×181 | sprite, barrel, prop, scenery | rusty barrel — scenery prop |
| `items/pickups/barrel_toxic.png` | 123×180 | sprite, barrel, hazard, prop | toxic barrel — hazard prop |
| `items/pickups/crate.png` | 168×172 | sprite, crate, prop, scenery | wooden crate — scenery / destructible prop |
| `items/pickups/currency.png` | 257×200 | sprite, currency, pickup, meta | soft currency — meta reward |
| `items/pickups/gem_blue.png` | 130×198 | sprite, gem, xp, pickup, blue | XP gem, blue — mid value |
| `items/pickups/gem_green.png` | 128×200 | sprite, gem, xp, pickup, green | XP gem, green |
| `items/pickups/gem_purple.png` | 120×196 | sprite, gem, xp, pickup, purple | XP gem, purple — high value |
| `items/pickups/health_pack.png` | 186×167 | sprite, health, pickup, heal | health pickup — restores HP |

## items/upgrade_icons

| file | px | tags | role |
|---|---|---|---|
| `items/upgrade_icons/up_boiler.png` | 217×217 | sprite, upgrade, train, power | upgrade: boiler (train power) |
| `items/upgrade_icons/up_extragun.png` | 217×217 | sprite, upgrade, turret, convoy | upgrade: extra gun (+turret slot) |
| `items/upgrade_icons/up_extrawagon.png` | 217×217 | sprite, upgrade, wagon, convoy | upgrade: extra wagon (+car slot) |
| `items/upgrade_icons/up_firetrail.png` | 217×217 | sprite, upgrade, fire, trail | upgrade: fire trail (burn on move) |
| `items/upgrade_icons/up_incendiary.png` | 217×217 | sprite, upgrade, fire, damage | upgrade: incendiary rounds |
| `items/upgrade_icons/up_multishot.png` | 217×217 | sprite, upgrade, weapon, multishot | upgrade: multishot (+projectiles) |
| `items/upgrade_icons/up_piercing.png` | 217×217 | sprite, upgrade, weapon, pierce | upgrade: piercing rounds |
| `items/upgrade_icons/up_rapidfire.png` | 217×217 | sprite, upgrade, weapon, rate | upgrade: rapid fire |
| `items/upgrade_icons/up_spikebumper.png` | 217×217 | sprite, upgrade, ram, spike | upgrade: spike bumper (ram damage) |

## screens/level-1-burning-metropolis

| file | px | tags | role |
|---|---|---|---|
| `screens/level-1-burning-metropolis/01-start-line.png` | 1024×1024 | screen, gameplay, 1 | clean Start line gameplay frame |
| `screens/level-1-burning-metropolis/02-train-moving.png` | 1024×1024 | screen, gameplay, 1 | clean Train moving gameplay frame |
| `screens/level-1-burning-metropolis/03-a-b-track-split-approaching.png` | 1024×1024 | screen, gameplay, 1 | clean A/B Track Split approaching gameplay frame |
| `screens/level-1-burning-metropolis/04-choosing-left-path-a.png` | 1024×1024 | screen, gameplay, 1 | clean Choosing left path (A) gameplay frame |
| `screens/level-1-burning-metropolis/05-choosing-right-path-b.png` | 1024×1024 | screen, gameplay, 1 | clean Choosing right path (B) gameplay frame |
| `screens/level-1-burning-metropolis/06-enemies-spawning-in-the-distance.png` | 1024×1024 | screen, gameplay, 1 | clean Enemies spawning in the distance gameplay frame |
| `screens/level-1-burning-metropolis/07-the-horde-swarming-the-track.png` | 1024×1024 | screen, gameplay, 1 | clean The Horde swarming the track gameplay frame |
| `screens/level-1-burning-metropolis/08-the-player-releasing-the-screen.png` | 1024×1024 | screen, gameplay, 1 | clean The player releasing the screen gameplay frame |
| `screens/level-1-burning-metropolis/09-train-braking.png` | 1024×1024 | screen, gameplay, 1 | clean Train braking gameplay frame |
| `screens/level-1-burning-metropolis/10-turrets-auto-targeting.png` | 1024×1024 | screen, gameplay, 1 | clean Turrets auto-targeting gameplay frame |
| `screens/level-1-burning-metropolis/11-turrets-firing.png` | 1024×1024 | screen, gameplay, 1 | clean Turrets firing gameplay frame |
| `screens/level-1-burning-metropolis/12-an-enemy-taking-damage.png` | 1024×1024 | screen, gameplay, 1 | clean An enemy taking damage gameplay frame |
| `screens/level-1-burning-metropolis/13-enemy-defeat.png` | 1024×1024 | screen, gameplay, 1 | clean Enemy defeat gameplay frame |
| `screens/level-1-burning-metropolis/14-defeating-a-fast-enemy-leaping-onto-the-train.png` | 1024×1024 | screen, gameplay, 1 | clean Defeating a fast enemy leaping onto the train gameplay frame |
| `screens/level-1-burning-metropolis/15-encountering-a-giant-boss-enemy.png` | 1024×1024 | screen, gameplay, 1 | clean Encountering a giant boss enemy gameplay frame |
| `screens/level-1-burning-metropolis/16-collecting-xp-gems.png` | 1024×1024 | screen, gameplay, 1 | clean Collecting XP gems gameplay frame |
| `screens/level-1-burning-metropolis/17-hitting-a-physical-barricade.png` | 1024×1024 | screen, gameplay, 1 | clean Hitting a physical barricade gameplay frame |
| `screens/level-1-burning-metropolis/18-destructible-barricade-destroyed-by-gunfire.png` | 1024×1024 | screen, gameplay, 1 | clean Destructible barricade destroyed by gunfire gameplay frame |
| `screens/level-1-burning-metropolis/19-level-complete.png` | 1024×1024 | screen, gameplay, 1 | clean Level Complete gameplay frame |
| `screens/level-1-burning-metropolis/20-level-up-upgrade-screen.png` | 1024×1024 | screen, gameplay, 1 | clean Level Up / Upgrade Screen gameplay frame |

## screens/level-2-frozen-canyon

| file | px | tags | role |
|---|---|---|---|
| `screens/level-2-frozen-canyon/01-start-line.png` | 1024×1024 | screen, gameplay, 2 | clean Start line gameplay frame |
| `screens/level-2-frozen-canyon/02-train-moving.png` | 1024×1024 | screen, gameplay, 2 | clean Train moving gameplay frame |
| `screens/level-2-frozen-canyon/03-a-b-track-split-approaching.png` | 1024×1024 | screen, gameplay, 2 | clean A/B Track Split approaching gameplay frame |
| `screens/level-2-frozen-canyon/04-choosing-left-path-a.png` | 1024×1024 | screen, gameplay, 2 | clean Choosing left path (A) gameplay frame |
| `screens/level-2-frozen-canyon/05-choosing-right-path-b.png` | 1024×1024 | screen, gameplay, 2 | clean Choosing right path (B) gameplay frame |
| `screens/level-2-frozen-canyon/06-enemies-spawning-in-the-distance.png` | 1024×1024 | screen, gameplay, 2 | clean Enemies spawning in the distance gameplay frame |
| `screens/level-2-frozen-canyon/07-the-horde-swarming-the-track.png` | 1024×1024 | screen, gameplay, 2 | clean The Horde swarming the track gameplay frame |
| `screens/level-2-frozen-canyon/08-the-player-releasing-the-screen.png` | 1024×1024 | screen, gameplay, 2 | clean The player releasing the screen gameplay frame |
| `screens/level-2-frozen-canyon/09-train-braking.png` | 1024×1024 | screen, gameplay, 2 | clean Train braking gameplay frame |
| `screens/level-2-frozen-canyon/10-turrets-auto-targeting.png` | 1024×1024 | screen, gameplay, 2 | clean Turrets auto-targeting gameplay frame |
| `screens/level-2-frozen-canyon/11-turrets-firing.png` | 1024×1024 | screen, gameplay, 2 | clean Turrets firing gameplay frame |
| `screens/level-2-frozen-canyon/12-an-enemy-taking-damage.png` | 1024×1024 | screen, gameplay, 2 | clean An enemy taking damage gameplay frame |
| `screens/level-2-frozen-canyon/13-enemy-defeat.png` | 1024×1024 | screen, gameplay, 2 | clean Enemy defeat gameplay frame |
| `screens/level-2-frozen-canyon/14-defeating-a-fast-enemy-leaping-onto-the-train.png` | 1024×1024 | screen, gameplay, 2 | clean Defeating a fast enemy leaping onto the train gameplay frame |
| `screens/level-2-frozen-canyon/15-encountering-a-giant-boss-enemy.png` | 1024×1024 | screen, gameplay, 2 | clean Encountering a giant boss enemy gameplay frame |
| `screens/level-2-frozen-canyon/16-collecting-xp-gems.png` | 1024×1024 | screen, gameplay, 2 | clean Collecting XP gems gameplay frame |
| `screens/level-2-frozen-canyon/17-hitting-a-physical-barricade.png` | 1024×1024 | screen, gameplay, 2 | clean Hitting a physical barricade gameplay frame |
| `screens/level-2-frozen-canyon/18-destructible-barricade-destroyed-by-gunfire.png` | 1024×1024 | screen, gameplay, 2 | clean Destructible barricade destroyed by gunfire gameplay frame |
| `screens/level-2-frozen-canyon/19-level-complete.png` | 1024×1024 | screen, gameplay, 2 | clean Level Complete gameplay frame |
| `screens/level-2-frozen-canyon/20-level-up-upgrade-screen.png` | 1024×1024 | screen, gameplay, 2 | clean Level Up / Upgrade Screen gameplay frame |

## screens/level-3-toxic-swamp

| file | px | tags | role |
|---|---|---|---|
| `screens/level-3-toxic-swamp/01-start-line.png` | 1024×1024 | screen, gameplay, 3 | clean Start line gameplay frame |
| `screens/level-3-toxic-swamp/02-train-moving.png` | 1024×1024 | screen, gameplay, 3 | clean Train moving gameplay frame |
| `screens/level-3-toxic-swamp/03-a-b-track-split-approaching.png` | 1024×1024 | screen, gameplay, 3 | clean A/B Track Split approaching gameplay frame |
| `screens/level-3-toxic-swamp/04-choosing-left-path-a.png` | 1024×1024 | screen, gameplay, 3 | clean Choosing left path (A) gameplay frame |
| `screens/level-3-toxic-swamp/05-choosing-right-path-b.png` | 1024×1024 | screen, gameplay, 3 | clean Choosing right path (B) gameplay frame |
| `screens/level-3-toxic-swamp/06-enemies-spawning-in-the-distance.png` | 1024×1024 | screen, gameplay, 3 | clean Enemies spawning in the distance gameplay frame |
| `screens/level-3-toxic-swamp/07-the-horde-swarming-the-track.png` | 1024×1024 | screen, gameplay, 3 | clean The Horde swarming the track gameplay frame |
| `screens/level-3-toxic-swamp/08-the-player-releasing-the-screen.png` | 1024×1024 | screen, gameplay, 3 | clean The player releasing the screen gameplay frame |
| `screens/level-3-toxic-swamp/09-train-braking.png` | 1024×1024 | screen, gameplay, 3 | clean Train braking gameplay frame |
| `screens/level-3-toxic-swamp/10-turrets-auto-targeting.png` | 1024×1024 | screen, gameplay, 3 | clean Turrets auto-targeting gameplay frame |
| `screens/level-3-toxic-swamp/11-turrets-firing.png` | 1024×1024 | screen, gameplay, 3 | clean Turrets firing gameplay frame |
| `screens/level-3-toxic-swamp/12-an-enemy-taking-damage.png` | 1024×1024 | screen, gameplay, 3 | clean An enemy taking damage gameplay frame |
| `screens/level-3-toxic-swamp/13-enemy-defeat.png` | 1024×1024 | screen, gameplay, 3 | clean Enemy defeat gameplay frame |
| `screens/level-3-toxic-swamp/14-defeating-a-fast-enemy-leaping-onto-the-train.png` | 1024×1024 | screen, gameplay, 3 | clean Defeating a fast enemy leaping onto the train gameplay frame |
| `screens/level-3-toxic-swamp/15-encountering-a-giant-boss-enemy.png` | 1024×1024 | screen, gameplay, 3 | clean Encountering a giant boss enemy gameplay frame |
| `screens/level-3-toxic-swamp/16-collecting-xp-gems.png` | 1024×1024 | screen, gameplay, 3 | clean Collecting XP gems gameplay frame |
| `screens/level-3-toxic-swamp/17-hitting-a-physical-barricade.png` | 1024×1024 | screen, gameplay, 3 | clean Hitting a physical barricade gameplay frame |
| `screens/level-3-toxic-swamp/18-destructible-barricade-destroyed-by-gunfire.png` | 1024×1024 | screen, gameplay, 3 | clean Destructible barricade destroyed by gunfire gameplay frame |
| `screens/level-3-toxic-swamp/19-level-complete.png` | 1024×1024 | screen, gameplay, 3 | clean Level Complete gameplay frame |
| `screens/level-3-toxic-swamp/20-level-up-upgrade-screen.png` | 1024×1024 | screen, gameplay, 3 | clean Level Up / Upgrade Screen gameplay frame |

