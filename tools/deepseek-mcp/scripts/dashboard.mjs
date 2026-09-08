#!/usr/bin/env node
/**
 * The live page: every delegation, every round, every tool call, and what the
 * worker was thinking when it made them -- refreshed on its own while the work
 * happens.
 *
 * Three views on the same log, and they do not overlap:
 *   watch.mjs      one event at a time, in a terminal
 *   report.mjs     a snapshot with no script in it, to archive or to send
 *   dashboard.mjs  this one: a page that keeps up, with the whole timeline
 *
 * It binds 127.0.0.1 and serves nothing but the page and its own JSON. It reads
 * the log and the diary; it never talks to the API, never touches the
 * repository, and cannot influence a delegation in flight.
 *
 *   node scripts/dashboard.mjs                  apre su http://127.0.0.1:7717
 *   node scripts/dashboard.mjs --port 8080      su un'altra porta
 *   node scripts/dashboard.mjs --export a.html  scrive un'istantanea e termina
 */

import fs from 'node:fs';
import http from 'node:http';

import { defaultActivityFile, defaultLogFile } from '../src/eventLog.mjs';
import { parseLine } from '../src/eventView.mjs';
import { groupDelegations, summarise } from '../src/eventReport.mjs';
import { renderPage } from '../src/eventPage.mjs';

const DEFAULT_PORT = 7717;
const HOST = '127.0.0.1';

/** How many diary lines the page carries. Enough to see the shift, not the month. */
const ACTIVITY_TAIL = 400;

function parseArgs(argv) {
    const options = { port: DEFAULT_PORT, file: null, activity: null, export: null, help: false };
    for (let i = 0; i < argv.length; i += 1) {
        const arg = argv[i];
        if (arg === '--port') {
            options.port = Number.parseInt(argv[i + 1] ?? '', 10);
            i += 1;
        } else if (arg === '--file') {
            options.file = argv[i + 1] ?? null;
            i += 1;
        } else if (arg === '--activity') {
            options.activity = argv[i + 1] ?? null;
            i += 1;
        } else if (arg === '--export') {
            options.export = argv[i + 1] ?? null;
            i += 1;
        } else if (arg === '--help' || arg === '-h') {
            options.help = true;
        }
    }
    return options;
}

/** Reads the log, tolerating a file that is being appended to as we read it. */
function readEvents(file) {
    if (!fs.existsSync(file)) return [];
    return fs
        .readFileSync(file, 'utf8')
        .split('\n')
        .map(parseLine)
        .filter((e) => e !== null);
}

function readActivity(file) {
    if (!file || !fs.existsSync(file)) return [];
    const lines = fs.readFileSync(file, 'utf8').split('\n');
    return lines.slice(Math.max(0, lines.length - ACTIVITY_TAIL));
}

function buildState({ file, activityFile, live }) {
    const delegations = groupDelegations(readEvents(file));
    return {
        delegations,
        summary: summarise(delegations),
        activity: readActivity(activityFile),
        activityFile,
        logFile: file,
        live,
        generatedAt: new Date().toISOString()
    };
}

function main() {
    const options = parseArgs(process.argv.slice(2));
    if (options.help) {
        process.stdout.write(
            'node scripts/dashboard.mjs [--port <n>] [--export <file.html>] [--file <registro>] [--activity <logact.md>]\n'
        );
        return;
    }

    const file = options.file ?? defaultLogFile();
    const activityFile = options.activity ?? defaultActivityFile();

    if (options.export) {
        const html = renderPage(buildState({ file, activityFile, live: false }));
        fs.writeFileSync(options.export, html, 'utf8');
        process.stdout.write('istantanea scritta: ' + options.export + '\n');
        return;
    }

    const port = Number.isInteger(options.port) && options.port > 0 ? options.port : DEFAULT_PORT;
    const server = http.createServer((req, res) => {
        const url = (req.url ?? '/').split('?')[0];
        if (url === '/api/state') {
            const body = JSON.stringify(buildState({ file, activityFile, live: true }));
            res.writeHead(200, { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' });
            res.end(body);
            return;
        }
        if (url === '/') {
            const html = renderPage(buildState({ file, activityFile, live: true }));
            res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
            res.end(html);
            return;
        }
        res.writeHead(404, { 'content-type': 'text/plain; charset=utf-8' });
        res.end('non trovato\n');
    });

    server.on('error', (err) => {
        process.stderr.write(
            err.code === 'EADDRINUSE'
                ? 'dashboard: la porta ' + port + ' e gia occupata. Usa --port <n>.\n'
                : 'dashboard: ' + err.message + '\n'
        );
        process.exit(1);
    });

    server.listen(port, HOST, () => {
        process.stdout.write('registro : ' + file + '\n');
        process.stdout.write('diario   : ' + activityFile + '\n');
        process.stdout.write('pagina   : http://' + HOST + ':' + port + '\n');
        if (!fs.existsSync(file)) {
            process.stdout.write('(nessuna delega registrata finora: la pagina si popola da sola)\n');
        }
    });
}

main();
