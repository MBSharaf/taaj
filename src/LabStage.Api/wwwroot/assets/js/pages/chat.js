// Order chat (doctor <-> lab): live messages over SignalR, read receipts, unread count, file attachments.
// Behaviour only: texts come from the HTML (#chat-texts); messages are user content set with textContent.
import { applyDomTranslations, getLang } from '../core/i18n.js?v=17';
import { live } from '../core/live.js?v=1';

const tr = (el) => { if (el && getLang() === 'en') applyDomTranslations(el); };

export async function initOrderChat(orderNumber, me) {
  const box = document.getElementById('order-chat-messages');
  const form = document.getElementById('order-chat-form');
  if (!box || !form) return;
  const input = document.getElementById('order-chat-input');
  const empty = document.getElementById('chat-empty');
  const unreadEl = document.getElementById('chat-unread');
  const unreadN = document.getElementById('chat-unread-n');
  const fileInput = document.getElementById('chat-file');
  const fileName = document.getElementById('chat-file-name');
  const text = (k) => document.querySelector(`#chat-texts [data-t="${k}"]`)?.textContent || '';
  const seen = new Set();
  let otherReadAt = null;
  const base = `/api/v1/orders/${encodeURIComponent(orderNumber)}/messages`;

  const time = (d) => new Date(d).toLocaleTimeString(getLang() === 'en' ? 'en-GB' : 'ar-EG', { hour: '2-digit', minute: '2-digit' });

  function bubble(m) {
    if (seen.has(m.id)) return;
    seen.add(m.id);
    const mine = m.mine ?? m.senderUserId === me?.id;
    const el = document.createElement('div');
    el.className = `chat-msg${mine ? ' is-mine' : ''}`;
    el.dataset.at = m.sentAt;
    const meta = document.createElement('div');
    meta.className = 'chat-msg-meta';
    meta.textContent = `${m.senderName} · ${time(m.sentAt)}`;
    el.append(meta);
    if (m.body) { const b = document.createElement('div'); b.textContent = m.body; el.append(b); }
    if (m.attachment) {
      const a = document.createElement('a');
      a.className = 'chat-file';
      a.href = m.attachment.downloadUrl;
      a.setAttribute('download', '');
      a.textContent = `📎 ${m.attachment.name}`;
      el.append(a);
    }
    if (mine) { const s = document.createElement('span'); s.className = 'chat-tick'; el.append(s); }
    box.append(el);
    empty.hidden = true;
  }

  function ticks() {
    box.querySelectorAll('.chat-msg.is-mine').forEach((el) => {
      const read = otherReadAt && new Date(otherReadAt) >= new Date(el.dataset.at);
      const t = el.querySelector('.chat-tick');
      t.textContent = read ? `✓✓ ${text('seen')}` : `✓ ${text('sent')}`;
      t.classList.toggle('is-read', !!read);
    });
  }

  let unread = 0;
  const setUnread = (n) => { unread = n; unreadN.textContent = new Intl.NumberFormat(getLang() === 'en' ? 'en-US' : 'ar-EG').format(n); unreadEl.hidden = n === 0; };
  const scrollDown = () => { box.scrollTop = box.scrollHeight; };

  // Read = the chat is on screen and the tab is visible.
  let readTimer = null;
  const markRead = () => {
    clearTimeout(readTimer);
    readTimer = setTimeout(async () => {
      if (document.visibilityState !== 'visible') return;
      await fetch(`${base}/read`, { method: 'POST' }).catch(() => null);
      setUnread(0);
    }, 400);
  };
  const visible = () => { const r = box.getBoundingClientRect(); return r.bottom > 0 && r.top < innerHeight; };

  async function load() {
    const r = await fetch(base).catch(() => null);
    if (!r?.ok) return;
    const d = await r.json();
    (d.items || []).forEach(bubble);
    otherReadAt = d.otherLastReadAt;
    empty.hidden = (d.items || []).length > 0;
    ticks();
    tr(document.getElementById('chat'));
    scrollDown();
    setUnread(d.unread || 0);
    if (d.unread && visible()) markRead();
  }

  // Live updates
  const conn = live();
  const join = async () => {
    try {
      await conn.invoke('JoinOrderGroup', orderNumber);
      document.getElementById('chat-live').hidden = false;
    } catch { document.getElementById('chat-live').hidden = true; }
  };
  if (conn) {
    conn.on('chatMessage', (m) => {
      if (m.orderNumber !== orderNumber) return;
      bubble(m);
      ticks();
      tr(box.lastElementChild);
      scrollDown();
      if (m.senderUserId !== me?.id) {
        if (visible() && document.visibilityState === 'visible') markRead();
        else setUnread(unread + 1);
      }
    });
    conn.on('chatRead', (e) => {
      if (e.orderNumber !== orderNumber || e.userId === me?.id) return;
      otherReadAt = e.readAt;
      ticks();
    });
    conn.on('stageUpdated', () => document.dispatchEvent(new CustomEvent('taaj:order-changed')));
    conn.onReconnected(() => { join(); load(); });
    join();
  } else {
    setInterval(load, 15000); // no live connection: check now and then
  }
  document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible' && !unreadEl.hidden && visible()) markRead(); });
  window.addEventListener('scroll', () => { if (!unreadEl.hidden && visible()) markRead(); }, { passive: true });

  // Attach a file: it is uploaded to the order's protected files, then sent as a message.
  document.getElementById('chat-attach-btn')?.addEventListener('click', () => fileInput.click());
  fileInput?.addEventListener('change', () => {
    const f = fileInput.files?.[0];
    fileName.hidden = !f;
    fileName.textContent = f ? `📎 ${f.name}` : '';
  });

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const body = input.value.trim();
    const file = fileInput?.files?.[0];
    if (!body && !file) return;
    const btn = form.querySelector('[type=submit]');
    btn.disabled = true;
    try {
      let fileId = null;
      if (file) {
        fileName.textContent = text('uploading');
        const fd = new FormData();
        fd.append('file', file);
        const up = await fetch(`/api/v1/orders/${encodeURIComponent(orderNumber)}/files`, { method: 'POST', body: fd });
        const out = await up.json().catch(() => ({}));
        if (!up.ok) { fileName.textContent = out.detail || text('failed'); fileName.hidden = false; return; }
        fileId = out.id;
      }
      const r = await fetch(base, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ body, fileId }) });
      const m = await r.json().catch(() => ({}));
      if (!r.ok) { fileName.textContent = m.detail || text('failed'); fileName.hidden = false; return; }
      input.value = '';
      if (fileInput) fileInput.value = '';
      fileName.hidden = true;
      bubble({ ...m, mine: true });
      ticks();
      tr(box.lastElementChild);
      scrollDown();
      if (file) document.dispatchEvent(new CustomEvent('taaj:order-changed'));
    } finally {
      btn.disabled = false;
      input.focus();
    }
  });

  if (location.hash === '#chat') document.getElementById('chat')?.scrollIntoView({ block: 'start' });
  await load();
}

/** Order lists: a small unread-messages badge next to each order number. */
export async function decorateUnreadChats() {
  const r = await fetch('/api/v1/messages/unread').catch(() => null);
  if (!r?.ok) return;
  const { orders } = await r.json();
  Object.entries(orders || {}).forEach(([n, count]) => {
    document.querySelectorAll('tbody tr').forEach((tr) => {
      const cell = tr.querySelector('td');
      if (!cell || !cell.textContent.includes(n) || cell.querySelector('.chat-badge')) return;
      const b = document.createElement('span');
      b.className = 'chat-badge';
      b.title = '💬';
      b.textContent = `💬 ${count}`;
      cell.append(b);
    });
  });
}
