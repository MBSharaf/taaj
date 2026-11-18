// Minimal SignalR client (JSON hub protocol over WebSocket) for live chat and notifications.
// Same-origin, uses the sign-in cookie; reconnects with back-off. On the static GitHub Pages demo it does nothing.
const RS = '\u001e';
let conn = null;

function create(path) {
  const handlers = new Map();
  const pending = new Map();
  const onReconnect = [];
  let ws = null;
  let nextId = 1;
  let retry = 0;
  let closed = false;
  let ready = null;

  const send = (obj) => { if (ws?.readyState === 1) ws.send(JSON.stringify(obj) + RS); };

  async function open() {
    const neg = await fetch(`${path}/negotiate?negotiateVersion=1`, { method: 'POST' }).catch(() => null);
    if (!neg?.ok) throw new Error('negotiate failed');
    const info = await neg.json();
    const token = info.connectionToken || info.connectionId;
    const url = `${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}${path}?id=${encodeURIComponent(token)}`;
    await new Promise((resolve, reject) => {
      ws = new WebSocket(url);
      let shook = false;
      ws.onopen = () => ws.send(JSON.stringify({ protocol: 'json', version: 1 }) + RS);
      ws.onmessage = (ev) => {
        String(ev.data).split(RS).filter(Boolean).forEach((frame) => {
          const msg = JSON.parse(frame);
          if (!shook) { shook = true; if (msg.error) reject(new Error(msg.error)); else { retry = 0; resolve(); } return; }
          if (msg.type === 1) (handlers.get(String(msg.target).toLowerCase()) || []).forEach((fn) => { try { fn(...(msg.arguments || [])); } catch (e) { console.warn(e); } });
          else if (msg.type === 3) { const p = pending.get(msg.invocationId); if (p) { pending.delete(msg.invocationId); msg.error ? p.reject(new Error(msg.error)) : p.resolve(msg.result); } }
          else if (msg.type === 6) send({ type: 6 });
        });
      };
      ws.onerror = () => { if (!shook) reject(new Error('socket error')); };
      ws.onclose = () => { pending.forEach((p) => p.reject(new Error('closed'))); pending.clear(); if (shook && !closed) schedule(); };
    });
  }

  function schedule() {
    const delay = Math.min(30000, 1000 * 2 ** retry++);
    setTimeout(async () => {
      try { await open(); onReconnect.forEach((fn) => fn()); } catch { schedule(); }
    }, delay);
  }

  ready = open().catch(() => { schedule(); });
  return {
    on(name, fn) { const k = name.toLowerCase(); if (!handlers.has(k)) handlers.set(k, []); handlers.get(k).push(fn); },
    onReconnected(fn) { onReconnect.push(fn); },
    async invoke(method, ...args) {
      await ready;
      if (ws?.readyState !== 1) throw new Error('not connected');
      const id = String(nextId++);
      return new Promise((resolve, reject) => { pending.set(id, { resolve, reject }); send({ type: 1, invocationId: id, target: method, arguments: args }); });
    },
    get connected() { return ws?.readyState === 1; },
    stop() { closed = true; ws?.close(); },
  };
}

/** The shared connection to /hubs/orders (null on the static demo, where there is no server). */
export function live() {
  if (location.pathname.startsWith('/taaj/') || !('WebSocket' in window)) return null;
  conn ||= create('/hubs/orders');
  return conn;
}
