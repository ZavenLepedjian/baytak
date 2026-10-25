"""
Builds the ChatGPT review walkthrough video from real screenshots of Baytak running in ChatGPT.
Each scene is a screenshot (taken from the ChatGPT window, nothing drawn on top of it) with a caption bar.

  python chatgpt-plugin/demo/make-video.py

Reads scenes.json next to this file (image paths are relative to demo/frames/) and writes baytak-chatgpt-demo.mp4.
Needs: pillow and imageio-ffmpeg (pip install pillow imageio-ffmpeg). The frames/ folder is git-ignored.
"""
import json
import pathlib
import subprocess

import imageio_ffmpeg
from PIL import Image, ImageDraw, ImageFont

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
W, H = 1280, 720
TEAL, NIGHT, INK = (0, 132, 124), (3, 26, 25), (233, 246, 244)
FONT_DIR = ROOT / "node_modules" / "@expo-google-fonts" / "ibm-plex-sans-arabic"
BOLD = str(FONT_DIR / "700Bold" / "IBMPlexSansArabic_700Bold.ttf")
SEMI = str(FONT_DIR / "600SemiBold" / "IBMPlexSansArabic_600SemiBold.ttf")
REG = str(FONT_DIR / "400Regular" / "IBMPlexSansArabic_400Regular.ttf")


def font(path, size):
    return ImageFont.truetype(path, size)


def wrap(draw, text, fnt, width):
    lines, line = [], ""
    for word in text.split():
        trial = (line + " " + word).strip()
        if draw.textlength(trial, font=fnt) <= width:
            line = trial
        else:
            lines.append(line)
            line = word
    lines.append(line)
    return lines


def card(title, lines, logo=True):
    img = Image.new("RGB", (W, H), TEAL)
    d = ImageDraw.Draw(img)
    if logo:
        mark = Image.open(ROOT / "assets" / "adaptive-icon.png").convert("RGBA").resize((120, 120), Image.LANCZOS)
        tile = Image.new("RGBA", (120, 120), (251, 253, 252, 255))
        tile.alpha_composite(mark)
        mask = Image.new("L", (120, 120), 0)
        ImageDraw.Draw(mask).rounded_rectangle((0, 0, 119, 119), radius=24, fill=255)
        img.paste(tile.convert("RGB"), (120, 150), mask)
    d.text((120, 310), title, font=font(BOLD, 76), fill="white")
    y = 410
    for ln in lines:
        d.text((120, y), ln, font=font(REG, 32), fill="white")
        y += 48
    return img


def scene(path, caption, step):
    img = Image.new("RGB", (W, H), NIGHT)
    shot = Image.open(HERE / "frames" / path).convert("RGB")
    box_h = 580
    scale = box_h / shot.height
    shot = shot.resize((round(shot.width * scale), box_h), Image.LANCZOS)
    img.paste(shot, ((W - shot.width) // 2, 12))
    d = ImageDraw.Draw(img)
    d.rectangle((0, 604, W, H), fill=TEAL)
    if step:
        d.text((64, 612), step, font=font(SEMI, 20), fill=(207, 232, 229))
    cap = font(SEMI, 28)
    lines = wrap(d, caption, cap, W - 128)
    y = 640 if len(lines) == 1 else 636
    for ln in lines:
        d.text((64, y), ln, font=cap, fill="white")
        y += 36
    return img


def main():
    spec = json.loads((HERE / "scenes.json").read_text(encoding="utf-8"))
    work = HERE / "frames" / "_built"
    work.mkdir(parents=True, exist_ok=True)
    for old in work.glob("*.png"):
        old.unlink()
    entries = []
    for i, s in enumerate(spec["scenes"]):
        if s.get("type") == "card":
            img = card(s["title"], s["lines"])
        else:
            img = scene(s["image"], s["caption"], s.get("step", ""))
        out = work / f"{i:02d}.png"
        img.save(out)
        entries.append((out, s["seconds"]))
    listing = work / "list.txt"
    with listing.open("w", encoding="utf-8") as f:
        for out, secs in entries:
            f.write(f"file '{out.as_posix()}'\nduration {secs}\n")
        f.write(f"file '{entries[-1][0].as_posix()}'\n")
    target = HERE / "baytak-chatgpt-demo.mp4"
    subprocess.run([
        imageio_ffmpeg.get_ffmpeg_exe(), "-y", "-f", "concat", "-safe", "0", "-i", str(listing),
        "-f", "lavfi", "-i", "anullsrc=channel_layout=stereo:sample_rate=48000",
        "-vf", "fps=30,scale=1280:720:flags=lanczos,format=yuv420p", "-c:v", "libx264", "-crf", "20", "-preset", "medium",
        "-c:a", "aac", "-shortest", "-movflags", "+faststart", str(target)], check=True, capture_output=True)
    print(target.name, round(target.stat().st_size / 1e6, 2), "MB,", sum(s for _, s in entries), "seconds")


if __name__ == "__main__":
    main()
