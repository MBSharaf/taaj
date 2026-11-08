import { applyDomTranslations, getLang } from '../core/i18n.js?v=8';

// Progressive enhancement for /labs and /lab-profile.html connected to /api/v1/labs.
const grid = document.getElementById('labs-grid');
const filterForm = document.getElementById('labs-filter-form');
const sortSelect = document.getElementById('f-sort');
const countEl = document.getElementById('labs-count');
const locateBtn = document.getElementById('locate-me-btn');
const fmt = new Intl.NumberFormat('ar-EG');

let userCoords = null;

function escapeHtml(s) {
  return String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

function getArabicMonogram(name) {
  const words = String(name || 'معمل').trim().split(/\s+/).map((w) => w.replace(/^ال/, ''));
  const first = words[0]?.[0] || 'م';
  const second = words[1]?.[0] || words[0]?.[1] || 'ع';
  return `${first}‌${second}`;
}

function attach3DTilt(root = document) {
  root.querySelectorAll('.card-3d-tilt, .lab-card').forEach((card) => {
    if (card.dataset.tiltBound) return;
    card.dataset.tiltBound = '1';
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
        card.style.setProperty('--rx', `${((0.5 - py) * 7).toFixed(2)}deg`);
        card.style.setProperty('--ry', `${((px - 0.5) * 7).toFixed(2)}deg`);
      });
    }, { passive: true });
    card.addEventListener('pointerleave', () => {
      card.style.setProperty('--rx', '0deg');
      card.style.setProperty('--ry', '0deg');
    });
  });
}

function renderLabCards(items) {
  if (!grid) return;
  if (countEl) countEl.textContent = fmt.format(items.length);
  if (!items.length) {
    grid.innerHTML = `<div class="card empty-state" style="grid-column:1/-1"><h3>لا توجد معامل مطابقة لهذه التصفية</h3><p>جرّب توسيع نطاق البحث أو إعادة ضبط المرشحات.</p></div>`;
    return;
  }
  grid.innerHTML = items.map((lab) => {
    const initials = escapeHtml(getArabicMonogram(lab.name));
    const services = (lab.services || []).slice(0, 2).map((s) =>
      `<div><span>${escapeHtml(s.name)}</span><b class="num">${fmt.format(s.priceEgp)} ج.م</b></div>`).join('');
    const dist = lab.distanceKm != null ? ` · <span class="num">${lab.distanceKm.toFixed(1)} كم</span>` : '';
    return `
      <article class="card lab-card card-lift card-3d-tilt ${lab.isSponsored ? 'is-sponsored' : ''}" data-lab-slug="${escapeHtml(lab.slug)}">
        <div>
          <div class="lab-card-head">
            <div class="lab-avatar lab-avatar-3d" aria-hidden="true">${initials}</div>
            <div class="lab-title-wrap">
              <h3><a href="/labs/${encodeURIComponent(lab.slug)}">${escapeHtml(lab.name)}</a></h3>
              <p class="lab-loc">${escapeHtml(lab.governorate)} · ${escapeHtml(lab.city)}${dist}</p>
            </div>
          </div>
          <div class="lab-badges" style="margin-block-start:var(--sp-3)">
            ${lab.isSponsored ? '<span class="badge badge-sponsored" title="ظهور إعلاني مدفوع — لا يغير التقييم الفعلي">مُموَّل</span>' : ''}
            ${lab.isVerified ? '<span class="badge badge-verified">موثّق ✓</span>' : '<span class="badge">قيد المراجعة</span>'}
            ${lab.offersCourier ? '<span class="badge">مندوب استلام وتسليم</span>' : ''}
          </div>
          <div class="lab-services-preview" style="margin-block-start:var(--sp-3)">${services}</div>
        </div>
        <div class="lab-card-foot">
          <span class="rating-chip">★ <span class="num">${Number(lab.averageRating || 0).toFixed(1)}</span> <small>(<span class="num">${fmt.format(lab.reviewCount || 0)}</span> تقييمًا موثقًا)</small></span>
          <a class="btn btn-primary btn-sm" href="/labs/${encodeURIComponent(lab.slug)}">عرض المتجر والطلب</a>
        </div>
      </article>`;
  }).join('');
  attach3DTilt(grid);
  if (getLang() === 'en') applyDomTranslations(grid);
}

async function fetchLabs() {
  if (!filterForm || !grid) return;
  const params = new URLSearchParams();
  const fd = new FormData(filterForm);
  for (const [k, v] of fd.entries()) {
    if (v) params.append(k, String(v));
  }
  if (sortSelect?.value) params.set('sort', sortSelect.value);
  if (userCoords) {
    params.set('lat', String(userCoords.lat));
    params.set('lng', String(userCoords.lng));
  }
  try {
    const res = await fetch(`/api/v1/labs?${params.toString()}`);
    if (!res.ok) return;
    const data = await res.json();
    if (Array.isArray(data.items)) renderLabCards(data.items);
  } catch {
    // Keep server-rendered cards if offline
  }
}

if (filterForm) {
  const qs = new URLSearchParams(window.location.search);
  if (qs.get('q') && filterForm.q) filterForm.q.value = qs.get('q');
  if (qs.get('gov') && filterForm.gov) filterForm.gov.value = qs.get('gov');
  filterForm.addEventListener('submit', (e) => { e.preventDefault(); fetchLabs(); });
  document.getElementById('labs-reset')?.addEventListener('click', () => { setTimeout(fetchLabs, 20); });
  sortSelect?.addEventListener('change', fetchLabs);
  locateBtn?.addEventListener('click', () => {
    if (!navigator.geolocation) return;
    locateBtn.classList.add('is-loading');
    navigator.geolocation.getCurrentPosition(
      (pos) => {
        locateBtn.classList.remove('is-loading');
        userCoords = { lat: pos.coords.latitude, lng: pos.coords.longitude };
        if (sortSelect) sortSelect.value = 'distance';
        fetchLabs();
      },
      () => { locateBtn.classList.remove('is-loading'); }
    );
  });
  fetchLabs();
}

// Mount 3D Crown on Lab Storefront Profile + Hydrate Role-Aware Viewer Mode & Storefront Sections
const profileServicesRoot = document.getElementById('lab-services');
if (profileServicesRoot) {
  attach3DTilt(document);

  const crownHost = document.getElementById('profile-3d-crown-host');
  let crownCtrl = null;
  if (crownHost) {
    const idle = window.requestIdleCallback || ((cb) => setTimeout(cb, 350));
    idle(async () => {
      try {
        const { mountCrown } = await import('./crown3d.js?v=6');
        crownCtrl = mountCrown(crownHost, { reducedMotion: false });
        crownCtrl.setStage(0.2);
      } catch {
        // Fallback SVG remains visible
      }
    });
    crownHost.querySelectorAll('[data-stage-idx]').forEach((btn) => {
      btn.addEventListener('click', (e) => {
        e.stopPropagation();
        const idx = Number(btn.dataset.stageIdx || 0);
        crownHost.querySelectorAll('[data-stage-idx]').forEach((b) => b.classList.toggle('is-active', b === btn));
        crownCtrl?.setStage(idx);
      });
    });
  }

  const pathParts = window.location.pathname.split('/').filter(Boolean);
  // /labs/{slug} on the server; ?slug= on the static demo (GitHub Pages)
  const slug = pathParts[0] === 'labs' && pathParts[1] ? pathParts[1]
    : (new URLSearchParams(window.location.search).get('slug') || 'cairo-digital-dental-lab');

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

  Promise.all([
    fetch(`/api/v1/labs/${encodeURIComponent(slug)}`).then((r) => (r.ok ? r.json() : null)).catch(() => null),
    fetch('/api/v1/auth/me').then((r) => (r.ok ? r.json() : null)).catch(() => null),
  ]).then(([lab, me]) => {
    // 1. Role-aware differentiation: Is the viewer the Lab Owner, a Dentist, or an Admin?
    const modeBar = document.getElementById('profile-viewer-mode-bar');
    const modePill = document.getElementById('viewer-mode-pill');
    const modeText = document.getElementById('viewer-mode-text');
    const modeActions = document.getElementById('viewer-mode-actions');
    const dentistActions = document.getElementById('profile-dentist-actions');
    const ownerActions = document.getElementById('profile-owner-actions');

    const isOwnerOfThisLab = Boolean(
      me && (me.laboratorySlug === slug || (lab && me.id && lab.ownerUserId === me.id))
    );
    const isAnotherLabUser = Boolean(
      me && !isOwnerOfThisLab && (me.role === 'LaboratoryOwner' || me.role === 'LaboratoryStaff')
    );

    if (isOwnerOfThisLab) {
      if (modeBar) modeBar.className = 'viewer-mode-bar is-owner';
      if (modePill) modePill.textContent = 'وضع صاحب المعمل (معاينة متجرك)';
      if (modeText) modeText.textContent = 'أنت تشاهد الآن متجر معملك العام كما يراه الأطباء في السوق. يمكنك تعديل الشعار، الوصف، الأجهزة، معرض الأعمال، وقائمة الأسعار من لوحة المعمل.';
      if (modeActions) {
        modeActions.innerHTML = `
          <a class="btn btn-mint btn-sm" href="/lab-dashboard.html#storefront">تعديل المتجر والأجهزة</a>
          <a class="btn btn-ghost btn-sm" href="/lab-dashboard.html#pricelist" style="color:#fff;border-color:rgba(255,255,255,.35)">تعديل الـ Price List</a>
          <a class="btn btn-primary btn-sm" href="/lab-dashboard.html">الرجوع للوحة المعمل ←</a>`;
      }
      if (dentistActions) dentistActions.hidden = true;
      if (ownerActions) ownerActions.hidden = false;
    } else if (me && me.role === 'Dentist') {
      if (modeBar) modeBar.className = 'viewer-mode-bar is-dentist';
      if (modePill) modePill.textContent = `وضع الطبيب · ${me.fullName}`;
      if (modeText) modeText.textContent = 'يمكنك إرسال طلب حالة جديد مباشرةً لهذا المعمل أو فتح محادثة فنية، مع سداد 20% فقط دفعة أولى عند اعتماد التصميم.';
      if (modeActions) {
        modeActions.innerHTML = `
          <a class="btn btn-primary btn-sm" href="/doctor-orders.html?newOrder=1&lab=${encodeURIComponent(slug)}">+ طلب حالة جديد من المعمل</a>
          <a class="btn btn-ghost btn-sm" href="/doctor-dashboard.html">العودة للوحة الطبيب ←</a>`;
      }
      if (dentistActions) dentistActions.hidden = false;
      if (ownerActions) ownerActions.hidden = true;
    } else if (isAnotherLabUser) {
      if (modePill) modePill.textContent = 'وضع الزملاء (حساب معمل)';
      if (modeText) modeText.textContent = 'أنت تتصفح ملف معمل زميل في الدليل العام.';
      if (modeActions) {
        modeActions.innerHTML = `<a class="btn btn-primary btn-sm" href="/lab-dashboard.html">الرجوع للوحة معملي ←</a>`;
      }
      if (dentistActions) dentistActions.hidden = true;
    }

    if (!lab) return;

    // 2. Hydrate Header, Hours, Equipment & Portfolio
    document.getElementById('lab-name') && (document.getElementById('lab-name').textContent = lab.name);
    document.getElementById('lab-bio') && (document.getElementById('lab-bio').textContent = lab.description);
    document.getElementById('lab-gov-crumb') && (document.getElementById('lab-gov-crumb').textContent = lab.governorate);
    document.getElementById('lab-initials') && (document.getElementById('lab-initials').textContent = getArabicMonogram(lab.name));
    document.getElementById('lab-rating') && (document.getElementById('lab-rating').textContent = Number(lab.averageRating || 4.9).toFixed(1));
    document.getElementById('hero-rating-val') && (document.getElementById('hero-rating-val').textContent = `${Number(lab.averageRating || 4.9).toFixed(1)} / 5.0`);
    document.getElementById('lab-review-count') && (document.getElementById('lab-review-count').textContent = fmt.format(lab.reviewCount || 0));
    document.getElementById('lab-hours') && lab.workingHours && (document.getElementById('lab-hours').textContent = lab.workingHours);
    document.getElementById('lab-areas') && lab.serviceAreas?.length && (document.getElementById('lab-areas').textContent = lab.serviceAreas.join('، '));
    document.getElementById('lab-delivery') && (document.getElementById('lab-delivery').textContent =
      lab.offersCourier && lab.offersPickup ? 'استلام من المعمل أو عبر مندوب المعمل' : lab.offersCourier ? 'مندوب توصيل للعيادة' : 'استلام من مقر المعمل');

    const orderCta = document.getElementById('order-cta');
    const chatCta = document.getElementById('chat-cta');
    if (orderCta) orderCta.href = `/doctor-orders.html?newOrder=1&lab=${encodeURIComponent(lab.slug)}`;
    if (chatCta) chatCta.href = `/doctor-dashboard.html?chatLab=${encodeURIComponent(lab.slug)}`;

    const hoursEl = document.getElementById('price-sheet-hours');
    const handleEl = document.getElementById('price-sheet-handle');
    if (hoursEl && lab.workingHours) hoursEl.textContent = lab.workingHours;
    if (handleEl && lab.slug) handleEl.textContent = `@${lab.slug} · EGP`;

    // Equipment list
    const eqList = document.getElementById('lab-equipment');
    if (eqList && Array.isArray(lab.equipment) && lab.equipment.length) {
      eqList.innerHTML = lab.equipment.map((e) =>
        `<li><strong>${escapeHtml(e.name)}</strong><span>${escapeHtml(e.brandAndModel)}</span></li>`).join('');
    }

    // Portfolio showcase
    const portGrid = document.getElementById('lab-portfolio');
    if (portGrid && Array.isArray(lab.portfolio) && lab.portfolio.length) {
      portGrid.innerHTML = lab.portfolio.map((p) => `
        <figure class="portfolio-item card-3d-tilt">
          <div class="portfolio-thumb">
            <span class="portfolio-3d-tag">VITA ${escapeHtml(p.shade || 'A2')}</span>
            <strong>${escapeHtml(p.title)}</strong>
          </div>
          <figcaption>${escapeHtml(p.caption || 'حالة رقمية منفذة بتقنية CAD/CAM')}</figcaption>
        </figure>`).join('');
      attach3DTilt(portGrid);
    }

    // Price list
    if (Array.isArray(lab.services) && lab.services.length) {
      const groups = new Map();
      for (const s of lab.services) {
        const cat = s.category || 'تركيبات ثابتة';
        if (!groups.has(cat)) groups.set(cat, []);
        groups.get(cat).push(s);
      }

      profileServicesRoot.innerHTML = [...groups.entries()].map(([cat, list]) => {
        const en = catEnMap[cat] || 'LABORATORY SERVICES';
        const rows = list.map((s) => `
          <div class="price-sheet-row">
            <div class="price-sheet-name">
              <span>${escapeHtml(s.name)}</span>
              <span class="price-sheet-meta">${fmt.format(s.estimatedDays || 3)} أيام</span>
            </div>
            <span class="price-sheet-val num">${fmt.format(s.priceEgp)} ج.م</span>
          </div>`).join('');
        return `
          <div class="price-sheet-group">
            <h3 class="price-sheet-cat"><span>${escapeHtml(cat)}</span><small class="ltr">${escapeHtml(en)}</small></h3>
            <div class="price-sheet-rows">${rows}</div>
          </div>`;
      }).join('');
    }
  });
}
