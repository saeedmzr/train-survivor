#!/usr/bin/env python3
"""Rebuild finalized-for-develop/: sliced UI, item library, screens, palettes, catalog, sheets, INDEX.

Run from anywhere:  python3 finalized-for-develop/meta/rebuild.py
Reads design/figma/* and images/sliced/*; overwrites everything except AGENTS.md.
Adjust the crop boxes in HUD_BOXES / LV_BOXES, not the PNGs.
"""
import json, os, shutil, math
from PIL import Image, ImageDraw, ImageFont

PROJ = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(PROJ, "design/figma")
RAW = os.path.join(SRC, "raw/figma-file.json")
SPEC = json.load(open(os.path.join(SRC, "figma-spec.json")))
IMGMAP = json.load(open(os.path.join(SRC, "assets/image-map.json")))
OUT = os.path.join(PROJ, "finalized-for-develop")
SLICED = os.path.join(PROJ, "images/sliced")

raw = json.load(open(RAW))
CATALOG = []


def find(n, tid):
    if n.get("id") == tid:
        return n
    for c in n.get("children", []):
        r = find(c, tid)
        if r:
            return r


def ensure(*p):
    d = os.path.join(OUT, *p)
    os.makedirs(d, exist_ok=True)
    return d


def add(path, category, tags, role, source, transparency=False, notes=""):
    im = Image.open(path)
    CATALOG.append({
        "id": os.path.splitext(os.path.basename(path))[0],
        "path": os.path.relpath(path, OUT),
        "category": category,
        "tags": tags,
        "role": role,
        "w": im.size[0],
        "h": im.size[1],
        "alpha": transparency or im.mode in ("RGBA", "LA"),
        "source": source,
        "notes": notes,
    })


def key_out(im, bg, thr=8.0, ramp=18.0):
    im = im.convert("RGB")
    px = im.load()
    out = Image.new("RGBA", im.size)
    op = out.load()
    for y in range(im.size[1]):
        for x in range(im.size[0]):
            r, g, b = px[x, y]
            d = math.sqrt((r - bg[0]) ** 2 + (g - bg[1]) ** 2 + (b - bg[2]) ** 2)
            a = 0 if d <= thr else (255 if d >= ramp else int(255 * (d - thr) / (ramp - thr)))
            op[x, y] = (r, g, b, a)
    return out


def crop(src, box, dest, alpha_bg=None, pad=0):
    im = Image.open(src).convert("RGB")
    x0, y0, x1, y1 = box
    c = im.crop((max(0, x0), max(0, y0), min(im.size[0], x1), min(im.size[1], y1)))
    if alpha_bg is not None:
        c = key_out(c, alpha_bg)
    c.save(dest)
    return dest


def bg_of(im):
    px = im.convert("RGB").load()
    w, h = im.size
    pts = [(2, 2), (w - 3, 2), (2, h - 3), (w - 3, h - 3)]
    vals = [px[p] for p in pts]
    return tuple(sorted(v[i] for v in vals)[1] for i in range(3))


# ---------------------------------------------------------------- 1. UI: HUD sheet
HUD = os.path.join(SRC, "assets/raw/level-3-toxic-swamp__01-start-line__c65e402d.png")
hud_bg = (24, 29, 33)
ui_hud = ensure("ui", "hud")
HUD_BOXES = {
    "hud_distance_header": (305, 52, 712, 99, "wordmark for the distance bar"),
    "hud_heart": (140, 86, 232, 176, "HP icon, left cap of the distance bar"),
    "hud_flag": (798, 92, 892, 180, "goal flag, right cap of the distance bar"),
    "hud_distance_track": (222, 98, 808, 160, "empty distance track between heart and flag"),
    "hud_label_hp": (110, 258, 242, 334, "HP label glyph"),
    "hud_bar_hp_full": (252, 258, 900, 334, "HP bar, full: dark track + green fill"),
    "hud_fill_hp": (256, 263, 858, 330, "HP green fill only"),
    "hud_label_xp": (108, 415, 242, 495, "XP label glyph"),
    "hud_bar_xp_full": (252, 420, 905, 497, "XP bar, full: dark track + blue fill"),
    "hud_fill_xp": (256, 424, 745, 492, "XP blue fill only"),
}
for name, (x0, y0, x1, y1, role) in HUD_BOXES.items():
    p = crop(HUD, (x0, y0, x1, y1), os.path.join(ui_hud, name + ".png"), alpha_bg=hud_bg)
    add(p, "ui/hud", ["hud", "bar" if "bar" in name or "fill" in name else "icon"], role,
        "figma image fill c65e402d (HUD kit sheet)", transparency=True)

# ---------------------------------------------------------------- 2. UI: level-up screen
LVUP = os.path.join(SRC, "assets/raw/level-3-toxic-swamp__20-level-up-upgrade-screen__1c955e61.png")
ui_cards = ensure("ui", "cards")
LV_BOXES = {
    "up_levelup_wordmark": (86, 66, 490, 158, "LEVEL UP! wordmark, letters plus outline"),
    "up_levelup_burst": (30, 30, 548, 172, "LEVEL UP! wordmark with its spark burst"),
    "up_card_row_mini": (88, 162, 492, 350, "condensed 3-card row under the wordmark"),
    "up_card_multishot": (30, 402, 192, 608, "full card: frame + label + art"),
    "up_card_firetrail": (202, 402, 364, 608, "full card: frame + label + art"),
    "up_card_spikebumper": (375, 402, 537, 608, "full card: frame + label + art"),
    "up_art_multishot": (46, 448, 176, 578, "card art only, no frame"),
    "up_art_firetrail": (218, 448, 348, 578, "card art only, no frame"),
    "up_art_spikebumper": (391, 448, 521, 578, "card art only, no frame"),
    "up_tap_choose": (200, 610, 396, 664, "TAP CHOOSE prompt"),
    "up_arrow_left": (30, 798, 154, 920, "prev/next arrow button"),
    "up_arrow_right": (415, 798, 539, 920, "prev/next arrow button, mirrored"),
    "up_gauge_distance": (214, 790, 360, 930, "circular DISTANCE TO SURVIVE gauge"),
    "up_instruction": (70, 930, 505, 978, "swipe/tap instruction line"),
}
for name, (x0, y0, x1, y1, role) in LV_BOXES.items():
    p = crop(LVUP, (x0, y0, x1, y1), os.path.join(ui_cards, name + ".png"))
    add(p, "ui/cards", ["upgrade", "modal"], role, "figma image fill 1c955e61 (level-up sheet)")

# ---------------------------------------------------------------- 3. UI: touch chip states
ui_ctrl = ensure("ui", "control")
CHIP_FOR = {"07": "hold", "08": "release", "10": "auto", "19": "ready"}
l1 = [l for l in SPEC["levels"] if "METRO" in l["name"]][0]
for s in l1["states"]:
    if s["number"] not in CHIP_FOR:
        continue
    st = find(raw["document"], s["id"])
    mock = next(c for c in st["children"] if "mockup" in c["name"].lower())
    tc = next((c for c in mock["children"] if c["name"].lower().startswith("touch control")), None)
    if not tc:
        continue
    mb, tb = mock["absoluteBoundingBox"], tc["absoluteBoundingBox"]
    x0 = (tb["x"] - mb["x"]) * 2
    y0 = (tb["y"] - mb["y"]) * 2
    box = (int(x0) - 2, int(y0) - 2, int(x0 + tb["width"] * 2) + 2, int(y0 + tb["height"] * 2) + 2)
    src = os.path.join(SRC, "states", l1["slug"], s["renderName"] + ".png")
    if not os.path.exists(src):
        continue
    p = crop(src, box, os.path.join(ui_ctrl, "control_%s.png" % CHIP_FOR[s["number"]]))
    add(p, "ui/control", ["touch", "input"], "touch-control chip in the %s state" % CHIP_FOR[s["number"]].upper(),
        "figma card render %s" % s["renderName"], transparency=False,
        notes="composed over live gameplay art; chip is 32x41 at design scale, this crop is 2x")

# ---------------------------------------------------------------- 4. screens
for lvl in SPEC["levels"]:
    d = ensure("screens", lvl["slug"])
    for s in lvl["states"]:
        st = find(raw["document"], s["id"])
        mock = next((c for c in st["children"] if "mockup" in c["name"].lower()), None)
        ref = None
        if mock:
            for f in mock.get("fills") or []:
                if f.get("type") == "IMAGE":
                    ref = f.get("imageRef")
        if not ref or ref not in IMGMAP:
            continue
        src = os.path.join(SRC, IMGMAP[ref]["file"])
        im = Image.open(src)
        if im.size != (1024, 1024):
            continue
        dest = os.path.join(d, s["renderName"] + ".png")
        shutil.copyfile(src, dest)
        add(dest, "screens/" + lvl["slug"], ["screen", "gameplay", lvl["slug"].split("-")[1]],
            "clean %s gameplay frame" % s["title"], "figma image ref %s" % ref[:8],
            notes="1024x1024, no HUD (HUD is drawn separately)")

# ---------------------------------------------------------------- 4b. hero crops + composed HUD reference
HERO = ensure("items", "hero")
for name, box, role, srcn in [
    ("train_burning-metropolis", (368, 140, 665, 1000), "engine + full convoy, top-down rear view", "level-1-burning-metropolis/01-start-line.png"),
    ("boss_inferno-giant", (455, 35, 900, 430), "the L1 boss creature (giant infected juggernaut)", "level-1-burning-metropolis/15-encountering-a-giant-boss-enemy.png"),
]:
    p = crop(os.path.join(OUT, "screens", srcn), box, os.path.join(HERO, name + ".png"))
    add(p, "items/hero", ["reference", "crop"], role, "crop of screens/" + srcn,
        notes="opaque scene crop, background included; box documented in AGENTS.md")

p = crop(os.path.join(SRC, "states", l1["slug"], "07-the-horde-swarming-the-track.png"),
         (0, 0, 600, 600), os.path.join(ensure("ui"), "reference_hud_layout.png"))
add(p, "ui", ["reference", "hud", "layout"], "HUD composed over gameplay at design proportions (2x)",
    "figma card render 07", notes="shows spacing of distance bar, status bar and touch chip together")

# ---------------------------------------------------------------- 5. items
ENEMY_BASE = {"walker": "walker zombie — slow shambler",
              "sprinter": "sprinter zombie — fast leaper",
              "tank": "tank zombie — armored heavy"}
ROLE_MAP = {
    "gem_green": ("XP gem, green", ["gem", "xp", "pickup", "green"]),
    "gem_blue": ("XP gem, blue — mid value", ["gem", "xp", "pickup", "blue"]),
    "gem_purple": ("XP gem, purple — high value", ["gem", "xp", "pickup", "purple"]),
    "health_pack": ("health pickup — restores HP", ["health", "pickup", "heal"]),
    "currency": ("soft currency — meta reward", ["currency", "pickup", "meta"]),
    "crate": ("wooden crate — scenery / destructible prop", ["crate", "prop", "scenery"]),
    "barrel_rust": ("rusty barrel — scenery prop", ["barrel", "prop", "scenery"]),
    "barrel_toxic": ("toxic barrel — hazard prop", ["barrel", "hazard", "prop"]),
    "block_barrier": ("striped road barrier blockade", ["barrier", "blockade", "ram"]),
    "block_barrels": ("stacked barrel blockade", ["barrels", "blockade", "ram"]),
    "block_wreck": ("wrecked car blockade", ["wreck", "blockade", "ram"]),
    "block_debris": ("rubble pile blockade", ["debris", "blockade", "ram"]),
    "up_multishot": ("upgrade: multishot (+projectiles)", ["upgrade", "weapon", "multishot"]),
    "up_firetrail": ("upgrade: fire trail (burn on move)", ["upgrade", "fire", "trail"]),
    "up_spikebumper": ("upgrade: spike bumper (ram damage)", ["upgrade", "ram", "spike"]),
    "up_piercing": ("upgrade: piercing rounds", ["upgrade", "weapon", "pierce"]),
    "up_boiler": ("upgrade: boiler (train power)", ["upgrade", "train", "power"]),
    "up_rapidfire": ("upgrade: rapid fire", ["upgrade", "weapon", "rate"]),
    "up_incendiary": ("upgrade: incendiary rounds", ["upgrade", "fire", "damage"]),
    "up_extragun": ("upgrade: extra gun (+turret slot)", ["upgrade", "turret", "convoy"]),
    "up_extrawagon": ("upgrade: extra wagon (+car slot)", ["upgrade", "wagon", "convoy"]),
    "marker_a": ("route A branch marker", ["route", "split", "a"]),
    "marker_b": ("route B branch marker", ["route", "split", "b"]),
    "flag_green": ("goal flag, green", ["flag", "goal", "green"]),
    "flag_red": ("goal flag, red — stage end", ["flag", "goal", "red"]),
    "flag_yellow": ("goal flag, yellow — checkpoint", ["flag", "goal", "yellow"]),
    "warn_0": ("warning triangle", ["warn", "hazard", "alert"]),
    "warn_1": ("warning triangle, variant", ["warn", "hazard", "alert"]),
    "banner_levelup": ("LEVEL UP banner (full-screen)", ["banner", "levelup", "modal"]),
    "banner_victory": ("VICTORY banner (full-screen)", ["banner", "victory", "stage"]),
    "bar_frame": ("bar frame, 9-slice source", ["hud", "9slice", "frame"]),
    "bar_fill_green": ("HP fill, 9-slice source", ["hud", "9slice", "hp", "fill"]),
    "bar_fill_blue": ("XP fill, 9-slice source", ["hud", "9slice", "xp", "fill"]),
    "icon_heart": ("heart icon — HP", ["hud", "icon", "heart", "hp"]),
}
GROUPS = {
    "enemies": (("walker_", "sprinter_", "tank_"), "enemy"),
    "pickups": (("gem_", "health_pack", "currency", "crate", "barrel_"), "pickup"),
    "obstacles": (("block_",), "obstacle"),
    "upgrade_icons": (("up_",), "upgrade"),
    "markers": (("marker_", "flag_", "warn_"), "marker"),
    "banners": (("banner_",), "banner"),
    "hud_art": (("bar_frame", "bar_fill", "icon_heart"), "hud"),
}


def describe(stem, tag):
    if stem in ROLE_MAP:
        return ROLE_MAP[stem]
    for base, label in ENEMY_BASE.items():
        if stem.startswith(base + "_"):
            kind = stem[len(base) + 1:]
            if kind == "hit":
                return ("%s — hit flash frame" % label, [base, "enemy", "hit", "flash"])
            if kind == "dead":
                return ("%s — death frame" % label, [base, "enemy", "dead"])
            return ("%s — idle cycle frame %s" % (label, kind), [base, "enemy", "idle", "frame"])
    return (tag, [tag])


for grp, (prefixes, tag) in GROUPS.items():
    d = ensure("items", grp)
    for fn in sorted(os.listdir(SLICED)):
        if not fn.endswith(".png") or not fn.startswith(prefixes):
            continue
        stem = os.path.splitext(fn)[0]
        role, tags = describe(stem, tag)
        src = os.path.join(SLICED, fn)
        dest = os.path.join(d, fn)
        shutil.copyfile(src, dest)
        add(dest, "items/" + grp, ["sprite"] + tags, role, "images/sliced/" + fn,
            notes="alpha-keyed, tight trimmed")

# ---------------------------------------------------------------- 6. palettes
def palette_of(files, n=8):
    total = {}
    for f in files:
        im = Image.open(f).convert("RGB").resize((160, 160))
        q = im.quantize(colors=n, method=Image.MEDIANCUT).convert("RGB")
        cnt = {}
        for px in q.getdata():
            cnt[px] = cnt.get(px, 0) + 1
        for c, v in cnt.items():
            total[c] = total.get(c, 0) + v
    return [c for c, _ in sorted(total.items(), key=lambda kv: -kv[1])[:n]]


def hexc(c):
    return "#%02X%02X%02X" % c


BIOME_COLORS = {}
for lvl in SPEC["levels"]:
    d = ensure("biomes", lvl["slug"])
    screens = sorted(os.listdir(os.path.join(OUT, "screens", lvl["slug"])))
    pick = [os.path.join(OUT, "screens", lvl["slug"], s) for s in screens if s[:2] in ("01", "07", "15", "19")]
    cols = palette_of(pick)
    BIOME_COLORS[lvl["slug"]] = [hexc(c) for c in cols]
    sw = Image.new("RGB", (n_w := 720, 90), (14, 21, 34))
    dr = ImageDraw.Draw(sw)
    step = n_w // len(cols)
    for i, c in enumerate(cols):
        dr.rectangle([i * step, 0, (i + 1) * step - 2, 64], fill=c)
        lum = 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]
        dr.text((i * step + 6, 68), hexc(c), fill=(242, 244, 238) if lum > 60 else (147, 163, 173))
    sw.save(os.path.join(d, "palette.png"))
    short = "l" + lvl["slug"].split("-")[1]
    add(os.path.join(d, "palette.png"), "biomes", ["palette", "biome", short],
        "area-weighted palette of %s, from states 01/07/15/19" % lvl["header"]["title"],
        "derived from screens", notes="swatch strip; exact hexes in meta/palette.json")

# ---------------------------------------------------------------- 7. contact sheets
def sheet(paths, dest, cols=6, cell=200, checker=False, title=""):
    rows = (len(paths) + cols - 1) // cols
    pad, top = 8, 26
    W = cols * cell + pad * 2
    H = rows * (cell + 20) + top + pad
    im = Image.new("RGB", (W, H), (46, 56, 68) if not checker else (16, 22, 30))
    d = ImageDraw.Draw(im)
    d.text((pad, 8), title, fill=(237, 253, 120))
    for i, p in enumerate(paths):
        cx = pad + (i % cols) * cell
        cy = top + (i // cols) * (cell + 20)
        if checker:
            for by in range(cy, cy + cell - 18, 10):
                for bx in range(cx, cx + cell - 8, 10):
                    if ((bx - cx) // 10 + (by - cy) // 10) % 2 == 0:
                        d.rectangle([bx, by, bx + 9, by + 9], fill=(38, 44, 52))
        tile = Image.open(p)
        tile.thumbnail((cell - 10, cell - 24))
        if tile.mode == "RGBA":
            im.paste(tile, (cx + 4, cy + 2), tile)
        else:
            im.paste(tile, (cx + 4, cy + 2))
        d.text((cx + 4, cy + cell - 14), os.path.splitext(os.path.basename(p))[0][:30], fill=(180, 195, 205))
    im.save(dest)
    return dest


ui_paths = [os.path.join(OUT, e["path"]) for e in CATALOG if e["category"].startswith("ui/")]
item_paths = [os.path.join(OUT, e["path"]) for e in CATALOG if e["category"].startswith("items/")]
sh = ensure("sheets")
sheet(ui_paths, os.path.join(sh, "ui.png"), cols=5, cell=220, checker=False, title="UI — HUD, cards, control")
sheet(item_paths, os.path.join(sh, "items.png"), cols=8, cell=170, checker=True, title="ITEMS — enemies, pickups, obstacles, icons")
for lvl in SPEC["levels"]:
    ps = [os.path.join(OUT, "screens", lvl["slug"], f)
          for f in sorted(os.listdir(os.path.join(OUT, "screens", lvl["slug"])))]
    sheet(ps, os.path.join(sh, "screens-%s.png" % lvl["slug"]), cols=5, cell=260,
          title="SCREENS — %s" % lvl["name"])

# ---------------------------------------------------------------- 8. catalog + meta
meta = ensure("meta")
# unique ids: screen/palette names repeat across biomes
for e in CATALOG:
    if e["category"].startswith("screens/"):
        short = e["category"].split("/")[1].split("-")[1]
        e["id"] = "%s-%s" % ("l" + short, e["id"])
    elif e["id"] == "palette":
        e["id"] = "palette-" + e["path"].split("/")[1]
CATALOG.sort(key=lambda e: (e["category"], e["id"]))
json.dump({"root": "finalized-for-develop", "source": "design/figma (Figma file WCcq0Z67MX8Xuus3m5qVVa)",
           "assetCount": len(CATALOG), "assets": CATALOG},
          open(os.path.join(meta, "catalog.json"), "w"), indent=1)
json.dump({"biomes": BIOME_COLORS,
           "hud": {"canvas": 300, "statusBar": {"w": 284, "h": 27, "radius": 5, "color": "#0E1822", "opacity": 0.94},
                   "track": {"w": 106, "h": 16, "radius": 4, "hp": "#123617", "xp": "#112B45"},
                   "fill": {"h": 12, "radius": 2},
                   "distance": {"w": 284, "h": 50, "y": 5, "fillH": 11, "x0": 38},
                   "touch": {"w": 32, "h": 41, "x": 253, "y": 177},
                   "boss": {"w": 190, "h": 33, "trackW": 178, "trackH": 6},
                   "card": {"w": 88, "h": 200, "artW": 80, "artH": 120}}},
          open(os.path.join(meta, "palette.json"), "w"), indent=1)

by_cat = {}
for e in CATALOG:
    by_cat.setdefault(e["category"], []).append(e)
json.dump({k: len(v) for k, v in by_cat.items()}, open(os.path.join(meta, "counts.json"), "w"), indent=1)
print(json.dumps({k: len(v) for k, v in by_cat.items()}, indent=1))
print("total", len(CATALOG))

# ---------------------------------------------------------------- 9. INDEX.md
import collections
by = collections.defaultdict(list)
for e in CATALOG:
    by[e["category"]].append(e)
QUICK = [("HUD bars, heart, flag", "ui/hud/"), ("HUD spacing over gameplay", "ui/reference_hud_layout.png"),
         ("upgrade cards + level-up modal", "ui/cards/"), ("touch chip per input state", "ui/control/"),
         ("enemies", "items/enemies/"), ("gems, health, crate, barrels", "items/pickups/"),
         ("barricades", "items/obstacles/"), ("upgrade icons", "items/upgrade_icons/"),
         ("route markers, flags, warnings", "items/markers/"), ("banners", "items/banners/"),
         ("train / boss reference crops", "items/hero/"), ("full gameplay frames", "screens/"),
         ("biome palettes", "biomes/"), ("machine search", "meta/catalog.json")]
L = ["# Asset index — finalized for development", "",
     "%d files. Browse visually first: `sheets/`." % len(CATALOG), "",
     "| Sheet | Shows |", "|---|---|",
     "| `sheets/ui.png` | every HUD piece, card, chip |",
     "| `sheets/items.png` | every sprite on transparency |",
     "| `sheets/screens-*.png` | all frames of one biome |", "",
     "| I need | Path |", "|---|---|"]
for label, path in QUICK:
    L.append("| %s | `%s` |" % (label, path))
L.append("")
for lvl in SPEC["levels"]:
    files = sorted(os.listdir(os.path.join(OUT, "screens", lvl["slug"])))
    L.append("- `screens/%s/` — %d frames (`%s` … `%s`)" % (lvl["slug"], len(files), files[0], files[-1]))
L.append("")
for cat in sorted(by, key=lambda c: (c.count("/"), c)):
    L += ["## %s" % cat, "", "| file | px | tags | role |", "|---|---|---|---|"]
    for a in sorted(by[cat], key=lambda x: x["id"]):
        L.append("| `%s` | %d×%d | %s | %s |" % (a["path"], a["w"], a["h"], ", ".join(a["tags"]), a["role"]))
    L.append("")
open(os.path.join(OUT, "INDEX.md"), "w").write("\n".join(L) + "\n")
print("INDEX.md rewritten")
