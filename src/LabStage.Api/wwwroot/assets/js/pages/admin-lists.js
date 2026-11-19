// Admin lists with server-side paging and Excel export (users, orders, disputes, refunds),
// plus the dispute card on order pages. Texts come from the HTML (#admin-texts, #dispute-texts).
import { applyDomTranslations, getLang } from '../core/i18n.js?v=20';

const tr = (el) => { if (el && getLang() === 'en') applyDomTranslations(el); };
const t = (k) => document.querySelector(`#admin-texts [data-t="${k}"]`)?.textContent || k;
const money = (v) => `${new Intl.NumberFormat('en-US', { minimumFractionDigits: 0, maximumFractionDigits: 2 }).format(Number(v || 0))} ج.م`;
const day = (d) => (d ? new Date(d).toLocaleDateString('en-GB') : '—');

function cell(tr0, value, cls) {
  const td = document.createElement('td');
  if (value instanceof Node) td.append(value); else td.textContent = value ?? '—';
  if (cls) td.className = cls;
  tr0.append(td);
  return td;
}
const badge = (text, tone) => { const b = document.createElement('span'); b.className = `badge ${tone || ''}`; b.textContent = text; return b; };
const button = (text, cls = 'btn btn-ghost btn-sm') => { const b = document.createElement('button'); b.type = 'button'; b.className = cls; b.textContent = text; return b; };

/** One paged list: filters form -> GET url?filters&page -> rows -> pager. */
function pagedList({ key, url, tbody, cols, row, onData }) {
  const form = document.querySelector(`[data-filters="${key}"]`);
  const pager = document.querySelector(`[data-pager="${key}"]`);
  const body = document.getElementById(tbody);
  let page = 1;
  const params = () => {
    const q = new URLSearchParams();
    if (form) new FormData(form).forEach((v, k) => {
      if (!v) return;
      if (k === 'from') q.set(k, new Date(`${v}T00:00:00`).toISOString());
      else if (k === 'to') q.set(k, new Date(`${v}T23:59:59`).toISOString());
      else q.set(k, String(v));
    });
    return q;
  };
  async function load() {
    const q = params();
    q.set('page', String(page));
    q.set('pageSize', '15');
    const r = await fetch(`${url}?${q}`).catch(() => null);
    if (!r?.ok) return;
    const d = await r.json();
    body.innerHTML = '';
    if (!d.items.length) {
      const tr0 = document.createElement('tr');
      const td = cell(tr0, t('none'), 'muted');
      td.colSpan = cols;
      body.append(tr0);
    }
    d.items.forEach((it) => body.append(row(it, load)));
    const max = Math.max(1, Math.ceil(d.total / d.pageSize));
    pager.querySelector('[data-pg="cur"]').textContent = String(d.page);
    pager.querySelector('[data-pg="max"]').textContent = String(max);
    pager.querySelector('[data-pg="total"]').textContent = String(d.total);
    pager.querySelector('[data-pg="prev"]').disabled = d.page <= 1;
    pager.querySelector('[data-pg="next"]').disabled = d.page >= max;
    onData?.(d);
    tr(body.closest('section'));
  }
  form?.addEventListener('submit', (e) => { e.preventDefault(); page = 1; load(); });
  pager.querySelector('[data-pg="prev"]').addEventListener('click', () => { page = Math.max(1, page - 1); load(); });
  pager.querySelector('[data-pg="next"]').addEventListener('click', () => { page += 1; load(); });
  document.querySelector(`[data-export="${key}"]`)?.addEventListener('click', async (e) => {
    const btn = e.currentTarget;
    btn.disabled = true;
    const r = await fetch(`/api/v1/admin/export/${key}.xlsx?${params()}`).catch(() => null);
    btn.disabled = false;
    if (!r?.ok || !(r.headers.get('Content-Type') || '').includes('spreadsheet')) { window.alert(t('exportDemo')); return; }
    const a = document.createElement('a');
    a.href = URL.createObjectURL(await r.blob());
    a.download = (r.headers.get('Content-Disposition') || '').match(/filename="?([^";]+)"?/)?.[1] || `taaj-${key}.xlsx`;
    document.body.append(a); a.click();
    setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 1000);
  });
  return { load };
}

export async function initAdminLists() {
  if (!document.getElementById('users-tbody')) return;

  const users = pagedList({
    key: 'users', url: '/api/v1/admin/users', tbody: 'users-tbody', cols: 8,
    row: (u, reload) => {
      const r = document.createElement('tr');
      cell(r, u.fullName);
      cell(r, u.email, 'ltr mono');
      cell(r, t(u.role));
      cell(r, u.organization || '—');
      cell(r, String(u.orders), 'num');
      cell(r, badge(u.isSuspended ? t('suspended') : u.emailConfirmed ? t('active') : t('unconfirmed'), u.isSuspended ? 'is-danger' : u.emailConfirmed ? 'is-success' : 'is-warning'));
      cell(r, day(u.createdAt), 'ltr');
      if (u.role === 'Admin') { cell(r, ''); return r; }
      const b = button(u.isSuspended ? t('activate') : t('suspend'), u.isSuspended ? 'btn btn-primary btn-sm' : 'btn btn-ghost btn-sm');
      b.addEventListener('click', async () => {
        let reason = '';
        if (!u.isSuspended) { reason = window.prompt(t('suspendReason')) || ''; if (!reason.trim()) return; }
        const x = await fetch(`/api/v1/admin/users/${u.id}/suspend`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ suspended: !u.isSuspended, reason }) });
        if (!x.ok) { window.alert((await x.json().catch(() => ({}))).detail || '!'); return; }
        reload();
      });
      cell(r, b);
      return r;
    },
  });

  const labSelect = document.querySelector('[data-filters="orders"] [name="labId"]');
  const orders = pagedList({
    key: 'orders', url: '/api/v1/admin/orders', tbody: 'orders-tbody', cols: 9,
    onData: (d) => {
      if (labSelect && labSelect.options.length <= 1) (d.labs || []).forEach((l) => { const o = document.createElement('option'); o.value = l.id; o.textContent = l.name; labSelect.append(o); });
    },
    row: (o) => {
      const r = document.createElement('tr');
      const n = document.createElement('span');
      n.textContent = o.orderNumber;
      if (o.openDispute) n.append(' ', badge(t('dispute'), 'is-danger'));
      cell(r, n, 'ltr mono');
      cell(r, o.patient);
      cell(r, o.dentist);
      cell(r, o.lab);
      const st = document.querySelector(`[data-filters="orders"] [name="status"] option[value="${o.status}"]`)?.textContent || o.status;
      cell(r, badge(st, o.status === 'Completed' ? 'is-success' : o.status === 'Rejected' ? 'is-danger' : 'is-warning'));
      cell(r, money(o.totalEgp), 'num');
      cell(r, money(o.paidEgp), 'num');
      cell(r, o.refundedEgp ? money(o.refundedEgp) : '—', 'num');
      cell(r, day(o.createdAt), 'ltr');
      return r;
    },
  });

  // Disputes + decision form
  const decide = document.getElementById('dispute-decide');
  let current = null;
  const disputes = pagedList({
    key: 'disputes', url: '/api/v1/admin/disputes', tbody: 'disputes-tbody', cols: 7,
    row: (d, reload) => {
      const r = document.createElement('tr');
      cell(r, d.orderNumber, 'ltr mono');
      const who = document.createElement('span');
      who.textContent = `${d.openedBy} · ${t(d.openedByRole)}`;
      cell(r, who);
      cell(r, d.reason);
      cell(r, badge(t(d.status), d.status === 'Open' ? 'is-warning' : d.status === 'Resolved' ? 'is-success' : 'is-danger'));
      cell(r, money(d.paidEgp), 'num');
      cell(r, day(d.createdAt), 'ltr');
      if (d.status !== 'Open') { cell(r, d.resolution || ''); return r; }
      const b = button(t('decide'), 'btn btn-primary btn-sm');
      b.addEventListener('click', async () => {
        current = { ...d, reload };
        decide.hidden = false;
        decide.reset();
        document.getElementById('dd-order').textContent = d.orderNumber;
        document.getElementById('dd-reason').textContent = d.reason;
        document.getElementById('dd-msg').textContent = '';
        const sel = decide.paymentId;
        sel.querySelectorAll('option:not([value=""])').forEach((o) => o.remove());
        const p = await fetch(`/api/v1/admin/orders/${encodeURIComponent(d.orderNumber)}/payments`).then((x) => (x.ok ? x.json() : { items: [] }));
        p.items.filter((x) => x.status !== 'Pending' && x.status !== 'Failed').forEach((x) => {
          const o = document.createElement('option');
          o.value = x.id;
          const st = { Succeeded: 'مدفوعة', PartiallyRefunded: 'مستردة جزئيًا', Refunded: 'مستردة' }[x.status] || x.status;
          o.textContent = `${money(x.amountEgp)} · ${st}${x.refundedEgp ? ` (−${money(x.refundedEgp)})` : ''}`;
          sel.append(o);
        });
        decide.scrollIntoView({ block: 'center', behavior: 'smooth' });
        tr(decide);
      });
      cell(r, b);
      return r;
    },
  });
  document.getElementById('dd-cancel')?.addEventListener('click', () => { decide.hidden = true; });
  decide?.addEventListener('submit', async (e) => {
    e.preventDefault();
    const msg = document.getElementById('dd-msg');
    const resolve = decide.decision.value === 'resolve';
    const body = { resolution: decide.resolution.value.trim(), resolve };
    if (resolve && decide.paymentId.value && Number(decide.amount.value) > 0) {
      Object.assign(body, { paymentId: decide.paymentId.value, refundAmountEgp: Number(decide.amount.value), faultParty: Number(decide.faultParty.value) });
    }
    const r = await fetch(`/api/v1/admin/disputes/${current.id}/decide`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    if (!r.ok) { msg.textContent = (await r.json().catch(() => ({}))).detail || '!'; msg.classList.add('is-error'); tr(msg); return; }
    decide.hidden = true;
    current.reload();
    refunds.load();
    orders.load();
  });

  const refunds = pagedList({
    key: 'refunds', url: '/api/v1/admin/refunds', tbody: 'refunds-tbody', cols: 7,
    row: (x) => {
      const r = document.createElement('tr');
      cell(r, x.orderNumber, 'ltr mono');
      cell(r, money(x.amountEgp), 'num');
      cell(r, money(x.reversedCommissionEgp), 'num');
      cell(r, t(x.faultParty));
      cell(r, x.reason);
      cell(r, x.providerRefundRef || '—', 'ltr mono');
      cell(r, day(x.createdAt), 'ltr');
      return r;
    },
  });

  // The admin search box (top bar) also feeds the users list.
  const q0 = new URLSearchParams(location.search).get('q');
  if (q0) document.querySelector('[data-filters="users"] [name="q"]').value = q0;
  await Promise.all([users.load(), orders.load(), disputes.load(), refunds.load()]);
}

// ---- Order pages: open a dispute and follow it ----
export async function initOrderDisputes(orderNumber) {
  const list = document.getElementById('dispute-list');
  const form = document.getElementById('dispute-form');
  if (!list || !form) return;
  const s = (k) => document.querySelector(`#dispute-texts [data-s="${k}"]`)?.textContent || k;
  const msg = document.getElementById('dispute-msg');
  const load = async () => {
    const r = await fetch(`/api/v1/orders/${encodeURIComponent(orderNumber)}/disputes`).catch(() => null);
    if (!r?.ok) return;
    const d = await r.json();
    list.innerHTML = '';
    d.items.forEach((x) => {
      const li = document.createElement('li');
      li.className = 'dispute-item';
      const head = document.createElement('div');
      head.append(badge(s(x.status), x.status === 'Open' ? 'is-warning' : x.status === 'Resolved' ? 'is-success' : 'is-danger'), ` ${day(x.createdAt)}`);
      const reason = document.createElement('p');
      reason.textContent = x.reason;
      li.append(head, reason);
      if (x.resolution) { const res = document.createElement('p'); res.className = 'muted'; res.textContent = `${s('decision')} ${x.resolution}`; li.append(res); }
      list.append(li);
    });
    form.hidden = !d.canOpen;
    tr(list.closest('section'));
  };
  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const r = await fetch(`/api/v1/orders/${encodeURIComponent(orderNumber)}/disputes`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ reason: form.reason.value.trim() }) });
    const out = await r.json().catch(() => ({}));
    msg.textContent = r.ok ? s('sent') : out.detail || '!';
    msg.classList.toggle('is-error', !r.ok);
    tr(msg);
    if (r.ok) { form.reset(); load(); }
  });
  await load();
}
