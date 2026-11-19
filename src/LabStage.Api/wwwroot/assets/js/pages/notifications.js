// Notification center in the dashboard top bar: grouped by day, filters, unread counts, mark read, paging,
// live updates (SignalR), and the per-category preferences form on "My account".
// Behaviour only: every text is in the HTML (groups, filters, empty state).
import { applyDomTranslations, getLang } from '../core/i18n.js?v=22';
import { live } from '../core/live.js?v=1';

const ICON = {
  Orders: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M7.5 4.5c2-1.6 3.4-1 4.5-.3 1.1-.7 2.5-1.3 4.5.3 2 1.7 1.6 5.2.6 7.9-.7 2-1 4.3-1.9 5.9-.6 1-1.7 1-2.1-.2l-.9-3.3c-.2-.8-1.5-.8-1.7 0l-.9 3.3c-.4 1.2-1.5 1.2-2.1.2-.9-1.6-1.2-3.9-1.9-5.9-1-2.7-1.4-6.2.6-7.9z"/></svg>',
  Production: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><polygon points="12 2.5 21 7.5 12 12.5 3 7.5 12 2.5"/><polyline points="3 12.5 12 17.5 21 12.5"/></svg>',
  Payments: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round"><rect x="3" y="6" width="18" height="13" rx="3"/><path d="M3 10h18M16 14.5h2"/></svg>',
  Delivery: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M3 7h11v9H3zM14 10h4l3 3v3h-7z"/><circle cx="7" cy="17.5" r="1.6"/><circle cx="17" cy="17.5" r="1.6"/></svg>',
  Chat: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M4 5h16v11H9l-5 4z"/></svg>',
  Account: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round"><circle cx="12" cy="8" r="4"/><path d="M4 21c1.5-4 4.5-6 8-6s6.5 2 8 6"/></svg>',
};

const safeLink = (u) => (typeof u === 'string' && u.startsWith('/') && !u.startsWith('//') ? u : '#');
const tr = (el) => { if (el && getLang() === 'en') applyDomTranslations(el); };
const fmtCount = (n) => new Intl.NumberFormat(getLang() === 'en' ? 'en-US' : 'ar-EG').format(n);

export function initNotificationCenter() {
  const btn = document.getElementById('notif-btn');
  const panel = document.getElementById('notif-panel');
  if (!btn || !panel) return;
  const list = document.getElementById('np-list');
  const empty = document.getElementById('np-empty');
  const more = document.getElementById('notif-more');
  const badge = document.getElementById('notif-count');
  const tpl = document.getElementById('notif-item-tpl');
  const groupText = (g) => document.querySelector(`#notif-groups [data-g="${g}"]`)?.textContent || '';
  let filter = '';
  let page = 1;

  const setBadge = (n) => { badge.textContent = fmtCount(n); badge.hidden = n === 0; };

  const dayKey = (d) => {
    const t = new Date(); t.setHours(0, 0, 0, 0);
    const x = new Date(d); x.setHours(0, 0, 0, 0);
    const diff = Math.round((t - x) / 86400000);
    return diff <= 0 ? 'today' : diff === 1 ? 'yesterday' : 'older';
  };

  function item(n) {
    const a = tpl.content.firstElementChild.cloneNode(true);
    a.href = safeLink(n.linkUrl);
    a.dataset.id = n.id;
    a.classList.toggle('is-unread', !n.isRead);
    a.querySelector('.notif-ic').innerHTML = ICON[n.category] || ICON.Account;
    a.querySelector('.notif-ic').dataset.cat = n.category || 'Account';
    a.querySelector('[data-f="title"]').textContent = n.title;
    a.querySelector('[data-f="body"]').textContent = n.body;
    const t = a.querySelector('[data-f="time"]');
    t.dateTime = n.createdAt;
    t.textContent = new Date(n.createdAt).toLocaleString(getLang() === 'en' ? 'en-GB' : 'ar-EG', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });
    a.addEventListener('click', () => { if (!n.isRead) fetch(`/api/v1/notifications/${n.id}/read`, { method: 'POST', keepalive: true }); });
    return a;
  }

  async function load(reset = true) {
    if (reset) { page = 1; list.innerHTML = ''; }
    const q = new URLSearchParams({ page: String(page), pageSize: '15' });
    if (filter === 'unread') q.set('unread', 'true'); else if (filter) q.set('category', filter);
    const r = await fetch(`/api/v1/notifications?${q}`).catch(() => null);
    if (!r?.ok) return;
    const d = await r.json();
    setBadge(d.unreadCount || 0);
    let lastGroup = list.querySelector('.notif-group:last-of-type')?.dataset.g || '';
    (d.items || []).forEach((n) => {
      const g = dayKey(n.createdAt);
      if (g !== lastGroup) {
        const h = document.createElement('p');
        h.className = 'notif-group';
        h.dataset.g = g;
        h.textContent = groupText(g);
        list.append(h);
        lastGroup = g;
      }
      list.append(item(n));
    });
    empty.hidden = list.children.length > 0;
    more.hidden = page * (d.pageSize || 15) >= (d.total || 0);
    tr(panel);
  }

  const open = (on) => {
    panel.hidden = !on;
    btn.setAttribute('aria-expanded', String(on));
    if (on) load();
  };
  btn.addEventListener('click', (e) => { e.stopPropagation(); open(panel.hidden); });
  document.addEventListener('click', (e) => { if (!panel.hidden && !panel.contains(e.target)) open(false); });
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && !panel.hidden) { open(false); btn.focus(); } });
  panel.querySelectorAll('[data-cat]').forEach((b) => b.addEventListener('click', () => {
    filter = b.dataset.cat;
    panel.querySelectorAll('[data-cat]').forEach((x) => x.setAttribute('aria-pressed', String(x === b)));
    load();
  }));
  more.addEventListener('click', () => { page += 1; load(false); });
  document.getElementById('notif-read-all')?.addEventListener('click', async () => {
    const q = filter && filter !== 'unread' ? `?category=${encodeURIComponent(filter)}` : '';
    await fetch(`/api/v1/notifications/read-all${q}`, { method: 'POST' });
    load();
  });

  // Live: new notifications update the badge, the open panel, and show a short pop-up.
  const toast = document.getElementById('notif-toast');
  let toastTimer = null;
  const conn = live();
  conn?.on('notification', (n) => {
    if (typeof n?.unreadCount === 'number') setBadge(n.unreadCount);
    if (!panel.hidden) load();
    if (toast && n?.title) {
      toast.querySelector('[data-f="title"]').textContent = n.title;
      toast.querySelector('[data-f="body"]').textContent = n.body || '';
      toast.hidden = false;
      toast.onclick = () => { if (safeLink(n.linkUrl) !== '#') location.href = safeLink(n.linkUrl); };
      clearTimeout(toastTimer);
      toastTimer = setTimeout(() => { toast.hidden = true; }, 6000);
    }
    document.dispatchEvent(new CustomEvent('taaj:notification', { detail: n }));
  });
  // Without a live connection (demo, blocked sockets), refresh the count now and then.
  if (!conn) setInterval(() => { if (panel.hidden) load(); }, 60000);
}

// ---- "My account": notification preferences ----
export async function initNotificationPrefs() {
  const form = document.getElementById('prefs-form');
  if (!form) return;
  const msg = document.getElementById('prefs-msg');
  const r = await fetch('/api/v1/account/notification-preferences').catch(() => null);
  if (r?.ok) (await r.json()).items.forEach((p) => {
    if (form[`${p.category}-site`]) form[`${p.category}-site`].checked = p.inSite;
    if (form[`${p.category}-email`]) form[`${p.category}-email`].checked = p.email;
  });
  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const items = [...form.querySelectorAll('tr[data-cat]')].map((row) => ({
      category: row.dataset.cat, inSite: form[`${row.dataset.cat}-site`].checked, email: form[`${row.dataset.cat}-email`].checked,
    }));
    const s = await fetch('/api/v1/account/notification-preferences', { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(items) }).catch(() => null);
    msg.textContent = s?.ok ? 'تم حفظ إعدادات التنبيهات.' : 'تعذّر حفظ الإعدادات.';
    msg.classList.toggle('is-error', !s?.ok);
    tr(msg);
  });
}
