#!/usr/bin/env python3
"""Cut the painted creature out of an enemy card, then derive game-ready sprites.

The cards are a fixed layout: big title on top, creature in the middle, an info panel
across the bottom. The creature is the only region with dense high-frequency detail, so:

  1. edge energy (FIND_EDGES on a blurred grey copy) gives a "painted stuff" mask
  2. connected components keep only the blobs around the creature (title letters and the
     panel are separate components and get dropped)
  3. holes are filled, the mask is feathered, cropped tight, and exported

Outputs per enemy directory:
  sprite.png       clean cutout, alpha, trimmed  (game billboard)
  sprite_hit.png   white-flash frame
  sprite_dead.png  desaturated, darkened, flattened frame
  cutout_preview.png  cutout on a flat colour (QA only)

Usage: python3 tools/extract_enemy_art.py [id ...]
"""
import json, math, os, sys
from PIL import Image, ImageDraw, ImageFilter, ImageChops, ImageEnhance

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ART = os.path.join(ROOT, "art-work/enemies")

# per-enemy overrides: crop box (l, t, r, b) in source pixels, plus rects to erase
# (leftover title letters / panel edges that the component filter cannot drop).
CONFIG = {
    "walker":    {"box": (40, 150, 730, 850)},
    "sprinter":  {"box": (30, 150, 740, 850)},
    "tank":      {"box": (20, 140, 750, 870)},
    "crawler":   {"box": (30, 140, 740, 860)},
    "bloater":   {"box": (30, 150, 740, 860)},
    "spitter":   {"box": (30, 150, 740, 860)},
    "shrieker":  {"box": (30, 150, 740, 860)},
    "bulwark":   {"box": (30, 140, 745, 870)},
    "glider":    {"box": (20, 140, 750, 880)},
    "drillmaw":  {"box": (30, 140, 740, 870)},
    "lantern":   {"box": (60, 55, 720, 880), "top": 0.0, "panel": 0.98,
                  "erase": [(0, 0, 150, 210)],
                  "poly": [(248, 16), (312, 92), (344, 118), (384, 184), (430, 282),
                           (450, 430), (462, 500), (436, 560), (414, 620), (402, 700),
                           (372, 686), (196, 686), (166, 660), (152, 600), (142, 520),
                           (120, 470), (60, 470), (46, 380), (78, 340), (88, 250), (110, 134)]},
    "rimback":   {"box": (20, 130, 750, 880)},
}


def edge_mask(im, w=384):
    """Density-of-detail mask: painted regions are dense, smoke grain is sparse."""
    h = int(im.size[1] * w / im.size[0])
    small = im.convert("RGB").resize((w, h), Image.LANCZOS)
    grey = small.convert("L")
    hp = ImageChops.difference(grey, grey.filter(ImageFilter.GaussianBlur(8)))
    detail = hp.point(lambda v: 255 if v > 18 else 0)
    mask = detail.filter(ImageFilter.MaxFilter(3))
    return mask, w, h


def components(mask, w, h, min_area=140):
    """Label 4-connected blobs. Returns (accepted blobs, label map)."""
    px = mask.load()
    labels = [0] * (w * h)
    out = []
    cid = 0
    for y0 in range(h):
        row = y0 * w
        for x0 in range(w):
            if labels[row + x0] or px[x0, y0] == 0:
                continue
            cid += 1
            stack = [(x0, y0)]
            labels[row + x0] = cid
            area = 0
            minx, maxx, miny, maxy = x0, x0, y0, y0
            sx = sy = 0
            while stack:
                x, y = stack.pop()
                area += 1
                sx += x
                sy += y
                if x < minx: minx = x
                if x > maxx: maxx = x
                if y < miny: miny = y
                if y > maxy: maxy = y
                for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                    if 0 <= nx < w and 0 <= ny < h and not labels[ny * w + nx] and px[nx, ny]:
                        labels[ny * w + nx] = cid
                        stack.append((nx, ny))
            if area >= min_area:
                out.append({"area": area, "id": cid, "bbox": (minx, miny, maxx, maxy),
                            "cx": sx / area, "cy": sy / area})
    return out, labels


def fill_holes(mask):
    """Flood the outside from the border; anything unreached is a hole."""
    inv = ImageChops.invert(mask)
    w, h = mask.size
    flood = inv.copy()
    ImageDraw.floodfill(flood, (0, 0), 128, thresh=0)
    holes = flood.point(lambda v: 255 if v == 255 else 0)   # unreached = inside a hole
    return ImageChops.lighter(mask, holes)


def find_panel_top(im, w=384):
    """Y (in `w`-space) where the info panel's top border starts, or h if not found."""
    h = int(im.size[1] * w / im.size[0])
    small = im.convert("L").resize((w, h), Image.LANCZOS).filter(ImageFilter.GaussianBlur(0.8))
    px = small.load()
    best = h
    for y in range(int(h * 0.55), h - 1):
        run = 0
        longest = 0
        for x in range(1, w - 1):
            if abs(px[x, y] - px[x, y - 1]) + abs(px[x, y] - px[x, y + 1]) > 40:
                run += 1
                longest = max(longest, run)
            else:
                run = 0
        if longest > 0.55 * w:
            best = y
            break
    return best


def cutout(path, cfg):
    src = Image.open(path).convert("RGB")
    box = cfg.get("box", (0, 0, src.size[0], src.size[1]))
    im = src.crop(box)

    mask, w, h = edge_mask(im)

    if cfg.get("poly"):
        # hand-authored outline, intersected with the painted-detail mask so the
        # background inside the polygon (smoke, panel corners) drops out
        poly = Image.new("L", (w, h), 0)
        sc = w / im.size[0]
        ImageDraw.Draw(poly).polygon([(x * sc, y * sc) for x, y in cfg["poly"]], fill=255)
        detail = mask.filter(ImageFilter.MaxFilter(3))
        m = ImageChops.multiply(poly, detail)
        m = m.filter(ImageFilter.MaxFilter(5))
        m = fill_holes(m)
        m = ImageChops.multiply(m, poly)
        m = m.filter(ImageFilter.MaxFilter(3)).filter(ImageFilter.GaussianBlur(1.6))
        full = m.resize(im.size, Image.LANCZOS).point(
            lambda v: 0 if v < 70 else (255 if v > 170 else int((v - 70) * 255 / 100)))
        out = im.convert("RGBA")
        out.putalpha(full)
        # soft fade over the last 14 px so the hard cut above the card panel does not read
        fade = ImageDraw.Draw(out)
        hh = out.size[1]
        for i in range(min(14, hh)):
            pass
        alpha = out.split()[3]
        ramp = Image.new("L", (1, hh), 255)
        for y in range(hh - 14, hh):
            ramp.putpixel((0, y), int(255 * max(0.0, (hh - y) / 14.0)))
        alpha = ImageChops.multiply(alpha, ramp.resize(out.size, Image.BILINEAR))
        out.putalpha(alpha)
        if cfg.get("erase"):
            ed = ImageDraw.Draw(out)
            for rect in cfg["erase"]:
                ed.rectangle(rect, fill=(0, 0, 0, 0))
        bbox = out.split()[3].point(lambda v: 255 if v > 8 else 0).getbbox()
        out = despeckle(out.crop(bbox)) if bbox else out
        return out, {"sourceBox": box, "method": "polygon+detail", "trimmed": out.size}

    comps, labels = components(mask, w, h)
    if not comps:
        return None, {"error": "no components"}

    # the creature is the big blob nearest the middle of the frame; title letters and the
    # info panel are separate blobs and get dropped by this rule
    def score(c):
        cx, cy = c["cx"] / w, c["cy"] / h
        dist = ((cx - 0.5) ** 2 + (cy - 0.48) ** 2) ** 0.5
        return dist - min(c["area"], 40000) / 120000.0

    cands = [c for c in comps if c["area"] > 400 and c["cy"] < 0.86 * h]
    if not cands:
        return None, {"error": "no candidate"}
    main_c = min(cands, key=score)
    x0, y0, x1, y1 = main_c["bbox"]
    keep_ids = {main_c["id"]}
    for c in cands:
        if c["id"] == main_c["id"]:
            continue
        bx0, by0, bx1, by1 = c["bbox"]
        ox = max(0, min(x1, bx1) - max(x0, bx0))
        oy = max(0, min(y1, by1) - max(y0, by0))
        if ox * oy > 0.35 * (bx1 - bx0) * (by1 - by0):     # detached limb / prop
            keep_ids.add(c["id"])

    m = Image.new("L", (w, h), 0)
    mp = m.load()
    for y in range(h):
        row = y * w
        for x in range(w):
            if labels[row + x] in keep_ids:
                mp[x, y] = 255
    top_y = int(cfg.get("top", 0.05) * h)
    ImageDraw.Draw(m).rectangle((0, 0, w, top_y), fill=0)
    panel_y = int(cfg.get("panel", 0.70) * h)
    ImageDraw.Draw(m).rectangle((0, panel_y, w, h), fill=0)
    m = m.filter(ImageFilter.MaxFilter(3))          # close stroke gaps
    m = fill_holes(m)                               # solid body
    # clipping again: fill_holes can re-seal the panel/title strips behind the creature
    ImageDraw.Draw(m).rectangle((0, 0, w, top_y), fill=0)
    ImageDraw.Draw(m).rectangle((0, panel_y, w, h), fill=0)
    m = m.filter(ImageFilter.MaxFilter(3))
    m = m.filter(ImageFilter.MinFilter(3))
    m = m.filter(ImageFilter.GaussianBlur(1.4))

    full = m.resize(im.size, Image.LANCZOS)
    full = full.point(lambda v: 0 if v < 64 else (255 if v > 176 else int((v - 64) * 255 / 112)))

    out = im.convert("RGBA")
    out.putalpha(full)

    for rect in cfg.get("erase", []):
        ImageDraw.Draw(out).rectangle(rect, fill=(0, 0, 0, 0))

    bbox = out.split()[3].point(lambda v: 255 if v > 8 else 0).getbbox()
    if not bbox:
        return None, {"error": "empty after mask"}
    out = out.crop(bbox)
    return out, {"sourceBox": box, "blob": main_c["area"], "components": len(keep_ids),
                 "trimmed": out.size}


def despeckle(sprite, keep_ratio=0.02, w=160):
    """Remove tiny islands (title drips, panel slivers) from a cutout."""
    alpha = sprite.split()[3]
    h = int(alpha.size[1] * w / alpha.size[0])
    small = alpha.resize((w, h), Image.LANCZOS).point(lambda v: 255 if v > 60 else 0)
    comps, labels = components(small, w, h, min_area=6)
    if len(comps) <= 1:
        return sprite
    big = max(c["area"] for c in comps)
    keep = {c["id"] for c in comps if c["area"] >= big * keep_ratio}
    m = Image.new("L", (w, h), 0)
    mp = m.load()
    for y in range(h):
        row = y * w
        for x in range(w):
            if labels[row + x] in keep:
                mp[x, y] = 255
    m = m.resize(sprite.size, Image.LANCZOS).filter(ImageFilter.GaussianBlur(0.8))
    mask = Image.composite(alpha, Image.new("L", sprite.size, 0), m)
    out = sprite.copy()
    out.putalpha(mask)
    bbox = mask.point(lambda v: 255 if v > 8 else 0).getbbox()
    out = out.crop(bbox) if bbox else sprite
    # the card's panel border sometimes survives as a thin band on the bottom edge
    if out.size[1] > 8:
        ImageDraw.Draw(out).rectangle((0, out.size[1] - 4, out.size[0], out.size[1]), fill=(0, 0, 0, 0))
        bbox = out.split()[3].point(lambda v: 255 if v > 8 else 0).getbbox()
        if bbox:
            out = out.crop(bbox)
    return out


def variants(sprite):
    w, h = sprite.size
    rgb = sprite.convert("RGB")

    hit = Image.blend(rgb, Image.new("RGB", (w, h), (255, 250, 240)), 0.82)
    hit = Image.merge("RGBA", (*hit.split(), sprite.split()[3]))

    dead = ImageEnhance.Color(rgb).enhance(0.18)
    dead = ImageEnhance.Brightness(dead).enhance(0.55)
    dead = dead.resize((int(w * 1.14), int(h * 0.55)), Image.LANCZOS)
    dead = Image.merge("RGBA", (*dead.split(), dead.convert("L").point(lambda v: 255 if v > 6 else 0)))
    return hit, dead


def main(ids):
    report = {}
    for eid in ids:
        d = os.path.join(ART, eid)
        card = os.path.join(d, "card.png")
        if not os.path.exists(card):
            print("skip", eid)
            continue
        cfg = CONFIG.get(eid, {})
        sprite, info = cutout(card, cfg)
        if sprite is None:
            print("FAIL", eid, info)
            report[eid] = info
            continue
        sprite = despeckle(sprite)
        sprite.save(os.path.join(d, "sprite.png"))
        hit, dead = variants(sprite)
        hit.save(os.path.join(d, "sprite_hit.png"))
        dead.save(os.path.join(d, "sprite_dead.png"))
        prev = Image.new("RGB", (sprite.size[0] + 40, sprite.size[1] + 40), (255, 0, 200))
        prev.paste(sprite, (20, 20), sprite)
        prev.save(os.path.join(d, "cutout_preview.png"))
        report[eid] = {**info, "sprite": sprite.size,
                       "coverage": round(100 * sum(1 for v in sprite.split()[3].get_flattened_data()
                                         if v > 128) / (sprite.size[0] * sprite.size[1]), 1)}
        print("%-9s %s -> %s  (%s%%)" % (eid, info.get("trimmed"), sprite.size, report[eid]["coverage"]))
    json.dump(report, open(os.path.join(ART, "_extract.json"), "w"), indent=1)
    return report


if __name__ == "__main__":
    ids = sys.argv[1:] or [d for d in sorted(os.listdir(ART)) if os.path.isdir(os.path.join(ART, d))]
    main(ids)
