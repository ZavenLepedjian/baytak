"""App Store screenshots: the app's own screens (the replicas the Reels are built from, in the app's teal design tokens),
captured full-bleed at 1206 x 2622, the size App Store Connect takes for the iPhone 6.3-inch slot ("iPhone with Dynamic Island",
the only iPhone slot on the version page as of Oct 2026; it accepts 1179 x 2556 or 1206 x 2622, and 1290 x 2796 is rejected
with "File dimensions are invalid"). Check the sizes the slot lists on the version page before changing this.

  python shots.py OUT_DIR

Each shot seeks a marketing page to a moment where the screen shows what the shot is about, then captures just the phone's
screen. Apple wants flattened images, so the screen's rounded corners are squared off before capture and the result is RGB.
These are recreations of the app's screens, not captures from a phone: say so if you are ever asked.
"""
import io
import pathlib
import sys

from PIL import Image
from playwright.sync_api import sync_playwright

ROOT = pathlib.Path(__file__).resolve().parents[1]
W, H = 1206, 2622

# name, page, seek time (seconds), selector of the phone screen
SHOTS = [
    ("1-sign-in", "reels/r7-login.html", 2.2, "#ph .screen"),
    ("2-verification-code", "reels/r7-login.html", 4.15, "#ph .screen"),
    ("3-what-you-owe", "ad/baytak-ad.html", 17.0, "#phR .screen"),
    ("4-new-receipt", "ad/baytak-ad.html", 11.0, "#phA .screen"),
    ("5-who-has-paid", "ad/baytak-ad.html", 21.0, "#phA .screen"),
]


def capture(p, page_path, t, selector):
    browser = p.chromium.launch(channel="chrome", headless=True)
    page = browser.new_page(viewport={"width": 1080, "height": 1920}, device_scale_factor=3)
    page.goto((ROOT / page_path).resolve().as_uri())
    page.wait_for_function("window.baytakAd && window.baytakAd.ready")
    page.evaluate("window.baytakAd.exportMode()")
    page.evaluate("window.baytakAd.ready.then(() => window.baytakAd.pause())")
    page.evaluate("document.fonts.ready")
    page.add_style_tag(content=".screen { border-radius: 0 !important; } .tap, #sms { display: none !important; }")
    page.evaluate(f"window.baytakAd.seek({t})")
    png = page.locator(selector).first.screenshot()
    browser.close()
    return Image.open(io.BytesIO(png)).convert("RGB").resize((W, H), Image.LANCZOS)


def main():
    out = pathlib.Path(sys.argv[1])
    out.mkdir(parents=True, exist_ok=True)
    with sync_playwright() as p:
        for name, page, t, selector in SHOTS:
            img = capture(p, page, t, selector)
            path = out / f"{name}.png"
            img.save(path)
            print(path, img.size)


if __name__ == "__main__":
    main()
