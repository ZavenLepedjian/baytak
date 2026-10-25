# Baytak marketing playbook (@getbaytak on Instagram)

This folder makes Baytak's Instagram content: animated Reels and carousels built from the app's own design system, rendered
to files, published as media on the `marketing-media` git branch, and scheduled through Buffer. A weekly scheduled task follows
this playbook; a person can follow it too.

## Who we talk to, and how

- **Building administrators** in Lebanon: they set Baytak up and bring their building. Main call to action: "Managing a building? Send us a DM."
- **Residents**: they feel the pain of fee chaos and pass Baytak to their admin. Call to action: "Share this with your building's admin."
- Voice: plain, warm, specific. Short sentences. No finance-speak, no hype, at most one emoji per caption (usually none).
- English captions; Arabic is welcome for the brand line (بيتك, "your home") and hashtags. The app is English-only today and Arabic is
  planned with no date, so never promise an Arabic release date.

## Hard rules

1. **Logo:** always the project's file `assets/adaptive-icon.png`, unchanged (never redrawn or recolored). The kit crops it to its
   bounds for the corner mark and splits it along its own white gap for the stamp animation; the final frame is always the intact logo.
2. **Only true claims.** Every feature shown must exist in the code. Use the app's real copy (screens, toasts, push texts). Never invent
   user counts, buildings, testimonials, ratings, prices, "free", store availability or press.
3. **Example data is fine and must look like an example:** Cedar Residence, apartments like 3B, 75 USD, 2,225,000 LBP.
   LBP amounts must be believable at today's rate: **1 USD = 89,000 LBP** (the owner's figure, October 2026; the market rate is about 89,500).
   2,225,000 LBP is exactly 25 USD. Check the rate before inventing any LBP amount, and update it here when it moves.
4. **Brand world (DESIGN.md):** the logo's teal (`#00847C`), paper, white slips, door plates. Green means Paid, vermilion means Unpaid, amber means
   Pending; never use them for decoration. State is always word plus icon. Small crisp corners, no pills.
5. The wallet is a side tool whose future is undecided: mention it at most in passing, never as a headline.
6. **Every video has sound.** After `render.py ... video`, run the soundtrack step (below). The music is generated here (nothing sampled or
   downloaded), so there is no licence to check; keep it that way. The logo lands on a bar line, so keep `stampLand`/`bowlStamp` animations.

## Files

| Path | What |
| --- | --- |
| `reels/kit.css`, `reels/kit.js` | Shared tokens, components (phone, status bar, door plate, stamp, slip, notification, end card), clock and viewer |
| `reels/r*.html` | One file per Reel, 1080 x 1920, 9 to 12 seconds, ends on the logo end card (set `--end` on the stage) |
| `carousels/c*.html` | One file per carousel, 1080 x 1350 (`class="stage portrait"`), one `<section class="slide">` per slide, `Kit.init({ slides: true })` |
| `ad/baytak-ad.html` | The 31 s launch ad |
| `render.py` | Renders a page to `video` (MP4 with silent AAC), `slides` (JPEGs) or `stills` (QA PNGs) |
| `audio/` | The soundtrack: `make.py` adds original synthesised music and sound effects timed to the page's own animations (`events.py` reads them, `synth.py` makes the sound). Silent renders are kept in `media-silent/`, finished videos in `media/` |
| `media/` | Rendered files (local copies of what is on the `marketing-media` branch) |
| `content-log.md` | What has been made and scheduled, and the last app commit reviewed. Read it first, update it last |
| `instagram-plan.md` | The launch plan (first 8 Reels) |

Python with the render dependencies: `C:\Users\zaven\source\repos\baytak\marketing-tools\venv\Scripts\python.exe`
(create it with `python -m venv` + `pip install playwright imageio-ffmpeg pillow` if missing). Chrome must be installed.

```bash
PY=/c/Users/zaven/source/repos/baytak/marketing-tools/venv/Scripts/python
cd /c/Users/zaven/source/repos/baytak/baytak/marketing
$PY render.py reels/r8-name.html stills /tmp/qa 1 3 5 7 9       # look at these before rendering video
$PY render.py reels/r8-name.html video media-silent/r8-name.mp4
$PY audio/make.py file reels/r8-name.html media-silent/r8-name.mp4 media/r8-name.mp4   # adds music + sound effects
$PY render.py carousels/c2-name.html slides media/c2-name      # c2-name-1.jpg ... c2-name-N.jpg
```

Reel craft: hook text on screen in the first second (people watch muted); one idea per Reel; keep text inside y 180 to 1500 (Instagram
covers the bottom with the caption); use the app's real screens and wording; end card from the kit. Carousel craft: 5 to 7 slides; slide 1
is the hook; one point per slide; last slide is the logo lockup with the call to action; keep slide count in the top right.

## Publishing media (git)

Media lives only on the orphan branch `marketing-media` (never on `main`). Use a temporary worktree so the app's working copy is untouched:

```bash
REPO=/c/Users/zaven/source/repos/baytak/baytak; WT=$(mktemp -d)/media
git -C $REPO fetch origin marketing-media
git -C $REPO worktree add $WT marketing-media
mkdir -p $WT/reels $WT/carousels/c2-name
cp $REPO/marketing/media/r8-name.mp4 $WT/reels/
cp $REPO/marketing/media/c2-name/*.jpg $WT/carousels/c2-name/
git -C $WT add -A && git -C $WT commit -m "Add week of <date> media for @getbaytak"
git -C $WT push origin marketing-media
SHA=$(git -C $WT rev-parse HEAD); git -C $REPO worktree remove $WT
```

Public URLs: `https://cdn.jsdelivr.net/gh/ZavenLepedjian/baytak@$SHA/<path>` (jsDelivr serves the right content type; pin the SHA).
Check every URL with `curl -s -o /dev/null -w "%{http_code} %{content_type} %{size_download}"` and compare the size with the local file.
jsDelivr sometimes answers 503 on the first request for a new file: wait a few seconds and retry.

## Scheduling (Buffer)

- Organization `5d170b8e258cc54c071ef1cd` ("My Organization"), channel `6ad09b81778b240e2f166eb4` (Instagram business, getbaytak).
- Free plan: **at most 10 scheduled posts at a time**, and **no first comment** (put hashtags at the end of the caption).
- Reel: `metadata.instagram = { type: "reel", shouldShareToFeed: true }`, one `video` asset with `metadata.thumbnailOffset` (ms) on a frame that shows the hook.
- Carousel: `metadata.instagram = { type: "post", shouldShareToFeed: true }`, 2 to 10 `image` assets in order, each with `altText`.
- `schedulingType: "automatic"`, `mode: "customScheduled"`, `dueAt` in Beirut time: `+03:00` from the last Sunday of March to the last
  Sunday of October, `+02:00` otherwise.
- Slots: Monday, Wednesday, Friday 19:30; an optional fourth post on Saturday 12:00.
- Hashtags: `#Baytak #Lebanon #Beirut #BuildingManagement #PropertyManagement #Apartment #لبنان #بيروت` (swap one or two for the topic).

## The weekly routine

1. Read `content-log.md`.
2. **What changed in the app:** `git -C $REPO fetch origin main`, then `git log <last reviewed>..origin/main --stat` and read the changed
   screens, `PRODUCT.md`, `DESIGN.md` and backend push texts. A new or changed user-facing feature becomes this week's lead post, built
   from the real screen and wording. Note changes that make older posts wrong (renamed buttons, removed features) in the summary.
3. **Capacity and gaps:** list the channel's scheduled posts. Capacity = 10 minus scheduled. The coming week is next Monday to Sunday.
4. **Plan** 3 or 4 posts for that week (at least one carousel and one Reel when capacity allows), only in empty slots, never repeating a
   topic from the log within four weeks. If capacity is 0, make nothing new and say so.
5. **Make** them with the kit. Render QA stills, look at them, fix overlaps or clipped text, then render the files.
6. **Publish media** on `marketing-media`, verify the URLs.
7. **Schedule** in Buffer, then confirm with `list_posts` that each one shows `scheduled` with the right time.
8. **Log** every post in `content-log.md` (date, type, topic, files, Buffer id) and update "App reviewed up to".

## Topic bank (not yet used)

- Payment reminders: the admin taps "Remind 9 unpaid" and unpaid residents get "Payment reminder: … is still unpaid." (once a day per receipt).
- Send a receipt to selected apartments only (Select apartments).
- Receipt format: the admin picks the currency and the fields once (maintenance, electricity, elevator, generator, water, cleaning…).
- Join requests: approve or reject, residents get "Request approved".
- Finding your building: nearest buildings first, using your location.
- Receipt history: every receipt, paid or not, in one place.
- Admin vs resident: two roles, one app (carousel).
- "What's on a Baytak receipt" (carousel): date, lines, total, currency, stamp.
- Profile and emergency contact; deleting your account when you leave.
- Start of month = fees time (seasonal Reel on the 1st).
- FAQ carousel: Do residents pay through the app? (No: the admin records payments.) Can receipts be edited? (No.) USD or LBP? (Both, never mixed.)
