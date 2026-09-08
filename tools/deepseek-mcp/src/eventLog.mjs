/**
 * Append-only operational log for delegations, one JSON object per line.
 *
 * The transport is stdio JSON-RPC, so nothing here may ever reach stdout: the
 * log is a file, and the only fallback is a single line on stderr. It records
 * what happened -- assignment, model, request boundaries, tool calls, file
 * changes, refusals, errors, ceilings -- and never what was said: no file
 * contents, no message text, no worker reasoning, no credentials.
 *
 * A log that breaks must not break a delegation. Every write is guarded, and
 * the first failure disables the log for the rest of the process instead of
 * propagating.
 */

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

/** Set to '0' to turn the log off. Unset or anything else leaves it on. */
export const LOG_ENABLED_ENV = 'NOSAI_DEEPSEEK_LOG';

/** Overrides the directory the log file is written to. */
export const LOG_DIR_ENV = 'NOSAI_DEEPSEEK_LOG_DIR';

/** How much of the assignment is kept: enough to recognise it, not a copy of it. */
export const TASK_EXCERPT_CHARS = 400;

/** How much of an argument or a refusal is kept. */
export const FIELD_EXCERPT_CHARS = 200;

/**
 * Tool arguments worth recording. Everything else -- above all `content`,
 * `oldText` and `newText` -- is deliberately absent: the point of the log is
 * which file was touched, never what was written into it.
 */
const LOGGABLE_ARG_KEYS = ['path', 'directory', 'pattern', 'glob'];

const DEFAULT_DIR = fileURLToPath(new URL('../logs/', import.meta.url));
const FILE_NAME = 'delegations.jsonl';

/** A log that does nothing: the disabled case, and the default for callers that pass none. */
export const nullEventLog = Object.freeze({
    id: null,
    file: null,
    enabled: false,
    event() {}
});

/**
 * Identifier for one delegation: sortable by time, unique enough to tell two
 * concurrent delegations apart in a shared file.
 */
export function newDelegationId(now = () => new Date(), random = Math.random) {
    const stamp = now().toISOString().replace(/[-:T]/g, '').slice(0, 14);
    return 'd' + stamp + '-' + random().toString(36).slice(2, 6).padEnd(4, '0');
}

/** One line of a long string, cut to `max`, or null when there is nothing to record. */
export function excerpt(text, max = FIELD_EXCERPT_CHARS) {
    if (typeof text !== 'string') return null;
    const flat = text.replace(/\s+/g, ' ').trim();
    if (flat === '') return null;
    return flat.length <= max ? flat : flat.slice(0, max) + '...';
}

/** The subset of a tool call's arguments that names a target without quoting content. */
export function toolArgFields(args) {
    const fields = {};
    if (args === null || typeof args !== 'object') return fields;
    for (const key of LOGGABLE_ARG_KEYS) {
        const value = excerpt(args[key]);
        if (value !== null) fields[key] = value;
    }
    return fields;
}

/**
 * Opens the log for one delegation.
 *
 * @param {object} [options]
 * @param {object} [options.env] environment carrying the two switches
 * @param {string} [options.dir] directory override, ahead of the environment
 * @param {string} [options.id] delegation id, generated when absent
 * @param {Function} [options.now] injected clock
 * @param {Function} [options.writer] injected sink, for tests
 */
export function createEventLog({ env = process.env, dir, id, now = () => new Date(), writer } = {}) {
    const delegationId = id ?? newDelegationId(now);
    if ((env[LOG_ENABLED_ENV] ?? '1') === '0') {
        return { ...nullEventLog, id: delegationId };
    }

    const targetDir = dir ?? env[LOG_DIR_ENV] ?? DEFAULT_DIR;
    const file = path.join(targetDir, FILE_NAME);
    let broken = false;

    const append =
        writer ??
        ((line) => {
            fs.mkdirSync(targetDir, { recursive: true });
            fs.appendFileSync(file, line, 'utf8');
        });

    return {
        id: delegationId,
        file,
        enabled: true,
        event(name, fields = {}) {
            if (broken) return;
            try {
                append(JSON.stringify({ ts: now().toISOString(), id: delegationId, ev: name, ...fields }) + '\n');
            } catch (err) {
                broken = true;
                process.stderr.write('[nosai-deepseek] event log disabled: ' + err.message + '\n');
            }
        }
    };
}
