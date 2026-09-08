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

import { markdownOf } from './eventView.mjs';

/** Set to '0' to turn the log off. Unset or anything else leaves it on. */
export const LOG_ENABLED_ENV = 'NOSAI_DEEPSEEK_LOG';

/** Overrides the directory the log file is written to. */
export const LOG_DIR_ENV = 'NOSAI_DEEPSEEK_LOG_DIR';

/**
 * Set to '0' to keep the worker's own words out of the log. On by default: the
 * operator asked to watch DeepSeek think, and a log of tool calls alone shows
 * what was done without ever showing why.
 */
export const LOG_THOUGHTS_ENV = 'NOSAI_DEEPSEEK_THOUGHTS';

/** How much of one assistant turn is kept. Long enough to follow it, bounded. */
export const THOUGHT_CHARS = 4000;

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

/**
 * Path of the shared activity diary, or '0' to write none.
 *
 * The JSON Lines file is for programs; this one is for a person watching a
 * Markdown preview while the work happens. Every agent appends to the same
 * file, so each line carries the short id of the delegation that wrote it.
 */
export const ACTIVITY_MD_ENV = 'NOSAI_ACTIVITY_MD';

const DEFAULT_ACTIVITY_MD = fileURLToPath(new URL('../../../logact.md', import.meta.url));

/** Where the diary is written, so a reader never has to compose the path itself. */
export function defaultActivityFile(env = process.env) {
    return env[ACTIVITY_MD_ENV] ?? DEFAULT_ACTIVITY_MD;
}

/** A log that does nothing: the disabled case, and the default for callers that pass none. */
export const nullEventLog = Object.freeze({
    id: null,
    file: null,
    enabled: false,
    thoughtsEnabled: false,
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

/**
 * A bounded copy of a multi-line passage. Line breaks survive -- reasoning read
 * as one paragraph is reasoning you cannot follow -- and the caller is told when
 * the passage was cut rather than being left to guess.
 */
export function passage(text, max = THOUGHT_CHARS) {
    if (typeof text !== 'string') return null;
    const trimmed = text.trim();
    if (trimmed === '') return null;
    return {
        text: trimmed.length <= max ? trimmed : trimmed.slice(0, max),
        chars: trimmed.length,
        truncated: trimmed.length > max
    };
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
/**
 * The file the log is written to, and therefore the file every reader must open.
 *
 * Kept here beside the writer: a reader that composes this path on its own is a
 * reader that shows an empty page the day the writer moves.
 */
export function defaultLogFile(env = process.env) {
    return path.join(env[LOG_DIR_ENV] ?? DEFAULT_DIR, FILE_NAME);
}

export function createEventLog({
    env = process.env,
    dir,
    id,
    now = () => new Date(),
    writer,
    markdownWriter
} = {}) {
    const delegationId = id ?? newDelegationId(now);
    const chosenDir = dir ?? env[LOG_DIR_ENV];
    // Under `node --test` the suite exercises the whole server, delegation tool
    // included. Without this the test runs would append fixtures to the operator's
    // real log, which is the one file that must only ever hold real work.
    const wouldUseDefaultDir = chosenDir === undefined && writer === undefined;
    if ((env[LOG_ENABLED_ENV] ?? '1') === '0' || (env.NODE_TEST_CONTEXT !== undefined && wouldUseDefaultDir)) {
        return { ...nullEventLog, id: delegationId };
    }

    const targetDir = chosenDir ?? DEFAULT_DIR;
    const file = path.join(targetDir, FILE_NAME);
    let broken = false;

    const append =
        writer ??
        ((line) => {
            fs.mkdirSync(targetDir, { recursive: true });
            fs.appendFileSync(file, line, 'utf8');
        });

    // Where the diary goes, in order of how explicit the intent is. A caller who
    // redirected the machine log without naming a diary gets none: that caller is
    // a test, and the diary it would otherwise touch is the operator's own file
    // in the repository root.
    const configuredMd =
        env[ACTIVITY_MD_ENV] ??
        (markdownWriter !== undefined
            ? '(iniettato)'
            : chosenDir
              ? path.join(chosenDir, 'logact.md')
              : writer !== undefined
                ? '0'
                : DEFAULT_ACTIVITY_MD);
    const activityFile = configuredMd === '0' ? null : configuredMd;
    let diaryBroken = activityFile === null;

    const appendMarkdown =
        markdownWriter ??
        ((text) => {
            fs.mkdirSync(path.dirname(activityFile), { recursive: true });
            fs.appendFileSync(activityFile, text + '\n', 'utf8');
        });

    return {
        id: delegationId,
        file,
        activityFile: diaryBroken ? null : activityFile,
        enabled: true,
        thoughtsEnabled: (env[LOG_THOUGHTS_ENV] ?? '1') !== '0',
        event(name, fields = {}) {
            const event = { ts: now().toISOString(), id: delegationId, ev: name, ...fields };
            if (!broken) {
                try {
                    append(JSON.stringify(event) + '\n');
                } catch (err) {
                    broken = true;
                    process.stderr.write('[nosai-deepseek] event log disabled: ' + err.message + '\n');
                }
            }
            // The diary is a convenience: it fails on its own without taking the
            // machine-readable log, or the delegation, down with it.
            if (diaryBroken) return;
            try {
                const line = markdownOf(event);
                if (line !== null) appendMarkdown(line);
            } catch (err) {
                diaryBroken = true;
                process.stderr.write('[nosai-deepseek] activity diary disabled: ' + err.message + '\n');
            }
        }
    };
}
