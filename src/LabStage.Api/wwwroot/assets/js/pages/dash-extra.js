// Lab finance lock (password modal + top-bar chip), lab finance settings, doctor clinics, order tracker.
// Behaviour only: every text shown here is written in the HTML (hidden until needed).
import { applyDomTranslations, getLang } from '../core/i18n.js?v=22';

const translate = (el) => { if (el && getLang() === 'en') applyDomTranslations(el); };
const LAB_ROLES = ['LaboratoryOwner', 'LaboratoryStaff'];
const rawFetch = window.fetch.bind(window);
const jsonPost = (url, body) => rawFetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body ?? {}) });

let finStatus = null;
let finPrompt = null;
let isLabUser = false;

/** Reads the error text the API sent (ProblemDetails.detail). */
async function problemText(res, fallback) {
  const out = await res.json().catch(() => ({}));
  return out.detail || out.title || fallback;
}

async function loadFinStatus() {
  const r = await rawFetch('/api/v1/labs/my/finance-lock').catch(() => null);
  finStatus = r?.ok ? await r.json() : null;
  syncChip();
  return finStatus;
}

function syncChip() {
  const chip = document.querySelector('[data-fin-chip]');
  if (!chip || !finStatus) return;
  chip.hidden = false;
  chip.dataset.state = finStatus.unlocked ? 'open' : 'locked';
  const label = chip.querySelector(finStatus.unlocked ? '.txt-open' : '.txt-locked');
  chip.setAttribute('aria-label', label?.textContent.trim() || '');
}

/**
 * Shows the finance password modal. Resolves true once unlocked (or after first-time setup), false on cancel.
 * required: cancelling leaves the page (used by pages of the finance section).
 */
export function askFinancePin({ required = false } = {}) {
  const modal = document.getElementById('fin-modal');
  if (!modal) return Promise.resolve(false);
  if (finPrompt) return finPrompt;
  finPrompt = (async () => {
    const st = finStatus || await loadFinStatus();
    const view = st?.hasPin ? 'unlock' : st?.isOwner ? 'setup' : 'nopin';
    modal.querySelectorAll('[data-fin-view]').forEach((v) => { v.hidden = v.dataset.finView !== view; });
    modal.querySelectorAll('.fin-error').forEach((e) => { e.textContent = ''; });
    modal.querySelectorAll('form').forEach((f) => f.reset());
    modal.hidden = false;
    document.body.style.overflow = 'hidden';
    translate(modal);
    setTimeout(() => modal.querySelector(`[data-fin-view="${view}"] input`)?.focus(), 60);

    return new Promise((resolve) => {
      const close = (ok) => {
        modal.hidden = true;
        document.body.style.overflow = '';
        modal.querySelectorAll('[data-fin-cancel]').forEach((b) => { b.onclick = null; });
        modal.querySelectorAll('form').forEach((f) => { f.onsubmit = null; });
        document.removeEventListener('keydown', onKey);
        finPrompt = null;
        if (!ok && required) { window.location.href = '/lab-dashboard.html#overview'; return; }
        resolve(ok);
      };
      const onKey = (e) => { if (e.key === 'Escape') close(false); };
      document.addEventListener('keydown', onKey);
      modal.querySelectorAll('[data-fin-cancel]').forEach((b) => { b.onclick = () => close(false); });

      const unlockForm = modal.querySelector('[data-fin-form="unlock"]');
      unlockForm.onsubmit = async (e) => {
        e.preventDefault();
        const err = unlockForm.querySelector('.fin-error');
        const pin = unlockForm.pin.value.trim();
        if (!pin) { unlockForm.pin.focus(); return; }
        const btn = unlockForm.querySelector('[type="submit"]');
        btn.disabled = true;
        const r = await jsonPost('/api/v1/labs/my/finance-lock/unlock', { pin });
        btn.disabled = false;
        if (!r.ok) { err.textContent = await problemText(r, 'كلمة مرور المالية غير صحيحة.'); translate(err); unlockForm.pin.select(); return; }
        await loadFinStatus();
        close(true);
      };

      const setupForm = modal.querySelector('[data-fin-form="setup"]');
      setupForm.onsubmit = async (e) => {
        e.preventDefault();
        const err = setupForm.querySelector('.fin-error');
        const newPin = setupForm.newPin.value.trim();
        if (newPin.length < 4) { err.textContent = 'كلمة مرور المالية 4 أحرف أو أرقام على الأقل.'; translate(err); return; }
        if (newPin !== setupForm.confirmPin.value.trim()) { err.textContent = 'تأكيد كلمة المرور غير مطابق.'; translate(err); return; }
        const r = await jsonPost('/api/v1/labs/my/finance-lock/setup', { accountPassword: setupForm.accountPassword.value, newPin });
        if (!r.ok) { err.textContent = await problemText(r, 'تعذّر حفظ كلمة المرور.'); translate(err); return; }
        await loadFinStatus();
        close(true);
      };
    });
  })();
  return finPrompt;
}

/**
 * Lab users: money stays hidden until the finance password is entered.
 * - an action refused with 423 (withdraw, price edits...) opens the modal and is retried once unlocked;
 * - pages of the finance section ask first; the top-bar chip opens or locks.
 */
export async function installFinanceGate(user, page) {
  isLabUser = LAB_ROLES.includes(user?.role);
  if (!isLabUser) return;
  window.fetch = async (input, init) => {
    const res = await rawFetch(input, init);
    const method = (init?.method || 'GET').toUpperCase();
    if (res.status !== 423 || method === 'GET') return res;
    if (!await askFinancePin()) return res;
    return rawFetch(input, init);
  };

  const st = await loadFinStatus();
  document.querySelector('[data-fin-chip]')?.addEventListener('click', async () => {
    if (finStatus?.unlocked) {
      await jsonPost('/api/v1/labs/my/finance-lock/lock');
      window.location.reload();
    } else if (await askFinancePin()) {
      window.location.reload();
    }
  });

  // Finance pages (and the price list) open behind the password.
  const finPage = () => page === 'lab-finance' || (page === 'lab-dashboard' && location.hash === '#pricelist');
  const guard = async () => {
    if (!finStatus || finStatus.unlocked || !finPage()) return;
    if (await askFinancePin({ required: true })) window.location.reload();
  };
  window.addEventListener('hashchange', guard);
  await guard();

  // Lock again in the page when the unlock time ends.
  if (st?.unlocked && st.unlockedUntil) {
    const ms = new Date(st.unlockedUntil).getTime() - Date.now();
    if (ms > 0 && ms < 2 ** 31) setTimeout(() => { loadFinStatus(); if (finPage()) window.location.reload(); }, ms + 500);
  }
}

// ---- Lab: finance settings page ----
export async function initLabSettings() {
  const form = document.getElementById('fin-setup-form');
  const msg = document.getElementById('fin-setup-msg');
  const statusText = document.getElementById('fin-status-text');
  const say = (text, bad = false) => { msg.textContent = text; msg.classList.toggle('is-error', bad); translate(msg); };

  const render = async () => {
    const st = await loadFinStatus();
    if (!st) return;
    statusText.textContent = st.hasPin
      ? (st.unlocked ? 'كلمة مرور المالية مفعّلة · المالية مفتوحة الآن على هذا الجهاز.' : 'كلمة مرور المالية مفعّلة · المالية مقفلة.')
      : 'لم تُحدَّد كلمة مرور للمالية بعد. المبالغ مخفية حتى تحديدها.';
    translate(statusText);
    form.hidden = !st.isOwner;
    document.getElementById('fin-staff-note').hidden = st.isOwner;
    document.getElementById('fin-setup-btn').textContent = st.hasPin ? 'تغيير كلمة مرور المالية' : 'حفظ كلمة مرور المالية';
    translate(document.getElementById('fin-setup-btn'));
  };

  form?.addEventListener('submit', async (e) => {
    e.preventDefault();
    const newPin = form.newPin.value.trim();
    if (!form.accountPassword.value) { say('اكتب كلمة مرور حسابك للتأكيد.', true); return; }
    if (newPin.length < 4) { say('كلمة مرور المالية 4 أحرف أو أرقام على الأقل.', true); return; }
    if (newPin !== form.confirmPin.value.trim()) { say('تأكيد كلمة المرور غير مطابق.', true); return; }
    const r = await jsonPost('/api/v1/labs/my/finance-lock/setup', { accountPassword: form.accountPassword.value, newPin });
    if (!r.ok) { say(await problemText(r, 'تعذّر حفظ كلمة المرور.'), true); return; }
    form.reset();
    say('تم حفظ كلمة مرور المالية، والمالية مفتوحة الآن لمدة 15 دقيقة.');
    render();
  });
  document.getElementById('fin-lock-now')?.addEventListener('click', async () => {
    await jsonPost('/api/v1/labs/my/finance-lock/lock');
    say('تم قفل المالية.');
    render();
  });
  await render();
}

// ---- Doctor: clinics ----
export async function initDoctorSettings() {
  const list = document.getElementById('clinic-list');
  const form = document.getElementById('clinic-form');
  const msg = document.getElementById('clinic-msg');
  const cancel = document.getElementById('clinic-cancel');
  const title = document.getElementById('clinic-form-title');
  const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const say = (text, bad = false) => { msg.textContent = text; msg.classList.toggle('is-error', bad); translate(msg); };
  let clinics = [];

  const edit = (c) => {
    form.id.value = c?.id || '';
    form.name.value = c?.name || '';
    form.address.value = c?.address || '';
    form.city.value = c?.city || '';
    form.governorate.value = c?.governorate || '';
    form.phone.value = c?.phone || '';
    form.isDefault.checked = !!c?.isDefault;
    cancel.hidden = !c;
    title.textContent = c ? 'تعديل العيادة' : 'إضافة عيادة';
    translate(title);
    if (c) form.name.focus();
  };

  const load = async () => {
    const r = await fetch('/api/v1/clinics');
    clinics = r.ok ? (await r.json()).items || [] : [];
    list.innerHTML = clinics.length ? clinics.map((c) => `
      <li class="clinic-item">
        <div>
          <h4>${esc(c.name)}${c.isDefault ? ' <span class="tag-default">الافتراضية</span>' : ''}</h4>
          <p>${esc([c.address, c.city, c.governorate].filter(Boolean).join('، ') || '—')}</p>
          ${c.phone ? `<p class="ltr" dir="ltr" style="text-align:start">${esc(c.phone)}</p>` : ''}
        </div>
        <div class="clinic-actions">
          ${c.isDefault ? '' : `<button type="button" data-default="${esc(c.id)}">جعلها الافتراضية</button>`}
          <button type="button" data-edit="${esc(c.id)}">تعديل</button>
          ${clinics.length > 1 ? `<button type="button" class="danger" data-del="${esc(c.id)}">حذف</button>` : ''}
        </div>
      </li>`).join('') : '<li class="muted">لا توجد عيادات بعد. أضف أول عيادة.</li>';
    translate(list);
  };

  const save = async (id, body) => {
    const r = await fetch(id ? `/api/v1/clinics/${encodeURIComponent(id)}` : '/api/v1/clinics', {
      method: id ? 'PUT' : 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
    });
    if (!r.ok) { say(await problemText(r, 'تعذّر حفظ العيادة.'), true); return false; }
    return true;
  };

  list.addEventListener('click', async (e) => {
    const b = e.target.closest('button');
    if (!b) return;
    const c = clinics.find((x) => x.id === (b.dataset.edit || b.dataset.default || b.dataset.del));
    if (!c) return;
    if (b.dataset.edit) edit(c);
    if (b.dataset.default && await save(c.id, { ...c, isDefault: true })) { say('تم تغيير العيادة الافتراضية.'); load(); }
    if (b.dataset.del) {
      const r = await fetch(`/api/v1/clinics/${encodeURIComponent(c.id)}`, { method: 'DELETE' });
      if (!r.ok) { say(await problemText(r, 'تعذّر حذف العيادة.'), true); return; }
      say('تم حذف العيادة.');
      if (form.id.value === c.id) edit(null);
      load();
    }
  });
  cancel.addEventListener('click', () => edit(null));
  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    if (!form.name.value.trim()) { say('اكتب اسم العيادة.', true); form.name.focus(); return; }
    const body = {
      name: form.name.value.trim(), address: form.address.value.trim(), city: form.city.value.trim(),
      governorate: form.governorate.value.trim(), phone: form.phone.value.trim(), isDefault: form.isDefault.checked,
    };
    if (await save(form.id.value, body)) { say(form.id.value ? 'تم حفظ التعديل.' : 'تمت إضافة العيادة.'); edit(null); load(); }
  });
  await load();
}

// ---- Order page: tracker (sent → accepted → production → delivery → completed) and delivery actions ----
export function renderTracker(o, isLabView, { orderNumber, showMsg, refresh }) {
  const card = document.getElementById('order-track-card');
  if (!card) return;
  card.hidden = false;
  const ds = o.deliveryStatus;
  const prodDone = ['ReadyForDelivery', 'Completed'].includes(o.status);
  const inDelivery = ['OutForDelivery', 'Delivered'].includes(ds);
  const accepted = !['PendingLabReview', 'PriceRevisionProposed', 'Rejected'].includes(o.status);
  const done = {
    sent: true,
    accepted,
    production: prodDone,
    delivery: o.status === 'Completed' || ds === 'Delivered',
    completed: o.status === 'Completed',
  };
  const order = ['sent', 'accepted', 'production', 'delivery', 'completed'];
  const current = order.find((k) => !done[k]);
  card.querySelectorAll('[data-track]').forEach((li) => {
    li.classList.toggle('done', !!done[li.dataset.track]);
    li.classList.toggle('current', li.dataset.track === current && o.status !== 'Rejected');
  });

  // clinic + delivery state (texts are in the HTML)
  document.getElementById('order-clinic-name').textContent = o.clinic?.name || '—';
  document.getElementById('order-clinic-address').textContent = [o.clinic?.address, o.clinic?.city].filter(Boolean).join('، ') || o.pickupAddress || '';
  const key = o.status === 'Completed' ? 'Completed' : ds;
  const src = card.querySelector(`[data-dstate="${key}"]`);
  const stateEl = document.getElementById('order-delivery-state');
  stateEl.textContent = src ? src.textContent : '';
  translate(stateEl);

  const allPaid = (o.milestones || []).length > 0 && o.milestones.every((m) => m.status === 'Paid');
  const post = async (url, body) => {
    const r = await rawFetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body ?? {}) });
    if (!r.ok) { showMsg(await problemText(r, 'تعذّر تنفيذ الإجراء.'), false); return; }
    refresh();
  };
  if (isLabView) {
    const box = document.getElementById('delivery-lab-actions');
    const ready = o.status === 'ReadyForDelivery';
    box.hidden = !ready || ds === 'Delivered';
    document.getElementById('btn-out-for-delivery').hidden = ds === 'OutForDelivery';
    document.getElementById('btn-delivered').hidden = !allPaid;
    document.getElementById('delivery-wait-payment').hidden = !ready || allPaid || ds === 'Delivered';
    box.querySelectorAll('[data-delivery-to]').forEach((b) => {
      b.onclick = () => post(`/api/v1/orders/${encodeURIComponent(orderNumber)}/delivery`, { status: Number(b.dataset.deliveryTo), note: null });
    });
  } else {
    const box = document.getElementById('delivery-doc-actions');
    box.hidden = !(o.status === 'ReadyForDelivery' && inDelivery);
    document.getElementById('delivery-auto-note').hidden = !(o.status === 'ReadyForDelivery' && ds === 'Delivered');
    const days = document.getElementById('delivery-auto-days');
    if (days) days.textContent = String(o.autoCompleteDays || 3);
    document.getElementById('btn-confirm-receipt').onclick = () => post(`/api/v1/orders/${encodeURIComponent(orderNumber)}/confirm-receipt`);
  }
}

// ---- Email confirmation banner (dashboards) + "resend" buttons ----
export function initVerifyBanner(user) {
  const banner = document.getElementById('verify-banner');
  if (banner && user && user.emailConfirmed === false) banner.hidden = false;
  document.querySelectorAll('[data-verify-resend]').forEach((b) => b.addEventListener('click', async () => {
    b.disabled = true;
    const r = await rawFetch('/api/v1/account/resend-confirmation', { method: 'POST' }).catch(() => null);
    document.querySelectorAll('[data-verify-sent]').forEach((s) => { s.hidden = !r?.ok; });
    setTimeout(() => { b.disabled = false; }, 30000);
  }));
}

// ---- Two-step verification card (account pages; the text and forms are in the HTML) ----
async function initTwoFactor(say) {
  const card = document.getElementById('two-factor-card');
  if (!card) return;
  const $ = (id) => document.getElementById(id);
  const msg = $('tf-msg');
  const show = (state) => {
    $('tf-off').hidden = state !== 'off';
    $('tf-setup').hidden = state !== 'setup';
    $('tf-on').hidden = state !== 'on';
  };
  const refresh = async () => {
    const r = await rawFetch('/api/v1/account/two-factor');
    if (!r.ok) { card.hidden = true; return; }
    const st = await r.json();
    $('tf-required').hidden = !(st.required && !st.enabled);
    $('tf-left').textContent = new Intl.NumberFormat(getLang() === 'en' ? 'en' : 'ar-EG').format(st.recoveryCodesLeft || 0);
    show(st.enabled ? 'on' : 'off');
  };
  $('tf-start').addEventListener('click', async () => {
    const r = await rawFetch('/api/v1/account/two-factor/setup', { method: 'POST' });
    if (!r.ok) { say(msg, await problemText(r, 'تعذّر بدء الإعداد.'), true); return; }
    const d = await r.json();
    $('tf-qr').src = d.qrSvgDataUri;
    $('tf-secret').textContent = d.secret;
    say(msg, '');
    show('setup');
    $('tf-setup').code.focus();
  });
  $('tf-setup').addEventListener('submit', async (e) => {
    e.preventDefault();
    const r = await rawFetch('/api/v1/account/two-factor/enable', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ code: e.target.code.value }) });
    if (!r.ok) { say(msg, await problemText(r, 'الكود غير صحيح.'), true); return; }
    const d = await r.json();
    const list = $('tf-code-list');
    list.replaceChildren(...d.recoveryCodes.map((c) => Object.assign(document.createElement('li'), { textContent: c })));
    $('tf-codes').hidden = false;
    e.target.reset();
    say(msg, 'تم تفعيل التحقق بخطوتين، وتم تسجيل الخروج من الأجهزة الأخرى.');
    await refresh();
  });
  $('tf-on').addEventListener('submit', async (e) => {
    e.preventDefault();
    const f = e.target;
    const r = await rawFetch('/api/v1/account/two-factor/disable', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ password: f.password.value, code: f.code.value }) });
    if (!r.ok) { say(msg, await problemText(r, 'تعذّر إيقاف التحقق بخطوتين.'), true); return; }
    f.reset();
    $('tf-codes').hidden = true;
    say(msg, 'تم إيقاف التحقق بخطوتين.');
    await refresh();
  });
  await refresh();
}

// ---- "My account": profile, password, and (lab owner) technicians ----
export async function initAccountPage(user) {
  const say = (el, text, bad = false) => { if (!el) return; el.textContent = text; el.classList.toggle('is-error', bad); translate(el); };
  const pf = document.getElementById('profile-form');
  const pwf = document.getElementById('password-form');

  const loadProfile = async () => {
    const r = await rawFetch('/api/v1/account/profile');
    if (!r.ok) return;
    const p = await r.json();
    pf.fullName.value = p.fullName || '';
    pf.phone.value = p.phone || '';
    pf.email.value = p.email || '';
    document.getElementById('acc-email-ok').hidden = !p.emailConfirmed;
    document.getElementById('acc-email-pending').hidden = !!p.emailConfirmed;
  };

  pf?.addEventListener('submit', async (e) => {
    e.preventDefault();
    const msg = document.getElementById('profile-msg');
    if (pf.fullName.value.trim().length < 2) { say(msg, 'اكتب الاسم الكامل.', true); return; }
    const r = await rawFetch('/api/v1/account/profile', { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ fullName: pf.fullName.value.trim(), phone: pf.phone.value.trim() }) });
    if (!r.ok) { say(msg, await problemText(r, 'تعذّر حفظ البيانات.'), true); return; }
    say(msg, 'تم حفظ البيانات.');
    const top = document.getElementById('top-user-name');
    if (top) top.textContent = pf.fullName.value.trim();
  });

  pwf?.addEventListener('submit', async (e) => {
    e.preventDefault();
    const msg = document.getElementById('password-msg');
    { const pw = pwf.newPassword.value; if (pw.length < 10 || !/\p{L}/u.test(pw) || !/\d/.test(pw)) { say(msg, 'كلمة المرور 10 أحرف على الأقل، فيها حروف وأرقام.', true); return; } }
    if (pwf.newPassword.value !== pwf.confirmPassword.value) { say(msg, 'تأكيد كلمة المرور غير مطابق.', true); return; }
    const r = await rawFetch('/api/v1/account/change-password', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ currentPassword: pwf.currentPassword.value, newPassword: pwf.newPassword.value }) });
    if (!r.ok) { say(msg, await problemText(r, 'تعذّر تغيير كلمة المرور.'), true); return; }
    pwf.reset();
    say(msg, 'تم تغيير كلمة المرور، وتم تسجيل الخروج من الأجهزة الأخرى.');
  });

  await loadProfile();
  await initTwoFactor(say);

  // Technicians (lab owner only)
  const card = document.getElementById('staff-card');
  if (!card || user?.role !== 'LaboratoryOwner') return;
  card.hidden = false;
  const list = document.getElementById('staff-list');
  const tpl = document.getElementById('staff-row-tpl');
  const sf = document.getElementById('staff-form');
  const smsg = document.getElementById('staff-msg');
  const empty = list.querySelector('li.muted');
  const loadStaff = async () => {
    const r = await rawFetch('/api/v1/labs/my/staff');
    const items = r.ok ? (await r.json()).items || [] : [];
    list.querySelectorAll('li:not(.muted)').forEach((li) => li.remove());
    if (empty) { empty.hidden = items.length > 0; empty.textContent = 'لا يوجد فنيون بعد. أضف أول فني.'; translate(empty); }
    items.forEach((s) => {
      const li = tpl.content.firstElementChild.cloneNode(true);
      li.querySelector('[data-f="name"]').textContent = s.fullName;
      li.querySelector('[data-f="email"]').textContent = [s.email, s.phone].filter(Boolean).join(' · ');
      li.querySelector('[data-f="active"]').hidden = !s.active;
      li.querySelector('[data-f="pending"]').hidden = s.active;
      li.querySelector('[data-act="resend"]').hidden = s.active;
      li.querySelector('[data-act="resend"]').onclick = async () => {
        const x = await rawFetch(`/api/v1/labs/my/staff/${encodeURIComponent(s.userId)}/resend`, { method: 'POST' });
        say(smsg, x.ok ? 'تم إرسال الدعوة مرة أخرى.' : await problemText(x, 'تعذّر الإرسال.'), !x.ok);
      };
      li.querySelector('[data-act="remove"]').onclick = async () => {
        const x = await rawFetch(`/api/v1/labs/my/staff/${encodeURIComponent(s.userId)}`, { method: 'DELETE' });
        say(smsg, x.ok ? 'تمت إزالة الفني من المعمل وإغلاق حسابه.' : await problemText(x, 'تعذّرت الإزالة.'), !x.ok);
        loadStaff();
      };
      list.append(li);
    });
    translate(list);
  };
  sf?.addEventListener('submit', async (e) => {
    e.preventDefault();
    if (sf.fullName.value.trim().length < 2) { say(smsg, 'اكتب اسم الفني.', true); return; }
    if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(sf.email.value.trim())) { say(smsg, 'أدخل بريدًا إلكترونيًا صحيحًا.', true); return; }
    const r = await rawFetch('/api/v1/labs/my/staff', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ fullName: sf.fullName.value.trim(), email: sf.email.value.trim(), phone: sf.phone.value.trim() }) });
    if (!r.ok) { say(smsg, await problemText(r, 'تعذّر إضافة الفني.'), true); return; }
    sf.reset();
    say(smsg, 'تمت إضافة الفني، ووصلته دعوة بالبريد لاختيار كلمة المرور.');
    loadStaff();
  });
  await loadStaff();
}

// ---- PDF documents: any [data-doc] button downloads the file (lab copies ask for the finance password first) ----
export function installDocLinks() {
  document.addEventListener('click', async (e) => {
    const btn = e.target.closest('[data-doc]');
    if (!btn) return;
    e.preventDefault();
    btn.disabled = true;
    try {
      let r = await rawFetch(btn.dataset.doc);
      if (r.status === 423 && await askFinancePin()) r = await rawFetch(btn.dataset.doc);
      if (!r.ok) {
        const msg = await problemText(r, 'تعذّر تحميل المستند.');
        window.alert(getLang() === 'en' ? 'The document could not be downloaded.' : msg);
        return;
      }
      const blob = await r.blob();
      const name = (r.headers.get('Content-Disposition') || '').match(/filename="?([^";]+)"?/)?.[1] || 'taaj.pdf';
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = name;
      document.body.append(a);
      a.click();
      setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 1000);
    } finally {
      btn.disabled = false;
    }
  });
}

// ---- Admin: reconciliation with the payment gateway ----
export async function initReconciliation() {
  const form = document.getElementById('rec-form');
  if (!form) return;
  const tbody = document.getElementById('rec-tbody');
  const stateText = (k) => document.querySelector(`#rec-states [data-state="${k}"]`)?.textContent || k;
  const money = (v) => (v === null || v === undefined ? '—' : `${new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(v)} ج.م`);
  const today = new Date();
  form.to.value = today.toISOString().slice(0, 10);
  form.from.value = new Date(today.getTime() - 30 * 86400000).toISOString().slice(0, 10);
  const load = async () => {
    const q = new URLSearchParams({ from: new Date(`${form.from.value}T00:00:00`).toISOString(), to: new Date(`${form.to.value}T23:59:59`).toISOString() });
    const r = await rawFetch(`/api/v1/admin/reconciliation?${q}`);
    if (!r.ok) return;
    const d = await r.json();
    const s = d.summary;
    document.getElementById('rec-matched').textContent = String(s.matched);
    document.getElementById('rec-issues').textContent = String(s.issues);
    document.getElementById('rec-collected').textContent = `${money(s.collectedEgp)} / ${money(s.gatewayCollectedEgp)}`;
    document.getElementById('rec-refunded').textContent = `${money(s.refundedEgp)} / ${money(s.gatewayRefundedEgp)}`;
    document.getElementById('rec-commission').textContent = money(s.commissionEgp);
    document.getElementById('rec-fees').textContent = money(s.gatewayFeesEgp);
    tbody.innerHTML = '';
    if (!d.items.length) {
      const tr = document.createElement('tr');
      tr.innerHTML = '<td colspan="7" class="muted"></td>';
      tr.firstElementChild.textContent = stateText('none');
      tbody.append(tr);
    }
    d.items.forEach((it) => {
      const tr = document.createElement('tr');
      const bad = ['MissingAtGateway', 'AmountMismatch', 'PaidAtGatewayOnly'].includes(it.state);
      const cells = [it.orderNumber || '—', stateText(it.state), it.status, money(it.amountEgp), money(it.gatewayAmountEgp), it.providerTransactionRef || '—', new Date(it.createdAt).toLocaleString('en-GB')];
      cells.forEach((c, i) => {
        const td = document.createElement('td');
        if (i === 1) { const b = document.createElement('span'); b.className = `badge ${bad ? 'is-danger' : 'is-warning'}`; b.textContent = c; td.append(b); } else td.textContent = c;
        if ([0, 5, 6].includes(i)) td.className = 'ltr mono';
        tr.append(td);
      });
      tbody.append(tr);
    });
    translate(document.getElementById('reconciliation'));
  };
  form.addEventListener('submit', (e) => { e.preventDefault(); load(); });
  await load();
}

// Print buttons (no inline handlers: the Content-Security-Policy blocks them)
document.addEventListener('click', (e) => { if (e.target.closest('[data-print]')) window.print(); });
