/* Baytak reels kit: icon sprite, viewer chrome, and one clock that drives every CSS animation plus a
   per-reel update(t) for choreography that is easier to write in script. render.py drives the same API. */
(() => {
  const ICONS = {
    back: '<path d="M15 5l-7 7 7 7"/>',
    fwd: '<path d="M9 5l7 7-7 7"/>',
    down: '<path d="M6 9l6 6 6-6"/>',
    cash: '<rect x="2.5" y="6" width="19" height="12" rx="2"/><circle cx="12" cy="12" r="2.6"/><path d="M6 9.5v5M18 9.5v5"/>',
    people: '<circle cx="9" cy="8.5" r="3"/><path d="M3.5 19c.6-3 2.8-4.6 5.5-4.6s4.9 1.6 5.5 4.6"/><circle cx="16.5" cy="9" r="2.4"/><path d="M15.8 14.4c2.4.1 4.1 1.5 4.7 4"/>',
    plane: '<path d="M21 3L10.5 13.5"/><path d="M21 3l-6.5 18-4-7.5L3 9.5z"/>',
    warn: '<path d="M12 3.8L2.8 19.8h18.4z"/><path d="M12 10v4.4"/><path d="M12 17.3v.1"/>',
    done: '<path d="M1.8 12.8l4.6 4.6L16.2 7.6"/><path d="M10.6 16.2l1.2 1.2L22.2 7.6"/>',
    ring: '<circle cx="12" cy="12" r="7.5"/>',
    bell: '<path d="M6 16.5V11a6 6 0 0 1 12 0v5.5l1.6 2H4.4z"/><path d="M10 20.8a2 2 0 0 0 4 0"/>',
    funnel: '<path d="M3.5 5h17l-6.5 7.5v5.5l-4 2v-7.5z"/>',
    call: '<path d="M5.2 3.8h3.3l1.7 4.3-2.2 1.4a11 11 0 0 0 6.5 6.5l1.4-2.2 4.3 1.7v3.3a1.6 1.6 0 0 1-1.7 1.6C10.6 19.8 4.2 13.4 3.6 5.5a1.6 1.6 0 0 1 1.6-1.7z"/>',
    lock: '<rect x="5" y="10.5" width="14" height="10" rx="2"/><path d="M8.5 10.5v-3a3.5 3.5 0 0 1 7 0v3"/><path d="M12 14.5v2"/>',
    okc: '<circle cx="12" cy="12" r="9"/><path d="M8 12.4l2.7 2.7 5.6-5.8"/>',
    building: '<rect x="5" y="3.5" width="14" height="17" rx="1.2"/><path d="M9 7.5h1.5M13.5 7.5H15M9 11h1.5M13.5 11H15M9 14.5h1.5M13.5 14.5H15M10.5 20.5v-3h3v3"/>',
    receipt: '<path d="M5.5 3h13v18l-2.2-1.4-2.1 1.4-2.2-1.4-2.2 1.4-2.1-1.4-2.2 1.4z"/><path d="M9 8h6M9 11.5h6M9 15h3.5"/>',
    phone: '<rect x="6.5" y="2.5" width="11" height="19" rx="2.4"/><path d="M10.5 18.5h3"/>',
    chat: '<path d="M4 5.5h16v10.5H10l-4.5 3.5V16H4z"/>',
    chatdots: '<path d="M4 5.5h16v10.5H10l-4.5 3.5V16H4z"/><path d="M8.5 11h.01M12 11h.01M15.5 11h.01" stroke-width="2.6"/>',
    pencil: '<path d="M4 20l1-4.5L15.5 5a2.1 2.1 0 0 1 3 3L8 18.5z"/><path d="M13.5 7l3 3"/>',
    trash: '<path d="M4.5 6.5h15M9.5 6.5V4.5h5v2M6.5 6.5l1 13.5h9l1-13.5"/><path d="M10 10.5v6M14 10.5v6"/>',
    plus: '<path d="M12 5v14M5 12h14"/>',
    x: '<path d="M6 6l12 12M18 6L6 18"/>',
    home: '<path d="M3.5 11L12 4l8.5 7"/><path d="M6 9.5V20h12V9.5"/><path d="M10 20v-5.5h4V20"/>',
    stack: '<path d="M7 4.5h10M5.5 8h13"/><rect x="3.5" y="11" width="17" height="9" rx="1.5"/>',
  };
  const sprite = `<svg width="0" height="0" style="position:absolute" aria-hidden="true"><defs>${Object.entries(ICONS)
    .map(([k, v]) => `<symbol id="i-${k}" viewBox="0 0 24 24">${v}</symbol>`).join('')}</defs></svg>`;
  document.body.insertAdjacentHTML('afterbegin', sprite);

  const clamp = (x, a = 0, b = 1) => Math.min(b, Math.max(a, x));
  const Kit = (window.Kit = {
    icon: (name, cls = '') => `<svg class="ico ${cls}"><use href="#i-${name}"/></svg>`,
    clamp,
    /** Progress of t through [a, b], clamped to 0..1. */
    seg: (t, a, b) => clamp((t - a) / (b - a)),
    lerp: (a, b, p) => a + (b - a) * p,
    ease: {
      out: (x) => 1 - Math.pow(1 - x, 3),
      in: (x) => x * x * x,
      inOut: (x) => (x < 0.5 ? 4 * x * x * x : 1 - Math.pow(-2 * x + 2, 3) / 2),
      outBack: (x) => 1 + 2.2 * Math.pow(x - 1, 3) + 1.2 * Math.pow(x - 1, 2),
    },
    fmt: (n) => String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ','),
    statusBar: (time, light) =>
      `<div class="sb${light ? ' light' : ''}"><span>${time}</span><span class="sbi"><svg viewBox="0 0 18 12"><rect x="0" y="8" width="3" height="4" rx=".7"/><rect x="5" y="5.5" width="3" height="6.5" rx=".7"/><rect x="10" y="3" width="3" height="9" rx=".7"/><rect x="15" y="0" width="3" height="12" rx=".7"/></svg><span class="batt"><i></i></span></span></div>`,
  });

  /**
   * opts: { title, duration, update(t), bug: 'dark' | 'light' | false, endcard: true, handle: '@getbaytak' }
   */
  Kit.init = (opts) => {
    const stage = document.getElementById('stage');
    const slides = opts.slides ? [...stage.querySelectorAll('.slide')] : null;
    const D = slides ? slides.length : opts.duration;
    const H = stage.offsetHeight || 1920;
    if (slides) { opts.bug = opts.bug ?? false; opts.endcard = false; }
    if (opts.bug !== false) {
      stage.insertAdjacentHTML('beforeend', `<div class="bug${opts.bug === 'light' ? ' light' : ''}" aria-hidden="true"><span class="mark"></span>Baytak</div>`);
    }
    if (opts.endcard !== false) {
      stage.insertAdjacentHTML('beforeend', `<div class="endcard" aria-hidden="true"><div class="logo"><div class="part stem"></div><div class="part bowl"></div></div>
        <p class="wm">Baytak</p><p class="tag">Building fees, made clear.</p><p class="handle"><bdi>${opts.handle || '@getbaytak'}</bdi></p></div>`);
    }

    // Viewer chrome around the stage
    const main = document.createElement('main');
    main.className = 'viewer';
    main.innerHTML = `<header class="top"><h1>${opts.title}</h1><p>${slides ? `Instagram carousel &middot; 1080 &times; ${H} &middot; ${D} slides` : `Instagram Reel &middot; 1080 &times; 1920 &middot; ${D} seconds`}</p></header>
      <div class="stage-wrap"></div>
      <div class="controls">
        <button type="button" class="play" aria-label="Pause"><svg class="ico" viewBox="0 0 24 24"></svg></button>
        <button type="button" class="restart" aria-label="Play from the start"><svg class="ico" viewBox="0 0 24 24"><path d="M4 12a8 8 0 1 0 2.4-5.7"/><path d="M4 4v4.5h4.5"/></svg></button>
        <input type="range" class="scrub" min="0" max="${D * 1000}" step="10" value="0" aria-label="Position in the reel">
        <span class="clock">0:00</span>
      </div>`;
    stage.parentNode.insertBefore(main, stage);
    const wrap = main.querySelector('.stage-wrap');
    wrap.appendChild(stage);
    const scrub = main.querySelector('.scrub'), clock = main.querySelector('.clock');
    const playBtn = main.querySelector('.play'), playIco = playBtn.querySelector('svg');

    let anims = [], t = 0, playing = false, last = 0, exporting = false;
    const seek = (x) => {
      const ms = x * 1000;
      for (const a of anims) a.currentTime = ms;
      if (slides) slides.forEach((s, i) => s.classList.toggle('on', i === Math.min(D - 1, Math.floor(x))));
      if (opts.update) opts.update(x);
    };
    const mmss = (x) => `${Math.floor(x / 60)}:${String(Math.floor(x % 60)).padStart(2, '0')}`;
    const ui = () => { scrub.value = Math.round(t * 1000); clock.textContent = `${mmss(t)} / ${mmss(D)}`; };
    const goTo = (x) => { t = clamp(x, 0, D - 0.001); seek(t); ui(); };
    const setPlaying = (p) => {
      playing = p;
      playBtn.setAttribute('aria-label', p ? 'Pause' : 'Play');
      playIco.innerHTML = p ? '<path d="M8 5v14M16 5v14" stroke-width="3"/>' : '<path d="M7 4.5v15l12-7.5z" fill="currentColor"/>';
      if (p) { last = performance.now(); requestAnimationFrame(frame); }
    };
    const frame = (now) => {
      if (!playing) return;
      t += Math.min((now - last) / 1000, 0.1);
      last = now;
      if (t >= D) t -= D;
      seek(t); ui();
      requestAnimationFrame(frame);
    };
    const fit = () => {
      let s = 1;
      if (!exporting) {
        const chrome = main.querySelector('.top').offsetHeight + main.querySelector('.controls').offsetHeight + 16 * 2 + 14 * 2;
        const availW = Math.min(document.documentElement.clientWidth - 32, 1080);
        const availH = Math.max(320, window.innerHeight - chrome);
        s = Math.max(0.16, Math.min(availW / 1080, availH / H));
      }
      stage.style.transform = `scale(${s})`;
      wrap.style.width = `${1080 * s}px`;
      wrap.style.height = `${H * s}px`;
      document.documentElement.style.setProperty('--w', `${Math.max(1080 * s, 300)}px`);
    };

    playBtn.addEventListener('click', () => setPlaying(!playing));
    main.querySelector('.restart').addEventListener('click', () => { goTo(0); setPlaying(true); });
    scrub.addEventListener('input', () => goTo(scrub.value / 1000));
    document.addEventListener('keydown', (e) => {
      if (e.target === scrub || e.target.closest('button')) return;
      if (e.key === ' ' || e.key === 'k') { e.preventDefault(); setPlaying(!playing); }
    });
    window.addEventListener('resize', fit);

    window.baytakAd = {
      duration: D,
      height: H,
      slides: slides ? slides.length : 0,
      seek: (x) => { t = x; seek(x); },
      pause: () => setPlaying(false),
      exportMode: () => { exporting = true; setPlaying(false); document.documentElement.classList.add('export'); fit(); },
      ready: null,
    };
    fit();
    seek(0);
    const logo = new Promise((res) => { const img = new Image(); img.onload = img.onerror = res; img.src = '../../assets/adaptive-icon.png'; });
    window.baytakAd.ready = Promise.all([document.fonts.ready, logo]).then(() => {
      anims = stage.getAnimations({ subtree: true });
      anims.forEach((a) => a.pause());
      if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) { goTo(D - 0.5); setPlaying(false); }
      else if (!exporting) { goTo(0); setPlaying(true); }
      fit();
    });
  };
})();
