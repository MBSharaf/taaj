// Homepage hero showcase: rotates the restorations automatically.
// The pieces are real-time 3D (hero3d.js, three.js). If WebGL is unavailable the SVG pieces in index.html stay.
// Markup and every visible string live in index.html; this file only moves state classes:
// is-active (centre), is-next (bottom-end corner), is-after (behind it), is-prev (leaving to the top),
// unclassed = parked out of sight. The frame colour follows the active piece.
const root = document.querySelector('.showcase');

if (root) {
  const frame = root.querySelector('.sc-frame');
  const items = [...root.querySelectorAll('.sc-item')];
  const slides = [...root.querySelectorAll('.sc-slide')];
  const tags = [...root.querySelectorAll('.sc-tags p')];
  const dots = [...root.querySelectorAll('.sc-dots span')];
  const toggle = root.querySelector('[data-sc="toggle"]');
  const n = items.length;
  const CYCLE = 3800; // ms each piece stays (includes the ~1.1s move)
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  let index = 0;
  let elapsed = 0;
  let last = performance.now();
  let userPaused = reduced; // reduced motion: start paused, arrows still work
  let offscreen = false;
  let focusInside = false;
  let holding = false; // dragging the 3D piece
  let three = null;

  const STATES = ['is-active', 'is-next', 'is-after', 'is-prev'];

  function render() {
    items.forEach((el, k) => {
      const d = (k - index + n) % n;
      const state = d === 0 ? 'is-active' : d === 1 ? 'is-next' : d === 2 ? 'is-after' : d === n - 1 ? 'is-prev' : '';
      STATES.forEach((s) => el.classList.toggle(s, s === state));
    });
    slides.forEach((el, k) => {
      el.classList.toggle('is-active', k === index);
      el.toggleAttribute('aria-hidden', k !== index);
    });
    tags.forEach((el, k) => el.classList.toggle('is-active', k === index));
    dots.forEach((el, k) => {
      el.classList.toggle('is-active', k === index);
      el.classList.toggle('is-done', k < index);
      el.style.setProperty('--p', 0);
    });
    frame.style.setProperty('--sc-bg', items[index].dataset.bg);
    three?.go(index);
  }

  function go(to) {
    index = (to + n) % n;
    elapsed = 0;
    render();
  }

  function setPaused(value) {
    userPaused = value;
    toggle.setAttribute('aria-pressed', String(value));
  }

  function tick(now) {
    const dt = Math.min(now - last, 100);
    last = now;
    const running = !userPaused && !offscreen && !focusInside && !holding && !document.hidden;
    if (running) {
      elapsed += dt;
      if (elapsed >= CYCLE) go(index + 1);
    }
    dots[index]?.style.setProperty('--p', Math.min(1, elapsed / CYCLE).toFixed(3));
    requestAnimationFrame(tick);
  }

  root.addEventListener('click', (e) => {
    const btn = e.target.closest('[data-sc]');
    if (!btn) return;
    const action = btn.dataset.sc;
    if (action === 'next') go(index + 1);
    else if (action === 'prev') go(index - 1);
    else if (action === 'toggle') setPaused(!userPaused);
  });

  // Hold still while someone is typing in the search or using the controls.
  root.addEventListener('focusin', (e) => { focusInside = !e.target.closest('.sc-controls'); });
  root.addEventListener('focusout', () => { focusInside = false; });

  if ('IntersectionObserver' in window) {
    new IntersectionObserver(([entry]) => { offscreen = !entry.isIntersecting; }, { threshold: 0.15 }).observe(root);
  }

  setPaused(userPaused);
  render();
  requestAnimationFrame(tick);

  // Real 3D pieces. Loaded after first paint; the SVG pieces show until the canvas is ready.
  const host = root.querySelector('.sc-3d');
  if (host) {
    import('./hero3d.js?v=5')
      .then(({ mountHero3D }) => {
        three = mountHero3D(frame, {
          host,
          stage: root.querySelector('.sc-stage'),
          grab: host.querySelector('.sc-grab'),
          count: n,
          index,
          reducedMotion: reduced,
          onHold: (v) => { holding = v; },
        });
        root.classList.add('has-3d');
      })
      .catch((err) => { console.warn('3D hero unavailable, keeping the SVG pieces.', err); });
  }
}
