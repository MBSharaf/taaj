import { initPreferences, applyDomTranslations, getLang } from '../core/i18n.js?v=9';

// Unified controller for Dentist, Laboratory, and Admin dashboards.
// Enforces authentication & role checks before rendering any protected data.

const fmt = new Intl.NumberFormat('ar-EG', { maximumFractionDigits: 2 });
const fmtEgp = (v) => `${fmt.format(Number(v || 0))} ج.م`;
const page = document.body.dataset.dashPage || '';

// Modern Duotone 3D Vector Icons
const ICONS = {
  brand: `<svg class="brand-mark" viewBox="0 0 40 40" aria-hidden="true"><rect width="40" height="40" rx="12" fill="#2F6BFF"/><path d="M12.5 13.2c2.2-2 4.4-1.6 7.5-.4 3.1-1.2 5.3-1.6 7.5.4 2.4 2.2 1.8 6.4.6 9.7-.9 2.6-1.3 5.6-2.4 7.6-.8 1.4-2.3 1.3-2.8-.3l-1.3-4.6c-.4-1.4-2.8-1.4-3.2 0l-1.3 4.6c-.5 1.6-2 1.7-2.8.3-1.1-2-1.5-5-2.4-7.6-1.2-3.3-1.8-7.5.6-9.7Z" fill="#CFE2FF"/></svg>`,
  grid: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="7" height="7" rx="2" fill="currentColor" fill-opacity=".22"/><rect x="14" y="3" width="7" height="7" rx="2"/><rect x="14" y="14" width="7" height="7" rx="2" fill="currentColor" fill-opacity=".22"/><rect x="3" y="14" width="7" height="7" rx="2"/></svg>`,
  tooth: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M7.5 4.8C9 3.5 10.6 3.9 12 4.6c1.4-.7 3-1.1 4.5.2 1.7 1.5 1.3 4.5.4 6.8-.6 1.8-.9 3.9-1.7 5.3-.5 1-1.6.9-2-.2l-.9-3.2c-.3-1-1.9-1-2.2 0l-.9 3.2c-.4 1.1-1.5 1.2-2 .2-.8-1.4-1.1-3.5-1.7-5.3-.9-2.3-1.3-5.3.5-6.8Z" fill="currentColor" fill-opacity=".22"/></svg>`,
  wallet: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="2.5" y="5.5" width="19" height="14" rx="3" fill="currentColor" fill-opacity=".2"/><path d="M16 12.5h5.5M6.5 5.5l9-2.5v2.5"/><circle cx="16.5" cy="12.5" r="1.2" fill="currentColor"/></svg>`,
  compass: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9" fill="currentColor" fill-opacity=".18"/><polygon points="15.5 8.5 13.5 13.5 8.5 15.5 10.5 10.5 15.5 8.5" fill="currentColor"/></svg>`,
  shield: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3l7.5 3.2v5.3c0 5-3.2 8.6-7.5 10-4.3-1.4-7.5-5-7.5-10V6.2L12 3z" fill="currentColor" fill-opacity=".22"/><path d="M9 12.2l2 2 4.2-4.2"/></svg>`,
  layers: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><polygon points="12 2.5 21 7.5 12 12.5 3 7.5 12 2.5" fill="currentColor" fill-opacity=".25"/><polyline points="3 12.5 12 17.5 21 12.5"/><polyline points="3 17 12 22 21 17"/></svg>`,
  chart: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="12" width="4" height="8" rx="1" fill="currentColor" fill-opacity=".24"/><rect x="10" y="7" width="4" height="13" rx="1" fill="currentColor" fill-opacity=".24"/><rect x="17" y="3" width="4" height="17" rx="1" fill="currentColor" fill-opacity=".4"/></svg>`,
  bell: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M18 8a6 6 0 0 0-12 0c0 7-3 9-3 9h18s-3-2-3-9" fill="currentColor" fill-opacity=".22"/><path d="M13.73 21a2 2 0 0 1-3.46 0"/></svg>`,
  spark: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2.5l2.4 6.1 6.1 2.4-6.1 2.4-2.4 6.1-2.4-6.1-6.1-2.4 6.1-2.4L12 2.5z" fill="currentColor" fill-opacity=".26"/></svg>`,
  crown3dWire: `<svg class="dash-3d-wire-fallback" viewBox="0 0 160 140" fill="none" aria-hidden="true">
    <defs>
      <radialGradient id="crGlow" cx="50%" cy="45%" r="55%">
        <stop offset="0%" stop-color="#CFE2FF" stop-opacity="0.55"/>
        <stop offset="65%" stop-color="#7CC4FF" stop-opacity="0.18"/>
        <stop offset="100%" stop-color="#1B47D6" stop-opacity="0"/>
      </radialGradient>
    </defs>
    <ellipse cx="80" cy="112" rx="54" ry="12" stroke="#6E8FE0" stroke-width="1.2" stroke-dasharray="4 3" opacity="0.7"/>
    <path d="M28 74 C24 44, 46 22, 64 30 C74 34, 86 34, 96 30 C114 22, 136 44, 132 74 C129 96, 114 110, 80 110 C46 110, 31 96, 28 74 Z" fill="url(#crGlow)" stroke="#CFE2FF" stroke-width="1.8"/>
    <path d="M34 58 C56 46, 104 46, 126 58 M30 76 C58 66, 102 66, 130 76 M36 92 C60 84, 100 84, 124 92" stroke="#7CC4FF" stroke-width="1.1" opacity="0.8"/>
    <path d="M52 28 C48 56, 52 86, 58 108 M80 33 L80 110 M108 28 C112 56, 108 86, 102 108" stroke="#7CC4FF" stroke-width="1.1" opacity="0.75"/>
    <path d="M44 40 Q62 52 80 42 Q98 52 116 40" stroke="#ffffff" stroke-width="1.6" stroke-linecap="round" opacity="0.9"/>
  </svg>`,
};

function enhanceDashboard3DAndIcons() {
  // 1. Upgrade sidebar brand & nav links with modern 3D duotone SVG icons
  const brand = document.querySelector('.dash-sidebar .brand');
  if (brand && !brand.querySelector('svg, img')) {
    brand.insertAdjacentHTML('afterbegin', ICONS.brand);
  }
  document.querySelectorAll('.dash-nav a').forEach((a) => {
    if (a.querySelector('.nav-ic')) return;
    const href = a.getAttribute('href') || '';
    let ic = ICONS.grid;
    if (href.includes('orders')) ic = ICONS.tooth;
    else if (href.includes('#dentists')) ic = ICONS.layers;
    else if (href.includes('#wallet') || href.includes('finance')) ic = ICONS.wallet;
    else if (href.includes('#pricelist')) ic = ICONS.chart;
    else if (href.includes('#storefront')) ic = ICONS.spark;
    else if (href.includes('#campaigns')) ic = ICONS.bell;
    else if (href.includes('/labs')) ic = ICONS.compass;
    else if (href.includes('admin')) ic = ICONS.shield;
    a.insertAdjacentHTML('afterbegin', `<span class="nav-ic" aria-hidden="true">${ic}</span>`);
  });

  // 2. Replace plain emoji notification bell with custom SVG
  const notifPill = document.querySelector('.notif-pill');
  if (notifPill) {
    const countSpan = notifPill.querySelector('#notif-count');
    const cText = countSpan ? countSpan.textContent : '0';
    notifPill.innerHTML = `${ICONS.bell} <span id="notif-count" class="num">${cText}</span> إشعارات`;
  }

  // 3. Inject 3D Duotone Icon Orbs + Interactive 3D Pointer Tilt on all .stat-card elements
  const statIcons = [ICONS.tooth, ICONS.layers, ICONS.wallet, ICONS.chart];
  document.querySelectorAll('.stat-card').forEach((card, idx) => {
    if (!card.querySelector('.stat-icon-3d')) {
      const label = card.querySelector('.stat-label');
      const topRow = document.createElement('div');
      topRow.className = 'stat-card-top';
      if (label) topRow.appendChild(label);
      const orb = document.createElement('span');
      orb.className = 'stat-icon-3d';
      orb.setAttribute('aria-hidden', 'true');
      orb.innerHTML = statIcons[idx % statIcons.length];
      topRow.appendChild(orb);
      card.insertAdjacentElement('afterbegin', topRow);
    }

    let tiltRaf = 0;
    card.addEventListener('pointermove', (e) => {
      if (tiltRaf) return;
      const cx = e.clientX;
      const cy = e.clientY;
      tiltRaf = requestAnimationFrame(() => {
        tiltRaf = 0;
        const r = card.getBoundingClientRect();
        const px = (cx - r.left) / r.width;
        const py = (cy - r.top) / r.height;
        const rx = ((0.5 - py) * 8).toFixed(2) + 'deg';
        const ry = ((px - 0.5) * 8).toFixed(2) + 'deg';
        card.style.setProperty('--rx', rx);
        card.style.setProperty('--ry', ry);
        card.style.setProperty('--mx', `${(px * 100).toFixed(1)}%`);
        card.style.setProperty('--my', `${(py * 100).toFixed(1)}%`);
      });
    }, { passive: true });
    card.addEventListener('pointerleave', () => {
      card.style.setProperty('--rx', '0deg');
      card.style.setProperty('--ry', '0deg');
    });
  });

  // 4. Mount interactive WebGL 3D Dental Crown inside .greeting-banner (or create one on Admin dashboard)
  let banner = document.querySelector('.greeting-banner');
  if (!banner && page === 'admin-dashboard') {
    const main = document.querySelector('.dash-main');
    const topbar = document.querySelector('.dash-topbar');
    if (main && topbar) {
      banner = document.createElement('section');
      banner.className = 'greeting-banner';
      banner.innerHTML = `
        <div class="greeting-copy">
          <span class="greeting-eyebrow">${ICONS.shield} الرقابة المالية والتشغيلية الموحدة</span>
          <h1>مركز قيادة منصة تاج — مصر</h1>
          <p>مراقبة فورية لدفتر الأستاذ المالي، عمولات المنصة (2% معمل + 1% طبيب)، مصروفات بوابة الدفع، وتوثيق المعامل.</p>
        </div>
        <div class="dash-3d-stage" id="dash-3d-crown-host">${ICONS.crown3dWire}<span class="dash-3d-pill">3D CAD/CAM LIVE</span></div>
        <a class="btn btn-mint btn-lg" href="/labs">استعراض دليل المعامل العام</a>`;
      topbar.insertAdjacentElement('afterend', banner);
    }
  } else if (banner && !banner.querySelector('.dash-3d-stage')) {
    const firstDiv = banner.querySelector('div');
    if (firstDiv) {
      firstDiv.classList.add('greeting-copy');
      if (!firstDiv.querySelector('.greeting-eyebrow')) {
        const badgeText = page.startsWith('lab')
          ? 'نظام إدارة الإنتاج الرقمي CAD/CAM'
          : 'بوابة العيادة الرقمية وتتبع المراحل';
        firstDiv.insertAdjacentHTML('afterbegin', `<span class="greeting-eyebrow">${ICONS.spark} ${badgeText}</span>`);
      }
    }
    const stageDiv = document.createElement('div');
    stageDiv.className = 'dash-3d-stage';
    stageDiv.id = 'dash-3d-crown-host';
    stageDiv.innerHTML = `${ICONS.crown3dWire}<span class="dash-3d-pill">3D CROWN STAGE</span>`;
    if (firstDiv) firstDiv.insertAdjacentElement('afterend', stageDiv);
    else banner.prepend(stageDiv);
  }

  const crownHost = document.getElementById('dash-3d-crown-host');
  if (crownHost && !crownHost.dataset.mounted) {
    crownHost.dataset.mounted = '1';
    const idle = window.requestIdleCallback || ((cb) => setTimeout(cb, 450));
    idle(async () => {
      try {
        const { mountCrown } = await import('./crown3d.js?v=6');
        const stageIdx = page.startsWith('lab') ? 1.2 : page.startsWith('admin') ? 3.0 : 0.15;
        const ctrl = mountCrown(crownHost, { reducedMotion: false });
        ctrl.setStage(stageIdx);
        let s = stageIdx;
        crownHost.addEventListener('click', () => {
          s = (Math.floor(s) + 1) % 4;
          ctrl.setStage(s);
        });
      } catch {
        // Fallback 3D wireframe SVG remains visible
      }
    });
  }
}

function esc(s) {
  return String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

const stageAr = { Design: 'التصميم', Milling: 'التفريز', Layering: 'الطبقات', Glazing: 'الجليز' };
const statusAr = {
  PendingLabReview: 'بانتظار مراجعة المعمل',
  PriceRevisionProposed: 'مقترح سعر بانتظار موافقة الطبيب',
  Accepted: 'مقبول ومثبّت السعر',
  Rejected: 'مرفوض',
  InProduction: 'قيد التصنيع',
  ReadyForDelivery: 'جاهز للتسليم',
  Completed: 'مكتمل ومسلّم',
  NotStarted: 'لم تبدأ',
  InProgress: 'قيد التنفيذ',
  SubmittedForReview: 'مرفوعة لمراجعة الطبيب',
  RevisionRequested: 'مطلوب تعديل',
  Approved: 'معتمدة من الطبيب',
  Blocked: 'متوقفة مؤقتًا',
  Locked: 'غير مستحقة بعد',
  Due: 'مستحقة السداد الآن',
  Paid: 'مدفوعة ✓',
  Refunded: 'مستردة',
};

let currentUser = null;

async function ensureAuthenticated() {
  const res = await fetch('/api/v1/auth/me');
  if (!res.ok) {
    window.location.href = `/login.html?returnUrl=${encodeURIComponent(window.location.pathname + window.location.search)}`;
    return null;
  }
  const user = await res.json();
  currentUser = user;
  document.getElementById('sidebar-user-name') && (document.getElementById('sidebar-user-name').textContent = `${user.fullName} (${user.email})`);
  document.getElementById('top-user-name') && (document.getElementById('top-user-name').textContent = user.fullName);
  document.getElementById('notif-count') && (document.getElementById('notif-count').textContent = fmt.format(user.unreadNotifications || 0));
  if (user.laboratorySlug && document.getElementById('my-public-profile-link')) {
    document.getElementById('my-public-profile-link').href = `/labs/${encodeURIComponent(user.laboratorySlug)}`;
  }
  return user;
}

document.getElementById('logout-btn')?.addEventListener('click', async () => {
  await fetch('/api/v1/auth/logout', { method: 'POST' });
  window.location.href = '/login.html';
});

async function loadNotifications() {
  const box = document.getElementById('notif-list');
  if (!box) return;
  const res = await fetch('/api/v1/notifications');
  if (!res.ok) return;
  const data = await res.json();
  if (!data.items?.length) {
    box.innerHTML = `<p class="muted">لا توجد إشعارات جديدة.</p>`;
    return;
  }
  box.innerHTML = data.items.slice(0, 8).map((n) => `
    <div style="padding:var(--sp-3);border-radius:var(--r-md);background:var(--bg);border:1px solid var(--line);display:flex;justify-content:space-between;gap:var(--sp-2);align-items:center">
      <div><strong>${esc(n.title)}</strong><div class="muted" style="font-size:var(--fs-xs)">${esc(n.body)}</div></div>
      ${n.linkUrl ? `<a class="btn btn-ghost btn-sm" href="${esc(n.linkUrl)}">عرض</a>` : ''}
    </div>`).join('');
}

function renderBarChart(containerId, items) {
  const el = document.getElementById(containerId);
  if (!el || !items?.length) return;
  const maxVal = Math.max(...items.map((i) => Number(i.value || 0)), 1);
  el.innerHTML = `
    <div class="chart-bars-grid">
      ${items.map((item) => {
        const pct = Math.max(12, Math.round((Number(item.value || 0) / maxVal) * 100));
        return `
          <div class="chart-bar-col">
            <span class="chart-bar-val num">${esc(item.display)}</span>
            <div class="chart-bar-fill ${item.mint ? 'is-mint' : ''}" style="block-size:${pct}%"></div>
            <span class="chart-bar-label">${esc(item.label)}</span>
          </div>`;
      }).join('')}
    </div>`;
}

function renderProgressChart(containerId, rows) {
  const el = document.getElementById(containerId);
  if (!el || !rows?.length) return;
  const maxVal = Math.max(...rows.map((r) => Number(r.value || 0)), 1);
  el.innerHTML = `
    <div class="chart-progress-list">
      ${rows.map((r) => {
        const pct = Math.max(8, Math.min(100, Math.round((Number(r.value || 0) / maxVal) * 100)));
        return `
          <div class="chart-progress-row">
            <div class="chart-progress-meta">
              <span>${esc(r.label)}</span>
              <strong class="num">${esc(r.display)}</strong>
            </div>
            <div class="chart-progress-track">
              <div class="chart-progress-fill" style="inline-size:${pct}%"></div>
            </div>
          </div>`;
      }).join('')}
    </div>`;
}

function getArabicMonogram(name) {
  const words = String(name || 'معمل').trim().split(/\s+/).map((w) => w.replace(/^ال/, ''));
  const first = words[0]?.[0] || 'م';
  const second = words[1]?.[0] || words[0]?.[1] || 'ع';
  return `${first}\u200C${second}`;
}

// ---- Doctor Dashboard ----
async function initDoctorDashboard() {
  document.getElementById('greeting-title').textContent = `مرحبًا ${currentUser.fullName}`;
  const [ordersRes, finRes] = await Promise.all([
    fetch('/api/v1/orders'),
    fetch('/api/v1/finance/doctor'),
  ]);
  let items = [];
  if (ordersRes.ok) {
    const ordersData = await ordersRes.json();
    items = ordersData.items || [];
    document.getElementById('st-total-orders').textContent = fmt.format(items.length);
    document.getElementById('st-active-orders').textContent = fmt.format(items.filter((o) => !['Completed', 'Rejected'].includes(o.status)).length);
    const tbody = document.getElementById('doc-recent-orders');
    tbody.innerHTML = items.length ? items.slice(0, 8).map((o) => `
      <tr>
        <td class="ltr mono"><strong>${esc(o.orderNumber)}</strong></td>
        <td>${esc(o.serviceName)} <div class="muted" style="font-size:var(--fs-xs)">${esc(o.patientName || o.caseReference)}</div></td>
        <td>${esc(o.laboratoryName)}</td>
        <td><span class="badge">${esc(statusAr[o.status] || o.status)}</span></td>
        <td class="num">${fmtEgp(o.paidEgp)} / ${fmtEgp(o.confirmedPriceEgp ?? o.estimatedPriceEgp)}</td>
        <td><a class="btn btn-primary btn-sm" href="/doctor-order-details.html?orderNumber=${encodeURIComponent(o.orderNumber)}">التفاصيل والمحادثة</a></td>
      </tr>`).join('') : `<tr><td colspan="6" class="muted">لا توجد طلبات بعد.</td></tr>`;

    const allStages = items.flatMap((o) => o.stages || []);
    const sCounts = {
      Design: allStages.filter((s) => s.stage === 'Design').length || 1,
      Milling: allStages.filter((s) => s.stage === 'Milling').length || 1,
      Layering: allStages.filter((s) => s.stage === 'Layering').length || 1,
      Glazing: allStages.filter((s) => s.stage === 'Glazing').length || 1,
    };
    renderProgressChart('doc-stages-chart', [
      { label: '01 · التصميم الرقمي (Design)', value: sCounts.Design, display: `${fmt.format(sCounts.Design)} حالة` },
      { label: '02 · التفريز الخماسي (Milling)', value: sCounts.Milling, display: `${fmt.format(sCounts.Milling)} حالة` },
      { label: '03 · بناء الطبقات (Layering)', value: sCounts.Layering, display: `${fmt.format(sCounts.Layering)} حالة` },
      { label: '04 · الجليز والتشطيب (Glazing)', value: sCounts.Glazing, display: `${fmt.format(sCounts.Glazing)} حالة` },
    ]);
  }
  if (finRes.ok) {
    const fin = await finRes.json();
    document.getElementById('st-paid-egp').textContent = fmtEgp(fin.totalPaidEgp);
    document.getElementById('st-due-egp').textContent = fmtEgp(fin.totalDueNowEgp);
    document.getElementById('st-doc-comm') && (document.getElementById('st-doc-comm').textContent = fmtEgp(fin.dentistCommissionEgp || fin.totalExpectedDentistCommissionEgp));
    renderBarChart('doc-finance-chart', [
      { label: 'إجمالي الحالات المؤكدة', value: fin.totalConfirmedEgp || 1800, display: fmtEgp(fin.totalConfirmedEgp) },
      { label: 'المدفوع حتى الآن', value: fin.totalPaidEgp || 900, display: fmtEgp(fin.totalPaidEgp), mint: true },
      { label: 'دفعات مستحقة السداد', value: fin.totalDueNowEgp || 450, display: fmtEgp(fin.totalDueNowEgp) },
      { label: 'المتبقي عند الإتمام', value: fin.totalRemainingEgp || 900, display: fmtEgp(fin.totalRemainingEgp), mint: true },
    ]);
  }
  await loadNotifications();
}

// ---- Doctor Orders & CAD/CAM Case Builder ----
const FDI_ARCH_LAYOUT = [
  // Upper Right (11..18)
  { fdi: '11', x: 172, y: 42, rx: 14, ry: 17, rot: -6, kind: 'ant' },
  { fdi: '12', x: 141, y: 50, rx: 12.5, ry: 15.5, rot: -18, kind: 'ant' },
  { fdi: '13', x: 114, y: 69, rx: 13.5, ry: 16.5, rot: -32, kind: 'can' },
  { fdi: '14', x: 95, y: 98, rx: 15, ry: 15.5, rot: -18, kind: 'pre' },
  { fdi: '15', x: 83, y: 131, rx: 15.5, ry: 16, rot: -10, kind: 'pre' },
  { fdi: '16', x: 75, y: 169, rx: 19, ry: 18.5, rot: -4, kind: 'mol' },
  { fdi: '17', x: 73, y: 209, rx: 18.5, ry: 17.5, rot: 0, kind: 'mol' },
  { fdi: '18', x: 76, y: 247, rx: 17, ry: 16.5, rot: 4, kind: 'mol' },
  // Upper Left (21..28)
  { fdi: '21', x: 208, y: 42, rx: 14, ry: 17, rot: 6, kind: 'ant' },
  { fdi: '22', x: 239, y: 50, rx: 12.5, ry: 15.5, rot: 18, kind: 'ant' },
  { fdi: '23', x: 266, y: 69, rx: 13.5, ry: 16.5, rot: 32, kind: 'can' },
  { fdi: '24', x: 285, y: 98, rx: 15, ry: 15.5, rot: 18, kind: 'pre' },
  { fdi: '25', x: 297, y: 131, rx: 15.5, ry: 16, rot: 10, kind: 'pre' },
  { fdi: '26', x: 305, y: 169, rx: 19, ry: 18.5, rot: 4, kind: 'mol' },
  { fdi: '27', x: 307, y: 209, rx: 18.5, ry: 17.5, rot: 0, kind: 'mol' },
  { fdi: '28', x: 304, y: 247, rx: 17, ry: 16.5, rot: -4, kind: 'mol' },
  // Lower Right (48..41)
  { fdi: '48', x: 79, y: 294, rx: 17, ry: 16.5, rot: -5, kind: 'mol' },
  { fdi: '47', x: 77, y: 332, rx: 18.5, ry: 17.5, rot: 0, kind: 'mol' },
  { fdi: '46', x: 82, y: 372, rx: 19, ry: 18.5, rot: 6, kind: 'mol' },
  { fdi: '45', x: 93, y: 409, rx: 15, ry: 15, rot: 14, kind: 'pre' },
  { fdi: '44', x: 110, y: 439, rx: 14.5, ry: 14.5, rot: 24, kind: 'pre' },
  { fdi: '43', x: 131, y: 463, rx: 12.5, ry: 15, rot: 32, kind: 'can' },
  { fdi: '42', x: 154, y: 477, rx: 11, ry: 14, rot: 16, kind: 'ant' },
  { fdi: '41', x: 178, y: 483, rx: 10.5, ry: 13.5, rot: 5, kind: 'ant' },
  // Lower Left (31..38)
  { fdi: '31', x: 202, y: 483, rx: 10.5, ry: 13.5, rot: -5, kind: 'ant' },
  { fdi: '32', x: 226, y: 477, rx: 11, ry: 14, rot: -16, kind: 'ant' },
  { fdi: '33', x: 249, y: 463, rx: 12.5, ry: 15, rot: -32, kind: 'can' },
  { fdi: '34', x: 270, y: 439, rx: 14.5, ry: 14.5, rot: -24, kind: 'pre' },
  { fdi: '35', x: 287, y: 409, rx: 15, ry: 15, rot: -14, kind: 'pre' },
  { fdi: '36', x: 298, y: 372, rx: 19, ry: 18.5, rot: -6, kind: 'mol' },
  { fdi: '37', x: 303, y: 332, rx: 18.5, ry: 17.5, rot: 0, kind: 'mol' },
  { fdi: '38', x: 301, y: 294, rx: 17, ry: 16.5, rot: 5, kind: 'mol' },
];

function buildToothSvgMarkup(t) {
  const { rx, ry, kind } = t;
  // Organic anatomical crown silhouette + subtle peripheral cusps/grooves that keep the center clear for the FDI number
  const dCrown = `M ${-rx * 0.72} ${-ry}
    C ${-rx * 0.25} ${-ry * 1.06}, ${rx * 0.25} ${-ry * 1.06}, ${rx * 0.72} ${-ry}
    C ${rx * 1.06} ${-ry * 0.65}, ${rx * 1.08} ${ry * 0.65}, ${rx * 0.7} ${ry}
    C ${rx * 0.22} ${ry * 1.06}, ${-rx * 0.22} ${ry * 1.06}, ${-rx * 0.7} ${ry}
    C ${-rx * 1.08} ${ry * 0.65}, ${-rx * 1.06} ${-ry * 0.65}, ${-rx * 0.72} ${-ry} Z`;

  let grooves = '';
  if (kind === 'mol') {
    grooves = `<path class="tooth-grooves" d="M ${-rx * 0.65} 0 L ${-rx * 0.36} 0 M ${rx * 0.36} 0 L ${rx * 0.65} 0 M 0 ${-ry * 0.68} L 0 ${-ry * 0.38} M 0 ${ry * 0.38} L 0 ${ry * 0.68}"/>`;
  } else if (kind === 'pre') {
    grooves = `<path class="tooth-grooves" d="M ${-rx * 0.58} 0 L ${-rx * 0.32} 0 M ${rx * 0.32} 0 L ${rx * 0.58} 0"/>`;
  } else {
    grooves = `<path class="tooth-grooves" d="M ${-rx * 0.38} ${-ry * 0.58} Q 0 ${-ry * 0.7} ${rx * 0.38} ${-ry * 0.58}"/>`;
  }

  return `
    <g class="tooth-node" data-fdi="${t.fdi}" transform="translate(${t.x},${t.y})" role="button" tabindex="0" aria-label="السن رقم ${t.fdi}" aria-pressed="false">
      <g class="tooth-body">
        <g transform="rotate(${t.rot})">
          <path class="tooth-crown" d="${dCrown}"></path>
          ${grooves}
        </g>
        <text class="tooth-num" x="0" y="1">${t.fdi}</text>
      </g>
    </g>`;
}

async function initDoctorOrders() {
  const qs = new URLSearchParams(window.location.search);
  const qInput = document.getElementById('order-search-q');
  if (qs.get('q') && qInput) qInput.value = qs.get('q');

  const $ = (id) => document.getElementById(id);
  const createForm = $('create-order-form');
  const labSelect = $('co-lab');
  const serviceSelect = $('co-service');
  const priceInput = $('co-price');
  const teethInput = $('co-teeth');
  const teethList = $('cad-teeth-list');
  const patientInput = $('co-patient');
  const dueDateInput = $('co-due-date');
  const deliverySelect = $('co-delivery');
  const addrField = $('co-addr-field');
  const translate = (el) => { if (el && getLang() === 'en') applyDomTranslations(el); };

  const createdDateEl = $('cad-created-date');
  if (createdDateEl) createdDateEl.textContent = new Date().toLocaleDateString('en-GB');
  if (dueDateInput && !dueDateInput.value) {
    dueDateInput.value = new Date(Date.now() + 4 * 86400000).toISOString().slice(0, 10);
    dueDateInput.min = new Date().toISOString().slice(0, 10);
  }
  if (qs.get('lab') && labSelect) labSelect.value = qs.get('lab');

  // Courier needs the clinic address; pickup at the lab does not.
  const syncDelivery = () => { if (addrField) addrField.hidden = deliverySelect?.value !== 'LabCourier'; };
  deliverySelect?.addEventListener('change', syncDelivery);
  syncDelivery();

  // Lab price lists (public API): unit price of the chosen restoration type at the chosen lab.
  const labServices = new Map();
  try {
    const res = await fetch('/api/v1/labs');
    if (res.ok) (await res.json()).items?.forEach((l) => labServices.set(l.slug, l.services || []));
  } catch { /* keep the guide prices */ }

  function unitPrice() {
    const opt = serviceSelect?.selectedOptions?.[0];
    const match = opt?.dataset.match || '';
    const svc = (labServices.get(labSelect?.value) || []).find((s) => match && String(s.name).includes(match));
    return svc ? { price: Number(svc.priceEgp), fromLab: true } : { price: Number(opt?.dataset.unitPrice || 0), fromLab: false };
  }

  // ---- Tooth chart ----
  const selectedTeeth = new Set();
  const archGroup = $('odontogram-teeth-group');
  if (archGroup) archGroup.innerHTML = FDI_ARCH_LAYOUT.map(buildToothSvgMarkup).join('');
  const nodes = new Map([...(archGroup?.querySelectorAll('.tooth-node') || [])].map((n) => [n.dataset.fdi, n]));

  function updateSummary() {
    const n = selectedTeeth.size;
    const { price, fromLab } = unitPrice();
    const total = price * n;
    const now = Math.round(total * 0.2);
    if (priceInput) priceInput.value = String(total);
    $('sum-teeth').textContent = fmt.format(n);
    $('sum-total').textContent = fmt.format(total);
    $('sum-now').textContent = fmt.format(now);
    $('sum-rest').textContent = fmt.format(total - now);
    $('sum-patient').textContent = patientInput?.value.trim() || '—';
    $('sum-type').textContent = serviceSelect?.selectedOptions?.[0]?.textContent.trim() || '—';
    $('sum-price-src-lab').hidden = !fromLab;
    $('sum-price-src-guide').hidden = fromLab;
    const hasDesign = !!createForm?.querySelector('input[name="stage"][value="Design"]:checked');
    $('sum-now-when-design').hidden = !hasDesign;
    $('sum-now-when-confirm').hidden = hasDesign;
    translate(createForm?.querySelector('.order-summary'));
  }

  function syncTeeth() {
    const list = [...selectedTeeth].sort((a, b) => Number(a) - Number(b));
    if (teethInput) teethInput.value = list.join(', ');
    if (teethList) teethList.textContent = list.length ? list.join(', ') : '—';
    nodes.forEach((node, fdi) => {
      const on = selectedTeeth.has(fdi);
      node.classList.toggle('is-selected', on);
      node.setAttribute('aria-pressed', on ? 'true' : 'false');
    });
    updateSummary();
  }

  // One delegated listener for the whole chart; hover itself is pure CSS.
  const toggleTooth = (node) => {
    const fdi = node.dataset.fdi;
    if (selectedTeeth.has(fdi)) selectedTeeth.delete(fdi); else selectedTeeth.add(fdi);
    syncTeeth();
  };
  archGroup?.addEventListener('click', (e) => { const node = e.target.closest('.tooth-node'); if (node) toggleTooth(node); });
  archGroup?.addEventListener('keydown', (e) => {
    const node = e.target.closest('.tooth-node');
    if (node && (e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); toggleTooth(node); }
  });

  document.querySelectorAll('[data-quick-teeth]').forEach((btn) => {
    btn.addEventListener('click', () => {
      selectedTeeth.clear();
      (btn.dataset.quickTeeth || '').split(',').map((s) => s.trim()).filter(Boolean).forEach((t) => selectedTeeth.add(t));
      syncTeeth();
    });
  });

  serviceSelect?.addEventListener('change', updateSummary);
  labSelect?.addEventListener('change', updateSummary);
  patientInput?.addEventListener('input', updateSummary);
  createForm?.querySelectorAll('input[name="stage"]').forEach((i) => i.addEventListener('change', updateSummary));
  syncTeeth();

  // ---- Scan file (optional) ----
  const fileInput = $('co-initial-file');
  const fileLabel = $('cad-file-label');
  $('cad-btn-load')?.addEventListener('click', () => fileInput?.click());
  fileInput?.addEventListener('change', () => {
    const f = fileInput.files?.[0];
    if (f && fileLabel) fileLabel.textContent = `${f.name} (${fmt.format(Math.round(f.size / 1024))} KB)`;
  });

  // ---- Orders list ----
  async function loadList(q = '') {
    const res = await fetch(`/api/v1/orders${q ? `?q=${encodeURIComponent(q)}` : ''}`);
    if (!res.ok) return;
    const data = await res.json();
    const tbody = $('doc-orders-tbody');
    tbody.innerHTML = data.items?.length ? data.items.map((o) => `
      <tr>
        <td class="ltr mono"><strong>${esc(o.orderNumber)}</strong></td>
        <td>${esc(o.patientName || o.caseReference)}</td>
        <td>${esc(o.serviceName)} (${esc(o.toothNumbers)})</td>
        <td>${esc(o.laboratoryName)}</td>
        <td>${(o.stages || []).map((s) => `${esc(stageAr[s.stage] || s.stage)}: ${esc(statusAr[s.status] || s.status)}`).join(' · ')}</td>
        <td class="num">${fmtEgp(o.confirmedPriceEgp ?? o.estimatedPriceEgp)}</td>
        <td><span class="badge">${esc(statusAr[o.status] || o.status)}</span></td>
        <td><a class="btn btn-primary btn-sm" href="/doctor-order-details.html?orderNumber=${encodeURIComponent(o.orderNumber)}">فتح الحالة</a></td>
      </tr>`).join('') : `<tr><td colspan="8" class="muted">لا توجد طلبات مطابقة.</td></tr>`;
    translate(tbody);
  }

  $('order-search-form')?.addEventListener('submit', (e) => {
    e.preventDefault();
    loadList(qInput?.value || '');
  });

  // ---- Submit ----
  createForm?.addEventListener('submit', async (e) => {
    e.preventDefault();
    const alertBox = $('create-order-alert');
    const fail = (msg) => { alertBox.textContent = msg; alertBox.hidden = false; translate(alertBox); alertBox.scrollIntoView({ block: 'center', behavior: 'smooth' }); };
    alertBox.hidden = true;

    const patient = patientInput?.value.trim() || '';
    if (!patient) { fail('اكتب اسم المريض.'); patientInput?.focus(); return; }
    const teethStr = teethInput?.value.trim() || '';
    if (!teethStr) { fail('حدد سنًا واحدًا على الأقل من مخطط الأسنان.'); return; }
    const stages = [...createForm.querySelectorAll('input[name="stage"]:checked')].map((i) => i.value);
    if (!stages.length) { fail('اختر مرحلة تصنيع واحدة على الأقل.'); return; }
    const total = Number(priceInput?.value || 0);
    if (!(total > 0)) { fail('تعذّر حساب التكلفة. اختر نوع التركيبة والأسنان.'); return; }
    const courier = deliverySelect?.value === 'LabCourier';
    const addr = $('co-addr')?.value.trim() || '';
    if (courier && !addr) { fail('اكتب عنوان العيادة ليصل المندوب.'); $('co-addr')?.focus(); return; }

    const scanMode = $('co-scan-mode')?.value || '';
    const userNotes = createForm.instructions.value.trim();
    const instructions = [scanMode ? `طريقة الطبعة: ${scanMode}` : '', userNotes].filter(Boolean).join(' — ');

    const btn = $('cad-btn-save');
    btn.disabled = true;
    const payload = {
      laboratorySlug: labSelect.value,
      // The patient's name is stored as the case name: it is what the doctor searches by later.
      patientName: patient,
      caseReference: patient,
      serviceName: serviceSelect.value,
      toothNumbers: teethStr,
      shade: createForm.shade.value,
      instructions,
      // The API takes these enums as numbers (no string-enum converter is registered).
      stages: stages.map((st) => ({ Design: 1, Milling: 2, Layering: 3, Glazing: 4 })[st]),
      estimatedPriceEgp: total,
      deliveryMethod: courier ? 1 : 0,
      pickupAddress: courier ? addr : null,
      requiredDeliveryDate: dueDateInput?.value ? new Date(`${dueDateInput.value}T12:00:00`).toISOString() : null,
    };
    const res = await fetch('/api/v1/orders', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
    const out = await res.json().catch(() => ({}));
    if (!res.ok) { btn.disabled = false; fail(out.detail || 'تعذّر إنشاء الطلب.'); return; }

    if (fileInput?.files?.length) {
      const fd = new FormData();
      fd.append('file', fileInput.files[0]);
      await fetch(`/api/v1/orders/${encodeURIComponent(out.orderNumber)}/files`, { method: 'POST', body: fd });
    }
    window.location.href = `/doctor-order-details.html?orderNumber=${encodeURIComponent(out.orderNumber)}`;
  });

  await loadList(qInput?.value || '');
}

// ---- Order Details (Doctor & Lab) ----
async function initOrderDetails(isLabView) {
  const orderNumber = new URLSearchParams(window.location.search).get('orderNumber') || 'LS-000187';
  const alertBox = document.getElementById('order-action-alert');
  const showMsg = (msg, ok = true) => {
    if (!alertBox) return;
    alertBox.className = `alert ${ok ? 'alert-success' : 'alert-error'}`;
    alertBox.textContent = msg;
    alertBox.hidden = false;
  };

  async function refresh() {
    const res = await fetch(`/api/v1/orders/${encodeURIComponent(orderNumber)}`);
    if (!res.ok) {
      document.getElementById('order-header-box').innerHTML = `<p class="alert alert-error">الطلب غير موجود أو لا تملك صلاحية الوصول إليه.</p>`;
      return;
    }
    const o = await res.json();

    // Header & Acceptance / Price Revision Controls
    let actionButtons = '';
    if (isLabView && o.status === 'PendingLabReview') {
      actionButtons = `
        <div style="display:flex;flex-wrap:wrap;gap:var(--sp-2);margin-block-top:var(--sp-3)">
          <button class="btn btn-primary btn-sm" id="btn-accept-order">قبول الطلب وتثبيت السعر (${fmtEgp(o.estimatedPriceEgp)})</button>
          <button class="btn btn-ghost btn-sm" id="btn-propose-price">اقتراح سعر معدّل</button>
          <button class="btn btn-ghost btn-sm" id="btn-reject-order">رفض الطلب مع ذكر السبب</button>
        </div>`;
    } else if (!isLabView && o.status === 'PriceRevisionProposed') {
      actionButtons = `
        <div class="alert alert-info" style="margin-block-top:var(--sp-3);display:flex;justify-content:space-between;align-items:center;gap:var(--sp-3)">
          <div><strong>اقترح المعمل سعرًا معدلًا: ${fmtEgp(o.proposedPriceEgp)}</strong><div>السبب: ${esc(o.priceRevisionNote)}</div></div>
          <button class="btn btn-primary btn-sm" id="btn-approve-price">الموافقة وتثبيت السعر</button>
        </div>`;
    }

    const baseOrderPrice = Number(o.confirmedPriceEgp ?? o.estimatedPriceEgp ?? 0);
    const dentistFeeVal = Number(o.dentistCommissionEgp ?? (baseOrderPrice * 0.01));
    const roleFinanceSummary = isLabView
      ? `<div class="muted" style="font-size:var(--fs-xs)">السعر المؤكد للحالة</div>
         <strong class="num" style="font-size:var(--fs-2xl);color:var(--g-900)">${fmtEgp(baseOrderPrice)}</strong>
         <div class="muted" style="font-size:var(--fs-xs)">عمولة المنصة على المعمل: 2% (صافي المعمل 98%)</div>`
      : `<div class="muted" style="font-size:var(--fs-xs)">السعر المؤكد للحالة</div>
         <strong class="num" style="font-size:var(--fs-2xl);color:var(--g-900)">${fmtEgp(baseOrderPrice)}</strong>
         <div class="muted" style="font-size:var(--fs-xs)">عمولة الخدمة للطبيب (1%): <span class="num">${fmtEgp(dentistFeeVal)}</span></div>`;

    document.getElementById('order-header-box').innerHTML = `
      <div style="display:flex;flex-wrap:wrap;justify-content:space-between;gap:var(--sp-4);align-items:flex-start">
        <div>
          <span class="badge">${esc(statusAr[o.status] || o.status)}</span>
          <h1 style="font-size:var(--fs-2xl);margin-block-top:var(--sp-2)">طلب <span class="ltr mono">${esc(o.orderNumber)}</span> — ${esc(o.serviceName)}</h1>
          <p class="muted">اسم المريض: <strong>${esc(o.patientName || o.caseReference)}</strong> · الأسنان: <bdi class="ltr">${esc(o.toothNumbers)}</bdi> · اللون: <bdi class="ltr">${esc(o.shade)}</bdi></p>
          <p class="muted" style="font-size:var(--fs-sm)">الطبيب: ${esc(o.dentist?.name)} · المعمل: ${esc(o.laboratory?.name)}</p>
          ${o.instructions ? `<p style="margin-block-top:var(--sp-2);font-size:var(--fs-sm)"><strong>ملاحظات الحالة:</strong> ${esc(o.instructions)}</p>` : ''}
        </div>
        <div style="text-align:end">
          ${roleFinanceSummary}
        </div>
      </div>
      ${actionButtons}`;

    document.getElementById('btn-accept-order')?.addEventListener('click', async () => {
      const r = await fetch(`/api/v1/orders/${encodeURIComponent(orderNumber)}/accept`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' });
      if (r.ok) { showMsg('تم قبول الطلب وتوليد جدول الدفعات.'); refresh(); }
    });
    document.getElementById('btn-propose-price')?.addEventListener('click', async () => {
      const price = prompt('أدخل السعر المقترح بالجنيه المصري:', String(o.estimatedPriceEgp));
      if (!price) return;
      const note = prompt('اذكر سبب تعديل السعر للطبيب:', 'يتطلب دعامة إضافية');
      if (!note) return;
      const r = await fetch(`/api/v1/orders/${encodeURIComponent(orderNumber)}/propose-price`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ proposedPriceEgp: Number(price), note }),
      });
      if (r.ok) { showMsg('تم إرسال مقترح السعر لاعتماد الطبيب.'); refresh(); }
    });
    document.getElementById('btn-reject-order')?.addEventListener('click', async () => {
      const reason = prompt('أدخل سبب رفض الطلب:');
      if (!reason) return;
      const r = await fetch(`/api/v1/orders/${encodeURIComponent(orderNumber)}/reject`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ reason }),
      });
      if (r.ok) { showMsg('تم تسجيل رفض الطلب.'); refresh(); }
    });
    document.getElementById('btn-approve-price')?.addEventListener('click', async () => {
      const r = await fetch(`/api/v1/orders/${encodeURIComponent(orderNumber)}/approve-price`, { method: 'POST' });
      if (r.ok) { showMsg('تم اعتماد السعر النهائي وتثبيت الشروط التجارية.'); refresh(); }
    });

    // Stages Timeline
    const timeline = document.getElementById('order-stages-timeline');
    timeline.innerHTML = (o.stages || []).map((s) => {
      const isDone = s.status === 'Completed' || s.status === 'Approved';
      const isActive = ['InProgress', 'SubmittedForReview', 'RevisionRequested'].includes(s.status);
      let controls = '';
      if (isLabView) {
        if (['NotStarted', 'RevisionRequested', 'Blocked'].includes(s.status)) {
          controls += `<button class="btn btn-primary btn-sm" data-stage-act="${s.stage}" data-target="InProgress">بدء المرحلة</button>`;
        } else if (s.status === 'InProgress' && s.requiresDentistReview) {
          controls += `<button class="btn btn-primary btn-sm" data-stage-act="${s.stage}" data-target="SubmittedForReview">رفع لمراجعة الطبيب</button>`;
        } else if ((s.status === 'InProgress' && !s.requiresDentistReview) || s.status === 'Approved') {
          controls += `<button class="btn btn-primary btn-sm" data-stage-act="${s.stage}" data-target="Completed">إتمام المرحلة ✓</button>`;
        }
      } else if (!isLabView && s.status === 'SubmittedForReview') {
        controls += `
          <div style="display:flex;gap:6px;flex-wrap:wrap">
            <button class="btn btn-primary btn-sm" data-stage-act="${s.stage}" data-target="Approved">اعتماد التصميم ✓</button>
            <button class="btn btn-ghost btn-sm" data-stage-act="${s.stage}" data-target="RevisionRequested" data-need-reason="1">طلب تعديل</button>
          </div>`;
      }
      return `
        <article class="stage-node ${isDone ? 'is-completed' : ''} ${isActive ? 'is-active' : ''}">
          <div class="stage-node-head">
            <strong>${esc(stageAr[s.stage] || s.stage)}</strong>
            <span class="badge">${esc(statusAr[s.status] || s.status)}</span>
          </div>
          <p class="muted" style="font-size:var(--fs-xs)">${esc(s.notes || 'لا توجد ملاحظات مسجلة بعد.')}</p>
          ${s.lastRevisionReason ? `<div class="alert alert-error" style="font-size:var(--fs-xs)">سبب طلب التعديل: ${esc(s.lastRevisionReason)}</div>` : ''}
          ${controls}
        </article>`;
    }).join('');

    timeline.querySelectorAll('[data-stage-act]').forEach((btn) => {
      btn.addEventListener('click', async () => {
        const st = btn.dataset.stageAct;
        const targetStatus = btn.dataset.target;
        let reason = null;
        if (btn.dataset.needReason) {
          reason = prompt('أدخل سبب طلب التعديل الفني:');
          if (!reason) return;
        }
        const r = await fetch(`/api/v1/orders/${encodeURIComponent(orderNumber)}/stages/${st}/transition`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          // The API takes StageStatus as a number (no string-enum converter is registered).
          body: JSON.stringify({ targetStatus: { NotStarted: 0, InProgress: 1, SubmittedForReview: 2, RevisionRequested: 3, Approved: 4, Completed: 5, Blocked: 6 }[targetStatus], notes: `تم التحديث إلى ${statusAr[targetStatus] || targetStatus}`, reason }),
        });
        const out = await r.json().catch(() => ({}));
        if (!r.ok) { showMsg(out.detail || 'انتقال غير مسموح.', false); return; }
        showMsg(`تم تحديث مرحلة ${stageAr[st] || st} بنجاح.`);
        refresh();
      });
    });

    // Milestones
    const msBox = document.getElementById('order-milestones-box');
    msBox.innerHTML = (o.milestones || []).length ? o.milestones.map((m) => {
      const pct = m.percentage || (m.sequence === 1 ? 20 : 80);
      return `
      <div style="padding:var(--sp-3);border-radius:var(--r-md);background:var(--bg);border:1px solid var(--line);display:flex;justify-content:space-between;align-items:center;gap:var(--sp-2)">
        <div>
          <strong>الدفعة ${m.sequence} (${pct}%) — <span class="num">${fmtEgp(m.amountEgp)}</span></strong>
          <div class="muted" style="font-size:var(--fs-xs)">${esc(statusAr[m.status] || m.status)}</div>
        </div>
        ${(!isLabView && m.status === 'Due') ? `<button class="btn btn-primary btn-sm" data-pay-seq="${m.sequence}">ادفع الآن (${fmtEgp(m.amountEgp)})</button>` : `<span class="badge">${esc(statusAr[m.status] || m.status)}</span>`}
      </div>`;
    }).join('') : `<p class="muted" style="font-size:var(--fs-xs)">يتم إنشاء جدول الدفعتين (20% دفعة أولى + 80% دفعة نهائية) فور تثبيت السعر النهائي للطلب.</p>`;

    msBox.querySelectorAll('[data-pay-seq]').forEach((btn) => {
      btn.addEventListener('click', async () => {
        btn.classList.add('is-loading');
        const seq = Number(btn.dataset.paySeq);
        const chkRes = await fetch('/api/v1/payments/checkout', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ orderNumber, milestoneSequence: seq }),
        });
        const chk = await chkRes.json().catch(() => ({}));
        if (!chkRes.ok) { btn.classList.remove('is-loading'); showMsg(chk.detail || 'تعذّر بدء الدفع.', false); return; }
        const confirmRes = await fetch(`/api/v1/payments/sandbox-confirm/${encodeURIComponent(chk.checkoutId)}`, { method: 'POST' });
        btn.classList.remove('is-loading');
        if (confirmRes.ok) {
          showMsg(`تم تأكيد سداد الدفعة ${seq} عبر Webhook الخادم بنجاح.`);
          refresh();
        }
      });
    });

    // Files
    const filesBox = document.getElementById('order-files-list');
    filesBox.innerHTML = (o.files || []).length ? o.files.map((f) => `
      <div style="display:flex;justify-content:space-between;align-items:center;padding:8px 12px;background:var(--bg);border-radius:var(--r-sm);font-size:var(--fs-xs)">
        <span class="ltr mono">${esc(f.originalFileName)} (${fmt.format(Math.round(f.sizeBytes / 1024))} KB)</span>
        <a class="btn btn-ghost btn-sm" href="${esc(f.downloadUrl)}" download>تنزيل آمن</a>
      </div>`).join('') : `<p class="muted" style="font-size:var(--fs-xs)">لا توجد ملفات مرفوعة بعد.</p>`;

    // Chat
    const chatBox = document.getElementById('order-chat-messages');
    chatBox.innerHTML = (o.messages || []).map((m) => `
      <div class="chat-msg ${m.senderUserId === currentUser.id ? 'is-mine' : ''}">
        <div class="chat-msg-meta">${esc(m.senderName)}</div>
        <div>${esc(m.body)}</div>
      </div>`).join('');
    chatBox.scrollTop = chatBox.scrollHeight;

    // History
    const histBox = document.getElementById('order-history-list');
    histBox.innerHTML = (o.history || []).map((h) => `
      <div style="padding:6px 10px;border-inline-start:2px solid var(--g-700);background:var(--bg)">
        <strong>${esc(h.action)}</strong> ${h.reason ? `— ${esc(h.reason)}` : ''}
      </div>`).join('');
  }

  document.getElementById('order-chat-form')?.addEventListener('submit', async (e) => {
    e.preventDefault();
    const input = document.getElementById('order-chat-input');
    const body = input.value.trim();
    if (!body) return;
    input.value = '';
    const r = await fetch(`/api/v1/orders/${encodeURIComponent(orderNumber)}/messages`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ body }),
    });
    if (r.ok) refresh();
  });

  document.getElementById('file-upload-form')?.addEventListener('submit', async (e) => {
    e.preventDefault();
    const fileInput = document.getElementById('case-file-input');
    if (!fileInput.files?.length) return;
    const fd = new FormData();
    fd.append('file', fileInput.files[0]);
    const r = await fetch(`/api/v1/orders/${encodeURIComponent(orderNumber)}/files`, { method: 'POST', body: fd });
    const out = await r.json().catch(() => ({}));
    if (!r.ok) { showMsg(out.detail || 'فشل رفع الملف.', false); return; }
    fileInput.value = '';
    showMsg('تم رفع ملف الحالة وحمايته بنجاح.');
    refresh();
  });

  await refresh();
}

// ---- Doctor Finance ----
async function initDoctorFinance() {
  const alertBox = document.getElementById('doc-fin-alert');
  async function load() {
    const res = await fetch('/api/v1/finance/doctor');
    if (!res.ok) return;
    const d = await res.json();
    document.getElementById('df-confirmed').textContent = fmtEgp(d.totalConfirmedEgp);
    document.getElementById('df-paid').textContent = fmtEgp(d.totalPaidEgp);
    document.getElementById('df-due').textContent = fmtEgp(d.totalDueNowEgp);
    document.getElementById('df-remaining').textContent = fmtEgp(d.totalRemainingEgp);
    document.getElementById('df-comm') && (document.getElementById('df-comm').textContent = fmtEgp(d.dentistCommissionEgp || d.totalExpectedDentistCommissionEgp));

    const mBody = document.getElementById('df-milestones-tbody');
    mBody.innerHTML = (d.orders || []).map((o) => {
      const m1 = (o.milestones || []).find((m) => m.sequence === 1);
      const m2 = (o.milestones || []).find((m) => m.sequence === 2);
      const dueM = (o.milestones || []).find((m) => m.status === 'Due');
      const dFee = o.dentistCommissionEgp ?? (Number(o.confirmedPriceEgp || 0) * 0.01);
      return `
        <tr>
          <td class="ltr mono"><a href="/doctor-order-details.html?orderNumber=${encodeURIComponent(o.orderNumber)}">${esc(o.orderNumber)}</a></td>
          <td>${esc(o.serviceName)} <div class="muted" style="font-size:var(--fs-xs)">${esc(o.laboratoryName)}</div></td>
          <td class="num">${fmtEgp(o.confirmedPriceEgp)} <div class="muted" style="font-size:11px">+ ${fmtEgp(dFee)} عمولة 1%</div></td>
          <td>${m1 ? `<span class="num">${fmtEgp(m1.amountEgp)} (20%)</span> · <span class="badge">${esc(statusAr[m1.status] || m1.status)}</span>` : '—'}</td>
          <td>${m2 ? `<span class="num">${fmtEgp(m2.amountEgp)} (80%)</span> · <span class="badge">${esc(statusAr[m2.status] || m2.status)}</span>` : '—'}</td>
          <td>${dueM ? `<button class="btn btn-primary btn-sm" data-pay-order="${esc(o.orderNumber)}" data-pay-seq="${dueM.sequence}">سداد الدفعة ${dueM.sequence} (${fmtEgp(dueM.amountEgp)})</button>` : '<span class="muted">لا توجد دفعة مستحقة</span>'}</td>
        </tr>`;
    }).join('');

    mBody.querySelectorAll('[data-pay-order]').forEach((btn) => {
      btn.addEventListener('click', async () => {
        btn.classList.add('is-loading');
        const orderNumber = btn.dataset.payOrder;
        const milestoneSequence = Number(btn.dataset.paySeq);
        const chkRes = await fetch('/api/v1/payments/checkout', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ orderNumber, milestoneSequence }),
        });
        const chk = await chkRes.json().catch(() => ({}));
        if (chkRes.ok) {
          await fetch(`/api/v1/payments/sandbox-confirm/${encodeURIComponent(chk.checkoutId)}`, { method: 'POST' });
          if (alertBox) { alertBox.textContent = `تم سداد الدفعة ${milestoneSequence} للطلب ${orderNumber} بنجاح.`; alertBox.hidden = false; }
          await load();
        }
        btn.classList.remove('is-loading');
      });
    });

    const pBody = document.getElementById('df-payments-tbody');
    pBody.innerHTML = (d.payments || []).length ? d.payments.map((p) => `
      <tr>
        <td class="ltr mono">${esc(p.providerTransactionRef || p.providerCheckoutId)}</td>
        <td class="num">${fmtEgp(p.amountEgp)}</td>
        <td class="num">${fmtEgp(p.refundedEgp)}</td>
        <td><span class="badge">${esc(p.status)}</span></td>
        <td class="ltr">${p.confirmedAt ? new Date(p.confirmedAt).toLocaleString('ar-EG') : '—'}</td>
      </tr>`).join('') : `<tr><td colspan="5" class="muted">لا توجد معاملات بعد.</td></tr>`;
  }
  await load();
}

// ---- Lab Dashboard, Dedicated Price List Editor & Storefront Customizer ----
async function initLabDashboard() {
  if (currentUser.laboratoryName) {
    document.getElementById('greeting-title').textContent = `لوحة ${currentUser.laboratoryName}`;
  }
  if (currentUser.laboratorySlug) {
    const pubUrl = `/labs/${encodeURIComponent(currentUser.laboratorySlug)}`;
    document.getElementById('pricelist-preview-link') && (document.getElementById('pricelist-preview-link').href = pubUrl);
    document.getElementById('storefront-preview-link') && (document.getElementById('storefront-preview-link').href = pubUrl);
  }

  // Sidebar & Hash Tab Switching (#overview | #pricelist | #storefront)
  function syncLabDashboardTab() {
    const rawHash = (window.location.hash || '#overview').replace('#', '');
    const activeTab = ['overview', 'pricelist', 'storefront'].includes(rawHash) ? rawHash : 'overview';
    document.querySelectorAll('.dash-tab-pane').forEach((pane) => {
      pane.hidden = pane.id !== `tab-pane-${activeTab}`;
    });
    document.querySelectorAll('.dash-nav a[data-dash-tab]').forEach((a) => {
      if (a.dataset.dashTab === activeTab) a.setAttribute('aria-current', 'page');
      else a.removeAttribute('aria-current');
    });
  }
  window.addEventListener('hashchange', syncLabDashboardTab);
  document.querySelectorAll('[data-switch-tab]').forEach((btn) => {
    btn.addEventListener('click', (e) => {
      e.preventDefault();
      window.location.hash = btn.dataset.switchTab;
    });
  });
  syncLabDashboardTab();

  const [ordersRes, finRes] = await Promise.all([
    fetch('/api/v1/orders'),
    fetch('/api/v1/finance/lab'),
  ]);
  let orderItems = [];
  if (ordersRes.ok) {
    const oData = await ordersRes.json();
    orderItems = oData.items || [];
    document.getElementById('lab-st-orders').textContent = fmt.format(orderItems.length);
    const tbody = document.getElementById('lab-recent-orders');
    tbody.innerHTML = orderItems.length ? orderItems.slice(0, 8).map((o) => `
      <tr>
        <td class="ltr mono"><strong>${esc(o.orderNumber)}</strong></td>
        <td><strong>${esc(o.dentistName)}</strong></td>
        <td>${esc(o.serviceName)}</td>
        <td>${(o.stages || []).map((s) => `${esc(stageAr[s.stage] || s.stage)}`).join(' ← ')}</td>
        <td><span class="badge">${esc(statusAr[o.status] || o.status)}</span></td>
        <td><a class="btn btn-primary btn-sm" href="/lab-order-details.html?orderNumber=${encodeURIComponent(o.orderNumber)}">إدارة المراحل</a></td>
      </tr>`).join('') : `<tr><td colspan="6" class="muted">لا توجد طلبات بعد.</td></tr>`;
  }
  if (finRes.ok) {
    const fin = await finRes.json();
    const dentists = fin.dentistStatements || [];
    document.getElementById('lab-st-dentists') && (document.getElementById('lab-st-dentists').textContent = fmt.format(dentists.length));
    document.getElementById('lab-st-gross').textContent = fmtEgp(fin.grossPaidEgp);
    document.getElementById('lab-st-comm').textContent = fmtEgp(fin.totalCommissionEgp);
    document.getElementById('lab-st-net').textContent = fmtEgp(fin.availableToWithdrawEgp ?? fin.unsettledPayableEgp);

    // Render Revenue Chart
    const totalOrderVal = dentists.reduce((acc, d) => acc + Number(d.totalOrderEgp || 0), 0);
    renderBarChart('lab-revenue-chart', [
      { label: 'إجمالي قيمة الطلبات', value: totalOrderVal || 3600, display: fmtEgp(totalOrderVal) },
      { label: 'المحصل من الأطباء', value: fin.grossPaidEgp || 1800, display: fmtEgp(fin.grossPaidEgp), mint: true },
      { label: 'صافي أرباح المعمل (98%)', value: fin.netEarnedEgp || 1764, display: fmtEgp(fin.netEarnedEgp), mint: true },
      { label: 'رصيد المحفظة للسحب', value: (fin.availableToWithdrawEgp ?? fin.unsettledPayableEgp) || 1764, display: fmtEgp(fin.availableToWithdrawEgp ?? fin.unsettledPayableEgp) },
    ]);

    // Render Stages & Dentists Distribution Chart
    const allStages = orderItems.flatMap((o) => o.stages || []);
    renderProgressChart('lab-stages-chart', [
      { label: 'مراحل التصميم الرقمي (Design)', value: allStages.filter((s) => s.stage === 'Design').length || 2, display: `${fmt.format(allStages.filter((s) => s.stage === 'Design').length)} مرحلة` },
      { label: 'مراحل التفريز الخماسي (Milling)', value: allStages.filter((s) => s.stage === 'Milling').length || 2, display: `${fmt.format(allStages.filter((s) => s.stage === 'Milling').length)} مرحلة` },
      { label: 'مراحل الطبقات والجليز', value: allStages.filter((s) => ['Layering', 'Glazing'].includes(s.stage)).length || 2, display: `${fmt.format(allStages.filter((s) => ['Layering', 'Glazing'].includes(s.stage)).length)} مرحلة` },
      { label: 'الأطباء المتعاملون مع المعمل', value: dentists.length || 1, display: `${fmt.format(dentists.length)} أطباء` },
    ]);

    // Render Quick Dentists Cards on Lab Dashboard Overview
    const dentistsBox = document.getElementById('lab-overview-dentists');
    if (dentistsBox) {
      dentistsBox.innerHTML = dentists.length ? dentists.map((d) => `
        <article class="dentist-mini-card">
          <div style="display:flex;justify-content:space-between;align-items:flex-start;gap:8px">
            <div>
              <strong style="font-size:var(--fs-md);color:var(--g-900)">${esc(d.dentistName)}</strong>
              <div class="muted" style="font-size:var(--fs-xs)">${esc(d.clinicName || 'عيادة أسنان')} · ${esc(d.governorate || 'القاهرة')}</div>
            </div>
            <span class="badge badge-verified">${fmt.format(d.orderCount)} طلبات</span>
          </div>
          <div style="display:flex;justify-content:space-between;font-size:var(--fs-xs);padding-block-top:6px;border-block-start:1px solid var(--line)">
            <span>المدفوع: <b class="num" style="color:var(--g-800)">${fmtEgp(d.paidEgp)}</b></span>
            <span>المتبقي: <b class="num">${fmtEgp(d.remainingEgp)}</b></span>
          </div>
          <a class="btn btn-ghost btn-sm" href="/lab-finance.html?dentistId=${encodeURIComponent(d.dentistUserId)}#dentists">كشف حساب الطبيب التفصيلي ←</a>
        </article>`).join('') : `<p class="muted">لا يوجد أطباء متعاملون بعد.</p>`;
    }
  }

  const priceGroupsEl = document.getElementById('lab-editable-price-groups');
  const alertEl = document.getElementById('add-srv-alert');
  const sfAlertEl = document.getElementById('storefront-alert');
  const catEnMap = {
    'تركيبات الزركونيا والبورسلين': 'CROWN & BRIDGE',
    'تركيبات ثابتة': 'CROWN & BRIDGE',
    'زركونيا': 'ZIRCONIA CROWNS',
    'التركيبات التجميلية والإيماكس': 'AESTHETIC & E.MAX',
    'تجميل وفينير': 'AESTHETIC & E.MAX',
    'إيماكس': 'IPS E.MAX',
    'فينير': 'AESTHETIC VENEERS',
    'دعامات الزرعات والطباعة الرقمية': 'IMPLANTS & DIGITAL',
    'غرسات وزرعات': 'IMPLANTS & DIGITAL',
    'زرعات': 'IMPLANT ABUTMENTS',
    'أجهزة رقمية': 'DIGITAL APPLIANCES',
    'واقي ليلي': 'OCCLUSAL SPLINTS',
  };

  async function loadMyLabAndStorefront() {
    const r = await fetch('/api/v1/labs/my');
    if (!r.ok) return;
    const lab = await r.json();

    // Hydrate Storefront Editor Form
    const pf = document.getElementById('lab-profile-form');
    if (pf) {
      pf.name.value = lab.name || '';
      pf.workingHours.value = lab.workingHours || '';
      pf.description.value = lab.description || '';
      pf.governorate.value = lab.governorate || '';
      pf.city.value = lab.city || '';
      pf.address.value = lab.address || '';
      pf.serviceAreasCsv.value = Array.isArray(lab.serviceAreas) ? lab.serviceAreas.join('، ') : '';
      pf.offersCourier.checked = Boolean(lab.offersCourier);
      pf.offersPickup.checked = Boolean(lab.offersPickup);

      const syncBannerPreview = () => {
        document.getElementById('sf-preview-monogram') && (document.getElementById('sf-preview-monogram').textContent = getArabicMonogram(pf.name.value));
        document.getElementById('sf-preview-name') && (document.getElementById('sf-preview-name').textContent = pf.name.value || lab.name);
        document.getElementById('sf-preview-bio') && (document.getElementById('sf-preview-bio').textContent = pf.description.value || lab.description);
      };
      syncBannerPreview();
      pf.name.addEventListener('input', syncBannerPreview);
      pf.description.addEventListener('input', syncBannerPreview);
    }

    // Hydrate Equipment List in Storefront Editor
    const eqBox = document.getElementById('sf-equipment-list');
    if (eqBox) {
      const eqs = lab.equipment || [];
      eqBox.innerHTML = eqs.length ? eqs.map((e) => `
        <div style="display:flex;justify-content:space-between;align-items:center;padding:8px 12px;background:var(--bg);border:1px solid var(--line);border-radius:var(--r-sm);font-size:var(--fs-xs)">
          <div><strong>${esc(e.name)}</strong> · <span class="ltr mono">${esc(e.brandAndModel)}</span></div>
          <button type="button" class="btn btn-ghost btn-sm" data-del-eq="${esc(e.id)}" style="min-block-size:26px;padding:0 8px">حذف ✕</button>
        </div>`).join('') : `<p class="muted" style="font-size:var(--fs-xs)">لا توجد أجهزة مسجلة بعد.</p>`;

      eqBox.querySelectorAll('[data-del-eq]').forEach((b) => {
        b.addEventListener('click', async () => {
          await fetch(`/api/v1/labs/my/equipment/${encodeURIComponent(b.dataset.delEq)}`, { method: 'DELETE' });
          await loadMyLabAndStorefront();
        });
      });
    }

    // Hydrate Portfolio List in Storefront Editor
    const portBox = document.getElementById('sf-portfolio-list');
    if (portBox) {
      const ports = lab.portfolio || [];
      portBox.innerHTML = ports.length ? ports.map((p) => `
        <div style="display:flex;justify-content:space-between;align-items:center;padding:8px 12px;background:var(--bg);border:1px solid var(--line);border-radius:var(--r-sm);font-size:var(--fs-xs)">
          <div><strong>${esc(p.title)}</strong> (<span class="ltr">VITA ${esc(p.shade)}</span>) — <span class="muted">${esc(p.caption)}</span></div>
          <button type="button" class="btn btn-ghost btn-sm" data-del-port="${esc(p.id)}" style="min-block-size:26px;padding:0 8px">حذف ✕</button>
        </div>`).join('') : `<p class="muted" style="font-size:var(--fs-xs)">لا توجد نماذج أعمال مضافة بعد.</p>`;

      portBox.querySelectorAll('[data-del-port]').forEach((b) => {
        b.addEventListener('click', async () => {
          await fetch(`/api/v1/labs/my/portfolio/${encodeURIComponent(b.dataset.delPort)}`, { method: 'DELETE' });
          await loadMyLabAndStorefront();
        });
      });
    }

    // Hydrate Editable Price List
    if (!priceGroupsEl) return;
    const hoursEl = document.getElementById('lab-edit-hours');
    const handleEl = document.getElementById('lab-edit-handle');
    if (hoursEl && lab.workingHours) hoursEl.textContent = lab.workingHours;
    if (handleEl && lab.slug) handleEl.textContent = `@${lab.slug} · EGP`;

    const services = lab.services || [];
    if (!services.length) {
      priceGroupsEl.innerHTML = `<p class="muted" style="font-size:var(--fs-sm)">لا توجد بنود مضافة بعد. أضف أول خدمة من النموذج بالأسفل.</p>`;
      return;
    }

    const groups = new Map();
    for (const s of services) {
      const cat = s.category || 'تركيبات ثابتة';
      if (!groups.has(cat)) groups.set(cat, []);
      groups.get(cat).push(s);
    }

    priceGroupsEl.innerHTML = [...groups.entries()].map(([cat, list]) => {
      const en = catEnMap[cat] || 'LABORATORY SERVICES';
      const rows = list.map((s) => `
        <div class="price-sheet-row is-editable" data-srv-id="${esc(s.id)}" data-srv-cat="${esc(cat)}" data-srv-days="${s.estimatedDays || 3}">
          <input type="text" class="price-sheet-input-name" value="${esc(s.name)}" aria-label="اسم الخدمة">
          <span class="price-sheet-val">
            <input type="number" class="price-sheet-input-price" min="50" step="25" value="${Number(s.priceEgp)}" aria-label="سعر الخدمة بالجنيه المصري">
            <span>ج.م</span>
            <button type="button" class="btn btn-primary btn-sm" data-save-srv="${esc(s.id)}" style="min-block-size:28px;padding:0 10px;font-size:11px">حفظ</button>
            <button type="button" class="btn btn-ghost btn-sm" data-del-srv="${esc(s.id)}" style="min-block-size:28px;padding:0 8px;font-size:11px" title="حذف البند">✕</button>
          </span>
        </div>`).join('');
      return `
        <div class="price-sheet-group">
          <h3 class="price-sheet-cat"><span>${esc(cat)}</span><small class="ltr">${esc(en)}</small></h3>
          <div class="price-sheet-rows">${rows}</div>
        </div>`;
    }).join('');

    priceGroupsEl.querySelectorAll('[data-save-srv]').forEach((btn) => {
      btn.addEventListener('click', async () => {
        const row = btn.closest('.price-sheet-row');
        const id = btn.dataset.saveSrv;
        const name = row.querySelector('.price-sheet-input-name').value.trim();
        const priceEgp = Number(row.querySelector('.price-sheet-input-price').value);
        const category = row.dataset.srvCat;
        const estimatedDays = Number(row.dataset.srvDays || 3);
        const res = await fetch(`/api/v1/labs/my/services/${encodeURIComponent(id)}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ category, name, description: '', priceEgp, estimatedDays, stages: ['Design', 'Milling', 'Layering', 'Glazing'] }),
        });
        if (res.ok && alertEl) {
          alertEl.textContent = `تم حفظ تعديل "${name}" (${fmtEgp(priceEgp)}) في قائمة الأسعار الموحدة.`;
          alertEl.hidden = false;
        }
      });
    });

    priceGroupsEl.querySelectorAll('[data-del-srv]').forEach((btn) => {
      btn.addEventListener('click', async () => {
        const id = btn.dataset.delSrv;
        const res = await fetch(`/api/v1/labs/my/services/${encodeURIComponent(id)}`, { method: 'DELETE' });
        if (res.ok) {
          if (alertEl) { alertEl.textContent = 'تم حذف البند من قائمة الأسعار.'; alertEl.hidden = false; }
          await loadMyLabAndStorefront();
        }
      });
    });
  }

  await loadMyLabAndStorefront();

  // Save Storefront Profile Form
  const profileForm = document.getElementById('lab-profile-form');
  profileForm?.addEventListener('submit', async (e) => {
    e.preventDefault();
    const payload = {
      name: profileForm.name.value.trim(),
      description: profileForm.description.value.trim(),
      governorate: profileForm.governorate.value.trim(),
      city: profileForm.city.value.trim(),
      address: profileForm.address.value.trim(),
      serviceAreasCsv: profileForm.serviceAreasCsv.value.replace(/،/g, ',').trim(),
      workingHours: profileForm.workingHours.value.trim(),
      offersCourier: profileForm.offersCourier.checked,
      offersPickup: profileForm.offersPickup.checked,
    };
    const r = await fetch('/api/v1/labs/my/profile', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    });
    if (r.ok && sfAlertEl) {
      sfAlertEl.textContent = 'تم تحديث بيانات متجر وبروفايل المعمل ومواعيد العمل بنجاح ✓';
      sfAlertEl.hidden = false;
      await loadMyLabAndStorefront();
    }
  });

  // Add Equipment Form
  const addEqForm = document.getElementById('sf-add-equipment-form');
  addEqForm?.addEventListener('submit', async (e) => {
    e.preventDefault();
    const r = await fetch('/api/v1/labs/my/equipment', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        name: addEqForm.name.value.trim(),
        brandAndModel: addEqForm.brandAndModel.value.trim(),
      }),
    });
    if (r.ok) {
      addEqForm.reset();
      if (sfAlertEl) { sfAlertEl.textContent = 'تمت إضافة الجهاز إلى متجر المعمل ✓'; sfAlertEl.hidden = false; }
      await loadMyLabAndStorefront();
    }
  });

  // Add Portfolio Form
  const addPortForm = document.getElementById('sf-add-portfolio-form');
  addPortForm?.addEventListener('submit', async (e) => {
    e.preventDefault();
    const r = await fetch('/api/v1/labs/my/portfolio', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        title: addPortForm.title.value.trim(),
        shade: addPortForm.shade.value.trim(),
        caption: addPortForm.caption.value.trim(),
      }),
    });
    if (r.ok) {
      addPortForm.reset();
      if (sfAlertEl) { sfAlertEl.textContent = 'تمت إضافة العمل إلى معرض أعمال المعمل ✓'; sfAlertEl.hidden = false; }
      await loadMyLabAndStorefront();
    }
  });

  const srvForm = document.getElementById('add-service-form');
  srvForm?.addEventListener('submit', async (e) => {
    e.preventDefault();
    const payload = {
      name: srvForm.name.value.trim(),
      category: srvForm.category.value.trim(),
      priceEgp: Number(srvForm.priceEgp.value),
      estimatedDays: Number(srvForm.estimatedDays.value),
      description: '',
      stages: ['Design', 'Milling', 'Layering', 'Glazing'],
    };
    const r = await fetch('/api/v1/labs/my/services', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    });
    if (r.ok) {
      if (alertEl) {
        alertEl.textContent = 'تمت إضافة الخدمة لقائمة أسعار المعمل الموحدة بنجاح.';
        alertEl.hidden = false;
      }
      srvForm.reset();
      await loadMyLabAndStorefront();
    }
  });
}

async function initLabOrders() {
  const qInput = document.getElementById('order-search-q');
  const qs = new URLSearchParams(window.location.search);
  if (qs.get('q') && qInput) qInput.value = qs.get('q');

  async function load(q = '') {
    const res = await fetch(`/api/v1/orders${q ? `?q=${encodeURIComponent(q)}` : ''}`);
    if (!res.ok) return;
    const data = await res.json();
    const tbody = document.getElementById('lab-orders-tbody');
    tbody.innerHTML = data.items?.length ? data.items.map((o) => `
      <tr>
        <td class="ltr mono"><strong>${esc(o.orderNumber)}</strong></td>
        <td>${esc(o.dentistName)}</td>
        <td>${esc(o.patientName || o.caseReference)}</td>
        <td>${esc(o.serviceName)} (${esc(o.toothNumbers)})</td>
        <td>${(o.stages || []).map((s) => `${esc(stageAr[s.stage] || s.stage)}: ${esc(statusAr[s.status] || s.status)}`).join(' · ')}</td>
        <td class="num">${fmtEgp(o.confirmedPriceEgp ?? o.estimatedPriceEgp)}</td>
        <td><span class="badge">${esc(statusAr[o.status] || o.status)}</span></td>
        <td><a class="btn btn-primary btn-sm" href="/lab-order-details.html?orderNumber=${encodeURIComponent(o.orderNumber)}">إدارة الحالة</a></td>
      </tr>`).join('') : `<tr><td colspan="8" class="muted">لا توجد طلبات مطابقة.</td></tr>`;
  }
  document.getElementById('order-search-form')?.addEventListener('submit', (e) => {
    e.preventDefault();
    load(qInput?.value || '');
  });
  await load(qInput?.value || '');
}

// ---- Lab Finance, Per-Dentist Ledger, Wallet Withdrawal & Sponsored Campaigns ----
async function initLabFinance() {
  const alertBox = document.getElementById('lab-fin-alert');
  let lastOrders = [];
  let dentistStatements = [];

  function syncFinTab() {
    const rawHash = (window.location.hash || '#dentists').replace('#', '');
    const activeTab = ['dentists', 'wallet', 'campaigns'].includes(rawHash) ? rawHash : 'dentists';
    document.querySelectorAll('.fin-tab-pane').forEach((pane) => {
      pane.hidden = pane.id !== `fin-pane-${activeTab}`;
    });
    document.querySelectorAll('.dash-nav a[data-fin-tab]').forEach((a) => {
      if (a.dataset.finTab === activeTab) a.setAttribute('aria-current', 'page');
      else a.removeAttribute('aria-current');
    });
  }
  window.addEventListener('hashchange', syncFinTab);
  document.querySelectorAll('[data-switch-fin-tab]').forEach((b) => {
    b.addEventListener('click', () => {
      window.location.hash = b.dataset.switchFinTab;
    });
  });
  syncFinTab();

  function selectDentistStatement(dentistUserId) {
    const st = dentistStatements.find((d) => String(d.dentistUserId) === String(dentistUserId)) || dentistStatements[0];
    if (!st) return;
    document.getElementById('sdl-title') && (document.getElementById('sdl-title').textContent = `كشف حساب المعاملات المنفصل: ${st.dentistName} (${st.clinicName || 'عيادة خاصة'})`);
    document.getElementById('sdl-sub') && (document.getElementById('sdl-sub').textContent = `البريد: ${st.dentistEmail || '—'} · المحافظة: ${st.governorate || 'القاهرة'} · إجمالي الطلبات: ${st.orderCount}`);
    document.getElementById('sdl-count') && (document.getElementById('sdl-count').textContent = `${fmt.format(st.orderCount)} (${fmt.format(st.activeCount || 0)} نشط)`);
    document.getElementById('sdl-total') && (document.getElementById('sdl-total').textContent = fmtEgp(st.totalOrderEgp));
    document.getElementById('sdl-paid') && (document.getElementById('sdl-paid').textContent = fmtEgp(st.paidEgp));
    document.getElementById('sdl-rem') && (document.getElementById('sdl-rem').textContent = fmtEgp(st.remainingEgp));

    const tbody = document.getElementById('sdl-orders-tbody');
    if (tbody) {
      const dOrders = st.orders || [];
      tbody.innerHTML = dOrders.length ? dOrders.map((o) => `
        <tr>
          <td class="ltr mono"><a href="/lab-order-details.html?orderNumber=${encodeURIComponent(o.orderNumber)}"><strong>${esc(o.orderNumber)}</strong></a></td>
          <td>${esc(o.serviceName)} <div class="muted" style="font-size:var(--fs-xs)">مرجع: ${esc(o.patientName || o.caseReference)} · لون ${esc(o.shade)}</div></td>
          <td class="ltr">${o.createdAt ? new Date(o.createdAt).toLocaleDateString('ar-EG') : '—'}</td>
          <td><span class="badge">${esc(statusAr[o.status] || o.status)}</span></td>
          <td class="num">${fmtEgp(o.totalEgp)}</td>
          <td class="num">${fmtEgp(o.paidEgp)}</td>
          <td class="num">${fmtEgp(o.remainingEgp)}</td>
          <td class="num">${fmtEgp(o.platformCommissionEgp)}</td>
          <td class="num"><strong>${fmtEgp(o.labNetEgp)}</strong></td>
        </tr>`).join('') : `<tr><td colspan="9" class="muted">لا توجد طلبات لهذا الطبيب.</td></tr>`;
    }
  }

  async function loadFin(q = '') {
    const [finRes, subRes] = await Promise.all([
      fetch(`/api/v1/finance/lab${q ? `?q=${encodeURIComponent(q)}` : ''}`),
      fetch('/api/v1/subscriptions/my'),
    ]);
    if (finRes.ok) {
      const d = await finRes.json();
      lastOrders = d.orders || [];
      dentistStatements = d.dentistStatements || [];

      const avail = d.availableToWithdrawEgp ?? d.unsettledPayableEgp ?? 0;
      const pend = d.pendingWithdrawalEgp ?? 0;
      const settled = d.settledPaidToLabEgp ?? 0;

      document.getElementById('lf-gross').textContent = fmtEgp(d.grossPaidEgp);
      document.getElementById('lf-comm').textContent = fmtEgp(d.totalCommissionEgp);
      document.getElementById('lf-net').textContent = fmtEgp(d.netEarnedEgp);
      document.getElementById('lf-unsettled').textContent = fmtEgp(avail);
      document.getElementById('lf-settled') && (document.getElementById('lf-settled').textContent = fmtEgp(settled));
      document.getElementById('lf-pending-wd') && (document.getElementById('lf-pending-wd').textContent = fmtEgp(pend));

      document.getElementById('wallet-available-big') && (document.getElementById('wallet-available-big').textContent = fmtEgp(avail));
      document.getElementById('wallet-pending-big') && (document.getElementById('wallet-pending-big').textContent = fmtEgp(pend));
      document.getElementById('wallet-paid-big') && (document.getElementById('wallet-paid-big').textContent = fmtEgp(settled));

      document.getElementById('lf-dentists-count-badge') && (document.getElementById('lf-dentists-count-badge').textContent = `${fmt.format(dentistStatements.length)} أطباء متعاملين`);

      const dTbody = document.getElementById('lf-dentists-tbody');
      if (dTbody) {
        dTbody.innerHTML = dentistStatements.length ? dentistStatements.map((s) => `
          <tr>
            <td>
              <strong>${esc(s.dentistName)}</strong>
              <div class="muted" style="font-size:var(--fs-xs)">${esc(s.clinicName || 'عيادة خاصة')}</div>
            </td>
            <td>${esc(s.governorate || 'القاهرة')}</td>
            <td class="num">${fmt.format(s.orderCount)} <small class="muted">(${fmt.format(s.activeCount || 0)} نشط / ${fmt.format(s.completedCount || 0)} مكتمل)</small></td>
            <td class="num">${fmtEgp(s.totalOrderEgp)}</td>
            <td class="num">${fmtEgp(s.paidEgp)}</td>
            <td class="num">${fmtEgp(s.remainingEgp)}</td>
            <td class="num"><strong>${fmtEgp(s.labNetEgp)}</strong></td>
            <td><button type="button" class="btn btn-primary btn-sm" data-view-dentist="${esc(s.dentistUserId)}">كشف حساب الطبيب التفصيلي</button></td>
          </tr>`).join('') : `<tr><td colspan="8" class="muted">لا يوجد أطباء متعاملون بعد.</td></tr>`;

        dTbody.querySelectorAll('[data-view-dentist]').forEach((b) => {
          b.addEventListener('click', () => {
            selectDentistStatement(b.dataset.viewDentist);
            document.getElementById('single-dentist-ledger-card')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
          });
        });
      }

      const qsDentist = new URLSearchParams(window.location.search).get('dentistId');
      selectDentistStatement(qsDentist || dentistStatements[0]?.dentistUserId);

      // Settlements / Withdrawals table
      const stTbody = document.getElementById('lf-settlements-tbody');
      if (stTbody) {
        const settlements = d.settlements || [];
        stTbody.innerHTML = settlements.length ? settlements.map((s) => `
          <tr>
            <td class="num">${fmtEgp(s.grossEgp)}</td>
            <td class="num">${fmtEgp(s.commissionEgp)}</td>
            <td class="num"><strong>${fmtEgp(s.netPayableEgp)}</strong></td>
            <td><span class="badge ${s.status === 'Paid' ? 'badge-verified' : ''}">${s.status === 'Paid' ? 'تم التحويل البنكي ✓' : 'قيد المراجعة والتحويل'}</span></td>
            <td class="ltr mono" style="font-size:var(--fs-xs)">${esc(s.externalBankTransferRef || 'قيد المعالجة')}</td>
          </tr>`).join('') : `<tr><td colspan="5" class="muted">لا توجد طلبات سحب سابقة بعد.</td></tr>`;
      }

      document.getElementById('lf-orders-tbody').innerHTML = lastOrders.map((o) => `
        <tr>
          <td class="ltr mono"><a href="/lab-order-details.html?orderNumber=${encodeURIComponent(o.orderNumber)}">${esc(o.orderNumber)}</a></td>
          <td>${esc(o.dentistName)}</td>
          <td>${esc(o.serviceName)} (${esc(o.patientName || o.caseReference)})</td>
          <td class="num">${fmtEgp(o.totalEgp)}</td>
          <td class="num">${fmtEgp(o.paidEgp)}</td>
          <td class="num">${fmtEgp(o.remainingEgp)}</td>
          <td class="num">${fmtEgp(o.platformCommissionEgp)}</td>
          <td class="num"><strong>${fmtEgp(o.labNetEgp)}</strong></td>
        </tr>`).join('');
    }

    if (subRes.ok) {
      const sData = await subRes.json();
      const latest = (sData.subscriptions || [])[0];
      const subBox = document.getElementById('lab-sub-box');
      if (subBox) {
        subBox.innerHTML = latest ? `
          <div style="padding:var(--sp-3);background:var(--bg);border-radius:var(--r-md);border:1px solid var(--line)">
            <strong>حالة الاشتراك: ${latest.isTrial ? 'فترة تجريبية مجانية (شهر أول)' : 'اشتراك شهري مدفوع'} — <span class="badge">${esc(latest.status)}</span></strong>
            <div class="muted" style="font-size:var(--fs-xs)">ينتهي في: ${new Date(latest.endsAt).toLocaleDateString('ar-EG')} · قيمة التجديد الشهري: ${fmtEgp(sData.monthlyFeeEgp)}</div>
          </div>` : '';
      }

      // Sponsored Campaigns & Detailed Analytics
      const campaigns = sData.campaigns || [];
      const campListEl = document.getElementById('lab-campaigns-list');
      const totalImp = campaigns.reduce((a, c) => a + Number(c.impressions || 0), 0);
      const totalClk = campaigns.reduce((a, c) => a + Number(c.clicks || 0), 0);
      const totalDoc = campaigns.reduce((a, c) => a + Number(c.doctorsReached || 0), 0);
      const totalOrd = campaigns.reduce((a, c) => a + Number(c.ordersAttributed || 0), 0);

      renderBarChart('lab-campaigns-chart', [
        { label: 'ظهور الإعلان للأطباء', value: totalImp || 120, display: `${fmt.format(totalImp || 120)} مشاهدة` },
        { label: 'زيارات بروفايل المعمل', value: totalClk || 28, display: `${fmt.format(totalClk || 28)} زيارة`, mint: true },
        { label: 'أطباء تفاعلوا مع المتجر', value: totalDoc || 9, display: `${fmt.format(totalDoc || 9)} أطباء`, mint: true },
        { label: 'طلبات ناتجة عن الإعلان', value: totalOrd || 4, display: `${fmt.format(totalOrd || 4)} طلبات` },
      ]);

      if (campListEl) {
        campListEl.innerHTML = campaigns.length ? campaigns.map((c) => `
          <article class="campaign-admin-card" data-camp-id="${esc(c.id)}">
            <div style="display:flex;justify-content:space-between;align-items:flex-start;flex-wrap:wrap;gap:var(--sp-3)">
              <div>
                <div style="display:flex;align-items:center;gap:8px;flex-wrap:wrap">
                  <span class="badge ${c.isActive ? 'badge-verified' : ''}">${c.isActive ? 'نشط الآن في صدارة الدليل ✓' : 'متوقف مؤقتًا (Paused)'}</span>
                  <span class="badge badge-sponsored">موضع الظهور: ${esc(c.placement || 'DirectoryTop')}</span>
                </div>
                <h3 style="font-size:var(--fs-lg);margin-block-top:6px">${esc(c.title)}</h3>
                <p class="muted" style="font-size:var(--fs-xs)">
                  تاريخ البدء: ${new Date(c.startsAt).toLocaleDateString('ar-EG')} · ينتهي في: <strong>${new Date(c.EndsAt || c.endsAt).toLocaleDateString('ar-EG')}</strong> · الميزانية الكلية: <strong class="num">${fmtEgp(c.budgetEgp)}</strong>
                </p>
              </div>
              <div style="display:flex;flex-wrap:wrap;gap:6px">
                <button type="button" class="btn btn-primary btn-sm" data-camp-toggle="${esc(c.id)}" data-current-active="${c.isActive ? '1' : '0'}">${c.isActive ? 'إيقاف مؤقت للإعلان' : 'تشغيل واستئناف الإعلان ▶'}</button>
                <button type="button" class="btn btn-mint btn-sm" data-camp-extend="${esc(c.id)}">+ تمديد المدة والميزانية</button>
                <button type="button" class="btn btn-ghost btn-sm" data-camp-edit="${esc(c.id)}" data-camp-title="${esc(c.title)}">تعديل عنوان الإعلان</button>
              </div>
            </div>

            <div class="campaign-kpi-grid">
              <div class="campaign-kpi-box"><span>شاهد الإعلان (Impressions)</span><strong class="num">${fmt.format(c.impressions || 0)} طبيب</strong></div>
              <div class="campaign-kpi-box"><span>دخلوا البروفايل (Profile Visits)</span><strong class="num">${fmt.format(c.clicks || 0)} زيارة</strong></div>
              <div class="campaign-kpi-box"><span>نسبة النقر (CTR)</span><strong class="num">${Number(c.ctrPercent || 0).toFixed(1)}%</strong></div>
              <div class="campaign-kpi-box"><span>أطباء تواصلوا مع المعمل</span><strong class="num">${fmt.format(c.doctorsReached || 0)} دكاترة</strong></div>
              <div class="campaign-kpi-box"><span>طلبات محققة خلال الحملة</span><strong class="num">${fmt.format(c.ordersAttributed || 0)} طلبات</strong></div>
            </div>
          </article>`).join('') : `<p class="muted">لا توجد حملات إعلانية بعد. أطلق أول حملة من النموذج أعلاه.</p>`;

        campListEl.querySelectorAll('[data-camp-toggle]').forEach((btn) => {
          btn.addEventListener('click', async () => {
            const id = btn.dataset.campToggle;
            const currentlyActive = btn.dataset.currentActive === '1';
            const r = await fetch(`/api/v1/campaigns/${encodeURIComponent(id)}`, {
              method: 'PUT',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ isActive: !currentlyActive }),
            });
            if (r.ok) {
              alertBox.textContent = !currentlyActive ? 'تم تشغيل واستئناف الحملة الإعلانية فوراً.' : 'تم إيقاف الحملة الإعلانية مؤقتًا.';
              alertBox.hidden = false;
              loadFin();
            }
          });
        });

        campListEl.querySelectorAll('[data-camp-extend]').forEach((btn) => {
          btn.addEventListener('click', async () => {
            const id = btn.dataset.campExtend;
            const days = prompt('كم يومًا إضافيًا تريد تمديد الإعلان؟', '7');
            if (!days) return;
            const extraBudget = prompt('الميزانية الإضافية بالجنيه المصري (اختياري):', '500');
            const r = await fetch(`/api/v1/campaigns/${encodeURIComponent(id)}`, {
              method: 'PUT',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ extendDays: Number(days), additionalBudgetEgp: Number(extraBudget || 0) }),
            });
            if (r.ok) {
              alertBox.textContent = `تم تمديد الإعلان لمدة ${days} أيام إضافية بنجاح.`;
              alertBox.hidden = false;
              loadFin();
            }
          });
        });

        campListEl.querySelectorAll('[data-camp-edit]').forEach((btn) => {
          btn.addEventListener('click', async () => {
            const id = btn.dataset.campEdit;
            const newTitle = prompt('أدخل العنوان الجديد للحملة الإعلانية:', btn.dataset.campTitle || '');
            if (!newTitle) return;
            const r = await fetch(`/api/v1/campaigns/${encodeURIComponent(id)}`, {
              method: 'PUT',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ title: newTitle.trim() }),
            });
            if (r.ok) {
              alertBox.textContent = 'تم تحديث عنوان الحملة الإعلانية بنجاح.';
              alertBox.hidden = false;
              loadFin();
            }
          });
        });
      }
    }
  }

  document.getElementById('lab-fin-search-form')?.addEventListener('submit', (e) => {
    e.preventDefault();
    loadFin(document.getElementById('lab-fin-q')?.value || '');
  });

  // Lab Wallet Withdrawal Request
  const wdForm = document.getElementById('lab-withdraw-form');
  wdForm?.addEventListener('submit', async (e) => {
    e.preventDefault();
    const r = await fetch('/api/v1/finance/lab/withdraw', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        payoutMethod: wdForm.payoutMethod.value,
        accountDestination: wdForm.accountDestination.value.trim(),
      }),
    });
    const out = await r.json().catch(() => ({}));
    if (!r.ok) {
      alertBox.className = 'alert alert-error';
      alertBox.textContent = out.detail || 'لا يوجد رصيد متاح للسحب حاليًا.';
      alertBox.hidden = false;
      return;
    }
    alertBox.className = 'alert alert-success';
    alertBox.textContent = `تم تسجيل طلب سحب الأرباح بقيمة ${fmtEgp(out.netPayableEgp)} بنجاح (${out.externalBankTransferRef}).`;
    alertBox.hidden = false;
    wdForm.reset();
    await loadFin();
  });

  document.getElementById('renew-sub-btn')?.addEventListener('click', async () => {
    const r = await fetch('/api/v1/subscriptions/renew', { method: 'POST' });
    if (r.ok) {
      alertBox.className = 'alert alert-success';
      alertBox.textContent = 'تم تجديد الاشتراك الشهري للمعمل (2,000 ج.م) لمدة 30 يومًا إضافية.';
      alertBox.hidden = false;
      loadFin();
    }
  });

  const campForm = document.getElementById('create-campaign-form');
  campForm?.addEventListener('submit', async (e) => {
    e.preventDefault();
    const r = await fetch('/api/v1/campaigns', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        title: campForm.title.value.trim(),
        budgetEgp: Number(campForm.budgetEgp.value),
        durationDays: Number(campForm.durationDays.value),
      }),
    });
    if (r.ok) {
      alertBox.className = 'alert alert-success';
      alertBox.textContent = 'تم إطلاق وتفعيل حملة الظهور المُموَّل بنجاح.';
      alertBox.hidden = false;
      campForm.reset();
      await loadFin();
    }
  });

  document.getElementById('export-csv-btn')?.addEventListener('click', () => {
    const rows = [
      ['OrderNumber', 'Dentist', 'Service', 'TotalEGP', 'PaidEGP', 'RemainingEGP', 'Commission2PctEGP', 'LabNetEGP'],
      ...lastOrders.map((o) => [o.orderNumber, o.dentistName, o.serviceName, o.totalEgp, o.paidEgp, o.remainingEgp, o.platformCommissionEgp, o.labNetEgp]),
    ];
    const csv = '\uFEFF' + rows.map((r) => r.map((v) => `"${String(v ?? '').replace(/"/g, '""')}"`).join(',')).join('\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'labstage-financial-statement.csv';
    a.click();
  });

  await loadFin();
}

// ---- Admin Dashboard ----
async function initAdminDashboard() {
  const alertBox = document.getElementById('admin-alert');
  async function load() {
    const res = await fetch('/api/v1/admin/overview');
    if (!res.ok) return;
    const d = await res.json();
    document.getElementById('ad-collected').textContent = fmtEgp(d.metrics.totalCollectedEgp);
    document.getElementById('ad-comm').textContent = fmtEgp(d.metrics.totalCommissionRevenueEgp);
    document.getElementById('ad-subs').textContent = fmtEgp(d.metrics.totalSubscriptionRevenueEgp);
    document.getElementById('ad-gw-fee').textContent = fmtEgp(d.metrics.platformGatewayFeeExpenseEgp);

    renderBarChart('admin-revenue-chart', [
      { label: 'إجمالي التحصيل', value: d.metrics.totalCollectedEgp || 3600, display: fmtEgp(d.metrics.totalCollectedEgp) },
      { label: 'عمولات المنصة (2% + 1%)', value: d.metrics.totalCommissionRevenueEgp || 72, display: fmtEgp(d.metrics.totalCommissionRevenueEgp), mint: true },
      { label: 'إيراد الاشتراكات', value: d.metrics.totalSubscriptionRevenueEgp || 2000, display: fmtEgp(d.metrics.totalSubscriptionRevenueEgp), mint: true },
      { label: 'مصروف البوابة', value: d.metrics.platformGatewayFeeExpenseEgp || 54, display: fmtEgp(d.metrics.platformGatewayFeeExpenseEgp) },
    ]);

    renderProgressChart('admin-ops-chart', [
      { label: 'المعامل الموثقة النشطة', value: d.metrics.verifiedLabs || 1, display: `${fmt.format(d.metrics.verifiedLabs)} / ${fmt.format(d.metrics.totalLabs)} معامل` },
      { label: 'إجمالي الطلبات المسجلة', value: d.metrics.totalOrders || 1, display: `${fmt.format(d.metrics.totalOrders)} طلبات` },
      { label: 'إجمالي المستخدمين والحسابات', value: d.metrics.totalUsers || 1, display: `${fmt.format(d.metrics.totalUsers)} حسابات` },
      { label: 'دفعات التسوية البنكية للمعامل', value: (d.settlements || []).length || 1, display: `${fmt.format((d.settlements || []).length)} دفعات` },
    ]);

    const sf = document.getElementById('admin-settings-form');
    if (sf && d.settings) {
      sf.commissionRateBps.value = d.settings.commissionRateBps;
      sf.monthlySubscriptionEgp.value = d.settings.monthlySubscriptionEgp;
      sf.freeTrialDays.value = d.settings.freeTrialDays;
      sf.gatewayFeeBps.value = d.settings.gatewayFeeBps;
    }

    const labsTbody = document.getElementById('ad-labs-tbody');
    labsTbody.innerHTML = (d.laboratories || []).map((l) => `
      <tr>
        <td><strong>${esc(l.name)}</strong></td>
        <td>${esc(l.governorate)}</td>
        <td><span class="badge">${esc(l.verificationStatus)}</span></td>
        <td><span class="badge">${esc(l.subscriptionStatus)}</span></td>
        <td style="display:flex;gap:6px;flex-wrap:wrap">
          <button class="btn btn-primary btn-sm" data-verify-lab="${l.id}">توثيق ✓</button>
          <button class="btn btn-ghost btn-sm" data-settle-lab="${l.id}">إنشاء دفعة تسوية</button>
        </td>
      </tr>`).join('');

    labsTbody.querySelectorAll('[data-verify-lab]').forEach((b) => {
      b.addEventListener('click', async () => {
        await fetch(`/api/v1/admin/labs/${b.dataset.verifyLab}/verify`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ status: 1, note: 'Verified by Admin' }),
        });
        alertBox.textContent = 'تم توثيق المعمل وتسجيل العملية في سجل التدقيق.';
        alertBox.hidden = false;
        load();
      });
    });

    labsTbody.querySelectorAll('[data-settle-lab]').forEach((b) => {
      b.addEventListener('click', async () => {
        const r = await fetch('/api/v1/admin/settlements', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ laboratoryId: b.dataset.settleLab }),
        });
        const out = await r.json().catch(() => ({}));
        alertBox.textContent = r.ok ? `تم إنشاء دفعة تسوية بصافي ${fmtEgp(out.netPayableEgp)}.` : (out.detail || 'لا توجد مستحقات غير مسواة.');
        alertBox.hidden = false;
        load();
      });
    });

    const stTbody = document.getElementById('ad-settlements-tbody');
    stTbody.innerHTML = (d.settlements || []).length ? d.settlements.map((s) => `
      <tr>
        <td class="num">${fmtEgp(s.grossEgp)}</td>
        <td class="num">${fmtEgp(s.commissionEgp)}</td>
        <td class="num"><strong>${fmtEgp(s.netPayableEgp)}</strong></td>
        <td><span class="badge">${esc(s.status)}</span></td>
        <td>${s.status === 'Paid' ? `<span class="ltr mono">${esc(s.externalBankTransferRef)}</span>` : `<button class="btn btn-primary btn-sm" data-mark-paid="${s.id}">تأكيد تحويل بنكي</button>`}</td>
      </tr>`).join('') : `<tr><td colspan="5" class="muted">لا توجد دفعات تسوية بعد.</td></tr>`;

    stTbody.querySelectorAll('[data-mark-paid]').forEach((b) => {
      b.addEventListener('click', async () => {
        const ref = prompt('أدخل رقم مرجع التحويل البنكي الفعلي:', `BNK-EG-${Date.now()}`);
        if (!ref) return;
        await fetch(`/api/v1/admin/settlements/${b.dataset.markPaid}/mark-paid`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ externalBankTransferRef: ref }),
        });
        alertBox.textContent = 'تم تأكيد التحويل البنكي وإثبات قيد الصرف في دفتر الأستاذ.';
        alertBox.hidden = false;
        load();
      });
    });

    document.getElementById('ad-audit-list').innerHTML = (d.auditLogs || []).map((a) => `
      <div style="padding:6px 10px;background:var(--bg);border-inline-start:2px solid var(--g-800)">
        <strong>${esc(a.action)}</strong> (${esc(a.actorRole)}) — ${esc(a.details)}
      </div>`).join('');
  }

  document.getElementById('admin-settings-form')?.addEventListener('submit', async (e) => {
    e.preventDefault();
    const sf = e.target;
    const r = await fetch('/api/v1/admin/settings', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        commissionRateBps: Number(sf.commissionRateBps.value),
        monthlySubscriptionEgp: Number(sf.monthlySubscriptionEgp.value),
        freeTrialDays: Number(sf.freeTrialDays.value),
        gatewayFeeBps: Number(sf.gatewayFeeBps.value),
      }),
    });
    if (r.ok) {
      alertBox.textContent = 'تم تحديث إعدادات المنصة بنجاح (لا تتأثر الطلبات المثبتة سابقًا).';
      alertBox.hidden = false;
      load();
    }
  });

  await load();
}

(async () => {
  enhanceDashboard3DAndIcons();
  initPreferences();
  const user = await ensureAuthenticated();
  if (!user) return;
  if (page === 'doctor-dashboard') await initDoctorDashboard();
  else if (page === 'doctor-orders') await initDoctorOrders();
  else if (page === 'doctor-order-details') await initOrderDetails(false);
  else if (page === 'doctor-finance') await initDoctorFinance();
  else if (page === 'lab-dashboard') await initLabDashboard();
  else if (page === 'lab-orders') await initLabOrders();
  else if (page === 'lab-order-details') await initOrderDetails(true);
  else if (page === 'lab-finance') await initLabFinance();
  else if (page === 'admin-dashboard') await initAdminDashboard();
  if (getLang() === 'en') applyDomTranslations(document.body);
})();

// ---- Phones: each table cell carries its column title, so CSS can show rows as cards ----
// Behaviour only: copies the existing <th> text onto the cells (also after re-renders and translation).
(() => {
  let queued = false;
  const label = () => {
    queued = false;
    document.querySelectorAll('.data-table').forEach((t) => {
      const heads = [...t.querySelectorAll('thead th')].map((th) => th.textContent.trim());
      t.querySelectorAll('tbody tr').forEach((tr) => {
        [...tr.children].forEach((td, i) => {
          const h = td.hasAttribute('colspan') ? '' : (heads[i] || '');
          if (td.getAttribute('data-label') !== h) td.setAttribute('data-label', h);
        });
      });
    });
  };
  new MutationObserver(() => { if (!queued) { queued = true; requestAnimationFrame(label); } })
    .observe(document.body, { childList: true, subtree: true, characterData: true });
  label();
})();
