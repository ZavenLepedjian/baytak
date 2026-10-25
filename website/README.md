# Baytak website

The public site: landing page, privacy policy, terms of use, support, account deletion and the ChatGPT page.
It is plain HTML, one stylesheet and one small script, so it can sit in blob storage as a static website.

Live at https://baytakapi8803.z16.web.core.windows.net/ (the static website of the API's storage account, `$web` container).

| Path | Page |
| --- | --- |
| `/` | Landing page |
| `/privacy/` | Privacy policy |
| `/terms/` | Terms of use |
| `/support/` | Support and common questions |
| `/delete-account/` | How to delete an account (also the account-deletion URL for store listings) |
| `/chatgpt/` | What Baytak does in ChatGPT |

## Look

- **Colour comes from the logo.** The bowl of the "b" (`#00847c`) is the primary and the stem (`#009898`) its lighter partner.
  Neutrals are tinted with the same teal. Paid is the brand teal and Unpaid the one warm ink. Light and dark follow the visitor's system.
- **Type** is the app's own IBM Plex Sans Arabic, self-hosted. The Arabic word "بيتك" ("your home") is a four-letter subset of the same font.
- **The logo** is `assets/adaptive-icon.png`, copied byte for byte. Icons are Phosphor (MIT). Photos are crops of `assets/images/login-background.jpg`.
- **Motion** is scroll-driven and restrained: reveals, the sticky "how it works" story, the receipt stamp. It is all off with reduced motion,
  and nothing needs a library (IntersectionObserver only, no scroll listeners).

## Publish

```powershell
./website/deploy.ps1
```

It turns static website hosting on and uploads `website/public`. Run it again after any change. It needs a signed-in Azure CLI.

## Edit

- Pages are generated: change `tools/build.py` (header, footer and every page's text live there), run `python website/tools/build.py`,
  then publish. Styles are in `public/assets/site.css` and behaviour in `public/assets/main.js`.
- The contact email, operator name and effective date are constants at the top of `tools/build.py`.
- `tools/prepare-assets.py` rebuilds the fonts, photo crops and icon sprite from the repo's own files (see its header for the command).

## Still open

- Store badges: no App Store or Play Store link exists yet, so the landing page has none. Add them to the hero when the listings are live.
- The photo is the one the app already ships. Confirm its licence before using it widely.
- The in-app screens and the Instagram media are still ink blue (`theme.ts`, `marketing/`); the site now follows the logo.
