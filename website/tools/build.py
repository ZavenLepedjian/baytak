"""
Builds the HTML pages of the website into website/public (header, footer and head are shared, so they are written once here).
Run:  python website/tools/build.py        (standard library only)
Then publish with website/deploy.ps1.
"""
import pathlib
import re

OUT = pathlib.Path(__file__).resolve().parents[1] / "public"
EMAIL = "zavenlepedjian@outlook.com"
MAILTO = f"mailto:{EMAIL}?subject=Baytak%20support"
EFFECTIVE = "17 October 2026"
OPERATOR = "Zaven Lepedjian"


def icon(name, cls="i"):
    return f'<svg class="{cls}" aria-hidden="true" focusable="false"><use href="/assets/icons.svg#{name}"/></svg>'


NAV = [
    ("/#how", "How it works", "how"),
    ("/#who", "Who it is for", "who"),
    ("/chatgpt/", "ChatGPT", "chatgpt"),
    ("/support/", "Support", "support"),
]

CSP = ("default-src 'none'; img-src 'self'; style-src 'self'; font-src 'self'; script-src 'self'; "
       "connect-src 'none'; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'")


def header(active):
    def links():
        out = []
        for href, label, key in NAV:
            cur = ' aria-current="page"' if key == active else ""
            out.append(f'<li><a href="{href}"{cur}>{label}</a></li>')
        return "\n".join(out)

    return f"""<a class="skip" href="#main">Skip to content</a>
<header class="site-header">
  <div class="wrap bar">
    <a class="brand" href="/"><img src="/assets/img/baytak-logo.png" width="40" height="40" alt="">Baytak</a>
    <nav class="nav" aria-label="Main"><ul>
{links()}
    </ul></nav>
    <details class="menu">
      <summary>{icon("list")}Menu</summary>
      <ul>
{links()}
      </ul>
    </details>
  </div>
</header>"""


def footer():
    return f"""<footer class="site-footer">
  <div class="wrap">
    <div class="foot-grid">
      <div class="foot-brand">
        <a class="brand" href="/"><img src="/assets/img/baytak-logo.png" width="40" height="40" alt="">Baytak</a>
        <p>Building fees, made clear. A service for residential buildings in Lebanon.</p>
      </div>
      <nav class="foot-col" aria-label="Product">
        <h2>Product</h2>
        <a href="/#how">How it works</a>
        <a href="/#who">Who it is for</a>
        <a href="/chatgpt/">Baytak in ChatGPT</a>
      </nav>
      <nav class="foot-col" aria-label="Legal">
        <h2>Legal</h2>
        <a href="/privacy/">Privacy policy</a>
        <a href="/terms/">Terms of use</a>
        <a href="/delete-account/">Delete your account</a>
      </nav>
      <nav class="foot-col" aria-label="Help">
        <h2>Help</h2>
        <a href="/support/">Support</a>
        <a href="{MAILTO}">{EMAIL}</a>
      </nav>
    </div>
    <p class="foot-base">&copy; 2026 {OPERATOR}. Baytak is operated by {OPERATOR}.</p>
  </div>
</footer>"""


def page(path, title, desc, body, active=None):
    html = f"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta http-equiv="Content-Security-Policy" content="{CSP}">
<title>{title}</title>
<meta name="description" content="{desc}">
<meta name="color-scheme" content="light dark">
<meta name="theme-color" content="#f4f8f7" media="(prefers-color-scheme: light)">
<meta name="theme-color" content="#041312" media="(prefers-color-scheme: dark)">
<meta property="og:site_name" content="Baytak">
<meta property="og:type" content="website">
<meta property="og:title" content="{title}">
<meta property="og:description" content="{desc}">
<link rel="icon" href="/assets/img/favicon.png">
<link rel="preload" href="/assets/fonts/plex-700.woff2" as="font" type="font/woff2" crossorigin>
<link rel="stylesheet" href="/assets/site.css">
<script src="/assets/main.js"></script>
</head>
<body>
{header(active)}
<main id="main">
{body}
</main>
{footer()}
</body>
</html>
"""
    target = OUT / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(html, encoding="utf-8", newline="\n")


def faq(items):
    out = []
    for q, a in items:
        out.append(f"""<details>
  <summary>{q}{icon("caret-down")}</summary>
  <div class="answer">{a}</div>
</details>""")
    return "\n".join(out)


def toc(items):
    links = "\n".join(f'<a href="#{i}">{t}</a>' for i, t in items)
    return f'<nav class="toc" aria-label="On this page"><h2>On this page</h2>\n{links}\n</nav>'


def doc_page(path, title, desc, h1, lead, meta, toc_items, prose, active=None):
    body = f"""<div class="doc-hero">
  <div class="wrap">
    <h1>{h1}</h1>
    <p class="lead">{lead}</p>
    {f'<p class="meta">{meta}</p>' if meta else ''}
  </div>
</div>
<div class="wrap">
  <div class="doc-grid">
    {toc(toc_items)}
    <div class="prose">
{prose}
    </div>
  </div>
</div>"""
    page(path, title, desc, body, active)


# ------------------------------------------------------------------ home

HERO_ALT = ("A pedestrian street in downtown Beirut lined with sandstone apartment buildings, "
            "with a clock tower at the far end.")
CLOCK_ALT = "A clock tower at the end of a street in downtown Beirut, framed by sandstone buildings and a green tree."
ARCADE_ALT = "Sandstone arcades with hanging lanterns on a pedestrian street in downtown Beirut, with two people walking."

FEES = ["Maintenance", "Electricity", "Security", "Elevator", "Cleaning", "Water", "Generator", "Repairs"]
marquee_items = "".join(f"<span>{f}</span>" for f in FEES) * 2

# the story stage: four floors of four apartments
plates = []
k = 0
for floor in (4, 3, 2, 1):
    cells = []
    for letter in "ABCD":
        name = f"{floor}{letter}"
        cls = "plate"
        if name == "3B":
            cls += " plate-pending"
        if name in ("2C", "1D"):
            cls += " plate-late"
        cells.append(f'<span class="{cls}" data-k="{k}">{name}{icon("check")}</span>')
        k += 1
    plates.append(f'<div class="floor"><span class="floor-label"><span class="fl-long">Floor </span>{floor}</span>{"".join(cells)}</div>')
floors = "\n".join(plates)

home = f"""<section class="hero">
  <div class="wrap hero-grid">
    <div class="hero-copy">
      <h1 class="display" data-lines>
        <span class="line"><span data-i="0">Building fees,</span></span>
        <span class="line"><span data-i="1">made clear.</span></span>
      </h1>
      <div class="hero-low" data-reveal data-i="4">
        <p class="lead">Your administrator issues a receipt. Every resident sees what they owe, in USD or LBP, and whether it is paid.</p>
        <div class="hero-cta"><a class="btn btn-primary" href="#how">See how it works{icon("arrow-down")}</a></div>
      </div>
    </div>
    <div class="hero-visual">
      <figure class="hero-photo">
        <img src="/assets/img/beirut-1000.jpg" srcset="/assets/img/beirut-640.jpg 640w, /assets/img/beirut-1000.jpg 1000w"
             sizes="(min-width: 961px) 36vw, 100vw" width="1000" height="1250" alt="{HERO_ALT}" fetchpriority="high">
      </figure>
      <aside class="slip" aria-label="Sample receipt">
        <div class="slip-top"><span>Receipt</span><span>Apartment 3B</span></div>
        <dl>
          <div><dt>Maintenance</dt><dd>25.00 USD</dd></div>
          <div><dt>Electricity</dt><dd>40.00 USD</dd></div>
          <div class="total"><dt>Total</dt><dd>65.00 USD</dd></div>
        </dl>
        <span class="stamp stamp-paid">{icon("checks")}Paid</span>
        <p class="slip-note">Sample receipt</p>
      </aside>
    </div>
  </div>
</section>

<div class="marquee">
  <p class="sr">One receipt can cover maintenance, electricity, security, the elevator, cleaning, water, the generator and repairs.</p>
  <div class="marquee-track" aria-hidden="true">{marquee_items}</div>
</div>

<section class="story" id="how" data-step="1">
  <div class="story-pin">
    <div class="wrap story-grid">
      <div class="story-copy">
        <h2>From one receipt to everyone paid.</h2>
        <ol class="steps">
          <li class="step step-1"><div><h3>Register the building</h3><p>The administrator adds the building, its floors and a receipt format: which fields, in which currency.</p></div></li>
          <li class="step step-2"><div><h3>Approve residents</h3><p>Residents find the building and ask to join their apartment. Each request is approved by the administrator.</p></div></li>
          <li class="step step-3"><div><h3>Issue the receipt</h3><p>One receipt goes to the whole building or to chosen apartments. Residents are notified straight away.</p></div></li>
          <li class="step step-4"><div><h3>Mark it paid</h3><p>Neighbours pay the administrator outside Baytak. The administrator stamps each apartment Paid, and everyone can see it.</p></div></li>
        </ol>
      </div>
      <div class="stage" aria-hidden="true">
        <div class="stage-board">
          <div class="floors">
{floors}
          </div>
          <div class="legend">
            <span class="stamp stamp-paid">{icon("checks")}Paid</span>
            <span class="stamp stamp-unpaid">{icon("circle")}Unpaid</span>
            <span>Sample building</span>
          </div>
        </div>
        <div class="float float-join">
          <strong>Apartment 3B wants to join</strong>
          <p>Floor 3</p>
          <div class="float-actions"><span class="no">Reject</span><span class="yes">{icon("checks")}Approve</span></div>
        </div>
        <div class="float float-issue">
          <strong>Receipt issued</strong>
          <div class="amount">65.00 USD</div>
          <p>Sent to every apartment. Residents are notified.</p>
        </div>
        <div class="float float-paid">
          <strong>Two apartments left to collect</strong>
          <p>Tap an unpaid plate to stamp it paid. Undo is one tap away.</p>
        </div>
      </div>
    </div>
  </div>
  <div class="story-track" aria-hidden="true">
    <div class="story-marker" data-step="1"></div>
    <div class="story-marker" data-step="2"></div>
    <div class="story-marker" data-step="3"></div>
    <div class="story-marker" data-step="4"></div>
  </div>
</section>

<section class="section" id="features">
  <div class="wrap">
    <div class="feat-head">
      <h2 data-reveal>Made for how buildings really work.</h2>
      <p class="sub" data-reveal data-i="1">Two currencies, a phone number, and records nobody can quietly change.</p>
    </div>
    <div class="bento">
      <figure class="cell cell-photo" data-reveal>
        <img src="/assets/img/beirut-clock-1400.jpg" srcset="/assets/img/beirut-clock-800.jpg 800w, /assets/img/beirut-clock-1400.jpg 1400w"
             sizes="(min-width: 961px) 58vw, 100vw" width="1400" height="1141" alt="{CLOCK_ALT}" loading="lazy">
        <figcaption>
          <h3>Built for Lebanese buildings</h3>
          <p>Receipts in USD and LBP, a phone number to sign in, and a notification when a new receipt arrives.</p>
        </figcaption>
      </figure>
      <article class="cell cell-money" data-reveal data-i="1">
        <div class="currencies" aria-hidden="true"><span>USD</span><span>LBP</span></div>
        <div>
          <h3>Two currencies, kept apart</h3>
          <p>Every amount shows its own currency. Baytak never adds them together.</p>
        </div>
      </article>
      <article class="cell cell-phone" data-reveal data-i="2">
        <div aria-hidden="true">
          <div class="field"><small>+961</small><span>70 123 456</span></div>
          <div class="code"><span>4</span><span>8</span><span>2</span><span>9</span><span>1</span><span>7</span></div>
        </div>
        <div>
          <h3>Sign in with your phone</h3>
          <p>A phone number and a 6-digit code. No password to forget.</p>
        </div>
      </article>
      <article class="cell cell-final" data-reveal>
        {icon("lock-simple")}
        <div>
          <h3>Receipts are final</h3>
          <p>Once issued, a receipt is never edited or deleted.</p>
        </div>
      </article>
      <article class="cell cell-push" data-reveal data-i="1">
        <div>
          <h3>Residents know right away</h3>
          <p>A notification arrives the moment the administrator issues a receipt.</p>
        </div>
        <div class="push" aria-hidden="true">
          <img src="/assets/img/baytak-logo.png" width="44" height="44" alt="">
          <div>
            <strong>Baytak <span>now</span></strong>
            <p>New receipt for your building: 65.00 USD. Tap to see the lines.</p>
          </div>
        </div>
      </article>
    </div>
    <p class="bento-note">Amounts, codes and notifications shown are samples.</p>
  </div>
</section>

<section class="section roles" id="who">
  <div class="wrap">
    <div class="roles-head">
      <h2 data-reveal>One app, two roles.</h2>
      <p class="sub" data-reveal data-i="1">Residents get a calm, short app. Administrators get the tools to run the building, in their own place.</p>
    </div>
    <div class="tabs" data-reveal data-i="2">
      <input type="radio" name="role" id="r-res" checked>
      <input type="radio" name="role" id="r-adm">
      <div class="tablist"><label for="r-res">Residents</label><label for="r-adm">Administrators</label></div>

      <div class="pane pane-res">
        <div class="pane-copy">
          <h3>Know what you owe, and whether it is settled.</h3>
          <ul class="ticks">
            <li>{icon("check-circle")}<span>Your balance in USD and LBP, kept apart</span></li>
            <li>{icon("check-circle")}<span>Every receipt, marked Paid or Unpaid in words</span></li>
            <li>{icon("check-circle")}<span>A notification when a new receipt arrives</span></li>
            <li>{icon("check-circle")}<span>Sign in with your phone number and a code</span></li>
          </ul>
        </div>
        <div class="pane-visual" aria-hidden="true">
          <div class="statement">
            <div class="owe">You owe</div>
            <div class="amount">40.00<small>USD</small></div>
            <ul>
              <li><span>October building fees</span><span class="stamp stamp-unpaid">{icon("circle")}Unpaid</span></li>
              <li><span>September building fees</span><span class="stamp stamp-paid">{icon("checks")}Paid</span></li>
            </ul>
            <p class="sample">Sample statement</p>
          </div>
        </div>
      </div>

      <div class="pane pane-adm">
        <div class="pane-copy">
          <h3>Run the building's fees from your phone.</h3>
          <ul class="ticks">
            <li>{icon("check-circle")}<span>Register your building, its floors and a receipt format</span></li>
            <li>{icon("check-circle")}<span>Approve or reject residents who ask to join</span></li>
            <li>{icon("check-circle")}<span>Issue receipts to everyone, or to chosen apartments, in USD, LBP or both</span></li>
            <li>{icon("check-circle")}<span>See who has paid, apartment by apartment</span></li>
            <li>{icon("check-circle")}<span>Mark an apartment paid with one tap, and undo it if you slip</span></li>
          </ul>
          <p class="pane-note">Administrator access is reviewed and approved by Baytak before it works.</p>
        </div>
        <div class="pane-visual" aria-hidden="true">
          <div class="statement">
            <div class="headline">5 of 8 apartments have paid</div>
            <div class="mini-board">
              <span class="plate plate-paid">1A{icon("check")}</span><span class="plate plate-paid">1B{icon("check")}</span><span class="plate plate-unpaid">1C</span><span class="plate plate-paid">1D{icon("check")}</span>
              <span class="plate plate-paid">2A{icon("check")}</span><span class="plate plate-unpaid">2B</span><span class="plate plate-unpaid">2C</span><span class="plate plate-paid">2D{icon("check")}</span>
            </div>
            <p class="sample">Sample building</p>
          </div>
        </div>
      </div>
    </div>
  </div>
</section>

<section class="section" id="trust">
  <div class="wrap principles-grid">
    <div class="principles-lead">
      <h2 data-reveal>Built to be trusted.</h2>
      <p class="sub" data-reveal data-i="1">Fees are money between neighbours. Baytak keeps the record plain, and the same for everyone.</p>
    </div>
    <div class="principles-list">
      <div class="principle" data-reveal><span class="tile">{icon("coins")}</span><div><h3>Every amount shows its currency</h3><p>USD and LBP are never added together, and status is always a word, never only a colour.</p></div></div>
      <div class="principle" data-reveal><span class="tile">{icon("lock-simple")}</span><div><h3>Receipts are final</h3><p>Once issued, a receipt cannot be edited or deleted. Totals are calculated on the server.</p></div></div>
      <div class="principle" data-reveal><span class="tile">{icon("wifi-high")}</span><div><h3>Made for everyday phones</h3><p>Built for mid-range phones and patchy connections, with safe retries when a request fails.</p></div></div>
      <div class="principle" data-reveal><span class="tile">{icon("text-aa")}</span><div><h3>Plain words</h3><p>No finance jargon. Text that a resident of any age can act on without help.</p></div></div>
    </div>
  </div>
</section>

<section class="section gpt" id="chatgpt">
  <div class="wrap gpt-grid">
    <div class="gpt-copy">
      <h2 data-reveal>Ask in ChatGPT.</h2>
      <p class="sub" data-reveal data-i="1">Connect your Baytak account, sign in with your phone number, and ask about your building in plain words.</p>
      <a class="text-link" href="/chatgpt/" data-reveal data-i="2">How Baytak works in ChatGPT{icon("arrow-right")}</a>
    </div>
    <div class="quotes">
      <blockquote data-reveal>&ldquo;Do I owe anything for my building this month?&rdquo;<span class="who">For residents</span></blockquote>
      <blockquote data-reveal data-i="1">&ldquo;Who hasn&rsquo;t paid this month&rsquo;s building receipt yet?&rdquo;<span class="who">For administrators</span></blockquote>
      <blockquote data-reveal data-i="2">&ldquo;Show my last receipts and which ones are paid.&rdquo;<span class="who">For residents</span></blockquote>
    </div>
  </div>
</section>

<section class="section" id="faq">
  <div class="wrap faq-grid">
    <div class="faq-head">
      <h2 data-reveal>Questions people ask.</h2>
      <p data-reveal data-i="1">Something else? <a href="/support/">Support</a> is one click away.</p>
    </div>
    <div class="faq" data-reveal data-i="1">
{faq([
    ("Does Baytak take payments?", "<p>No. Baytak records receipts and who has paid. Residents pay their administrator outside the app, and the administrator marks the apartment paid.</p>"),
    ("Can a receipt be changed after it is issued?", "<p>No. Receipts cannot be edited or deleted once issued. If one is wrong, ask your administrator how they want to correct it.</p>"),
    ("Which currencies does Baytak use?", "<p>USD and LBP. Each amount shows its own currency, and the two are never added together.</p>"),
    ("How do I sign in?", "<p>Enter your phone number and the 6-digit code that Baytak sends you by SMS. There is no password.</p>"),
    ("Who can see my phone number?", "<p>Your building administrator sees your name, phone number, apartment, emergency contact and payment status. The <a href=\"/privacy/\">privacy policy</a> lists everything we collect and who receives it.</p>"),
    ("Is Baytak available in Arabic?", "<p>Not yet. The app is in English today. Arabic with right-to-left layout is planned.</p>"),
    ("How do I delete my account?", "<p>In the app, open Account and choose Delete account. <a href=\"/delete-account/\">Read what gets deleted</a>.</p>"),
])}
    </div>
  </div>
</section>

<section class="closing">
  <div class="wrap closing-grid">
    <div class="closing-copy">
      <h2 data-reveal>Make your building&rsquo;s fees clear.</h2>
      <p class="sub" data-reveal data-i="1">Baytak is Arabic for &ldquo;your home&rdquo;.</p>
      <div class="closing-cta" data-reveal data-i="2"><a class="btn btn-light" href="{MAILTO}">{icon("envelope-simple")}Email support</a></div>
    </div>
    <figure class="closing-photo" data-reveal data-i="1">
      <img src="/assets/img/beirut-arcade-1100.jpg" srcset="/assets/img/beirut-arcade-700.jpg 700w, /assets/img/beirut-arcade-1100.jpg 1100w"
           sizes="(min-width: 961px) 30vw, 100vw" width="1100" height="1229" alt="{ARCADE_ALT}" loading="lazy">
    </figure>
  </div>
  <div class="wordmark" aria-hidden="true" lang="ar">&#1576;&#1610;&#1578;&#1603;</div>
</section>"""

page("index.html", "Baytak | Building fees, made clear",
     "Baytak lets a building administrator issue receipts and every resident see what they owe, in USD or LBP, and whether it is paid.",
     home)

# ------------------------------------------------------------------ privacy

privacy = f"""<ul class="glance">
  <li>{icon("check-circle")}<span><strong>Your data runs Baytak.</strong> It is used for the purposes listed on this page.</span></li>
  <li>{icon("shield-check")}<span><strong>No card details, ID numbers or passwords.</strong> You sign in with a phone number and a code.</span></li>
  <li>{icon("trash")}<span><strong>You can delete your account</strong> in the app, and your personal data goes with it.</span></li>
  <li>{icon("chat-circle-text")}<span><strong>ChatGPT never receives phone numbers</strong> or emergency contacts, even if you connect it.</span></li>
</ul>

<h2 id="who-we-are">Who we are</h2>
<p>Baytak is a service for residential buildings in Lebanon, operated by {OPERATOR}. It lets a building's administrator issue receipts for building fees and lets residents see what they owe. This policy explains what we collect, why, who receives it, how long we keep it, and what you control. You can reach us at <a href="{MAILTO}">{EMAIL}</a>.</p>

<h2 id="collect">What we collect</h2>
<dl class="facts">
  <div><dt>Account</dt><dd>Your phone number, which is how you sign in, and your role (resident or building administrator).</dd></div>
  <div><dt>Profile (optional)</dt><dd>Your first and last name, and an emergency contact's name and phone number.</dd></div>
  <div><dt>Your home</dt><dd>Your building, apartment number, floor and a short apartment description.</dd></div>
  <div><dt>Receipts</dt><dd>Amounts, currency (USD or LBP), dates, notes written by your administrator, and whether each receipt is paid.</dd></div>
  <div><dt>Location</dt><dd>If you allow location access, your phone's location when you sign in. We keep your most recent location to show the buildings nearest to you. Administrators also set their building's location on a map.</dd></div>
  <div><dt>Notifications</dt><dd>A push notification token for your device, removed when you sign out.</dd></div>
  <div><dt>Personal wallet (optional)</dt><dd>The income and expense entries you add yourself.</dd></div>
  <div><dt>Technical logs</dt><dd>Requests to our servers, with phone numbers masked.</dd></div>
</dl>
<p>We do not collect payment card details, government identifiers or passwords. We do not store your sign-in codes: a code is sent to your phone and checked by our verification provider.</p>

<h2 id="why">Why we use it</h2>
<p>To sign you in, show you your receipts and what you owe, let your administrator issue receipts and record payments, notify you about receipts and join requests, show nearby buildings, keep the service secure and fix problems. If that ever changes, we will update this policy first and tell you in the app.</p>

<h2 id="receive">Who receives it</h2>
<ul>
  <li><strong>Your building administrator</strong> sees your name, phone number, apartment, emergency contact and payment status.</li>
  <li><strong>Service providers</strong> process data for us: Microsoft Azure (hosting, database and logs, in the EU, North Europe region), Twilio (sending and checking sign-in codes for your phone number) and Expo (delivering push notifications).</li>
  <li><strong>ChatGPT (OpenAI)</strong>, only if you connect Baytak to it. Baytak then sends ChatGPT the results of the Baytak tools used in your conversation: your apartment, receipts and payment status. For administrators, that also includes residents' names, apartment numbers and payment status in their building. Baytak never sends phone numbers or emergency contacts to ChatGPT, and does not receive your ChatGPT conversations, only the requests ChatGPT makes to Baytak's tools. OpenAI handles that data under its own privacy policy. See <a href="/chatgpt/">Baytak in ChatGPT</a>.</li>
  <li><strong>Authorities</strong>, when the law requires it.</li>
</ul>

<h2 id="keep">How long we keep it</h2>
<ul>
  <li><strong>Sign-in code records:</strong> deleted after 2 days.</li>
  <li><strong>Account, apartment and receipt data:</strong> until you delete your account or your administrator removes your apartment.</li>
  <li><strong>Technical logs:</strong> 90 days.</li>
  <li><strong>ChatGPT connection:</strong> access lasts 1 hour at a time, and a connection ends after at most 30 days, or when you sign out of Baytak.</li>
</ul>

<h2 id="choices">Your choices</h2>
<ul>
  <li>Edit your profile in the app.</li>
  <li>Delete your account in the app: open Account, then choose Delete account. This removes your personal data. <a href="/delete-account/">Details</a>.</li>
  <li>Turn off location or notifications for Baytak in your phone's settings at any time.</li>
  <li>Disconnect Baytak in ChatGPT's settings, or sign out of the Baytak app to end every session, ChatGPT's included.</li>
  <li>Ask us for a copy of your data, or for a correction, at <a href="{MAILTO}">{EMAIL}</a>.</li>
</ul>

<h2 id="security">Security</h2>
<p>Traffic between the app and our servers is encrypted with HTTPS. The app keeps your sign-in in your phone's secure storage, and signing out ends it.</p>

<h2 id="children">Children</h2>
<p>Baytak is not directed at children under 13.</p>

<h2 id="changes">Changes to this policy</h2>
<p>We will post changes here with a new effective date, and tell you in the app when they are significant.</p>

<h2 id="contact">Contact</h2>
<p>Questions about this policy or your data: <a href="{MAILTO}">{EMAIL}</a>.</p>"""

doc_page("privacy/index.html", "Privacy policy | Baytak",
         "What Baytak collects, why, who receives it, how long it is kept, and what you control.",
         "Privacy policy",
         "What Baytak collects, why we collect it, who receives it, and what you can do about it.",
         f"Effective {EFFECTIVE}",
         [("who-we-are", "Who we are"), ("collect", "What we collect"), ("why", "Why we use it"), ("receive", "Who receives it"),
          ("keep", "How long we keep it"), ("choices", "Your choices"), ("security", "Security"), ("children", "Children"),
          ("changes", "Changes"), ("contact", "Contact")],
         privacy)

# ------------------------------------------------------------------ terms

terms = f"""<p>By using Baytak, in the app or through ChatGPT, you agree to these terms. Baytak is operated by {OPERATOR} (&ldquo;we&rdquo;). Contact: <a href="{MAILTO}">{EMAIL}</a>.</p>

<h2 id="what">What Baytak does</h2>
<p>Baytak keeps a record of a residential building's fees: administrators issue receipts and mark them paid, and residents see what they owe. Baytak does not collect, hold or transfer money. Payments happen between residents and their building administrator, outside Baytak.</p>

<h2 id="account">Your account</h2>
<ul>
  <li>You sign in with a phone number you own and a code sent to it. Keep your phone secure. Actions taken from your account are your responsibility.</li>
  <li>Give accurate information, including your apartment when you ask to join a building.</li>
  <li>Administrator access is granted only after we review the request.</li>
</ul>

<h2 id="administrators">Building administrators</h2>
<ul>
  <li>You are responsible for the receipts you issue and the payment statuses you record. Receipts cannot be edited or deleted once issued.</li>
  <li>Use residents' information only to manage your building's fees and to contact residents about them.</li>
  <li>Send reminders only about real, unpaid receipts.</li>
</ul>

<h2 id="chatgpt">Using Baytak in ChatGPT</h2>
<p>Connecting Baytak to ChatGPT is optional. ChatGPT's answers can be wrong: the Baytak app is the record of what you owe and what was issued. Review every preview before you confirm an action such as issuing a receipt. Your use of ChatGPT is also governed by OpenAI's terms.</p>

<h2 id="acceptable-use">Acceptable use</h2>
<p>Do not try to see data that is not yours, misuse sign-in codes, overload or probe the service, copy it with automated tools, or use it to harass anyone.</p>

<h2 id="availability">Availability and changes</h2>
<p>We work to keep Baytak running, but we cannot promise it will always be available or free of errors. We may change or stop features. We will post changes to these terms with a new effective date.</p>

<h2 id="ending">Ending your use</h2>
<p>You can delete your account in the app at any time. We may suspend accounts that break these terms.</p>

<h2 id="liability">Liability</h2>
<p>Baytak is provided &ldquo;as is&rdquo;. To the extent the law allows, we are not liable for indirect losses, or for disputes between residents and administrators about fees or payments.</p>

<h2 id="law">Governing law</h2>
<p>These terms are governed by the laws of Lebanon, except where your local law gives you other rights.</p>"""

doc_page("terms/index.html", "Terms of use | Baytak",
         "The terms for using Baytak, in the app or through ChatGPT.",
         "Terms of use",
         "Short, plain terms that match how Baytak works: it records fees, it moves no money, and receipts are final once issued.",
         f"Effective {EFFECTIVE}",
         [("what", "What Baytak does"), ("account", "Your account"), ("administrators", "Building administrators"),
          ("chatgpt", "Using Baytak in ChatGPT"), ("acceptable-use", "Acceptable use"), ("availability", "Availability"),
          ("ending", "Ending your use"), ("liability", "Liability"), ("law", "Governing law")],
         terms)

# ------------------------------------------------------------------ support

support_faq = faq([
    ("My sign-in code did not arrive", "<p>Check that you entered your number with its country code, for example +961. Codes can take a minute to arrive, and you can ask for a new one after a short wait. If it still does not arrive, write to us with your number's country code and what you saw.</p>"),
    ("I cannot find my building", "<p>Buildings are listed nearest first, using your phone's location, so allow location access for Baytak. If your building is still missing, its administrator has to register it first.</p>"),
    ("My request to join is still pending", "<p>Your building administrator approves each request. Ask them to open Baytak and approve it.</p>"),
    ("I asked for administrator access", "<p>Baytak reviews every administrator request. It stays pending until we approve it. Write to us if you have been waiting a while.</p>"),
    ("A receipt amount looks wrong", "<p>Receipts cannot be changed once issued, so ask your administrator how they want to correct it. Baytak only shows what the administrator issued.</p>"),
    ("How do I delete my account?", "<p>Open Account in the app and choose Delete account. <a href=\"/delete-account/\">Read what gets deleted</a>.</p>"),
    ("How do I disconnect ChatGPT?", "<p>Disconnect Baytak in ChatGPT's settings, or sign out of the Baytak app to end every session. <a href=\"/chatgpt/\">More about Baytak in ChatGPT</a>.</p>"),
    ("I am an administrator and want to leave", "<p>An administrator whose building still has residents or receipts must hand it over first. Write to us and we will help with that.</p>"),
])

support = f"""<div class="doc-hero">
  <div class="wrap">
    <h1>Support</h1>
    <p class="lead">Tell us your role (resident or administrator), your building, and what happened.</p>
  </div>
</div>
<div class="wrap support-body">
  <div class="contact-panel">
    <div><strong>{EMAIL}</strong><p>Never include a sign-in code in your message.</p></div>
    <a class="btn btn-light" href="{MAILTO}">{icon("envelope-simple")}Email support</a>
  </div>
  <div class="support-faq">
    <h2>Common questions</h2>
    <div class="faq">
{support_faq}
    </div>
  </div>
  <div class="page-links">
    <a class="btn btn-ghost" href="/privacy/">Privacy policy</a>
    <a class="btn btn-ghost" href="/terms/">Terms of use</a>
  </div>
</div>"""
page("support/index.html", "Support | Baytak",
     "Get help with Baytak: sign-in codes, joining a building, receipts, deleting your account and ChatGPT.",
     support, "support")

# ------------------------------------------------------------------ delete account

delete = f"""<h2 id="in-the-app">Delete your account in the app</h2>
<ol>
  <li><span>Open Baytak and go to <strong>Account</strong>.</span></li>
  <li><span>Choose <strong>Delete account</strong>.</span></li>
  <li><span>Confirm. The deletion happens immediately and cannot be undone.</span></li>
</ol>

<h2 id="what-is-deleted">What gets deleted</h2>
<ul>
  <li>Your profile, including your name and emergency contact.</li>
  <li>Your apartment, and the receipts issued to it.</li>
  <li>Your personal wallet entries.</li>
  <li>Your sign-in code records.</li>
  <li>If you are a building administrator: your building and its receipt format.</li>
</ul>
<p>Any ChatGPT connection you made stops working, because it relies on your account.</p>

<h2 id="what-we-keep">What we keep</h2>
<p>Technical logs are kept for 90 days, with phone numbers masked. Nothing else tied to you is kept after deletion.</p>

<h2 id="administrators">If you are an administrator</h2>
<p>A building that still has residents or receipts cannot be deleted with its administrator's account, because the residents' records belong to the building. Write to <a href="{MAILTO}">{EMAIL}</a> and we will help you hand the building over first.</p>

<h2 id="without-the-app">If you cannot open the app</h2>
<p>Write to <a href="{MAILTO}">{EMAIL}</a> with the phone number on your account. We will ask you to confirm that the number is yours before we delete anything.</p>"""

doc_page("delete-account/index.html", "Delete your account | Baytak",
         "How to delete your Baytak account and personal data, and what is removed.",
         "Delete your account",
         "You can delete your Baytak account yourself, at any time, from the app.",
         None,
         [("in-the-app", "In the app"), ("what-is-deleted", "What gets deleted"), ("what-we-keep", "What we keep"),
          ("administrators", "Administrators"), ("without-the-app", "Without the app")],
         delete)

# ------------------------------------------------------------------ chatgpt

chatgpt = f"""<h2 id="what-you-can-do">What you can do</h2>
<p>Baytak in ChatGPT shows only your own apartment, or your own building if you are its administrator. Residents get three actions, and approved building administrators get all of them.</p>
<div class="two-up">
  <article class="panel">
    <h3>Residents</h3>
    <ul class="ticks">
      <li>{icon("check-circle")}<span>What you owe now, per currency, with the unpaid receipts</span></li>
      <li>{icon("check-circle")}<span>Your receipts, paid and unpaid</span></li>
      <li>{icon("check-circle")}<span>Your apartment, building, request status and role</span></li>
    </ul>
  </article>
  <article class="panel panel-dark">
    <h3>Building administrators</h3>
    <h4>Look things up</h4>
    <ul class="ticks">
      <li>{icon("check-circle")}<span>Who has paid a receipt, apartment by apartment</span></li>
      <li>{icon("check-circle")}<span>Residents waiting to join, and approved apartments</span></li>
      <li>{icon("check-circle")}<span>The currency and fields of your receipts</span></li>
    </ul>
    <h4>Make changes</h4>
    <ul class="ticks">
      <li>{icon("check-circle")}<span>Mark apartments paid or unpaid</span></li>
      <li>{icon("check-circle")}<span>Approve or reject a join request</span></li>
      <li>{icon("check-circle")}<span>Send a reminder to apartments that have not paid, once a day per receipt</span></li>
      <li>{icon("check-circle")}<span>Issue receipts in USD, LBP or both, after you review a preview</span></li>
    </ul>
  </article>
</div>

<h2 id="sign-in">Signing in</h2>
<p>When you connect Baytak, ChatGPT opens Baytak's own sign-in page. Enter the phone number you use in the Baytak app and the 6-digit code sent to it. Then you see what ChatGPT will be allowed to do, and you choose Allow or Deny. Your code is typed only on Baytak's page, never in the chat.</p>

<h2 id="confirmation">Changes need your confirmation</h2>
<p>Issuing a receipt shows a preview first: the amounts, the apartments and a note that it cannot be undone. Nothing is issued until you confirm. Receipts, once issued, cannot be edited or deleted, so review every preview.</p>

<h2 id="shared">What ChatGPT receives</h2>
<p>ChatGPT receives the results of the Baytak actions used in your conversation: your apartment, receipts and payment status, and for administrators, residents' names, apartment numbers and payment status in their building. Baytak never sends phone numbers or emergency contacts, and does not receive your ChatGPT conversations. ChatGPT's answers can be wrong, so treat the Baytak app as the record. The <a href="/privacy/">privacy policy</a> has the full detail.</p>

<h2 id="disconnect">Disconnecting</h2>
<p>Disconnect Baytak in ChatGPT's settings at any time, or sign out of the Baytak app to end every session. Access lasts 1 hour at a time, and a connection ends after at most 30 days.</p>

<h2 id="try">Questions to try</h2>
<ul>
  <li>&ldquo;Do I owe anything for my building this month?&rdquo;</li>
  <li>&ldquo;Show my last receipts and which ones are paid.&rdquo;</li>
  <li>For administrators: &ldquo;Who hasn&rsquo;t paid this month&rsquo;s building receipt yet?&rdquo;</li>
</ul>
<p>Need help? See <a href="/support/">Support</a>.</p>"""

doc_page("chatgpt/index.html", "Baytak in ChatGPT | Baytak",
         "What Baytak does in ChatGPT, how sign-in works, what ChatGPT receives, and how to disconnect.",
         "Baytak in ChatGPT",
         "Ask what you owe, see your receipts, or run your building's fees, in plain words.",
         None,
         [("what-you-can-do", "What you can do"), ("sign-in", "Signing in"), ("confirmation", "Confirmation"),
          ("shared", "What ChatGPT receives"), ("disconnect", "Disconnecting"), ("try", "Questions to try")],
         chatgpt, "chatgpt")

# ------------------------------------------------------------------ 404

notfound = f"""<div class="wrap notfound">
  <h1>That page is not here.</h1>
  <p>The link may be old, or mistyped. These pages are here.</p>
  <div class="hero-cta">
    <a class="btn btn-primary" href="/">Go to the home page</a>
    <a class="btn btn-ghost" href="/support/">Support</a>
  </div>
</div>"""
page("404.html", "Page not found | Baytak", "That page is not here.", notfound)

(OUT / "robots.txt").write_text("User-agent: *\nAllow: /\n", encoding="utf-8", newline="\n")

# ------------------------------------------------------------------ checks
bad = []
for f in OUT.rglob("*.html"):
    t = f.read_text(encoding="utf-8")
    for ch in ("—", "–"):
        if ch in t:
            bad.append((f.name, repr(ch)))
    if re.search(r'\sstyle="', t):
        bad.append((f.name, "inline style"))
    if re.search(r"<script(?![^>]*\ssrc=)", t):
        bad.append((f.name, "inline script"))
print("problems:", bad or "none")
