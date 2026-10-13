---
version: 1
slug: "screens-dashboard-tsx"
primary_target: "screens/Dashboard.tsx"
related_targets: ["screens/Admin/Manage.tsx"]
---

# Surface brief: Home verdict + admin Collection

## Scope and mode
Operate. Native Expo app (iOS + Android, one shared look, light only). Targets: resident Home (`screens/Dashboard.tsx`), new admin Collection (`screens/Admin/Collection.tsx`), Manage entry (`screens/Admin/Manage.tsx`). The palette and type change is app-wide through `theme.ts` (replacement world); layouts of other screens follow in later passes. Code-led (Operate).

## Audience, job, proof
- Resident: "do I owe anything, how much, which currency?" in seconds. Older, mid-range phone, one-handed.
- Admin: 10-40 apartments, cash handed over in person, marked paid same day. Needs who-paid at a glance, mark paid in two taps, remind unpaid in two taps.
- Real material: receipt fields/totals, apartment numbers/floors, resident names (already in backend). No invented counts.
- Statuses shown as "Unpaid" / "Paid" (mapped from Pending / Paid). Currencies never summed.

## Direction (user-pinned: receipt-slip / door-plate world; roll skipped)
Operate rule: the world lends type, palette, density, one signature move only. Layout, navigation, controls stay platform standard.

## Direction contract
THESIS: Building fees are a stamped record, not a payments dashboard. The surface owns one idea: a receipt is a slip that gets stamped, and an apartment is a door plate. It refuses the hero-metric stat block, the pill-badge status and the greeting-hero header.
OWN-WORLD: Carbon-copy receipt-book ink. Committed deep ink blue (#1F2A7A, ink 900 #141A44 for text) carries the shell, primary buttons and door plates; ground is cool carbon-blue paper (#EDF0F7), slips are white. Stamp inks do the state jobs: verdigris green #0B6B4D = Paid, vermilion #B3321B = Unpaid, both with word + icon. Hairline engraved rim on plates in muted brass #B8913F. Type: IBM Plex Sans Arabic (Latin + Arabic, fixed-width digits) in 400/500/600/700; money and plate numerals in 700. Corners small (6-10), depth by offset soft shadow only.
STORY: A resident opens Home and reads one sentence of truth ("You owe 120 USD" or "You're all paid up") above the slips that make it up, and believes the record because it looks like the building's own receipt book. An administrator opens Collection and sees the whole building as door plates, taps one, stamps it Paid.
FIRST VIEWPORT: Resident Home: flat ink-blue header band with the apartment as a door plate (number + floor) on the leading side, no greeting. Below on ground: the verdict sentence at display size with per-currency amounts, then up to four unpaid slips (date, one-line breakdown, total, Unpaid stamp). Collection: summary line "14 of 18 apartments have paid", collected per currency, "Remind 4" action, receipt switcher, then door plates grouped by floor in a 4-column grid with status word under each number.
FORM: Door plate numerals + the stamp (signature move: a Paid stamp that settles onto the slip/plate with a short spring, honouring Reduce Motion; single authored moment). Pinned by the user; no roll, no seed key.
FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance

## Unresolved
RTL layout pass, dark mode, whether Wallet adopts the world, final Android 48dp targets.
