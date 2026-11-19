// Forgot password, set a new password (reset or staff invitation) and email confirmation pages.
// Behaviour only: every text is written in the HTML; views are shown/hidden here.
import { applyDomTranslations, getLang } from '../core/i18n.js?v=22';

const $ = (sel) => document.querySelector(sel);
const qs = new URLSearchParams(location.search);
const token = qs.get('token') || '';
const show = (name) => document.querySelectorAll('[data-view]').forEach((v) => { v.hidden = v.dataset.view !== name; });
const tr = (el) => { if (el && getLang() === 'en') applyDomTranslations(el); };
const post = (url, body) => fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
const fail = (msg) => { const a = $('#acc-alert'); if (!a) return; a.textContent = msg; a.hidden = false; tr(a); };
const detail = async (r, fallback) => ((await r.json().catch(() => ({}))).detail) || fallback;

// show / hide password
document.querySelectorAll('[data-pw-toggle]').forEach((b) => b.addEventListener('click', () => {
  const i = document.getElementById(b.dataset.pwToggle);
  if (i) i.type = i.type === 'password' ? 'text' : 'password';
}));

// ---- forgot password ----
$('#forgot-form')?.addEventListener('submit', async (e) => {
  e.preventDefault();
  const email = e.target.email.value.trim();
  if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) { fail('أدخل بريدًا إلكترونيًا صحيحًا.'); return; }
  const btn = e.target.querySelector('[type=submit]');
  btn.disabled = true;
  const r = await post('/api/v1/auth/forgot-password', { email }).catch(() => null);
  btn.disabled = false;
  if (!r || (!r.ok && r.status !== 200)) { fail(r?.status === 429 ? 'محاولات كثيرة. حاول بعد دقيقة.' : 'تعذّر الاتصال بالخادم. حاول مرة أخرى.'); return; }
  const out = await r.json().catch(() => ({}));
  if (out.demoLink) { $('#demo-link').href = out.demoLink; $('#demo-link-box').hidden = false; } // static demo only
  show('sent');
});

// ---- reset password / staff invitation ----
const resetForm = $('#reset-form');
if (resetForm) {
  const invite = qs.get('invite') === '1';
  document.querySelectorAll('[data-mode]').forEach((el) => { el.hidden = el.dataset.mode !== (invite ? 'invite' : 'reset'); });
  if (!token) show('invalid');
  resetForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    const pw = resetForm.newPassword.value;
    if (pw.length < 10 || !/\p{L}/u.test(pw) || !/\d/.test(pw)) { fail('كلمة المرور 10 أحرف على الأقل، فيها حروف وأرقام.'); return; }
    if (pw !== resetForm.confirmPassword.value) { fail('تأكيد كلمة المرور غير مطابق.'); return; }
    const r = await post('/api/v1/auth/reset-password', { token, newPassword: pw }).catch(() => null);
    if (r?.ok) { show('done'); return; }
    if (r?.status === 400 && /الرابط/.test(await r.clone().text())) { show('invalid'); return; }
    fail(r ? await detail(r, 'تعذّر حفظ كلمة المرور.') : 'تعذّر الاتصال بالخادم. حاول مرة أخرى.');
  });
}

// ---- email confirmation ----
if (document.querySelector('[data-view="loading"]')) {
  (async () => {
    const r = token ? await post('/api/v1/auth/confirm-email', { token }).catch(() => null) : null;
    show(r?.ok ? 'done' : 'invalid');
    const me = await fetch('/api/v1/auth/me').catch(() => null);
    if (me?.ok) {
      const u = await me.json();
      const home = { Dentist: '/doctor-dashboard.html', LaboratoryOwner: '/lab-dashboard.html', LaboratoryStaff: '/lab-dashboard.html', Admin: '/admin-dashboard.html' }[u.role];
      document.querySelectorAll('[data-view] a[href="/login.html"]').forEach((a) => { if (home) a.href = home; });
    }
  })();
}
