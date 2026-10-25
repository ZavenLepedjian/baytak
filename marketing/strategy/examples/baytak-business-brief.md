# Business brief: Baytak (example input and test fixture)

Written 2026-10-20 from the product's own documents (`PRODUCT.md`, `DESIGN.md`, `marketing/README.md`). Facts here are the owner's; anything
marked "unknown" really is unknown and the engine must ask or assume visibly.

## 1. What the business is
Baytak (Arabic *bayt*, "house"; بيتك, "your home") is a mobile app that makes building fees clear in Lebanese residential buildings.
An administrator issues a receipt, every resident sees what they owe, and nobody has to chase a group chat or a paper list.

## 2. What you sell, and for how much
The app on iOS and Android. **Pricing and business model: not decided.** Never state a price or say "free".
A personal USD/LBP wallet exists as a side tool; its future is undecided; mention it at most in passing.

## 3. Who it is for
- **Building administrators** (one per building): register the building, define the receipt format, approve residents, issue receipts, mark them paid.
  Administrator access is approved by the operator (the owner) before it works.
- **Residents** (owners or renters): see what they owe and whether it is paid; get a push notification for each receipt.
- Possible others to investigate, not assumed: professional building managers, non-resident owners, caretakers, real-estate agents.

## 4. The problem and how people solve it today
Building fees in Lebanon are collected through WhatsApp groups, paper lists and spreadsheets, in two currencies (USD and LBP). Unknown and to be researched:
how fees are really billed and collected, how disputes arise, what is trusted.

## 5. Why different, and what proves it (only things that exist in the product)
One receipt reaches the whole building or chosen apartments; every amount shows its currency; status (Paid, Unpaid, Pending) is always visible; receipts cannot be
edited after they are issued; totals are computed on the server; passwordless sign-in with a phone number and a 6-digit SMS code; push notifications.
An integration lets an administrator issue receipts by chatting with ChatGPT or Claude; it is **not public yet**, so it is never a live claim.
**Proof assets that do not exist:** testimonials, user or building counts, ratings, press, awards, pricing, partnerships.

## 6. Where, and in which languages
Lebanon (starting city or region not decided). Beirut time zone (UTC+2 from Sunday 2026-10-25, UTC+3 in summer).
The app is **English only today**. Arabic with right-to-left layout is planned **with no date**: never promise a date. Captions are in English; Arabic is welcome for the brand
line and hashtags.

## 7. Goal for the next 90 days
Make the problem and the product obvious, start conversations with building administrators, and get the first buildings issuing receipts. No numeric target set yet; the engine
should propose a hypothesis with reasoning. Start date for the plan: Monday 2026-10-26.

## 8. Budget and time
Paid budget: **not decided** (none, small test, moderate: the engine should present scenarios). Owner time per week: unknown, ask. One person; the content pipeline (below) does the production.

## 9. What exists already
- Name Baytak; handle @getbaytak on Instagram; website `https://baytakapi8803.z16.web.core.windows.net/` (landing, privacy, terms, support, delete-account); no website analytics.
- **Logo:** the file `assets/adaptive-icon.png` (a teal "b"), always used exactly as is. Never redraw, recolour, trace or restyle it.
- **Colour:** logo teal `#00847C` (stem `#009898`) as the single accent; green means Paid, vermilion means Unpaid, amber means Pending, never for decoration. Small crisp corners, no pills.
- **Voice:** plain, warm, specific, short sentences, no finance-speak, no hype, at most one emoji per caption.
- A content pipeline that makes animated Reels (1080x1920, 9 to 12 seconds, original synthesised soundtrack) and carousels (1080x1350, 5 to 7 slides) from the app's own design system, scheduled through Buffer
  (free plan: at most 10 scheduled posts, no first comment). A weekly task prepares the next week's posts. Typical cadence so far: Monday, Wednesday, Friday at 19:30 Beirut time.
- Eight posts are scheduled from 2026-10-19 to 2026-11-04 (see `marketing/content-log.md`).
- **Store state (as of 2026-10-20, verify before relying on it):** Apple App Store has an old 2023 version live and version 1.7.0 awaits a build and submission; Apple removes the app on 2026-12-28 unless an
  update is approved first. Google Play listing is "Removed by Google" with an appeal pending, so **do not assume Android is available**. Any "download" call to action must be gated on a store being live.
- The previous generation of the app (2023) exists; its users were deliberately not migrated. There are **no user, building or revenue numbers to quote**.

## 10. Rules
- **Only true claims**: every feature shown must exist in the product. Never invent user counts, buildings, testimonials, ratings, prices, "free", store availability or press.
- Example people and data must look like examples (Cedar Residence, apartment 3B, 75 USD, 2,225,000 LBP). Never use real names or phone numbers.
- **Exchange rate: 1 USD = 89,000 LBP** (owner's figure, October 2026; market about 89,500). Any LBP example must be believable at this rate (2,225,000 LBP = 25 USD). Re-check before each build.
- Never write "we do not sell your data" or "no advertising" in any form. Do not promise an Arabic release date. The wallet is never a headline.
- Every video has sound. Any new tracking (website analytics, tracked links) must be reflected in the privacy policy first.
- The review demo phone numbers used for the stores must never appear in marketing.
- Never post, schedule, push to git or spend without the owner's approval.

## 11. Competitors and alternatives
Known: WhatsApp groups, paper lists, spreadsheets. Unknown: building-management or fee apps used in Lebanon and the region, other products named "Baytak" (name collision to check). None admired or feared yet named.

## 12. Anything else
Money apps need trust in a low-trust environment. The operator-approval step for administrators could be a trust feature to promote or an internal step to hide: unknown, ask.
