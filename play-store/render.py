"""Renders the Play Store graphics from the repo's own files.
  feature-graphic.png : 1024 x 500, from feature-graphic.html
  icon-512.png        : the app icon (assets/adaptive-icon.png) scaled to 512 x 512, otherwise unchanged
Run: python play-store/render.py   (needs playwright with Chrome, and pillow)"""
import pathlib
from PIL import Image
from playwright.sync_api import sync_playwright

root = pathlib.Path(__file__).resolve().parent
with sync_playwright() as p:
    browser = p.chromium.launch(channel="chrome", headless=True)
    page = browser.new_page(viewport={"width": 1024, "height": 500})
    page.goto((root / "feature-graphic.html").as_uri())
    page.wait_for_load_state("networkidle")
    page.evaluate("document.fonts.ready")
    page.screenshot(path=str(root / "feature-graphic.png"))
    browser.close()
Image.open(root.parent / "assets" / "adaptive-icon.png").convert("RGBA").resize((512, 512), Image.LANCZOS).save(root / "icon-512.png")
for name in ("feature-graphic.png", "icon-512.png"):
    print(name, Image.open(root / name).size, (root / name).stat().st_size, "bytes")
