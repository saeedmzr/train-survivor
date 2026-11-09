#!/usr/bin/env python3
"""Synthesise the enemy voice set + combat SFX as 16-bit mono WAVs.

No samples to source, no model to run: everything here is filtered noise and detuned
harmonics, which is exactly what a zombie growl is. Clips land in
unity/Assets/_Project/Art/Resources/Audio/ so AudioDirector can Resources.Load them.

Usage: python3 tools/make_enemy_sfx.py
"""
import math, os, random, struct, wave

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "unity/Assets/_Project/Art/Resources/Audio")
SR = 44100


def env(n, attack=0.02, decay=0.3, sustain=0.7, release=0.4):
    """Simple ADSR shaped to n samples."""
    a = int(n * attack)
    d = int(n * decay)
    s = int(n * (1 - attack - decay - release))
    r = n - a - d - s
    out = []
    for i in range(a):
        out.append(i / max(1, a))
    for i in range(d):
        out.append(1 - (1 - sustain) * (i / max(1, d)))
    out.extend([sustain] * max(0, s))
    for i in range(max(0, r)):
        out.append(sustain * (1 - i / max(1, r)))
    return out[:n] + [0.0] * max(0, n - len(out))


class Chain:
    """Tiny one-pole lowpass + resonator, so noise turns into a throat instead of hiss."""

    def __init__(self, lp=0.2, res_freq=300.0, res_q=8.0):
        self.lp_a = lp
        self.lp = 0.0
        self.f = 2 * math.sin(math.pi * res_freq / SR)
        self.q = 1.0 / res_q
        self.y1 = self.y2 = 0.0

    def step(self, x):
        self.lp += self.lp_a * (x - self.lp)
        y = self.f * self.lp + self.y1 - self.q * self.y2
        self.y2 = self.y1
        self.y1 = y
        return y


def voice(dur, f0, growl=6.0, noise=0.25, formant=320.0, q=9.0, lp=0.25,
          vibrato=18.0, vibrato_hz=5.0, jitter=0.06, slide=1.0, seed=1):
    """A growl/scream: detuned saw + noise through a formant resonator."""
    rnd = random.Random(seed)
    n = int(SR * dur)
    ch = Chain(lp=lp, res_freq=formant, res_q=q)
    ph = 0.0
    out = []
    for i in range(n):
        t = i / SR
        x = t / dur
        f = f0 * (1.0 + jitter * rnd.uniform(-1, 1)) * (slide ** x)
        f *= 1.0 + 0.012 * math.sin(2 * math.pi * vibrato_hz * t) * vibrato / 18.0
        ph += 2 * math.pi * f / SR
        saw = (2 * (ph / (2 * math.pi) % 1.0)) - 1.0
        sub = math.sin(ph * 0.5)
        nz = rnd.uniform(-1, 1)
        y = ch.step(saw * 0.7 + sub * 0.4 + nz * noise)
        out.append(y)
    return out


def clank(dur=0.5, freq=1800, seed=2):
    rnd = random.Random(seed)
    n = int(SR * dur)
    out = []
    for i in range(n):
        t = i / SR
        a = math.exp(-t * 9) * math.sin(2 * math.pi * freq * t)
        b = math.exp(-t * 3) * math.sin(2 * math.pi * freq * 1.7 * t) * 0.5
        c = rnd.uniform(-1, 1) * math.exp(-t * 40) * 0.6
        out.append(a + b + c)
    return out


def boom(dur=0.9, seed=3):
    rnd = random.Random(seed)
    n = int(SR * dur)
    ch = Chain(lp=0.08, res_freq=90, res_q=3)
    out = []
    for i in range(n):
        t = i / SR
        f = 120 * math.exp(-t * 2.2) + 32
        s = math.sin(2 * math.pi * f * t) * math.exp(-t * 2.6)
        y = ch.step(s * 0.8 + rnd.uniform(-1, 1) * math.exp(-t * 6) * 0.5)
        out.append(y)
    return out


def squelch(dur=0.34, seed=4):
    rnd = random.Random(seed)
    n = int(SR * dur)
    ch = Chain(lp=0.5, res_freq=520, res_q=5)
    out = []
    for i in range(n):
        t = i / SR
        pop = math.sin(2 * math.pi * (240 * math.exp(-t * 9)) * t) * math.exp(-t * 16)
        wet = ch.step(rnd.uniform(-1, 1)) * math.exp(-t * 7)
        out.append(pop + wet * 0.8)
    return out


def screech(dur=0.45, seed=5):
    rnd = random.Random(seed)
    n = int(SR * dur)
    out = []
    for i in range(n):
        t = i / SR
        sweep = 900 + 2600 * (t / dur)
        s = math.sin(2 * math.pi * sweep * t) * 0.5
        s += rnd.uniform(-1, 1) * 0.25
        out.append(s * (1 - t / dur))
    return out



def crack(dur=0.11, seed=51):
    """Muzzle crack: high-passed noise with a very fast decay, no tonal content."""
    rnd = random.Random(seed)
    n = int(SR * dur)
    out = []
    prev = 0.0
    for i in range(n):
        x = rnd.uniform(-1.0, 1.0)
        hp = x - prev
        prev = x
        out.append(hp * math.exp(-i / SR * 70.0))
    return out


def mix(*layers):
    n = max(len(l) for l in layers)
    out = [0.0] * n
    for l in layers:
        for i, v in enumerate(l):
            out[i] += v
    return out


def write(name, samples, gain=0.85):
    os.makedirs(OUT, exist_ok=True)
    peak = max(1e-6, max(abs(s) for s in samples))
    scale = gain / peak
    frames = b"".join(struct.pack("<h", int(max(-1, min(1, s * scale)) * 32767)) for s in samples)
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(frames)
    print("%-14s %.2fs" % (name, len(samples) / SR))


def build():
    voices = {
        # id            freq   growl noise formant
        "en_walker":   (voice(0.95, 92, noise=0.30, formant=340, q=7, seed=11), 0.9),
        "en_sprinter": (voice(0.55, 148, noise=0.34, formant=620, q=9, vibrato=30, seed=12), 0.9),
        "en_tank":     (voice(1.05, 61, noise=0.26, formant=210, q=6, lp=0.14, seed=13), 1.0),
        "en_crawler":  (voice(0.5, 118, noise=0.42, formant=780, q=12, vibrato=40, seed=14), 0.8),
        "en_bloater":  (voice(0.75, 78, noise=0.30, formant=260, q=14, lp=0.2, seed=15), 0.95),
        "en_spitter":  (voice(0.6, 132, noise=0.46, formant=880, q=16, vibrato=26, seed=16), 0.85),
        "en_shrieker": (voice(0.9, 320, noise=0.22, formant=1450, q=18, vibrato=55,
                              vibrato_hz=7.5, slide=1.5, seed=17), 0.9),
        "en_bulwark":  (mix(clank(0.5, 1500, 21), voice(0.55, 74, noise=0.2, formant=240, q=8, seed=22)), 0.9),
        "en_glider":   (mix(voice(0.42, 210, noise=0.55, formant=1100, q=6, vibrato=70, seed=18),
                            [s * 0.4 for s in screech(0.42, 31)]), 0.85),
        "en_drillmaw": (mix(voice(0.7, 86, noise=0.3, formant=300, q=9, seed=19),
                            [math.sin(2 * math.pi * (150 + 40 * math.sin(2 * math.pi * 11 * i / SR)) * i / SR) * 0.25
                             for i in range(int(SR * 0.7))]), 0.95),
        "en_lantern":  (mix(voice(1.1, 64, noise=0.16, formant=180, q=10, vibrato=8, seed=20),
                            [math.exp(-i / SR * 3) * math.sin(2 * math.pi * 740 * i / SR) * 0.3 for i in range(int(SR * 1.1))],
                            [math.exp(-max(0, i / SR - 0.45) * 4) * math.sin(2 * math.pi * 980 * (i / SR)) * 0.2
                             for i in range(int(SR * 1.1))]), 0.9),
        "en_rimeback":  (mix(voice(0.8, 70, noise=0.28, formant=230, q=8, seed=23),
                            clank(0.8, 2400, 24), screech(0.5, 25)), 0.95),
    }
    for name, (samples, gain) in voices.items():
        write(name, [s * e for s, e in zip(samples, env(len(samples), 0.01, 0.15, 0.75, 0.35))], gain)

    write("fx_hit", [s * e for s, e in zip(squelch(0.12, 41), env(int(SR * 0.12), 0.005, 0.05, 0.5, 0.2))], 0.9)
    write("fx_death", squelch(0.34, 42), 0.95)
    write("fx_blast", boom(0.9, 43), 1.0)
    write("fx_brake", screech(0.45, 44), 0.75)
    write("fx_ram", mix(boom(0.3, 45), clank(0.3, 900, 46)), 0.9)
    write("fx_shot", mix(crack(0.11, 51), boom(0.25, 52), clank(0.16, 2400, 53)), 0.8)


if __name__ == "__main__":
    build()
