#!/usr/bin/env python3
"""Objective mix check for a captured game WAV (stdlib only).

    python tools/analyze_audio.py <file.wav> [--json out.json]

Reports duration/peak/RMS, per-second RMS, coarse band energy (low/mid/high) and a
transient count so "is the gun audible over the bed" is a number, not a claim.
"""
import array
import json
import math
import sys
import wave


class BQ:
    def __init__(self, f0, q, sr):
        w = 2 * math.pi * f0 / sr
        al = math.sin(w) / (2 * q)
        a0 = 1 + al
        self.b0 = al / a0
        self.b1 = 0.0
        self.b2 = -al / a0
        self.a1 = -2 * math.cos(w) / a0
        self.a2 = (1 - al) / a0
        self.x1 = self.x2 = self.y1 = self.y2 = 0.0

    def __call__(self, x):
        y = (self.b0 * x + self.b1 * self.x1 + self.b2 * self.x2
             - self.a1 * self.y1 - self.a2 * self.y2)
        self.x2, self.x1 = self.x1, x
        self.y2, self.y1 = self.y1, y
        return y


def main(path, json_out=None):
    with wave.open(path) as w:
        n, sr, ch, sw = w.getnframes(), w.getframerate(), w.getnchannels(), w.getsampwidth()
        raw = w.readframes(n)
    a = array.array("h")
    a.frombytes(raw)
    mono = [sum(a[i + c] for c in range(ch)) / ch / 32768.0
            for i in range(0, len(a), ch)]

    peak = max(abs(v) for v in mono)
    rms = math.sqrt(sum(v * v for v in mono) / len(mono))

    low, mid, high = BQ(160, 0.9, sr), BQ(900, 1.0, sr), BQ(3200, 0.9, sr)
    e_low = e_mid = e_high = 0.0
    for v in mono:
        e_low += low(v) ** 2
        e_mid += mid(v) ** 2
        e_high += high(v) ** 2

    # per-second RMS + transient count (fast-attack events, i.e. gunshots/impacts)
    per_sec = []
    transients = 0
    win = int(sr * 0.05)
    prev_env = 0.0
    for s in range(0, len(mono), sr):
        seg = mono[s:s + sr]
        per_sec.append(round(math.sqrt(sum(v * v for v in seg) / max(1, len(seg))), 4))
    for i in range(0, len(mono) - win, win):
        seg = mono[i:i + win]
        env = math.sqrt(sum(v * v for v in seg) / win)
        if env > 0.10 and env > prev_env * 2.2:
            transients += 1
        prev_env = env

    out = {
        "file": path,
        "seconds": round(len(mono) / sr, 2),
        "rate": sr,
        "channels": ch,
        "peak": round(peak, 3),
        "clipped": peak >= 0.999,
        "rms": round(rms, 4),
        "band_ratio_low_mid_high": [round(e_low / max(1e-9, e_mid), 2),
                                    round(e_mid / max(1e-9, e_high), 2)],
        "transients": transients,
        "rms_per_second": per_sec,
    }
    print(json.dumps(out, indent=2))
    if json_out:
        with open(json_out, "w") as f:
            json.dump(out, f, indent=2)


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    js = None
    if "--json" in sys.argv:
        js = sys.argv[sys.argv.index("--json") + 1]
    main(args[0], js)
