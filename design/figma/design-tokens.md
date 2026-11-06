# Design tokens extracted from Figma

Palette is area-weighted across all three boards (bigger surfaces rank higher). Type list is every font/size pair actually used, with occurrence count.

## Colour

| Hex | Approx. area (px²) |
|-----|--------------------|
| `#10151B` | 23397688 |
| `#0E1822` | 483350 |
| `#F2F4EE` | 436161 |
| `#93A3AD` | 333865 |
| `#FFFFFF` | 240581 |
| `#101820` | 190384 |
| `#071118` | 189000 |
| `#123617` | 106848 |
| `#112B45` | 106848 |
| `#0A0F14` | 90000 |
| `#EDFD78` | 68531 |
| `#0F1721` | 25408 |
| `#B8F36A` | 23623 |
| `#14241A` | 17600 |
| `#26170A` | 17600 |
| `#1A0F29` | 17600 |
| `#FFAB61` | 14684 |
| `#70DEFF` | 14154 |

## Type

| Font / weight / size | Uses |
|----------------------|------|
| Inter 400 / 11.0px | 74 |
| IBM Plex Mono 600 / 15.0px | 64 |
| Inter 700 / 14.0px | 64 |
| Inter 700 / 6.0px | 57 |
| IBM Plex Mono 400 / 8.0px | 27 |
| Inter 800 / 16.0px | 18 |
| IBM Plex Mono 400 / 7.0px | 17 |
| Inter 400 / 7.0px | 12 |
| IBM Plex Mono 700 / 8.0px | 11 |
| Inter 400 / 13.0px | 6 |
| IBM Plex Mono 400 / 11.0px | 6 |
| IBM Plex Mono 700 / 10.0px | 5 |
| IBM Plex Mono 400 / 12.0px | 3 |
| Barlow Condensed 700 / 44.0px | 3 |
| IBM Plex Mono 400 / 14.0px | 3 |
| Inter 400 / 12.0px | 3 |
| Inter 800 / 9.0px | 3 |
| Barlow Condensed 800 / 27.0px | 3 |
| Inter 400 / 9.0px | 3 |
| Barlow Condensed 800 / 11.0px | 3 |
| Inter 600 / 8.0px | 3 |
| Barlow Condensed 900 / 28.0px | 2 |
| Barlow Condensed 800 / 12.0px | 2 |
| IBM Plex Mono 400 / 9.0px | 1 |
| Barlow Condensed 800 / 22.0px | 1 |
| Barlow Condensed 700 / 16.0px | 1 |
| Barlow Condensed 900 / 58.0px | 1 |
| Barlow Condensed 800 / 18.0px | 1 |
| Barlow Condensed 700 / 14.0px | 1 |
| Barlow Condensed 700 / 18.0px | 1 |
| Barlow Condensed 700 / 15.0px | 1 |
| IBM Plex Mono 400 / 6.0px | 1 |

## HUD metrics (300 × 300 mockup canvas)

| Element | Size | Inset |
|---------|------|-------|
| HUD backdrop (status bar) | 284 × 27, r5 | 8 px from each side |
| HP / XP track | 106 × 16, r4 | inside backdrop |
| HP / XP fill | 12 px tall, r2 | 2 px inside track (102 px = 100 %) |
| Distance HUD artwork | 284 × 50 | top, 8 px inset |
| Distance fill | 11 px tall | 228 px = full stage (x 38 → 266) |
| Touch control | 32 × 41 | bottom-right, 24 px hand icon |
| Boss bar | 190 × 33, track 178 × 6 | top centre |
| Upgrade card | 88 × 200 (art 80 × 120) | 3-up row, 284 wide |

Scale to the device: the mockup is a square crop of a portrait frame; multiply by (screenWidth / 300) to get real pixels.
