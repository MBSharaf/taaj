// Taaj UI/UX QA matrix — one command:  node tools/qa/ui_matrix.js
// Every page x role x language (ar/en) x theme (light/dark) x width (360, 390, 768, 1024, 1440).
// Checks: horizontal overflow, Arabic left in English mode, text contrast (WCAG AA), tap targets on phones,
// console/JS errors, failed requests, axe-core accessibility (390 + 1440), and the mobile drawer (opens inside the
// screen, scrolls, has a close button, closes with Esc / backdrop / link) plus the phone bottom bar not covering content.
// Output: tools/qa/out/ui-matrix.json, tools/qa/out/ui-matrix.md and screenshots in tools/qa/out/shots/.
// Env: BASE (default http://localhost:5080, dev server with demo data), ONLY=role[,role], PAGES=substr, W=widths,
//      SHOTS=0 to skip screenshots, AXE=0 to skip axe.
const { chromium } = require('playwright-core');
const fs = require('fs');
const path = require('path');
const BASE = process.env.BASE || 'http://localhost:5080';
const OUT = process.env.OUT || path.join(__dirname, 'out');
const WIDTHS = (process.env.W || '360,390,768,1024,1440').split(',').map(Number);
const SHOTS = process.env.SHOTS !== '0';
const AXE = process.env.AXE !== '0';
const CHROME = process.env.CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const ORDER = process.env.ORDER || 'LS-000187';
const TECH = ['qa.tech@labstage.eg', 'Tech@12345'];

const PAGES = {
  public: ['index.html', 'labs', 'labs/cairo-digital-dental-lab', 'how-it-works.html', 'about.html', 'contact.html', 'login.html',
    'register.html', 'forgot-password.html', 'reset-password.html?token=x', 'legal/terms.html', 'legal/privacy.html', 'legal/payments.html', 'legal/disputes.html'],
  doctor: ['doctor-dashboard.html', 'doctor-orders.html', `doctor-order-details.html?orderNumber=${ORDER}`, 'doctor-finance.html', 'doctor-settings.html', 'doctor-account.html', 'payment-test.html'],
  lab: ['lab-dashboard.html', 'lab-orders.html', `lab-order-details.html?orderNumber=${ORDER}`, 'lab-finance.html', 'lab-settings.html', 'lab-account.html', 'lab-profile.html'],
  tech: ['lab-dashboard.html', 'lab-orders.html', `lab-order-details.html?orderNumber=${ORDER}`, 'lab-account.html'],
  admin: ['admin-dashboard.html', 'admin-account.html'],
};
const CREDS = { doctor: ['doctor@labstage.eg', 'Doctor@12345'], lab: ['lab@labstage.eg', 'Lab@12345'], admin: ['admin@labstage.eg', 'Admin@12345'], tech: TECH };

// ---------- in-page probe ----------
function probe({ lang, w }) {
  const W = innerWidth;
  const sel = (e) => {
    let s = e.tagName.toLowerCase();
    if (e.id) return s + '#' + e.id;
    const c = (typeof e.className === 'string' ? e.className : '').trim().split(/\s+/).filter(Boolean).slice(0, 2).join('.');
    return s + (c ? '.' + c : '');
  };
  const shown = (e) => {
    const r = e.getBoundingClientRect(); if (r.width < 1 || r.height < 1) return false;
    for (let p = e; p && p !== document; p = p.parentElement) {
      const s = getComputedStyle(p);
      if (s.display === 'none' || s.visibility === 'hidden' || +s.opacity < 0.05) return false;
      if (p.hidden || p.getAttribute('aria-hidden') === 'true' && p !== e) return false;
    }
    return true;
  };
  const out = { overflow: Math.max(0, document.documentElement.scrollWidth - W), offenders: [], arabic: [], contrast: [], taps: [] };
  if (out.overflow > 1) {
    const clipped = (e) => { for (let p = e.parentElement; p && p !== document.body; p = p.parentElement) { const ox = getComputedStyle(p).overflowX; if (ox === 'auto' || ox === 'scroll' || ox === 'hidden' || ox === 'clip') return true; } return false; };
    const all = [...document.body.querySelectorAll('*')].filter((e) => { const r = e.getBoundingClientRect(); return r.width > 0 && (r.right > W + 1 || r.left < -1) && !clipped(e) && getComputedStyle(e).position !== 'fixed'; });
    out.offenders = all.filter((e) => !all.includes(e.parentElement)).slice(0, 5).map((e) => { const r = e.getBoundingClientRect(); return `${sel(e)} [${Math.round(r.left)}..${Math.round(r.right)}]`; });
  }
  // Text nodes
  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  const seenAr = new Set(); const textEls = new Set();
  for (let n; (n = walker.nextNode());) {
    const t = n.nodeValue.trim(); if (!t) continue;
    const el = n.parentElement; if (!el || /^(SCRIPT|STYLE|NOSCRIPT|TEMPLATE|OPTION)$/.test(el.tagName)) continue;
    if (!shown(el)) continue;
    textEls.add(el);
    if (lang === 'en' && /[\u0621-\u064A]{2,}/.test(t) && !el.closest('[data-user-text],[lang="ar"],.pref-btn,[data-lang-toggle],.user-data')) {
      const k = t.replace(/\s+/g, ' ').slice(0, 90); if (!seenAr.has(k)) { seenAr.add(k); out.arabic.push(`${k}  ⟨${sel(el)}⟩`); }
    }
  }
  // Placeholders / aria-labels / titles in EN
  if (lang === 'en') for (const e of document.querySelectorAll('[placeholder],[aria-label],[title]')) {
    if (e.closest('.pref-btn') || (!shown(e) && !e.matches('[aria-label]'))) continue;
    for (const a of ['placeholder', 'aria-label', 'title']) { const v = e.getAttribute(a); if (v && /[\u0621-\u064A]{2,}/.test(v)) { const k = `${a}="${v.slice(0, 60)}"`; if (!seenAr.has(k)) { seenAr.add(k); out.arabic.push(`${k}  ⟨${sel(e)}⟩`); } } }
  }
  // Contrast
  const rgba = (s) => { const m = s.match(/rgba?\(([^)]+)\)/); if (!m) return null; const p = m[1].split(/[ ,/]+/).filter(Boolean).map(Number); return [p[0], p[1], p[2], p.length > 3 ? p[3] : 1]; };
  const lum = ([r, g, b]) => { const f = (c) => { c /= 255; return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); }; return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b); };
  const blend = (top, bot) => { const a = top[3]; return [top[0] * a + bot[0] * (1 - a), top[1] * a + bot[1] * (1 - a), top[2] * a + bot[2] * (1 - a), 1]; };
  const pageBg = () => { for (const e of [document.body, document.documentElement]) { const c = rgba(getComputedStyle(e).backgroundColor); if (c && c[3] > 0.5) return c; } return [255, 255, 255, 1]; };
  // Background really under the text: the stack of elements at the text's centre (siblings too, not only parents).
  const bgOf = (el) => {
    const r = el.getBoundingClientRect(); const x = Math.min(Math.max(r.left + Math.min(r.width / 2, 12), 1), innerWidth - 1), y = r.top + r.height / 2;
    if (y < 0 || y > innerHeight) return 'offscreen';
    const stack = document.elementsFromPoint(x, y); const i = stack.indexOf(el); const layers = [];
    for (const p of (i >= 0 ? stack.slice(i) : [el, ...stack])) {
      // decorative layers drawn with ::before / ::after paint above the element's own background
      for (const pe of ['::after', '::before']) {
        const ps = getComputedStyle(p, pe);
        if (ps.content === 'none' || ps.display === 'none' || +ps.opacity < 0.3) continue;
        const pr = p.getBoundingClientRect(); if (ps.position !== 'absolute' && ps.position !== 'fixed') continue;
        const pstops = (ps.backgroundImage.match(/rgba?\([^)]+\)/g) || []).map(rgba).filter((c) => c && c[3] >= 0.6);
        if (pstops.length >= 2 && pr.width > 50) { layers.push([0, 1, 2].map((k) => pstops.reduce((t, c) => t + c[k], 0) / pstops.length).concat(1)); return finish(layers); }
        const pc = rgba(ps.backgroundColor); if (pc && pc[3] >= 0.95 && pr.width > 50) { layers.push(pc); return finish(layers); }
      }
      const s = getComputedStyle(p);
      if (p.tagName === 'IMG' || p.tagName === 'CANVAS' || p.tagName === 'VIDEO' || /url\(/.test(s.backgroundImage)) return null; // over a picture: cannot judge
      if (s.backgroundImage && s.backgroundImage !== 'none') {
        const stops = (s.backgroundImage.match(/rgba?\([^)]+\)/g) || []).map(rgba).filter((c) => c && c[3] >= 0.6);
        if (stops.length) { layers.push([0, 1, 2].map((k) => stops.reduce((t, c) => t + c[k], 0) / stops.length).concat(1)); break; }
      }
      const c = rgba(s.backgroundColor); if (c && c[3] > 0) { layers.push(c); if (c[3] >= 0.95) break; }
    }
    return finish(layers);
  };
  function finish(layers) { let col = pageBg(); for (let k = layers.length - 1; k >= 0; k--) col = blend(layers[k], col); return col; }
  const seenC = new Set();
  const screens = []; for (let y = 0, n = 0; y < document.documentElement.scrollHeight && n < 6; y += innerHeight * 0.9, n++) screens.push(y);
  for (const sy of screens) { window.scrollTo({ top: sy, behavior: 'instant' });
  for (const el of textEls) {
    const r = el.getBoundingClientRect(); if (r.bottom < 0 || r.top > innerHeight) continue;
    const s = getComputedStyle(el); const fg = rgba(s.color); if (!fg || fg[3] < 0.3) continue;
    if (el.closest('svg,canvas,.sc-stage,[data-no-contrast]')) continue;
    const r0 = el.getBoundingClientRect(); if (r0.width <= 2 && r0.height <= 2) continue; // screen-reader-only text
    const bg = bgOf(el); if (!bg || bg === 'offscreen') continue;
    const f = blend(fg, bg); const L1 = lum(f), L2 = lum(bg);
    const ratio = (Math.max(L1, L2) + 0.05) / (Math.min(L1, L2) + 0.05);
    const size = parseFloat(s.fontSize); const bold = +s.fontWeight >= 700;
    const need = size >= 24 || (bold && size >= 18.6) ? 3 : 4.5;
    if (ratio < need - 0.05) {
      const k = sel(el) + Math.round(ratio * 10); if (seenC.has(k)) continue; seenC.add(k);
      out.contrast.push({ ratio: +ratio.toFixed(2), need, el: sel(el), text: (el.textContent || '').trim().slice(0, 40), fg: s.color, bg: `rgb(${bg.slice(0, 3).map(Math.round).join(',')})` });
    }
  }
  }
  window.scrollTo({ top: 0, behavior: 'instant' });
  // Tap targets on phones
  if (W < 768) {
    for (const e of document.querySelectorAll('a[href],button,input:not([type=hidden]),select,textarea,[role=button],[role=tab]')) {
      if (!shown(e)) continue;
      if (e.tagName === 'A' && e.closest('p,li p,td') && !e.matches('.btn,[class*=btn]')) continue; // inline text link
      const r = e.getBoundingClientRect(); if (r.width <= 2 || r.height <= 2) continue; const m = Math.min(r.width, r.height);
      if (m < 24) out.taps.push(`${sel(e)} ${Math.round(r.width)}x${Math.round(r.height)} "${(e.textContent || e.getAttribute('aria-label') || '').trim().slice(0, 20)}"`);
    }
    out.taps = [...new Set(out.taps)].slice(0, 12);
  }
  return out;
}

// Mobile drawer + bottom bar checks (dashboard pages, phone/tablet widths).
async function drawerCheck(p, w) {
  const r = { issues: [] };
  const has = await p.evaluate(() => !!document.querySelector('.dash-sidebar'));
  if (!has) return null;
  // bottom bar must not cover the end of the page
  const cover = await p.evaluate(async () => {
    const bar = [...document.querySelectorAll('.dash-bottombar, .bottom-bar, nav[class*=bottom]')].find((b) => { const s = getComputedStyle(b); return s.position === 'fixed' && s.display !== 'none' && b.getBoundingClientRect().height > 0; });
    if (!bar) return null;
    window.scrollTo({ top: document.documentElement.scrollHeight, behavior: 'instant' }); await new Promise((res) => setTimeout(res, 400));
    const top = bar.getBoundingClientRect().top;
    const main = document.querySelector('main') || document.body;
    const kids = [...main.querySelectorAll('*')].filter((e) => { const rr = e.getBoundingClientRect(); return rr.height > 0 && rr.width > 0 && getComputedStyle(e).position !== 'fixed' && !bar.contains(e); });
    const bottom = Math.max(...kids.map((e) => e.getBoundingClientRect().bottom));
    window.scrollTo({ top: 0, behavior: 'instant' });
    return { top: Math.round(top), bottom: Math.round(bottom) };
  });
  if (cover && cover.bottom > cover.top + 2) r.issues.push(`bottom bar covers content (content ends ${cover.bottom}, bar starts ${cover.top})`);
  const toggle = () => p.evaluate(() => { const b = [...document.querySelectorAll('.dash-menu-btn')].find((x) => x.getBoundingClientRect().width > 0 && !x.closest('.dash-sidebar')); if (b) b.click(); return !!b; });
  await p.evaluate(() => { document.body.classList.remove('nav-open'); const s = document.querySelector('.dash-scrim'); if (s) s.hidden = true; });
  if (!(await toggle())) return r;
  await p.waitForTimeout(600);
  const st = await p.evaluate(() => {
    const sb = document.querySelector('.dash-sidebar'); const rr = sb.getBoundingClientRect(); const s = getComputedStyle(sb);
    const close = [...sb.querySelectorAll('button,[role=button]')].find((b) => /close|إغلاق|اغلاق/i.test((b.getAttribute('aria-label') || '') + b.textContent) || b.classList.contains('dash-menu-btn'));
    const closeVisible = close && close.getBoundingClientRect().width > 0 && close.getBoundingClientRect().top < innerHeight;
    const scrim = document.querySelector('.dash-scrim');
    return { open: document.body.classList.contains('nav-open'), left: Math.round(rr.left), right: Math.round(rr.right), top: Math.round(rr.top), bottom: Math.round(rr.bottom),
      vw: innerWidth, vh: innerHeight, sh: sb.scrollHeight, ch: sb.clientHeight, oy: s.overflowY, closeVisible: !!closeVisible,
      scrim: !!scrim && !scrim.hidden && getComputedStyle(scrim).display !== 'none', bodyLock: ['hidden', 'clip'].includes(getComputedStyle(document.body).overflowY) || ['hidden', 'clip'].includes(getComputedStyle(document.documentElement).overflowY),
      zSide: +s.zIndex || 0 };
  });
  if (!st.open) r.issues.push('menu button did not open the drawer');
  if (st.left < -1 || st.right > st.vw + 1) r.issues.push(`drawer outside the screen [${st.left}..${st.right}] of ${st.vw}`);
  if (st.bottom > st.vh + 1 && !['auto', 'scroll'].includes(st.oy)) r.issues.push('drawer taller than the screen and does not scroll');
  if (st.sh > st.ch + 2 && !['auto', 'scroll'].includes(st.oy)) r.issues.push('drawer content cut off (no scroll)');
  if (!st.closeVisible) r.issues.push('no visible close button inside the drawer');
  if (!st.scrim) r.issues.push('no backdrop behind the drawer');
  if (!st.bodyLock) r.issues.push('page behind the drawer still scrolls');
  await p.keyboard.press('Escape'); await p.waitForTimeout(400);
  if (await p.evaluate(() => document.body.classList.contains('nav-open'))) r.issues.push('Esc does not close the drawer');
  // backdrop click
  await toggle(); await p.waitForTimeout(500);
  const scr = p.locator('.dash-scrim'); if (await scr.count()) { await scr.click({ position: { x: (await p.evaluate(() => { const r = document.querySelector('.dash-sidebar').getBoundingClientRect(); return r.left > 20 ? Math.round(r.left / 2) : Math.round((r.right + innerWidth) / 2); })), y: 300 }, force: true }).catch(() => {}); await p.waitForTimeout(400); }
  if (await p.evaluate(() => document.body.classList.contains('nav-open'))) r.issues.push('backdrop click does not close the drawer');
  await p.evaluate(() => { document.body.classList.remove('nav-open'); const s = document.querySelector('.dash-scrim'); if (s) s.hidden = true; });
  return r;
}

async function setupTech(b) {
  const ctx = await b.newContext();
  const t = await ctx.request.post(`${BASE}/api/v1/auth/login`, { data: { email: TECH[0], password: TECH[1] } });
  if (t.ok()) { await ctx.close(); return; }
  const owner = await b.newContext();
  await owner.request.post(`${BASE}/api/v1/auth/login`, { data: { email: CREDS.lab[0], password: CREDS.lab[1] } });
  const inv = await owner.request.post(`${BASE}/api/v1/labs/my/staff`, { data: { fullName: 'فني الجودة QA', email: TECH[0], phone: '01000000000' } });
  const dir = path.join(__dirname, '../../src/LabStage.Api/App_Data/outbox');
  const f = fs.readdirSync(dir).filter((x) => x.includes('qa_tech')).sort().pop();
  const html = f ? fs.readFileSync(path.join(dir, f), 'utf8') : '';
  const tok = decodeURIComponent((html.match(/token=([^"&<\s]+)/) || [])[1] || '');
  const rs = await ctx.request.post(`${BASE}/api/v1/auth/reset-password`, { data: { token: tok, newPassword: TECH[1] } });
  console.log('technician account', inv.status(), rs.status());
  await owner.close(); await ctx.close();
}

(async () => {
  fs.mkdirSync(path.join(OUT, 'shots'), { recursive: true });
  const b = await chromium.launch({ executablePath: CHROME });
  const roles = process.env.ONLY ? process.env.ONLY.split(',') : Object.keys(PAGES);
  if (roles.includes('tech')) await setupTech(b);
  const axeSrc = AXE ? fs.readFileSync(require.resolve('axe-core/axe.min.js'), 'utf8') : '';
  const results = [];
  for (const role of roles) {
    for (const lang of (process.env.LANGS || 'ar,en').split(',')) for (const theme of (process.env.THEMES || 'light,dark').split(',')) {
      const ctx = await b.newContext({ viewport: { width: 1440, height: 900 } });
      if (CREDS[role]) {
        const lr = await ctx.request.post(`${BASE}/api/v1/auth/login`, { data: { email: CREDS[role][0], password: CREDS[role][1] } });
        if (!lr.ok()) { console.log('LOGIN FAILED', role, lr.status()); await new Promise((r) => setTimeout(r, 61000)); await ctx.request.post(`${BASE}/api/v1/auth/login`, { data: { email: CREDS[role][0], password: CREDS[role][1] } }); }
      }
      await ctx.addInitScript(([l, t]) => { localStorage.setItem('labstage_lang', l); localStorage.setItem('labstage_theme', t); localStorage.removeItem('taaj_rail_pinned'); }, [lang, theme]);
      for (const pg of PAGES[role].filter((x) => !process.env.PAGES || x.includes(process.env.PAGES))) {
        const p = await ctx.newPage();
        const errors = []; const failed = []; let apiText = '';
        p.on('response', async (r) => { if (/\/api\//.test(r.url()) && /json/.test(r.headers()['content-type'] || '')) { try { apiText += JSON.stringify(await r.json()); } catch {} } });
        p.on('pageerror', (e) => errors.push('JS ' + e.message.slice(0, 160)));
        p.on('console', (m) => { if (m.type() === 'error' && !/Failed to load resource|401|423|Connecting to .https:..fonts/.test(m.text())) errors.push(m.text().slice(0, 160)); });
        p.on('response', (r) => { const s = r.status(); if (s >= 400 && ![401, 423].includes(s) && !/favicon|token=x|reset-password/.test(r.url())) failed.push(`${s} ${r.url().replace(BASE, '')}`); });
        await p.goto(`${BASE}/${pg}`, { waitUntil: 'load' }).catch((e) => errors.push('NAV ' + e.message.slice(0, 80)));
        await p.waitForTimeout(1500);
        for (const w of WIDTHS) {
          const mobile = w < 768;
          await p.setViewportSize({ width: w, height: mobile ? 800 : 900 }); await p.waitForTimeout(350);
          const pr = await p.evaluate(probe, { lang, w });
          const isData = (t) => { const ws = (t.replace(/\s+⟨.*$/, '').match(/[ء-ي]{2,}/g) || []); return ws.length > 0 && ws.filter((x) => apiText.includes(x)).length / ws.length >= 0.7; };
          pr.userData = pr.arabic.filter(isData).length; pr.arabic = pr.arabic.filter((t) => !isData(t));
          const cell = { role, page: pg, lang, theme, w, ...pr, errors: [...errors], failed: [...new Set(failed)] };
          if (w <= 768) { try { const d = await drawerCheck(p, w); if (d) cell.drawer = d.issues; } catch (e) { cell.drawer = ['drawer test crashed: ' + e.message.slice(0, 80)]; } }
          if (AXE && (w === 390 || w === 1440)) {
            await p.evaluate(axeSrc);
            const ax = await p.evaluate(async () => { const r = await axe.run(document, { resultTypes: ['violations'] }); return r.violations.map((v) => ({ id: v.id, impact: v.impact, n: v.nodes.length, sample: v.nodes[0]?.target?.join(' ') })); });
            cell.axe = ax;
          }
          if (SHOTS && (w === 390 || w === 1440)) {
            const f = `${role}-${pg.replace(/[^a-z0-9]+/gi, '_').slice(0, 40)}-${lang}-${theme}-${w}.png`;
            await p.screenshot({ path: path.join(OUT, 'shots', f) }); cell.shot = f;
          }
          results.push(cell);
        }
        const last = results[results.length - 1];
        const n = results.filter((c) => c.role === role && c.page === pg && c.lang === lang && c.theme === theme);
        const sum = (k) => n.reduce((s, c) => s + (Array.isArray(c[k]) ? c[k].length : (c[k] > 1 ? 1 : 0)), 0);
        console.log(`${role} ${pg} ${lang}/${theme}: overflow ${sum('overflow')} arabic ${sum('arabic')} contrast ${sum('contrast')} taps ${sum('taps')} drawer ${sum('drawer')} errors ${last.errors.length} failed ${last.failed.length}`);
        await p.close();
      }
      await ctx.close();
    }
  }
  await b.close();
  fs.writeFileSync(path.join(OUT, 'ui-matrix.json'), JSON.stringify(results, null, 1));
  // Summary
    const uniq = { arabic: new Set(), labels: new Set(), contrast: new Set(), taps: new Set(), drawer: new Set(), errors: new Set(), failed: new Set(), axe: new Set(), overflow: new Set() };
  for (const c of results) {
    if (c.overflow > 1) uniq.overflow.add(`${c.page} @${c.w}: ${c.offenders.join(', ')}`);
    c.arabic.forEach((x) => (/^(aria-label|title|placeholder)=/.test(x) ? uniq.labels : uniq.arabic).add(`${c.page}: ${x.replace(/  ⟨.*$/, '')}`));
    c.contrast.forEach((x) => uniq.contrast.add(`${c.theme} ${c.page}: ${x.el.replace(/#.*/, '')} ${x.ratio}<${x.need} (${x.fg} on ${x.bg}) "${x.text.slice(0, 25)}"`));
    c.taps.forEach((x) => uniq.taps.add(`${c.page}: ${x}`));
    (c.drawer || []).forEach((x) => uniq.drawer.add(`${c.role} ${c.page} @${c.w}: ${x.replace(/\(.*\)/, '')}`));
    c.errors.forEach((x) => uniq.errors.add(`${c.page}: ${x}`));
    c.failed.forEach((x) => uniq.failed.add(`${c.page}: ${x}`));
    (c.axe || []).filter((v) => ['critical', 'serious'].includes(v.impact)).forEach((v) => uniq.axe.add(`${c.theme} ${c.page}: ${v.id} (${v.impact}) x${v.n} ${v.sample}`));
  }
  let md = `# Taaj UI QA matrix — ${new Date().toISOString()}\n\nCells: ${results.length}\n\n| check | unique issues |\n|---|---|\n`;
  for (const k of Object.keys(uniq)) md += `| ${k} | ${uniq[k].size} |\n`;
  for (const k of Object.keys(uniq)) { md += `\n## ${k} (${uniq[k].size})\n`; [...uniq[k]].slice(0, 400).forEach((x) => { md += `- ${x}\n`; }); }
  fs.writeFileSync(path.join(OUT, 'ui-matrix.md'), md);
  console.log('\nSUMMARY'); for (const k of Object.keys(uniq)) console.log(k.padEnd(10), uniq[k].size);
})();
