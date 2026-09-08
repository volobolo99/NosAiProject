/**
 * Turns the flat event log into the shape a page can show: one entry per
 * delegation, most recently started first, each carrying its own events.
 *
 * Several delegations can be open at once -- every Claude Code session runs its
 * own MCP server and they all append to the same file -- so grouping by id is
 * what makes "who is working right now" answerable at all.
 *
 * Nothing here interprets the work. A delegation with no closing event is
 * reported as still open, never as failed: the log says what was recorded, and
 * silence is not an outcome.
 */

import { clockOf, markdownOf, seconds, shortId, thousands } from './eventView.mjs';

const OPENING = 'delegation_start';
const CLOSING = new Set(['delegation_end', 'delegation_refused', 'setup_error']);

/** Milliseconds without a single event after which an open delegation is called quiet. */
export const QUIET_AFTER_MS = 120000;

function millis(ts) {
    const value = Date.parse(ts);
    return Number.isNaN(value) ? null : value;
}

/**
 * Groups events by delegation id.
 *
 * @param {object[]} events every event read from the log, in file order
 * @param {number} [nowMs] clock, injected by tests
 */
export function groupDelegations(events, nowMs = Date.now()) {
    const byId = new Map();

    for (const event of events) {
        if (typeof event?.id !== 'string') continue;
        let entry = byId.get(event.id);
        if (entry === undefined) {
            entry = {
                id: event.id,
                model: null,
                modelSource: null,
                workingDirectory: null,
                allowedPaths: [],
                readOnly: false,
                task: null,
                taskChars: null,
                budget: null,
                status: null,
                events: [],
                rounds: 0,
                toolCalls: 0,
                refusedCalls: 0,
                fileChanges: [],
                totalTokens: 0,
                startedAt: null,
                lastAt: null,
                durationMs: null,
                error: null
            };
            byId.set(event.id, entry);
        }

        entry.events.push(event);
        const at = millis(event.ts);
        if (at !== null) {
            if (entry.startedAt === null) entry.startedAt = at;
            entry.lastAt = at;
        }

        switch (event.ev) {
            case OPENING:
                entry.model = event.model ?? null;
                entry.modelSource = event.modelSource ?? null;
                entry.workingDirectory = event.workingDirectory ?? null;
                entry.allowedPaths = Array.isArray(event.allowedPaths) ? event.allowedPaths : [];
                entry.readOnly = event.readOnly === true;
                entry.task = event.task ?? null;
                entry.taskChars = event.taskChars ?? null;
                entry.budget = event.budget ?? null;
                break;
            case 'api_request_start':
                entry.rounds = Math.max(entry.rounds, event.round ?? 0);
                break;
            case 'api_request_end':
                entry.rounds = Math.max(entry.rounds, event.round ?? 0);
                entry.totalTokens += event.usage?.totalTokens ?? 0;
                break;
            case 'tool_call':
                entry.toolCalls += 1;
                if (event.ok === false) entry.refusedCalls += 1;
                break;
            case 'file_change':
                entry.fileChanges.push(event);
                break;
            case 'delegation_end':
                entry.status = event.status ?? 'end';
                entry.durationMs = event.durationMs ?? null;
                entry.error = event.error ?? null;
                if (typeof event.usage?.totalTokens === 'number') entry.totalTokens = event.usage.totalTokens;
                break;
            case 'delegation_refused':
                entry.status = 'refused';
                entry.error = event.reason ?? null;
                break;
            case 'setup_error':
                entry.status = 'setup_error';
                entry.error = event.error ?? null;
                break;
            default:
                break;
        }
    }

    const list = [...byId.values()];
    for (const entry of list) {
        entry.open = entry.status === null;
        entry.quiet = entry.open && entry.lastAt !== null && nowMs - entry.lastAt > QUIET_AFTER_MS;
        entry.sinceMs = entry.lastAt === null ? null : nowMs - entry.lastAt;
        if (entry.durationMs === null && entry.startedAt !== null && entry.lastAt !== null) {
            entry.durationMs = entry.lastAt - entry.startedAt;
        }
    }
    list.sort((a, b) => (b.startedAt ?? 0) - (a.startedAt ?? 0));
    return list;
}

/** Counts for the header: how much is running, how much has closed. */
export function summarise(delegations) {
    return {
        total: delegations.length,
        open: delegations.filter((d) => d.open).length,
        quiet: delegations.filter((d) => d.quiet).length,
        files: delegations.reduce((n, d) => n + d.fileChanges.length, 0),
        tokens: delegations.reduce((n, d) => n + d.totalTokens, 0)
    };
}

/**
 * Escapes text for HTML.
 *
 * Everything on this page that is not a number comes from a language model or
 * from a file path: it is untrusted by construction, and it is interpolated
 * into markup. This is the only thing standing between a reasoning block and
 * the page executing it.
 */
export function escapeHtml(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

const STATUS_LABEL = {
    ok: 'conclusa',
    end: 'conclusa',
    refused: 'rifiutata',
    setup_error: 'errore di avvio'
};

/**
 * How long a delegation has been silent, in the unit a reader can act on.
 *
 * `seconds` from the viewer is right for one request; a delegation can sit open
 * for hours, and "5322.5 s" is a number nobody converts in their head.
 */
function sinceLabel(ms) {
    if (typeof ms !== 'number' || !Number.isFinite(ms)) return '?';
    const total = Math.round(ms / 1000);
    if (total < 90) return total + ' s';
    const minutes = Math.round(total / 60);
    if (minutes < 90) return minutes + ' min';
    const hours = Math.floor(minutes / 60);
    const rest = minutes % 60;
    return hours + ' h' + (rest > 0 ? ' ' + rest + ' min' : '');
}

/** The word and the colour class for one delegation's state. */
function stateOf(entry) {
    if (!entry.open) {
        const label = STATUS_LABEL[entry.status] ?? entry.status;
        const bad = entry.status !== 'ok' && entry.status !== 'end';
        return { label, kind: bad ? 'bad' : 'done' };
    }
    // Quiet is not failed. The log records what happened; a delegation that has
    // said nothing for two minutes may still be thinking, and the page says that
    // rather than deciding for the reader.
    return entry.quiet
        ? { label: 'ferma da ' + sinceLabel(entry.sinceMs), kind: 'quiet' }
        : { label: 'in corso', kind: 'live' };
}

function row(label, value) {
    if (value === null || value === undefined || value === '') return '';
    return '<div class="row"><span class="k">' + escapeHtml(label) + '</span><span class="v">' + value + '</span></div>';
}

function budgetOf(entry) {
    const b = entry.budget;
    if (!b) return null;
    return escapeHtml(b.maxApiRounds + ' giri · ' + b.maxToolCalls + ' strumenti · ' + b.timeoutSeconds + ' s');
}

function filesOf(entry) {
    if (entry.fileChanges.length === 0) return null;
    const items = entry.fileChanges.map(
        (f) =>
            '<li><span class="act">' + escapeHtml(f.action) + '</span> <code>' + escapeHtml(f.path) + '</code> ' +
            '<span class="bytes">' + escapeHtml(thousands(f.bytesBefore) + ' → ' + thousands(f.bytesAfter) + ' B') +
            '</span></li>'
    );
    return '<ul class="files">' + items.join('') + '</ul>';
}

function cardOf(entry) {
    const state = stateOf(entry);
    const counters = [
        entry.rounds + ' giri',
        entry.toolCalls + ' strumenti' + (entry.refusedCalls > 0 ? ' (' + entry.refusedCalls + ' rifiutati)' : ''),
        entry.fileChanges.length + ' file',
        thousands(entry.totalTokens) + ' token',
        seconds(entry.durationMs)
    ].join(' · ');

    const files = filesOf(entry);

    return [
        '<article class="card ' + state.kind + '">',
        '<header>',
        '<h2>' + escapeHtml(entry.model ?? 'modello ignoto') + '</h2>',
        '<code class="id">' + escapeHtml(shortId(entry.id)) + '</code>',
        '<span class="state">' + escapeHtml(state.label) + '</span>',
        '<time>' + escapeHtml(entry.startedAt === null ? '--:--:--' : clockOf(new Date(entry.startedAt).toISOString())) +
            '</time>',
        '</header>',
        row('cartella', '<code>' + escapeHtml(entry.workingDirectory ?? '?') + '</code>'),
        row(
            'perimetro',
            entry.allowedPaths.map((p) => '<code>' + escapeHtml(p) + '</code>').join(' ') +
                (entry.readOnly ? ' <span class="ro">sola lettura</span>' : '')
        ),
        row('tetti', budgetOf(entry)),
        row('conto', escapeHtml(counters)),
        entry.error ? row('errore', '<span class="err">' + escapeHtml(entry.error) + '</span>') : '',
        entry.task ? '<details><summary>incarico</summary><pre>' + escapeHtml(entry.task) + '</pre></details>' : '',
        files ? '<details><summary>file toccati</summary>' + files + '</details>' : '',
        '</article>'
    ]
        .filter((part) => part !== '')
        .join('\n');
}

const STYLE = [
    ':root { color-scheme: light dark; --bg:#f6f6f4; --fg:#1b1b1a; --card:#fff; --line:#e0e0dc; --dim:#6c6c68;',
    '        --live:#1f7a3f; --quiet:#a8700a; --done:#4a4a48; --bad:#a32020; }',
    '@media (prefers-color-scheme: dark) {',
    '  :root { --bg:#16161a; --fg:#e8e8e4; --card:#1e1e23; --line:#32323a; --dim:#9a9a95;',
    '          --live:#5fd08a; --quiet:#e0aa3e; --done:#8a8a86; --bad:#f07070; }',
    '}',
    '* { box-sizing: border-box; }',
    'body { margin:0; padding:24px; background:var(--bg); color:var(--fg);',
    '       font:14px/1.5 ui-sans-serif, system-ui, -apple-system, "Segoe UI", sans-serif; }',
    'h1 { font-size:18px; margin:0 0 4px; }',
    '.sub { color:var(--dim); margin:0 0 20px; }',
    '.counts { display:flex; flex-wrap:wrap; gap:8px; margin:0 0 20px; padding:0; list-style:none; }',
    '.counts li { background:var(--card); border:1px solid var(--line); border-radius:8px; padding:8px 12px; }',
    '.counts b { font-size:18px; display:block; }',
    '.counts span { color:var(--dim); font-size:12px; }',
    '.card { background:var(--card); border:1px solid var(--line); border-left:4px solid var(--done);',
    '        border-radius:8px; padding:12px 16px; margin:0 0 12px; }',
    '.card.live { border-left-color:var(--live); }',
    '.card.quiet { border-left-color:var(--quiet); }',
    '.card.bad { border-left-color:var(--bad); }',
    'header { display:flex; align-items:baseline; gap:10px; flex-wrap:wrap; }',
    'h2 { font-size:15px; margin:0; }',
    '.id { color:var(--dim); }',
    '.state { font-weight:600; }',
    '.live .state { color:var(--live); }',
    '.quiet .state { color:var(--quiet); }',
    '.bad .state { color:var(--bad); }',
    '.done .state { color:var(--done); }',
    'time { margin-left:auto; color:var(--dim); }',
    '.row { display:flex; gap:10px; padding:3px 0; align-items:baseline; }',
    '.k { color:var(--dim); min-width:78px; flex:none; }',
    '.v { min-width:0; overflow-wrap:anywhere; }',
    '.ro { color:var(--quiet); font-weight:600; }',
    '.err { color:var(--bad); }',
    'code { font-family:ui-monospace, "Cascadia Mono", Consolas, monospace; font-size:12.5px; }',
    'details { margin-top:8px; }',
    'summary { cursor:pointer; color:var(--dim); }',
    'pre { background:var(--bg); border:1px solid var(--line); border-radius:6px; padding:10px;',
    '      overflow-x:auto; white-space:pre-wrap; overflow-wrap:anywhere; margin:8px 0 0; }',
    '.files { margin:8px 0 0; padding-left:18px; }',
    '.files li { padding:2px 0; }',
    '.act { font-weight:600; }',
    '.bytes { color:var(--dim); }',
    '.empty { color:var(--dim); }'
].join('\n');

/**
 * The whole report as one self-contained HTML file: no script, no network, no
 * font to fetch. It is opened from disk, often while a delegation is still
 * running, and a page that needs something it cannot reach is a page that lies
 * about the state of the work.
 *
 * @param {object[]} delegations output of groupDelegations
 * @param {Date} [generatedAt] clock, injected by tests
 */
export function renderPage(delegations, generatedAt = new Date()) {
    const s = summarise(delegations);
    const counts = [
        ['deleghe', String(s.total)],
        ['in corso', String(s.open)],
        ['ferme', String(s.quiet)],
        ['file toccati', String(s.files)],
        ['token', thousands(s.tokens)]
    ]
        .map(([label, value]) => '<li><b>' + escapeHtml(value) + '</b><span>' + escapeHtml(label) + '</span></li>')
        .join('');

    const body =
        delegations.length === 0
            ? '<p class="empty">Il registro non contiene ancora nessuna delega.</p>'
            : delegations.map(cardOf).join('\n');

    return [
        '<!doctype html>',
        '<html lang="it">',
        '<head>',
        '<meta charset="utf-8">',
        '<meta name="viewport" content="width=device-width, initial-scale=1">',
        '<title>DeepSeek — deleghe</title>',
        '<style>',
        STYLE,
        '</style>',
        '</head>',
        '<body>',
        '<h1>DeepSeek — deleghe</h1>',
        '<p class="sub">Istantanea del registro, presa il ' + escapeHtml(generatedAt.toLocaleString('it-IT')) +
            '. La pagina non si aggiorna da sola: rigenerala per vedere il seguito.</p>',
        '<ul class="counts">' + counts + '</ul>',
        body,
        '</body>',
        '</html>',
        ''
    ].join('\n');
}

/**
 * The same log as the Markdown diary, for pasting into a document rather than
 * opening in a browser. Events that add nothing in prose are dropped by
 * `markdownOf`, which decides that on its own.
 */
export function renderMarkdown(events, generatedAt = new Date()) {
    const lines = ['# DeepSeek — deleghe', '', '_Registro al ' + generatedAt.toLocaleString('it-IT') + '._'];
    for (const event of events) {
        const line = markdownOf(event);
        if (line !== null) lines.push(line);
    }
    return lines.join('\n') + '\n';
}
