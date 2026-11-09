#!/usr/bin/env python3
"""Generate the action clips the event mix still lacks.

Deterministic, stdlib only: a restrained walker hurt grunt, a swing whoosh, a footstep
crunch, a barricade collapse (wood/debris, deliberately not a blast) and a UI confirm.
Same synthesis approach as tools/make_enemy_sfx.py; nothing external is sampled.

    python3 tools/make_action_sfx.py
"""
import math
import os
import random
import struct
import wave

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "unity/Assets/_Project/Art/Resources/Audio")
SR = 44100


class Chain:
    """One-pole lowpass into a resonator: turns noise into a throat/body, not hiss."""

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


def env(n, attack=0.02, decay=0.2, sustain=0.7, release=0.4):
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


def write(name, samples, gain=0.85):
    if not os.path.isdir(OUT):
        os.makedirs(OUT)
    peak = max(1e-6, max(abs(s) for s in samples))
    scale = gain / peak
    frames = b"".join(struct.pack("<h", int(max(-1, min(1, s * scale)) * 32767))
                      for s in samples)
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(frames)
    print("%-18s %.2fs" % (name, len(samples) / SR))


def hurt_grunt(dur=0.30, seed=71):
    """Closed-mouth grunt: low, short, no scream tail."""
    rnd = random.Random(seed)
    n = int(SR * dur)
    ch = Chain(lp=0.22, res_freq=430, res_q=7.0)
    out = []
    ph = 0.0
    for i in range(n):
        t = i / SR
        f = 112 * (1.0 + 0.05 * rnd.uniform(-1, 1)) * (0.85 ** (t / dur))
        ph += 2 * math.pi * f / SR
        saw = (2 * (ph / (2 * math.pi) % 1.0)) - 1.0
        nz = rnd.uniform(-1, 1)
        out.append(ch.step(saw * 0.55 + nz * 0.20))
    return [s * e for s, e in zip(out, env(n, 0.02, 0.25, 0.45, 0.45))]


def swing_whoosh(dur=0.20, seed=81):
    """Air swipe: filtered noise with a rising then falling cutoff."""
    rnd = random.Random(seed)
    n = int(SR * dur)
    out = []
    lp = 0.0
    for i in range(n):
        t = i / n
        cut = 0.08 + 0.35 * math.sin(math.pi * t)
        lp += cut * (rnd.uniform(-1, 1) - lp)
        hump = math.sin(math.pi * min(1.0, t * 1.05)) ** 1.6
        out.append(lp * hump)
    return out


def footstep_crunch(dur=0.09, seed=91):
    """Boot on gravel: a short noise scuff over a soft low thump."""
    rnd = random.Random(seed)
    n = int(SR * dur)
    ch = Chain(lp=0.5, res_freq=1900, res_q=3.0)
    out = []
    for i in range(n):
        t = i / SR
        nz = ch.step(rnd.uniform(-1, 1)) * math.exp(-t * 38)
        thump = math.sin(2 * math.pi * 95 * t) * math.exp(-t * 26) * 0.5
        out.append(nz + thump)
    return out


def collapse(dur=0.95, seed=101):
    """Barricade giving way: low rumble, wood cracks, then debris patter.

    No pitched sub drop and no long boom, so it cannot read as the bloater blast.
    """
    rnd = random.Random(seed)
    n = int(SR * dur)
    out = [0.0] * n
    rumble = Chain(lp=0.03, res_freq=70, res_q=2.0)
    for i in range(n):
        t = i / SR
        out[i] += rumble.step(rnd.uniform(-1, 1)) * math.exp(-t * 2.2) * 1.4

    # timber cracks at irregular times in the first half second
    for k in range(7):
        at = int(SR * rnd.uniform(0.0, 0.48))
        freq = rnd.uniform(380, 900)
        amp = rnd.uniform(0.35, 0.85)
        for i in range(at, min(n, at + int(SR * 0.16))):
            t = (i - at) / SR
            out[i] += (math.sin(2 * math.pi * freq * t) * 0.6 +
                       rnd.uniform(-1, 1) * 0.4) * math.exp(-t * 34) * amp

    # debris patter after the main break
    for k in range(26):
        at = int(SR * rnd.uniform(0.05, 0.9))
        freq = rnd.uniform(900, 2600)
        amp = rnd.uniform(0.08, 0.30)
        ln = int(SR * rnd.uniform(0.02, 0.05))
        for i in range(at, min(n, at + ln)):
            t = (i - at) / SR
            out[i] += math.sin(2 * math.pi * freq * t) * math.exp(-t * 90) * amp
    return out


def ui_confirm(dur=0.17):
    n = int(SR * dur)
    e = env(n, 0.01, 0.15, 0.5, 0.4)
    out = []
    split = int(n * 0.42)
    for i in range(n):
        f = 620 if i < split else 930
        out.append(math.sin(2 * math.pi * f * i / SR) * e[i] * 0.4)
    return out


def main():
    write("vo_walker_hurt", hurt_grunt(), 0.7)
    write("fx_swing", swing_whoosh(), 0.65)
    write("fx_step", footstep_crunch(), 0.55)
    write("fx_collapse", collapse(), 0.9)
    write("ui_confirm", ui_confirm(), 0.5)


if __name__ == "__main__":
    main()
