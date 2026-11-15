// Lab finance lock (password modal + top-bar chip), lab finance settings, doctor clinics, order tracker.
// Behaviour only: every text shown here is written in the HTML (hidden until needed).
import { applyDomTranslations, getLang } from '../core/i18n.js?v=11';

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
