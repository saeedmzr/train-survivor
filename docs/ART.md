# Art pipeline — Train Survivor

The game's art arrives as three generated images in `images/`:

| file | contents | used for |
|---|---|---|
| `Gemini_Generated_Image_13c0os13c0os13c0.jpeg` | hero/mockup shot (UI baked in) | reference only |
| `Gemini_Generated_Image_20iy0i20iy0i20iy.jpeg` | 9 upgrade icons, banners, bars, warnings, flags, obstacles | HUD, cards, blockades |
| `Gemini_Generated_Image_20iy0i20iy0i20iy (1).jpeg` | zombie variants, collectibles, A/B markers | enemies, pickups |

The sheets are **not** hand-authored sprite sheets: they are raster images on flat grey
backgrounds, with captions and glows. `tools/slice_art.py` turns them into game assets.

## Running the slicer

```bash
/usr/bin/python3 tools/slice_art.py
```

Outputs:

* `images/sliced/*.png` — one trimmed, alpha-keyed PNG per sprite (review these; they are
  also the source of truth for future re-slicing).
* `unity/Assets/_Project/Art/Resources/Art/atlas_world.png` — 2048² atlas: enemies, gems,
  blockades (28 sprites).
* `unity/Assets/_Project/Art/Resources/Art/atlas_ui.png` — 2048² atlas: card icons,
  banners, bars, heart, flags (23 sprites).
* `unity/Assets/_Project/Scripts/Game/View/ArtAtlas.Generated.cs` — the sprite table
  (name → atlas rect + pivot) that `ArtSprites` reads at runtime.
* `tools/out/atlas_preview.png` — contact sheet for a quick visual check.

## How slicing works

1. Each sprite has a **generous cell** in the `SPRITES` table in `tools/slice_art.py`.
   Cells only need to avoid touching their neighbours; the slicer trims the rest.
2. The background is sampled **per crop** (the zombie sheet's interior is `#545454` while
   its corners are `#363636`, so a global key fails).
3. Alpha is a soft ramp on the distance from that background, then the sprite is tight
   trimmed with a 2 px pad.
4. A few sprites touch their neighbours on the sheet (the heart sits flush against the
   green bar). `CLEAN` lets a sprite drop pixels of the neighbour's hue after keying.
5. Sprites are shelf-packed into a power-of-two atlas and the C# table is regenerated.

Adding art: crop a new cell, add a line to `SPRITES`, re-run. Never hand-edit the
generated `.cs`.

## Painted enemy cards (v2)

Ten enemies + the three originals arrive as **cards** rather than sprite sheets:
`art-work/enemies/<id>/card.png`, each with the title and a stat panel baked in.

| Step | Tool | Output |
|---|---|---|
| cut the creature out | `tools/extract_enemy_art.py` | `sprite.png`, `sprite_hit.png`, `sprite_dead.png` |
| pack for Unity | `tools/pack_enemy_atlas.py` | `atlas_enemies.png` (1024²) + `EnemyArt.Generated.cs` |
| document | `tools/write_enemy_meta.py` | `meta.json` per enemy, `_catalog.json` |
| build prefabs | `EnemyPrefabBuilder` (editor) | `Prefabs/Enemies/<id>.prefab` |

The extractor keys on **detail density** (painted regions are high-contrast, the card's
smoke is not), keeps the blob nearest the frame centre, drops the title strip and the info
panel, then fills and feathers. Per-enemy overrides (crop box, top/panel cut, erase rects,
or a hand-authored polygon) live in `CONFIG` at the top of the script — `lantern` needs the
polygon because its hat overlaps the title.

Only the classic sheets go through `slice_art.py`; the card pipeline writes its own
atlases and never touches `ArtAtlas.Generated.cs`.

## Runtime

* `ArtSprites.Get(name)` → `Sprite` for UGUI (with optional 9-slice border).
* `ArtSprites.TryUv(name, out Vector4 uv)` → atlas UV rect for world billboards.
* `SpriteBillboard` draws one depth-tested, camera-facing quad with a per-instance UV
  rect, tint and hit-flash through a `MaterialPropertyBlock` (one shared material).
* `ArtTextureImport` (editor) forces the right import settings: clamp wrap, mipmaps,
  alphaIsTransparency, CompressedHQ, 2048 max.

## Sprite names

| group | names |
|---|---|
| enemies | `walker_0..3`, `walker_hit`, `walker_dead`, `sprinter_0..3`, `sprinter_hit`, `tank_0..1`, `tank_hit`, `tank_dead` |
| pickups | `gem_green`, `gem_blue`, `gem_purple`, `health_pack`, `crate`, `barrel_rust`, `barrel_toxic`, `currency` |
| blockades | `block_barrier`, `block_barrels`, `block_wreck`, `block_debris` |
| cards | `up_multishot`, `up_firetrail`, `up_spikebumper`, `up_piercing`, `up_boiler`, `up_rapidfire`, `up_incendiary`, `up_extragun`, `up_extrawagon` |
| HUD | `bar_frame`, `bar_fill_green`, `bar_fill_blue`, `icon_heart`, `banner_levelup`, `banner_victory`, `warn_0/1`, `flag_green/red/yellow`, `marker_a/b` |

The v2 content (10 new enemies, 10 bosses, 7 ammo types, 10 kill FX) is not sliced from
these sheets — it is generated as placeholder vector art by
`finalized-for-develop/meta/gen_expansion.py`, with a paint brief per creature in
`finalized-for-develop/data/*.json`. When painted sheets arrive, add their cells to
`SPRITES` here and keep the same ids.

Unused but sliced and available: `currency`, `marker_a/b`, `flag_*`, `block_debris`,
`crate`, `barrel_*` (scenery), `sprinter_dead`, `icon_blockade`.
