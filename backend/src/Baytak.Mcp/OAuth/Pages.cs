using System.Net;

namespace Baytak.Mcp.OAuth;

/// <summary>
/// The sign-in pages people see when they connect Baytak to ChatGPT (or Claude). Plain HTML forms, no script, in the app's
/// visual world of the website: the logo's teal, cool paper, the app's own logo file. Copy matches the mobile app.
/// </summary>
public static class Pages
{
    public static string Phone(string flow, string clientName, string redirectHost, string? error = null, string? phone = null) => Layout("Sign in", $"""
        <h1>Sign in to Baytak</h1>
        <p class="lede"><b>{E(clientName)}</b> wants to connect to your Baytak account. Enter your phone number and we'll text you a verification code.</p>
        {ErrorBlock(error)}
        <form method="post" action="/oauth/authorize">
          <input type="hidden" name="flow" value="{E(flow)}">
          <input type="hidden" name="step" value="send">
          <label for="phone">Phone number</label>
          <input id="phone" name="phone" type="tel" inputmode="tel" autocomplete="tel" placeholder="e.g. 03 123 456" value="{E(phone ?? "")}" required autofocus>
          <button class="primary" type="submit">Continue</button>
        </form>
        <p class="fine">After you sign in you'll go back to {E(redirectHost)}.</p>
        """);

    public static string Code(string flow, string phone, string? error = null, string? notice = null) => Layout("Enter verification code", $"""
        <h1>Enter verification code</h1>
        <p class="lede">We sent a 6-digit code to <b>{E(phone)}</b>.</p>
        {ErrorBlock(error)}{NoticeBlock(notice)}
        <form method="post" action="/oauth/authorize">
          <input type="hidden" name="flow" value="{E(flow)}">
          <input type="hidden" name="step" value="verify">
          <label for="code">Verification code</label>
          <input id="code" name="code" type="text" inputmode="numeric" autocomplete="one-time-code" pattern="[0-9]*" maxlength="6" required autofocus>
          <button class="primary" type="submit">Verify</button>
        </form>
        <div class="row">
          <form method="post" action="/oauth/authorize">
            <input type="hidden" name="flow" value="{E(flow)}">
            <input type="hidden" name="step" value="resend">
            <button class="ghost" type="submit">Resend code</button>
          </form>
          <form method="post" action="/oauth/authorize">
            <input type="hidden" name="flow" value="{E(flow)}">
            <input type="hidden" name="step" value="restart">
            <button class="ghost" type="submit">Change number</button>
          </form>
        </div>
        """);

    public static string Consent(string flow, string clientName, string redirectHost, string phone, bool isAdmin, bool adminPending) => Layout("Allow access", $"""
        <h1>Allow {E(clientName)} to use Baytak?</h1>
        <p class="lede">Signed in as <b>{E(phone)}</b>. {E(clientName)} ({E(redirectHost)}) will be able to:</p>
        <ul class="can">
          <li>See your apartment, what you owe and your receipts, in USD and LBP</li>
          {(isAdmin ? """
          <li>See your building's collection status and join requests</li>
          <li>Mark receipts paid or unpaid and send payment reminders</li>
          <li>Approve or reject join requests</li>
          <li>Issue receipts, after you confirm each one in the chat. Receipts can't be edited or deleted once issued.</li>
          """ : "")}
        </ul>
        {(adminPending ? """<p class="note">Your administrator access is still pending, so building tools will appear once it's approved.</p>""" : "")}
        <p class="fine">To disconnect later, remove the connection in {E(clientName)}, or sign out of Baytak in the app to end every session.</p>
        <div class="row">
          <form method="post" action="/oauth/authorize">
            <input type="hidden" name="flow" value="{E(flow)}">
            <input type="hidden" name="step" value="deny">
            <button class="secondary" type="submit">Cancel</button>
          </form>
          <form method="post" action="/oauth/authorize">
            <input type="hidden" name="flow" value="{E(flow)}">
            <input type="hidden" name="step" value="allow">
            <button class="primary" type="submit">Allow</button>
          </form>
        </div>
        """);

    public static string Problem(string message) => Layout("Can't sign in", $"""
        <h1>This sign-in link can't be used</h1>
        <p class="lede">{E(message)}</p>
        <p class="fine">Start again from the app you were connecting to Baytak.</p>
        """);

    private static string ErrorBlock(string? error) =>
        string.IsNullOrEmpty(error) ? "" : $"""<p class="error" role="alert"><span aria-hidden="true">!</span>{E(error)}</p>""";

    private static string NoticeBlock(string? notice) =>
        string.IsNullOrEmpty(notice) ? "" : $"""<p class="notice" role="status">{E(notice)}</p>""";

    private static string E(string text) => WebUtility.HtmlEncode(text);

    private static string Layout(string title, string body) => $$"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <meta name="robots" content="noindex">
        <title>{{E(title)}} · Baytak</title>
        <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=IBM+Plex+Sans+Arabic:wght@400;600;700&display=swap">
        <style>
          :root { --ink: #00847C; --ink-dark: #007069; --ink-deep: #05403D; --ink-soft: #E4F3F1; --paper: #F4F8F7; --slip: #FBFDFC;
                  --rule: #D6E4E2; --input: #6F8A87; --text: #0A2321; --muted: #3D5654; --vermilion: #B8321B; --vermilion-wash: #FBE6DF;
                  --amber: #8A5A00; --amber-wash: #FBEFD0; }
          * { box-sizing: border-box; }
          body { margin: 0; min-height: 100vh; background: var(--paper); color: var(--text); display: grid; place-items: start center;
                 font: 16px/23px "IBM Plex Sans Arabic", "Segoe UI", system-ui, sans-serif; padding: 40px 16px; }
          main { width: 100%; max-width: 420px; background: var(--slip); border-radius: 14px; padding: 28px 24px;
                 box-shadow: 0 2px 5px rgba(5,64,61,.10); }
          .brand { display: flex; align-items: center; gap: 10px; font-weight: 700; font-size: 20px; color: var(--ink-deep); margin-bottom: 20px; }
          .brand img { width: 40px; height: 40px; }
          h1 { font-size: 22px; line-height: 30px; margin: 0 0 6px; text-wrap: balance; }
          .lede { color: var(--muted); margin: 0 0 20px; }
          .lede b { color: var(--text); font-weight: 600; }
          label { display: block; font-weight: 600; color: var(--muted); margin-bottom: 6px; }
          input[type=tel], input[type=text] { width: 100%; height: 52px; border: 1.5px solid var(--input); border-radius: 8px; padding: 0 14px;
                 font: inherit; color: var(--text); background: #fff; }
          input:focus { outline: none; border-color: var(--ink); }
          button { font: inherit; font-weight: 600; height: 52px; border-radius: 8px; border: 1px solid transparent; padding: 0 16px; cursor: pointer; width: 100%; }
          button:focus-visible { outline: 2px solid var(--ink); outline-offset: 2px; }
          .primary { background: var(--ink); color: #fff; margin-top: 20px; }
          .primary:hover { background: var(--ink-dark); }
          .secondary { background: var(--ink-soft); color: var(--ink-deep); margin-top: 20px; }
          .ghost { background: transparent; color: var(--ink); height: 44px; margin-top: 8px; }
          .row { display: flex; gap: 12px; }
          .row form { flex: 1; }
          .can { margin: 0 0 16px; padding-left: 20px; }
          .can li { margin-bottom: 6px; }
          .error { display: flex; gap: 8px; align-items: flex-start; color: var(--vermilion); background: var(--vermilion-wash); border-radius: 8px; padding: 10px 12px; margin: 0 0 16px; }
          .error span { font-weight: 700; }
          .notice, .note { background: var(--paper); border-radius: 8px; padding: 10px 12px; margin: 0 0 16px; color: var(--muted); }
          .note { background: var(--amber-wash); color: var(--amber); }
          .fine { font-size: 14px; line-height: 20px; color: var(--muted); margin: 16px 0 0; }
        </style>
        </head>
        <body>
        <main>
          <div class="brand"><img src="/oauth/logo.png" alt="" width="40" height="40">Baytak</div>
          {{body}}
        </main>
        </body>
        </html>
        """;
}
