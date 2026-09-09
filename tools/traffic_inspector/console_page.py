"""The console: every agent, what it is doing, and a box to tell it what to do.

One page, three zones - the totals, the roster on the left, the conversation on
the right. The roster mixes two populations that the operator thinks of as one
thing: the workers reachable over HTTP, and the Claude Code sessions running on
this machine. Both are listed with what they are doing right now; both accept a
message.

A Claude session is commanded by resuming it headless, which is why the page
says so on the button: the message does not land in the window open on the
desktop, it starts a run on that session's history.
"""

CONSOLE_PAGE = r"""<!DOCTYPE html>
<html lang="it">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>NosAi - Console degli agenti</title>
<style>
  :root { color-scheme: dark; }
  * { box-sizing: border-box; }
  body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
         background: #0b0f19; color: #f8fafc; margin: 0; padding: 18px 20px; }
  h1 { font-size: 19px; margin: 0 0 4px; }
  a { color: #38bdf8; text-decoration: none; }
  a:hover { text-decoration: underline; }
  .sub { color: #94a3b8; font-size: 12px; margin-bottom: 14px; display: flex;
         gap: 10px; align-items: center; flex-wrap: wrap; }
  .live { display: inline-flex; align-items: center; gap: 6px; }
  .dot { width: 8px; height: 8px; border-radius: 50%; background: #22c55e; }
  .dot.off { background: #ef4444; }
  .dot.pulse { animation: pulse 1.6s ease-in-out infinite; }
  @keyframes pulse { 0%,100% { opacity: 1 } 50% { opacity: .25 } }

  .tot { display: flex; gap: 22px; flex-wrap: wrap; background: #131d31;
         border: 1px solid #1e293b; border-radius: 10px; padding: 12px 16px;
         margin-bottom: 14px; font-size: 10px; color: #64748b;
         text-transform: uppercase; letter-spacing: .04em; }
  .tot b { display: block; font-size: 17px; color: #f8fafc; text-transform: none;
           letter-spacing: 0; font-variant-numeric: tabular-nums; margin-bottom: 2px; }
  .tot .paid b { color: #fbbf24; }
  .tot .free b { color: #22c55e; }

  .layout { display: grid; grid-template-columns: 290px 1fr; gap: 14px;
            height: calc(100vh - 175px); min-height: 400px; }
  .side { display: flex; flex-direction: column; gap: 8px; overflow-y: auto;
          padding-right: 4px; }
  .side h2 { font-size: 10px; color: #64748b; text-transform: uppercase;
             letter-spacing: .06em; margin: 8px 0 2px; }
  .tab { background: #131d31; border: 1px solid #1e293b; border-radius: 10px;
         padding: 10px 12px; cursor: pointer; transition: border-color .2s, background .2s; }
  .tab:hover { border-color: #334d6e; }
  .tab.on { border-color: #38bdf8; background: #16233c; }
  .tab.dim { opacity: .55; }
  .tname { font-size: 13px; font-weight: 600; display: flex; align-items: center;
           gap: 6px; }
  .tmeta { font-size: 11px; color: #64748b; margin-top: 3px; overflow: hidden;
           text-overflow: ellipsis; white-space: nowrap; }
  .tmeta.doing { color: #93c5fd; }
  .pip { width: 7px; height: 7px; border-radius: 50%; background: #64748b; flex: none; }
  .pip.ATTIVO { background: #22c55e; animation: pulse 1.6s ease-in-out infinite; }
  .pip.PRONTO { background: #38bdf8 }
  .pip.LAVORA { background: #fbbf24; animation: pulse 1.2s ease-in-out infinite; }
  .pip.ERRORE, .pip.OFFLINE, .pip.IRRAGGIUNGIBILE, .pip.ASSENTE { background: #f97316 }

  .main { display: flex; flex-direction: column; min-height: 0; gap: 10px; }
  .head { background: #131d31; border: 1px solid #1e293b; border-radius: 10px;
          padding: 12px 14px; }
  .head .hname { font-size: 14px; font-weight: 600; }
  .head .hmeta { font-size: 11px; color: #64748b; margin-top: 4px; }
  .head .hdoing { font-size: 12px; color: #93c5fd; margin-top: 6px;
                  font-family: ui-monospace, Consolas, monospace; }
  .head .chips { margin-top: 8px; }

  .thread { display: flex; flex-direction: column; gap: 12px; overflow-y: auto;
            padding-right: 6px; min-height: 0; flex: 1; }
  .turn { background: #131d31; border: 1px solid #1e293b; border-radius: 10px;
          padding: 12px 14px; }
  .turn.err { border-color: #f87171; }
  .turn.run { border-color: #fbbf24; }
  .turn header { display: flex; justify-content: space-between; flex-wrap: wrap;
                 gap: 8px; font-size: 11px; color: #64748b; margin-bottom: 8px;
                 font-variant-numeric: tabular-nums; }
  .turn header b { color: #cbd5e1; font-weight: 600; }
  .ok { color: #22c55e } .ko { color: #f87171 } .runc { color: #fbbf24 }
  .role { font-size: 10px; text-transform: uppercase; letter-spacing: .06em;
          color: #64748b; margin: 8px 0 4px; }
  .bubble { border-radius: 8px; padding: 9px 11px; font-family: ui-monospace,
            'Cascadia Mono', Consolas, monospace; font-size: 12px; line-height: 1.5;
            white-space: pre-wrap; word-break: break-word; max-height: 300px;
            overflow: auto; }
  .bubble.out { background: #16233c; border-left: 3px solid #38bdf8; }
  .bubble.in { background: #101a2c; border-left: 3px solid #22c55e; }
  .bubble.err { background: #101a2c; border-left: 3px solid #f87171; color: #fca5a5; }
  .bubble.trace { background: #0f1626; border-left: 3px solid #a78bfa; color: #c4b5fd;
                  max-height: 160px; }
  .bubble.tall { max-height: none; }
  .more { background: none; border: none; color: #38bdf8; font-size: 11px;
          padding: 4px 0 0; cursor: pointer; font-family: inherit; }
  .caret { display: inline-block; width: 7px; height: 13px; background: #38bdf8;
           vertical-align: text-bottom; animation: pulse 1s steps(2) infinite; }

  .chips { display: flex; flex-wrap: wrap; gap: 7px; margin-top: 9px; }
  .chip { background: #0b0f19; border: 1px solid #1e293b; border-radius: 999px;
          padding: 3px 9px; font-size: 11px; color: #cbd5e1;
          font-variant-numeric: tabular-nums; }
  .chip b { color: #f8fafc; }
  .chip.paid { border-color: #7c5c12; }
  .chip.free { border-color: #14532d; }
  .void { color: #64748b; font-size: 13px; padding: 16px; line-height: 1.6; }

  .composer { background: #131d31; border: 1px solid #1e293b; border-radius: 10px;
              padding: 10px; display: flex; flex-direction: column; gap: 8px; }
  .composer.busy { border-color: #fbbf24; }
  textarea { background: #0b0f19; border: 1px solid #1e293b; border-radius: 8px;
             color: #f8fafc; font-family: ui-monospace, Consolas, monospace;
             font-size: 12px; padding: 9px 11px; resize: vertical; min-height: 66px;
             line-height: 1.5; }
  textarea:focus { outline: none; border-color: #38bdf8; }
  textarea:disabled { opacity: .5; }
  .row { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
  .grow { flex: 1; }
  select, button { font-family: inherit; font-size: 11px; border-radius: 7px;
                   border: 1px solid #1e293b; background: #0b0f19; color: #cbd5e1;
                   padding: 6px 10px; cursor: pointer; }
  select:focus, button:focus { outline: none; border-color: #38bdf8; }
  button.go { background: #1d4ed8; border-color: #2563eb; color: #fff; font-weight: 600;
              padding: 7px 16px; }
  button.go:hover { background: #2563eb; }
  button.go:disabled { background: #1e293b; border-color: #1e293b; color: #64748b;
                       cursor: not-allowed; }
  button.stop { background: #7f1d1d; border-color: #b91c1c; color: #fecaca; }
  label.opt { font-size: 11px; color: #94a3b8; display: inline-flex; gap: 5px;
              align-items: center; cursor: pointer; }
  .warn { font-size: 11px; color: #fbbf24; }
  .hint { font-size: 11px; color: #64748b; }

  .side::-webkit-scrollbar, .thread::-webkit-scrollbar, .bubble::-webkit-scrollbar {
    width: 8px; height: 8px; }
  .side::-webkit-scrollbar-thumb, .thread::-webkit-scrollbar-thumb,
  .bubble::-webkit-scrollbar-thumb { background: #1e293b; border-radius: 4px; }

  @media (max-width: 820px) {
    body { padding: 14px; }
    .layout { grid-template-columns: 1fr; height: auto; }
    .side { flex-direction: row; overflow-x: auto; }
    .side h2 { display: none; }
    .tab { min-width: 210px; flex: none; }
    .thread { max-height: 60vh; }
  }
</style>
</head>
<body>
<h1>Console degli agenti</h1>
<div class="sub">
  <span class="live"><span class="dot pulse" id="conn"></span><span id="connLabel">in diretta</span></span>
  <span>|</span><span id="clock">--</span>
  <span>|</span><a href="/flusso">flusso e stato dei worker</a>
  <span>|</span><span id="hostinfo">--</span>
</div>

<div class="tot" id="tot"></div>

<div class="layout">
  <div class="side" id="side"></div>
  <div class="main">
    <div class="head" id="head"></div>
    <div class="thread" id="thread"></div>
    <div class="composer" id="composer">
      <textarea id="msg" placeholder="Scrivi l'incarico e premi Ctrl+Invio"></textarea>
      <div class="row">
        <span id="opts" class="row grow"></span>
        <button class="stop" id="stopBtn" style="display:none">ferma</button>
        <button class="go" id="sendBtn">invia</button>
      </div>
      <div class="hint" id="hint"></div>
    </div>
  </div>
</div>

<script>
const TOKEN = '__CONSOLE_TOKEN__';   // sostituito dal server a ogni richiesta
const sideEl = document.getElementById('side');
const threadEl = document.getElementById('thread');
const headEl = document.getElementById('head');
const totEl = document.getElementById('tot');
const msgEl = document.getElementById('msg');
const sendBtn = document.getElementById('sendBtn');
const stopBtn = document.getElementById('stopBtn');
const optsEl = document.getElementById('opts');
const hintEl = document.getElementById('hint');

let workers = [];          // catalogo dal server
let sessions = [];         // sessioni Claude vive
let chats = [];            // scambi conclusi
let live = new Map();      // jobId -> {agent, prompt, text, trace, at}
let selected = localStorage.getItem('nosai.console.agent') || 'local-7b';
const expanded = new Set();

function esc(v) {
  return String(v == null ? '' : v).replace(/[&<>"']/g, c => (
    {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
}
function fmtTime(v) {
  if (!v) return '--';
  return new Date(v * 1000).toLocaleTimeString('it-IT', {hour12: false});
}
function fmtDur(ms) {
  if (ms == null) return '--';
  if (ms < 1000) return ms + ' ms';
  if (ms < 60000) return (ms / 1000).toFixed(1) + ' s';
  return Math.floor(ms / 60000) + 'm ' + Math.round((ms % 60000) / 1000) + 's';
}
function fmtAgo(at) {
  if (!at) return '--';
  const s = Math.max(0, Math.round(Date.now() / 1000 - at));
  if (s < 60) return s + ' s fa';
  if (s < 3600) return Math.round(s / 60) + ' min fa';
  return Math.round(s / 3600) + ' h fa';
}
function num(v) { return (v || 0).toLocaleString('it-IT'); }

function agentById(id) {
  const w = workers.find(a => a.id === id);
  if (w) return w;
  const s = sessions.find(a => a.id === id);
  return s || null;
}
function isSession(id) { return id.startsWith('claude:'); }

function renderTotals() {
  let free = 0, paid = 0, cost = 0, calls = 0, failed = 0;
  for (const c of chats) {
    calls += 1;
    if (!c.ok) failed += 1;
    if (c.free) free += c.tokens || 0; else paid += c.tokens || 0;
    if (typeof c.costUsd === 'number') cost += c.costUsd;
  }
  let sessTok = 0, sessCost = 0, active = 0;
  for (const s of sessions) {
    sessTok += (s.outputTokens || 0) + (s.inputTokens || 0);
    if (typeof s.costUsd === 'number') sessCost += s.costUsd;
    if (s.status === 'ATTIVO') active += 1;
  }
  totEl.innerHTML =
    '<div class="free"><b>' + num(free) + '</b>token a costo zero</div>' +
    '<div class="paid"><b>' + num(paid) + '</b>token a pagamento (console)</div>' +
    '<div><b>' + num(sessTok) + '</b>token delle sessioni Claude</div>' +
    '<div><b>$' + (cost + sessCost).toFixed(2) + '</b>costo dichiarato</div>' +
    '<div><b>' + active + ' / ' + sessions.length + '</b>sessioni attive</div>' +
    '<div><b>' + num(calls) + '</b>invii dalla console' + (failed ? ' (' + failed + ' falliti)' : '') + '</div>';
}

function renderSide() {
  const parts = ['<h2>Worker</h2>'];
  for (const w of workers) {
    const mine = chats.filter(c => c.agent === w.id);
    const tok = mine.reduce((s, c) => s + (c.tokens || 0), 0);
    const busy = [...live.values()].some(j => j.agent === w.id);
    parts.push(
      '<div class="tab' + (w.id === selected ? ' on' : '') + (w.ready ? '' : ' dim') +
      '" data-id="' + w.id + '">' +
      '<div class="tname"><span class="pip ' + (busy ? 'LAVORA' : (w.ready ? 'PRONTO' : 'ASSENTE')) +
      '"></span>' + esc(w.name) + '</div>' +
      '<div class="tmeta">' + esc(w.model) + ' &middot; ' + esc(w.cost) + '</div>' +
      '<div class="tmeta' + (busy ? ' doing' : '') + '">' +
      (busy ? 'sta rispondendo...' : (w.ready ? mine.length + ' invii &middot; ' + num(tok) + ' token'
                                              : esc(w.why))) + '</div></div>');
  }
  parts.push('<h2>Sessioni Claude</h2>');
  if (!sessions.length) parts.push('<div class="void" style="padding:8px">nessuna sessione recente</div>');
  for (const s of sessions) {
    const busy = [...live.values()].some(j => j.agent === s.id);
    parts.push(
      '<div class="tab' + (s.id === selected ? ' on' : '') + '" data-id="' + s.id + '">' +
      '<div class="tname"><span class="pip ' + (busy ? 'LAVORA' : s.status) + '"></span>' +
      esc(s.name) + '</div>' +
      '<div class="tmeta">' + esc(s.model || 'modello ignoto') + ' &middot; ' +
      num(s.outputTokens) + ' token emessi</div>' +
      '<div class="tmeta doing">' + esc(s.activityTool ? s.activityTool + ': ' + s.activity
                                        : (s.status === 'ATTIVO' ? 'in lavorazione' : 'ferma ' + fmtAgo(s.lastSeen))) +
      '</div></div>');
  }
  sideEl.innerHTML = parts.join('');
  sideEl.querySelectorAll('.tab').forEach(el => {
    el.onclick = () => {
      selected = el.dataset.id;
      localStorage.setItem('nosai.console.agent', selected);
      renderAll(true);
    };
  });
}

function renderHead() {
  const a = agentById(selected);
  if (!a) { headEl.innerHTML = '<div class="void">agente non piu\' presente</div>'; return; }
  if (isSession(selected)) {
    const chipList = [
      '<span class="chip">turni <b>' + num(a.turns) + '</b></span>',
      '<span class="chip">strumenti <b>' + num(a.tools) + '</b></span>',
      '<span class="chip">emessi <b>' + num(a.outputTokens) + '</b></span>',
      '<span class="chip">letti da cache <b>' + num(a.cacheReadTokens) + '</b></span>',
      (typeof a.costUsd === 'number' ? '<span class="chip paid">costo <b>$' + a.costUsd.toFixed(2) + '</b></span>' : ''),
      (a.partial ? '<span class="chip">conteggio dalla coda del transcript</span>' : ''),
      '<span class="chip">id <b>' + esc(a.sessionId.slice(0, 8)) + '</b></span>',
    ].join('');
    headEl.innerHTML =
      '<div class="hname">' + esc(a.name) + ' <span class="' + (a.status === 'ATTIVO' ? 'ok' : '') +
      '" style="font-size:11px">' + esc(a.status) + '</span></div>' +
      '<div class="hmeta">' + esc(a.model) + ' &middot; ramo ' + esc(a.branch || '?') +
      ' &middot; ultimo segno di vita ' + fmtAgo(a.lastSeen) + '</div>' +
      (a.activity ? '<div class="hdoing">&rsaquo; ' + esc(a.activityTool) + ': ' + esc(a.activity) + '</div>' : '') +
      (a.said ? '<div class="hmeta" style="margin-top:6px">ha detto: ' + esc(a.said.slice(0, 220)) + '</div>' : '') +
      '<div class="chips">' + chipList + '</div>';
  } else {
    const mine = chats.filter(c => c.agent === a.id);
    const tok = mine.reduce((s, c) => s + (c.tokens || 0), 0);
    headEl.innerHTML =
      '<div class="hname">' + esc(a.name) + '</div>' +
      '<div class="hmeta">' + esc(a.host) + ' &middot; ' + esc(a.model) + ' &middot; ' + esc(a.cost) + '</div>' +
      (a.ready ? '' : '<div class="hdoing" style="color:#fbbf24">' + esc(a.why) + '</div>') +
      '<div class="chips"><span class="chip ' + (a.cost === 'gratis' ? 'free' : 'paid') + '">token spesi qui <b>' +
      num(tok) + '</b></span><span class="chip">invii <b>' + mine.length + '</b></span></div>';
  }
}

function bubble(kind, text, key) {
  const long = (text || '').length > 1400;
  const open = expanded.has(key);
  return '<div class="bubble ' + kind + (open ? ' tall' : '') + '">' + esc(text || '(vuoto)') + '</div>' +
    (long ? '<button class="more" data-key="' + esc(key) + '">' +
      (open ? 'riduci' : 'apri per intero (' + num((text || '').length) + ' caratteri)') + '</button>' : '');
}

function chips(c) {
  const out = ['<span class="chip ' + (c.free ? 'free' : 'paid') + '">token <b>' + num(c.tokens) + '</b></span>'];
  if (c.promptTokens != null) out.push('<span class="chip">prompt <b>' + num(c.promptTokens) + '</b></span>');
  if (c.completionTokens != null) out.push('<span class="chip">risposta <b>' + num(c.completionTokens) + '</b></span>');
  if (c.reasoningTokens) out.push('<span class="chip">ragionamento <b>' + num(c.reasoningTokens) + '</b></span>');
  if (c.cacheHitTokens) out.push('<span class="chip">cache <b>' + num(c.cacheHitTokens) + '</b></span>');
  if (typeof c.costUsd === 'number') out.push('<span class="chip paid">costo <b>$' + c.costUsd.toFixed(4) + '</b></span>');
  if (c.turns) out.push('<span class="chip">giri <b>' + num(c.turns) + '</b></span>');
  if (c.sessionId) out.push('<span class="chip">sessione <b>' + esc(c.sessionId.slice(0, 8)) + '</b></span>');
  return '<div class="chips">' + out.join('') + '</div>';
}

function renderThread(toBottom) {
  const atBottom = threadEl.scrollHeight - threadEl.scrollTop - threadEl.clientHeight < 80;
  const mine = chats.filter(c => c.agent === selected);
  const jobs = [...live.entries()].filter(([, j]) => j.agent === selected);
  const parts = [];

  if (!mine.length && !jobs.length) {
    const a = agentById(selected);
    parts.push('<div class="void">' + (isSession(selected)
      ? 'Nessun comando inviato a questa sessione dalla console.<br>Quello che sta facendo si legge qui sopra, in diretta.'
      : 'Nessun invio a questo worker. Scrivi qui sotto: la risposta arriva parola per parola.') +
      (a && !a.ready ? '<br><br>' + esc(a.why) : '') + '</div>');
  }
  for (const c of mine) {
    parts.push(
      '<div class="turn' + (c.ok ? '' : ' err') + '">' +
      '<header><span><b>' + esc(c.agentName) + '</b> &middot; ' + esc(c.model) + '</span>' +
      '<span>' + fmtTime(c.at) + ' &middot; ' + fmtDur(c.durationMs) + ' &middot; ' +
      (c.ok ? '<span class="ok">riuscito</span>' : '<span class="ko">fallito</span>') + '</span></header>' +
      '<div class="role">inviato</div>' + bubble('out', c.prompt, c.id + ':p') +
      (c.trace ? '<div class="role">traccia</div>' + bubble('trace', c.trace, c.id + ':t') : '') +
      '<div class="role">' + (c.ok ? 'risposta' : 'errore') + '</div>' +
      bubble(c.ok ? 'in' : 'err', c.ok ? c.response : (c.error || c.response), c.id + ':r') +
      chips(c) + '</div>');
  }
  for (const [id, j] of jobs) {
    parts.push(
      '<div class="turn run"><header><span><b>' + esc(j.agentName || '') + '</b></span>' +
      '<span>' + fmtTime(j.at) + ' &middot; <span class="runc">in corso</span></span></header>' +
      '<div class="role">inviato</div>' + bubble('out', j.prompt, id + ':p') +
      (j.trace ? '<div class="role">traccia</div><div class="bubble trace">' + esc(j.trace.slice(-2000)) + '</div>' : '') +
      '<div class="role">risposta</div>' +
      '<div class="bubble in">' + esc(j.text) + '<span class="caret"></span></div></div>');
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

function renderComposer() {
  const a = agentById(selected);
  const busy = [...live.values()].some(j => j.agent === selected);
  const claude = isSession(selected) || selected === 'claude-new';
  optsEl.innerHTML = claude
    ? '<select id="optModel"><option value="predefinito">modello predefinito</option>' +
      '<option value="haiku">haiku (economico)</option><option value="sonnet">sonnet</option>' +
      '<option value="opus">opus</option></select>' +
      '<label class="opt"><input type="checkbox" id="optEdit"> puo\' modificare i file</label>'
    : '<label class="opt">temperatura <select id="optTemp">' +
      '<option value="0.1">0.1</option><option value="0.2" selected>0.2</option>' +
      '<option value="0.6">0.6</option></select></label>';
  sendBtn.disabled = busy || (a && a.ready === false);
  sendBtn.textContent = busy ? 'in corso...' : 'invia';
  stopBtn.style.display = busy ? '' : 'none';
  msgEl.disabled = a && a.ready === false;
  document.getElementById('composer').classList.toggle('busy', busy);
  hintEl.innerHTML = isSession(selected)
    ? 'Il messaggio riprende la sessione <b>' + esc(selected.slice(7, 15)) +
      '</b> in modalita\' headless (<code>claude --resume</code>): non compare nella finestra aperta sul desktop, ma lavora sulla stessa storia e sulla stessa cartella.'
    : (selected === 'claude-new'
      ? 'Avvia un agente Claude nuovo in questa cartella. Di suo puo\' solo leggere: spunta la casella per lasciargli modificare i file.'
      : 'Ctrl+Invio per inviare. La risposta arriva in streaming e i token si contano a fine risposta.');
}

function renderAll(toBottom) {
  renderTotals(); renderSide(); renderHead(); renderThread(toBottom); renderComposer();
}

async function send() {
  const text = msgEl.value.trim();
  if (!text) return;
  const options = {};
  const model = document.getElementById('optModel');
  const edit = document.getElementById('optEdit');
  const temp = document.getElementById('optTemp');
  if (model) options.model = model.value;
  if (edit) options.canEdit = edit.checked;
  if (temp) options.temperature = parseFloat(temp.value);
  sendBtn.disabled = true;
  const res = await fetch('/api/send', {
    method: 'POST',
    headers: {'Content-Type': 'application/json', 'X-Console-Token': TOKEN},
    body: JSON.stringify({agent: selected, prompt: text, options}),
  }).then(r => r.json()).catch(e => ({error: String(e)}));
  if (res.error) { hintEl.innerHTML = '<span class="warn">' + esc(res.error) + '</span>'; sendBtn.disabled = false; return; }
  msgEl.value = '';
  renderComposer();
}

async function stopJob() {
  const mine = [...live.entries()].filter(([, j]) => j.agent === selected);
  for (const [id] of mine) {
    await fetch('/api/stop', {
      method: 'POST',
      headers: {'Content-Type': 'application/json', 'X-Console-Token': TOKEN},
      body: JSON.stringify({jobId: id}),
    }).catch(() => {});
  }
}

sendBtn.onclick = send;
stopBtn.onclick = stopJob;
msgEl.addEventListener('keydown', ev => {
  if (ev.key === 'Enter' && (ev.ctrlKey || ev.metaKey)) { ev.preventDefault(); send(); }
});

let source = null;
function connect() {
  source = new EventSource('/events');
  source.onopen = () => {
    document.getElementById('conn').classList.remove('off');
    document.getElementById('connLabel').textContent = 'in diretta';
  };
  source.onmessage = (ev) => {
    const data = JSON.parse(ev.data);
    if (data.kind === 'snapshot') {
      workers = data.workers || [];
      sessions = data.sessions || [];
      chats = (data.chats || []).filter(c => c.op === 'console' || c.agent);
      live = new Map();
      (data.jobs || []).forEach(j => live.set(j.id, {...j, text: '', trace: ''}));
      document.getElementById('hostinfo').textContent = data.cwd || '';
      renderAll(true);
    } else if (data.kind === 'sessions') {
      sessions = data.sessions || [];
      renderSide(); renderHead(); renderTotals();
    } else if (data.kind === 'job') {
      const prev = live.get(data.job.id) || {text: '', trace: ''};
      live.set(data.job.id, {...prev, ...data.job});
      renderAll(true);
    } else if (data.kind === 'chunk') {
      const job = live.get(data.jobId);
      if (job) {
        if (data.channel === 'text') job.text += data.text; else job.trace += data.text;
        if (job.agent === selected) renderThread(false);
      }
    } else if (data.kind === 'jobEnd') {
      live.delete(data.jobId);
      renderComposer();
    } else if (data.kind === 'chat') {
      chats.push(data.chat);
      renderAll(false);
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
}, 1000);
</script>
</body>
</html>
"""
