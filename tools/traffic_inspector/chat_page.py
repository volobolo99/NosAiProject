"""The conversation view: one thread per MCP worker, pushed live.

The board at "/" answers "is this worker up and how many calls did it take".
This page answers the other half - what was actually said, and what each
exchange cost. It reads the same SSE stream, so a delegation appears here the
moment the orchestrator finishes writing it to data/traffic/chats.jsonl.

The base stylesheet was drafted by the local Qwen worker (ask_local_qwen,
2026-09-08) and corrected here: the draft emitted the Italian words `card;` and
`pillola;` as declarations, `wrap: wrap` instead of `flex-wrap`, and left the
chip without its pill radius.
"""

CHAT_PAGE = r"""<!DOCTYPE html>
<html lang="it">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>NosAi - Chat dei worker</title>
<style>
  :root { color-scheme: dark; }
  body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
         background: #0b0f19; color: #f8fafc; margin: 0; padding: 24px; }
  h1 { font-size: 20px; margin: 0 0 4px; }
  a { color: #38bdf8; text-decoration: none; }
  a:hover { text-decoration: underline; }
  .sub { color: #94a3b8; font-size: 13px; margin-bottom: 16px; display: flex;
         gap: 10px; align-items: center; flex-wrap: wrap; }
  .live { display: inline-flex; align-items: center; gap: 6px; }
  .dot { width: 8px; height: 8px; border-radius: 50%; background: #22c55e; }
  .dot.off { background: #ef4444; }
  .dot.pulse { animation: pulse 1.6s ease-in-out infinite; }
  @keyframes pulse { 0%,100% { opacity: 1 } 50% { opacity: .25 } }

  .tot { display: flex; gap: 24px; flex-wrap: wrap; background: #131d31;
         border: 1px solid #1e293b; border-radius: 10px; padding: 14px 16px;
         margin-bottom: 16px; font-size: 11px; color: #64748b;
         text-transform: uppercase; letter-spacing: .04em; }
  .tot b { display: block; font-size: 18px; color: #f8fafc; text-transform: none;
           letter-spacing: 0; font-variant-numeric: tabular-nums; margin-bottom: 2px; }
  .tot .paid b { color: #fbbf24; }

  .layout { display: grid; grid-template-columns: 260px 1fr; gap: 16px;
            height: calc(100vh - 230px); min-height: 320px; }
  .side { display: flex; flex-direction: column; gap: 8px; overflow-y: auto;
          padding-right: 4px; }
  .tab { background: #131d31; border: 1px solid #1e293b; border-radius: 10px;
         padding: 12px; cursor: pointer; transition: border-color .2s, background .2s; }
  .tab:hover { border-color: #334d6e; }
  .tab.on { border-color: #38bdf8; background: #16233c; }
  .tname { font-size: 13px; font-weight: 600; display: flex; align-items: center;
           gap: 6px; }
  .tmeta { font-size: 11px; color: #64748b; margin-top: 4px; }
  .pip { width: 7px; height: 7px; border-radius: 50%; background: #64748b; flex: none; }
  .pip.ATTIVO { background: #22c55e } .pip.PRONTO { background: #38bdf8 }
  .pip.ERRORE, .pip.OFFLINE, .pip.IRRAGGIUNGIBILE, .pip.ASSENTE { background: #f97316 }

  .main { display: flex; flex-direction: column; min-height: 0; gap: 10px; }
  #q { background: #131d31; border: 1px solid #1e293b; border-radius: 8px;
       padding: 9px 12px; color: #f8fafc; font-size: 12px; font-family: inherit; }
  #q::placeholder { color: #64748b; }
  #q:focus { outline: none; border-color: #38bdf8; }
  .thread { display: flex; flex-direction: column; gap: 14px; overflow-y: auto;
            padding-right: 6px; min-height: 0; }

  .turn { background: #131d31; border: 1px solid #1e293b; border-radius: 10px;
          padding: 14px; }
  .turn.err { border-color: #f87171; }
  .turn.run { border-color: #fbbf24; }
  .turn header { display: flex; justify-content: space-between; flex-wrap: wrap;
                 gap: 8px; font-size: 11px; color: #64748b; margin-bottom: 10px;
                 font-variant-numeric: tabular-nums; }
  .turn header b { color: #cbd5e1; font-weight: 600; }
  .ok { color: #22c55e } .ko { color: #f87171 } .runc { color: #fbbf24 }

  .role { font-size: 10px; text-transform: uppercase; letter-spacing: .06em;
          color: #64748b; margin: 8px 0 4px; }
  .bubble { border-radius: 8px; padding: 10px 12px; font-family: ui-monospace,
            'Cascadia Mono', Consolas, monospace; font-size: 12px; line-height: 1.5;
            white-space: pre-wrap; word-break: break-word; max-height: 260px;
            overflow: auto; }
  .bubble.out { background: #16233c; border-left: 3px solid #38bdf8; }
  .bubble.in { background: #101a2c; border-left: 3px solid #22c55e; }
  .bubble.err { background: #101a2c; border-left: 3px solid #f87171; color: #fca5a5; }
  .bubble.tall { max-height: none; }
  .more { background: none; border: none; color: #38bdf8; font-size: 11px;
          padding: 4px 0 0; cursor: pointer; font-family: inherit; }
  .more:hover { text-decoration: underline; }
  mark { background: #38bdf8; color: #0b0f19; border-radius: 2px; }

  .chips { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 10px; }
  .chip { background: #0b0f19; border: 1px solid #1e293b; border-radius: 999px;
          padding: 3px 9px; font-size: 11px; color: #cbd5e1;
          font-variant-numeric: tabular-nums; }
  .chip b { color: #f8fafc; }
  .chip.paid { border-color: #7c5c12; }
  .void { color: #64748b; font-size: 13px; padding: 20px; line-height: 1.6; }

  .side::-webkit-scrollbar, .thread::-webkit-scrollbar,
  .bubble::-webkit-scrollbar { width: 8px; height: 8px; }
  .side::-webkit-scrollbar-thumb, .thread::-webkit-scrollbar-thumb,
  .bubble::-webkit-scrollbar-thumb { background: #1e293b; border-radius: 4px; }
  .side::-webkit-scrollbar-track, .thread::-webkit-scrollbar-track,
  .bubble::-webkit-scrollbar-track { background: transparent; }

  @media (max-width: 760px) {
    body { padding: 16px; }
    .layout { grid-template-columns: 1fr; height: auto; }
    .side { flex-direction: row; overflow-x: auto; }
    .tab { min-width: 200px; flex: none; }
    .thread { max-height: 70vh; }
  }
</style>
</head>
<body>
<h1>Chat dei worker</h1>
<div class="sub">
  <span class="live"><span class="dot pulse" id="conn"></span><span id="connLabel">in diretta</span></span>
  <span>|</span><span id="clock">--</span>
  <span>|</span><a href="/">flusso e stato dei worker</a>
  <span>|</span><span>registro: data/traffic/chats.jsonl</span>
</div>

<div class="tot" id="tot"></div>

<div class="layout">
  <div class="side" id="side"></div>
  <div class="main">
    <input id="q" type="search" placeholder="cerca nel testo degli scambi (prompt e risposta)">
    <div class="thread" id="thread"></div>
  </div>
</div>

<script>
const PRICE_PER_MTOK = 3.00;   // stessa stima del badge in logact.md
const FREE = new Set(['local', 'colab']);

const sideEl = document.getElementById('side');
const threadEl = document.getElementById('thread');
const totEl = document.getElementById('tot');
const qEl = document.getElementById('q');

let agents = new Map();   // id -> stato del worker
let chats = [];           // scambi conclusi, dal piu' vecchio al piu' recente
let running = new Map();  // id chiamata -> evento di inizio ancora aperto
let selected = localStorage.getItem('nosai.chat.agent') || null;
let query = '';
const expanded = new Set();

function esc(v) {
  return String(v == null ? '' : v).replace(/[&<>"']/g, c => (
    {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
}
function hl(text) {
  const safe = esc(text);
  if (!query) return safe;
  const rx = new RegExp('(' + query.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + ')', 'gi');
  return safe.replace(rx, '<mark>$1</mark>');
}
function fmtTime(v) {
  if (!v) return '--';
  return new Date(v * 1000).toLocaleTimeString('it-IT', {hour12: false});
}
function fmtDate(v) {
  if (!v) return '';
  return new Date(v * 1000).toLocaleDateString('it-IT',
    {day: '2-digit', month: '2-digit'});
}
function fmtDur(ms) {
  if (ms == null) return '--';
  if (ms < 1000) return ms + ' ms';
  if (ms < 60000) return (ms / 1000).toFixed(1) + ' s';
  return Math.floor(ms / 60000) + 'm ' + Math.round((ms % 60000) / 1000) + 's';
}
function num(v) { return (v || 0).toLocaleString('it-IT'); }

function matches(chat) {
  if (!query) return true;
  const q = query.toLowerCase();
  return (chat.prompt || '').toLowerCase().includes(q)
      || (chat.response || '').toLowerCase().includes(q)
      || (chat.error || '').toLowerCase().includes(q);
}

function renderTotals() {
  let freeTok = 0, paidTok = 0, calls = 0, failed = 0, duration = 0;
  for (const c of chats) {
    calls += 1;
    duration += c.durationMs || 0;
    if (!c.ok) failed += 1;
    if (FREE.has(c.agent)) freeTok += c.tokens || 0; else paidTok += c.tokens || 0;
  }
  const saved = (freeTok / 1e6) * PRICE_PER_MTOK;
  const avg = calls ? duration / calls : 0;
  totEl.innerHTML =
    '<div><b>' + num(freeTok) + '</b>token a costo zero</div>' +
    '<div><b>$' + saved.toFixed(3) + '</b>risparmio stimato ($3/M)</div>' +
    '<div class="paid"><b>' + num(paidTok) + '</b>token DeepSeek (a pagamento)</div>' +
    '<div><b>' + num(calls) + '</b>scambi registrati</div>' +
    '<div><b>' + num(failed) + '</b>falliti</div>' +
    '<div><b>' + fmtDur(Math.round(avg)) + '</b>durata media</div>';
}

function renderSide() {
  const order = ['local', 'colab', 'deepseek'];
  const rows = order.filter(id => agents.has(id)).map(id => agents.get(id));
  if (selected === null && rows.length) selected = rows[0].id;
  sideEl.innerHTML = rows.map(a => {
    const mine = chats.filter(c => c.agent === a.id);
    const tok = mine.reduce((s, c) => s + (c.tokens || 0), 0);
    const last = mine.length ? mine[mine.length - 1].at : a.lastSeen;
    const live = [...running.values()].some(e => e.agent === a.id);
    return '<div class="tab' + (a.id === selected ? ' on' : '') + '" data-id="' + a.id + '">' +
      '<div class="tname"><span class="pip ' + esc(live ? 'ATTIVO' : a.status) + '"></span>' +
      esc(a.name) + '</div>' +
      '<div class="tmeta">' + esc((a.models && a.models[0]) || a.host || '') + '</div>' +
      '<div class="tmeta">' + mine.length + ' scambi &middot; ' + num(tok) + ' token' +
      (FREE.has(a.id) ? ' &middot; gratis' : ' &middot; a pagamento') + '</div>' +
      '<div class="tmeta">ultimo: ' + (last ? fmtTime(last) : '--') + '</div>' +
      '</div>';
  }).join('');
  sideEl.querySelectorAll('.tab').forEach(el => {
    el.onclick = () => {
      selected = el.dataset.id;
      localStorage.setItem('nosai.chat.agent', selected);
      renderSide();
      renderThread(true);
    };
  });
}

function bubble(kind, text, id, which) {
  const key = id + ':' + which;
  const long = (text || '').length > 1200;
  const open = expanded.has(key);
  const cls = 'bubble ' + kind + (open ? ' tall' : '');
  return '<div class="' + cls + '">' + hl(text || '(vuoto)') + '</div>' +
    (long ? '<button class="more" data-key="' + esc(key) + '">' +
      (open ? 'riduci' : 'apri per intero (' + num((text || '').length) + ' caratteri)') +
      '</button>' : '');
}

function chips(c) {
  const out = [];
  out.push('<span class="chip' + (FREE.has(c.agent) ? '' : ' paid') + '">token <b>' +
           num(c.tokens) + '</b></span>');
  if (c.promptTokens != null)
    out.push('<span class="chip">prompt <b>' + num(c.promptTokens) + '</b></span>');
  if (c.completionTokens != null)
    out.push('<span class="chip">risposta <b>' + num(c.completionTokens) + '</b></span>');
  if (c.reasoningTokens)
    out.push('<span class="chip">ragionamento <b>' + num(c.reasoningTokens) + '</b></span>');
  if (c.cacheHitTokens)
    out.push('<span class="chip">cache <b>' + num(c.cacheHitTokens) + '</b></span>');
  if (c.servedModel)
    out.push('<span class="chip">servito da <b>' + esc(c.servedModel) + '</b></span>');
  if (c.channel)
    out.push('<span class="chip">' + esc(c.channel) + '</span>');
  out.push('<span class="chip">id <b>' + esc(c.id) + '</b></span>');
  return '<div class="chips">' + out.join('') + '</div>';
}

function renderThread(toBottom) {
  const atBottom = threadEl.scrollHeight - threadEl.scrollTop - threadEl.clientHeight < 60;
  const mine = chats.filter(c => c.agent === selected && matches(c));
  const live = [...running.values()].filter(e => e.agent === selected);
  const parts = [];

  if (!mine.length && !live.length) {
    parts.push('<div class="void">' + (query
      ? 'Nessuno scambio contiene &laquo;' + esc(query) + '&raquo;.'
      : 'Nessuno scambio registrato per questo worker.<br>' +
        'Il testo delle conversazioni si registra dalle deleghe fatte dopo il ' +
        '2026-09-08: le chiamate precedenti restano nel flusso, senza contenuto.') +
      '</div>');
  }

  for (const c of mine) {
    const state = c.ok ? '<span class="ok">riuscito</span>'
                       : '<span class="ko">fallito</span>';
    parts.push(
      '<div class="turn' + (c.ok ? '' : ' err') + '">' +
      '<header><span><b>' + esc(c.op) + '</b> &middot; ' + esc(c.model) + '</span>' +
      '<span>' + fmtDate(c.at) + ' ' + fmtTime(c.at) + ' &middot; ' +
      fmtDur(c.durationMs) + ' &middot; ' + state + '</span></header>' +
      '<div class="role">inviato al worker</div>' +
      bubble('out', c.prompt, c.id, 'p') +
      '<div class="role">' + (c.ok ? 'risposta' : 'errore') + '</div>' +
      bubble(c.ok ? 'in' : 'err', c.ok ? c.response : (c.error || c.response), c.id, 'r') +
      chips(c) + '</div>');
  }

  for (const e of live) {
    parts.push(
      '<div class="turn run"><header><span><b>' + esc(e.op) + '</b> &middot; ' +
      esc(e.model) + '</span><span>' + fmtTime(e.at) +
      ' &middot; <span class="runc">in corso</span></span></header>' +
      '<div class="void" style="padding:8px 0">Chiamata partita da ' +
      fmtDur(Math.round((Date.now() / 1000 - e.at) * 1000)) +
      ': il testo compare quando il worker risponde.</div></div>');
  }

  threadEl.innerHTML = parts.join('');
  threadEl.querySelectorAll('.more').forEach(btn => {
    btn.onclick = () => {
      const key = btn.dataset.key;
      if (expanded.has(key)) expanded.delete(key); else expanded.add(key);
      renderThread(false);
    };
  });
  if (toBottom || atBottom) threadEl.scrollTop = threadEl.scrollHeight;
}

function renderAll(toBottom) {
  renderTotals();
  renderSide();
  renderThread(toBottom);
}

function applyEvent(ev) {
  if (ev.phase === 'start') running.set(ev.id, ev);
  else running.delete(ev.id);
}

qEl.oninput = () => { query = qEl.value.trim(); renderThread(false); };

let source = null;
function connect() {
  source = new EventSource('/events');
  source.onopen = () => {
    document.getElementById('conn').classList.remove('off');
    document.getElementById('connLabel').textContent = 'in diretta';
  };
  source.onmessage = (msg) => {
    const data = JSON.parse(msg.data);
    if (data.kind === 'snapshot') {
      agents = new Map(data.agents.map(a => [a.id, a]));
      chats = data.chats || [];
      running = new Map();
      (data.events || []).forEach(applyEvent);
      renderAll(true);
    } else if (data.kind === 'agent') {
      agents.set(data.agent.id, data.agent);
      renderSide();
    } else if (data.kind === 'chat') {
      chats.push(data.chat);
      renderAll(false);
    } else if (data.kind === 'event') {
      applyEvent(data.event);
      renderSide();
      renderThread(false);
    }
  };
  source.onerror = () => {
    document.getElementById('conn').classList.add('off');
    document.getElementById('connLabel').textContent = 'connessione persa, riprovo';
    source.close();
    setTimeout(connect, 3000);
  };
}
connect();

setInterval(() => {
  document.getElementById('clock').textContent =
    new Date().toLocaleTimeString('it-IT', {hour12: false});
  if (running.size) renderThread(false);   // il cronometro delle chiamate aperte
}, 1000);
</script>
</body>
</html>
"""
