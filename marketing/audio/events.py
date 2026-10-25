"""Reads every CSS animation of a marketing page (the same pages render.py films) and returns the moments that should make a sound.

An animation's start time is its delay, so the sound lands on the frame where the motion begins. Nothing is guessed from the video.
usage: python events.py PAGE.html [slow]     (prints the distinct animation names and the sound events)
"""
import json
import pathlib
import sys

from playwright.sync_api import sync_playwright

# animation name -> sound. Captions rising and fading make no sound on purpose.
SOUND = {
    "pop": "pop",              # chat bubble / message lands
    "slipUp": "tear",          # the receipt slip wipes the chat away
    "stemDrop": "drop",        # the logo stem falls
    "bowlStamp": "stamp_big",  # the logo bowl lands like a stamp
    "stampLand": "stamp",      # a Paid / Unpaid stamp lands
    "plateSettle": "stamp",    # a door plate is stamped
    "tap": "tap",              # a finger tap
    "notifIn": "ding",         # a push notification arrives
    "toastIn": "chime",        # success toast
    "sheetUp": "sheet",        # bottom sheet slides up
    "phoneUp": "whoosh",       # phone slides in
    "inRight": "whoosh",
    "inLeft": "whoosh",
    "appOpen": "open",         # app opens from the notification
    "printIn": "print",        # a receipt slip prints
    "cardIn": "tick",          # a feature card slides in
    "shake": "denied",         # a locked receipt refuses an edit
}

JS = """
() => {
  const stage = document.getElementById('stage');
  return stage.getAnimations({ subtree: true }).map((a) => {
    const t = a.effect.getTiming();
    const el = a.effect.target;
    return { name: a.animationName || '', delay: t.delay, dur: t.duration, cls: (el.className && el.className.baseVal === undefined ? el.className : '') + '', id: el.id || '' };
  });
}
"""


def read_events(page_path, slow=1.0, width=1080, height=1920):
    page_path = pathlib.Path(page_path).resolve()
    with sync_playwright() as p:
        browser = p.chromium.launch(channel="chrome", headless=True)
        page = browser.new_page(viewport={"width": width, "height": height}, device_scale_factor=1)
        page.goto(page_path.as_uri())
        page.wait_for_function("window.baytakAd && window.baytakAd.ready")
        page.evaluate("window.baytakAd.exportMode()")
        page.evaluate("window.baytakAd.ready.then(() => window.baytakAd.pause())")
        anims = page.evaluate(JS)
        extra = page.evaluate("window.__sfx || []")   # moments a page drives from script (typing, arrivals)
        duration = page.evaluate("window.baytakAd.duration")
        browser.close()
    events = []
    for a in anims:
        kind = SOUND.get(a["name"])
        if kind and a["delay"] is not None:
            events.append((round(a["delay"] / 1000.0 * slow, 3), kind, a["name"]))
    events += [(round(t * slow, 3), kind, "script") for t, kind in extra]
    events.sort()
    return duration, events, sorted({a["name"] for a in anims})


if __name__ == "__main__":
    page = sys.argv[1]
    slow = float(sys.argv[2]) if len(sys.argv) > 2 else 1.0
    w, h = (1920, 1080) if "demo" in page else (1080, 1920)
    d, ev, names = read_events(page, slow, w, h)
    print("duration", d)
    print("animation names:", names)
    print(json.dumps(ev))
