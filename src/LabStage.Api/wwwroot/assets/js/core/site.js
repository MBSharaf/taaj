// Shared public-site behaviour: header state, mobile nav, scroll reveals, counters.
// Loaded as an ES module on every public page.
import { initPreferences, mountPreferenceToggles, applyDomTranslations, getLang } from './i18n.js?v=8';

const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)');

function initHeader() {
  const header = document.querySelector('.site-header');
  if (!header) return;
  let lastY = window.scrollY;
  let ticking = false;
  const update = () => {
    const y = window.scrollY;
    header.classList.toggle('is-scrolled', y > 8);
    // Hide on scroll down, show on scroll up (never while the mobile menu is open).
    const menuOpen = header.querySelector('.nav-main.is-open');
    header.classList.toggle('is-hidden', !menuOpen && y > 400 && y > lastY + 4);
    if (y < lastY - 4 || y <= 400) header.classList.remove('is-hidden');
    lastY = y;
    ticking = false;
  };
  window.addEventListener('scroll', () => { if (!ticking) { ticking = true; requestAnimationFrame(update); } }, { passive: true });
  update();
}

function initMobileNav() {
  const toggle = document.querySelector('.nav-toggle');
  const nav = document.getElementById(toggle?.getAttribute('aria-controls') ?? '');
  if (!toggle || !nav) return;
  const setOpen = (open) => {
    toggle.setAttribute('aria-expanded', String(open));
    nav.classList.toggle('is-open', open);
    document.body.style.overflow = open ? 'hidden' : '';
  };
  toggle.addEventListener('click', () => setOpen(toggle.getAttribute('aria-expanded') !== 'true'));
  nav.addEventListener('click', (e) => { if (e.target.closest('a')) setOpen(false); });
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && nav.classList.contains('is-open')) { setOpen(false); toggle.focus(); }
  });
  window.matchMedia('(min-width: 901px)').addEventListener('change', (m) => { if (m.matches) setOpen(false); });
}

function initReveal() {
  const items = document.querySelectorAll('[data-reveal]');
  if (!items.length) return;
  // Auto-stagger children of [data-stagger] groups.
  document.querySelectorAll('[data-stagger]').forEach((group) => {
    [...group.querySelectorAll(':scope > [data-reveal]')].forEach((el, i) => el.style.setProperty('--stagger', i));
  });
  if (reduceMotion.matches || !('IntersectionObserver' in window)) {
    items.forEach((el) => el.classList.add('is-visible'));
    return;
  }
  // GSAP path: panels rise out of depth as they enter, in batches, with a slight 3D tilt.
  const { gsap, ScrollTrigger } = window;
  if (gsap && ScrollTrigger) {
    gsap.registerPlugin(ScrollTrigger);
    document.documentElement.classList.add('gsap-reveal');
    gsap.set(items, { autoAlpha: 0, y: 48, rotationX: -10, transformPerspective: 1000, transformOrigin: '50% 100%' });
    ScrollTrigger.batch(items, {
      start: 'top 88%',
      once: true,
      onEnter: (batch) => gsap.to(batch, {
        autoAlpha: 1, y: 0, rotationX: 0, duration: 1.1, ease: 'expo.out', stagger: 0.09,
        onComplete() { batch.forEach((el) => el.classList.add('is-visible')); },
      }),
    });
    return;
  }
  const io = new IntersectionObserver((entries) => {
    for (const entry of entries) {
      if (entry.isIntersecting) { entry.target.classList.add('is-visible'); io.unobserve(entry.target); }
    }
  }, { rootMargin: '0px 0px -10% 0px', threshold: 0.12 });
  items.forEach((el) => io.observe(el));
}

/** Animates [data-count-to] numbers when visible. Only use for real, factual values. */
function initCounters() {
  const els = document.querySelectorAll('[data-count-to]');
  if (!els.length) return;
  const fmt = new Intl.NumberFormat('ar-EG');
  const run = (el) => {
    const target = Number(el.dataset.countTo);
    if (reduceMotion.matches) { el.textContent = fmt.format(target); return; }
    const start = performance.now();
    const dur = 1200;
    const step = (t) => {
      const p = Math.min(1, (t - start) / dur);
      el.textContent = fmt.format(Math.round(target * (1 - Math.pow(1 - p, 3))));
      if (p < 1) requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
  };
  const io = new IntersectionObserver((entries) => {
    entries.forEach((e) => { if (e.isIntersecting) { run(e.target); io.unobserve(e.target); } });
  }, { threshold: 0.6 });
  els.forEach((el) => io.observe(el));
}

function initYear() {
  document.querySelectorAll('[data-year]').forEach((el) => { el.textContent = new Intl.NumberFormat('ar-EG', { useGrouping: false }).format(new Date().getFullYear()); });
}

initHeader();
initMobileNav();
initReveal();
initCounters();
initYear();
initPreferences();

// Authenticated Return-to-Dashboard Navigation on all Public Pages
(async () => {
  try {
    const res = await fetch('/api/v1/auth/me');
    if (!res.ok) return;
    const user = await res.json();
    const dashUrl = user.role === 'Admin'
      ? '/admin-dashboard.html'
      : (user.role === 'LaboratoryOwner' || user.role === 'LaboratoryStaff')
        ? '/lab-dashboard.html'
        : '/doctor-dashboard.html';
    const roleLabel = user.role === 'Admin'
      ? 'مدير المنصة'
      : (user.role === 'LaboratoryOwner' || user.role === 'LaboratoryStaff')
        ? 'لوحة المعمل'
        : 'لوحة الطبيب';

    const navActions = document.querySelector('.site-header .nav-actions');
    if (navActions) {
      navActions.innerHTML = `
        <a class="btn btn-primary btn-sm" href="${dashUrl}" style="gap:6px">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="7" height="7" rx="1.5"/><rect x="14" y="3" width="7" height="7" rx="1.5"/><rect x="14" y="14" width="7" height="7" rx="1.5"/><rect x="3" y="14" width="7" height="7" rx="1.5"/></svg>
          <span>العودة للوحة التحكم (${roleLabel})</span>
        </a>
        ${user.laboratorySlug ? `<a class="btn btn-ghost btn-sm hide-mobile" href="/lab-dashboard.html#storefront">تعديل متجري</a>` : ''}`;
      mountPreferenceToggles();
      if (getLang() === 'en') applyDomTranslations(navActions);
    }

    if (!document.getElementById('floating-dash-return')) {
      const pill = document.createElement('div');
      pill.id = 'floating-dash-return';
      pill.className = 'floating-dash-pill';
      pill.innerHTML = `
        <span class="floating-dash-dot" aria-hidden="true"></span>
        <span>مسجل الدخول كـ <strong>${user.fullName}</strong></span>
        <a class="btn btn-mint btn-sm" href="${dashUrl}">الرجوع للـ Dashboard ←</a>`;
      document.body.appendChild(pill);
      if (getLang() === 'en') applyDomTranslations(pill);
    }
  } catch {
    // Public visitor
  }
})();
