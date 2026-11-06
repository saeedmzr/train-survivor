#!/usr/bin/env python3
"""Expansion art generator: 10 enemies, 10 bosses, projectiles, kill FX, HUD v2.

Vector-style sprites drawn with PIL at 4x supersample, in the game's palette.
Run: python3 finalized-for-develop/meta/gen_expansion.py
Outputs images into enemies/ bosses/ projectiles/ fx/ ui/hud/, data/*.json,
merges entries into meta/catalog.json, writes INDEX.md, and a contact sheet each.
"""
import json, math, os, random
from PIL import Image, ImageDraw, ImageFilter, ImageChops, ImageFont

PROJ = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(PROJ, "finalized-for-develop")
SS = 3  # supersample factor


# ------------------------------------------------------------------ colour utils
def hx(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def mix(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def shade(c, f):
    """f<1 darkens, f>1 lightens toward white."""
    if f <= 1:
        return tuple(int(max(0, min(255, v * f))) for v in c)
    return mix(c, (255, 255, 255), min(1.0, f - 1))


def hxs(c):
    return "#%02X%02X%02X" % c


# ------------------------------------------------------------------ sprite core
class Sprite:
    """Design space is 0..100 on both axes; output is `size` px square."""

    def __init__(self, size, pad=6):
        self.size = size
        self.pad = pad
        self.img = Image.new("RGBA", (size * SS, size * SS), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img)
        self.emissive = []
        self.glow_scale = 1.0

    # -- coordinate helpers
    def _xy(self, x, y):
        s = self.size * SS / 100.0
        return x * s, y * s

    def _box(self, cx, cy, rx, ry):
        x0, y0 = self._xy(cx - rx, cy - ry)
        x1, y1 = self._xy(cx + rx, cy + ry)
        return [x0, y0, x1, y1]

    # -- primitives (each one paints a cel highlight for cheap volume)
    def _hi(self, color, k=0.34):
        return mix(color, (255, 250, 235), k)

    def ellipse(self, cx, cy, rx, ry, color, rot=0):
        box = self._box(cx, cy, rx, ry)
        self.d.ellipse(box, fill=color + (255,))
        if rx > 2.2 and ry > 2.2:
            b2 = self._box(cx - rx * 0.10, cy - ry * 0.16, rx * 0.68, ry * 0.62)
            self.d.ellipse(b2, fill=self._hi(color) + (150,))
        return self

    def poly(self, pts, color):
        self.d.polygon(self._px(pts), fill=color + (255,))
        cx = sum(p[0] for p in pts) / len(pts)
        cy = sum(p[1] for p in pts) / len(pts)
        hi = [(cx + (x - cx) * 0.74 - 0.8, cy + (y - cy) * 0.74 - 1.0) for x, y in pts]
        self.d.polygon(self._px(hi), fill=self._hi(color) + (140,))
        return self

    def _px(self, pts):
        return [self._xy(x, y) for x, y in pts]

    def rrect(self, x0, y0, x1, y1, r, color):
        a = self._xy(x0, y0)
        b = self._xy(x1, y1)
        rr = r * self.size * SS / 100.0
        self.d.rounded_rectangle([a[0], a[1], b[0], b[1]], radius=rr, fill=color + (255,))
        m = 0.14 * (x1 - x0)
        if (x1 - x0) > 3 * m and (y1 - y0) > 4 * m:
            c = self._xy(x0 + m, y0 + m * 1.4)
            d2 = self._xy(x1 - m * 1.2, y1 - m * 1.6)
            if d2[0] > c[0] and d2[1] > c[1]:
                self.d.rounded_rectangle([c[0], c[1], d2[0], d2[1]], radius=max(1, rr * 0.7),
                                         fill=self._hi(color) + (110,))
        return self

    def capsule(self, x0, y0, x1, y1, w, color):
        a = self._xy(x0, y0)
        b = self._xy(x1, y1)
        ww = w * self.size * SS / 100.0
        self.d.line([a, b], fill=color + (255,), width=int(ww), joint="curve")
        r = ww / 2
        for p in (a, b):
            self.d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=color + (255,))
        if ww > 6:
            off = ww * 0.16
            self.d.line([(a[0] - off * 0.5, a[1] - off), (b[0] - off * 0.5, b[1] - off)],
                        fill=self._hi(color) + (105,), width=max(2, int(ww * 0.34)), joint="curve")
        return self

    def line(self, pts, w, color):
        self.d.line(self._px(pts), fill=color + (255,),
                    width=max(1, int(w * self.size * SS / 100.0)), joint="curve")
        return self

    def spikes(self, cx, cy, r0, r1, n, color, rot=0, arc=360, wratio=0.42):
        pts = []
        step = arc / n
        for i in range(n):
            a0 = math.radians(rot + i * step)
            am = math.radians(rot + i * step + step / 2)
            a1 = math.radians(rot + (i + 1) * step)
            pts += [(cx + math.cos(a0) * r0, cy + math.sin(a0) * r0),
                    (cx + math.cos(am) * r1, cy + math.sin(am) * r1),
                    (cx + math.cos(a1) * r0, cy + math.sin(a1) * r0)]
        self.poly(pts, color)
        return self

    def glow(self, cx, cy, r, color, alpha=120):
        self.emissive.append((cx, cy, r, color, alpha))
        return self

    # -- post
    def finish(self, outline=1.3, vol=0.5, rim=0.75, glowgain=1.0, shadow=True,
               dark=(10, 12, 16), light=(255, 245, 220)):
        w = self.size
        big = self.img
        alpha = big.split()[3]
        canvas = Image.new("RGBA", big.size, (0, 0, 0, 0))

        if shadow:
            sh = Image.new("RGBA", big.size, (0, 0, 0, 0))
            sd = ImageDraw.Draw(sh)
            x0, y0 = self._xy(18, 88)
            x1, y1 = self._xy(82, 99)
            sd.ellipse([x0, y0, x1, y1], fill=(0, 0, 0, 120))
            canvas.alpha_composite(sh.filter(ImageFilter.GaussianBlur(w * SS * 0.018)))

        if self.emissive:
            g = Image.new("RGBA", big.size, (0, 0, 0, 0))
            gd = ImageDraw.Draw(g)
            for cx, cy, r, col, al in self.emissive:
                r = r * self.glow_scale
                x0, y0 = self._xy(cx - r, cy - r)
                x1, y1 = self._xy(cx + r, cy + r)
                gd.ellipse([x0, y0, x1, y1], fill=col + (int(al * glowgain),))
            canvas.alpha_composite(g.filter(ImageFilter.GaussianBlur(w * SS * 0.02)))

        # cheap dilation: do it at 1x, then scale back up
        k = max(3, int(outline * 2) * 2 + 1)
        small = alpha.resize((w, w), Image.LANCZOS).filter(ImageFilter.MaxFilter(k))
        outl = small.resize(big.size, Image.BICUBIC)
        ol = Image.new("RGBA", big.size, dark + (0,))
        ol.putalpha(outl.point(lambda v: int(v * 0.96)))
        canvas.alpha_composite(ol)

        body = big.copy()
        if vol > 0 and False:
            gs = Image.new("L", (256, 256))
            gd = ImageDraw.Draw(gs)
            for i in range(256):
                t = i / 255.0
                v = int(255 * (1.02 - 0.42 * t))
                gd.line([(0, i), (256, i)], fill=min(255, v))
            gs = gs.resize((256, 256))
            # rotate the ramp so light comes from the top-left
            ramp = Image.new("L", (256, 256), 255)
            for yy in range(256):
                for xx in range(0, 256, 8):
                    t = ((xx / 255.0) * 0.55 + (yy / 255.0) * 0.45)
                    v = int(255 * (1.0 - vol * 0.55 * t))
                    ramp.paste(v, (xx, yy, xx + 8, yy + 1))
            ramp = ramp.resize(big.size, Image.BILINEAR)
            mult = ImageChops.multiply(body.convert("RGB"), Image.merge("RGB", (ramp, ramp, ramp)))
            body = Image.merge("RGBA", (*mult.split(), alpha))
        canvas.alpha_composite(body)

        a2 = body.split()[3]
        # cel shadow inside the lower-right edge
        d = int(w * SS * 0.035)
        inner = ImageChops.subtract(a2, ImageChops.offset(a2, -d, -d))
        cs = Image.new("RGBA", big.size, dark + (0,))
        cs.putalpha(inner.point(lambda v: int(v * 0.45)))
        canvas.alpha_composite(cs)

        # grain
        noise = Image.effect_noise((w * SS // 3, w * SS // 3), 26).resize(big.size, Image.BILINEAR).convert("L")
        gn = Image.merge("RGB", (noise, noise, noise)).convert("RGBA")
        gn.putalpha(a2.point(lambda v: int(v * 0.055)))
        canvas.alpha_composite(gn)

        shifted = ImageChops.offset(a2, int(w * SS * 0.014), int(w * SS * 0.014))
        rim_mask = ImageChops.subtract(shifted, a2)
        rim_mask = rim_mask.filter(ImageFilter.GaussianBlur(w * SS * 0.004))
        rim_mask = rim_mask.point(lambda v: int(v * rim))
        rl = Image.new("RGBA", big.size, light + (0,))
        rl.putalpha(rim_mask)
        canvas.alpha_composite(rl)

        return canvas.resize((w, w), Image.LANCZOS)


def save(im, *path):
    p = os.path.join(OUT, *path)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    im.save(p)
    return p


# ------------------------------------------------------------------ enemy recipes
def en_crawler(s, P):
    # low quadruped: long back, flat skull, four stub legs, tail
    s.capsule(24, 58, 76, 54, 13, P["base"])
    s.ellipse(70, 50, 12, 9, P["base"], rot=-14)
    s.poly([(78, 46), (96, 50), (92, 58), (76, 56)], P["dark"])
    s.poly([(84, 52), (96, 50), (94, 55), (86, 56)], P["light"])
    for x in (30, 40, 58, 66):
        s.capsule(x, 62, x - 2, 76, 5, P["dark"])
        s.ellipse(x - 2, 77, 5, 2.6, P["dark"])
    s.capsule(24, 58, 12, 50, 4, P["dark"])
    s.spikes(46, 48, 8, 15, 5, P["accent"], rot=200, arc=120)
    s.ellipse(73, 47, 2.6, 2.6, P["eye"])
    s.glow(73, 47, 6, P["glow"], 130)


def en_bloater(s, P):
    s.ellipse(50, 56, 26, 27, P["base"])
    s.ellipse(50, 64, 18, 16, P["accent"])
    s.ellipse(50, 40, 11, 10, P["base"])
    s.poly([(44, 34), (50, 26), (56, 34)], P["dark"])
    s.ellipse(46, 39, 2.6, 2.2, P["eye"])
    s.ellipse(54, 39, 2.6, 2.2, P["eye"])
    for x in (34, 66):
        s.capsule(x, 74, x + (2 if x < 50 else -2), 84, 7, P["dark"])
    s.line([(50, 30), (54, 20), (50, 12)], 2.2, P["dark"])
    s.ellipse(50, 11, 3.4, 3.4, P["glow"])
    s.glow(50, 60, 20, P["glow"], 90)
    s.glow(50, 11, 7, P["glow"], 200)


def en_spitter(s, P):
    s.ellipse(46, 64, 12, 17, P["base"])            # hunched torso
    s.ellipse(58, 45, 10, 9, P["base"])             # head
    s.ellipse(66, 46, 6, 5.4, P["dark"])            # maw
    s.ellipse(66, 46, 3.2, 2.8, P["glow"])          # acid glow inside
    s.ellipse(60, 42, 2.2, 2.2, P["eye"])
    s.ellipse(38, 56, 13, 11, P["accent"], rot=-18)     # sac
    for x, y in ((30, 52), (43, 50), (36, 62)):
        s.ellipse(x, y, 3.2, 3.2, P["light"])
    s.capsule(40, 76, 36, 88, 6, P["dark"])
    s.capsule(52, 76, 56, 88, 6, P["dark"])
    s.capsule(38, 56, 26, 66, 4.5, P["dark"])       # arm
    s.glow(70, 46, 10, P["glow"], 120)


def en_shrieker(s, P):
    s.ellipse(50, 64, 9, 21, P["base"])
    s.ellipse(50, 34, 8, 10, P["base"])
    s.poly([(42, 30), (50, 14), (58, 30)], P["dark"])   # crest
    s.poly([(43, 40), (50, 55), (57, 40), (50, 44)], P["dark"])  # screaming jaw
    s.ellipse(47, 33, 2, 2, P["eye"])
    s.ellipse(53, 33, 2, 2, P["eye"])
    s.capsule(42, 58, 28, 70, 4, P["dark"])
    s.capsule(58, 58, 72, 70, 4, P["dark"])
    s.capsule(46, 82, 42, 92, 5, P["dark"])
    s.capsule(54, 82, 58, 92, 5, P["dark"])
    for y in (52, 60, 68):
        s.capsule(42, y, 58, y + 1, 2.2, P["accent"])
    s.glow(50, 44, 14, P["glow"], 120)


def en_bulwark(s, P):
    s.ellipse(42, 58, 13, 17, P["base"])            # torso
    s.ellipse(38, 35, 7.5, 7, P["light"])           # head
    s.ellipse(36, 34, 2, 2, P["eye"])
    s.capsule(34, 72, 31, 88, 7, P["dark"])
    s.capsule(50, 72, 54, 88, 7, P["dark"])
    s.capsule(52, 50, 62, 54, 5, P["dark"])         # arm holding the plate
    s.rrect(54, 30, 78, 80, 4, P["plate"])          # rail-sign plate
    s.rrect(57, 34, 75, 76, 3, P["base"])
    for y in (44, 56, 68):
        s.line([(60, y), (72, y)], 1.8, P["dark"])
    s.poly([(57, 34), (63, 34), (63, 76), (57, 76)], P["light"])   # plate bevel
    s.line([(66, 30), (66, 24)], 2.2, P["accent"])  # sign post
    s.glow(36, 34, 4, P["glow"], 90)


def en_glider(s, P):
    s.ellipse(50, 56, 8, 15, P["base"])
    s.ellipse(50, 34, 7, 7, P["base"])
    s.poly([(44, 40), (14, 30), (20, 56), (44, 54)], P["accent"])   # left wing
    s.poly([(56, 40), (86, 30), (80, 56), (56, 54)], P["accent"])
    s.line([(44, 44), (18, 36)], 1.4, P["dark"])
    s.line([(56, 44), (82, 36)], 1.4, P["dark"])
    s.poly([(46, 74), (42, 88), (48, 84), (50, 90), (54, 74)], P["dark"])
    s.ellipse(48, 33, 2, 2, P["eye"])
    s.ellipse(52, 33, 2, 2, P["eye"])
    s.glow(50, 34, 9, P["glow"], 110)


def en_drillmaw(s, P):
    for i, (x, y, r) in enumerate(((50, 74, 16), (50, 58, 14), (50, 44, 12))):
        s.ellipse(x, y, r, r * 0.78, P["base"] if i % 2 == 0 else P["dim"])
    s.spikes(50, 30, 7, 15, 9, P["accent"], rot=-90, arc=360)
    s.ellipse(50, 30, 6, 6, P["dark"])
    s.ellipse(50, 30, 2.6, 2.6, P["glow"])
    for y in (66, 52):
        for x in (36, 64):
            s.capsule(x, y, x + (5 if x < 50 else -5), y - 4, 3, P["dark"])
    s.glow(50, 30, 12, P["glow"], 170)


def en_mites(s, P):
    random.seed(7)
    for i, (x, y, r) in enumerate(((36, 66, 9), (54, 72, 7), (64, 58, 8),
                                   (44, 50, 6), (30, 54, 6), (52, 58, 5))):
        s.ellipse(x, y, r, r * 0.8, P["base"] if i % 2 == 0 else P["dim"])
        s.ellipse(x - r * 0.5, y - r * 0.3, r * 0.32, r * 0.3, P["accent"])
        s.line([(x - r * 0.4, y - r * 0.6), (x - r * 0.9, y - r * 1.1)], 1.2, P["dark"])
        s.line([(x + r * 0.4, y - r * 0.6), (x + r * 0.9, y - r * 1.1)], 1.2, P["dark"])
        s.ellipse(x + r * 0.2, y - r * 0.1, 1.3, 1.3, P["eye"])
    s.glow(46, 62, 26, P["glow"], 70)


def en_lantern(s, P):
    # robed conductor with a lantern; tall silhouette
    s.poly([(38, 36), (62, 36), (70, 92), (30, 92)], P["base"])   # coat
    s.poly([(38, 36), (62, 36), (58, 62), (42, 62)], P["dim"])    # chest
    s.ellipse(50, 26, 8, 9, P["dark"])                            # head
    s.poly([(38, 24), (50, 12), (62, 24)], P["dark"])             # hat brim
    s.rrect(44, 8, 56, 14, 1.5, P["dark"])
    s.ellipse(47, 26, 1.8, 1.8, P["eye"])
    s.ellipse(53, 26, 1.8, 1.8, P["eye"])
    s.capsule(66, 50, 78, 66, 3.4, P["dark"])                     # arm
    s.rrect(74, 62, 86, 76, 2, P["dark"])                         # lantern
    s.rrect(76, 65, 84, 73, 1.2, P["glow"])
    s.glow(80, 69, 12, P["glow"], 190)
    for y in (70, 80):
        s.line([(34, y), (66, y)], 1.4, P["accent"])





def en_rimeback(s, P):
    s.capsule(36, 74, 33, 90, 8, P["dim"])               # legs
    s.capsule(60, 74, 64, 90, 8, P["dim"])
    s.ellipse(30, 56, 8, 12, P["dim"])                   # arms
    s.ellipse(68, 56, 8, 12, P["dim"])
    s.ellipse(48, 58, 22, 24, P["base"])                 # bulk
    s.ellipse(48, 42, 10, 9, P["dim"])                   # head sunk into shoulders
    s.poly([(58, 80), (72, 84), (70, 92), (56, 90)], P["dark"])
    s.poly([(26, 80), (40, 84), (42, 92), (28, 90)], P["dark"])
    for x, h in ((34, 18), (44, 24), (56, 20), (66, 14)):
        s.poly([(x, 44), (x + 5, 44 - h), (x + 10, 44)], P["accent"])
    s.ellipse(45, 39, 2.2, 2.2, P["eye"])
    s.ellipse(51, 39, 2.2, 2.2, P["eye"])
    s.line([(40, 60), (56, 54)], 2, P["dark"])
    s.glow(48, 44, 14, P["glow"], 90)


ENEMIES = [
    ("crawler", "Crawler", "low quadruped that slides under the cowcatcher — ram misses, shoot it",
     dict(base=hx("#5F7A46"), dark=hx("#2A3A24"), light=hx("#A9C07A"), accent=hx("#93E06B"),
          eye=hx("#F6FFC8"), glow=hx("#8CFF6B"), dim=hx("#44583A")), en_crawler),
    ("bloater", "Bloater", "bloated suicide bomber — detonates on contact for area damage",
     dict(base=hx("#8C9257"), dark=hx("#3A3D24"), light=hx("#D6DC96"), accent=hx("#B6D24A"),
          eye=hx("#FFF3B0"), glow=hx("#FFB43C"), dim=hx("#6E7444")), en_bloater),
    ("spitter", "Spitter", "ranged acid lobber on the embankment — arcs globs over barricades",
     dict(base=hx("#A05C7E"), dark=hx("#3B2130"), light=hx("#E5A8C4"), accent=hx("#C6E05A"),
          eye=hx("#FFE6F2"), glow=hx("#B6FF4D"), dim=hx("#7C4560")), en_spitter),
    ("shrieker", "Shrieker", "support screamer — buffs nearby enemies and calls the horde",
     dict(base=hx("#7E93A6"), dark=hx("#26333E"), light=hx("#CBDCE8"), accent=hx("#9FE8FF"),
          eye=hx("#EAFFFF"), glow=hx("#7FE4FF"), dim=hx("#5A6E80")), en_shrieker),
    ("bulwark", "Bulwark", "carries a rail-sign plate — frontally immune, hit it from the side",
     dict(base=hx("#6B6F78"), dark=hx("#23262C"), light=hx("#B4BAC4"), accent=hx("#C8643C"),
          eye=hx("#FFD9A8"), glow=hx("#FF9A4D"), dim=hx("#4C5058"), plate=hx("#8E939C")), en_bulwark),
    ("glider", "Glider", "ragged-wing leaper — vaults barricades and lands on a wagon",
     dict(base=hx("#6E5A93"), dark=hx("#241E36"), light=hx("#BFA9E6"), accent=hx("#9D7BD8"),
          eye=hx("#FFE9FF"), glow=hx("#C88CFF"), dim=hx("#4E3F6B")), en_glider),
    ("drillmaw", "Drillmaw", "burrower — tunnels under the track and surfaces ahead of the train",
     dict(base=hx("#8A6A45"), dark=hx("#33261A"), light=hx("#D3AE7B"), accent=hx("#D8D2C4"),
          eye=hx("#FFEFA0"), glow=hx("#FFC65C"), dim=hx("#6A5034")), en_drillmaw),
    ("mites", "Rustmites", "swarm of six — chews armour, needs splash to clear",
     dict(base=hx("#9A6A3E"), dark=hx("#33210F"), light=hx("#D9A86A"), accent=hx("#C0512E"),
          eye=hx("#FFE07A"), glow=hx("#FF8A3C"), dim=hx("#7A5230")), en_mites),
    ("lantern", "The Lantern", "necromancer — raises fallen zombies as husks until killed",
     dict(base=hx("#2F4A4A"), dark=hx("#121F1F"), light=hx("#7FA8A2"), accent=hx("#C8B06A"),
          eye=hx("#B6FFE6"), glow=hx("#66FFD0"), dim=hx("#223333")), en_lantern),
    ("rimeback", "Rimeback", "armoured brute — its hit chills the throttle for 2 s",
     dict(base=hx("#5C8FA8"), dark=hx("#1B303C"), light=hx("#C7ECF8"), accent=hx("#8FE4FF"),
          eye=hx("#FFFFFF"), glow=hx("#7FD8FF"), dim=hx("#3F6A80")), en_rimeback),
]


NEMIES = [
    ("crawler", "Crawler", "low quadruped that slides under the cowcatcher — ram misses, shoot it",
     dict(base=hx("#5F7A46"), dark=hx("#2A3A24"), light=hx("#A9C07A"), accent=hx("#93E06B"),
          eye=hx("#F6FFC8"), glow=hx("#8CFF6B"), dim=hx("#44583A")), en_crawler),
    ("bloater", "Bloater", "bloated suicide bomber — detonates on contact for area damage",
     dict(base=hx("#8C9257"), dark=hx("#3A3D24"), light=hx("#D6DC96"), accent=hx("#B6D24A"),
          eye=hx("#FFF3B0"), glow=hx("#FFB43C"), dim=hx("#6E7444")), en_bloater),
    ("spitter", "Spitter", "ranged acid lobber on the embankment — arcs globs over barricades",
     dict(base=hx("#A05C7E"), dark=hx("#3B2130"), light=hx("#E5A8C4"), accent=hx("#C6E05A"),
          eye=hx("#FFE6F2"), glow=hx("#B6FF4D"), dim=hx("#7C4560")), en_spitter),
    ("shrieker", "Shrieker", "support screamer — buffs nearby enemies and calls the horde",
     dict(base=hx("#7E93A6"), dark=hx("#26333E"), light=hx("#CBDCE8"), accent=hx("#9FE8FF"),
          eye=hx("#EAFFFF"), glow=hx("#7FE4FF"), dim=hx("#5A6E80")), en_shrieker),
    ("bulwark", "Bulwark", "carries a rail-sign plate — frontally immune, hit it from the side",
     dict(base=hx("#6B6F78"), dark=hx("#23262C"), light=hx("#B4BAC4"), accent=hx("#C8643C"),
          eye=hx("#FFD9A8"), glow=hx("#FF9A4D"), dim=hx("#4C5058"), plate=hx("#8E939C")), en_bulwark),
    ("glider", "Glider", "ragged-wing leaper — vaults barricades and lands on a wagon",
     dict(base=hx("#6E5A93"), dark=hx("#241E36"), light=hx("#BFA9E6"), accent=hx("#9D7BD8"),
          eye=hx("#FFE9FF"), glow=hx("#C88CFF"), dim=hx("#4E3F6B")), en_glider),
    ("drillmaw", "Drillmaw", "burrower — tunnels under the track and surfaces ahead of the train",
     dict(base=hx("#8A6A45"), dark=hx("#33261A"), light=hx("#D3AE7B"), accent=hx("#D8D2C4"),
          eye=hx("#FFEFA0"), glow=hx("#FFC65C"), dim=hx("#6A5034")), en_drillmaw),
    ("mites", "Rustmites", "swarm of six — chews armour, needs splash to clear",
     dict(base=hx("#9A6A3E"), dark=hx("#33210F"), light=hx("#D9A86A"), accent=hx("#C0512E"),
          eye=hx("#FFE07A"), glow=hx("#FF8A3C"), dim=hx("#7A5230")), en_mites),
    ("lantern", "The Lantern", "necromancer — raises fallen zombies as husks until killed",
     dict(base=hx("#2F4A4A"), dark=hx("#121F1F"), light=hx("#7FA8A2"), accent=hx("#C8B06A"),
          eye=hx("#B6FFE6"), glow=hx("#66FFD0"), dim=hx("#223333")), en_lantern),
    ("rimeback", "Rimeback", "armoured brute — its hit chills the throttle for 2 s",
     dict(base=hx("#5C8FA8"), dark=hx("#1B303C"), light=hx("#C7ECF8"), accent=hx("#8FE4FF"),
          eye=hx("#FFFFFF"), glow=hx("#7FD8FF"), dim=hx("#3F6A80")), en_rimeback),
]


# ------------------------------------------------------------------ render helpers
def render(size, fn, palette, glow_scale=1.0, **kw):
    s = Sprite(size)
    s.glow_scale = glow_scale
    fn(s, palette)
    return s.finish(**kw)


def contact(paths, dest, cols=5, cell=260, bg=(38, 46, 58), title=""):
    from PIL import ImageDraw as ID
    rows = (len(paths) + cols - 1) // cols
    W = cols * cell + 16
    H = rows * (cell + 26) + 30
    im = Image.new("RGB", (W, H), bg)
    d = ID.Draw(im)
    d.text((8, 10), title, fill=(237, 253, 120))
    for i, p in enumerate(paths):
        t = Image.open(p)
        t.thumbnail((cell - 12, cell - 26))
        x = 8 + (i % cols) * cell + (cell - t.size[0]) // 2
        y = 30 + (i // cols) * (cell + 26)
        im.paste(t, (x, y), t if t.mode == "RGBA" else None)
        d.text((8 + (i % cols) * cell, y + cell - 22),
               os.path.splitext(os.path.basename(p))[0][:30], fill=(190, 205, 215))
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    im.save(dest)
    return dest


if __name__ == "__main__" and os.environ.get("ONLY_ENEMIES"):
    ps = []
    for eid, name, role, pal, fn in ENEMIES:
        im = render(512, fn, pal)
        ps.append(save(im, "enemies", "en_%s.png" % eid))
    contact(ps, os.path.join(OUT, "sheets", "probe-enemies.png"), title="ENEMIES (probe)")
    print("done", len(ps))


# ------------------------------------------------------------------ boss recipes
def bs_inferno(s, P):
    s.spikes(50, 30, 20, 34, 7, P["accent"], rot=-90, arc=200)
    s.capsule(30, 66, 26, 90, 11, P["dim"]); s.capsule(70, 66, 74, 90, 11, P["dim"])
    s.poly([(24, 40), (76, 40), (82, 74), (18, 74)], P["base"])
    s.ellipse(22, 44, 13, 12, P["dim"]); s.ellipse(78, 44, 13, 12, P["dim"])
    s.ellipse(50, 28, 12, 11, P["light"])
    s.ellipse(45, 28, 2.4, 2.2, P["eye"]); s.ellipse(55, 28, 2.4, 2.2, P["eye"])
    s.poly([(36, 74), (64, 74), (66, 92), (34, 92)], P["plate"])
    for a, b in (((38, 44), (28, 62)), ((62, 44), (72, 62)), ((46, 50), (44, 66)), ((54, 52), (58, 68))):
        s.line([a, (a[0] + (b[0] - a[0]) * .5, b[1]), b], 2.2, P["glow"])
    s.capsule(18, 50, 8, 64, 8, P["base"]); s.ellipse(8, 66, 7, 7, P["dark"])
    s.capsule(82, 50, 92, 64, 8, P["base"]); s.ellipse(92, 66, 7, 7, P["dark"])
    s.glow(50, 60, 26, P["glow"], 150)
    s.glow(50, 28, 10, P["glow2"], 120)


def bs_railmaw(s, P):
    s.ellipse(36, 78, 20, 16, P["dim"]); s.ellipse(58, 68, 17, 14, P["base"])
    s.ellipse(46, 54, 15, 13, P["base"])
    s.spikes(60, 34, 12, 26, 11, P["accent"], rot=-90, arc=360)
    s.ellipse(60, 34, 13, 13, P["dark"])
    s.ellipse(60, 34, 7, 7, P["glow"])
    for i, x in enumerate((30, 42, 54)):
        s.capsule(x, 60 - i * 0.5, x + 10, 60 - i * 0.5, 2.4, P["rail"])
    s.rrect(30, 44, 40, 54, 2, P["dark"])
    s.ellipse(35, 49, 2.4, 2.4, P["rail"])
    s.line([(20, 84), (52, 66)], 2, P["dark"])
    s.glow(60, 34, 20, P["glow"], 170)
    s.glow(35, 49, 8, P["rail"], 120)


def bs_slagheart(s, P):
    s.capsule(28, 76, 24, 92, 9, P["dim"]); s.capsule(72, 76, 76, 92, 9, P["dim"])
    s.rrect(20, 34, 80, 78, 12, P["base"])
    s.ellipse(50, 56, 15, 15, P["dark"])
    s.ellipse(50, 56, 11, 11, P["glow"])
    for y in (50, 56, 62):
        s.line([(40, y), (60, y)], 2, P["dark"])
    s.capsule(62, 34, 62, 12, 8, P["dim"])
    s.ellipse(64, 8, 7, 5, P["light"]); s.ellipse(72, 6, 5, 4, P["light"])
    for x in (30, 38, 46):
        s.line([(x, 40), (x, 46)], 2, P["accent"])
    s.capsule(20, 48, 8, 58, 7, P["dim"]); s.capsule(80, 48, 92, 58, 7, P["dim"])
    s.glow(50, 56, 26, P["glow"], 200)


def bs_conductor(s, P):
    s.poly([(34, 40), (66, 40), (78, 94), (22, 94)], P["base"])
    s.poly([(34, 40), (66, 40), (62, 62), (38, 62)], P["dim"])
    s.line([(50, 44), (50, 92)], 1.6, P["accent"])
    s.ellipse(50, 30, 9, 10, P["light"])
    s.poly([(50, 22), (36, 24), (34, 30), (66, 30), (64, 24)], P["dark"])
    s.rrect(44, 12, 56, 20, 2, P["dark"])
    s.ellipse(47, 31, 2, 2, P["eye"]); s.ellipse(53, 31, 2, 2, P["eye"])
    s.rrect(46, 36, 54, 40, 1, P["dark"])
    s.capsule(24, 52, 10, 40, 4, P["dim"])
    s.capsule(78, 52, 90, 40, 4, P["dim"])
    s.rrect(84, 26, 98, 42, 2, P["dark"]); s.rrect(86, 29, 96, 39, 1.4, P["glow"])
    s.capsule(84, 34, 92, 34, 2, P["accent"])
    s.glow(91, 34, 16, P["glow"], 210)
    for y in (56, 64, 72):
        s.ellipse(50, y, 1.6, 1.6, P["accent"])


def bs_glacier(s, P):
    s.poly([(10, 44), (90, 44), (78, 74), (22, 74)], P["base"])
    for x in (22, 34, 46, 58, 70, 82):
        s.poly([(x, 74), (x + 4, 60), (x + 8, 74)], P["light"])
    s.poly([(14, 44), (86, 44), (74, 24), (26, 24)], P["dim"])
    for x in (24, 38, 52, 66, 78):
        s.poly([(x, 24), (x + 4, 12), (x + 8, 24)], P["accent"])
    s.ellipse(34, 36, 3.4, 3.2, P["eye"]); s.ellipse(66, 36, 3.4, 3.2, P["eye"])
    for i in range(3):
        s.line([(28 + i * 2, 50 + i * 6), (72 - i * 2, 48 + i * 6)], 2, P["dim"])
    s.poly([(4, 40), (18, 30), (16, 50)], P["accent"])
    s.poly([(96, 40), (82, 30), (84, 50)], P["accent"])
    s.glow(50, 46, 26, P["glow"], 110)


def bs_rime_engine(s, P):
    s.rrect(18, 60, 82, 90, 4, P["rail"])
    s.poly([(24, 46), (76, 46), (86, 72), (14, 72)], P["base"])
    for x in (20, 28, 36, 44, 52, 60, 68, 76):
        s.line([(x, 70), (x + 2, 62)], 2.4, P["light"])
    s.rrect(30, 26, 70, 60, 8, P["dim"])
    s.ellipse(50, 40, 12, 12, P["glow"])
    s.ellipse(50, 40, 8, 8, P["glow2"])
    s.rrect(44, 8, 56, 26, 3, P["dark"])
    s.ellipse(50, 6, 7, 5, P["light"]); s.ellipse(60, 4, 5, 4, P["light"])
    s.rrect(16, 34, 30, 48, 3, P["dark"]); s.capsule(16, 41, 6, 41, 4, P["accent"])
    s.rrect(70, 34, 84, 48, 3, P["dark"]); s.capsule(84, 41, 94, 41, 4, P["accent"])
    s.rrect(44, 74, 56, 88, 2, P["dark"])
    s.glow(50, 40, 30, P["glow"], 150)


def bs_mireheart(s, P):
    for x0 in (18, 30, 50, 70, 82):
        s.poly([(x0, 74), (x0 + 3, 96), (x0 + 7, 74)], P["dim"])
    s.capsule(46, 46, 54, 80, 18, P["light"])
    s.ellipse(50, 34, 34, 18, P["base"])
    s.ellipse(50, 38, 30, 10, P["dim"])
    for x in (26, 40, 62, 74):
        s.ellipse(x, 26, 4.2, 3, P["accent"])
    s.ellipse(50, 60, 12, 12, P["dim"])
    s.ellipse(50, 60, 8, 8, P["glow"])
    for a, b in (((18, 52), (6, 40)), ((82, 52), (94, 40)), ((30, 66), (20, 84)), ((70, 66), (80, 84))):
        s.line([a, ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2 + 4), b], 2.2, P["accent"])
    s.glow(50, 34, 30, P["glow"], 120)
    s.glow(50, 60, 18, P["glow"], 180)


def bs_hollow_queen(s, P):
    s.spikes(50, 24, 8, 18, 7, P["accent"], rot=-90, arc=180)
    s.ellipse(50, 30, 9, 9, P["light"])
    s.ellipse(47, 30, 2, 2, P["eye"]); s.ellipse(53, 30, 2, 2, P["eye"])
    s.ellipse(50, 52, 15, 13, P["base"])
    s.ellipse(50, 72, 24, 19, P["dim"])
    for i, y in enumerate((64, 72, 80)):
        s.line([(28, y), (72, y + 1)], 2, P["base"])
    for x, y in ((42, 70), (58, 70), (50, 80)):
        s.ellipse(x, y, 4, 5, P["dark"]); s.ellipse(x, y + 1, 2.2, 2.6, P["glow"])
    for a, b in (((38, 44), (18, 34)), ((62, 44), (82, 34)), ((40, 52), (22, 60)), ((60, 52), (78, 60))):
        s.capsule(a[0], a[1], b[0], b[1], 3.2, P["base"])
        s.ellipse(b[0], b[1], 3, 3, P["light"])
    s.glow(50, 74, 24, P["glow"], 130)


def bs_iron_choir(s, P):
    s.capsule(10, 40, 90, 40, 5, P["dim"])
    for x in (10, 50, 90):
        s.capsule(x, 40, x, 90, 4, P["dim"])
    for i, (cx, w, h) in enumerate(((22, 15, 30), (50, 18, 38), (78, 15, 30))):
        top = 40 - h / 2
        s.rrect(cx - w, top, cx + w, top + h, w * 0.9, P["base"] if i != 1 else P["dim"])
        s.ellipse(cx - w * 0.35, top + h * 0.42, 2.2, 2.2, P["eye"])
        s.ellipse(cx + w * 0.35, top + h * 0.42, 2.2, 2.2, P["eye"])
        s.rrect(cx - w * 0.45, top + h * 0.68, cx + w * 0.45, top + h * 0.78, 2, P["dark"])
        s.ellipse(cx, top + h * 0.74, 3, 2.4, P["glow"])
        s.glow(cx, top + h * 0.74, 12, P["glow"], 150)
        if i < 2:
            for k in range(3):
                s.ellipse(cx + w + 4 + k * 3, 58, 1.8, 1.8, P["accent"])


def bs_nightmail(s, P):
    s.ellipse(50, 70, 34, 20, P["dim"])
    s.rrect(24, 44, 76, 86, 6, P["base"])
    s.rrect(44, 24, 76, 52, 5, P["dim"])
    s.ellipse(34, 58, 7, 6, P["glow"]); s.ellipse(46, 58, 7, 6, P["glow"])
    s.rrect(28, 62, 40, 78, 2, P["dark"])
    s.capsule(28, 30, 28, 16, 6, P["dim"])
    s.ellipse(24, 12, 7, 5, P["light"]); s.ellipse(32, 8, 5, 4, P["light"])
    for x in (20, 36, 52, 68, 84):
        s.ellipse(x, 92, 1.8, 1.8, P["glow"])
    for i in range(5):
        s.ellipse(56 + i * 6, 40 - i * 2, 2, 2, P["accent"])
    s.glow(50, 66, 34, P["glow"], 150)
    s.glow(40, 58, 14, P["glow"], 220)


BOSSES = [
    ("the_inferno", "The Inferno", "burning-metropolis",
     "Giant infected juggernaut: molten seams open in its chest between attacks.",
     dict(base=hx("#3E6B4E"), dark=hx("#16241B"), light=hx("#8FBF84"), accent=hx("#FF7A3C"),
          eye=hx("#FFD9A0"), glow=hx("#FF5A1E"), glow2=hx("#FFC24D"), dim=hx("#2C4B39"),
          plate=hx("#C8C3B4")), bs_inferno),
    ("railmaw", "Railmaw", "burning-metropolis",
     "A worm that lives under the ballast and wears the track as teeth.",
     dict(base=hx("#7A6350"), dark=hx("#241C14"), light=hx("#C9AE8A"), accent=hx("#D9D2C0"),
          eye=hx("#FFE9A8"), glow=hx("#FF8A3C"), dim=hx("#57452F"), rail=hx("#E04B3C")), bs_railmaw),
    ("slagheart", "Slagheart", "burning-metropolis",
     "Walking furnace: the grate is the weak point, and it knows it.",
     dict(base=hx("#6B4A3E"), dark=hx("#1E1410"), light=hx("#C08A6E"), accent=hx("#FFB25C"),
          eye=hx("#FFF0C0"), glow=hx("#FF7A1E"), dim=hx("#4A3229")), bs_slagheart),
    ("the_conductor", "The Conductor", "burning-metropolis",
     "Undead stationmaster with a signal lamp; calls trains that were never built.",
     dict(base=hx("#2C4050"), dark=hx("#0E161D"), light=hx("#7E9CAE"), accent=hx("#D8B24A"),
          eye=hx("#9FFFE0"), glow=hx("#5CFFCF"), dim=hx("#1E2E39")), bs_conductor),
    ("glacier_maw", "Glacier Maw", "frozen-canyon",
     "A jaw the size of a tunnel, with icicle teeth that regrow every phase.",
     dict(base=hx("#6E9CB8"), dark=hx("#14242F"), light=hx("#DCF2FA"), accent=hx("#9BE8FF"),
          eye=hx("#EAFDFF"), glow=hx("#63C8FF"), dim=hx("#3F6A82")), bs_glacier),
    ("rime_engine", "Rime Engine", "frozen-canyon",
     "A train like yours, grown over with ice and aimed the wrong way.",
     dict(base=hx("#5A7E92"), dark=hx("#141F27"), light=hx("#CFE8F4"), accent=hx("#FF6B57"),
          eye=hx("#FFFFFF"), glow=hx("#8FE4FF"), glow2=hx("#EAFBFF"), dim=hx("#36505F"),
          rail=hx("#2A3740")), bs_rime_engine),
    ("mireheart", "Mireheart", "toxic-swamp",
     "Fungal colossus; its cap breathes spore clouds that hide the horde.",
     dict(base=hx("#6E4C7A"), dark=hx("#1E1424"), light=hx("#C89BD4"), accent=hx("#B6FF4D"),
          eye=hx("#EAFFC0"), glow=hx("#9CFF3C"), dim=hx("#4C3556")), bs_mireheart),
    ("hollow_queen", "Hollow Queen", "toxic-swamp",
     "A sac on legs that births swarmlings until you break the eggs.",
     dict(base=hx("#8A5A6E"), dark=hx("#241419"), light=hx("#D8A0AE"), accent=hx("#FFD06B"),
          eye=hx("#FFF0D0"), glow=hx("#FF8A5C"), dim=hx("#5F3B48")), bs_hollow_queen),
    ("iron_choir", "Iron Choir", "frozen-canyon",
     "Three bell-torsos on one frame: kill them in order or the dead one sings.",
     dict(base=hx("#5F6E7E"), dark=hx("#151D24"), light=hx("#B9C8D4"), accent=hx("#E8C35A"),
          eye=hx("#FFF3C4"), glow=hx("#FFD27A"), dim=hx("#3E4A56")), bs_iron_choir),
    ("nightmail", "The Nightmail", "toxic-swamp",
     "A ghost locomotive that phases through the swamp — and through you.",
     dict(base=hx("#3A4C6B"), dark=hx("#101725"), light=hx("#9FB6E0"), accent=hx("#8CD8FF"),
          eye=hx("#EAF6FF"), glow=hx("#6FC8FF"), dim=hx("#26334A")), bs_nightmail),
]


# ------------------------------------------------------------------ projectiles
def pr_bullet(s, P):
    s.capsule(30, 50, 74, 50, 7, P["base"])
    s.ellipse(74, 50, 4.5, 4.5, P["light"])
    for i, y in enumerate((44, 50, 56)):
        s.line([(34 - i * 4, y), (14 - i * 6, y)], 1.8, P["accent"])
    s.glow(72, 50, 16, P["glow"], 150)


def pr_rocket(s, P):
    s.capsule(30, 50, 70, 50, 16, P["base"])
    s.poly([(70, 38), (92, 50), (70, 62)], P["light"])
    s.poly([(30, 42), (18, 34), (26, 50), (18, 66), (30, 58)], P["dim"])
    s.poly([(26, 50), (8, 42), (14, 50), (8, 58)], P["glow2"])
    s.ellipse(60, 50, 5, 5, P["glow"])
    s.glow(12, 50, 20, P["glow"], 190)


def pr_fire(s, P):
    s.ellipse(52, 50, 24, 22, P["dim"])
    s.ellipse(56, 50, 18, 16, P["base"])
    s.ellipse(62, 50, 11, 10, P["light"])
    for a, r in ((-150, 26), (-190, 22), (-215, 18)):
        s.poly([(52, 50), (52 + math.cos(math.radians(a)) * r, 50 + math.sin(math.radians(a)) * r),
                (52 + math.cos(math.radians(a + 18)) * r * 0.8, 50 + math.sin(math.radians(a + 18)) * r * 0.8)],
               P["base"])
    s.ellipse(24, 36, 3, 3, P["light"]); s.ellipse(18, 60, 2.4, 2.4, P["light"])
    s.glow(54, 50, 30, P["glow"], 170)


def pr_acid(s, P):
    s.poly([(30, 42), (58, 36), (76, 50), (60, 64), (34, 60)], P["base"])
    s.ellipse(52, 48, 8, 7, P["light"])
    for x, y, r in ((32, 66, 4), (44, 68, 3), (68, 62, 3.4)):
        s.ellipse(x, y, r, r * 1.2, P["base"])
    for x, y in ((44, 40), (62, 44), (52, 54)):
        s.ellipse(x, y, 2.2, 2.2, P["light"])
    s.glow(52, 50, 26, P["glow"], 160)


def pr_tesla(s, P):
    pts = [(12, 50), (28, 34), (38, 54), (52, 28), (60, 52), (74, 36), (88, 50)]
    s.line(pts, 5.5, P["base"])
    s.line(pts, 2.2, P["light"])
    s.line([(38, 54), (30, 74), (18, 80)], 2.6, P["base"])
    s.line([(60, 52), (64, 74), (56, 84)], 2.2, P["base"])
    s.glow(50, 50, 34, P["glow"], 170)


def pr_pellet(s, P):
    for i, (x, y) in enumerate(((30, 34), (44, 50), (58, 66))):
        s.capsule(x, y, x + 14, y, 6, P["base"])
        s.ellipse(x + 14, y, 3.4, 3.4, P["light"])
        s.line([(x - 2, y), (x - 14, y)], 1.6, P["accent"])
    s.glow(50, 50, 26, P["glow"], 110)


def pr_bone(s, P):
    s.poly([(20, 50), (44, 40), (82, 50), (44, 58)], P["base"])
    s.ellipse(20, 50, 5, 6, P["light"])
    s.poly([(58, 44), (66, 38), (60, 50)], P["dim"])
    s.poly([(58, 54), (66, 62), (60, 50)], P["dim"])
    s.glow(70, 50, 16, P["glow"], 90)


PROJECTILES = [
    ("bullet_tracer", "Tracer round", "kinetic", "Cannon + gatling. Fast, no splash, pierces 1.",
     dict(base=hx("#F7D98A"), light=hx("#FFFBEA"), accent=hx("#FFB347"), dark=hx("#3A2A12"),
          dim=hx("#C9A45C"), glow=hx("#FFC85A")), pr_bullet, 256),
    ("rocket", "Rocket", "blast", "Rocket launcher. Arcs slightly, big splash, slow.",
     dict(base=hx("#B85A3A"), light=hx("#E3A07A"), accent=hx("#FFD9A0"), dark=hx("#2A1610"),
          dim=hx("#8A3F28"), glow=hx("#FF8A2E"), glow2=hx("#FFE07A")), pr_rocket, 256),
    ("fireball", "Fireball", "fire", "Flamer / fire card. Ignites ground, damage over time.",
     dict(base=hx("#FF8A2E"), light=hx("#FFE07A"), accent=hx("#FFB347"), dark=hx("#3A1206"),
          dim=hx("#C23C12"), glow=hx("#FF6A1E")), pr_fire, 256),
    ("acid_glob", "Acid glob", "acid", "Acid turret + spitters. Shreds armour, leaves a pool.",
     dict(base=hx("#A8D34A"), light=hx("#E6FF9A"), accent=hx("#6FA82E"), dark=hx("#1A2A0C"),
          dim=hx("#7FA832"), glow=hx("#9CFF3C")), pr_acid, 256),
    ("tesla_arc", "Tesla arc", "electric", "Tesla coil. Chains to a second target, stuns briefly.",
     dict(base=hx("#9FE4FF"), light=hx("#EAFFFF"), accent=hx("#63C8FF"), dark=hx("#0E2436"),
          dim=hx("#5FA8CC"), glow=hx("#7FDFFF")), pr_tesla, 256),
    ("shotgun_pellet", "Pellet spread", "kinetic", "Shotgun. 5 pellets, wide cone, close range.",
     dict(base=hx("#F2D9A0"), light=hx("#FFF6DC"), accent=hx("#FFC46B"), dark=hx("#2E2412"),
          dim=hx("#BFA06A"), glow=hx("#FFD98A")), pr_pellet, 256),
    ("bone_spike", "Bone spike", "kinetic", "Enemy ranged attack (spitter, conductor, queen).",
     dict(base=hx("#E8E0C8"), light=hx("#FFFDF2"), accent=hx("#C8B48A"), dark=hx("#2A2418"),
          dim=hx("#C4B79A"), glow=hx("#FFE9B0")), pr_bone, 256),
]


# ------------------------------------------------------------------ kill FX
def fx_impact(s, P):
    s.spikes(50, 50, 6, 26, 9, P["base"], rot=-90)
    s.ellipse(50, 50, 12, 12, P["light"])
    for a in range(0, 360, 45):
        x, y = 50 + math.cos(math.radians(a)) * 34, 50 + math.sin(math.radians(a)) * 34
        s.line([(50 + math.cos(math.radians(a)) * 18, 50 + math.sin(math.radians(a)) * 18), (x, y)], 1.6, P["accent"])
    s.glow(50, 50, 30, P["glow"], 190)


def fx_fire(s, P):
    for a in range(0, 360, 40):
        r = 30 if a % 80 == 0 else 22
        x, y = 50 + math.cos(math.radians(a)) * r, 50 + math.sin(math.radians(a)) * r
        s.ellipse(x, y, 12, 11, P["dim"])
    s.ellipse(50, 50, 26, 24, P["base"])
    s.ellipse(50, 50, 15, 14, P["light"])
    for x, y in ((28, 30), (70, 34), (36, 70), (66, 68)):
        s.ellipse(x, y, 3, 3, P["light"])
    s.glow(50, 50, 38, P["glow"], 200)


def fx_blast(s, P):
    for r, w in ((40, 3.4), (30, 2.6), (20, 2)):
        s.d.ellipse([s._xy(50 - r, 50 - r)[0], s._xy(50 - r, 50 - r)[1],
                     s._xy(50 + r, 50 + r)[0], s._xy(50 + r, 50 + r)[1]], outline=P["accent"] + (255,), width=int(w * s.size * SS / 100))
    for a in (20, 70, 130, 200, 260, 310):
        x, y = 50 + math.cos(math.radians(a)) * 34, 50 + math.sin(math.radians(a)) * 34
        s.poly([(x, y), (x + 7, y - 3), (x + 5, y + 6)], P["light"])
    s.ellipse(50, 50, 16, 15, P["base"])
    s.ellipse(50, 50, 9, 8, P["light"])
    s.glow(50, 50, 40, P["glow"], 210)


def fx_acid(s, P):
    s.poly([(50, 26), (72, 40), (68, 62), (52, 74), (32, 62), (28, 42)], P["base"])
    s.ellipse(50, 50, 13, 12, P["light"])
    for x, y, r in ((24, 36, 4), (76, 40, 5), (30, 70, 3.4), (70, 72, 3)):
        s.ellipse(x, y, r, r * 1.15, P["base"])
    s.ellipse(50, 82, 22, 5, P["dim"])
    s.glow(50, 52, 34, P["glow"], 170)


def fx_electric(s, P):
    for a in (10, 80, 150, 220, 290):
        x, y = 50 + math.cos(math.radians(a)) * 40, 50 + math.sin(math.radians(a)) * 40
        mx, my = 50 + math.cos(math.radians(a + 12)) * 24, 50 + math.sin(math.radians(a + 12)) * 24
        s.line([(50, 50), (mx, my), (x, y)], 3.2, P["base"])
    s.line([(50, 50), (60, 30), (52, 18)], 2.2, P["light"])
    s.ellipse(50, 50, 10, 10, P["light"])
    s.glow(50, 50, 40, P["glow"], 210)


def fx_goo(s, P):
    s.poly([(50, 20), (70, 40), (66, 66), (54, 78), (38, 70), (30, 48)], P["base"])
    for x, y, r in ((30, 28, 5), (72, 30, 4), (76, 62, 3.6), (26, 62, 3), (50, 86, 4)):
        s.ellipse(x, y, r, r, P["dim"])
    s.ellipse(50, 46, 12, 11, P["light"])
    s.glow(50, 50, 28, P["glow"], 130)


def fx_metal(s, P):
    for a, r in ((15, 36), (75, 30), (140, 34), (210, 30), (280, 34)):
        x, y = 50 + math.cos(math.radians(a)) * r, 50 + math.sin(math.radians(a)) * r
        s.poly([(x, y), (x + 10, y - 6), (x + 12, y + 6), (x + 2, y + 9)], P["light"] if a % 2 else P["base"])
    s.ellipse(50, 50, 14, 13, P["base"])
    s.ellipse(50, 50, 6, 6, P["dark"])
    s.glow(50, 50, 26, P["glow"], 120)


def fx_ice(s, P):
    for a in (0, 60, 120, 180, 240, 300):
        x, y = 50 + math.cos(math.radians(a)) * 36, 50 + math.sin(math.radians(a)) * 36
        s.poly([(50, 50), (x, y), (x + 8, y + 10)], P["light"] if a % 120 else P["base"])
    s.ellipse(50, 50, 12, 12, P["light"])
    s.glow(50, 50, 34, P["glow"], 170)


def fx_bone(s, P):
    for a in (30, 110, 200, 290):
        x, y = 50 + math.cos(math.radians(a)) * 30, 50 + math.sin(math.radians(a)) * 30
        s.capsule(50, 50, x, y, 4, P["base"])
        s.ellipse(x, y, 4.4, 4, P["light"])
    s.ellipse(50, 50, 10, 9, P["light"])
    s.glow(50, 50, 22, P["glow"], 90)


def fx_swarm(s, P):
    random.seed(3)
    for i in range(18):
        a = random.uniform(0, 360)
        r = random.uniform(14, 40)
        x, y = 50 + math.cos(math.radians(a)) * r, 50 + math.sin(math.radians(a)) * r
        s.ellipse(x, y, random.uniform(2.5, 4.5), random.uniform(2, 3.5), P["base"] if i % 3 else P["light"])
    s.glow(50, 50, 30, P["glow"], 110)


FX = [
    ("impact_kinetic", "kinetic impact", dict(base=hx("#FFE9A8"), light=hx("#FFFFFF"), accent=hx("#FFC24D"),
                                              dark=hx("#2A2010"), dim=hx("#C9A45C"), glow=hx("#FFD06B")), fx_impact),
    ("fire_burst", "fire burst", dict(base=hx("#FF8A2E"), light=hx("#FFE07A"), accent=hx("#FFC24D"),
                                      dark=hx("#3A1206"), dim=hx("#C23C12"), glow=hx("#FF5A1E")), fx_fire),
    ("blast_ring", "blast / rocket", dict(base=hx("#E8C79A"), light=hx("#FFF3DC"), accent=hx("#FFB347"),
                                          dark=hx("#2A1A0E"), dim=hx("#B08A5E"), glow=hx("#FF9A3C")), fx_blast),
    ("acid_splash", "acid splash", dict(base=hx("#A8D34A"), light=hx("#E6FF9A"), accent=hx("#6FA82E"),
                                        dark=hx("#1A2A0C"), dim=hx("#7FA832"), glow=hx("#9CFF3C")), fx_acid),
    ("electric_burst", "electric burst", dict(base=hx("#9FE4FF"), light=hx("#EAFFFF"), accent=hx("#63C8FF"),
                                              dark=hx("#0E2436"), dim=hx("#5FA8CC"), glow=hx("#7FDFFF")), fx_electric),
    ("death_goo", "flesh / goo death", dict(base=hx("#8FBF6A"), light=hx("#D8F0A8"), accent=hx("#4E7A34"),
                                            dark=hx("#1A2410"), dim=hx("#6E9150"), glow=hx("#A8FF6B")), fx_goo),
    ("death_metal", "armoured death", dict(base=hx("#8E939C"), light=hx("#DCE2EA"), accent=hx("#FF8A3C"),
                                           dark=hx("#191C21"), dim=hx("#5E646E"), glow=hx("#FFB25C")), fx_metal),
    ("death_ice", "frozen shatter", dict(base=hx("#9FE4FF"), light=hx("#F4FDFF"), accent=hx("#63C8FF"),
                                         dark=hx("#0E2436"), dim=hx("#6FA8C4"), glow=hx("#8FE4FF")), fx_ice),
    ("death_bone", "skeletal burst", dict(base=hx("#E8E0C8"), light=hx("#FFFDF2"), accent=hx("#BFAE8A"),
                                          dark=hx("#241E14"), dim=hx("#C4B79A"), glow=hx("#FFE9B0")), fx_bone),
    ("death_swarm", "swarm scatter", dict(base=hx("#9A6A3E"), light=hx("#D9A86A"), accent=hx("#C0512E"),
                                          dark=hx("#20140A"), dim=hx("#7A5230"), glow=hx("#FF8A3C")), fx_swarm),
]


# ------------------------------------------------------------------ stats
ENEMY_STATS = {
 "crawler": dict(hp=22, speed=4.4, dmg=4, xp=4, armor=0, pack="3–5", lane="rails",
   behavior=["lowers to 0.25 m tall when the train is within 8 m", "slides under the cowcatcher — ram does nothing",
             "sprints straight down the rails, ignores barricades"],
   counters=["any bullet", "shotgun close range", "splash"], threat="low"),
 "bloater": dict(hp=60, speed=2.2, dmg=18, xp=12, armor=1, pack="1–2", lane="rails",
   behavior=["fuse glows 1.2 s before detonation", "explodes on contact OR on death — 3 m radius",
             "blast also damages other enemies"],
   counters=["kill it early (chain reaction)", "rocket from range"], threat="medium"),
 "spitter": dict(hp=45, speed=2.8, dmg=7, xp=10, armor=0, pack="1–3", lane="embankment",
   behavior=["stops at 25 m and lobs acid globs in an arc over barricades", "repositions when the train brakes",
             "globs leave a pool for 2 s"],
   counters=["shoot at range", "ram it before it settles", "brake to break its lead"], threat="medium"),
 "shrieker": dict(hp=70, speed=2.6, dmg=0, xp=15, armor=0, pack="1", lane="rear",
   behavior=["aura: +15 % movement speed to enemies within 12 m", "screams every 8 s, spawning 6 walkers",
             "scream is a 1.5 s telegraph you can hear"],
   counters=["focus it first", "rocket into the spawn point"], threat="medium"),
 "bulwark": dict(hp=110, speed=2.4, dmg=12, xp=18, armor=3, pack="1–2", lane="rails",
   behavior=["carries a rail sign plate: immune to damage from the front cone (±50°)",
             "turns slowly — flank or let it pass", "plate breaks under sustained fire (600 dmg) and stays broken"],
   counters=["bullets that curve in from a side turret", "ram from the side", "fire DoT ignores the plate"], threat="high"),
 "glider": dict(hp=35, speed=7.0, dmg=9, xp=9, armor=0, pack="2–4", lane="air",
   behavior=["ignores barricades and rails, flies in a sine wave", "lands on a wagon and chews a gun slot (3 s disable)",
             "cannot be rammed while airborne"],
   counters=["tesla chain", "shotgun as it lands", "kill before it reaches the convoy"], threat="medium"),
 "drillmaw": dict(hp=90, speed=3.0, dmg=15, xp=16, armor=2, pack="1", lane="underground",
   behavior=["burrows at 5 m/s, untargetable, dust trail visible", "surfaces 8 m ahead of the train",
             "on surfacing it lunges 6 m — hit it during the 0.6 s wind-up"],
   counters=["time the brake", "watch the dust trail", "splash at the surface point"], threat="high"),
 "mites": dict(hp=6, speed=5.2, dmg=2, xp=1, armor=0, pack="6", lane="rails",
   behavior=["six bodies, one spawn", "each bite stacks armour shred (−10 % for 5 s)",
             "spread around the train instead of attacking one point"],
   counters=["splash (rocket, fire trail)", "shotgun", "ram kills one per hit"], threat="low"),
 "lantern": dict(hp=85, speed=2.0, dmg=6, xp=20, armor=0, pack="1", lane="rear",
   behavior=["raises one dead enemy every 6 s as a husk at 50 % HP", "husks collapse when it dies",
             "lamp glow lights the area — it is always visible in fog"],
   counters=["rush it", "piercing rounds through the husks", "fire trail denies the corpse line"], threat="high"),
 "rimeback": dict(hp=140, speed=1.8, dmg=16, xp=22, armor=4, pack="1", lane="rails",
   behavior=["on hit: chills the throttle (−35 % for 2 s)", "ice armour cracks after 3 hits, then takes double damage",
             "slow turn, wide sweep — the ram is a real option"],
   counters=["fire (melts armour fastest)", "ram then shoot the cracked plates"], threat="high"),
}

BOSS_STATS = {
 "the_inferno": dict(hp=2400, arena="straight avenue, 2 barricade lanes", reward="+1 wagon slot, 400 XP",
   entry="stand-off at 30 m; it walks, never runs",
   phases=[("Molten Walk", 1.0, ["sweeping arm slam (2.5 m)", "ember spit: 3 slow fireballs"]),
           ("Vent", 0.55, ["chest vents open for 6 s — weak point ×3", "lava trail follows its feet"]),
           ("Overheat", 0.25, ["enraged: double slam, embers become a ring", "arena starts burning from the edges"])],
   weakness="chest vent only in phase 2+", attacks_note="every attack has a 0.8 s wind-up you can brake out of"),
 "railmaw": dict(hp=1900, arena="long straight, rails twist", reward="repair kit, 350 XP",
   entry="burrows ahead, surfaces behind the train",
   phases=[("Coil", 1.0, ["bursts from the ballast beside the track", "rail-shard volley, 5 shards in a fan"]),
           ("Squeeze", 0.6, ["coils across the track, blocks the lane for 4 s", "telegraph: rails bend before it rises"]),
           ("Collapse", 0.3, ["digs under and takes the track with it — forced brake", "spawns 4 crawlers from the hole"])],
   weakness="teeth ring: shoot the mouth during the coil", attacks_note="it never attacks from the front — watch the sides"),
 "slagheart": dict(hp=2600, arena="furnace yard, floor vents", reward="fire card, 450 XP",
   entry="strides in from a side street, slower than it looks",
   phases=[("Bellows", 1.0, ["stomp shockwave (4 m)", "vent bursts: 4 flame jets on a timer"]),
           ("Stoke", 0.5, ["opens the grate core — ×4 damage for 4 s, then it slams shut", "chimney drops slag rain"]),
           ("Meltdown", 0.2, ["core exposed permanently but it chases at double speed", "leaves a lava pool wherever it stands"])],
   weakness="the grate", attacks_note="slag rain is telegraphed by the chimney glowing white"),
 "the_conductor": dict(hp=2100, arena="station platform, two switches", reward="card choice ×2, 500 XP",
   entry="stands on the platform; the fight starts when the signal turns red",
   phases=[("Timetable", 1.0, ["semaphore arm signals which lane is safe", "summons a ghost train that runs the opposite lane"]),
           ("Delay", 0.6, ["lamp staff blinds a gun slot for 4 s", "husks rise from the platform"]),
           ("Last Call", 0.25, ["switches the tracks under you — forced A/B pick", "attacks twice as fast, stops signalling"])],
   weakness="lamp staff arm — break it to stop the blind", attacks_note="the semaphore never lies: red means move"),
 "glacier_maw": dict(hp=3000, arena="frozen canyon, ice walls", reward="tesla coil, 550 XP",
   entry="blocks the canyon mouth; you have to break in",
   phases=[("Bite", 1.0, ["lunges the width of the track", "frost breath cone that freezes the nearest turret"]),
           ("Shell", 0.6, ["ice plates: 3 plates, 1 breaks per 400 damage", "falling icicles from the ceiling"]),
           ("Thaw", 0.25, ["armour gone, speed doubled", "the floor becomes slick — braking is halved"])],
   weakness="between plates at the jaw hinge", attacks_note="frost breath is a 1 s inhale — brake out of the cone"),
 "rime_engine": dict(hp=3200, arena="head-on duel on a single track", reward="+1 gun slot per wagon, 600 XP",
   entry="the mirror fight: it comes at you on your own rails",
   phases=[("Head-on", 1.0, ["rams — the only way out is a side branch", "side turrets fire in sync with yours"]),
           ("Boiler Burst", 0.6, ["vents steam that hides the track for 3 s", "drops ice caltrops behind it"]),
           ("Derail", 0.25, ["loses a wheel, drifts across both lanes", "turret fire becomes a continuous beam"])],
   weakness="the headlight — shoot it to blind its aim", attacks_note="it mirrors your speed: brake and it overshoots"),
 "mireheart": dict(hp=2800, arena="swamp clearing, water on both sides", reward="acid turret, 500 XP",
   entry="rooted at first; it pulls itself free at 70 %",
   phases=[("Rooted", 1.0, ["spore clouds: 4 clouds that hide enemies", "tendril sweep across the track"]),
           ("Uproot", 0.7, ["walks on roots, spawns 2 spitters per 10 s", "acid rain pools on the rails"]),
           ("Bloom", 0.3, ["cap bursts: 8 spore mines", "tendrils grab the train and hold it for 2 s"])],
   weakness="the stalk under the cap", attacks_note="spore clouds do not damage — they hide; shoot blind into them"),
 "hollow_queen": dict(hp=2500, arena="nest of egg sacs on the track", reward="rapid fire, 480 XP",
   entry="the nest is already on the track; she is inside it",
   phases=[("Brood", 1.0, ["births 3 swarmlings every 5 s", "shell hardens while birthing (×0.5 damage)"]),
           ("Rupture", 0.6, ["sac bursts — 6 mites, shell softens ×1.5", "web slows the train in a 10 m patch"]),
           ("Last Eggs", 0.3, ["guards 3 eggs; each egg killed removes a swarm wave", "body attacks with 4 arms"])],
   weakness="eggs — they are the real health bar", attacks_note="she cannot move; the danger is the swarm"),
 "iron_choir": dict(hp=3600, arena="bridge with three bell towers", reward="tesla + rocket, 700 XP",
   entry="three torsos on one frame, singing in tune",
   phases=[("Three Voices", 1.0, ["each torso has its own attack pattern: left shockwave, middle beam, right swarm", "kill order is left → right → middle"]),
           ("Discord", 0.6, ["a dead torso keeps attacking for 8 s unless the next is killed fast", "bells ring: screen shake, aim drifts"]),
           ("Silence", 0.2, ["last torso absorbs the others: bigger, faster, all three patterns", "the frame collapses, arena narrows"])],
   weakness="the chain links between torsos (breaks the sync)", attacks_note="listen for which bell rings first"),
 "nightmail": dict(hp=4200, arena="flooded swamp line, ghost rails", reward="nightmail skin + 900 XP",
   entry="it phases through the trees and arrives behind you",
   phases=[("Phantom", 1.0, ["phases out for 2 s, untargetable, then reappears beside the train", "lantern beams that burn the nearest gun slot"]),
           ("Twin", 0.65, ["splits into two: one is an illusion — the real one casts a shadow", "ghost rails force an A/B choice mid-fight"]),
           ("Terminus", 0.3, ["stops phasing, opens both boilers", "chain lightning across the wagons, 3 s telegraph"])],
   weakness="the headlight lanterns — break both to stop the phasing", attacks_note="the illusion never touches the rails"),
}

FX_MAP = {
 "impact": {"kinetic": "impact_kinetic", "fire": "fire_burst", "blast": "blast_ring",
            "acid": "acid_splash", "electric": "electric_burst"},
 "death": {"walker": "death_goo", "sprinter": "death_bone", "tank": "death_metal",
           "crawler": "death_goo", "bloater": "blast_ring", "spitter": "acid_splash",
           "shrieker": "death_bone", "bulwark": "death_metal", "glider": "death_goo",
           "drillmaw": "death_goo", "mites": "death_swarm", "lantern": "death_bone",
           "rimeback": "death_ice"},
}

GUN_MAP = {
 "CANNON": "bullet_tracer", "GATLING": "bullet_tracer", "ROCKET": "rocket", "TESLA": "tesla_arc",
 "FIRE": "fireball", "SHOTGUN": "shotgun_pellet", "ACID": "acid_glob",
}


# ------------------------------------------------------------------ HUD v2
def grad_rrect(w, h, r, c1, c2, horizontal=False):
    im = Image.new("RGBA", (w, h))
    d = ImageDraw.Draw(im)
    n = w if horizontal else h
    for i in range(n):
        t = i / max(1, n - 1)
        col = mix(c1, c2, t)
        if horizontal:
            d.line([(i, 0), (i, h)], fill=col + (255,))
        else:
            d.line([(0, i), (w, i)], fill=col + (255,))
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, w - 1, h - 1], radius=r, fill=255)
    im.putalpha(mask)
    return im


def font(size, bold=True, narrow=False):
    if narrow:
        p = "/usr/share/fonts/truetype/liberation/LiberationSansNarrow-Bold.ttf" if bold else \
            "/usr/share/fonts/truetype/liberation/LiberationSansNarrow-Regular.ttf"
    else:
        p = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" if bold else \
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
    try:
        return ImageFont.truetype(p, size)
    except Exception:
        return ImageFont.load_default()


def mono(size, bold=True):
    p = "/usr/share/fonts/truetype/dejavu/DejaVuSansMono-Bold.ttf" if bold else \
        "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf"
    try:
        return ImageFont.truetype(p, size)
    except Exception:
        return ImageFont.load_default()


def hud_xp_row(level="07", pct=0.42, w=560, h=96):
    """[LV 07] + XP track, level number sits in the bar's left cap (4x of 140x24)."""
    im = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    chip_w = int(w * 0.23)
    track_x = chip_w - 12
    track_w = w - track_x
    # track
    tr = grad_rrect(track_w, int(h * 0.68), int(h * 0.28), hx("#173A55"), hx("#0E2135"))
    im.alpha_composite(tr, (track_x, int(h * 0.16)))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([track_x, int(h * 0.16), w - 1, int(h * 0.84)], radius=int(h * 0.28),
                        outline=hx("#2B5878") + (255,), width=2)
    # fill
    fw = max(6, int((track_w - 10) * pct))
    fl = grad_rrect(fw, int(h * 0.42), int(h * 0.18), hx("#5BE0FF"), hx("#1E8CD8"))
    im.alpha_composite(fl, (track_x + 5, int(h * 0.29)))
    d = ImageDraw.Draw(im)
    d.line([(track_x + 14, int(h * 0.33)), (track_x + fw - 6, int(h * 0.33))],
           fill=(255, 255, 255, 110), width=max(2, int(h * 0.05)))
    # chip
    chip = grad_rrect(chip_w, int(h * 0.92), int(h * 0.26), hx("#1B2433"), hx("#0B1220"))
    im.alpha_composite(chip, (0, int(h * 0.04)))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([0, int(h * 0.04), chip_w - 1, int(h * 0.96)], radius=int(h * 0.26),
                        outline=hx("#FFC24D") + (255,), width=3)
    d.text((14, int(h * 0.16)), "LV", font=mono(int(h * 0.20)), fill=hx("#FFC24D") + (255,))
    f = font(int(h * 0.46), narrow=True)
    tw = d.textlength(level, font=f)
    d.text((chip_w - 16 - tw, int(h * 0.32)), level, font=f, fill=hx("#F2F4EE") + (255,))
    return im


def hud_wagon_slots(w=360, h=140):
    im = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([0, 0, w - 1, h - 1], radius=18, fill=hx("#0E1822") + (240,),
                        outline=hx("#2B5878") + (255,), width=2)
    # wagon glyph
    for i in range(3):
        d.rounded_rectangle([20 + i * 34, 46, 48 + i * 34, 96], radius=8,
                            fill=hx("#1E3348") + (255,), outline=hx("#3E6A8A") + (255,), width=2)
    d.text((20, 20), "WAGON", font=mono(16), fill=hx("#93A3AD") + (255,))
    # slots
    sx = 150
    for i in range(2):
        x = sx + i * 96
        d.rounded_rectangle([x, 34, x + 80, 110], radius=14, fill=hx("#12202E") + (255,),
                            outline=(hx("#FFC24D") if i == 0 else hx("#33506A")) + (255,), width=3 if i == 0 else 2)
        if i == 0:
            d.ellipse([x + 22, 52, x + 58, 88], fill=hx("#2C4258") + (255,), outline=hx("#FFC24D") + (255,), width=2)
            d.rounded_rectangle([x + 46, 66, x + 72, 78], radius=4, fill=hx("#C8D6E2") + (255,))
            d.text((x + 8, 12), "GUN 1", font=mono(13), fill=hx("#FFC24D") + (255,))
        else:
            d.text((x + 8, 12), "GUN 2", font=mono(13), fill=hx("#5B7E9A") + (255,))
            for k in range(0, 74, 12):
                d.line([(x + 6 + k, 104), (x + 12 + k, 104)], fill=hx("#33506A") + (255,), width=2)
            d.text((x + 26, 62), "LOCKED", font=mono(14), fill=hx("#5B7E9A") + (255,))
    return im


# ------------------------------------------------------------------ design cards
def wrap(d, text, f, max_w):
    words, lines, cur = text.split(), [], ""
    for wd in words:
        t = (cur + " " + wd).strip()
        if d.textlength(t, font=f) <= max_w:
            cur = t
        else:
            lines.append(cur)
            cur = wd
    if cur:
        lines.append(cur)
    return lines


def card(portrait_path, title, subtitle, kind, pal, stats, idx, total, biome="", phases=None, footer=""):
    W, H = 640, 906
    im = Image.new("RGB", (W, H), (9, 12, 17))
    d = ImageDraw.Draw(im)
    # deep tinted backdrop
    top = mix(shade(pal["dark"], 1.35), (9, 12, 17), 0.25)
    for y in range(H):
        t = min(1.0, y / (H * 0.7))
        d.line([(0, y), (W, y)], fill=mix(top, (8, 10, 14), t))
    for x in range(0, W, 40):
        d.line([(x, 0), (x, H)], fill=mix(top, (255, 255, 255), 0.05))
    # accent glow behind the portrait
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.ellipse([W // 2 - 210, 60, W // 2 + 210, 540], fill=pal["glow"] + (46,))
    im.paste(Image.alpha_composite(im.convert("RGBA"), glow.filter(ImageFilter.GaussianBlur(60))).convert("RGB"), (0, 0))
    d = ImageDraw.Draw(im)

    d.rectangle([0, 0, W, 5], fill=pal["accent"])
    d.text((36, 30), "%s  ·  %02d/%02d" % (kind, idx, total), font=mono(15), fill=pal["accent"])
    tag = biome.replace("-", " ").upper() if biome else "ALL BIOMES"
    d.text((W - 36 - d.textlength(tag, font=mono(13)), 32), tag, font=mono(13), fill=(142, 158, 170))

    p = Image.open(portrait_path).convert("RGBA")
    p.thumbnail((400, 400))
    im.paste(p, ((W - p.size[0]) // 2, 92), p)
    d = ImageDraw.Draw(im)

    d.text((34, 540), title.upper(), font=font(50, narrow=True), fill=(244, 246, 240))
    d.line([(36, 604), (W - 36, 604)], fill=pal["accent"], width=2)
    fsub = font(19, bold=False)
    y = 618
    for ln in wrap(d, subtitle, fsub, W - 72)[:3]:
        d.text((36, y), ln, font=fsub, fill=(150, 166, 178))
        y += 25

    y = 692
    for label, val, mx, col in stats:
        d.text((36, y), label, font=mono(13), fill=(132, 148, 160))
        bx, bw = 150, 330
        d.rounded_rectangle([bx, y + 3, bx + bw, y + 13], radius=5, fill=(20, 29, 40))
        d.rounded_rectangle([bx, y + 3, bx + max(4, int(bw * min(1, val / mx))), y + 13], radius=5, fill=col)
        txt = str(val)
        d.text((bx + bw + 12, y - 1), txt, font=mono(13), fill=(222, 232, 240))
        y += 25

    if phases:
        y = 800
        d.text((36, y), "PHASES", font=mono(12), fill=(132, 148, 160))
        y += 20
        for i, ph in enumerate(phases[:3]):
            cw = (W - 72) // 3
            x = 36 + i * cw
            d.rounded_rectangle([x, y, x + cw - 10, y + 30], radius=6,
                                fill=mix(pal["dark"], (0, 0, 0), 0.2), outline=pal["accent"] + (120,), width=1)
            d.text((x + 8, y + 8), "P%d %s" % (i + 1, ph[:16]), font=mono(11), fill=(226, 236, 244))
    if footer:
        fy = 862 if phases else 806
        d.line([(36, fy - 14), (W - 36, fy - 14)], fill=(255, 255, 255, 30), width=1)
        d.text((36, fy), "COUNTER", font=mono(12), fill=(132, 148, 160))
        for k, ln in enumerate(wrap(d, footer, mono(12, bold=False), W - 200)[:2]):
            d.text((138, fy + k * 18), ln, font=mono(12, bold=False), fill=(196, 208, 218))
    return im


def prompt_for(name, role, biome, kind):
    return ("%s — %s. %s. Top-down 3/4 game sprite, thick dark outline, painterly comic style, "
            "teal-and-ember palette, rim light from the upper left, transparent background, single "
            "subject centred, no text, no UI, 1024x1024." % (name, role, biome))


# ------------------------------------------------------------------ build
def build():
    cat_path = os.path.join(OUT, "meta/catalog.json")
    cat = json.load(open(cat_path))
    keep = [a for a in cat["assets"] if not (
        a["path"].startswith(("enemies/", "bosses/", "projectiles/", "fx/", "data/")) or
        a["id"].startswith(("hud_xp_level_row", "hud_level_chip", "hud_wagon_slots")))]
    new = []

    def reg(path, category, tags, role, notes=""):
        try:
            im = Image.open(path)
            w, h, al = im.size[0], im.size[1], im.mode in ("RGBA", "LA")
        except Exception:
            w, h, al = 0, 0, False
        new.append({"id": os.path.splitext(os.path.basename(path))[0], "path": os.path.relpath(path, OUT),
                    "category": category, "tags": tags, "role": role, "w": w, "h": h,
                    "alpha": al, "source": "meta/gen_expansion.py", "notes": notes})

    # enemies
    en_paths = []
    for i, (eid, name, role, pal, fn) in enumerate(ENEMIES, 1):
        im = render(512, fn, pal)
        p = save(im, "enemies", "en_%s.png" % eid)
        en_paths.append(p)
        reg(p, "enemies", ["enemy", eid], name)
        cp = save(card(p, name, role, "NORMAL ENEMY", pal,
                       [("HP", ENEMY_STATS[eid]["hp"], 150, hx("#E05C4A")),
                        ("SPEED", ENEMY_STATS[eid]["speed"], 7.0, hx("#5BE0FF")),
                        ("DMG", ENEMY_STATS[eid]["dmg"], 20, hx("#FFC24D")),
                        ("XP", ENEMY_STATS[eid]["xp"], 25, hx("#A8FF6B"))], i, len(ENEMIES), "",
                       footer=" · ".join(ENEMY_STATS[eid]["counters"])),
                   "enemies", "cards", "card_%s.png" % eid)
        reg(cp, "enemies/cards", ["card", "enemy", eid], "design card for %s" % name)
    contact(en_paths, os.path.join(OUT, "sheets", "enemies.png"), cols=5, title="ENEMIES — 10 new normal types")

    # bosses
    bs_paths = []
    for i, (bid, name, biome, role, pal, fn) in enumerate(BOSSES, 1):
        im = render(768, fn, pal)
        p = save(im, "bosses", "boss_%s.png" % bid)
        bs_paths.append(p)
        reg(p, "bosses", ["boss", bid, biome], name)
        ph = len(BOSS_STATS[bid]["phases"])
        cp = save(card(p, name, role, "BOSS · %d PHASES" % ph, pal,
                       [("HP", BOSS_STATS[bid]["hp"], 4500, hx("#E05C4A")),
                        ("PHASES", ph, 3, hx("#5BE0FF")),
                        ("XP", {"the_inferno": 400, "railmaw": 350, "slagheart": 450, "the_conductor": 500,
                                "glacier_maw": 550, "rime_engine": 600, "mireheart": 500, "hollow_queen": 480,
                                "iron_choir": 700, "nightmail": 900}[bid], 900, hx("#A8FF6B"))],
                       i, len(BOSSES), biome, phases=[p_[0] for p_ in BOSS_STATS[bid]["phases"]],
                       footer=BOSS_STATS[bid]["weakness"]),
                   "bosses", "cards", "card_%s.png" % bid)
        reg(cp, "bosses/cards", ["card", "boss", bid], "design card for %s" % name)
    contact(bs_paths, os.path.join(OUT, "sheets", "bosses.png"), cols=5, cell=300, title="BOSSES — 10")

    # projectiles
    pr_paths = []
    for pid, name, dtype, role, pal, fn, size in PROJECTILES:
        im = render(size, fn, pal, glow_scale=0.55, shadow=False)
        p = save(im, "projectiles", "proj_%s.png" % pid)
        pr_paths.append(p)
        reg(p, "projectiles", ["projectile", dtype, pid], name)
    contact(pr_paths, os.path.join(OUT, "sheets", "projectiles.png"), cols=4, title="PROJECTILES")

    # fx
    fx_paths = []
    for fid, name, pal, fn in FX:
        im = render(256, fn, pal, glow_scale=0.5, shadow=False)
        p = save(im, "fx", "fx_%s.png" % fid)
        fx_paths.append(p)
        reg(p, "fx", ["fx", "death" if fid.startswith("death") else "impact", fid], name)
    contact(fx_paths, os.path.join(OUT, "sheets", "fx.png"), cols=5, title="KILL / IMPACT FX")

    # hud v2
    p = save(hud_xp_row(), "ui", "hud", "hud_xp_level_row.png")
    reg(p, "ui/hud", ["hud", "xp", "level"], "XP bar with the level number fused into its left cap")
    p2 = save(hud_xp_row(level="15", pct=0.78), "ui", "hud", "hud_xp_level_row_lv15.png")
    reg(p2, "ui/hud", ["hud", "xp", "level"], "same row at LV 15 / 78 %")
    p3 = save(hud_wagon_slots(), "ui", "hud", "hud_wagon_slots.png")
    reg(p3, "ui/hud", ["hud", "wagon", "slots"], "wagon with 2 gun slots, slot 1 filled, slot 2 locked")
    contact([p, p2, p3], os.path.join(OUT, "sheets", "hud-v2.png"), cols=2, cell=400, title="HUD v2")

    # data
    dd = os.path.join(OUT, "data")
    os.makedirs(dd, exist_ok=True)
    enemies_json = {"version": 2, "note": "10 new normal enemies; walker/sprinter/tank stay as-is",
                    "enemies": []}
    for eid, name, role, pal, fn in ENEMIES:
        st = ENEMY_STATS[eid]
        enemies_json["enemies"].append({
            "id": eid, "name": name, "role": role, **st,
            "biomes": ["burning-metropolis", "frozen-canyon", "toxic-swamp"],
            "sprite": "enemies/en_%s.png" % eid, "card": "enemies/cards/card_%s.png" % eid,
            "palette": [hxs(pal[k]) for k in ("base", "dark", "light", "accent", "glow")],
            "prompt": prompt_for(name, role, "three biomes", "enemy")})
    json.dump(enemies_json, open(os.path.join(dd, "enemies.json"), "w"), indent=1, ensure_ascii=False)

    bosses_json = {"version": 1, "bosses": []}
    for bid, name, biome, role, pal, fn in BOSSES:
        st = BOSS_STATS[bid]
        bosses_json["bosses"].append({
            "id": bid, "name": name, "biome": biome, "role": role, "hp": st["hp"],
            "arena": st["arena"], "entry": st["entry"], "weakness": st["weakness"],
            "attackNote": st["attacks_note"], "reward": st["reward"],
            "phases": [{"name": n, "at": at, "attacks": a} for n, at, a in st["phases"]],
            "sprite": "bosses/boss_%s.png" % bid, "card": "bosses/cards/card_%s.png" % bid,
            "palette": [hxs(pal[k]) for k in ("base", "dark", "light", "accent", "glow")],
            "prompt": prompt_for(name, role, biome, "boss")})
    json.dump(bosses_json, open(os.path.join(dd, "bosses.json"), "w"), indent=1, ensure_ascii=False)

    proj = {"version": 1, "gunMap": GUN_MAP, "projectiles": []}
    n_dmg = {"kinetic": [9, 6, 30], "blast": [26, 0, 0], "fire": [12, 0, 0], "acid": [10, 0, 0],
             "electric": [14, 0, 0]}
    for pid, name, dtype, role, pal, fn, size in PROJECTILES:
        proj["projectiles"].append({
            "id": pid, "name": name, "damageType": dtype, "role": role,
            "sprite": "projectiles/proj_%s.png" % pid,
            "impact": FX_MAP["impact"].get(dtype, "impact_kinetic"),
            "uses": [g for g, p_ in GUN_MAP.items() if p_ == pid] or ["enemy ranged"],
            "palette": [hxs(pal[k]) for k in ("base", "light", "glow")]})
    json.dump(proj, open(os.path.join(dd, "projectiles.json"), "w"), indent=1, ensure_ascii=False)

    fx = {"version": 1, "impactByDamageType": FX_MAP["impact"], "deathByEnemy": FX_MAP["death"],
          "fx": [{"id": fid, "name": name, "sprite": "fx/fx_%s.png" % fid,
                  "frames": 1, "durationMs": 380 if not fid.startswith("death") else 520}
                 for fid, name, pal, fn in FX]}
    json.dump(fx, open(os.path.join(dd, "fx.json"), "w"), indent=1, ensure_ascii=False)

    wagons = {
        "version": 2,
        "rule": {"gunSlotsMax": 2, "gunSlotsAtStart": 1,
                 "secondSlotUnlock": "spend an 'Extra Gun' upgrade card on that wagon",
                 "notes": "each wagon arrives with 1 gun; a wagon may never hold more than 2"},
        "convoy": [{"wagon": 1, "unlockLevel": 1, "slots": [True, False]},
                   {"wagon": 2, "unlockLevel": 5, "slots": [True, False]},
                   {"wagon": 3, "unlockLevel": 10, "slots": [True, False]},
                   {"wagon": 4, "unlockLevel": 15, "slots": [True, False]},
                   {"wagon": 5, "unlockLevel": 20, "slots": [True, False]}],
        "ui": {"hud": "ui/hud/hud_wagon_slots.png",
               "composite": "ui/hud/hud_wagon_slots.png"},
        "integration": ["GunSlot[] per wagon, length 2, index 1 locked until unlocked",
                        "ram/turret targeting iterates wagons then slots (wagon 1 = engine)",
                        "glider disables the slot it lands on for 3 s"]}
    json.dump(wagons, open(os.path.join(dd, "wagons.json"), "w"), indent=1, ensure_ascii=False)

    hudv2 = {"version": 2,
             "change": "level number moved out of the separate TRAIN LV badge into the XP bar's left cap",
             "removed": ["TRAIN LV badge on the level-3 GROW states"],
             "row": {"assets": ["ui/hud/hud_xp_level_row.png", "ui/hud/hud_xp_level_row_lv15.png"],
                     "chipW": 34, "chipH": 20, "trackW": 106, "trackH": 16, "gap": 4,
                     "behaviour": ["chip shows LV + zero-padded number, always visible",
                                   "fill under the chip shows progress to the next level",
                                   "on level-up the chip flashes and the number rolls"]},
             "wagon": {"assets": "ui/hud/hud_wagon_slots.png", "slotsMax": 2}}
    json.dump(hudv2, open(os.path.join(dd, "hud-v2.json"), "w"), indent=1, ensure_ascii=False)

    reg(os.path.join(dd, "enemies.json"), "data", ["data", "enemies"], "machine-readable enemy roster")
    reg(os.path.join(dd, "bosses.json"), "data", ["data", "bosses"], "boss roster with phases and attacks")
    reg(os.path.join(dd, "projectiles.json"), "data", ["data", "projectiles"], "projectile roster + gun mapping")
    reg(os.path.join(dd, "fx.json"), "data", ["data", "fx"], "impact/death FX mapping")
    reg(os.path.join(dd, "wagons.json"), "data", ["data", "rules"], "wagon gun-slot rule (max 2, start 1)")
    reg(os.path.join(dd, "hud-v2.json"), "data", ["data", "hud"], "HUD v2 change spec")

    cat["assets"] = keep + new
    cat["assetCount"] = len(cat["assets"])
    json.dump(cat, open(cat_path, "w"), indent=1)
    write_index(cat)
    print("assets", len(new), "total", cat["assetCount"])


QUICK = [("HUD bars, heart, flag", "ui/hud/"), ("HUD spacing over gameplay", "ui/reference_hud_layout.png"),
         ("XP bar with the level in it (v2)", "ui/hud/hud_xp_level_row.png"),
         ("wagon gun slots (max 2)", "ui/hud/hud_wagon_slots.png"),
         ("upgrade cards + level-up modal", "ui/cards/"), ("touch chip per input state", "ui/control/"),
         ("NEW normal enemies (10)", "enemies/"), ("NEW enemy design cards", "enemies/cards/"),
         ("NEW bosses (10)", "bosses/"), ("NEW boss design cards", "bosses/cards/"),
         ("projectile ammo (7)", "projectiles/"), ("kill + impact FX (10)", "fx/"),
         ("game data: rosters, rules", "data/"),
         ("legacy sprites (walker/sprinter/tank...)", "items/"),
         ("full gameplay frames", "screens/"), ("biome palettes", "biomes/"),
         ("machine search", "meta/catalog.json")]


def write_index(cat):
    import collections
    by = collections.defaultdict(list)
    for e in cat["assets"]:
        by[e["category"]].append(e)
    L = ["# Asset index — finalized for development", "",
         "%d files. Browse visually first: `sheets/`." % cat["assetCount"], "",
         "| Sheet | Shows |", "|---|---|",
         "| `sheets/enemies.png` | the 10 new enemies |",
         "| `sheets/bosses.png` | the 10 bosses |",
         "| `sheets/projectiles.png` | the 7 ammo types |",
         "| `sheets/fx.png` | impact + death bursts |",
         "| `sheets/hud-v2.png` | new level/XP row, wagon slots |",
         "| `sheets/ui.png` | HUD, cards, chips |",
         "| `sheets/items.png` | legacy sprites |",
         "| `sheets/screens-*.png` | all frames of one biome |", "",
         "| I need | Path |", "|---|---|"]
    for label, path in QUICK:
        L.append("| %s | `%s` |" % (label, path))
    L.append("")
    for lvl in ("level-1-burning-metropolis", "level-2-frozen-canyon", "level-3-toxic-swamp"):
        d = os.path.join(OUT, "screens", lvl)
        if os.path.isdir(d):
            files = sorted(os.listdir(d))
            L.append("- `screens/%s/` — %d frames (`%s` … `%s`)" % (lvl, len(files), files[0], files[-1]))
    L.append("")
    order = ["ui/hud", "ui/cards", "ui/control", "ui", "enemies", "enemies/cards", "bosses",
             "bosses/cards", "projectiles", "fx", "data"]
    cats = [c for c in order if c in by] + sorted(c for c in by if c not in order)
    for cat_name in cats:
        L += ["## %s" % cat_name, "", "| file | px | tags | role |", "|---|---|---|---|"]
        for a in sorted(by[cat_name], key=lambda x: x["id"]):
            px = "%d×%d" % (a["w"], a["h"]) if a["w"] else "—"
            L.append("| `%s` | %s | %s | %s |" % (a["path"], px, ", ".join(a["tags"]), a["role"]))
        L.append("")
    open(os.path.join(OUT, "INDEX.md"), "w").write("\n".join(L) + "\n")


if __name__ == "__main__":
    build()
