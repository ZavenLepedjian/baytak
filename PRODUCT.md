# Product

<!-- impeccable:product-schema 1 -->

## Platform

adaptive

The product ships on iOS and Android from one Expo / React Native codebase. The visual identity is deliberately **one shared Baytak look on both**; only system behaviours (back gesture, pickers, safe areas, status bar, notification permissions) follow each OS. Recorded as `adaptive` so both platform references apply to those behaviours; it does not mean the design language is split per OS.

## Users

- **Residents**: people who own or rent an apartment in a Lebanese residential building and owe recurring building fees (maintenance, electricity, security, elevator, etc.). They open the app to see what they owe, whether it is paid, and to manage their own profile and apartment registration. Wide range of ages and tech comfort; typical phones are mid-range.
- **Building administrators**: one per building. They register the building, define the receipt format, approve or reject join requests, issue receipts to apartments, and mark them paid. They act from their phone, often while in the building or on the move.
- **Operator (internal)**: approves administrator requests out of band (there is no back-office UI yet). Not an app user.

Language today: **English only**. Arabic with right-to-left layout is a planned later phase, so layouts must not be built in ways that make RTL expensive. The backend defaults to Lebanon (+961); amounts are in **USD and LBP**.

## Product Purpose

Make building fees clear: an administrator issues a receipt, every resident sees what they owe and whether it is settled, and nobody has to chase a WhatsApp thread or a paper list. Success is a resident who can answer "do I owe anything this month, how much, in which currency?" in a few seconds, and an administrator who can issue a receipt to the whole building in under a minute.

## Positioning

Building fees as a shared, trustworthy record between administrator and residents: receipts are immutable once issued, totals are computed on the server, payment status is set by the administrator, and residents are notified by push. A personal income/expense wallet is included as a **side tool**, not the core promise.

## Operating Context

- Sign-in is passwordless: phone number, then a 6-digit SMS code. Administrator access is requested from the login screen and stays pending until the operator approves it.
- Lifecycle: administrator sets up building (name, address, floors, map location) and receipt format (currency + fields) → resident finds the building (nearest first, using device location) and requests an apartment → administrator approves → administrator issues receipts → resident gets a push notification → administrator marks receipts paid.
- Receipts carry amount fields plus free-text notes, a date, and a currency (USD or LBP). They cannot be edited or deleted after issue.
- Connectivity can be poor; the app must stay usable and recoverable (timeouts, retries, safe re-send) rather than failing silently.

## Capabilities and Constraints

- Features in scope: phone login, profile with emergency contact, building and apartment registration, join-request approval, receipt format, issuing receipts (to everyone or selected apartments), per-apartment payment status, receipt history, push notifications, personal wallet (USD + LBP ledger with add/edit/delete), account deletion.
- Roles: `User` and `Admin`; an administrator account is usable as admin only once `Approved`.
- Technical: Expo SDK 57, React Native 0.86 (new architecture), React Navigation (bottom tabs + native stack), light mode only (`userInterfaceStyle: light`), backend in `backend/` (.NET 10 Azure Functions). Maps use `react-native-maps`; an Android Google Maps API key is not configured yet.
- Terminology to keep: "Building", "Apartment", "Receipt", "Join request", "Receipt format". Public API routes keep the legacy "Appartment" spelling; user-facing copy uses "Apartment".
- Undecided: whether the wallet stays as a long-term feature; Arabic/RTL timeline; dark mode; a back-office UI for the operator.

## Brand Commitments

- Product name: **Baytak** (Arabic *bayt*, "house/home"; "your home").
- No binding visual constraints have been stated. The incumbent app uses a teal-led identity; that is evidence in the code, not a confirmed brand rule.

## Evidence on Hand

- Real product content: the full flow above is implemented and covered by backend tests (62) and a live smoke test.
- Assets in the repo: app icon / adaptive icon / splash (`assets/`), `assets/images/login-background.jpg`.
- Absent, so future work must not fabricate: customer testimonials, resident or building counts, press, pricing, a logo file beyond the app icon, store screenshots, and any legal/terms copy.

## Product Principles

1. **Money is never ambiguous.** Every amount shows its currency; status (Pending / Paid) is always visible and never conveyed by color alone.
2. **One app, two roles, no leakage.** Residents see a calm, short app; administrator tools live in their own place and never clutter the resident experience.
3. **Irreversible means deliberate.** Issuing a receipt, deleting an account, rejecting a request: always confirmed, always explained in plain language.
4. **Built for unreliable networks and everyday phones.** Fast to first content, recoverable errors, safe retries, no dead ends.
5. **Plain words for everyone.** No jargon or finance-speak; copy a resident of any age can act on without help.

## Accessibility & Inclusion

No formal standard has been set. Current behaviour to preserve: touch targets of at least 44 points, text scaling allowed up to about 130%, status never by color alone. A broad age range of residents and a future Arabic/RTL release are the inclusion drivers.
