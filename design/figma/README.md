# Figma field guide → project

Everything from the Figma design file is pulled into this directory so design work can
continue against real data instead of screenshots of a canvas.

- **File** — `Untitled` · key `WCcq0Z67MX8Xuus3m5qVVa`
- **Link** — https://www.figma.com/design/WCcq0Z67MX8Xuus3m5qVVa/Untitled?node-id=2-11638
- **Version** — `2418708935468048979` (last modified 2026-11-06 10:44 UTC), exported 2026-11-06
- **Contents** — 3 biome boards, 64 storyboard states, 62 source images, 32 font/size pairs

Open `index.html` for a browsable contact sheet (board → state → render). Dev-ready
artefacts sliced from this export (HUD pieces, cards, sprites, clean screens) live in
[`finalized-for-develop/`](../../finalized-for-develop/AGENTS.md).

## Layout

```
design/figma/
  index.html              browsable contact sheet (open in a browser)
  figma-spec.json         full normalised tree: geometry, fills, text, HUD readings
  design-tokens.md        palette (area-weighted), type scale, HUD metrics
  levels/<biome>.md       one sheet per biome: state table + per-state detail
  boards/<biome>.png      whole board render
  states/<biome>/NN-*.png one render per state, 600×744 (2× the 300×372 card)
  assets/raw/*.png        original image fills (gameplay art), named by owning state
  assets/image-map.json   imageRef → file + owner
  raw/figma-file.json     untouched REST payload (1.5 MB) — nothing was dropped
```

## The boards

| Board | Slug | States | Loadout | Biome line |
|-------|------|--------|---------|------------|
| LEVEL 1: BURNING METROPOLIS | `level-1-burning-metropolis` | 20 | IRONCLAD Mk I — CANNON / GATLING / ROCKET / TESLA / FIRE / SHOTGUN | Ember-lit avenues. A broken city. One way out. |
| LEVEL 2: FROZEN CANYON | `level-2-frozen-canyon` | 20 | IRONCLAD Mk II — CANNON / GATLING / ROCKET / TESLA / FIRE / SHOTGUN | Subzero rails. Icebound predators. Keep the engine alive. |
| LEVEL 3: TOXIC SWAMP | `level-3-toxic-swamp` | 24 | IRONCLAD Mk III — CANNON / GATLING / ROCKET / TESLA / ACID / SHOTGUN | Poison water. Hungry shadows. Burn a path through the mire. |

Every board is the same 20-beat loop, split into phases by the file's own field notes:

```
01–05 NAVIGATE    start → accelerate → A/B split → pick a branch
06–09 SURVIVE     spawns → horde → release → brake
10–15 ENGAGE      target → fire → hit → kill → sprinter → boss
16–20 EVOLVE      XP gems → barricade → break-through → level complete → upgrade
21–24 GROW        convoy growth (level 3 only)
```

Level 3 adds four GROW states (train level milestones + a convoy growth reference
chart). See "Known gaps" — they exist in the file but not on the board.

## Reading `figma-spec.json`

```jsonc
{
  "source":  { "file", "key", "url", "version", "lastModified", "exportedAt" },
  "levels": [{
    "name", "id", "slug",
    "header": { "seriesLabel", "title", "biome", "boardKey": [...] },
    "fieldNotes": [...],
    "stateCount": 20,
    "states": [{
      "index", "id", "number", "title", "annotation", "renderName",
      "mockupBounds": {...},
      "frame": <node>          // 300×372 card, absolute child
    }],
    "node": <node>             // whole 3276×1060 board
  }],
  "typography": { "Inter 400 / 11.0px": 74, ... },
  "imageRefs": { "<ref>": 189, ... }
}
```

A `<node>` is `{id, type, name, x, y, w, h, fill[], stroke[], radius, opacity,
effects[], style{text,font,size,lineHeight,align}, children[]}` with coordinates
**relative to the node's frame** (cards start at 0,0). `fill` is a hex colour, a
gradient (`{gradient:"linear", stops:[...]}`) or `{image:"<ref>"}` — resolve refs via
`assets/image-map.json`.

Text is the primary source of truth: nothing was OCR'd, every string came out of the
file's own text nodes.

## HUD spec (as drawn)

All states share one HUD, laid out on the 300×300 square crop:

- status bar — 284×27, r5, `#0E1822` at 94 %, 8 px inset from both sides
- HP track `#123617` 106×16 r4, XP track `#112B45` 106×16 r4, 12 px fills r2
- distance HUD — 284×50 artwork, gradient progress fill 11 px tall, x 38 → 266 (228 px = full stage)
- touch control — 32×41 hand glyph, label toggles `HOLD` / `RELEASE` / `AUTO` / `READY`
- boss bar — 190×33, 178×6 track, name above (`THE INFERNO · BOSS`)
- upgrade screen — 3 cards 88×200 (80×120 art), `TAP CHOOSE`, 1000 m readout

Scale to device: multiply by `screenWidth / 300`. The HP/XP/distance columns in the
level sheets measure the drawn fills against their usable track (HP/XP 102 px inside a
106 px track, distance 228 px inside the 284 px distance strip), so 100 % really means a
full bar. These match the percentages printed inside the artwork — e.g. state 07 reads
`HP 091 % / XP 018 %` in the art and 91 % / 18 % in the tables.

## Known gaps

1. **States 21–24 (level 3 GROW) have no render.** They are laid out past the right
   edge of the storyboard row, so they are clipped by the frame and the image API
   returns `null` for them. Their text, HUD readings and geometry are all in
   `figma-spec.json` / `levels/level-3-toxic-swamp.md`; only the pixels are missing.
   Fix on the Figma side (move them into the row / widen the board) and re-pull.
2. **The level-3 board render is cropped after the two storyboard rows** — the part of
   Figma's cached render below them was a stale copy of that GROW strip and did not
   match the file.
3. `assets/raw` holds the 62 images the file actually uses. The file also carries ~315
   unused orphaned image refs from deleted layers; they were skipped on purpose.

Re-pull with (token never stored in the repo):

```bash
export FIGMA_TOKEN='<your figma token>'   # never commit it
curl -s -H "X-Figma-Token: $FIGMA_TOKEN" \
  "https://api.figma.com/v1/files/WCcq0Z67MX8Xuus3m5qVVa" -o raw/figma-file.json
curl -s -H "X-Figma-Token: $FIGMA_TOKEN" \
  "https://api.figma.com/v1/images/WCcq0Z67MX8Xuus3m5qVVa?ids=<node ids>&format=png&scale=2"
```

## Next

Nothing in `unity/` was touched. Natural next steps: rebuild the HUD from
`design-tokens.md` metric lines, then diff each state render against the current
build and turn the deltas into a task list.
