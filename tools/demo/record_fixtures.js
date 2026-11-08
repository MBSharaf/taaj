// Records the real API's GET responses (local dev server with demo seed data) as fixtures for the GitHub Pages
// in-browser demo API. Run with the server on http://localhost:5080:  node tools/demo/record_fixtures.js
// Output: src/LabStage.Api/wwwroot/assets/js/demo/fixtures.json  (demo seed data only; no secrets, no real users)
const fs = require('fs');
const path = require('path');
const BASE = process.env.BASE || 'http://localhost:5080';
const ROLES = {
  doctor: ['doctor@labstage.eg', 'Doctor@12345'],
  lab: ['lab@labstage.eg', 'Lab@12345'],
  admin: ['admin@labstage.eg', 'Admin@12345'],
};

async function session(email, password) {
  const res = await fetch(`${BASE}/api/v1/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password }) });
  const cookie = (res.headers.getSetCookie?.() || []).map((c) => c.split(';')[0]).join('; ');
  return { login: await res.json(), cookie };
}
async function get(url, cookie) {
  const res = await fetch(BASE + url, { headers: cookie ? { Cookie: cookie } : {} });
  if (!res.ok) return { __status: res.status };
  return res.json();
}

(async () => {
  const out = { recordedAt: new Date().toISOString(), public: {}, users: {} };
  out.public['/api/v1/labs'] = await get('/api/v1/labs');
  for (const l of out.public['/api/v1/labs'].items || []) out.public[`/api/v1/labs/${l.slug}`] = await get(`/api/v1/labs/${l.slug}`);
  for (const [role, [email, pw]] of Object.entries(ROLES)) {
    const { login, cookie } = await session(email, pw);
    const r = { email, password: pw, login, get: {} };
    const urls = ['/api/v1/auth/me', '/api/v1/notifications', '/api/v1/orders'];
    if (role === 'doctor') urls.push('/api/v1/finance/doctor');
    if (role === 'lab') urls.push('/api/v1/finance/lab', '/api/v1/labs/my', '/api/v1/subscriptions/my');
    if (role === 'admin') urls.push('/api/v1/admin/overview');
    for (const u of urls) r.get[u] = await get(u, cookie);
    for (const o of r.get['/api/v1/orders']?.items || []) r.get[`/api/v1/orders/${o.orderNumber}`] = await get(`/api/v1/orders/${o.orderNumber}`, cookie);
    if (role === 'lab') {
      const fin = r.get['/api/v1/finance/lab'];
      const ids = new Set((fin.dentists || fin.dentistAccounts || []).map((d) => d.dentistId || d.id).filter(Boolean));
      for (const id of ids) r.get[`/api/v1/finance/lab?dentistId=${id}`] = await get(`/api/v1/finance/lab?dentistId=${id}`, cookie);
    }
    out.users[role] = r;
  }
  const file = path.join(__dirname, '../../src/LabStage.Api/wwwroot/assets/js/demo/fixtures.json');
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, JSON.stringify(out));
  console.log('wrote', file, (fs.statSync(file).size / 1024).toFixed(0) + ' KB');
  for (const [k, v] of Object.entries(out.users)) console.log(k, Object.keys(v.get).length, 'GETs');
})();
