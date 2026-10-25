"""Original music and sound effects for the Baytak videos, synthesised from scratch (numpy + scipy): nothing is sampled or downloaded,
so there is nothing to license and nothing for Instagram or YouTube to match against.

music(duration, anchor)  a warm pluck-and-pad bed in C major. The anchor (the moment the logo lands) is put exactly on a bar line,
                         the music builds up to it, the groove starts on it, and the piece ends on a resolved chord that rings out.
sfx(kind)                a short mono sound for an on-screen moment (tap, stamp, notification, ...).
"""
import numpy as np
from scipy import signal

SR = 48000
RNG = np.random.default_rng(11)

NOTE = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(name, octave):
    return 440.0 * 2 ** ((NOTE[name] + 12 * (octave - 4) - 9) / 12)


CHORDS = {  # (bass root, pad voicing, pluck voicing)
    "C": (hz("C", 2), [hz("C", 3), hz("E", 3), hz("G", 3), hz("C", 4)], [hz("C", 4), hz("E", 4), hz("G", 4), hz("C", 5)]),
    "G": (hz("G", 1), [hz("G", 2), hz("D", 3), hz("B", 3), hz("G", 3)], [hz("G", 3), hz("B", 3), hz("D", 4), hz("G", 4)]),
    "Am": (hz("A", 1), [hz("A", 2), hz("E", 3), hz("C", 4), hz("A", 3)], [hz("A", 3), hz("C", 4), hz("E", 4), hz("A", 4)]),
    "F": (hz("F", 1), [hz("F", 2), hz("C", 3), hz("A", 3), hz("F", 3)], [hz("F", 3), hz("A", 3), hz("C", 4), hz("F", 4)]),
}
CYCLE = ["C", "G", "Am", "F"]


# ---------------------------------------------------------------- helpers
def n_of(sec):
    return int(round(sec * SR))


def tt(n):
    return np.arange(n) / SR


def lowpass(x, fc, order=2):
    sos = signal.butter(order, fc, btype="low", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def highpass(x, fc, order=2):
    sos = signal.butter(order, fc, btype="high", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def bandpass(x, lo, hi, order=2):
    sos = signal.butter(order, [lo, hi], btype="band", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def noise(n):
    return RNG.uniform(-1, 1, n)


def expenv(n, tau):
    return np.exp(-tt(n) / tau)


def attack(x, sec):
    a = min(len(x), n_of(sec))
    if a > 1:
        x[:a] *= np.linspace(0, 1, a) ** 1.5
    return x


def tail_fade(x, sec):
    a = min(len(x), n_of(sec))
    if a > 1:
        x[-a:] *= np.linspace(1, 0, a)
    return x


def glide_sine(f0, f1, dur, curve=3.0):
    n = n_of(dur)
    p = tt(n) / dur
    f = f1 + (f0 - f1) * np.exp(-curve * p)
    return np.sin(2 * np.pi * np.cumsum(f) / SR)


def bell(freq, dur, tau):
    n = n_of(dur)
    t = tt(n)
    y = np.sin(2 * np.pi * freq * t) + 0.35 * np.sin(2 * np.pi * freq * 2.76 * t) * np.exp(-t / (tau * 0.4)) + 0.2 * np.sin(2 * np.pi * freq * 5.4 * t) * np.exp(-t / (tau * 0.2))
    return attack(y * np.exp(-t / tau), 0.002)


def pluck(freq, dur, decay=0.995, soft=0.55):
    """Karplus-Strong string: a noise burst through a delay line with a gentle low-pass in the loop."""
    n = n_of(dur)
    N = max(2, int(round(SR / freq - 0.5)))
    x = np.zeros(n)
    burst = lowpass(noise(N), 2200 + freq * 2)
    x[:N] = burst
    a = np.zeros(N + 2)
    a[0] = 1.0
    a[N] = -0.5 * decay
    a[N + 1] = -0.5 * decay
    y = signal.lfilter([1.0], a, x)
    # the delay line is a whole number of samples, so the pitch is a few cents off: resample to land exactly on `freq`
    ratio = freq / (SR / (N + 0.5))
    y = np.interp(np.arange(int(n / ratio)) * ratio, np.arange(n), y)
    y = lowpass(y, 3800 * (0.6 + soft))
    y /= max(1e-9, np.max(np.abs(y)))
    return attack(y, 0.002)


def pad_voice(freq, dur, lp=2200):
    n = n_of(dur)
    t = tt(n)
    y = np.zeros(n)
    for det in (-5, 0, 5):                     # three slightly detuned voices: a soft chorus
        f = freq * 2 ** (det / 1200)
        y += np.sin(2 * np.pi * f * t) + 0.28 * np.sin(2 * np.pi * 2 * f * t) + 0.08 * np.sin(2 * np.pi * 3 * f * t)
    y = lowpass(y / 3, lp)
    attack(y, 0.35)
    return tail_fade(y, 0.55)


def kick():
    n = n_of(0.28)
    y = glide_sine(120, 46, 0.28, 9.0) * expenv(n, 0.09)
    y += 0.25 * lowpass(noise(n), 900) * expenv(n, 0.01)
    return attack(y, 0.001)


def hat():
    n = n_of(0.07)
    return highpass(noise(n), 7000) * expenv(n, 0.018)


def bass_note(freq, dur):
    n = n_of(dur)
    t = tt(n)
    y = np.sin(2 * np.pi * freq * t) + 0.18 * np.sin(2 * np.pi * 2 * freq * t)
    y *= np.exp(-t / (dur * 0.55))
    attack(y, 0.012)
    return tail_fade(y, 0.05)


def reverb_ir(sec=1.5):
    n = n_of(sec)
    out = []
    for _ in range(2):
        ir = noise(n) * np.exp(-tt(n) / (sec / 4.5))
        ir = lowpass(ir, 5500)
        ir[: n_of(0.012)] *= np.linspace(0, 1, n_of(0.012))
        out.append(ir / np.sqrt(np.sum(ir ** 2)))
    return out


IR = reverb_ir()


def reverb(stereo, wet):
    out = stereo.copy()
    for c in range(2):
        w = signal.fftconvolve(stereo[:, c], IR[c])[: len(stereo)]
        out[:, c] = (1 - wet) * stereo[:, c] + wet * w * 3.2
    return out


def place(mix, x, t, gain=1.0, pan=0.0):
    i = n_of(t)
    if i >= len(mix) or i + len(x) <= 0:
        return
    a, b = max(0, -i), min(len(x), len(mix) - i)
    ang = (pan + 1) * np.pi / 4
    mix[i + a:i + b, 0] += x[a:b] * gain * np.cos(ang)
    mix[i + a:i + b, 1] += x[a:b] * gain * np.sin(ang)


# ---------------------------------------------------------------- music (one style per video)
SCALES = {"major": [0, 2, 4, 5, 7, 9, 11], "minor": [0, 2, 3, 5, 7, 8, 10]}
QUAL = {"maj": [0, 4, 7, 12], "min": [0, 3, 7, 12], "maj7": [0, 4, 7, 11], "min7": [0, 3, 7, 10], "add9": [0, 4, 7, 14], "sus2": [0, 2, 7, 12]}
KEYS = {"C": 0, "Db": 1, "D": 2, "Eb": 3, "E": 4, "F": 5, "Gb": 6, "G": 7, "Ab": 8, "A": 9, "Bb": 10, "B": 11}


def midi_hz(m):
    return 440.0 * 2 ** ((m - 69) / 12)


def voicings(pc, qual):
    iv = QUAL[qual]
    return midi_hz(36 + pc), [midi_hz(48 + pc + i) for i in iv], [midi_hz(60 + pc + i) for i in iv]


def voice(kind, freq, dur):
    """One note in the style's instrument."""
    if kind == "pluck":
        return pluck(freq, dur)
    if kind == "pizz":
        return pluck(freq, min(dur, 0.55), 0.984, 0.15)
    if kind == "harp":
        return pluck(freq, dur, 0.997, 0.95)
    n = n_of(dur)
    t = tt(n)
    if kind == "marimba":
        y = np.sin(2 * np.pi * freq * t) * np.exp(-t / 0.32) + 0.5 * np.sin(2 * np.pi * 4 * freq * t) * np.exp(-t / 0.05) + 0.2 * np.sin(2 * np.pi * 10 * freq * t) * np.exp(-t / 0.02)
        return attack(y, 0.002)
    if kind == "epiano":
        idx = 1.5 * np.exp(-t / 0.3) + 0.15
        y = np.sin(2 * np.pi * freq * t + idx * np.sin(2 * np.pi * freq * t)) * np.exp(-t / 1.1) + 0.12 * np.sin(2 * np.pi * 2 * freq * t) * np.exp(-t / 0.25)
        return attack(y, 0.004)
    if kind == "bell":
        return bell(freq, dur, 0.7)
    raise KeyError(kind)


def clap():
    n = n_of(0.16)
    out = np.zeros(n)
    for k in (0.0, 0.012, 0.026):
        b = bandpass(noise(n_of(0.1)), 1200, 3400) * expenv(n_of(0.1), 0.02)
        s = n_of(k)
        out[s:s + len(b)] += b
    return out


def brush():
    n = n_of(0.3)
    y = lowpass(noise(n), 3200) * np.sin(np.pi * tt(n) / 0.3) ** 2
    return y


def shaker():
    n = n_of(0.05)
    return bandpass(noise(n), 5000, 9500) * expenv(n, 0.012)


def choose_tempo(anchor, pref):
    """A tempo that puts `anchor` (seconds) exactly on a bar line, as close to `pref` BPM as the anchor allows."""
    best = None
    for m in range(1, 60):
        bpm = 240.0 * m / anchor
        if 80 <= bpm <= 132 and (best is None or abs(bpm - pref) < abs(best[0] - pref)):
            best = (bpm, m)
    if best is None:
        return pref, max(1, round(anchor / (240.0 / pref)))
    return best


def music(duration, anchor, style):
    s = style
    n = n_of(duration)
    key = KEYS[s["key"]]
    bpm, m = choose_tempo(anchor, s["tempo"])
    bar = 240.0 / bpm
    beat = bar / 4
    j0 = int(np.floor((0 - anchor) / bar))
    j_end = max(int(np.floor((duration - 1.8 - anchor) / bar)), 0)
    prog = s["prog"]
    scale = SCALES[s["scale"]]

    def chord_of(j):
        off, q = s["final"] if j == j_end else s["pre"] if j == j_end - 1 else prog[j % len(prog)]
        return (key + off) % 12, q

    dry = np.zeros((n, 2))
    drums = np.zeros((n, 2))
    arp = s["arp"]
    steps = len(arp)
    j = j0
    while anchor + j * bar < duration:
        tb = anchor + j * bar
        pc, q = chord_of(j)
        root, padv, plkv = voicings(pc, q)
        last = j == j_end
        after = j >= 0
        end_t = duration if last else tb + bar
        # pad
        for k, f in enumerate(padv):
            v = pad_voice(f, (end_t - max(tb, 0)) + 0.5 if last else bar + 0.5, s["pad_lp"])
            place(dry, v, max(tb, 0) - 0.05 if tb > 0 else 0, s["pad"] * (1.0 if after else 0.85), pan=(-0.5, -0.15, 0.15, 0.5)[k])
        if last:
            for k, f in enumerate(plkv):
                place(dry, pluck(f, 3.2, 0.9985), tb + k * 0.035, 0.22, pan=(-0.4, -0.1, 0.1, 0.4)[k])
            place(drums, bass_note(root, 2.4), tb, 0.32)
            place(drums, kick(), tb, 0.34 * s["kick_gain"])
        else:
            # arpeggio: sparse and quieter while the tension builds, the full pattern once the logo has landed
            for st in range(steps):
                idx = arp[st] if after else (arp[st] if st % (steps // 4 or 1) == 0 else None)
                if idx is None:
                    continue
                ts = tb + st * bar / steps
                if ts < 0 or ts >= duration:
                    continue
                f = plkv[idx % 4]
                pan = (-0.7 if st % 2 == 0 else 0.7) if s.get("pingpong") else -0.25 + 0.5 * (st % 2)
                place(dry, voice(s["timbre"], f, 1.3), ts, s["arp_gain"] * (1.0 if after else 0.65) * (1.0 if st % 2 == 0 else 0.75), pan=pan)
            if after:
                for st8, which, ln in s["bass"]:                      # bass line
                    tb2 = tb + st8 * beat / 2
                    f = root * (2.0 if which == "oct" else 1.5 if which == "fifth" else 1.0)
                    if 0 <= tb2 < duration:
                        place(drums, bass_note(f, ln * beat / 2 * 1.9), tb2, s["bass_gain"])
                for st8 in s["kick"]:
                    t2 = tb + st8 * beat / 2
                    if 0 <= t2 < duration:
                        place(drums, kick(), t2, 0.36 * s["kick_gain"])
                for st8 in s["clap"]:
                    t2 = tb + st8 * beat / 2
                    if 0 <= t2 < duration:
                        place(drums, clap(), t2, 0.16, pan=0.15)
                for st16 in s["hat"]:
                    t2 = tb + st16 * beat / 4
                    if 0 <= t2 < duration:
                        place(drums, shaker() if s["shaker"] else hat(), t2, s["hat_gain"], pan=0.3)
                for st8 in s["brush"]:
                    t2 = tb + st8 * beat / 2
                    if 0 <= t2 < duration:
                        place(drums, brush(), t2, 0.10, pan=-0.2)
                if s["melody"]:                                       # a hook that belongs to this video
                    for st8, deg, ln in s["melody"][j % len(s["melody"])]:
                        t2 = tb + st8 * beat / 2
                        semi = scale[deg % 7] + 12 * (deg // 7)
                        if 0 <= t2 < duration:
                            place(dry, voice(s["lead"], midi_hz(72 + key + semi), max(0.6, ln * beat / 2 + 0.6)), t2, s["lead_gain"], pan=0.35)
            else:
                if s["intro"] == "ticks":                             # a nervous tick while the chat fills up
                    for st in range(8):
                        t2 = tb + st * beat / 2
                        if 0 <= t2 < anchor:
                            place(drums, hat(), t2, 0.05, pan=0.3)
                elif s["intro"] == "heart" and 0 <= tb < anchor:      # a slow heartbeat
                    place(drums, kick(), tb, 0.22)
                    place(drums, kick(), tb + 2 * beat, 0.14)
        j += 1

    rl = min(anchor, 1.6)
    if rl > 0.4:
        nn = n_of(rl)
        sweep = noise(nn)
        out = np.zeros(nn)
        blocks = 24
        for b in range(blocks):
            a0, a1 = b * nn // blocks, (b + 1) * nn // blocks
            fc = 400 * (3200 / 400) ** (b / (blocks - 1))
            out[a0:a1] = lowpass(sweep, fc)[a0:a1]
        out *= np.linspace(0, 1, nn) ** 2.2
        place(dry, out, anchor - rl, 0.16)

    mix = reverb(dry, s["wet"]) + drums
    mix *= np.minimum(1.0, tt(n) / 0.25)[:, None]
    fade = n_of(0.9)
    mix[-fade:] *= np.linspace(1, 0, fade)[:, None]
    return mix, bpm


# ---------------------------------------------------------------- sound effects (mono, peak about 1)
def sfx(kind, key=0):
    kf = 2 ** ((((key + 6) % 12) - 6) / 12)          # the effects are re-pitched into the video's key
    if kind == "tap":
        n = n_of(0.07)
        y = np.sin(2 * np.pi * 1700 * tt(n)) * expenv(n, 0.006) + 0.5 * np.sin(2 * np.pi * 380 * tt(n)) * expenv(n, 0.014)
        y += 0.3 * highpass(noise(n), 3000) * expenv(n, 0.004)
        return attack(y, 0.0005) * 0.5
    if kind == "key":
        n = n_of(0.05)
        y = bandpass(noise(n), 2200, 5200) * expenv(n, 0.005) + 0.5 * np.sin(2 * np.pi * 950 * kf * tt(n)) * expenv(n, 0.009)
        return attack(y, 0.0005) * 0.55
    if kind == "tick":
        n = n_of(0.09)
        return attack(np.sin(2 * np.pi * 1320 * kf * tt(n)) * expenv(n, 0.02), 0.001) * 0.45
    if kind == "pop":
        y = glide_sine(520 * kf, 1150 * kf, 0.11, 2.0) * expenv(n_of(0.11), 0.045)
        return attack(y, 0.002) * 0.9
    if kind == "pop2":
        y = glide_sine(700 * kf, 1050 * kf, 0.08, 2.0) * expenv(n_of(0.08), 0.03)
        return attack(y, 0.002) * 0.7
    if kind == "ding":
        y = np.zeros(n_of(1.2))
        for f, d, tau, g in ((1046.5 * kf, 1.2, 0.35, 1.0), (1568.0 * kf, 1.0, 0.28, 0.6)):
            b = bell(f, d, tau)
            y[:len(b)] += g * b
        return y * 0.55
    if kind == "chime":
        out = np.zeros(n_of(1.4))
        for i, f in enumerate((523.25 * kf, 659.25 * kf, 783.99 * kf, 1046.5 * kf)):
            b = bell(f, 1.0, 0.25)
            out[n_of(i * 0.075):n_of(i * 0.075) + len(b)] += b * (0.7 + 0.1 * i)
        return out * 0.5
    if kind in ("stamp", "stamp_big"):
        big = kind == "stamp_big"
        dur = 0.4 if big else 0.22
        n = n_of(dur)
        y = glide_sine(190 if big else 160, 52 if big else 60, dur, 10.0) * expenv(n, 0.14 if big else 0.07)
        y += 0.5 * lowpass(noise(n), 1500) * expenv(n, 0.02)
        y += 0.25 * highpass(noise(n), 2500) * expenv(n, 0.004)
        out = attack(y, 0.0008)
        if big:
            out = np.concatenate([out, np.zeros(n_of(0.2))])
            ring = bell(392 * kf, 1.4, 0.5)
            out[:len(ring)] += 0.12 * ring[:len(out)]
        return out * (1.0 if big else 0.8)
    if kind == "drop":
        n = n_of(0.5)
        y = glide_sine(900, 260, 0.45, 2.5) * np.linspace(1, 0.25, n_of(0.45)) * 0.35
        y = np.concatenate([y, np.zeros(n - len(y))])
        thud = lowpass(noise(n_of(0.12)), 600) * expenv(n_of(0.12), 0.03) * 0.6
        y[n_of(0.38):n_of(0.38) + len(thud)] += thud[: n - n_of(0.38)]
        return y * 0.7
    if kind in ("whoosh", "sheet", "open"):
        dur, amp, f0, f1 = {"whoosh": (0.5, 0.5, 300, 2400), "sheet": (0.3, 0.32, 400, 1800), "open": (0.3, 0.34, 500, 2600)}[kind]
        n = n_of(dur)
        x = noise(n)
        out = np.zeros(n)
        blocks = 16
        for b in range(blocks):
            a0, a1 = b * n // blocks, (b + 1) * n // blocks
            fc = f0 * (f1 / f0) ** (b / (blocks - 1))
            out[a0:a1] = bandpass(x, fc * 0.6, min(fc * 1.7, 12000))[a0:a1]
        env = np.sin(np.pi * tt(n) / dur) ** 2
        return out * env * amp * 2.2
    if kind == "tear":
        n = n_of(0.75)
        x = highpass(noise(n), 1200)
        crackle = (RNG.uniform(0, 1, n) > 0.965).astype(float) * noise(n)
        env = np.minimum(1, tt(n) / 0.12) * np.exp(-tt(n) / 0.5)
        return (0.5 * x + 0.9 * highpass(crackle, 1800)) * env * 0.7
    if kind == "print":
        n = n_of(0.45)
        out = np.zeros(n)
        for i in range(7):
            c = bandpass(noise(n_of(0.03)), 1500, 4500) * expenv(n_of(0.03), 0.006)
            s = n_of(i * 0.06)
            out[s:s + len(c)] += c * 0.8
        return out * 0.7
    if kind == "denied":
        n = n_of(0.3)
        f = 210 - 70 * tt(n) / 0.3
        sq = signal.square(2 * np.pi * np.cumsum(f) / SR)
        y = lowpass(sq, 700) * expenv(n, 0.14)
        return attack(y, 0.003) * 0.65
    raise KeyError(kind)
