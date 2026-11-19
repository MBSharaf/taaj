// Shared Bilingual (AR / EN) & Dark/Light Mode Engine for LabStage
// Zero MutationObserver overhead — executes once on page load and once after dynamic API renders.

const THEME_KEY = 'labstage_theme';
const LANG_KEY = 'labstage_lang';

const AR_LETTER = '\u0621-\u064A';
// The dictionary (about 150 KB) is fetched only for the English interface.
let EXACT_AR_TO_EN = {}, SUBSTRING_AR_TO_EN = [], COUNT_UNITS_AR_TO_EN = {}, COUNT_UNIT_RE = /$^/g, PLACEHOLDER_AR_TO_EN = {};
let SUBSTRING_RES = [];
let dictLoad = null, dictReady = false;
function loadDictionary() {
  dictLoad ??= import('./i18n-en.js?v=2').then((d) => {
    ({ EXACT_AR_TO_EN, SUBSTRING_AR_TO_EN, COUNT_UNITS_AR_TO_EN, COUNT_UNIT_RE, PLACEHOLDER_AR_TO_EN } = d);
    dictReady = true;
    SUBSTRING_RES = SUBSTRING_AR_TO_EN.map(([ar, en]) => [ar, en,
      new RegExp(`(?<![${AR_LETTER}])${ar.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}(?![${AR_LETTER}])`, 'g')]);
  });
  return dictLoad;
}

// English mode: Arabic-Indic digits, separators and ص/م times become Latin digits and AM/PM.
function latinizeNumbers(str) {
  return str
    .replace(/[\u0660-\u0669]/g, (d) => String(d.charCodeAt(0) - 0x0660))
    .replace(/\u066C/g, ',')
    .replace(/\u066B/g, '.')
    .replace(/(\d)\u060C/g, '$1,')
    .replace(/\u200F/g, '')
    .replace(/(\d{1,2}:\d{2}(?::\d{2})?)\s*\u0635/g, '$1 AM')
    .replace(/(\d{1,2}:\d{2}(?::\d{2})?)\s*\u0645(?![\u0600-\u06FF.])/g, '$1 PM');
}

export function getTheme() {
  return localStorage.getItem(THEME_KEY) === 'dark' ? 'dark' : 'light';
}

export function setTheme(theme) {
  const next = theme === 'dark' ? 'dark' : 'light';
  localStorage.setItem(THEME_KEY, next);
  document.documentElement.dataset.theme = next;
  updateToggleButtons();
}

export function getLang() {
  return localStorage.getItem(LANG_KEY) === 'en' ? 'en' : 'ar';
}

export function setLang(lang) {
  const next = lang === 'en' ? 'en' : 'ar';
  if (next === 'en' && !dictReady) { loadDictionary().then(() => setLang(next)); return; }
  localStorage.setItem(LANG_KEY, next);
  document.documentElement.lang = next;
  document.documentElement.dir = next === 'en' ? 'ltr' : 'rtl';
  applyDomTranslations(document.body);
  updateToggleButtons();
}


function translateStringToEn(str) {
  const trimmed = str.trim();
  if (!trimmed) return str;
  if (EXACT_AR_TO_EN[trimmed]) {
    return latinizeNumbers(str.replace(trimmed, EXACT_AR_TO_EN[trimmed]));
  }
  // Whole words only: never swap part of a longer Arabic word (e.g. "الطلب" must not become "الOrder").
  let out = str;
  for (const [ar, en, re] of SUBSTRING_RES) {
    if (!ar.includes(' ') && !/[:()←%]/.test(ar)) continue; // phrases first
    if (out.includes(ar)) out = out.replace(re, en);
  }
  const arabicWords = (out.match(/[\u0621-\u064A]+/g) || []).length;
  if (arabicWords < 3) {
    for (const [ar, en, re] of SUBSTRING_RES) {
      if (ar.includes(' ') || /[:()←%]/.test(ar)) continue;
      if (out.includes(ar)) out = out.replace(re, en);
    }
  }
  out = latinizeNumbers(out);
  return out
    .replace(COUNT_UNIT_RE, (m, n, unit) => n + ' ' + COUNT_UNITS_AR_TO_EN[unit])
    .replace(/\b(\d+) (case|Stage)\b/g, (m, n, unit) => (n === '1' ? m : n + ' ' + (unit === 'case' ? 'cases' : 'stages')));
}

const originalTextMap = new WeakMap();
const originalPlaceholderMap = new WeakMap();
const originalAttrMap = new WeakMap();
let originalTitle = null;

export function applyDomTranslations(root = document.body) {
  if (!root) return;
  const lang = getLang();

  root.querySelectorAll?.('input[placeholder], textarea[placeholder]').forEach((el) => {
    if (!originalPlaceholderMap.has(el)) {
      originalPlaceholderMap.set(el, el.getAttribute('placeholder') || '');
    }
    const orig = originalPlaceholderMap.get(el);
    const target = lang === 'en'
      ? (PLACEHOLDER_AR_TO_EN[orig] || translateStringToEn(orig))
      : orig;
    if (el.getAttribute('placeholder') !== target) {
      el.setAttribute('placeholder', target);
    }
  });

  // Screen-reader labels, tooltips and image text follow the language too.
  root.querySelectorAll?.('[aria-label], [title], img[alt]').forEach((el) => {
    if (el.closest('.pref-toggles')) return;
    let orig = originalAttrMap.get(el);
    if (!orig) { orig = {}; for (const a of ['aria-label', 'title', 'alt']) if (el.hasAttribute(a)) orig[a] = el.getAttribute(a); originalAttrMap.set(el, orig); }
    for (const [a, v] of Object.entries(orig)) {
      if (!v || !/[؀-ۿ]/.test(v)) continue;
      const target = lang === 'en' ? translateStringToEn(v) : v;
      if (el.getAttribute(a) !== target) el.setAttribute(a, target);
    }
  });
  if (root === document.body) {
    if (originalTitle === null) originalTitle = document.title;
    const t = lang === 'en' ? translateStringToEn(originalTitle) : originalTitle;
    if (document.title !== t) document.title = t;
  }

  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
    acceptNode(node) {
      const parent = node.parentElement;
      if (!parent) return NodeFilter.FILTER_REJECT;
      const tag = parent.tagName;
      if (tag === 'SCRIPT' || tag === 'STYLE' || tag === 'NOSCRIPT' || parent.closest('.pref-toggles')) {
        return NodeFilter.FILTER_REJECT;
      }
      return NodeFilter.FILTER_ACCEPT;
    },
  });

  let current;
  while ((current = walker.nextNode())) {
    const val = current.nodeValue || '';
    if (!val.trim()) continue;

    if (!originalTextMap.has(current)) {
      originalTextMap.set(current, val);
    }
    const orig = originalTextMap.get(current);
    const target = lang === 'en' ? translateStringToEn(orig) : orig;
    if (current.nodeValue !== target) {
      current.nodeValue = target;
    }
  }
}

function updateToggleButtons() {
  const theme = getTheme();
  const lang = getLang();
  const themeLabel = theme === 'dark'
    ? (lang === 'en' ? '☀️ Light' : '☀️ فاتح')
    : (lang === 'en' ? '🌙 Dark' : '🌙 داكن');
  const langLabel = lang === 'en' ? '🌐 عربي' : '🌐 EN';

  document.querySelectorAll('[data-pref-theme]').forEach((btn) => {
    const pressed = String(theme === 'dark');
    if (btn.getAttribute('aria-pressed') !== pressed) btn.setAttribute('aria-pressed', pressed);
    if (btn.textContent !== themeLabel) btn.textContent = themeLabel;
  });
  document.querySelectorAll('[data-pref-lang]').forEach((btn) => {
    const pressed = String(lang === 'en');
    if (btn.getAttribute('aria-pressed') !== pressed) btn.setAttribute('aria-pressed', pressed);
    if (btn.textContent !== langLabel) btn.textContent = langLabel;
  });
}

export function mountPreferenceToggles() {
  const targets = [
    document.querySelector('.site-header .nav-actions'),
    document.querySelector('.dash-topbar > div:last-child'),
    document.querySelector('.auth-top'),
    // phones: the dashboard top bar has no room, so the side menu carries them too
    document.querySelector('.dash-v2 .dash-sidebar > div:first-child'),
  ].filter(Boolean);

  targets.forEach((container) => {
    if (container.querySelector('.pref-toggles')) return;
    const wrap = document.createElement('div');
    wrap.className = 'pref-toggles';
    wrap.innerHTML = `
      <button type="button" class="pref-btn" data-pref-theme title="تبديل المظهر الفاتح / الداكن · Toggle Dark Mode">🌙 داكن</button>
      <button type="button" class="pref-btn" data-pref-lang title="تبديل اللغة العربية / الإنجليزية · Switch Language">🌐 EN</button>
    `;
    wrap.querySelector('[data-pref-theme]').addEventListener('click', () => {
      setTheme(getTheme() === 'dark' ? 'light' : 'dark');
    });
    wrap.querySelector('[data-pref-lang]').addEventListener('click', () => {
      setLang(getLang() === 'en' ? 'ar' : 'en');
    });
    const brand = container.querySelector(':scope > .brand');
    if (brand) brand.after(wrap); else container.prepend(wrap);
  });

  updateToggleButtons();
}

export function initPreferences() {
  document.documentElement.dataset.theme = getTheme();
  const lang = getLang();
  document.documentElement.lang = lang;
  document.documentElement.dir = lang === 'en' ? 'ltr' : 'rtl';

  mountPreferenceToggles();
  if (lang === 'en') {
    applyDomTranslations(document.body);
  }
}

// Static front-end demo (GitHub Pages has no API): reveal the notice written in each page.
if (/\.github\.io$/.test(window.location.hostname)) {
  document.querySelectorAll('.demo-notice, .demo-accounts').forEach((el) => { el.hidden = false; });
}

// English pages wait for the dictionary before anything renders (Arabic pages never load it).
if (getLang() === 'en') await loadDictionary();
