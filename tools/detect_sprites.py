#!/usr/bin/env python3
"""Detect foreground components in the art sheets so they can be sliced into sprites.

Pure PIL + run-length connected components (no numpy in this environment).
Prints one line per component: x,y,w,h,area,label-ish fill ratio.
"""
import sys
from PIL import Image, ImageChops

BG_TOL = 26          # colour distance from the sheet background that counts as "ink"
MIN_AREA = 1200      # ignore specks / letters


class UF:
    def __init__(self):
        self.p = []

    def add(self):
        self.p.append(len(self.p))
        return len(self.p) - 1

    def find(self, a):
        while self.p[a] != a:
            self.p[a] = self.p[self.p[a]]
            a = self.p[a]
        return a

    def union(self, a, b):
        ra, rb = self.find(a), self.find(b)
        if ra != rb:
            self.p[rb] = ra


def runs_of_row(row_bytes, width, thresh):
    """row_bytes: 0/255 per pixel. Returns list of (x0, x1) inclusive."""
    out = []
    x = 0
    while x < width:
        if row_bytes[x] > thresh:
            x0 = x
            while x < width and row_bytes[x] > thresh:
                x += 1
            out.append((x0, x - 1))
        else:
            x += 1
    return out


def components(img, thresh=127):
    w, h = img.size
    px = img.load()
    uf = UF()
    prev_runs = []      # (x0, x1, label)
    boxes = {}          # label -> [x0,y0,x1,y1,area]

    for y in range(h):
        row = [px[x, y] for x in range(w)]
        runs = runs_of_row(row, w, thresh)
        cur = []
        pi = 0
        for (x0, x1) in runs:
            label = None
            while pi < len(prev_runs) and prev_runs[pi][1] < x0 - 1:
                pi += 1
            k = pi
            while k < len(prev_runs) and prev_runs[k][0] <= x1 + 1:
                if label is None:
                    label = uf.find(prev_runs[k][2])
                else:
                    uf.union(label, prev_runs[k][2])
                k += 1
            if label is None:
                label = uf.add()
                boxes[label] = [x0, y, x1, y, 0]
            root = uf.find(label)
            b = boxes.get(root)
            if b is None:
                b = [x0, y, x1, y, 0]
                boxes[root] = b
            b[0] = min(b[0], x0); b[1] = min(b[1], y)
            b[2] = max(b[2], x1); b[3] = max(b[3], y)
            b[4] += x1 - x0 + 1
            cur.append((x0, x1, label))
        prev_runs = cur

    merged = {}
    for label, b in boxes.items():
        root = uf.find(label)
        m = merged.setdefault(root, [b[0], b[1], b[2], b[3], 0])
        m[0] = min(m[0], b[0]); m[1] = min(m[1], b[1])
        m[2] = max(m[2], b[2]); m[3] = max(m[3], b[3]); m[4] += b[4]
    return list(merged.values())


def main(path):
    img = Image.open(path).convert("RGB")
    w, h = img.size
    # background = median-ish corner sample
    corners = [img.getpixel(p) for p in [(2, 2), (w - 3, 2), (2, h - 3), (w - 3, h - 3)]]
    bg = tuple(sorted(c[i] for c in corners)[1] for i in range(3))
    diff = ImageChops.difference(img, Image.new("RGB", (w, h), bg)).convert("L")
    mask = diff.point(lambda v: 255 if v > BG_TOL else 0)

    comps = [c for c in components(mask) if c[4] >= MIN_AREA]
    comps.sort(key=lambda c: (c[1] // 40, c[0]))
    print(f"# {path}  {w}x{h}  bg={bg}  components={len(comps)}")
    for x0, y0, x1, y1, area in comps:
        bw, bh = x1 - x0 + 1, y1 - y0 + 1
        print(f"{x0:5d} {y0:5d} {bw:1d} {bh:1d}  area={area:7d} fill={area/(bw*bh):.2f}")


if __name__ == "__main__":
    for p in sys.argv[1:]:
        main(p)
