# AGENTS.md — how to use `finalized-for-develop/`

Read this before you touch art, UI or anything visual in Train Survivor. It tells you
what lives where, which numbers are authoritative, and which assets are traps.

## What this directory is

A dev-facing cut of the Figma field guide (`design/figma/`) plus the isolated sprites from
`images/sliced/`. Three kinds of thing:

1. **`screens/`** — 60 clean 1024×1024 gameplay frames, one per storyboard state, no HUD.
   Use these to match camera, scale, lighting and composition.
2. **`ui/`** — the HUD and level-up UI sliced out of the Figma art at native resolution.
   Use these to rebuild the interface pixel-accurately.
3. **`items/`** — alpha-keyed sprites (enemies, pickups, obstacles, icons, markers).
4. **`enemies/`, `bosses/`, `projectiles/`, `fx/`** — the v2 expansion: 10 new normal enemies,
   10 bosses, 7 ammo types and 10 kill effects, each with a design card, plus `data/*.json`
   (rosters, ammo→gun map, FX map, wagon rule) and a readable `BESTIARY.md`.

It is a **read-only library**. Art pipeline changes belong in `images/sliced/` +
`tools/slice_art.py`; HUD spec changes belong in Figma, then re-export. Regenerate this
folder with `python3 meta/rebuild.py` after either.

## Finding things (in order of speed)

1. `sheets/*.png` — one image per category, labelled. Fastest way to see what exists.
2. `INDEX.md` — the same thing as text, grouped, with sizes.
3. `meta/catalog.json` — machine index. Every asset has:
   `id, path, category, tags[], role, w, h, alpha, source, notes`.
   ```bash
   jq -r '.assets[] | select(.category=="items/enemies") | .path' meta/catalog.json
   jq -r '.assets[] | select(.tags|index("hud")) | .path + "  " + (.w|tostring) + "x" + (.h|tostring)' meta/catalog.json
   jq -r '.assets[] | select(.id|test("gem")) | .path + " — " + .role' meta/catalog.json
   ```
4. `find ui items -name '*pattern*'` — names are prefixed by purpose, see below.

## Layout

```
ui/hud/        10  hud_bar_hp_full, hud_fill_hp, hud_bar_xp_full, hud_fill_xp,
                   hud_distance_track, hud_distance_header, hud_heart, hud_flag,
                   hud_label_hp, hud_label_xp      (transparent, native px)
ui/cards/      14  up_card_<name>, up_card_row_mini, up_art_<name>, up_levelup_wordmark,
                   up_levelup_burst, up_tap_choose, up_arrow_left/right,
                   up_gauge_distance, up_instruction   (opaque, 1024-sheet crops)
ui/control/     4  control_hold / release / auto / ready  (2× card render, 64×82)
ui/hud/hud_xp_level_row.png    v2 XP bar with the level number in its left cap
ui/hud/hud_wagon_slots.png     wagon plaque: 2 gun slots, 1 filled, 1 locked
ui/reference_hud_layout.png    HUD composed over gameplay — spacing reference
enemies/en_*.png        10   new normal enemies (512², alpha, placeholder art)
enemies/cards/*.png     10   design cards: name, role, HP/SPD/DMG/XP, counters
bosses/boss_*.png       10   bosses (768², alpha, placeholder art)
bosses/cards/*.png      10   design cards: role, HP/phases/XP, phase strip, weakness
projectiles/proj_*.png  7    bullet, rocket, fireball, acid, tesla, pellets, bone
fx/fx_*.png             10   impact bursts + per-class death bursts
data/*.json             6    enemies, bosses, projectiles, fx, wagons, hud-v2
BESTIARY.md                  human-readable stats/phases sheet (from data/*.json)
items/enemies/ 16  walker_0..3, walker_hit/dead, sprinter_0..3, sprinter_hit/dead,
                   tank_0..1, tank_hit/dead   (RGBA, tight-trimmed)
items/pickups/  8  gem_green/blue/purple, health_pack, currency, crate, barrel_rust/tox
items/obstacles/4  block_barrier, block_barrels, block_wreck, block_debris
items/upgrade_icons/ 9  up_multishot, up_firetrail, up_spikebumper, up_piercing,
                   up_boiler, up_rapidfire, up_incendiary, up_extragun, up_extrawagon
items/markers/  7  marker_a, marker_b, flag_green/red/yellow, warn_0, warn_1
items/banners/  2  banner_levelup, banner_victory
items/hud_art/  4  bar_frame, bar_fill_green, bar_fill_blue, icon_heart
items/hero/     2  train_burning-metropolis (297×860), boss_inferno-giant (445×395)
screens/<biome>/20  1024×1024 clean frames, NN-state-title.png
biomes/<biome>/palette.png + palette.json colours in meta/palette.json
```

## Naming conventions

- `hud_*` HUD chrome, `up_*` level-up modal, `control_*` touch chip, `block_*` obstacle,
  `gem_*` XP gem, `flag_*` goal flag, `marker_*` route branch, `banner_*` full-screen.
- `_<state>` suffix on sprites means an animation/state frame: `_hit` = flash frame,
  `_dead` = death frame, `_0..3` = idle cycle.
- Screens keep the Figma state number: `07-the-horde-swarming-the-track.png`.

## HUD spec (authoritative numbers)

Design canvas is 300 × 300 for gameplay, 576 × 1024 for the level-up screen. Multiply the
300-space by `screenWidth / 300` for the device.

| element | design px | notes |
|---|---|---|
| status bar | 284 × 27, r5, `#0E1822` @ 94 % | 8 px inset from both sides, bottom-anchored |
| HP / XP track | 106 × 16, r4 | HP `#123617`, XP `#112B45` |
| HP / XP fill | 12 px tall, r2 | inset 2 px inside the track |
| bar row | HP label 10, track 38→144; XP label 155, track 183→289 | 11 px gap between bars |
| distance HUD | 284 × 50 at y 5 | fill 11 px tall, x 38 → 266 — **228 px is a full stage** |
| touch chip | 32 × 41, r4, ~92 % black | 253, 177 (moves to y 218 on the complete screen) |
| boss bar | 190 × 33 at 55, 54 | track 178 × 6, name 11 px above |
| upgrade card | 88 × 200, art 80 × 120 | 3-up row 284 wide, 6 px gutters |
| fill % | usable fill ÷ usable track | HP/XP 102 of 106, distance 228 of 284; matches the printed labels |

The bars are drawn as flat rounded rects in Figma but the shipped art uses a glossy
gradient — when in doubt copy `items/hud_art/bar_frame.png` + `bar_fill_*.png` (9-slice
ready, 564 × 99 and 564 × 99) rather than the Figma rect values.

## Biome colour scripts (from `meta/palette.json`)

- **level-3-toxic-swamp** — `#1D382C` · `#25362A` · `#192422` · `#1A202C` · `#273536` · `#302840` · `#0A1315` · `#1E3A27`
- **level-2-frozen-canyon** — `#2C6D8C` · `#1D5F8F` · `#0F3A73` · `#08152C` · `#1D538C` · `#0C3469` · `#091A41` · `#13356C`
- **level-1-burning-metropolis** — `#2E323C` · `#292934` · `#353137` · `#2D1F27` · `#454944` · `#8A4D37` · `#402E2F` · `#1F1A24`

## Expansion v2 — what was added

**10 normal enemies** (the existing walker / sprinter / tank stay untouched):

| Enemy | Hook |
|---|---|
| Crawler | flattens and slides under the cowcatcher — ram cannot kill it |
| Bloater | suicide bomber, 3 m blast that also hurts other enemies |
| Spitter | ranged acid lobber on the embankment, arcs over barricades |
| Shrieker | support: +15 % speed aura, calls a horde every 8 s |
| Bulwark | rail-sign plate, immune from the front — flank or burn it |
| Glider | ignores barricades, lands on a wagon and disables a gun slot |
| Drillmaw | burrows under the track and surfaces ahead of the train |
| Rustmites | six bodies, armour shred stacking, needs splash |
| The Lantern | necromancer, raises husks until you kill it |
| Rimeback | armour brute whose hit chills the throttle for 2 s |

Every one has `prompt` in `data/enemies.json` — hand it to the art pipeline (Gemini) to
replace the placeholder sprite; keep the id, palette and pivot.

**10 bosses**, each with 3 phases, a stated weakness and a telegraphed attack:
The Inferno, Railmaw, Slagheart, The Conductor (burning metropolis) · Glacier Maw,
Rime Engine, Iron Choir (frozen canyon) · Mireheart, Hollow Queen, The Nightmail
(toxic swamp). Phase thresholds, attacks and arena notes are in `data/bosses.json`,
tabulated in `BESTIARY.md`.

**Ammo / projectiles** — the guns no longer share one bullet sprite:

| Gun | Ammo | Type |
|---|---|---|
| CANNON, GATLING | `proj_bullet_tracer` | kinetic, pierce 1 |
| ROCKET | `proj_rocket` | blast, splash |
| FIRE | `proj_fireball` | fire, leaves burning ground |
| ACID | `proj_acid_glob` | acid, armour shred + pool |
| TESLA | `proj_tesla_arc` | electric, chains to a second target |
| SHOTGUN | `proj_shotgun_pellet` | kinetic, 5 pellets |
| — (enemy ranged) | `proj_bone_spike` | kinetic |

**Kill FX** — impact and death are separate lookups, not one poof: kinetic spark, fire
burst, blast ring, acid splash, electric burst; deaths split flesh/goo, metal, ice, bone
and swarm scatter. Mapping lives in `data/fx.json`.

**HUD v2 — level inside the XP bar.** The separate `TRAIN LV` badge is retired. The level
number now sits in a gold chip fused into the left cap of the XP track, with the XP fill
running underneath it: `ui/hud/hud_xp_level_row.png` (and `_lv15`), spec in
`data/hud-v2.json`. Same 106 × 16 track, chip 34 × 20, so the status bar keeps its width.

**Wagon gun rule.** Each wagon holds **at most 2 guns and starts with 1**; the second slot
is unlocked by spending an *Extra Gun* upgrade on that wagon. A 5-wagon convoy therefore
caps at 10 guns. Rule + unlock schedule in `data/wagons.json`, UI in
`ui/hud/hud_wagon_slots.png`.

**Art status.** The sprites in `enemies/` and `bosses/` are generated placeholders
(flat vector, in-palette) so the roster can be wired and tested now — they are not the
painted look of `screens/`. The design cards are the authoritative visual brief: silhouette,
palette, stats. Use `prompt` from `data/*.json` with the Gemini pipeline, drop the painted
PNG over the placeholder with the same filename and nothing else needs to change.

## Painted enemy art (`art-work/enemies/`)

The ten new enemies plus walker/sprinter/tank now have painted cards in
`art-work/enemies/<id>/card.png` (765 × 1024, AI-painted, title + stat panel baked in).
Everything downstream is generated from those cards:

```
art-work/enemies/<id>/card.png        the source card (never edit)
                     sprite.png       cutout, alpha, trimmed  ← the game sprite
                     sprite_hit.png   white-flash frame       (derived)
                     sprite_dead.png  flattened death frame    (derived)
                     meta.json        stats + art notes + unity paths for that enemy
art-work/enemies/_catalog.json        all 12 in one file
art-work/enemies/_extract.json        extraction report (coverage, source box)
```

Pipeline — run in this order after adding or replacing a card:

```bash
python3 tools/extract_enemy_art.py        # card -> sprite.png (+ hit/dead/preview)
python3 tools/pack_enemy_atlas.py         # sprite -> atlas_enemies.png + EnemyArt.Generated.cs
python3 tools/write_enemy_meta.py         # merge design + art + unity into meta.json
# then rebuild the prefabs (Unity):
~/Unity/Hub/Editor/6000.0.85f2/Editor/Unity -batchmode -quit -projectPath "$PWD/unity" \
  -executeMethod TrainSurvivor.EditorTools.EnemyPrefabBuilder.BuildFromBatch \
  -logFile "$PWD/unity/Logs/enemy_prefabs.log"
```

Unity side, already wired:

| Thing | Where |
|---|---|
| Atlas | `Art/Resources/Art/atlas_enemies.png` (1024², 12 sprites, names `en_<id>`) |
| Individual sprites | `Art/Sprites/Enemies/<id>.png` (imported as Sprite, bottom pivot) |
| Prefabs | `Prefabs/Enemies/<id>.prefab` — SpriteRenderer + `EnemyModel` (id, spriteName, heightM) |
| Runtime lookup | `ArtSprites.TryUv("en_crawler")`, `ArtSprites.AspectKey`, `SpriteBillboard.SetSprite("en_crawler", 1.15f)` |
| Sprite table | `Scripts/Game/View/EnemyArt.Generated.cs` (generated, do not edit) |

`ArtSprites` now merges three atlases (world, ui, enemies); `SpriteBillboard` picks the
right texture per sprite via `ArtSprites.AtlasKey`, so a Crawler billboard costs no extra
material.

Art notes worth knowing:

- Cutouts are side-on, so a billboard reads as a cardboard cut-out — same convention as the
  existing atlases, do not rotate these sprites in 3D.
- `lantern` uses a hand-authored polygon (`CONFIG["lantern"]["poly"]`) because its hat
  overlaps the title; the other 11 use the automatic detail-density mask.
- No art yet for **Rustmites** (the 13th enemy) and for the 10 bosses — their placeholder
  sprites in `enemies/` and `bosses/` remain the source of truth until cards arrive.
- The `rimback` folder maps to the roster id `rimeback`; keep that mapping in
  `tools/pack_enemy_atlas.py::GAME_ID`.

## Traps — read before cropping or compositing

1. **Screens still have baked UI in them.** A faint `ROUTE A/B` panel sits in the top-right
   corner of most frames, and the route-split frames (03–05) have painted-in A/B gate signs.
   Both are part of the artwork, not Figma layers. Mask the top-right corner or crop below
   y≈120 when using a screen as a full-bleed backdrop.
2. **`up_levelup_wordmark` is a raster on scene background** — the pixels behind the letters
   are blurred jungle/fire, not transparency. Use `items/banners/banner_levelup.png`
   (alpha) if you need the emblem to sit on a new background.
3. **`items/hero/*` are scene crops, not sprites.** Train and boss have no isolated art in
   any source file. They include rails/buildings behind them; use them as look-reference or
   cut them out by hand. Crop boxes are recorded in `meta/catalog.json` notes.
4. **No isolated turret art.** Cannon/gatling/rocket/tesla/acid/shotgun exist only as part
   of the train in the screens and as upgrade icons — do not invent new turret sprites.
5. **Don't upscale.** The smallest real assets are the 64×82 control chips (2× render).
   Anything smaller was cropped from a thumbnail and is not production art.
6. **Sprites are tint-free.** Biome variation comes from the palette + environment art, not
   from recolouring enemies — tinting breaks the painted lighting.

## Regenerating

```bash
python3 finalized-for-develop/meta/rebuild.py        # base kit (UI, screens, items)
python3 finalized-for-develop/meta/gen_expansion.py  # enemies, bosses, ammo, FX, HUD v2, data
python3 finalized-for-develop/meta/gen_bestiary.py   # BESTIARY.md from data/*.json
```

Run them in that order: `rebuild.py` recreates `meta/catalog.json` from the base sources and
`gen_expansion.py` merges the expansion back in and rewrites `INDEX.md`. `rebuild.py` reads
`design/figma/*` and `images/sliced/`; the expansion generator is self-contained. Crop boxes
for the UI sheets live in the `HUD_BOXES` / `LV_BOXES` dicts at the top of `rebuild.py`. Crop boxes for the UI sheets live in the `HUD_BOXES` / `LV_BOXES` dicts at the
top of the script — edit those, not the PNGs, if a crop is off.
