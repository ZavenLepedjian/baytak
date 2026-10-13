---
name: Baytak
description: A building's receipt book, kept on a phone. Carbon-copy ink blue, paper, and two stamp inks for Paid and Unpaid.
colors:
  ink: "#1F2A7A"
  ink-dark: "#18205F"
  ink-deep: "#141A44"
  ink-soft: "#DDE2F5"
  ink-tint: "#EEF0FA"
  paper: "#EDF0F7"
  slip: "#FFFFFF"
  paper-muted: "#E3E7F2"
  rule: "#D3D9EA"
  rule-strong: "#AEB6D2"
  border-input: "#7A81A3"
  text: "#141A44"
  text-muted: "#4B5273"
  text-faint: "#666C8A"
  verdigris: "#0B6B4D"
  verdigris-wash: "#DDF1E8"
  vermilion: "#B3321B"
  vermilion-deep: "#8F2815"
  vermilion-wash: "#FBE3DC"
  amber: "#8A5A00"
  amber-wash: "#FBEFD0"
  brass: "#B8913F"
  plate-text: "#F4F6FF"
  on-ink: "#FFFFFF"
  on-ink-muted: "rgba(255,255,255,0.85)"
  on-ink-wash: "rgba(255,255,255,0.18)"
  pressed-soft: "#CBD2EE"
  pressed-danger: "#F6D0C6"
typography:
  display:
    fontFamily: "IBMPlexSansArabic_700Bold"
    fontSize: "30px"
    fontWeight: 700
    lineHeight: "38px"
  title:
    fontFamily: "IBMPlexSansArabic_700Bold"
    fontSize: "22px"
    fontWeight: 700
    lineHeight: "30px"
  heading:
    fontFamily: "IBMPlexSansArabic_600SemiBold"
    fontSize: "17px"
    fontWeight: 600
    lineHeight: "24px"
  body:
    fontFamily: "IBMPlexSansArabic_400Regular"
    fontSize: "16px"
    fontWeight: 400
    lineHeight: "23px"
  body-strong:
    fontFamily: "IBMPlexSansArabic_600SemiBold"
    fontSize: "16px"
    fontWeight: 600
    lineHeight: "23px"
  caption:
    fontFamily: "IBMPlexSansArabic_400Regular"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: "20px"
  label:
    fontFamily: "IBMPlexSansArabic_600SemiBold"
    fontSize: "12px"
    fontWeight: 600
    lineHeight: "16px"
    letterSpacing: "0.4px"
  numeral:
    fontFamily: "IBMPlexSansArabic_700Bold"
    fontWeight: 700
    fontFeature: "tabular-nums"
rounded:
  sm: "4px"
  md: "8px"
  lg: "10px"
  xl: "14px"
  pill: "999px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "24px"
  xxl: "32px"
components:
  button-primary:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.on-ink}"
    typography: "{typography.body-strong}"
    rounded: "{rounded.md}"
    padding: "0 16px"
    height: "52px"
  button-primary-pressed:
    backgroundColor: "{colors.ink-dark}"
  button-secondary:
    backgroundColor: "{colors.ink-soft}"
    textColor: "{colors.ink-deep}"
    rounded: "{rounded.md}"
    height: "52px"
  button-ghost:
    backgroundColor: "transparent"
    textColor: "{colors.ink}"
    rounded: "{rounded.md}"
  button-danger:
    backgroundColor: "{colors.vermilion-wash}"
    textColor: "{colors.vermilion}"
    rounded: "{rounded.md}"
    height: "52px"
  button-danger-pressed:
    backgroundColor: "{colors.pressed-danger}"
  button-destructive:
    backgroundColor: "{colors.vermilion}"
    textColor: "{colors.on-ink}"
    rounded: "{rounded.md}"
    height: "52px"
  button-destructive-pressed:
    backgroundColor: "{colors.vermilion-deep}"
  segmented-selected:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.on-ink}"
    rounded: "{rounded.sm}"
  text-field:
    backgroundColor: "{colors.slip}"
    textColor: "{colors.text}"
    typography: "{typography.body}"
    rounded: "{rounded.md}"
    height: "52px"
    padding: "0 14px"
  card:
    backgroundColor: "{colors.slip}"
    rounded: "{rounded.lg}"
    padding: "16px"
  badge-success:
    backgroundColor: "{colors.verdigris-wash}"
    textColor: "{colors.verdigris}"
    typography: "{typography.label}"
    rounded: "{rounded.sm}"
    padding: "3px 8px"
  stamp-paid:
    backgroundColor: "{colors.verdigris-wash}"
    textColor: "{colors.verdigris}"
    rounded: "{rounded.sm}"
  stamp-unpaid:
    backgroundColor: "{colors.vermilion-wash}"
    textColor: "{colors.vermilion}"
    rounded: "{rounded.sm}"
  door-plate-ink:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.plate-text}"
    rounded: "{rounded.md}"
    height: "48px"
    width: "64px"
  door-plate-paper:
    backgroundColor: "{colors.slip}"
    textColor: "{colors.ink-deep}"
    rounded: "{rounded.md}"
  sheet:
    backgroundColor: "{colors.slip}"
    rounded: "{rounded.xl}"
    padding: "8px 16px 16px"
---

# Design System: Baytak

Status: recorded from source only. No simulator, emulator or screenshots were available. Finish review dispositions were `recapture` (round 1) and `fix` (round 2, applied); device captures are still owed. Everything below is what the code defines, not what was seen rendering.

## Overview

**Creative North Star: "The Building's Receipt Book"**

Baytak records rent and building charges between a resident and the person who runs the building. The world it borrows is the paper receipt book: carbon-copy ink blue for the shell, cool paper for the ground, white slips for content, and rubber stamps for state. Payment status is the one authored moment. Layout, navigation and controls stay platform-standard (bottom tabs, native stack, bottom sheets) so the world lends type, palette, density and the stamp, and does not reinvent how an app is operated.

The system is one shared look for iOS and Android (`adaptive`). The whole app (Login, Account, Wallet, admin lists, Collection, Manage and the rest) now sits in this world, not only Home and Collect. Density is calm and operational: 16px screen rhythm, 52px fields and buttons, small crisp corners (slips and plates, not pills). Tokens live as TypeScript constants in `theme.ts`; screens re-skin by changing values there.

**Key Characteristics:**
- Ink blue carries the shell, primary buttons and door plates; text is tinted from the ink, never neutral gray.
- Two stamp inks do the state jobs: verdigris is Paid, vermilion is Unpaid. State is always word plus icon, never colour alone.
- One family (IBM Plex Sans Arabic), Latin and Arabic, with fixed-width digits for money and numbers.
- Flat slips with a faint shadow; depth otherwise from paper-on-paper tone.
- Light mode only.

## Colors

A cool carbon-blue paper palette: one ink, one paper family, two stamp inks, one decorative brass.

### Primary
- **Carbon Ink** (`colors.ink`): the shell (Home header band, Collect header), primary buttons, active tab, focus border, door plates, selected chips. Companions: **Pressed Ink** (`ink-dark`) for pressed primary, **Deep Ink** (`ink-deep`) for text, secondary-button text and plate rims, **Ink Wash** (`ink-soft`) for secondary buttons, avatars and icon tiles, **Ink Tint** (`ink-tint`) for ghost-pressed and selected option rows.

### Secondary (state inks)
- **Verdigris** (`verdigris` on `verdigris-wash`): Paid, success toast, success badge.
- **Vermilion** (`vermilion` on `vermilion-wash`): Unpaid, errors, destructive button, danger toast.
- **Amber** (`amber` on `amber-wash`): Pending, warning toast and badge only.
  Contrast: each ink is documented in source as at least 6:1 on white (that claim holds for white backgrounds only). Ink on its wash is about 5:1, and Vermilion on `pressed-danger` is about 4.35:1; that is the known lower pair. **Deep Vermilion** (`vermilion-deep`) is the pressed fill of the solid destructive button.

### Neutral
- **Carbon Paper** (`paper`): screen ground and navigation header ground.
- **White Slip** (`slip`): cards, fields, tab bar, sheets.
- **Muted Paper** (`paper-muted`): disabled fields, neutral badges, note blocks.
- **Hairline** (`rule`) and **Strong Hairline** (`rule-strong`): dividers, chip and option borders, sheet handle. **Input Edge** (`border-input`): borders of fields, date boxes and unchecked boxes; 3:1 on white and on paper, so a control reads as a control.
- **Text** (`text`), **Muted Text** (`text-muted`), **Faint Text** (`text-faint`): primary, secondary, placeholder and inactive tab.
- **Engraved Brass** (`brass`): the rim line of an ink door plate. Decoration only, never text.
- **Plate Text** (`plate-text`), **On Ink** (`on-ink`), **On Ink Muted** (`on-ink-muted`, 85% white) and **On Ink Wash** (`on-ink-wash`, 18% white): light text and washes over ink; `on-ink` is also the label on selected segments and solid destructive buttons.
- **Pressed Soft** (`pressed-soft`) and **Pressed Danger** (`pressed-danger`): pressed fills of the secondary and danger buttons.

### Named Rules
**The Two Inks Rule.** Verdigris means Paid and vermilion means Unpaid, everywhere, and neither is used for decoration. Amber means Pending.

**The Words Beside Colour Rule.** A state colour never stands alone: it travels with the word (Paid, Unpaid) and an icon.

**The Ink-Tinted Text Rule.** Text colours come from the ink hue (`text`, `text-muted`, `text-faint`); no neutral gray text.

**The Money Is Not a Stamp Rule.** Income and expense (Wallet) are not set in verdigris or vermilion; the Wallet balance sits on a calm white card. The stamp inks stay reserved for state.

## Typography

**Display, Body and Label Font:** IBM Plex Sans Arabic, one family loaded in four weights through expo-font (400, 500, 600, 700). Each weight is its own font file because a custom face has no `fontWeight` on Android. The 500 file is loaded but no `fonts` role uses it.

**Character:** a neutral, engineered sans that reads Latin and Arabic in one voice, with fixed-width digits so amounts and door numbers align in columns.

### Hierarchy
- **Display** (700, 30/38): the big figure moments, such as the Verdict amount on Home and the largest door plate.
- **Title** (700, 22/30): screen-level titles.
- **Heading** (600, 17/24): navigation header titles, sheet titles.
- **Body** (400, 16/23): default reading text; field input text.
- **Body Strong** (600, 16/23): button labels, toast text, emphasized values.
- **Caption** (400, 14/20): helper, error and secondary lines.
- **Label** (600, 12/16, 0.4 tracking): badges, tab labels. Field labels and section headers are sentence-case Body Strong in muted text (no uppercase transform).
- **Numeral** (700, tabular-nums): applied on top of any role for money and door numbers.

### Named Rules
**The Numerals Rule.** Money and apartment numbers use the `numeral` treatment (heaviest weight, tabular digits).

**The Capped Scale Rule.** All text honours system font scaling up to `MAX_FONT_SCALE` = 1.6 and no further, via `maxFontSizeMultiplier` in the shared Text and TextField.

## Layout

Single-column, phone-first. Spacing scale: 4, 8, 12, 16, 24, 32 (`xs` to `xxl`); 16 is the standard screen and card inset, 12 the gap between related items. Touch target floor is `HIT` = 48px on Android and 44px elsewhere; primary buttons and fields are 52px; list rows add 12px to the target (56px minimum). Home is an ink header band (carrying a paper DoorPlate) over paper, then a Verdict sentence per currency, a Slip, and receipt rows. Collect is a wrapped grid of door plates grouped by floor, with a receipt switcher above. Navigation is a bottom tab bar (Home; Collect and Manage for admins; Account) over a native stack. Styles use start/end instead of left/right, and chevrons are direction-aware (`components/ui/direction.ts`). Safe-area insets are applied to sheets and toasts. No tablet or landscape layout has been defined.

## Elevation & Depth

Mostly flat, tonal layering: white slips on cool paper, hairline borders (1px or 1.5px) on fields, chips and options. Two shadows exist, both tinted with deep ink.

### Shadow Vocabulary
- **Slip** (`shadow.sm`; iOS offset 0/2, radius 5, opacity 0.08; Android elevation 2): cards.
- **Lifted** (`shadow.md`; iOS offset 0/6, radius 12, opacity 0.16; Android elevation 6): toasts only.
- **Backdrop** (`overlay`, deep ink at 50%): behind sheets.

### Named Rules
**The Slip Rule.** Content sits on white slips with at most the small shadow; only transient layers (toast) take the lifted one.

## Shapes

Small, crisp corners: slips and plates, not pills. Radii: 4 (stamps, badges, plate rims), 8 (buttons, fields, door plates, chips), 10 (cards, toasts), 14 (sheet top corners), 999 (avatars are circles by size/2). Outlines are 1.5px for fields, chips, badges, stamps and plates; 1px for hairlines. Stamps are set at a slight angle (-3 degrees). Door plates carry an inset engraved rim 3px from the edge.

## Components

### Buttons
Five variants in `Button`: primary (ink fill, white label), secondary (ink-soft fill, deep-ink label), ghost (transparent, ink label), danger (vermilion-wash fill, vermilion label), destructive (solid vermilion fill, white label, deep-vermilion pressed; used by ConfirmDialog for destructive confirms). Radius 8, 1px border in the fill colour, 16px horizontal padding, 52px tall (`HIT` for `small`). Pressed state swaps to a darker fill; disabled is 0.45 opacity; loading swaps the label for a spinner and sets busy state. Optional leading Ionicons icon (20px, 18px small). Primary gives a light haptic on press.

### Fields
`TextField`: white box, 1.5px border, radius 8, 52px minimum, 14px inset. Border is `border-input` at rest, ink on focus, vermilion on error. Label above in sentence-case body-strong muted text, helper or error in caption below. The leading icon uses an end margin. Disabled fields use muted paper with muted text. Multiline minimum 96px.

### Cards, Badges, Avatars
`Card` is a white slip, radius 10, 16px padding (or `flush`), small shadow; pressable cards dip to 0.85 opacity and 0.99 scale. `Badge` is a small square stamp-style tag (no pill): tone-matched wash, 1.5px outline in the tone ink, radius 4, 8px by 3px padding, label role. `SectionHeader` is sentence-case body-strong muted text. `Avatar` is a circle of ink-soft with deep-ink initials. `Divider` is a hairline with a start inset.

### Sheet and Confirm
`Sheet` is a modal bottom sheet: white, 14px top corners, a 40x4 handle inside a `HIT`-high target, optional heading, dimmed backdrop. It rises from the bottom edge (280ms in, 180ms out) and is dismissed by dragging the handle down (80px or a fast flick), tapping outside, tapping the handle, or the system back button. Content scrolls inside it, it lifts above the keyboard on iOS, it hosts its own toast layer (a Modal covers the root one), keeps its last content while animating out, and with Reduce Motion it appears and disappears with no animation. `ConfirmDialog` is a Sheet with a secondary Cancel and a primary (or solid destructive) Confirm, side by side.

### Toast
`ToastHost` shows one toast at a time from the top, safe-area aware: solid state-ink fill (info uses `text`), white text, leading icon, radius 10, lifted shadow, slides in over 220ms and out over 180ms. Visible 3.6s; with an action (Undo) 6s, and the action is an outlined `HIT`-high button. The message is announced to screen readers (and shown twice as long when one is on). More than one `ToastHost` can be mounted (app root plus each open Sheet).

### Navigation
Bottom tabs: white bar with hairline top border, 12px semibold labels, outline icons when inactive and filled when active, ink active tint, faint-text inactive tint. Stack headers sit on paper with ink tint and heading-role titles; Collect uses an ink header with white title.

### Stamp (signature)
`Stamp` renders Paid or Unpaid as a bordered, angled wash with icon and uppercase bold word (13px, 12px small, 1px tracking); Paid uses a double-check icon, Unpaid an empty circle. When a state turns Paid after first render and `settle` is set, it fades in over 120ms and springs from 1.45x scale to 1 (friction 6, tension 220). The motion is skipped when Reduce Motion is enabled, and the label is exposed as plain text for screen readers. Motion is otherwise minimal: no other authored animation exists.

### Door Plate (signature)
`DoorPlate` sets an apartment number as an enamel plate. `ink` tone: ink fill, plate-text numerals, brass rim. `paper` tone: white fill, ink outline, deep-ink numerals, strong-hairline rim, for settled or quiet states. Sizes: sm 44x34 (17pt), md 64x48 (24pt), lg 76x58 (30pt) are `minWidth`/`minHeight`, so plates grow with text; numerals shrink to fit on one line. In Collection, tapping an unpaid plate stamps it paid (the plate settles, an Undo toast stays 6s); long-press, or tapping a paid plate, opens the details sheet. Apartments, Join requests and Select apartments lists also use DoorPlates; in Apartments each row is a Pressable with a separate Call tile.

### Other controls
`Segmented` has a muted-paper track with a solid ink selected segment and light text (`HIT` high, radius 6). `DateField` is a field-styled box (iOS opens a Sheet, Android the native dialog). `ListRow` has an ink-soft icon tile and a direction-aware chevron. `InfoBanner`, `States` (empty, loading, error), `Screen`, `Hero` and `FocusedStatusBar` are shared primitives on the same tokens; they were read for this record but not audited in depth.

## Do's and Don'ts

### Do:
- **Do** take every colour, space, radius and type role from `theme.ts`; add a token before adding a literal.
- **Do** show payment state as a `Stamp` (word plus icon) and apartment numbers as a `DoorPlate`.
- **Do** keep targets at least `HIT` (48px Android, 44px elsewhere) and primary controls at 52px.
- **Do** use `border-input` for field and checkbox borders, and start/end (never left/right) in styles.
- **Do** gate any new motion on Reduce Motion, as `Stamp` does.
- **Do** use the `Text` component so the 1.6 font-scale cap applies.
- **Do** use Ionicons through the shared components, with an accessibility label on icon-only controls.

### Don't:
- **Don't** use verdigris or vermilion for anything but Paid/Success and Unpaid/Error.
- **Don't** signal state by colour alone.
- **Don't** use neutral gray text, pill-shaped buttons or badges, or large radii; the world is small crisp corners.
- **Don't** set money direction (income, expense) in the stamp inks.
- **Don't** use brass for text; it is a decorative rim.
- **Don't** invent a custom navigation or control pattern where the platform has one.

## Coverage and known gaps

- Coverage: the whole app has been brought into the world. Login (wordmark, tagline "Building fees, made clear.", admin request as a quiet "Managing a building? Request admin access" link that opens an explained confirm), LoginVerification, AdminPending (Rejected has its own copy and icon), Account (no duplicate "My receipts"), Wallet (calm white balance card), the admin lists, Manage (no longer duplicates Collection) and Collection (the admin's Collect tab).
- Money: `formatAmount` rounds to cents and shows two decimals for USD when the value is not whole ("1,250.50"); `formatMoney` is "<amount> <currency>" everywhere, Wallet included.
- Known open items: forms are white fields inside white Cards, the least world-specific part of the system; Wallet entry still uses a Sheet (review suggested a stack screen); Arabic/RTL is not verified on device, and `ordinalFloor` and string concatenation are English-only; dark mode is not built (light only); the toast sits at the top (a bottom snackbar on Android is not done); Collection headline totals are computed on the device from the receipt total and counts (rounded to cents) rather than taken from the server's collected/expected after an optimistic stamp; the danger-on-`pressed-danger` pair is about 4.35:1.
- Unverified without a device: rendering, contrast in situ, font loading appearance, Arabic shaping, spring and Sheet drag feel. Device captures owed on an iPhone and an Android phone, at default text and 1.6x: Collection plate grid, Home Verdict with USD and LBP, Wallet entry sheet with the keyboard, Login.
- Not canonized: the uppercase stamp word and uppercase Undo label are carried as they ship; they are not a licence for new uppercase labels elsewhere.
- Provenance: no raster assets were produced by this build. Icons come from `@expo/vector-icons` Ionicons; the login background photo and app icons pre-existed.
