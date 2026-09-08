/**
 * The HTML page that shows every delegation, live or as a saved snapshot.
 *
 * The document is one file with no external resource: it has to open from a
 * local path, on a machine that may be offline, years after the run it shows.
 *
 * Everything the page displays is built with `textContent`, never by pasting
 * markup together. The text comes from a language model and from file paths --
 * untrusted by construction -- and a viewer that could be made to execute what
 * it is meant to display would be a hole opened by the tool that watches for
 * holes.
 *
 * The state logic -- sinceLabel and statusOf -- is defined once, here in this
 * module, and injected into the client script below: the tests call the very
 * source the browser runs.
 */

const STYLE = `
:root {
    color-scheme: light dark;
    --bg: #f6f7f9; --panel: #fff; --ink: #14171c; --dim: #5c6673; --line: #dfe3e8;
    --open: #b26a00; --closed: #2c7a39; --failed: #b3261e; --think: #6a3ea1; --tool: #17607a;
}
@media (prefers-color-scheme: dark) {
    :root {
        --bg: #14171c; --panel: #1b1f26; --ink: #e7eaee; --dim: #97a1ae; --line: #2b313a;
        --open: #e0a458; --closed: #6fcf80; --failed: #ff8a80; --think: #c4a2f0; --tool: #74d1ea;
    }
}
* { box-sizing: border-box; }
body { margin: 0; background: var(--bg); color: var(--ink);
    font: 14px/1.5 "Segoe UI", system-ui, sans-serif; }
header { position: sticky; top: 0; z-index: 2; background: var(--panel);
    border-bottom: 1px solid var(--line); padding: 12px 20px;
    display: flex; gap: 24px; align-items: baseline; flex-wrap: wrap; }
header h1 { font-size: 15px; margin: 0; font-weight: 650; letter-spacing: .2px; }
header .counts { color: var(--dim); font-size: 13px; display: flex; gap: 16px; flex-wrap: wrap; }
header .live { margin-left: auto; font-size: 12px; color: var(--dim); }
main { display: grid; grid-template-columns: minmax(240px, 320px) 1fr; gap: 0; align-items: start; }
@media (max-width: 860px) { main { grid-template-columns: 1fr; } }
.left { border-right: 1px solid var(--line); display: flex; flex-direction: column; min-height: 60vh; }
#diary { border-top: 1px solid var(--line); padding: 10px 14px; max-height: 40vh; overflow: auto;
    font: 12px/1.55 ui-monospace, Consolas, monospace; }
#diary h3 { font: 650 12px/1.5 "Segoe UI", system-ui, sans-serif; margin: 0 0 6px; color: var(--dim); }
#diary div { white-space: pre-wrap; word-break: break-word; }
.card { padding: 12px 16px; border-bottom: 1px solid var(--line); cursor: pointer; }
.card:hover { background: var(--panel); }
.card.sel { background: var(--panel); box-shadow: inset 3px 0 0 var(--tool); }
.card .id { font: 12px/1.4 ui-monospace, Consolas, monospace; color: var(--dim); }
.card .model { font-weight: 620; }
.card .meta { color: var(--dim); font-size: 12px; margin-top: 2px; }
.badge { display: inline-block; padding: 1px 7px; border-radius: 999px; font-size: 11px;
    font-weight: 650; border: 1px solid currentColor; }
.badge.open { color: var(--open); }
.badge.closed { color: var(--closed); }
.badge.failed { color: var(--failed); }
.dot { display: inline-block; width: 7px; height: 7px; border-radius: 50%;
    background: var(--open); margin-right: 6px; animation: pulse 1.6s infinite; }
@keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: .25; } }
#detail { padding: 16px 22px 60px; }
#detail h2 { font-size: 15px; margin: 0 0 2px; }
.sub { color: var(--dim); font-size: 12px; margin-bottom: 14px;
    font-family: ui-monospace, Consolas, monospace; word-break: break-all; }
.task { background: var(--panel); border: 1px solid var(--line); border-left: 3px solid var(--tool);
    padding: 10px 12px; margin: 0 0 18px; white-space: pre-wrap; }
.ev { display: grid; grid-template-columns: 74px 1fr; gap: 10px; padding: 5px 0;
    border-bottom: 1px solid var(--line); }
.ev time { color: var(--dim); font: 12px/1.6 ui-monospace, Consolas, monospace; }
.ev .what { min-width: 0; }
.ev .kind { font-weight: 650; margin-right: 8px; }
.ev.think .kind { color: var(--think); }
.ev.tool .kind { color: var(--tool); }
.ev.file .kind { color: var(--closed); }
.ev.bad .kind, .ev.bad .detail { color: var(--failed); }
.path { font-family: ui-monospace, Consolas, monospace; word-break: break-all; }
blockquote { margin: 6px 0 2px; padding: 6px 12px; border-left: 2px solid var(--think);
    color: var(--ink); background: var(--panel); white-space: pre-wrap; }
.dim { color: var(--dim); }
.empty { padding: 40px 22px; color: var(--dim); }
`;

/**
 * How long a delegation has been silent, in the unit a reader can act on.
 *
 * A delegation can sit open for hours, and "5322 s" is a number nobody
 * converts in their head.
 *
 * Self-contained on purpose: this source is injected into the client script
 * below, where nothing of this module exists.
 */
export function sinceLabel(ms) {
    if (typeof ms !== 'number' || !Number.isFinite(ms)) return '?';
    const total = Math.round(ms / 1000);
    if (total < 90) return total + ' s';
    const minutes = Math.round(total / 60);
    if (minutes < 90) return minutes + ' min';
    const hours = Math.floor(minutes / 60);
    const rest = minutes % 60;
    return hours + ' h' + (rest > 0 ? ' ' + rest + ' min' : '');
}

/**
 * The label and the colour class for one delegation's state.
 *
 * `open` is checked first, and on purpose: a delegation the log has not closed
 * is open, never failed. Silence is not an outcome, so a quiet one says how
 * long it has been quiet and leaves the verdict to the reader.
 *
 * Self-contained on purpose: injected into the client script, like sinceLabel.
 */
export function statusOf(d) {
    if (d.open) return { label: d.quiet ? 'in corso · ferma da ' + sinceLabel(d.sinceMs) : 'in corso', cls: 'open' };
    if (d.status === 'completed') return { label: 'completata', cls: 'closed' };
    if (d.status === 'blocked') return { label: 'bloccata', cls: 'open' };
    return { label: d.status || 'chiusa', cls: 'failed' };
}

const CLIENT_BODY = `
const fmtTime = (ts) => {
    const d = new Date(ts);
    return Number.isNaN(d.getTime()) ? '--:--:--' : d.toTimeString().slice(0, 8);
};
const fmtNum = (n) => typeof n === 'number' && isFinite(n)
    ? Math.round(n).toString().replace(/\\B(?=(\\d{3})+(?!\\d))/g, '.') : '?';
const fmtMs = (ms) => typeof ms !== 'number' ? '?' : ms < 1000 ? ms + ' ms' : (ms / 1000).toFixed(1) + ' s';

let state = { delegations: [], summary: {}, live: false, generatedAt: null };
let selected = null;

function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined && text !== null) node.textContent = text;
    return node;
}

function renderList() {
    const list = document.getElementById('list');
    list.textContent = '';
    if (state.delegations.length === 0) {
        list.append(el('div', 'empty', 'Nessuna delega registrata. La pagina si aggiorna da sola quando ne parte una.'));
        return;
    }
    for (const d of state.delegations) {
        const st = statusOf(d);
        const card = el('div', 'card' + (d.id === selected ? ' sel' : ''));
        const head = el('div');
        if (d.open) head.append(el('span', 'dot'));
        head.append(el('span', 'model', d.model || '(modello ignoto)'));
        head.append(document.createTextNode(' '));
        head.append(el('span', 'badge ' + st.cls, st.label));
        card.append(head);
        card.append(el('div', 'id', d.id));
        card.append(el('div', 'meta',
            fmtTime(d.startedAt) + ' · ' + d.rounds + ' giri · ' + d.toolCalls + ' strumenti · ' +
            d.fileChanges.length + ' file · ' + fmtNum(d.totalTokens) + ' token'));
        card.addEventListener('click', () => { selected = d.id; render(); });
        list.append(card);
    }
}

function evRow(ts, kindText, kindClass, detailNode) {
    const row = el('div', 'ev' + (kindClass ? ' ' + kindClass : ''));
    row.append(el('time', null, fmtTime(ts)));
    const what = el('div', 'what');
    what.append(el('span', 'kind', kindText));
    if (detailNode) what.append(detailNode);
    row.append(what);
    return row;
}

function quoteBlock(passage) {
    const q = el('blockquote', null, passage.text);
    if (passage.truncated) q.append(el('div', 'dim', '[troncato a ' + fmtNum(passage.text.length) + ' di ' + fmtNum(passage.chars) + ' caratteri]'));
    return q;
}

function renderDetail() {
    const box = document.getElementById('detail');
    box.textContent = '';
    const d = state.delegations.find((x) => x.id === selected) || state.delegations[0];
    if (!d) return;
    selected = d.id;

    const st = statusOf(d);
    const title = el('h2', null, (d.model || '(modello ignoto)') + (d.modelSource ? ' (da ' + d.modelSource + ')' : '') + ' — ');
    title.append(el('span', 'badge ' + st.cls, st.label));
    box.append(title);
    box.append(el('div', 'sub', d.id + '  ·  ' + (d.workingDirectory || '') +
        (d.allowedPaths.length ? '  ·  perimetro: ' + d.allowedPaths.join(', ') : '') +
        (d.readOnly ? '  ·  SOLA LETTURA' : '') +
        (d.budget ? '  ·  tetti ' + d.budget.maxApiRounds + ' giri / ' + d.budget.maxToolCalls + ' strumenti / ' + d.budget.timeoutSeconds + ' s' : '')));
    if (d.task) box.append(el('div', 'task', d.task + (d.taskChars ? '\\n\\n[incarico di ' + fmtNum(d.taskChars) + ' caratteri, estratto]' : '')));

    for (const e of d.events) {
        if (e.ev === 'delegation_start') continue;
        if (e.ev === 'api_request_start') {
            box.append(evRow(e.ts, 'giro ' + e.round, null, el('span', 'dim', 'richiesta inviata')));
        } else if (e.ev === 'api_request_end') {
            box.append(evRow(e.ts, 'giro ' + e.round, null, el('span', 'dim',
                fmtMs(e.ms) + ' · fine=' + (e.finishReason || '?') + ' · strumenti chiesti ' + e.toolCallsRequested +
                ' · token ' + fmtNum(e.usage && e.usage.totalTokens) +
                ' (cache hit ' + fmtNum(e.usage && e.usage.promptCacheHitTokens) + ')')));
        } else if (e.ev === 'api_request_error') {
            box.append(evRow(e.ts, 'giro ' + e.round + ' fallito', 'bad', el('span', 'detail', e.error || '')));
        } else if (e.ev === 'assistant_message') {
            if (e.reasoning) {
                const row = evRow(e.ts, 'ragiona', 'think', el('span', 'dim', 'giro ' + e.round + ' · ' + fmtNum(e.reasoning.chars) + ' caratteri'));
                row.querySelector('.what').append(quoteBlock(e.reasoning));
                box.append(row);
            }
            if (e.text) {
                const row = evRow(e.ts, 'dice', 'think', el('span', 'dim', 'giro ' + e.round));
                row.querySelector('.what').append(quoteBlock(e.text));
                box.append(row);
            }
        } else if (e.ev === 'tool_call') {
            const target = e.path || e.directory || e.pattern || e.glob || '';
            const detail = el('span');
            if (target) detail.append(el('span', 'path', target));
            if (e.ok === false) {
                detail.append(document.createTextNode('  '));
                detail.append(el('span', 'detail', 'RIFIUTATO — ' + (e.reason || 'senza motivo')));
            }
            box.append(evRow(e.ts, e.name, e.ok === false ? 'bad' : 'tool', detail));
        } else if (e.ev === 'file_change') {
            const detail = el('span');
            detail.append(el('span', 'path', e.path));
            detail.append(el('span', 'dim', '  ' + fmtNum(e.bytesBefore) + ' → ' + fmtNum(e.bytesAfter) + ' B'));
            box.append(evRow(e.ts, e.action, 'file', detail));
        } else if (e.ev === 'worker_report') {
            box.append(evRow(e.ts, 'rapporto', null, el('span', 'dim',
                'criteri dichiarati ' + e.acceptanceCriteriaMet + ' · blocchi ' + e.blockers)));
        } else if (e.ev === 'delegation_end') {
            box.append(evRow(e.ts, 'fine: ' + e.status, e.status === 'completed' ? 'file' : 'bad',
                el('span', 'dim', e.rounds + ' giri · ' + e.toolCalls + ' strumenti (' + e.refusedCalls +
                    ' rifiutati) · ' + e.filesChanged + ' file · ' + fmtMs(e.durationMs) +
                    ' · token ' + fmtNum(e.usage && e.usage.totalTokens) + (e.error ? ' · ' + e.error : ''))));
        } else if (e.ev === 'delegation_refused' || e.ev === 'setup_error') {
            box.append(evRow(e.ts, e.ev === 'setup_error' ? 'avvio fallito' : 'rifiutata', 'bad',
                el('span', 'detail', e.reason || e.error || '')));
        }
    }
    if (d.open) {
        box.append(evRow(new Date().toISOString(), 'in corso', null,
            el('span', 'dim', d.quiet ? 'ferma da ' + sinceLabel(d.sinceMs) : 'in attesa del prossimo evento')));
    }
}

function renderHeader() {
    const s = state.summary || {};
    document.getElementById('counts').textContent =
        (s.open || 0) + ' in corso · ' + (s.total || 0) + ' deleghe · ' +
        (s.files || 0) + ' file toccati · ' + fmtNum(s.tokens || 0) + ' token';
    const live = document.getElementById('live');
    live.textContent = state.live
        ? 'aggiornata ' + fmtTime(state.generatedAt)
        : 'istantanea del ' + new Date(state.generatedAt).toLocaleString('it-IT') + ' (non aggiornata)';
}

function renderDiary() {
    const box = document.getElementById('diary');
    const stuck = box.scrollTop + box.clientHeight >= box.scrollHeight - 8;
    box.textContent = '';
    box.append(el('h3', null, 'Diario di tutti gli agenti — ' + (state.activityFile || 'logact.md')));
    const lines = (state.activity || []).filter((l) => l.trim() !== '');
    if (lines.length === 0) {
        box.append(el('div', 'dim', 'Nessuna riga ancora.'));
        return;
    }
    for (const line of lines) box.append(el('div', null, line));
    // Chi stava leggendo in fondo continua a vedere l'ultima riga; chi era
    // risalito a leggere resta dov'era.
    if (stuck) box.scrollTop = box.scrollHeight;
}

function render() { renderHeader(); renderList(); renderDetail(); renderDiary(); }

async function poll() {
    try {
        const res = await fetch('/api/state', { cache: 'no-store' });
        if (res.ok) { state = await res.json(); render(); }
    } catch (err) { /* il server e' chiuso: la pagina resta com'e' */ }
    setTimeout(poll, 1500);
}

state = JSON.parse(document.getElementById('data').textContent);
render();
if (state.live) setTimeout(poll, 1500);
`;

// One implementation, two runtimes. The state logic the tests call is the very
// source the browser runs: injected here, never written a second time by hand.
const CLIENT = String(sinceLabel) + '\n' + String(statusOf) + '\n' + CLIENT_BODY;

/**
 * The whole page as one string.
 *
 * @param {object} payload { delegations, summary, live, generatedAt }
 */
export function renderPage(payload) {
    // `</script>` inside the data would close the tag early; escaping `<` keeps
    // the JSON inert wherever it ends up.
    const json = JSON.stringify(payload).replace(/</g, '\\u003c');
    return [
        '<!doctype html>',
        '<html lang="it"><head><meta charset="utf-8">',
        '<meta name="viewport" content="width=device-width, initial-scale=1">',
        '<title>NosAi — DeepSeek al lavoro</title>',
        '<style>' + STYLE + '</style>',
        '</head><body>',
        '<header><h1>DeepSeek al lavoro</h1>',
        '<div class="counts" id="counts"></div>',
        '<div class="live" id="live"></div></header>',
        '<main><div class="left"><div id="list"></div><div id="diary"></div></div>',
        '<div id="detail"></div></main>',
        '<script id="data" type="application/json">' + json + '</script>',
        '<script>' + CLIENT + '</script>',
        '</body></html>'
    ].join('\n');
}
