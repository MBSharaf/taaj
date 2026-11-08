/* Taaj — in-browser demo API for the static GitHub Pages build ONLY.
 * The real site (ASP.NET Core) never loads this file: build_pages.py injects it into the Pages copy.
 * It answers the front end's /api/v1/* calls from demo data recorded from the development seed
 * (assets/js/demo/fixtures.json), keeps changes in this browser's localStorage, and follows the same
 * rules as the backend: stage order, 20% / 80% milestones, each party sees only its own commission.
 * No real users, no secrets: the three demo accounts are the public development seed accounts.
 */
(() => {
  'use strict';
  const KEY = 'taaj_demo_state_v2';
  const SESSION = 'taaj_demo_session_v1';
  const BASE = location.pathname.startsWith('/taaj/') ? '/taaj' : '';
  const realFetch = window.fetch.bind(window);
  const STAGES = ['Design', 'Milling', 'Layering', 'Glazing'];
  const STAGE_STATUS = ['NotStarted', 'InProgress', 'SubmittedForReview', 'RevisionRequested', 'Approved', 'Completed', 'Blocked'];
  const PATIENTS = ['أحمد محمود', 'سارة علي حسن', 'محمد عبد الله', 'منى السيد', 'خالد إبراهيم', 'نورهان فتحي', 'يوسف مصطفى', 'هبة عادل', 'عمر طارق', 'دينا شريف', 'مصطفى كمال', 'ريم أسامة', 'إسلام نبيل'];

  const uuid = () => (crypto.randomUUID ? crypto.randomUUID() : 'd' + Math.random().toString(16).slice(2) + Date.now().toString(16));
  const now = () => new Date().toISOString();
  const r2 = (n) => Math.round(n * 100) / 100;
  const clone = (o) => JSON.parse(JSON.stringify(o));

  let state = null;
  const save = () => { try { localStorage.setItem(KEY, JSON.stringify(state)); } catch { /* quota: keep in memory */ } };

  async function load() {
    try { const s = JSON.parse(localStorage.getItem(KEY) || 'null'); if (s && s.v === 2) { state = s; return; } } catch { /* reseed */ }
    const res = await realFetch(`${BASE}/assets/js/demo/fixtures.json`);
    const f = await res.json();
    state = { v: 2, public: f.public, users: f.users, seq: 0 };
    // Friendly patient names instead of internal test references.
    const names = {};
    for (const u of Object.values(state.users)) {
      for (const [k, v] of Object.entries(u.get)) {
        const fix = (o) => {
          if (!o || typeof o !== 'object' || !o.orderNumber || typeof o.caseReference !== 'string') return;
          if (!/[؀-ۿ]/.test(o.caseReference) || o.caseReference.includes('تجربة')) {
            names[o.orderNumber] ||= PATIENTS[Object.keys(names).length % PATIENTS.length];
            o.caseReference = names[o.orderNumber];
          }
        };
        const walk = (o) => { if (Array.isArray(o)) o.forEach(walk); else if (o && typeof o === 'object') { fix(o); Object.values(o).forEach(walk); } };
        walk(v); void k;
        const addPatient = (o) => { if (Array.isArray(o)) o.forEach(addPatient); else if (o && typeof o === 'object') { if (o.orderNumber && o.caseReference && o.patientName === undefined) o.patientName = o.caseReference; Object.values(o).forEach(addPatient); } };
        addPatient(v);
      }
    }
    save();
  }
  const ready = load();

  /* ---------------- helpers ---------------- */
  const session = () => { try { return JSON.parse(localStorage.getItem(SESSION) || 'null'); } catch { return null; } };
  const role = () => session()?.role || null;
  const U = (r = role()) => state.users[r];
  const roleName = (r) => ({ doctor: 'Dentist', lab: 'LaboratoryOwner', admin: 'Admin' }[r]);
  const json = (body, status = 200) => new Response(JSON.stringify(body).replace(/"(linkUrl|downloadUrl)":"\//g, `"$1":"${BASE}/`), { status, headers: { 'Content-Type': 'application/json' } });
  const problem = (detail, status = 400) => json({ title: 'Bad Request', status, detail }, status);
  const labSlug = () => state.users.lab.get['/api/v1/labs/my']?.slug;

  function details(orderNumber) {
    const out = [];
    for (const [r, u] of Object.entries(state.users)) {
      const d = u.get[`/api/v1/orders/${orderNumber}`];
      if (d) out.push([r, d]);
    }
    return out;
  }
  function listItems(orderNumber) {
    const out = [];
    for (const [r, u] of Object.entries(state.users)) {
      const it = (u.get['/api/v1/orders']?.items || []).find((o) => o.orderNumber === orderNumber);
      if (it) out.push([r, it]);
    }
    return out;
  }
  function paidDue(d) {
    const ms = d.milestones || [];
    return { paid: ms.filter((m) => m.status === 'Paid').reduce((a, m) => a + m.amountEgp, 0), due: ms.filter((m) => m.status === 'Due').reduce((a, m) => a + m.amountEgp, 0) };
  }
  function syncLists(orderNumber) {
    const d0 = details(orderNumber)[0]?.[1];
    if (!d0) return;
    const { paid, due } = paidDue(d0);
    for (const [, it] of listItems(orderNumber)) {
      Object.assign(it, { status: d0.status, proposedPriceEgp: d0.proposedPriceEgp, confirmedPriceEgp: d0.confirmedPriceEgp, caseReference: d0.caseReference, paidEgp: paid, dueEgp: due });
      it.stages = d0.stages.map((s) => ({ stage: s.stage, status: s.status }));
    }
    const lf = state.users.lab.get['/api/v1/finance/lab'];
    for (const o of [...(lf?.orders || []), ...(lf?.dentistStatements || []).flatMap((s) => s.orders || [])]) {
      if (o.orderNumber !== orderNumber) continue;
      const total = d0.confirmedPriceEgp ?? d0.estimatedPriceEgp;
      Object.assign(o, { status: d0.status, totalEgp: total, paidEgp: paid, remainingEgp: r2(total - paid) });
    }
    const ad = state.users.admin.get['/api/v1/admin/overview'];
    for (const o of ad?.orders || []) if (o.orderNumber === orderNumber) Object.assign(o, { status: d0.status, confirmedPriceEgp: d0.confirmedPriceEgp });
    // Doctor finance summary follows the doctor's own orders.
    const fd = state.users.doctor.get['/api/v1/finance/doctor'];
    if (fd) {
      const mine = Object.entries(state.users.doctor.get).filter(([k]) => /^\/api\/v1\/orders\/[^/]+$/.test(k)).map(([, v]) => v);
      let confirmed = 0; let paidAll = 0; let dueAll = 0;
      for (const x of mine) { if (x.confirmedPriceEgp) confirmed += x.confirmedPriceEgp; const pd = paidDue(x); paidAll += pd.paid; dueAll += pd.due; }
      Object.assign(fd, { totalConfirmedEgp: r2(confirmed), totalDueNowEgp: r2(dueAll), totalRemainingEgp: r2(confirmed - paidAll) });
      const fo = fd.orders.find((o) => o.orderNumber === orderNumber);
      const dv = state.users.doctor.get[`/api/v1/orders/${orderNumber}`];
      if (dv) {
        if (fo) Object.assign(fo, { status: dv.status, confirmedPriceEgp: dv.confirmedPriceEgp, caseReference: dv.caseReference, milestones: clone(dv.milestones) });
        else fd.orders.unshift({ orderNumber, caseReference: dv.caseReference, serviceName: dv.serviceName, laboratoryName: dv.laboratory.name, confirmedPriceEgp: dv.confirmedPriceEgp, dentistCommissionEgp: 0, status: dv.status, milestones: clone(dv.milestones) });
      }
    }
  }
  function history(orderNumber, action, reason = null) {
    for (const [, d] of details(orderNumber)) d.history.push({ id: uuid(), actorRole: roleName(role()), action, reason, createdAt: now() });
  }
  function notify(targetRole, type, title, body, link) {
    const n = U(targetRole)?.get['/api/v1/notifications'];
    if (!n) return;
    n.items.unshift({ id: uuid(), type, title, body, linkUrl: link, isRead: false, createdAt: now() });
    n.unreadCount = (n.unreadCount || 0) + 1;
    const me = U(targetRole).get['/api/v1/auth/me'];
    if (me) me.unreadNotifications = n.unreadCount;
  }
  const orderLink = (who, n) => `/${who === 'doctor' ? 'doctor' : 'lab'}-order-details.html?orderNumber=${n}`;
  const pct = () => {
    const s = state.users.admin.get['/api/v1/admin/overview']?.settings || {};
    return { lab: s.commissionPercent ?? 2, dentist: s.dentistCommissionPercent ?? 1 };
  };
  function milestoneFor(r, base) {
    const p = pct();
    const m = { ...base };
    const showDentist = r !== 'lab';
    const showLab = r !== 'doctor';
    m.dentistFeeEgp = showDentist ? r2(m.amountEgp * p.dentist / 100) : null;
    m.totalWithDentistFeeEgp = showDentist ? r2(m.amountEgp + m.dentistFeeEgp) : null;
    m.labCommissionEgp = showLab ? r2(m.amountEgp * p.lab / 100) : null;
    m.labNetEgp = showLab ? r2(m.amountEgp - m.labCommissionEgp) : null;
    return m;
  }
  function finalize(orderNumber, total) {
    const stages = details(orderNumber)[0][1].stages.map((s) => s.stage);
    const first = Math.round(total * 20) / 100;
    const plan = [{ id: uuid(), sequence: 1, percentage: 20, amountEgp: r2(first), triggerType: stages.includes('Design') ? 'StageApproved' : 'OrderFinalized', triggerStage: stages.includes('Design') ? 'Design' : null, status: stages.includes('Design') ? 'Locked' : 'Due', dueAt: stages.includes('Design') ? null : now(), paidAt: null }];
    if (total - first > 0) plan.push({ id: uuid(), sequence: 2, percentage: 80, amountEgp: r2(total - first), triggerType: 'StageCompleted', triggerStage: stages[stages.length - 1], status: 'Locked', dueAt: null, paidAt: null });
    for (const [r, d] of details(orderNumber)) {
      Object.assign(d, { confirmedPriceEgp: total, status: 'Accepted', finalizedAt: now() });
      d.milestones = plan.map((m) => milestoneFor(r, m));
    }
    history(orderNumber, `تثبيت الشروط التجارية والسعر النهائي (${total.toFixed(2)} ج.م) — الدفعة الأولى 20% والدفعة النهائية 80%`);
  }

  function canTransition(from, to, actor, review) {
    const k = `${from}>${to}>${actor}`;
    return ({
      'NotStarted>InProgress>lab': true, 'RevisionRequested>InProgress>lab': true, 'Blocked>InProgress>lab': true,
      'NotStarted>Blocked>lab': true, 'InProgress>Blocked>lab': true,
      'InProgress>SubmittedForReview>lab': review, 'InProgress>Completed>lab': !review,
      'SubmittedForReview>Approved>doctor': true, 'SubmittedForReview>RevisionRequested>doctor': true,
      'Approved>Completed>lab': true,
    })[k] === true;
  }

  /* ---------------- routes ---------------- */
  async function handle(method, url, body, form) {
    await ready;
    const u = new URL(url, location.origin);
    const p = u.pathname.replace(/^\/taaj/, '');
    const q = u.searchParams;
    const r = role();
    const seg = p.split('/').filter(Boolean); // api v1 ...

    // ---- auth ----
    if (p === '/api/v1/auth/login' && method === 'POST') {
      const hit = Object.entries(state.users).find(([, x]) => x.email.toLowerCase() === String(body?.email || '').trim().toLowerCase() && x.password === body?.password);
      if (!hit) return problem('البريد الإلكتروني أو كلمة المرور غير صحيحة. استخدم أحد الحسابات التجريبية.', 401);
      localStorage.setItem(SESSION, JSON.stringify({ role: hit[0] }));
      return json(hit[1].login);
    }
    if (p === '/api/v1/auth/logout') { localStorage.removeItem(SESSION); return json({ ok: true }); }
    if (p === '/api/v1/auth/register') return problem('إنشاء حساب جديد غير متاح في نسخة العرض. استخدم أحد الحسابات التجريبية في صفحة الدخول.');

    // ---- public ----
    if (p === '/api/v1/labs' && method === 'GET') {
      const all = clone(state.public['/api/v1/labs']);
      let items = all.items;
      const term = (q.get('q') || '').trim();
      if (term) items = items.filter((l) => l.name.includes(term) || l.description?.includes(term) || l.services.some((s) => s.name.includes(term)));
      if (q.get('governorate')) items = items.filter((l) => l.governorate === q.get('governorate'));
      if (q.get('service')) items = items.filter((l) => l.services.some((s) => s.name.includes(q.get('service'))));
      if (q.get('maxPrice')) items = items.filter((l) => l.services.some((s) => s.priceEgp <= Number(q.get('maxPrice'))));
      if (q.get('verifiedOnly') === 'true') items = items.filter((l) => l.isVerified);
      if (q.get('courierOnly') === 'true') items = items.filter((l) => l.offersCourier);
      if (q.get('sort') === 'priceAsc') items.sort((a, b) => a.minPriceEgp - b.minPriceEgp);
      if (q.get('sort') === 'ratingDesc') items.sort((a, b) => b.averageRating - a.averageRating);
      return json({ ...all, total: items.length, items });
    }
    if (seg[2] === 'labs' && seg.length === 4 && seg[3] !== 'my' && method === 'GET') {
      const lab = state.public[`/api/v1/labs/${seg[3]}`];
      return lab ? json(lab) : problem('المعمل غير موجود.', 404);
    }

    if (!r) return json({ title: 'Unauthorized', status: 401 }, 401);
    const G = U(r).get;

    // ---- simple GETs (recorded, kept up to date by the actions below) ----
    if (method === 'GET') {
      if (p === '/api/v1/orders') {
        const term = (q.get('q') || '').trim();
        const all = clone(G['/api/v1/orders']);
        if (term) all.items = all.items.filter((o) => [o.orderNumber, o.caseReference, o.patientName, o.serviceName, o.laboratoryName, o.dentistName].some((x) => String(x || '').includes(term)));
        all.total = all.items.length;
        return json(all);
      }
      if (p === '/api/v1/finance/lab' && q.get('q')) {
        const term = q.get('q').trim(); const f = clone(G['/api/v1/finance/lab']);
        f.dentistStatements = f.dentistStatements.filter((s) => [s.dentistName, s.clinicName, s.dentistEmail].some((x) => String(x || '').includes(term)));
        f.orders = f.orders.filter((o) => [o.orderNumber, o.caseReference, o.dentistName, o.clinicName].some((x) => String(x || '').includes(term)));
        return json(f);
      }
      if (/^\/api\/v1\/orders\/[^/]+\/files\//.test(p)) return new Response(new Blob(['ملف تجريبي — Taaj demo file'], { type: 'text/plain' }), { status: 200 });
      if (G[p]) return json(G[p]);
      return problem('غير متاح في نسخة العرض.', 404);
    }

    // ---- orders ----
    if (p === '/api/v1/orders' && method === 'POST' && r === 'doctor') {
      const all = Object.values(state.users).flatMap((x) => x.get['/api/v1/orders']?.items || []).map((o) => Number(String(o.orderNumber).replace(/\D/g, '')) || 0);
      const orderNumber = `LS-${String(Math.max(0, ...all) + 1).padStart(6, '0')}`;
      const lab = state.public['/api/v1/labs'].items.find((l) => l.slug === body.laboratorySlug);
      if (!lab) return problem('المعمل غير موجود.', 404);
      const me = G['/api/v1/auth/me'];
      const stages = (body.stages || []).map((s) => (typeof s === 'number' ? STAGES[s - 1] : s)).filter(Boolean);
      if (!stages.length) return problem('يجب اختيار مرحلة تصنيع واحدة على الأقل.');
      const created = now();
      const detail = {
        id: uuid(), orderNumber, caseReference: body.caseReference || body.patientName || orderNumber, patientName: body.patientName || body.caseReference || null, serviceName: body.serviceName, toothNumbers: body.toothNumbers,
        shade: body.shade, instructions: body.instructions || '', status: 'PendingLabReview', rejectionReason: null, priceRevisionNote: null,
        estimatedPriceEgp: Number(body.estimatedPriceEgp), proposedPriceEgp: null, confirmedPriceEgp: null, commissionRateBpsSnapshot: null,
        dentistCommissionPercent: null, labCommissionPercent: null, deliveryMethod: body.deliveryMethod === 0 ? 'Pickup' : 'LabCourier', deliveryStatus: 'NotRequested',
        pickupAddress: body.pickupAddress || null, requiredDeliveryDate: body.requiredDeliveryDate || null, createdAt: created, finalizedAt: null,
        dentist: { dentistUserId: me.id, name: me.fullName, phone: me.phone }, laboratory: { laboratoryId: lab.id, name: lab.name, slug: lab.slug },
        stages: stages.map((s) => ({ id: uuid(), stage: s, status: 'NotStarted', requiresDentistReview: s === 'Design', notes: '', lastRevisionReason: null, startedAt: null, submittedForReviewAt: null, approvedAt: null, completedAt: null })),
        milestones: [], files: [], history: [{ id: uuid(), actorRole: 'Dentist', action: 'إنشاء الطلب وإرساله للمعمل', reason: null, createdAt: created }],
        conversationId: uuid(), messages: [{ id: uuid(), senderUserId: me.id, senderName: me.fullName, senderRole: 'Dentist', body: `تم إرسال الطلب ${orderNumber} (${body.serviceName}).`, sentAt: created }],
      };
      const p0 = pct();
      const views = [['doctor', { dentistCommissionPercent: p0.dentist, labCommissionPercent: null }], ['admin', { dentistCommissionPercent: p0.dentist, labCommissionPercent: p0.lab }]];
      if (lab.slug === labSlug()) views.push(['lab', { dentistCommissionPercent: null, labCommissionPercent: p0.lab }]);
      for (const [vr, extra] of views) {
        state.users[vr].get[`/api/v1/orders/${orderNumber}`] = { ...clone(detail), ...extra };
        const list = state.users[vr].get['/api/v1/orders'];
        list.items.unshift({ id: detail.id, orderNumber, caseReference: detail.caseReference, patientName: detail.patientName, serviceName: detail.serviceName, toothNumbers: detail.toothNumbers, shade: detail.shade, status: detail.status, deliveryStatus: detail.deliveryStatus, estimatedPriceEgp: detail.estimatedPriceEgp, proposedPriceEgp: null, confirmedPriceEgp: null, dentistName: me.fullName, laboratoryName: lab.name, laboratorySlug: lab.slug, createdAt: created, stages: detail.stages.map((s) => ({ stage: s.stage, status: s.status })), paidEgp: 0, dueEgp: 0 });
        list.total = list.items.length;
      }
      const lf = state.users.lab.get['/api/v1/finance/lab'];
      if (lab.slug === labSlug() && lf) lf.orders.unshift({ id: detail.id, orderNumber, caseReference: detail.caseReference, serviceName: detail.serviceName, dentistUserId: me.id, dentistName: me.fullName, clinicName: me.clinicName, status: detail.status, totalEgp: detail.estimatedPriceEgp, paidEgp: 0, remainingEgp: detail.estimatedPriceEgp, platformCommissionEgp: 0, labNetEgp: 0, createdAt: created });
      const ad = state.users.admin.get['/api/v1/admin/overview'];
      if (ad) { ad.orders.unshift({ orderNumber, serviceName: detail.serviceName, dentistName: me.fullName, laboratoryName: lab.name, status: detail.status, confirmedPriceEgp: null, commissionRateBpsSnapshot: null, createdAt: created }); ad.metrics.totalOrders += 1; }
      if (lab.slug === labSlug()) notify('lab', 'NewOrder', `طلب جديد ${orderNumber}`, `${detail.serviceName} — ${detail.caseReference}`, orderLink('lab', orderNumber));
      save();
      return json({ id: detail.id, orderNumber, status: 'PendingLabReview' }, 201);
    }

    const om = p.match(/^\/api\/v1\/orders\/([^/]+)(?:\/(.+))?$/);
    if (om) {
      const n = decodeURIComponent(om[1]); const action = om[2] || '';
      const views = details(n);
      const mine = G[`/api/v1/orders/${n}`];
      if (!mine) return problem('الطلب غير موجود.', 404);
      const d = mine;

      if (action === 'accept' && r === 'lab') {
        if (d.status !== 'PendingLabReview') return problem('لا يمكن قبول الطلب في حالته الحالية.');
        finalize(n, d.estimatedPriceEgp);
        notify('doctor', 'OrderAccepted', `قبل المعمل الطلب ${n}`, 'تم تثبيت السعر والدفعتين 20% و80%.', orderLink('doctor', n));
      } else if (action === 'reject' && r === 'lab') {
        if (!String(body?.reason || '').trim()) return problem('سبب رفض الطلب إلزامي.');
        if (!['PendingLabReview', 'PriceRevisionProposed'].includes(d.status)) return problem('لا يمكن رفض الطلب بعد تثبيته.');
        for (const [, x] of views) Object.assign(x, { status: 'Rejected', rejectionReason: body.reason.trim() });
        history(n, 'رفض الطلب من المعمل', body.reason.trim());
        notify('doctor', 'OrderRejected', `تم اعتذار المعمل عن الطلب ${n}`, `السبب: ${body.reason.trim()}`, orderLink('doctor', n));
      } else if (action === 'propose-price' && r === 'lab') {
        if (!(body?.proposedPriceEgp > 0) || !String(body?.note || '').trim()) return problem('السعر المقترح ومبرر التعديل مطلوبان.');
        if (d.status !== 'PendingLabReview') return problem('لا يمكن تعديل السعر بعد تثبيت الطلب.');
        for (const [, x] of views) Object.assign(x, { proposedPriceEgp: Number(body.proposedPriceEgp), priceRevisionNote: body.note.trim(), status: 'PriceRevisionProposed' });
        history(n, `اقتراح سعر معدّل (${Number(body.proposedPriceEgp).toFixed(2)} ج.م)`, body.note.trim());
        notify('doctor', 'PriceProposed', `مقترح سعر للطلب ${n}`, `اقترح المعمل سعر ${Number(body.proposedPriceEgp).toFixed(2)} ج.م بانتظار موافقتك.`, orderLink('doctor', n));
      } else if (action === 'approve-price' && r === 'doctor') {
        if (d.status !== 'PriceRevisionProposed' || !d.proposedPriceEgp) return problem('لا يوجد مقترح سعر بانتظار الموافقة.');
        finalize(n, d.proposedPriceEgp);
        notify('lab', 'PriceApproved', `وافق الطبيب على السعر للطلب ${n}`, '', orderLink('lab', n));
      } else if (action.startsWith('stages/')) {
        const stage = action.split('/')[1];
        const target = typeof body?.targetStatus === 'number' ? STAGE_STATUS[body.targetStatus] : body?.targetStatus;
        if (!['Accepted', 'InProduction'].includes(d.status)) return problem('يجب قبول الطلب وتثبيت شروطه التجارية قبل تحديث مراحل الإنتاج.');
        const row = d.stages.find((s) => s.stage === stage);
        if (!row) return problem('هذه المرحلة غير مدرجة في هذا الطلب.');
        const actor = r === 'doctor' ? 'doctor' : 'lab';
        if (!canTransition(row.status, target, actor, row.requiresDentistReview)) return problem(`انتقال غير مسموح من ${row.status} إلى ${target}.`);
        if (target === 'InProgress' && row.status === 'NotStarted' && d.stages.some((s) => STAGES.indexOf(s.stage) < STAGES.indexOf(stage) && s.status !== 'Completed')) return problem('لا يمكن بدء هذه المرحلة قبل اكتمال جميع المراحل السابقة لها.');
        if (['Blocked', 'RevisionRequested'].includes(target) && !String(body?.reason || '').trim()) return problem('يجب تسجيل سبب واضح عند طلب تعديل أو إيقاف المرحلة.');
        const t = now();
        for (const [, x] of views) {
          const s = x.stages.find((y) => y.stage === stage);
          s.status = target; if (body?.notes) s.notes = body.notes;
          if (target === 'InProgress' && !s.startedAt) { s.startedAt = t; x.status = 'InProduction'; }
          if (target === 'SubmittedForReview') s.submittedForReviewAt = t;
          if (target === 'RevisionRequested') s.lastRevisionReason = body.reason.trim();
          if (target === 'Approved') { s.approvedAt = t; x.milestones.filter((m) => m.triggerType === 'StageApproved' && m.triggerStage === stage && m.status === 'Locked').forEach((m) => { m.status = 'Due'; m.dueAt = t; }); }
          if (target === 'Completed') {
            s.completedAt = t;
            x.milestones.filter((m) => m.triggerType === 'StageCompleted' && m.triggerStage === stage && m.status === 'Locked').forEach((m) => { m.status = 'Due'; m.dueAt = t; });
            if (x.stages.every((y) => y.status === 'Completed')) x.status = 'ReadyForDelivery';
          }
        }
        history(n, `تحديث مرحلة ${stage} إلى ${target}`, body?.reason || body?.notes || null);
        const other = actor === 'lab' ? 'doctor' : 'lab';
        notify(other, 'StageUpdated', `تحديث مرحلة ${stage} للطلب ${n}`, `الحالة الجديدة: ${target}`, orderLink(other, n));
        syncLists(n); save();
        return json({ orderNumber: n, stage, status: target, orderStatus: d.status });
      } else if (action === 'messages') {
        const me = G['/api/v1/auth/me'];
        const text = String(body?.body || '').trim();
        if (!text) return problem('اكتب رسالة.');
        const msg = { id: uuid(), senderUserId: me.id, senderName: me.fullName, senderRole: roleName(r), body: text, sentAt: now() };
        for (const [, x] of views) x.messages.push({ ...msg });
        const other = r === 'doctor' ? 'lab' : 'doctor';
        notify(other, 'NewMessage', `رسالة جديدة على الطلب ${n}`, text.slice(0, 80), orderLink(other, n));
        save();
        return json(msg);
      } else if (action === 'files' && method === 'POST') {
        const file = form?.get?.('file');
        const name = file?.name || 'scan.stl';
        const meta = { id: uuid(), originalFileName: name, extension: (name.match(/\.[^.]+$/) || [''])[0].toLowerCase(), sizeBytes: file?.size || 0, stage: form?.get?.('stage') || null, uploadedAt: now(), downloadUrl: `/api/v1/orders/${n}/files/${uuid()}` };
        for (const [, x] of views) x.files.push({ ...meta });
        save();
        return json(meta);
      } else if (action === 'delivery') {
        for (const [, x] of views) x.deliveryStatus = typeof body?.status === 'number' ? ['NotRequested', 'Requested', 'PickedUp', 'OutForDelivery', 'Delivered'][body.status] || x.deliveryStatus : body?.status || x.deliveryStatus;
      } else {
        return problem('غير متاح في نسخة العرض.', 404);
      }
      syncLists(n); save();
      return json({ orderNumber: n, status: details(n)[0][1].status });
    }

    // ---- payments (sandbox) ----
    if (p === '/api/v1/payments/checkout' && r === 'doctor') {
      const d = G[`/api/v1/orders/${body?.orderNumber}`];
      const m = d?.milestones?.find((x) => x.sequence === Number(body?.milestoneSequence));
      if (!m) return problem('الدفعة غير موجودة.', 404);
      if (m.status === 'Paid') return problem('تم سداد هذه الدفعة بالفعل.', 409);
      if (m.status !== 'Due') return problem('هذه الدفعة غير مستحقة بعد؛ تُفتح بعد اكتمال المرحلة المرتبطة بها.');
      const checkoutId = `chk_demo_${uuid().replace(/-/g, '')}`;
      state.checkouts = { ...(state.checkouts || {}), [checkoutId]: { orderNumber: d.orderNumber, sequence: m.sequence } };
      save();
      return json({ id: uuid(), provider: 'Sandbox', checkoutId, checkoutUrl: null, amountEgp: m.amountEgp, currency: 'EGP' });
    }
    if (p.startsWith('/api/v1/payments/sandbox-confirm/') && r === 'doctor') {
      const c = state.checkouts?.[decodeURIComponent(seg[4] || '')];
      if (!c) return problem('عملية الدفع غير موجودة.', 404);
      const n = c.orderNumber; const t = now(); let amount = 0;
      for (const [, x] of details(n)) { const m = x.milestones.find((y) => y.sequence === c.sequence); if (m && m.status !== 'Paid') { m.status = 'Paid'; m.paidAt = t; amount = m.amountEgp; } }
      if (!amount) return problem('تم سداد هذه الدفعة بالفعل.', 409);
      delete state.checkouts[seg[4]];
      const p0 = pct();
      const fd = state.users.doctor.get['/api/v1/finance/doctor'];
      if (fd) {
        fd.totalPaidEgp = r2(fd.totalPaidEgp + amount);
        fd.dentistCommissionEgp = r2((fd.dentistCommissionEgp || 0) + amount * p0.dentist / 100);
        fd.payments.unshift({ id: uuid(), providerCheckoutId: `chk_${t}`, providerTransactionRef: `txn_demo_${Date.now()}`, amountEgp: amount, dentistCommissionEgp: r2(amount * p0.dentist / 100), refundedEgp: 0, status: 'Succeeded', createdAt: t, confirmedAt: t });
        const fo = fd.orders.find((o) => o.orderNumber === n);
        if (fo) fo.milestones = clone(details(n).find(([vr]) => vr === 'doctor')?.[1].milestones || []);
      }
      const lf = state.users.lab.get['/api/v1/finance/lab'];
      if (lf && details(n).some(([vr]) => vr === 'lab')) {
        const comm = r2(amount * p0.lab / 100);
        lf.grossPaidEgp = r2(lf.grossPaidEgp + amount); lf.totalCommissionEgp = r2(lf.totalCommissionEgp + comm);
        lf.netEarnedEgp = r2(lf.netEarnedEgp + amount - comm); lf.unsettledPayableEgp = r2(lf.unsettledPayableEgp + amount - comm);
        lf.availableToWithdrawEgp = r2(lf.availableToWithdrawEgp + amount - comm);
        for (const o of [...lf.orders, ...lf.dentistStatements.flatMap((s) => s.orders || [])]) if (o.orderNumber === n) { o.platformCommissionEgp = r2((o.platformCommissionEgp || 0) + comm); o.labNetEgp = r2((o.labNetEgp || 0) + amount - comm); }
        notify('lab', 'PaymentReceived', `تم سداد دفعة للطلب ${n}`, `${amount.toFixed(2)} ج.م`, orderLink('lab', n));
      }
      const ad = state.users.admin.get['/api/v1/admin/overview'];
      if (ad) { ad.metrics.totalCollectedEgp = r2(ad.metrics.totalCollectedEgp + amount); ad.metrics.labCommissionRevenueEgp = r2(ad.metrics.labCommissionRevenueEgp + amount * p0.lab / 100); }
      history(n, `تأكيد سداد الدفعة ${c.sequence} (${amount.toFixed(2)} ج.م)`);
      syncLists(n); save();
      return json({ ok: true, status: 'Succeeded' });
    }

    // ---- notifications ----
    if (/^\/api\/v1\/notifications\/[^/]+\/read$/.test(p)) {
      const nlist = G['/api/v1/notifications'];
      const it = nlist?.items.find((x) => x.id === seg[3]);
      if (it && !it.isRead) { it.isRead = true; nlist.unreadCount = Math.max(0, nlist.unreadCount - 1); G['/api/v1/auth/me'].unreadNotifications = nlist.unreadCount; save(); }
      return json({ ok: true });
    }

    // ---- lab storefront ----
    if (p.startsWith('/api/v1/labs/my') && r === 'lab') {
      const my = G['/api/v1/labs/my'];
      const pub = state.public[`/api/v1/labs/${my.slug}`];
      const pubItem = state.public['/api/v1/labs'].items.find((l) => l.slug === my.slug);
      const sync = () => {
        if (pub) Object.assign(pub, { name: my.name, description: my.description, services: clone(my.services), equipment: clone(my.equipment), portfolio: clone(my.portfolio) });
        if (pubItem) { pubItem.services = my.services.map((s) => ({ id: s.id, name: s.name, category: s.category, priceEgp: s.priceEgp, estimatedDays: s.estimatedDays, stages: s.stages })); pubItem.minPriceEgp = Math.min(...my.services.map((s) => s.priceEgp)); pubItem.name = my.name; }
      };
      let out = { ok: true };
      if (p === '/api/v1/labs/my/profile') Object.assign(my, Object.fromEntries(Object.entries(body || {}).filter(([, v]) => v !== undefined && v !== null)));
      else if (p === '/api/v1/labs/my/equipment' && method === 'POST') my.equipment.push(out = { id: uuid(), name: body.name, brandAndModel: body.brandAndModel });
      else if (p.startsWith('/api/v1/labs/my/equipment/') && method === 'DELETE') my.equipment = my.equipment.filter((e) => e.id !== seg[5]);
      else if (p === '/api/v1/labs/my/portfolio' && method === 'POST') my.portfolio.push(out = { id: uuid(), title: body.title, caption: body.caption, shade: body.shade });
      else if (p.startsWith('/api/v1/labs/my/portfolio/') && method === 'DELETE') my.portfolio = my.portfolio.filter((e) => e.id !== seg[5]);
      else if (p === '/api/v1/labs/my/services' && method === 'POST') {
        if (!body?.name || !(body.priceEgp > 0)) return problem('اسم الخدمة والسعر مطلوبان.');
        my.services.push(out = { id: uuid(), category: body.category, name: body.name, description: body.description, priceEgp: Number(body.priceEgp), estimatedDays: body.estimatedDays, stages: body.stages || [] });
      } else if (p.startsWith('/api/v1/labs/my/services/') && method === 'PUT') {
        const s = my.services.find((x) => x.id === seg[5]); if (!s) return problem('الخدمة غير موجودة.', 404);
        Object.assign(s, Object.fromEntries(Object.entries(body || {}).filter(([k, v]) => v !== undefined && v !== null && !(k === 'priceEgp' && !(v > 0))))); out = s;
      } else if (p.startsWith('/api/v1/labs/my/services/') && method === 'DELETE') my.services = my.services.filter((x) => x.id !== seg[5]);
      else return problem('غير متاح في نسخة العرض.', 404);
      sync(); save();
      return json(out);
    }
    if (/^\/api\/v1\/labs\/[^/]+\/reviews$/.test(p) && r === 'doctor') {
      const lab = state.public[`/api/v1/labs/${seg[3]}`];
      const rev = { id: uuid(), dentistName: G['/api/v1/auth/me'].fullName, rating: Number(body?.rating) || 5, comment: body?.comment || '', createdAt: now() };
      lab?.reviews?.unshift(rev); save();
      return json(rev);
    }

    // ---- lab finance / subscription / campaigns ----
    if (p === '/api/v1/finance/lab/withdraw' && r === 'lab') {
      const lf = G['/api/v1/finance/lab'];
      if (!(lf.availableToWithdrawEgp > 0)) return problem('لا يوجد رصيد متاح للسحب حاليًا.');
      lf.pendingWithdrawalEgp = r2(lf.pendingWithdrawalEgp + lf.availableToWithdrawEgp); const amt = lf.availableToWithdrawEgp; lf.availableToWithdrawEgp = 0; save();
      return json({ ok: true, amountEgp: amt, status: 'Pending' });
    }
    const sub = G['/api/v1/subscriptions/my'];
    if (p === '/api/v1/subscriptions/renew' && r === 'lab') {
      const last = sub.subscriptions[0]; const start = new Date(Math.max(Date.now(), new Date(last?.endsAt || Date.now()).getTime()));
      const end = new Date(start); end.setMonth(end.getMonth() + 1);
      sub.subscriptions.unshift({ id: uuid(), status: 'Active', isTrial: false, monthlyPriceEgp: sub.monthlyFeeEgp, startsAt: start.toISOString(), endsAt: end.toISOString(), paidAt: now() }); save();
      return json({ ok: true, endsAt: end.toISOString() });
    }
    if (p === '/api/v1/campaigns' && method === 'POST' && r === 'lab') {
      const days = Number(body?.durationDays) || 7; const end = new Date(Date.now() + days * 86400000);
      const c = { id: uuid(), title: body?.title || 'حملة جديدة', placement: 'SearchTop', budgetEgp: Number(body?.budgetEgp) || 0, startsAt: now(), endsAt: end.toISOString(), isActive: true, impressions: 0, clicks: 0, ctrPercent: 0, doctorsReached: 0, ordersAttributed: 0 };
      sub.campaigns.unshift(c); save();
      return json(c);
    }
    if (/^\/api\/v1\/campaigns\/[^/]+$/.test(p) && r === 'lab') {
      const c = sub.campaigns.find((x) => x.id === seg[3]); if (!c) return problem('الحملة غير موجودة.', 404);
      if (body?.title) c.title = body.title; if (body?.placement) c.placement = body.placement;
      if (body?.additionalBudgetEgp) c.budgetEgp = r2(c.budgetEgp + Number(body.additionalBudgetEgp));
      if (body?.extendDays) c.endsAt = new Date(new Date(c.endsAt).getTime() + body.extendDays * 86400000).toISOString();
      if (typeof body?.isActive === 'boolean') c.isActive = body.isActive;
      save(); return json(c);
    }

    // ---- admin ----
    if (r === 'admin' && p.startsWith('/api/v1/admin/')) {
      const ad = G['/api/v1/admin/overview'];
      if (p === '/api/v1/admin/settings') {
        if (body?.commissionRateBps >= 0) { ad.settings.commissionRateBps = body.commissionRateBps; ad.settings.commissionPercent = body.commissionRateBps / 100; }
        if (body?.monthlySubscriptionEgp > 0) ad.settings.monthlySubscriptionEgp = body.monthlySubscriptionEgp;
        if (body?.freeTrialDays >= 0) ad.settings.freeTrialDays = body.freeTrialDays;
        if (body?.gatewayFeeBps >= 0) ad.settings.gatewayFeeBps = body.gatewayFeeBps;
        save(); return json(ad.settings);
      }
      let m = p.match(/^\/api\/v1\/admin\/labs\/([^/]+)\/verify$/);
      if (m) {
        const l = ad.laboratories.find((x) => x.id === m[1]); if (!l) return problem('المعمل غير موجود.', 404);
        const st = typeof body?.status === 'number' ? ['Pending', 'Verified', 'Rejected', 'Suspended'][body.status] : body?.status || 'Verified';
        l.verificationStatus = st; ad.metrics.verifiedLabs = ad.laboratories.filter((x) => x.verificationStatus === 'Verified').length;
        const pl = state.public['/api/v1/labs'].items.find((x) => x.id === l.id); if (pl) pl.isVerified = st === 'Verified';
        ad.auditLogs.unshift({ id: uuid(), actorUserId: G['/api/v1/auth/me'].id, actorRole: 'Admin', action: 'LabVerificationChanged', entityType: 'Laboratory', entityId: l.id, details: st, createdAt: now() });
        save(); return json({ ok: true, status: st });
      }
      if (p === '/api/v1/admin/settlements' && method === 'POST') {
        const lf = state.users.lab.get['/api/v1/finance/lab'];
        const isMainLab = state.users.lab.get['/api/v1/labs/my']?.id === body?.laboratoryId;
        const net = isMainLab ? lf.unsettledPayableEgp : 0;
        if (!(net > 0)) return problem('لا توجد مستحقات غير مسوّاة لهذا المعمل.');
        const gross = lf.grossPaidEgp; const s = { id: uuid(), laboratoryId: body.laboratoryId, grossEgp: gross, commissionEgp: r2(gross - net), netPayableEgp: net, status: 'Submitted', externalBankTransferRef: null, createdAt: now(), paidAt: null };
        ad.settlements.unshift(s); lf.settlements.unshift(clone(s)); lf.unsettledPayableEgp = 0; save();
        return json({ id: s.id, status: s.status, netPayableEgp: net, externalBankTransferRef: null, createdAt: s.createdAt });
      }
      m = p.match(/^\/api\/v1\/admin\/settlements\/([^/]+)\/mark-paid$/);
      if (m) {
        const t = now();
        for (const list of [ad.settlements, state.users.lab.get['/api/v1/finance/lab'].settlements]) { const s = list.find((x) => x.id === m[1]); if (s) Object.assign(s, { status: 'Paid', paidAt: t, externalBankTransferRef: body?.externalBankTransferRef || 'TRX-DEMO' }); }
        const lf = state.users.lab.get['/api/v1/finance/lab'];
        const s = ad.settlements.find((x) => x.id === m[1]); if (s) lf.settledPaidToLabEgp = r2(lf.settledPaidToLabEgp + s.netPayableEgp);
        save(); return json({ ok: true });
      }
      m = p.match(/^\/api\/v1\/admin\/users\/([^/]+)\/suspend$/);
      if (m) { const usr = ad.users.find((x) => x.id === m[1]); if (usr) Object.assign(usr, { isSuspended: !!body?.suspended, suspensionReason: body?.reason || null }); save(); return json({ ok: true }); }
    }
    if (p === '/api/v1/finance/refunds') return problem('الاسترداد غير متاح في نسخة العرض.');

    return problem('غير متاح في نسخة العرض.', 404);
  }

  window.fetch = async (input, init = {}) => {
    const url = typeof input === 'string' ? input : input?.url || String(input);
    if (!/\/api\//.test(url)) return realFetch(input, init);
    const method = String(init.method || (typeof input === 'object' && input.method) || 'GET').toUpperCase();
    let body = null; let form = null;
    if (init.body instanceof FormData) form = init.body;
    else if (typeof init.body === 'string' && init.body) { try { body = JSON.parse(init.body); } catch { body = null; } }
    await new Promise((res) => setTimeout(res, 120)); // feel like a network call
    try { return await handle(method, url, body, form); } catch (err) { console.warn('[Taaj demo API]', err); return problem('حدث خطأ في نسخة العرض. جرّب «إعادة ضبط بيانات العرض».', 500); }
  };

  // Demo account buttons on the login page fill the form (accounts are listed in login.html).
  document.addEventListener('click', (e) => {
    const b = e.target.closest?.('[data-demo-login]');
    if (!b) return;
    const [email, password] = b.dataset.demoLogin.split('|');
    const form = b.closest('main, body').querySelector('form');
    const em = form?.querySelector('input[type="email"], input[name="email"]');
    const pw = form?.querySelector('input[type="password"], input[name="password"]');
    if (em) em.value = email; if (pw) pw.value = password;
    pw?.focus();
  });

  // "Reset demo data" button inside the demo notice (written in each page's HTML).
  document.addEventListener('click', (e) => {
    if (!e.target.closest?.('[data-demo-reset]')) return;
    localStorage.removeItem(KEY); localStorage.removeItem(SESSION);
    location.href = `${BASE}/`;
  });
})();
