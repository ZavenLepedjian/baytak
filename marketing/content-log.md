# @getbaytak content log

App reviewed up to: `45c710d` (main, "Baytak: building fees, made clear")

Media branch `marketing-media` at `f2c4a2d926555868ffed5b40a556fb05d1fde9e7` (launch week files under `reels/`, carousel c1 under `carousels/`).

2026-10-20: the launch ad was already sent on Mon 2026-10-19 19:33 Beirut (Buffer post `6ad0a3671a39c7b5db32d14d`) with the version from `1cf93ce` (teal, 2,225,000 LBP, the first shared music), and cannot be edited. The seven posts still scheduled (r1 to r7) were switched to the versions with each video's own music (`f2c4a2d`, styles in `marketing/audio/styles.py`).

2026-10-18 (latest): the example lira amount was fixed to match 1 USD = 89,000 LBP (2,225,000 LBP = 25 USD, was 1,500,000); all nine videos were re-rendered, kept their soundtrack, and the eight Buffer posts point at the SHA above. Previous commits: `665f6ad` (1,500,000 LBP, with sound), `2aec631` (silent teal).

2026-10-18 (later): every video got a soundtrack (original synthesised music plus sound effects timed to the animation, `marketing/audio/`); the eight Buffer posts now point at the versions with sound at the SHA above. Silent teal renders are in `media-silent/` and at branch commit `2aec631`.

2026-10-18: every video was re-rendered in the logo teal (the first render was the old ink blue, which was off-brand) and the launch ad lost its "Get Baytak / For iPhone and Android" line (no store claims until the app is live). All eight Buffer posts were switched to the new jsDelivr URLs at the SHA above; captions and times are unchanged. The old blue files are in `media-old-blue/` and on the previous branch commit `0d1b8c8`.

## Scheduled and posted

| Date (Beirut) | Type | Topic | File | Buffer post |
| --- | --- | --- | --- | --- |
| Mon 2026-10-19 19:30 | Reel | Launch ad: the whole story | `reels/baytak-launch-ad.mp4` | 6ad0a3671a39c7b5db32d14d |
| Wed 2026-10-21 19:30 | Reel | Building fees shouldn't take 100 messages (group chat problem) | `reels/r1-messages.mp4` | 6ad0a39dd3c0a05adb08eea6 |
| Fri 2026-10-23 19:30 | Reel | Bill your whole building in one go (issue a receipt) | `reels/r2-issue.mp4` | 6ad0a39fa1ba7bd147e969d8 |
| Mon 2026-10-26 19:30 | Reel | Watch a whole building pay up (Collect, door plates) | `reels/r3-collect.mp4` | 6ad0a3a1a1ba7bd147e96a45 |
| Wed 2026-10-28 19:30 | Reel | Do I owe anything this month? (resident verdict) | `reels/r4-owe.mp4` | 6ad0a3a3d3c0a05adb08f16a |
| Fri 2026-10-30 19:30 | Reel | Dollars or lira? Both. Never mixed. (currencies) | `reels/r6-currency.mp4` | 6ad0a3a5d3c0a05adb08f1bf |
| Mon 2026-11-02 19:30 | Reel | Receipts can't be changed (immutability) | `reels/r5-locked.mp4` | 6ad0a3aa46219eb8e2fa5992 |
| Wed 2026-11-04 19:30 | Reel | No passwords to forget (phone + SMS code sign-in) | `reels/r7-login.mp4` | 6ad0a3ac68263b283a141014 |

## Ready, not yet published

- `carousels/c1-how-it-works.html`: 6-slide carousel, "How Baytak works: from one receipt to everyone paid" (building, join request,
  one receipt, stamp it paid, call to action). Rendered and checked; not yet on the media branch or in Buffer. Suggested caption:

  > How Baytak works, in six slides. The administrator adds the building, residents join their apartment, one receipt reaches everyone,
  > and every payment is stamped for all to see. Managing a building? Send us a DM to get set up.
