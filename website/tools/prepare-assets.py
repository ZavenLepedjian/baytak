"""
Builds the binary assets of the website from files that already live in this repo.
Run from anywhere:  python website/tools/prepare-assets.py <path to an unpacked @phosphor-icons/core package>

  - fonts : the app's own IBM Plex Sans Arabic (node_modules/@expo-google-fonts), subset to Latin, as woff2
  - images: the app's login photo, resized; the logo is copied byte for byte (it must stay unchanged)
  - icons : Phosphor (MIT) regular icons, merged into one SVG sprite
Needs: pip install fonttools brotli pillow
"""
import pathlib, re, shutil, sys

root = pathlib.Path(__file__).resolve().parents[2]
out = root / "website" / "public" / "assets"
(out / "fonts").mkdir(parents=True, exist_ok=True)
(out / "img").mkdir(parents=True, exist_ok=True)

# ---- fonts
from fontTools import subset
faces = {"400": "400Regular", "600": "600SemiBold", "700": "700Bold"}
unicodes = "U+0020-007E,U+00A0-00FF,U+2010-2015,U+2018-201D,U+2022,U+2026,U+20AC"
for weight, folder in faces.items():
    src = root / "node_modules" / "@expo-google-fonts" / "ibm-plex-sans-arabic" / folder / f"IBMPlexSansArabic_{folder}.ttf"
    dst = out / "fonts" / f"plex-{weight}.woff2"
    opts = subset.Options()
    opts.flavor = "woff2"
    opts.layout_features = ["kern", "liga", "tnum", "ccmp", "locl", "mark", "mkmk"]
    opts.notdef_outline = True
    font = subset.load_font(str(src), opts)
    s = subset.Subsetter(opts)
    s.populate(unicodes=subset.parse_unicodes(unicodes))
    s.subset(font)
    subset.save_font(font, str(dst), opts)
    print(dst.name, dst.stat().st_size, "bytes")

# Arabic wordmark subset: only the four letters of "بيتك", with the joining features they need
opts = subset.Options()
opts.flavor = "woff2"
opts.layout_features = ["init", "medi", "fina", "isol", "calt", "ccmp", "liga", "rlig", "locl", "mark", "mkmk", "kern"]
src = root / "node_modules" / "@expo-google-fonts" / "ibm-plex-sans-arabic" / "700Bold" / "IBMPlexSansArabic_700Bold.ttf"
font = subset.load_font(str(src), opts)
sub = subset.Subsetter(opts)
sub.populate(unicodes=subset.parse_unicodes("U+0628,U+064A,U+062A,U+0643,U+200C-200F"))
sub.subset(font)
subset.save_font(font, str(out / "fonts" / "plex-arabic-700.woff2"), opts)
print("plex-arabic-700.woff2", (out / "fonts" / "plex-arabic-700.woff2").stat().st_size, "bytes")

# ---- images
from PIL import Image
photo = Image.open(root / "assets" / "images" / "login-background.jpg").convert("RGB")
for width in (640, 1000):
    img = photo.resize((width, round(photo.height * width / photo.width)), Image.LANCZOS)
    path = out / "img" / f"beirut-{width}.jpg"
    img.save(path, "JPEG", quality=80, optimize=True, progressive=True)
    print(path.name, img.size, path.stat().st_size, "bytes")
crops = {"clock": ((1350, 2200, 2700, 3300), (1400, 800)), "arcade": ((2500, 2200, 3843, 3700), (1100, 700))}
for name, (box, widths) in crops.items():
    piece = photo.crop(box)
    for width in widths:
        img = piece.resize((width, round(piece.height * width / piece.width)), Image.LANCZOS)
        path = out / "img" / f"beirut-{name}-{width}.jpg"
        img.save(path, "JPEG", quality=80, optimize=True, progressive=True)
        print(path.name, img.size, path.stat().st_size, "bytes")
shutil.copyfile(root / "assets" / "adaptive-icon.png", out / "img" / "baytak-logo.png")
shutil.copyfile(root / "assets" / "favicon.png", out / "img" / "favicon.png")

# ---- icons
if len(sys.argv) > 1:
    pkg = pathlib.Path(sys.argv[1])
    names = ["receipt", "buildings", "user-check", "paper-plane-tilt", "checks", "circle", "list", "x", "arrow-right",
             "shield-check", "lock-simple", "device-mobile", "chat-circle-text", "envelope-simple", "trash", "coins",
             "text-aa", "wifi-high", "prohibit", "bell-simple", "caret-down", "arrow-down", "check-circle", "arrow-up-right",
             "check", "map-pin"]
    symbols = []
    for n in names:
        svg = (pkg / "assets" / "regular" / f"{n}.svg").read_text(encoding="utf-8")
        inner = re.search(r"<svg[^>]*>(.*)</svg>", svg, re.S).group(1).strip()
        symbols.append(f'<symbol id="{n}" viewBox="0 0 256 256">{inner}</symbol>')
    sprite = '<svg xmlns="http://www.w3.org/2000/svg" style="display:none">\n' + "\n".join(symbols) + "\n</svg>\n"
    (out / "icons.svg").write_text(sprite, encoding="utf-8")
    print("icons.svg", len(names), "icons")
