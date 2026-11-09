// Fills the v2 dashboard home grids (doctor / lab / admin). The layout and all labels are in the HTML;
// this module only writes numbers, list rows from API data, and simple state classes.
const nf = new Intl.NumberFormat('ar-EG', { maximumFractionDigits: 0 });
const byId = (id) => document.getElementById(id);
const set = (id, v) => { const el = byId(id); if (el) el.textContent = v; };
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const DONE = ['Completed', 'ReadyForDelivery'];
const ACTIVE = ['Accepted', 'InProduction'];
const WAITING = ['PendingLabReview', 'PriceRevisionProposed'];
const shortDate = (d) => new Date(d).toLocaleDateString('ar-EG', { day: 'numeric', month: 'short' });

function weekStart(now = new Date()) {
  const d = new Date(now); d.setHours(0, 0, 0, 0);
  d.setDate(d.getDate() - ((d.getDay() + 1) % 7)); // week starts on Saturday
  return d;
}
const thisMonth = (iso) => { const d = new Date(iso); const n = new Date(); return d.getFullYear() === n.getFullYear() && d.getMonth() === n.getMonth(); };
export const chipFor = (status) => (DONE.includes(status) ? 'st-ok' : ACTIVE.includes(status) ? 'st-wip' : 'st-bad');

/** Weekly pill bars: count of dates per day (Sat..Fri); days after today are hatched. */
export function fillBars(dates) {
  const start = weekStart(); const today = Math.floor((Date.now() - start) / 86400000);
  const counts = Array(7).fill(0);
  for (const iso of dates) { const i = Math.floor((new Date(iso) - start) / 86400000); if (i >= 0 && i < 7) counts[i]++; }
  const max = Math.max(1, ...counts); const total = counts.reduce((a, b) => a + b, 0);
  const peak = counts.indexOf(Math.max(...counts));
  document.querySelectorAll('#home-bars .pill-bar').forEach((bar) => {
    const i = Number(bar.dataset.day); const tip = bar.querySelector('.pill-tip');
    bar.classList.remove('is-empty', 'tone-1', 'tone-2', 'tone-3');
    if (i > today) { bar.classList.add('is-empty'); bar.style.setProperty('--p', String(0.45 + (i % 3) * 0.15)); if (tip) tip.hidden = true; return; }
    const c = counts[i];
    bar.style.setProperty('--p', String(Math.max(0.12, c / max)));
    bar.classList.add(c === max && c > 0 ? 'tone-1' : i % 2 ? 'tone-2' : 'tone-3');
    if (tip) { tip.hidden = !(i === peak && total > 0); tip.textContent = `${nf.format(Math.round((c / total) * 100))}%`; }
    bar.title = nf.format(c);
  });
}

export function fillList(rows) {
  const ul = byId('home-list'); if (!ul) return;
  ul.innerHTML = rows.length ? rows.slice(0, 5).map((r, i) => `
    <li><span class="mi-ic c${i % 5}" aria-hidden="true">${esc(r.icon || '•')}</span>
      <div><a href="${esc(r.href)}">${esc(r.title)}</a><small>${esc(r.sub)}</small></div></li>`).join('')
    : '<li class="muted">لا توجد عناصر بعد.</li>';
}

export function fillTeam(rows) {
  const ul = byId('home-team'); if (!ul) return;
  ul.innerHTML = rows.length ? rows.slice(0, 5).map((r) => `
    <li><span class="team-avatar" aria-hidden="true">${esc(String(r.name).replace(/^(د\.|م\.)\s*/, '').trim().charAt(0))}</span>
      <div style="min-inline-size:0"><div class="team-name">${esc(r.name)}</div><div class="team-task">${esc(r.lead)} <b>${esc(r.task)}</b></div></div>
      <span class="st-chip ${r.chip}">${esc(r.chipText)}</span></li>`).join('')
    : '<li class="muted">لا يوجد بعد.</li>';
}

/** Semicircle gauge: three segments on one arc (pathLength = 100). */
export function fillGauge(done, progress, pending) {
  const total = done + progress + pending || 1;
  const d = (done / total) * 100; const p = (progress / total) * 100; const q = (pending / total) * 100;
  const seg = (id, len, from) => { const el = byId(id); if (!el) return; el.setAttribute('stroke-dasharray', `${len} 100`); el.setAttribute('stroke-dashoffset', String(-from)); el.style.visibility = len < 0.5 ? 'hidden' : ''; };
  seg('g-done', d, 0); seg('g-progress', p, d); seg('g-pending', q, d + p);
  set('g-pct', `${nf.format(Math.round(d))}%`);
}

let timer = 0;
/** Big countdown digits. target: Date or null. */
export function startCountdown(target, subText, href) {
  clearInterval(timer);
  const digits = byId('timer-digits'); const sub = byId('timer-sub'); const open = byId('timer-open');
  if (href && open) open.href = href;
  if (sub && subText) sub.textContent = subText;
  const pad = (n) => String(n).padStart(2, '0');
  const tick = () => {
    if (!digits) return;
    const ms = target ? target - Date.now() : 0;
    if (ms <= 0) { digits.textContent = '00:00:00'; return; }
    const s = Math.floor(ms / 1000); const days = Math.floor(s / 86400);
    const hms = `${pad(Math.floor((s % 86400) / 3600))}:${pad(Math.floor((s % 3600) / 60))}:${pad(s % 60)}`;
    digits.textContent = days ? `${days}d ${hms}` : hms;
  };
  tick();
  if (target && !window.matchMedia('(prefers-reduced-motion: reduce)').matches) timer = setInterval(tick, 1000);
}

/** Nearest future delivery date among a few active orders (details carry requiredDeliveryDate). */
async function nearestDelivery(orders, detailsPath) {
  const active = orders.filter((o) => !DONE.includes(o.status) && !['Rejected', 'Cancelled'].includes(o.status)).slice(0, 6);
  const details = await Promise.all(active.map((o) => fetch(`/api/v1/orders/${encodeURIComponent(o.orderNumber)}`).then((r) => (r.ok ? r.json() : null)).catch(() => null)));
  const now = Date.now();
  const best = details.filter((d) => d?.requiredDeliveryDate && new Date(d.requiredDeliveryDate) > now)
    .sort((a, b) => new Date(a.requiredDeliveryDate) - new Date(b.requiredDeliveryDate))[0];
  return best ? { date: new Date(best.requiredDeliveryDate), order: best, href: `${detailsPath}?orderNumber=${encodeURIComponent(best.orderNumber)}` } : null;
}

const patient = (o) => o.patientName || o.caseReference || o.orderNumber;

export async function fillDoctorHome(orders, statusAr) {
  const n = orders.length;
  set('kpi-1', nf.format(n));
  set('kpi-1-n', nf.format(orders.filter((o) => thisMonth(o.createdAt)).length));
  set('kpi-2', nf.format(orders.filter((o) => ACTIVE.includes(o.status)).length));
  const awaiting = orders.filter((o) => o.status === 'PriceRevisionProposed' || (o.stages || []).some((s) => s.status === 'SubmittedForReview'));
  set('kpi-3', nf.format(awaiting.length));
  const done = orders.filter((o) => DONE.includes(o.status));
  set('kpi-4', nf.format(done.length));
  set('kpi-4-n', nf.format(done.filter((o) => thisMonth(o.createdAt)).length));
  fillBars(orders.map((o) => o.createdAt));

  const details = '/doctor-order-details.html';
  const next = awaiting[0] || orders.find((o) => ACTIVE.includes(o.status));
  if (next) {
    set('rem-title', `${patient(next)} — ${next.serviceName}`);
    set('rem-time', `${next.orderNumber} · ${statusAr[next.status] || next.status} · ${next.laboratoryName}`);
    const a = byId('rem-link'); if (a) a.href = `${details}?orderNumber=${encodeURIComponent(next.orderNumber)}`;
  }
  fillList(orders.map((o) => ({ icon: (o.serviceName || 'ت').charAt(0), title: `${patient(o)} — ${o.serviceName}`, sub: `${o.orderNumber} · ${shortDate(o.createdAt)}`, href: `${details}?orderNumber=${encodeURIComponent(o.orderNumber)}` })));

  const labs = new Map();
  for (const o of orders) if (!labs.has(o.laboratoryName)) labs.set(o.laboratoryName, o);
  fillTeam([...labs.values()].map((o) => ({ name: o.laboratoryName, lead: 'يعمل على', task: `${o.serviceName} (${patient(o)})`, chip: chipFor(o.status), chipText: statusAr[o.status] || o.status })));

  fillGauge(done.length, orders.filter((o) => ACTIVE.includes(o.status)).length, orders.filter((o) => WAITING.includes(o.status)).length);

  const run = async () => {
    const d = await nearestDelivery(orders, details);
    startCountdown(d?.date || null, d ? `${patient(d.order)} · ${d.order.orderNumber} · ${shortDate(d.date)}` : null, d?.href);
  };
  byId('timer-refresh')?.addEventListener('click', run);
  await run();
}

export async function fillLabHome(orders, fin, statusAr, fmtEgp) {
  const fresh = orders.filter((o) => o.status === 'PendingLabReview');
  set('kpi-1', nf.format(fresh.length));
  const ws = weekStart();
  set('kpi-1-n', nf.format(orders.filter((o) => new Date(o.createdAt) >= ws).length));
  set('kpi-2', nf.format(orders.filter((o) => ACTIVE.includes(o.status)).length));
  const monthNet = (fin?.orders || []).filter((o) => thisMonth(o.createdAt)).reduce((a, o) => a + Number(o.labNetEgp || 0), 0);
  set('kpi-3', fmtEgp(monthNet));
  set('kpi-4', fmtEgp(fin?.availableToWithdrawEgp ?? 0));
  fillBars(orders.map((o) => o.createdAt));

  const details = '/lab-order-details.html';
  const next = fresh[0] || orders.find((o) => ACTIVE.includes(o.status));
  if (next) {
    set('rem-title', `${next.serviceName} — ${next.dentistName}`);
    set('rem-time', `${next.orderNumber} · ${statusAr[next.status] || next.status}`);
    const a = byId('rem-link'); if (a) a.href = `${details}?orderNumber=${encodeURIComponent(next.orderNumber)}`;
  }
  fillList(orders.map((o) => ({ icon: (o.serviceName || 'ت').charAt(0), title: `${o.serviceName} — ${o.dentistName}`, sub: `${o.orderNumber} · ${shortDate(o.createdAt)}`, href: `${details}?orderNumber=${encodeURIComponent(o.orderNumber)}` })));

  fillTeam((fin?.dentistStatements || []).map((d) => ({
    name: d.dentistName, lead: d.clinicName || 'عيادة أسنان', task: `· ${nf.format(d.activeCount || 0)} حالة جارية`,
    chip: d.activeCount ? 'st-wip' : d.completedCount ? 'st-ok' : 'st-bad', chipText: d.activeCount ? 'حالات جارية' : d.completedCount ? 'مكتمل' : 'جديد',
  })));

  const stages = orders.filter((o) => !['Rejected', 'Cancelled'].includes(o.status)).flatMap((o) => o.stages || []);
  fillGauge(stages.filter((s) => s.status === 'Completed').length,
    stages.filter((s) => ['InProgress', 'SubmittedForReview', 'Approved', 'RevisionRequested'].includes(s.status)).length,
    stages.filter((s) => ['NotStarted', 'Blocked'].includes(s.status)).length);

  const run = async () => {
    const d = await nearestDelivery(orders, details);
    startCountdown(d?.date || null, d ? `${d.order.serviceName} · ${d.order.orderNumber} · ${shortDate(d.date)}` : null, d?.href);
  };
  byId('timer-refresh')?.addEventListener('click', run);
  await run();
}

export function fillAdminHome(d, fmtEgp) {
  const m = d.metrics || {};
  set('kpi-1', nf.format(m.totalUsers || 0));
  set('kpi-1-n', nf.format((d.users || []).filter((u) => thisMonth(u.createdAt)).length));
  set('kpi-2', nf.format(m.totalLabs || 0));
  set('kpi-2-n', nf.format(m.verifiedLabs || 0));
  set('kpi-3', nf.format(m.totalOrders || 0));
  set('kpi-3-n', nf.format((d.orders || []).filter((o) => thisMonth(o.createdAt)).length));
  set('kpi-4', fmtEgp((m.totalCommissionRevenueEgp || 0) + (m.totalSubscriptionRevenueEgp || 0)));
  fillBars((d.orders || []).map((o) => o.createdAt));

  const pendingLabs = (d.laboratories || []).filter((l) => l.verificationStatus !== 'Verified');
  if (pendingLabs[0]) {
    set('rem-title', pendingLabs[0].name);
    set('rem-time', `${pendingLabs[0].governorate || ''} · السجل التجاري ${pendingLabs[0].commercialRegister || '—'}`);
  }
  const waiting = [
    ...pendingLabs.map((l) => ({ icon: 'م', title: `توثيق ${l.name}`, sub: l.governorate || '', href: '#labs' })),
    ...(d.settlements || []).filter((s) => s.status !== 'Paid').map((s) => ({ icon: 'ت', title: `دفعة تسوية ${fmtEgp(s.netPayableEgp)}`, sub: shortDate(s.createdAt), href: '#settlements' })),
  ];
  fillList(waiting.length ? waiting : (d.orders || []).map((o) => ({ icon: 'ط', title: `${o.orderNumber} — ${o.serviceName}`, sub: `${o.laboratoryName} · ${shortDate(o.createdAt)}`, href: '#audit' })));

  const roleAr = { Dentist: 'طبيب أسنان', LaboratoryOwner: 'صاحب معمل', LaboratoryStaff: 'فني معمل', Admin: 'مدير' };
  fillTeam((d.users || []).slice(0, 5).map((u) => ({
    name: u.fullName, lead: roleAr[u.role] || u.role, task: `· ${shortDate(u.createdAt)}`,
    chip: u.isSuspended ? 'st-bad' : 'st-ok', chipText: u.isSuspended ? 'موقوف' : 'نشط',
  })));

  const labs = d.laboratories || [];
  fillGauge(labs.filter((l) => l.verificationStatus === 'Verified').length,
    labs.filter((l) => l.verificationStatus === 'Pending').length,
    labs.filter((l) => !['Verified', 'Pending'].includes(l.verificationStatus)).length);

  const now = new Date(); const monthEnd = new Date(now.getFullYear(), now.getMonth() + 1, 1);
  startCountdown(monthEnd, null, '#settlements');
  byId('timer-refresh')?.addEventListener('click', () => startCountdown(monthEnd, null, '#settlements'));
}
