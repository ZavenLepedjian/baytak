/*
  Baytak website script. No libraries, no scroll listeners: everything runs on IntersectionObserver.
  - reveals content as it enters the view (hierarchy: the eye is led down the page)
  - drives the "how it works" story (which step is current)
  - highlights the current section in the table of contents of document pages
  Without script, or with reduced motion, every element is simply visible.
*/
document.documentElement.classList.add("js");

document.addEventListener("DOMContentLoaded", function () {
  var reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  // Stagger values live in data attributes (a style attribute would break the page's content security policy).
  document.querySelectorAll("[data-i]").forEach(function (el) { el.style.setProperty("--i", el.getAttribute("data-i")); });
  document.querySelectorAll("[data-k]").forEach(function (el) { el.style.setProperty("--k", el.getAttribute("data-k")); });

  // Header gets its hairline once the page has moved.
  var header = document.querySelector(".site-header");
  if (header && "IntersectionObserver" in window) {
    var sentinel = document.createElement("div");
    sentinel.className = "sentinel";
    document.body.prepend(sentinel);
    new IntersectionObserver(function (entries) {
      header.classList.toggle("scrolled", !entries[0].isIntersecting);
    }).observe(sentinel);
  }

  var targets = document.querySelectorAll("[data-reveal], [data-lines], .slip");
  if (reduce || !("IntersectionObserver" in window)) {
    targets.forEach(function (el) { el.classList.add("in"); });
  } else {
    var reveal = new IntersectionObserver(function (entries, observer) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) return;
        entry.target.classList.add("in");
        observer.unobserve(entry.target);
      });
    }, { threshold: 0.18, rootMargin: "0px 0px -6% 0px" });
    targets.forEach(function (el) { reveal.observe(el); });
  }

  // The story: a sticky stage whose state follows the step that crosses the middle of the screen.
  var story = document.querySelector(".story");
  if (story && "IntersectionObserver" in window) {
    var narrow = window.matchMedia("(max-width: 960px)");
    var setFinal = function () { if (narrow.matches) story.setAttribute("data-step", "4"); };
    setFinal();
    narrow.addEventListener("change", function () { story.setAttribute("data-step", narrow.matches ? "4" : "1"); });
    var steps = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (entry.isIntersecting && !narrow.matches) story.setAttribute("data-step", entry.target.getAttribute("data-step"));
      });
    }, { rootMargin: "-50% 0px -50% 0px", threshold: 0 });
    story.querySelectorAll(".story-marker").forEach(function (marker) { steps.observe(marker); });
  }

  // Table of contents on document pages.
  var links = document.querySelectorAll(".toc a");
  if (links.length && "IntersectionObserver" in window) {
    var byId = {};
    links.forEach(function (a) { byId[a.getAttribute("href").slice(1)] = a; });
    var spy = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) return;
        links.forEach(function (a) { a.classList.remove("on"); });
        var link = byId[entry.target.id];
        if (link) link.classList.add("on");
      });
    }, { rootMargin: "-15% 0px -75% 0px", threshold: 0 });
    document.querySelectorAll(".prose h2[id]").forEach(function (h) { spy.observe(h); });
  }
});
