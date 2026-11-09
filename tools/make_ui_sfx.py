"""Generate the small UI/engine/impact sounds the event mix was missing.

Deterministic, stdlib only: every clip is a short envelope-shaped tone or noise burst
written as 22.05 kHz 16-bit mono WAV next to the existing SFX.

    python3 tools/make_ui_sfx.py
"""
import math
import os
import random
import struct
import wave

OUT = os.path.join(os.path.dirname(__file__), "..",
                   "unity/Assets/_Project/Art/Resources/Audio")
SR = 22050


def write(name, samples):
    # soft clip so nothing exceeds full scale
    data = b"".join(struct.pack("<h", int(max(-0.98, min(0.98, s)) * 32767)) for s in samples)
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data)


def env(i, n, attack=0.01, release=0.5):
    a = max(1, int(n * attack))
    r = max(1, int(n * release))
    if i < a:
        return i / a
    if i > n - r:
        return max(0.0, (n - i) / r)
    return 1.0


def tone(name, dur, f0, f1=None, vol=0.6, noise=0.0, seed=1):
    n = int(SR * dur)
    rnd = random.Random(seed)
    out = []
    phase = 0.0
    for i in range(n):
        t = i / n
        f = f0 if f1 is None else f0 + (f1 - f0) * t
        phase += 2 * math.pi * f / SR
        s = math.sin(phase)
        if noise:
            s = (1 - noise) * s + noise * (rnd.random() * 2 - 1)
        out.append(s * env(i, n) * vol)
    write(name, out)


def main():
    # two-step level-up chime
    n = int(SR * 0.42)
    out = []
    for i in range(n):
        f = 660 if i < n // 2 else 990
        out.append(math.sin(2 * math.pi * f * i / SR) * env(i, n, 0.02, 0.6) * 0.5)
    write("ui_levelup.wav", out)

    # stage clear: rising triad
    n = int(SR * 0.9)
    out = []
    for i in range(n):
        t = i / n
        f = 440 + 220 * t
        out.append((math.sin(2 * math.pi * f * i / SR) * 0.5 +
                    math.sin(2 * math.pi * f * 1.5 * i / SR) * 0.25) * env(i, n, 0.01, 0.7) * 0.6)
    write("ui_clear.wav", out)

    # defeat: falling minor
    tone("ui_defeat.wav", 0.9, 330, 110, vol=0.55, noise=0.05, seed=7)
    # upgrade choice click
    tone("ui_click.wav", 0.07, 1200, 900, vol=0.35, seed=3)
    # XP pickup: short bright blip
    tone("pickup_xp.wav", 0.10, 1500, 2100, vol=0.30, seed=4)
    # metal impact: bright noise ping
    tone("fx_metal.wav", 0.12, 2200, 900, vol=0.55, noise=0.75, seed=5)
    # engine bed: 2 s low rumble loop
    n = int(SR * 2.0)
    rnd = random.Random(11)
    out = []
    for i in range(n):
        t = i / SR
        s = math.sin(2 * math.pi * 48 * t) * 0.25 + math.sin(2 * math.pi * 73 * t) * 0.12
        s += (rnd.random() * 2 - 1) * 0.05
        # taper the loop ends to avoid a click
        k = min(1.0, i / 400.0, (n - i) / 400.0)
        out.append(s * k)
    write("fx_engine.wav", out)


if __name__ == "__main__":
    main()
