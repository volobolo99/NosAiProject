/**
 * Turns the flat event log into the data shape a page consumes: one entry per
 * delegation, most recently started first, each carrying its own events. This
 * module builds no page itself; the file that shows the data is another one.
 *
 * Several delegations can be open at once -- every Claude Code session runs its
 * own MCP server and they all append to the same file -- so grouping by id is
 * what makes "who is working right now" answerable at all.
 *
 * Nothing here interprets the work. A delegation with no closing event is
 * reported as still open, never as failed: the log says what was recorded, and
 * silence is not an outcome.
 */

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
 * Whatever a caller interpolates into markup out of this log -- a task, a path,
 * a model's own words -- is untrusted by construction. This is the one thing
 * standing between a reasoning block and a page that executes it.
 */
export function escapeHtml(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}
