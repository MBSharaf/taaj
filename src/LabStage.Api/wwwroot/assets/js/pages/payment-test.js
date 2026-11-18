// Local test checkout (development / demo only): simulates the gateway's payment page.
// The order is marked paid only when the gateway's signed confirmation is accepted by the server.
import { applyDomTranslations, getLang } from '../core/i18n.js?v=16';

const $ = (s) => document.querySelector(s);
const checkout = new URLSearchParams(location.search).get('checkout') || '';
const show = (name) => document.querySelectorAll('[data-view]').forEach((v) => { v.hidden = v.dataset.view !== name; });
const tr = (el) => { if (el && getLang() === 'en') applyDomTranslations(el); };

(async () => {
  const r = checkout ? await fetch(`/api/v1/payments/test-gateway/${encodeURIComponent(checkout)}`).catch(() => null) : null;
  if (!r?.ok) { show('unavailable'); return; }
  const d = await r.json();
  const back = `/doctor-order-details.html?orderNumber=${encodeURIComponent(d.orderNumber)}&payment=return`;
  $('#pay-back').href = back;
  $('#pay-retry').href = `/doctor-order-details.html?orderNumber=${encodeURIComponent(d.orderNumber)}`;
  if (d.status === 'Succeeded') { show('success'); return; }
  $('#pay-order').textContent = d.orderNumber;
  $('#pay-amount').textContent = new Intl.NumberFormat('en-US', { minimumFractionDigits: 2 }).format(d.amountEgp);
  $(`#pay-milestone-${d.milestoneSequence}`)?.removeAttribute('hidden');
  show('form');

  document.querySelectorAll('[data-card]').forEach((b) => b.addEventListener('click', () => { $('#card').value = b.dataset.card; $('#card').focus(); }));
  $('#pay-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const card = $('#card').value.replace(/\D/g, '');
    const alert = $('#acc-alert');
    if (card.length < 12) { alert.textContent = 'اكتب رقم البطاقة.'; alert.hidden = false; tr(alert); return; }
    const btn = e.target.querySelector('[type=submit]');
    btn.disabled = true;
    const res = await fetch(`/api/v1/payments/test-gateway/${encodeURIComponent(checkout)}/pay`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ cardNumber: card }),
    }).catch(() => null);
    btn.disabled = false;
    const out = res ? await res.json().catch(() => ({})) : {};
    if (res?.ok && out.status === 'Succeeded') { show('success'); setTimeout(() => { location.href = back; }, 2500); return; }
    if (res?.ok) { show('failed'); return; }
    alert.textContent = out.detail || 'تعذّر إتمام العملية.';
    alert.hidden = false;
    tr(alert);
  });
})();
