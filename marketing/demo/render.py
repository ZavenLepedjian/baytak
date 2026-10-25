"""Render play-demo.html (1920 x 1080) to stills or a YouTube-ready MP4 by seeking the page's own clock, frame by frame.

Needs: pip install playwright imageio-ffmpeg   (uses your installed Chrome, no browser download)

usage:
  python render.py video baytak-play-demo.mp4 [fps]
  python render.py stills OUT_DIR t1 t2 ...

The MP4 is H.264 High, yuv420p (TV range, BT.709), 30 fps, with a silent AAC track (YouTube accepts it; add music in YouTube Studio).
"""
import pathlib
import subprocess
import sys

import imageio_ffmpeg
from playwright.sync_api import sync_playwright

PAGE = pathlib.Path(__file__).resolve().with_name("play-demo.html")
W, H = 1920, 1080


def open_page(p):
    browser = p.chromium.launch(channel="chrome", headless=True)
    page = browser.new_page(viewport={"width": W, "height": H}, device_scale_factor=1)
    page.goto(PAGE.as_uri())
    page.wait_for_function("window.baytakAd && window.baytakAd.ready")
    page.evaluate("window.baytakAd.exportMode()")
    page.evaluate("window.baytakAd.ready.then(() => window.baytakAd.pause())")
    page.evaluate("document.fonts.ready")
    return browser, page


def shot(page, t, **kw):
    page.evaluate(f"window.baytakAd.seek({t})")
    return page.screenshot(clip={"x": 0, "y": 0, "width": W, "height": H}, **kw)


def main():
    mode, out = sys.argv[1], pathlib.Path(sys.argv[2])
    with sync_playwright() as p:
        browser, page = open_page(p)
        if mode == "stills":
            out.mkdir(parents=True, exist_ok=True)
            for t in sys.argv[3:]:
                path = out / f"t{float(t):05.2f}.png"
                path.write_bytes(shot(page, float(t)))
                print(path)
        else:
            fps = int(sys.argv[3]) if len(sys.argv) > 3 else 30
            duration = page.evaluate("window.baytakAd.duration")
            frames = int(round(duration * fps))
            cmd = [imageio_ffmpeg.get_ffmpeg_exe(), "-y", "-loglevel", "error",
                   "-f", "image2pipe", "-framerate", str(fps), "-c:v", "mjpeg", "-i", "-",
                   "-f", "lavfi", "-i", "anullsrc=channel_layout=stereo:sample_rate=48000",
                   "-map", "0:v", "-map", "1:a", "-shortest",
                   "-vf", "scale=in_range=pc:out_range=tv,format=yuv420p", "-color_range", "tv",
                   "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709",
                   "-c:v", "libx264", "-profile:v", "high", "-preset", "slow", "-crf", "16", "-r", str(fps),
                   "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart", str(out)]
            enc = subprocess.Popen(cmd, stdin=subprocess.PIPE)
            for i in range(frames):
                enc.stdin.write(shot(page, i / fps, type="jpeg", quality=95))
                if i % (fps * 5) == 0:
                    print(f"frame {i}/{frames}", flush=True)
            enc.stdin.close()
            enc.wait()
            print("wrote", out, f"({frames} frames)", "exit", enc.returncode)
        browser.close()


if __name__ == "__main__":
    main()
