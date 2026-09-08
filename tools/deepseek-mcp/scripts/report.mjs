#!/usr/bin/env node
/**
 * One page out of the delegation log: every delegation the log records, the
 * most recent first, with what each one did and whether it is still running.
 *
 * `watch.mjs` answers "what is happening right now", one event at a time.
 * This answers "what happened, and what is still open" -- the question you ask
 * after stepping away, or when three sessions have been delegating at once and
 * the flat log has interleaved them all.
 *
 * Like the watcher, it only reads the log file: it never talks to the API,
 * never touches the repository, and cannot influence a delegation in flight.
 *
 *   node scripts/report.mjs                   scrive logs/report.html
 *   node scripts/report.mjs --markdown        scrive logs/report.md
 *   node scripts/report.mjs --stdout          stampa invece di scrivere
 *   node scripts/report.mjs --out <percorso>  sceglie il file da scrivere
 *   node scripts/report.mjs --file <registro> legge un registro diverso
 */

import fs from 'node:fs';
import path from 'node:path';

import { defaultLogFile } from '../src/eventLog.mjs';
import { parseLine } from '../src/eventView.mjs';
import { groupDelegations, renderMarkdown, renderPage, summarise } from '../src/eventReport.mjs';

const USAGE = 'node scripts/report.mjs [--markdown] [--stdout] [--out <percorso>] [--file <registro>]\n';

function parseArgs(argv) {
    const options = { markdown: false, stdout: false, out: null, file: null, help: false, unknown: null };
    for (let i = 0; i < argv.length; i += 1) {
        const arg = argv[i];
        if (arg === '--markdown' || arg === '--md') options.markdown = true;
        else if (arg === '--stdout') options.stdout = true;
        else if (arg === '--out') {
            options.out = argv[i + 1] ?? null;
            i += 1;
        } else if (arg === '--file') {
            options.file = argv[i + 1] ?? null;
            i += 1;
        } else if (arg === '--help' || arg === '-h') options.help = true;
        else options.unknown = arg;
    }
    return options;
}

/**
 * Every readable event in the file, and how many lines were not readable.
 *
 * An unreadable line is counted rather than dropped in silence: the report is a
 * claim about what happened, and a claim built on a file it could not fully
 * read has to say so.
 */
function readEvents(file) {
    const text = fs.readFileSync(file, 'utf8');
    const events = [];
    let unreadable = 0;
    for (const line of text.split('\n')) {
        if (line.trim() === '') continue;
        const event = parseLine(line);
        if (event === null) unreadable += 1;
        else events.push(event);
    }
    return { events, unreadable };
}

function main() {
    const options = parseArgs(process.argv.slice(2));
    if (options.help) {
        process.stdout.write(USAGE);
        return 0;
    }
    if (options.unknown !== null) {
        process.stderr.write('report: argomento sconosciuto ' + options.unknown + '\n' + USAGE);
        return 2;
    }

    const file = options.file ?? defaultLogFile();
    // Missing log is a failure, not an empty page: a report nobody can build is
    // not the same claim as a report that found no delegations.
    if (!fs.existsSync(file)) {
        process.stderr.write('report: nessun registro in ' + file + '\n');
        return 1;
    }

    const { events, unreadable } = readEvents(file);
    const delegations = groupDelegations(events);
    const output = options.markdown ? renderMarkdown(events) : renderPage(delegations);

    if (options.stdout) {
        process.stdout.write(output);
    } else {
        const target = options.out ?? path.join(path.dirname(file), options.markdown ? 'report.md' : 'report.html');
        fs.mkdirSync(path.dirname(target), { recursive: true });
        fs.writeFileSync(target, output, 'utf8');
        process.stdout.write('scritto: ' + target + '\n');
    }

    const s = summarise(delegations);
    process.stderr.write(
        s.total + ' deleghe, ' + s.open + ' in corso' + (s.quiet > 0 ? ' (' + s.quiet + ' ferme)' : '') +
            ', ' + s.files + ' file toccati\n'
    );
    if (unreadable > 0) {
        process.stderr.write(
            '(' + unreadable + (unreadable === 1 ? ' riga illeggibile saltata' : ' righe illeggibili saltate') + ')\n'
        );
    }
    return 0;
}

try {
    process.exitCode = main();
} catch (err) {
    process.stderr.write('report: ' + err.message + '\n');
    process.exitCode = 1;
}
