"""Gives a rendered, silent Baytak video its soundtrack: original music plus sound effects timed to the page's own animation.

usage:
  python make.py all                       every video listed in JOBS (silent originals are read from media-silent/, results go to media/)
  python make.py one NAME                  one job, e.g. r3-collect
  python make.py file PAGE.html SILENT.mp4 OUT.mp4 [slow]    any other page (a new Reel): paths relative to marketing/
  python make.py qa NAME OUT.png           also writes a spectrogram with the sound events marked (to check timing by eye)

The video stream is copied untouched; only the audio track is replaced (AAC 192 kb/s, 48 kHz, about -16 LUFS).
"""
import pathlib
import subprocess
import sys
import wave

import imageio_ffmpeg
import numpy as np
from PIL import Image, ImageDraw
from scipy import signal

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import synth  # noqa: E402
from styles import FOR_VIDEO, STYLES  # noqa: E402
from events import read_events  # noqa: E402

ROOT = pathlib.Path(__file__).resolve().parents[1]
SILENT = ROOT / "media-silent"
OUT = ROOT / "media"
SR = synth.SR

# name: (page, slow, (w, h))
JOBS = {
    "baytak-launch-ad": ("ad/baytak-ad.html", 1.0, (1080, 1920)),
    "r1-messages": ("reels/r1-messages.html", 1.0, (1080, 1920)),
    "r2-issue": ("reels/r2-issue.html", 1.0, (1080, 1920)),
    "r3-collect": ("reels/r3-collect.html", 1.0, (1080, 1920)),
    "r4-owe": ("reels/r4-owe.html", 1.0, (1080, 1920)),
    "r5-locked": ("reels/r5-locked.html", 1.0, (1080, 1920)),
    "r6-currency": ("reels/r6-currency.html", 1.0, (1080, 1920)),
    "r7-login": ("reels/r7-login.html", 1.0, (1080, 1920)),
    "baytak-play-demo": ("demo/play-demo.html", 1.2, (1920, 1080)),
}

GAIN = {"tap": 0.42, "key": 0.34, "tick": 0.3, "pop": 0.5, "pop2": 0.36, "ding": 0.7, "chime": 0.55, "stamp": 0.62, "stamp_big": 0.95,
        "drop": 0.55, "whoosh": 0.32, "sheet": 0.28, "open": 0.28, "tear": 0.48, "print": 0.34, "denied": 0.55}


def clean(events):
    """Drop sounds that would double up: a tap just before a stamp, a tick on top of a stamp, the same sound twice within 60 ms."""
    ev = sorted((t, k) for t, k, _ in events)
    out = []
    for i, (t, k) in enumerate(ev):
        if k == "tap" and any(k2 == "stamp" and 0 <= t2 - t <= 0.12 for t2, k2 in ev):
            continue
        if k == "tick" and any(k2 == "stamp" and abs(t2 - t) < 0.03 for t2, k2 in ev):
            continue
        if any(k2 == k and 0 < t - t2 < 0.06 for t2, k2 in out):
            continue
        out.append((t, k))
    return out


def soundtrack(page, slow, size, name):
    duration, raw, _ = read_events(ROOT / page, slow, *size)
    events = clean(raw)
    anchors = [t for t, k in events if k == "stamp_big"]
    anchor = anchors[0] if anchors else min(duration * 0.5, 6.0)
    style = STYLES[FOR_VIDEO.get(name, "demo")]
    music, bpm = synth.music(duration, anchor, style)
    music *= 0.20 / max(1e-9, np.sqrt(np.mean(music ** 2)))       # music bed at about -14 dBFS RMS before mastering
    fx = np.zeros_like(music)
    for i, (t, kind) in enumerate(events):
        x = synth.sfx(kind, synth.KEYS[style["key"]])
        pan = (((i * 37) % 11) - 5) / 14.0
        synth.place(fx, x, t, GAIN[kind], pan)
    mix = music + synth.reverb(fx, 0.14)
    mix = np.tanh(mix * 1.1) / np.tanh(1.1)                       # soft limiter
    mix *= 0.89 / max(0.89, np.max(np.abs(mix)))
    return mix, events, bpm, duration, anchor


def write_wav(path, mix):
    pcm = (np.clip(mix, -1, 1) * 32767).astype("<i2")
    with wave.open(str(path), "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def mux(video_in, wav, video_out):
    cmd = [imageio_ffmpeg.get_ffmpeg_exe(), "-y", "-loglevel", "error", "-i", str(video_in), "-i", str(wav),
           "-map", "0:v", "-map", "1:a", "-c:v", "copy", "-filter:a", "loudnorm=I=-16:TP=-1.5:LRA=9",
           "-c:a", "aac", "-b:a", "192k", "-ar", str(SR), "-shortest", "-movflags", "+faststart", str(video_out)]
    subprocess.run(cmd, check=True)


def qa_image(mix, events, path):
    mono = mix.mean(axis=1)
    f, t, S = signal.spectrogram(mono, SR, nperseg=2048, noverlap=1536)
    S = 10 * np.log10(S + 1e-12)
    S = np.clip((S + 100) / 70, 0, 1)
    keep = f < 9000
    S = S[keep][::-1]
    img = Image.fromarray((S * 255).astype("uint8")).resize((1600, 520), Image.BILINEAR).convert("RGB")
    d = ImageDraw.Draw(img)
    total = len(mono) / SR
    for ts, k in events:
        x = int(ts / total * 1600)
        d.line([(x, 0), (x, 520)], fill=(255, 200, 0) if k != "stamp_big" else (255, 60, 60), width=1)
    img.save(path)


def run(name, qa=None, job=None, src=None, dst=None):
    page, slow, size = job or JOBS[name]
    mix, events, bpm, duration, anchor = soundtrack(page, slow, size, name)
    wav = ROOT / "audio" / f"_{name}.wav"
    write_wav(wav, mix)
    mux(src or SILENT / f"{name}.mp4", wav, dst or OUT / f"{name}.mp4")
    wav.unlink()
    rms = 20 * np.log10(np.sqrt(np.mean(mix ** 2)) + 1e-9)
    st = STYLES[FOR_VIDEO.get(name, "demo")]
    print(f"{name}: {duration:.1f}s, {FOR_VIDEO.get(name, 'demo')} in {st['key']}{' minor' if st['scale'] == 'minor' else ''}, {bpm:.0f} BPM, logo lands at {anchor:.2f}s (bar line), {len(events)} sound cues, peak {np.max(np.abs(mix)):.2f}, rms {rms:.1f} dBFS")
    if qa:
        qa_image(mix, events, qa)


if __name__ == "__main__":
    mode = sys.argv[1]
    OUT.mkdir(exist_ok=True)
    if mode == "all":
        for n in JOBS:
            run(n)
    elif mode == "one":
        run(sys.argv[2])
    elif mode == "qa":
        run(sys.argv[2], sys.argv[3])
    elif mode == "file":
        page, src, dst = sys.argv[2], ROOT / sys.argv[3], ROOT / sys.argv[4]
        slow = float(sys.argv[5]) if len(sys.argv) > 5 else 1.0
        dst.parent.mkdir(parents=True, exist_ok=True)
        run(pathlib.Path(page).stem, job=(page, slow, (1920, 1080) if "demo" in page else (1080, 1920)), src=src, dst=dst)
