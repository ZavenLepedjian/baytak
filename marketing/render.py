"""Render a Baytak ad or reel page to stills or an Instagram-ready MP4 by seeking the page's own clock.

Needs: pip install playwright imageio-ffmpeg   (uses your installed Chrome, no browser download)

usage:
  python render.py PAGE.html video OUT.mp4 [fps]
  python render.py PAGE.html stills OUT_DIR t1 t2 ...
  python render.py PAGE.html slides OUT_DIR          (carousel pages: one JPEG per slide, 1080 x 1350)

The MP4 is H.264 High, yuv420p (TV range, BT.709), 30 fps by default, with a silent AAC track:
Instagram's publishing API can reject videos that have no audio stream.
"""
import pathlib
import subprocess
import sys

import imageio_ffmpeg
from playwright.sync_api import sync_playwright


def open_page(p, html):
    browser = p.chromium.launch(channel="chrome", headless=True)
    page = browser.new_page(viewport={"width": 1080, "height": 1920}, device_scale_factor=1)
    page.goto(html.resolve().as_uri())
    page.wait_for_function("window.baytakAd && window.baytakAd.ready")
    page.evaluate("window.baytakAd.exportMode()")
    page.evaluate("window.baytakAd.ready.then(() => window.baytakAd.pause())")
    return browser, page


def shot(page, t, **kw):
    page.evaluate(f"window.baytakAd.seek({t})")
    height = page.evaluate("window.baytakAd.height || 1920")
    return page.screenshot(clip={"x": 0, "y": 0, "width": 1080, "height": height}, **kw)


def main():
    html, mode, out = pathlib.Path(sys.argv[1]), sys.argv[2], pathlib.Path(sys.argv[3])
    with sync_playwright() as p:
        browser, page = open_page(p, html)
        if mode == "slides":
            out.mkdir(parents=True, exist_ok=True)
            for i in range(page.evaluate("window.baytakAd.slides")):
                path = out / f"{html.stem}-{i + 1}.jpg"
                path.write_bytes(shot(page, i + 0.5, type="jpeg", quality=92))
                print(path)
        elif mode == "stills":
            out.mkdir(parents=True, exist_ok=True)
            for t in sys.argv[4:]:
                path = out / f"{html.stem}-t{float(t):05.2f}.png"
                path.write_bytes(shot(page, float(t)))
                print(path)
        else:
            fps = int(sys.argv[4]) if len(sys.argv) > 4 else 30
            duration = page.evaluate("window.baytakAd.duration")
            frames = int(round(duration * fps))
            cmd = [imageio_ffmpeg.get_ffmpeg_exe(), "-y", "-loglevel", "error",
                   "-f", "image2pipe", "-framerate", str(fps), "-c:v", "mjpeg", "-i", "-",
                   "-f", "lavfi", "-i", "anullsrc=channel_layout=stereo:sample_rate=48000",
                   "-map", "0:v", "-map", "1:a", "-shortest",
                   "-vf", "scale=in_range=pc:out_range=tv,format=yuv420p", "-color_range", "tv",
                   "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709",
                   "-c:v", "libx264", "-profile:v", "high", "-preset", "slow", "-crf", "17", "-r", str(fps),
                   "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart", str(out)]
            enc = subprocess.Popen(cmd, stdin=subprocess.PIPE)
            for i in range(frames):
                enc.stdin.write(shot(page, i / fps, type="jpeg", quality=95))
            enc.stdin.close()
            enc.wait()
            print("wrote", out, f"({frames} frames)", "exit", enc.returncode)
        browser.close()


if __name__ == "__main__":
    main()
