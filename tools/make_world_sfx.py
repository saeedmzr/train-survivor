#!/usr/bin/env python3
"""Generate the ruined-rail-district sound set + combat cues (stdlib only, deterministic).

Every clip is synthesised here: filtered noise, inharmonic metal modes and shaped
transients. Nothing is sampled, so the set stays original and re-generatable:

    amb_wind     looping wind bed through structures, faint metal drone
    fx_creak     distant metal plate groan (sparse one-shot)
    fx_engine    weighty locomotive loop: chug, exhaust chuff, mechanical rattle
    fx_rail      wheel over a rail joint: double clack with body
    fx_brake     brake squeal + air release
    fx_shot      dry machine-gun report: sharp crack, low punch, short tail
    fx_hit       wet flesh impact (differentiable from metal)
    fx_hit_metal bullet on steel: bright inharmonic ping + spark burst
    fx_step      boot on ballast: gravel grains over a low thud
    fx_swing     heavy arm swipe: low whoosh into a body whump
    fx_death_thud a body hitting the ground

Usage: python3 tools/make_world_sfx.py
"""
import math
import os
import random
import struct
import wave

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "unity/Assets/_Project/Art/Resources/Audio")
SR = 44100


# ------------------------------------------------------------------ dsp bits

class OnePole:
    def __init__(self, a):
        self.a = a
        self.y = 0.0

    def __call__(self, x):
        self.y += self.a * (x - self.y)
        return self.y


class Biquad:
    """RBJ band-pass (constant skirt gain) — the workhorse for resonances."""

    def __init__(self, f0, q, gain=1.0, sr=SR):
        f0 = max(20.0, min(sr * 0.45, f0))
        w = 2.0 * math.pi * f0 / sr
        alpha = math.sin(w) / (2.0 * q)
        b0 = alpha
        b1 = 0.0
        b2 = -alpha
        a0 = 1.0 + alpha
        a1 = -2.0 * math.cos(w)
        a2 = 1.0 - alpha
        self.b0, self.b1, self.b2 = b0 / a0 * gain, b1 / a0 * gain, b2 / a0 * gain
        self.a1, self.a2 = a1 / a0, a2 / a0
        self.x1 = self.x2 = self.y1 = self.y2 = 0.0

    def __call__(self, x):
        y = (self.b0 * x + self.b1 * self.x1 + self.b2 * self.x2
             - self.a1 * self.y1 - self.a2 * self.y2)
        self.x2, self.x1 = self.x1, x
        self.y2, self.y1 = self.y1, y
        return y


def highpass(x, prev):
    return x - prev


def soft(x, drive=1.6):
    return math.tanh(x * drive)


def fade_edges(out, ms=4):
    n = len(out)
    k = max(1, int(SR * ms / 1000.0))
    for i in range(min(k, n)):
        g = i / k
        out[i] *= g
        out[n - 1 - i] *= g
    return out


def loopify(out, fade_s=0.35):
    """Circular crossfade so a generated bed loops without a seam."""
    k = int(SR * fade_s)
    n = len(out) - k
    tail = out[n:]
    head = out[:k]
    mixed = [head[i] * (1.0 - i / k) + tail[i] * (i / k) for i in range(k)]
    return mixed + out[k:n]


def write(name, samples, gain=0.85, loop=False, fade_s=0.35):
    os.makedirs(OUT, exist_ok=True)
    if loop:
        samples = loopify(samples, fade_s)
    else:
        samples = fade_edges(list(samples))
    peak = max(1e-6, max(abs(s) for s in samples))
    scale = gain / peak
    frames = b"".join(struct.pack("<h", int(max(-1.0, min(1.0, s * scale)) * 32767))
                      for s in samples)
    path = os.path.join(OUT, name + ".wav")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(frames)
    print("%-14s %5.2fs peak=%.2f" % (name, len(samples) / SR, gain))


# ------------------------------------------------------------------ beds

def amb_wind(dur=11.0, seed=301):
    """Wind moving through a wrecked yard: gusting broadband air, a thin whistle
    through gaps, and a very low metallic drone that never resolves into a note."""
    rnd = random.Random(seed)
    n = int(SR * dur)
    out = [0.0] * n
    lp_low = OnePole(0.020)
    lp_mid = OnePole(0.085)
    bp_air = Biquad(430.0, 0.8)
    bp_high = Biquad(1050.0, 1.1)
    whistle = Biquad(1320.0, 14.0)
    plate = Biquad(226.0, 26.0)   # driven by gusts: a hanging sheet of steel
    for i in range(n):
        t = i / SR
        g = 0.55 + 0.26 * math.sin(2 * math.pi * 0.067 * t) \
            + 0.19 * math.sin(2 * math.pi * 0.113 * t + 1.7)
        g = max(0.08, min(1.0, g))
        nz = rnd.uniform(-1.0, 1.0)
        low = lp_low(nz) * 0.85 * g
        mid = lp_mid(nz) * 0.55 * (0.35 + 0.65 * g)
        air = bp_air(nz) * 0.32 * g
        high = bp_high(nz) * 0.10 * (g ** 2)
        # slow drifting whistle, only at the top of a gust
        wf = 1180.0 + 240.0 * math.sin(2 * math.pi * 0.041 * t + 0.4)
        if i % 256 == 0:
            whistle = Biquad(wf, 16.0)
        wh = whistle(nz) * 0.035 * max(0.0, g - 0.62) * 2.5
        # parked rolling stock: low structural drone + a plate that rings with the wind
        drone = (math.sin(2 * math.pi * 49.0 * t) * 0.010
                 + math.sin(2 * math.pi * 73.5 * t + 0.3) * 0.007
                 + math.sin(2 * math.pi * 110.0 * t + 1.9) * 0.003)
        sheet = plate(nz) * 0.05 * (g ** 3)
        out[i] = low + mid + air + high + wh + drone + sheet
    return out


def fx_engine(dur=4.0, seed=311):
    """A loaded diesel-electric: eight exhaust beats, a 46 Hz body and a rattle that
    sits above the bed. Four-second exact loop so the source can run forever."""
    rnd = random.Random(seed)
    n = int(SR * dur)
    rate = 2.0
    out = [0.0] * n
    chuff_bp = Biquad(240.0, 1.1)
    rattle_bp = Biquad(2600.0, 3.0)
    rumble_lp = OnePole(0.012)
    for i in range(n):
        t = i / SR
        ph = (t * rate) % 1.0
        beat = math.exp(-ph * 8.0)
        chuff = math.exp(-ph * 14.0)
        nz = rnd.uniform(-1.0, 1.0)
        body = (math.sin(2 * math.pi * 46.0 * t) * 0.30
                + math.sin(2 * math.pi * 92.0 * t + 0.7) * 0.13
                + math.sin(2 * math.pi * 138.0 * t + 2.1) * 0.06)
        thump = math.sin(2 * math.pi * (58.0 + 12.0 * beat) * t) * 0.42 * beat
        exhaust = chuff_bp(nz) * 0.55 * chuff
        rattle = rattle_bp(nz) * 0.055 * (0.4 + 0.6 * beat)
        air = rumble_lp(nz) * 0.9
        out[i] = body + thump + exhaust + rattle + air * 0.35
    return out


# ------------------------------------------------------------------ one-shots

def fx_rail(dur=0.30, seed=321):
    """Wheel over a joint: two hard clacks 55 ms apart, then a metal ring."""
    rnd = random.Random(seed)
    n = int(SR * dur)
    out = [0.0] * n
    for k, (at, amp, f) in enumerate(((0.0, 1.0, 1750.0), (0.055, 0.7, 1420.0))):
        start = int(SR * at)
        bp1 = Biquad(f, 5.0)
        bp2 = Biquad(f * 0.55, 3.0)
        for i in range(start, n):
            t = (i - start) / SR
            nz = rnd.uniform(-1.0, 1.0)
            out[i] += (bp1(nz) * math.exp(-t * 42.0) * 0.85
                       + bp2(nz) * math.exp(-t * 28.0) * 0.45
                       + math.sin(2 * math.pi * 88.0 * t) * math.exp(-t * 30.0) * 0.55) * amp
    ring = Biquad(2380.0, 18.0)
    for i in range(n):
        t = i / SR
        out[i] += ring(rnd.uniform(-1.0, 1.0)) * math.exp(-t * 11.0) * 0.10
    return out


def fx_brake(dur=1.1, seed=331):
    """Brake shoe on a steel tyre: rising squeal plus the air release under it."""
    rnd = random.Random(seed)
    n = int(SR * dur)
    out = [0.0] * n
    for i in range(n):
        t = i / SR
        x = t / dur
        f = 1850.0 + 900.0 * x + 60.0 * math.sin(2 * math.pi * 7.5 * t)
        ph = 2 * math.pi * f * t
        squeal = (math.sin(ph) + 0.4 * math.sin(ph * 2.02) + 0.2 * math.sin(ph * 3.07))
        env = min(1.0, x * 6.0) * math.exp(-max(0.0, x - 0.55) * 5.0)
        hiss = rnd.uniform(-1.0, 1.0) * math.exp(-x * 6.0) * 0.35
        out[i] = squeal * 0.32 * env + hiss
    b = Biquad(900.0, 0.9)
    out = [b(v) for v in out]
    return out


def fx_creak(dur=2.4, seed=341):
    """A big steel plate flexing: stick-slip amplitude over three inharmonic modes."""
    rnd = random.Random(seed)
    n = int(SR * dur)
    out = [0.0] * n
    modes = [Biquad(188.0, 22.0), Biquad(317.0, 18.0), Biquad(503.0, 15.0)]
    gains = [1.0, 0.55, 0.32]
    for i in range(n):
        t = i / SR
        # irregular pulse train = the "grinding" of a plate under load
        pulse = 0.55 + 0.45 * math.sin(2 * math.pi * 9.3 * t) * math.sin(2 * math.pi * 2.1 * t + 0.8)
        nz = rnd.uniform(-1.0, 1.0)
        s = 0.0
        for m, g in zip(modes, gains):
            s += m(nz) * g
        out[i] = s * pulse * math.exp(-t * 0.6) * 0.6
    return out


def fx_shot(dur=0.24, seed=351):
    """Machine-gun report: near-instant crack, a low punch behind it and a very
    short open-air tail. Dry on purpose — it has to cut through the wind bed."""
    rnd = random.Random(seed)
    n = int(SR * dur)
    out = [0.0] * n
    body_lp = OnePole(0.30)
    mid_bp = Biquad(950.0, 1.2)
    tail_lp = OnePole(0.45)
    prev = 0.0
    for i in range(n):
        t = i / SR
        nz = rnd.uniform(-1.0, 1.0)
        hp = nz - prev
        prev = nz
        crack = hp * math.exp(-t * 220.0) * 1.0 + nz * math.exp(-t * 120.0) * 0.55
        sweep = 185.0 * math.exp(-t * 16.0) + 78.0
        punch = math.sin(2 * math.pi * sweep * t) * math.exp(-t * 26.0) * 0.85
        mid = mid_bp(nz) * math.exp(-t * 30.0) * 0.55
        tail = tail_lp(nz) * math.exp(-t * 26.0) * 0.22
        ting = math.sin(2 * math.pi * 3260.0 * t) * math.exp(-t * 75.0) * 0.12
        out[i] = crack + punch + mid + tail + ting
    # one 26 ms slapback from the nearest wall, much softer
    d = int(SR * 0.026)
    for i in range(d, n):
        out[i] += out[i - d] * 0.20
    return [soft(v, 1.35) for v in out]


def fx_hit(dur=0.16, seed=361):
    """Wet body hit: a short squelch over a falling thump. Reads as flesh, never metal."""
    rnd = random.Random(seed)
    n = int(SR * dur)
    out = [0.0] * n
    wet = Biquad(560.0, 2.6)
    for i in range(n):
        t = i / SR
        nz = rnd.uniform(-1.0, 1.0)
        pop = math.sin(2 * math.pi * (190.0 * math.exp(-t * 12.0) + 60.0) * t) * math.exp(-t * 30.0) * 0.7
        out[i] = wet(nz) * math.exp(-t * 26.0) * 0.8 + pop
    return out


def fx_hit_metal(dur=0.22, seed=371):
    """Bullet on steel: three inharmonic modes plus a spark burst; no wet content."""
    rnd = random.Random(seed)
    n = int(SR * dur)
    out = [0.0] * n
    hp = OnePole(0.6)
    for i in range(n):
        t = i / SR
        nz = rnd.uniform(-1.0, 1.0)
        spark = (nz - hp(nz)) * math.exp(-t * 90.0) * 0.55
        ping = (math.sin(2 * math.pi * 2360.0 * t) * 0.30
                + math.sin(2 * math.pi * 3410.0 * t + 0.4) * 0.20
                + math.sin(2 * math.pi * 4870.0 * t + 1.1) * 0.10) * math.exp(-t * 22.0)
        thud = math.sin(2 * math.pi * 118.0 * t) * math.exp(-t * 40.0) * 0.35
        out[i] = spark + ping + thud
    return out


def fx_step(dur=0.16, seed=381):
    """Boot on ballast: a low thud plus four grit grains. Grounded, not a click."""
    rnd = random.Random(seed)
    n = int(SR * dur)
    out = [0.0] * n
    for i in range(n):
        t = i / SR
        thump = math.sin(2 * math.pi * (74.0 + 18.0 * math.exp(-t * 40.0)) * t) \
            * math.exp(-t * 30.0) * 0.9
        out[i] = thump
    grit = Biquad(2100.0, 2.5)
    for k in range(6):
        at = int(SR * rnd.uniform(0.0, 0.055))
        amp = rnd.uniform(0.15, 0.5)
        ln = int(SR * rnd.uniform(0.004, 0.012))
        for i in range(ln):
            out[at + i] += grit(rnd.uniform(-1.0, 1.0)) * amp * math.exp(-i / ln * 4.0)
    return out


def fx_swing(dur=0.30, seed=391):
    """Heavy arm swipe: band-passed air sweeping down into a body whump."""
    rnd = random.Random(seed)
    n = int(SR * dur)
    out = [0.0] * n
    bp = Biquad(700.0, 1.6)
    lp = OnePole(0.10)
    for i in range(n):
        t = i / SR
        x = t / dur
        if i % 64 == 0:
            bp = Biquad(320.0 + 900.0 * math.sin(math.pi * min(1.0, x * 1.2)), 1.7)
        nz = rnd.uniform(-1.0, 1.0)
        hump = math.sin(math.pi * min(1.0, x * 1.05)) ** 1.4
        whump = math.sin(2 * math.pi * 82.0 * t) * math.exp(-max(0.0, t - 0.16) * 18.0) \
            * (1.0 if t > 0.14 else 0.0) * 0.5
        out[i] = bp(nz) * hump * 0.9 + lp(nz) * hump * 0.25 + whump
    return out


def fx_death_thud(dur=0.45, seed=401):
    rnd = random.Random(seed)
    n = int(SR * dur)
    out = [0.0] * n
    lp = OnePole(0.09)
    for i in range(n):
        t = i / SR
        nz = rnd.uniform(-1.0, 1.0)
        boom = math.sin(2 * math.pi * (60.0 * math.exp(-t * 6.0) + 34.0) * t) * math.exp(-t * 10.0)
        out[i] = boom * 0.8 + lp(nz) * math.exp(-t * 12.0) * 1.4
    grit = Biquad(1500.0, 3.0)
    for k in range(9):
        at = int(SR * rnd.uniform(0.02, 0.34))
        amp = rnd.uniform(0.05, 0.22)
        for i in range(int(SR * 0.01)):
            if at + i < n:
                out[at + i] += grit(rnd.uniform(-1.0, 1.0)) * amp * (1.0 - i / (SR * 0.01))
    return out


def main():
    write("amb_wind", amb_wind(), 0.55, loop=True)
    # 4.5 s at 2 beats/s, crossfaded over exactly one beat (0.5 s): the loop point
    # lands on the same phase it left, so the engine rhythm never hiccups.
    write("fx_engine", fx_engine(dur=4.5), 0.80, loop=True, fade_s=0.5)
    write("fx_rail", fx_rail(), 0.70)
    write("fx_creak", fx_creak(), 0.45)
    write("fx_brake", fx_brake(), 0.70)
    write("fx_shot", fx_shot(), 0.95)
    write("fx_hit", fx_hit(), 0.90)
    write("fx_hit_metal", fx_hit_metal(), 0.90)
    write("fx_step", fx_step(), 0.80)
    write("fx_swing", fx_swing(), 0.80)
    write("fx_death_thud", fx_death_thud(), 0.85)


if __name__ == "__main__":
    main()
