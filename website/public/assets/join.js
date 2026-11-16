/*
  The page a building's WhatsApp invite opens (/join/?code=ABCD2345).
  It shows the code and a button that opens the Baytak app with it. Nothing is sent anywhere: the code stays in this page.
  Written with textContent only, so whatever is in the address can never become markup.
*/
(function () {
  "use strict";
  // Same letters as the app and the server: no 0, O, 1, I or L.
  var ALPHABET = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

  var raw = (new URLSearchParams(window.location.search).get("code") || "").replace(/[^a-zA-Z0-9]/g, "").toUpperCase();
  var valid = raw.length === 8 && raw.split("").every(function (c) { return ALPHABET.indexOf(c) !== -1; });

  document.querySelectorAll("[data-with-code]").forEach(function (el) { el.hidden = !valid; });
  document.querySelectorAll("[data-without-code]").forEach(function (el) { el.hidden = valid; });
  if (!valid) return;

  var shown = raw.slice(0, 4) + "-" + raw.slice(4);
  document.querySelectorAll("[data-code]").forEach(function (el) { el.textContent = shown; });
  document.querySelectorAll("[data-open-app]").forEach(function (el) { el.setAttribute("href", "baytak://join?code=" + raw); });
})();
