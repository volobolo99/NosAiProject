/**
 * Renders one logged event as the lines an operator reads.
 *
 * The log is JSON Lines because a machine has to parse it; this is the other
 * half, because a person has to follow it. Nothing here interprets: it lays out
 * what the event says, keeps the worker's own words apart from what the worker
 * actually did, and never turns an intention into a fact.
 */

const MARKERS = {
    delegation_start: 'DELEGA',
    delegation_end: 'FINE',
    delegation_refused: 'RIFIUTATA',
    setup_error: 'AVVIO FALLITO',
    api_request_start: '  ->',
    api_request_end: '  <-',
    api_request_error: '  !!',
    assistant_message: '',
    tool_call: '  USA',
    file_change: '  FILE',
    worker_report: 'RAPPORTO'
};

const ESC = String.fromCharCode(27);

const COLORS = {
    reset: ESC + '[0m',
    dim: ESC + '[2m',
    bold: ESC + '[1m',
    red: ESC + '[31m',
    green: ESC + '[32m',
    blue: ESC + '[34m',
    magenta: ESC + '[35m',
    cyan: ESC + '[36m'
};

const EVENT_COLOR = {
    delegation_start: 'bold',
    delegation_end: 'bold',
    delegation_refused: 'red',
    setup_error: 'red',
    api_request_error: 'red',
    api_request_start: 'dim',
    api_request_end: 'dim',
    assistant_message: 'magenta',
    tool_call: 'cyan',
    file_change: 'green',
    worker_report: 'blue'
};

/** Local wall-clock time of an event, seconds included; the date is on the delegation line. */
export function clockOf(ts) {
    const d = new Date(ts);
    if (Number.isNaN(d.getTime())) return '--:--:--';
    return d.toTimeString().slice(0, 8);
}

export function seconds(ms) {
    if (typeof ms !== 'number') return '?';
    return ms < 1000 ? ms + ' ms' : (ms / 1000).toFixed(1) + ' s';
}

/**
 * Grouped by thousands the Italian way. Done by hand rather than with
 * `toLocaleString`, whose output depends on the ICU data the running Node was
 * built with: a viewer that formats differently on two machines is a viewer
 * whose numbers cannot be compared.
 */
export function thousands(n) {
    if (typeof n !== 'number' || !Number.isFinite(n)) return '?';
    return Math.round(n)
        .toString()
        .replace(/\B(?=(\d{3})+(?!\d))/g, '.');
}

/** Indents a passage under its heading so the worker's words are never mistaken for events. */
function quote(text, indent = '          | ') {
    return text
        .split('\n')
        .map((line) => indent + line)
        .join('\n');
}

function paint(text, color, enabled) {
    if (!enabled || !color || !COLORS[color]) return text;
    return COLORS[color] + text + COLORS.reset;
}

function describe(e) {
    switch (e.ev) {
        case 'delegation_start': {
            const lines = [
                'modello ' + e.model + (e.modelSource ? ' (da ' + e.modelSource + ')' : ''),
                'cartella ' + e.workingDirectory,
                'perimetro ' + (Array.isArray(e.allowedPaths) ? e.allowedPaths.join(', ') : '?') +
                    (e.readOnly ? '   SOLA LETTURA' : ''),
                'tetti ' + e.budget?.maxApiRounds + ' giri, ' + e.budget?.maxToolCalls + ' strumenti, ' +
                    e.budget?.timeoutSeconds + ' s',
                'incarico (' + thousands(e.taskChars) + ' caratteri, ' + e.acceptanceCriteria + ' criteri)'
            ];
            const body = e.task ? '\n' + quote(e.task) : '';
            return { head: e.id, detail: lines.join('\n          ') + body };
        }
        case 'api_request_start':
            return { head: 'giro ' + e.round + ': richiesta inviata' };
        case 'api_request_end':
            return {
                head:
                    'giro ' + e.round + ': ' + seconds(e.ms) +
                    '   fine=' + (e.finishReason ?? '?') +
                    '   strumenti chiesti ' + e.toolCallsRequested +
                    '   token ' + thousands(e.usage?.totalTokens) +
                    ' (cache hit ' + thousands(e.usage?.promptCacheHitTokens) + ')'
            };
        case 'api_request_error':
            return { head: 'giro ' + e.round + ': ' + (e.status ?? 'errore') + ' — ' + e.error };
        case 'assistant_message': {
            const blocks = [];
            if (e.reasoning) {
                blocks.push(
                    'giro ' + e.round + ' — RAGIONA (' + thousands(e.reasoning.chars) + ' caratteri' +
                        (e.reasoning.truncated ? ', troncato' : '') + ')\n' + quote(e.reasoning.text)
                );
            }
            if (e.text) {
                blocks.push(
                    'giro ' + e.round + ' — DICE (' + thousands(e.text.chars) + ' caratteri' +
                        (e.text.truncated ? ', troncato' : '') + ')\n' + quote(e.text.text)
                );
            }
            // The second heading lines up under the first: one turn, one block.
            return { head: blocks.join('\n' + ' '.repeat(9)) };
        }
        case 'tool_call': {
            const target = e.path ?? e.directory ?? e.pattern ?? e.glob ?? '';
            const outcome = e.ok ? '' : '   RIFIUTATO — ' + (e.reason ?? 'senza motivo');
            return { head: e.name + (target ? '  ' + target : '') + outcome };
        }
        case 'file_change':
            return {
                head: e.action + '  ' + e.path + '   ' + thousands(e.bytesBefore) + ' -> ' + thousands(e.bytesAfter) + ' B'
            };
        case 'worker_report':
            return { head: 'criteri dichiarati ' + e.acceptanceCriteriaMet + ', blocchi ' + e.blockers };
        case 'delegation_end':
            return {
                head:
                    e.status + '   ' + e.rounds + ' giri, ' + e.toolCalls + ' strumenti (' + e.refusedCalls +
                    ' rifiutati), ' + e.filesChanged + ' file, ' + seconds(e.durationMs) +
                    ', token ' + thousands(e.usage?.totalTokens) + (e.error ? '\n          errore: ' + e.error : '')
            };
        case 'delegation_refused':
        case 'setup_error':
            return { head: e.reason ?? e.error ?? '(senza motivo)' };
        default:
            return { head: JSON.stringify(e) };
    }
}

/**
 * One event as text. Returns null for a line that is not an event, so a
 * half-written line at the tail of the file never stops the viewer.
 */
export function formatEvent(event, { colors = false } = {}) {
    if (event === null || typeof event !== 'object' || typeof event.ev !== 'string') return null;
    const { head, detail } = describe(event);
    const marker = MARKERS[event.ev] ?? '  ' + event.ev;
    const stamp = clockOf(event.ts);
    const line = (stamp + ' ' + (marker ? marker + ' ' : '') + head).trimEnd();
    const painted = paint(line, EVENT_COLOR[event.ev], colors);
    return detail ? painted + '\n          ' + detail : painted;
}

/** Parses one log line, returning null instead of throwing on a partial write. */
export function parseLine(line) {
    const trimmed = line.trim();
    if (trimmed === '') return null;
    try {
        return JSON.parse(trimmed);
    } catch {
        return null;
    }
}

/** Short tag for one delegation, so concurrent agents stay apart in a shared diary. */
export function shortId(id) {
    return typeof id === 'string' && id.length >= 4 ? id.slice(-4) : '????';
}

/** Markdown blockquote of a passage, indented under its bullet. */
function mdQuote(text) {
    return text
        .split('\n')
        .map((line) => '  > ' + line)
        .join('\n');
}

/**
 * One event as a Markdown fragment for the shared activity diary, or null when
 * the event adds nothing a reader would want in prose.
 *
 * A delegation opens a heading; everything else is a bullet tagged with the
 * delegation's short id, because several agents append to the same file at the
 * same time and a line that does not say who wrote it is noise.
 */
export function markdownOf(e) {
    if (e === null || typeof e !== 'object' || typeof e.ev !== 'string') return null;
    const at = clockOf(e.ts);
    const tag = '`' + shortId(e.id) + '`';

    switch (e.ev) {
        case 'delegation_start':
            return [
                '',
                '### ' + at + ' · ' + e.model + ' · ' + tag,
                '',
                '**Cartella** `' + e.workingDirectory + '`  ',
                '**Perimetro** ' + (Array.isArray(e.allowedPaths) ? e.allowedPaths.map((p) => '`' + p + '`').join(', ') : '?') +
                    (e.readOnly ? ' · **sola lettura**' : '') + '  ',
                '**Tetti** ' + e.budget?.maxApiRounds + ' giri · ' + e.budget?.maxToolCalls + ' strumenti · ' +
                    e.budget?.timeoutSeconds + ' s',
                '',
                e.task ? mdQuote(e.task) : '',
                ''
            ]
                .filter((line, i, all) => !(line === '' && all[i - 1] === ''))
                .join('\n');
        case 'api_request_end':
            return '- ' + at + ' ' + tag + ' giro ' + e.round + ' — ' + seconds(e.ms) + ', fine `' +
                (e.finishReason ?? '?') + '`, token ' + thousands(e.usage?.totalTokens);
        case 'api_request_error':
            return '- ' + at + ' ' + tag + ' **giro ' + e.round + ' fallito** — ' + e.error;
        case 'assistant_message': {
            const blocks = [];
            if (e.reasoning) {
                blocks.push('- ' + at + ' ' + tag + ' **ragiona** (' + thousands(e.reasoning.chars) + ' caratteri' +
                    (e.reasoning.truncated ? ', troncato' : '') + ')\n' + mdQuote(e.reasoning.text));
            }
            if (e.text) {
                blocks.push('- ' + at + ' ' + tag + ' **dice**\n' + mdQuote(e.text.text));
            }
            return blocks.length > 0 ? blocks.join('\n') : null;
        }
        case 'tool_call': {
            const target = e.path ?? e.directory ?? e.pattern ?? e.glob;
            return '- ' + at + ' ' + tag + ' `' + e.name + '`' + (target ? ' `' + target + '`' : '') +
                (e.ok === false ? ' — **rifiutato**: ' + (e.reason ?? 'senza motivo') : '');
        }
        case 'file_change':
            return '- ' + at + ' ' + tag + ' **' + e.action + '** `' + e.path + '` (' +
                thousands(e.bytesBefore) + ' → ' + thousands(e.bytesAfter) + ' byte)';
        case 'worker_report':
            return '- ' + at + ' ' + tag + ' rapporto: ' + e.acceptanceCriteriaMet + ' criteri, ' + e.blockers + ' blocchi';
        case 'delegation_end':
            return '- ' + at + ' ' + tag + ' **fine: ' + e.status + '** — ' + e.rounds + ' giri, ' + e.toolCalls +
                ' strumenti (' + e.refusedCalls + ' rifiutati), ' + e.filesChanged + ' file, ' +
                seconds(e.durationMs) + ', token ' + thousands(e.usage?.totalTokens) +
                (e.error ? ' — ' + e.error : '') + '\n';
        case 'delegation_refused':
            return '- ' + at + ' ' + tag + ' **delega rifiutata** — ' + (e.reason ?? '');
        case 'setup_error':
            return '- ' + at + ' ' + tag + ' **avvio fallito** — ' + (e.error ?? '');
        default:
            return null;
    }
}
