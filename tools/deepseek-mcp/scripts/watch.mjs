#!/usr/bin/env node
/**
 * Live view of what DeepSeek is doing, reading the delegation log as it grows.
 *
 * This is a separate program from the MCP server on purpose: the server owns a
 * stdio pipe and may never print anything for a human, so the human gets their
 * own process. It only reads the log file -- it never talks to the API, never
 * touches the repository, and cannot influence a delegation in flight.
 *
 *   node scripts/watch.mjs              segue solo cio che accade da adesso
 *   node scripts/watch.mjs --last       riparte dall'ultima delega, poi segue
 *   node scripts/watch.mjs --all        tutto il registro, poi segue
 *   node scripts/watch.mjs --id <id>    una delega sola
 *   node scripts/watch.mjs --no-follow  stampa e termina
 */

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { LOG_DIR_ENV } from '../src/eventLog.mjs';
import { formatEvent, parseLine } from '../src/eventView.mjs';

const POLL_MS = 400;

function parseArgs(argv) {
    const options = { mode: 'new', follow: true, colors: true, id: null, file: null };
    for (let i = 0; i < argv.length; i += 1) {
        const arg = argv[i];
        if (arg === '--last') options.mode = 'last';
        else if (arg === '--all') options.mode = 'all';
        else if (arg === '--no-follow') options.follow = false;
        else if (arg === '--no-color') options.colors = false;
        else if (arg === '--id') {
            options.id = argv[i + 1] ?? null;
            options.mode = 'all';
            i += 1;
        } else if (arg === '--file') {
            options.file = argv[i + 1] ?? null;
            i += 1;
        } else if (arg === '--help' || arg === '-h') {
            options.help = true;
        } else {
            options.unknown = arg;
        }
    }
    return options;
}

function defaultFile(env = process.env) {
    const dir = env[LOG_DIR_ENV] ?? fileURLToPath(new URL('../logs/', import.meta.url));
    return path.join(dir, 'delegations.jsonl');
}

/** Prints one line per event, skipping anything that is not one. */
function print(events, options) {
    for (const event of events) {
        if (options.id && event.id !== options.id) continue;
        const line = formatEvent(event, { colors: options.colors });
        if (line !== null) process.stdout.write(line + '\n');
    }
}

/**
 * Parses complete lines, and says how many were unreadable instead of dropping
 * them in silence: a line the viewer cannot read is a gap in what the operator
 * is being shown, and a gap has to be visible to count as one.
 */
function parseComplete(lines) {
    const events = [];
    let unreadable = 0;
    for (const line of lines) {
        if (line.trim() === '') continue;
        const event = parseLine(line);
        if (event === null) unreadable += 1;
        else events.push(event);
    }
    return { events, unreadable };
}

function reportUnreadable(count) {
    if (count > 0) {
        process.stdout.write('(' + count + (count === 1 ? ' riga illeggibile saltata' : ' righe illeggibili saltate') + ')\n');
    }
}

/** Every event in the file, plus the byte offset the reader should continue from. */
function readAll(file) {
    const text = fs.readFileSync(file, 'utf8');
    const { events, unreadable } = parseComplete(text.split('\n'));
    return { events, unreadable, size: Buffer.byteLength(text, 'utf8') };
}

/** The events of the last delegation the file records, by its `delegation_start`. */
function lastDelegation(events) {
    let start = -1;
    for (let i = events.length - 1; i >= 0; i -= 1) {
        if (events[i].ev === 'delegation_start') {
            start = i;
            break;
        }
    }
    if (start === -1) return events;
    const id = events[start].id;
    return events.slice(start).filter((e) => e.id === id);
}

function readFrom(file, offset) {
    const size = fs.statSync(file).size;
    if (size === offset) return { text: '', size };
    // A file that shrank was replaced or cleared; start over rather than read garbage.
    if (size < offset) return { text: fs.readFileSync(file, 'utf8'), size, restarted: true };
    const fd = fs.openSync(file, 'r');
    try {
        const buffer = Buffer.alloc(size - offset);
        fs.readSync(fd, buffer, 0, buffer.length, offset);
        return { text: buffer.toString('utf8'), size };
    } finally {
        fs.closeSync(fd);
    }
}

async function main() {
    const options = parseArgs(process.argv.slice(2));
    if (options.help) {
        process.stdout.write(
            'node scripts/watch.mjs [--last|--all|--id <id>] [--no-follow] [--no-color] [--file <percorso>]\n'
        );
        return;
    }

    const file = options.file ?? defaultFile();
    const colors = options.colors && process.stdout.isTTY && process.env.NO_COLOR === undefined;
    const view = { ...options, colors };

    process.stdout.write('registro: ' + file + '\n');

    let offset = 0;
    let pending = '';

    if (fs.existsSync(file)) {
        const { events, unreadable, size } = readAll(file);
        offset = size;
        if (options.mode === 'all') print(events, view);
        else if (options.mode === 'last') print(lastDelegation(events), view);
        if (options.mode !== 'new') {
            if (events.length === 0) process.stdout.write('(il registro e vuoto)\n');
            reportUnreadable(unreadable);
        }
    } else if (options.follow) {
        process.stdout.write('(nessun registro ancora: resto in attesa della prima delega)\n');
    } else {
        process.stdout.write('(nessun registro: nessuna delega e stata eseguita dopo l attivazione)\n');
        return;
    }

    if (!options.follow) return;
    process.stdout.write('--- in ascolto, Ctrl+C per uscire ---\n');

    for (;;) {
        await new Promise((resolve) => setTimeout(resolve, POLL_MS));
        if (!fs.existsSync(file)) continue;

        const { text, size, restarted } = readFrom(file, offset);
        if (restarted) {
            pending = '';
            process.stdout.write('--- il registro e stato sostituito: riparto ---\n');
        }
        offset = size;
        if (text === '') continue;

        pending += text;
        const lines = pending.split('\n');
        // The last piece may be half a line the writer has not finished yet.
        pending = lines.pop() ?? '';
        const { events, unreadable } = parseComplete(lines);
        print(events, view);
        reportUnreadable(unreadable);
    }
}

main().catch((err) => {
    process.stderr.write('watch: ' + err.message + '\n');
    process.exit(1);
});
