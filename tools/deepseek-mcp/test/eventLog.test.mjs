import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { test, describe, beforeEach, afterEach } from 'node:test';

import { runDelegation, STATUS } from '../src/agentLoop.mjs';
import { resolveBudget } from '../src/config.mjs';
import {
    LOG_ENABLED_ENV,
    LOG_THOUGHTS_ENV,
    createEventLog,
    excerpt,
    newDelegationId,
    toolArgFields
} from '../src/eventLog.mjs';
import { makeTempRoot, recordingSleep, scriptedFetch, toolCallResponse } from './helpers.mjs';

const API = { apiKey: 'sk-test-SENTINEL-KEY', baseUrl: 'https://api.deepseek.com', model: 'deepseek-v4-flash' };
const FILE_CONTENT = 'CONTENUTO-SENTINELLA-DEL-FILE';

/** Collects the lines an event log would write, without touching the disk. */
function collectingLog(env = {}) {
    const lines = [];
    const log = createEventLog({ env, writer: (line) => lines.push(line), id: 'd20260908063000-test' });
    return { log, lines, parsed: () => lines.map((l) => JSON.parse(l)) };
}

describe('event log', () => {
    test('every event is one JSON line carrying timestamp, delegation id and name', () => {
        const { log, lines, parsed } = collectingLog();
        log.event('delegation_start', { model: 'deepseek-v4-flash', rounds: 0 });
        log.event('delegation_end', { status: 'completed' });

        assert.equal(lines.length, 2);
        for (const line of lines) assert.ok(line.endsWith('\n'), 'each event ends the line it owns');

        const [start, end] = parsed();
        assert.equal(start.ev, 'delegation_start');
        assert.equal(start.id, 'd20260908063000-test');
        assert.equal(start.model, 'deepseek-v4-flash');
        assert.match(start.ts, /^\d{4}-\d{2}-\d{2}T/);
        assert.equal(end.ev, 'delegation_end');
    });

    test('NOSAI_DEEPSEEK_LOG=0 turns the log off without turning off the delegation id', () => {
        const lines = [];
        const log = createEventLog({ env: { [LOG_ENABLED_ENV]: '0' }, writer: (line) => lines.push(line) });
        log.event('delegation_start', { model: 'deepseek-v4-flash' });

        assert.equal(log.enabled, false);
        assert.equal(lines.length, 0);
        assert.match(log.id, /^d\d{14}-[a-z0-9]{4}$/);
    });

    test('a sink that throws disables the log instead of failing the delegation', () => {
        let attempts = 0;
        const log = createEventLog({
            env: {},
            writer: () => {
                attempts += 1;
                throw new Error('disk full');
            }
        });

        assert.doesNotThrow(() => log.event('delegation_start', {}));
        log.event('delegation_end', {});
        assert.equal(attempts, 1, 'after the first failure nothing else is attempted');
    });

    test('the file is JSON Lines under the directory it was given', () => {
        const root = makeTempRoot('nosai-log-');
        try {
            const log = createEventLog({ env: {}, dir: root.dir });
            log.event('delegation_start', { model: 'deepseek-v4-flash' });

            assert.equal(log.file, path.join(root.dir, 'delegations.jsonl'));
            const written = fs.readFileSync(log.file, 'utf8').trim().split('\n');
            assert.equal(written.length, 1);
            assert.equal(JSON.parse(written[0]).ev, 'delegation_start');
        } finally {
            root.cleanup();
        }
    });

    test('tool arguments are reduced to what names a target, never to content', () => {
        const fields = toolArgFields({
            path: 'src/Foo.cs',
            content: FILE_CONTENT,
            oldText: 'before',
            newText: 'after',
            pattern: 'class\\s+Foo'
        });

        assert.deepEqual(fields, { path: 'src/Foo.cs', pattern: 'class\\s+Foo' });
    });

    test('an excerpt is one flat line, cut at the ceiling', () => {
        assert.equal(excerpt('  due   righe\nunite  '), 'due righe unite');
        assert.equal(excerpt('abcdef', 3), 'abc...');
        assert.equal(excerpt('   '), null);
        assert.equal(excerpt(undefined), null);
    });

    test('delegation ids sort by time', () => {
        const early = newDelegationId(() => new Date('2026-09-08T06:00:00Z'), () => 0.1);
        const late = newDelegationId(() => new Date('2026-09-08T07:00:00Z'), () => 0.1);
        assert.ok(early < late);
        assert.match(early, /^d\d{14}-[a-z0-9]{4}$/);
    });
});

describe('what the delegation loop records', () => {
    let root;
    beforeEach(() => {
        root = makeTempRoot();
    });
    afterEach(() => {
        root.cleanup();
    });

    test('a full delegation leaves request boundaries, tool calls and file changes on the log', async () => {
        const { log, lines, parsed } = collectingLog();
        const fetchImpl = scriptedFetch([
            toolCallResponse([{ name: 'write_file', args: { path: 'src/app.txt', content: FILE_CONTENT } }], {
                usage: { prompt_tokens: 100, completion_tokens: 20, total_tokens: 120 }
            }),
            toolCallResponse([
                { name: 'report_done', args: { summary: 'fatto', acceptanceCriteriaMet: ['ok'], blockers: [] } }
            ])
        ]);

        const result = await runDelegation({
            api: API,
            budget: resolveBudget({}),
            request: {
                task: 'Scrivi una riga dentro il perimetro assegnato.',
                workingDirectory: root.dir,
                allowedPaths: ['src/**'],
                acceptanceCriteria: ['src/app.txt esiste']
            },
            fetchImpl,
            sleepImpl: recordingSleep(),
            log
        });

        assert.equal(result.status, STATUS.completed);

        const events = parsed();
        const names = events.map((e) => e.ev);
        assert.deepEqual(
            names.filter((n) => n.startsWith('api_request')),
            ['api_request_start', 'api_request_end', 'api_request_start', 'api_request_end']
        );

        const firstEnd = events.find((e) => e.ev === 'api_request_end');
        assert.equal(firstEnd.round, 1);
        assert.equal(firstEnd.finishReason, 'tool_calls');
        assert.equal(firstEnd.toolCallsRequested, 1);
        assert.equal(firstEnd.usage.totalTokens, 120);
        assert.equal(typeof firstEnd.ms, 'number');

        const write = events.find((e) => e.ev === 'tool_call' && e.name === 'write_file');
        assert.equal(write.path, 'src/app.txt');
        assert.equal(write.ok, true);
        assert.equal(write.content, undefined, 'the log names the file, it does not copy it');

        const change = events.find((e) => e.ev === 'file_change');
        assert.equal(change.path, 'src/app.txt');
        assert.equal(change.action, 'created');
        assert.equal(change.bytesAfter, FILE_CONTENT.length);

        const report = events.find((e) => e.ev === 'worker_report');
        assert.deepEqual({ met: report.acceptanceCriteriaMet, blocked: report.blockers }, { met: 1, blocked: 0 });

        const whole = lines.join('');
        assert.ok(!whole.includes(API.apiKey), 'no credential ever reaches the log');
        assert.ok(!whole.includes(FILE_CONTENT), 'no file content ever reaches the log');
    });

    test('a refused tool call is recorded with its reason and without its arguments', async () => {
        const { log, parsed } = collectingLog();
        const fetchImpl = scriptedFetch([
            toolCallResponse([{ name: 'write_file', args: { path: '../fuori.txt', content: FILE_CONTENT } }]),
            toolCallResponse([{ name: 'report_done', args: { summary: 'bloccato', blockers: ['fuori perimetro'] } }])
        ]);

        await runDelegation({
            api: API,
            budget: resolveBudget({}),
            request: {
                task: 'Prova a scrivere fuori dal perimetro assegnato.',
                workingDirectory: root.dir,
                allowedPaths: ['src/**'],
                acceptanceCriteria: ['il rifiuto è registrato']
            },
            fetchImpl,
            sleepImpl: recordingSleep(),
            log
        });

        const refused = parsed().find((e) => e.ev === 'tool_call' && e.ok === false);
        assert.ok(refused, 'the refusal is on the log');
        assert.equal(refused.name, 'write_file');
        assert.match(refused.reason, /ERROR \[/);
        assert.equal(refused.content, undefined);
    });

    test('an API failure is recorded as its own event with the round it fell on', async () => {
        const { log, parsed } = collectingLog();
        const fetchImpl = scriptedFetch([{ status: 401, body: { error: { message: 'Authentication Fails' } } }]);

        const result = await runDelegation({
            api: API,
            budget: resolveBudget({ maxRetries: 0 }),
            request: {
                task: 'Un incarico che non arriva mai al modello.',
                workingDirectory: root.dir,
                allowedPaths: ['src/**'],
                acceptanceCriteria: ["l'errore è registrato"]
            },
            fetchImpl,
            sleepImpl: recordingSleep(),
            log
        });

        assert.equal(result.status, STATUS.apiError);
        const failure = parsed().find((e) => e.ev === 'api_request_error');
        assert.equal(failure.round, 1);
        assert.equal(failure.status, STATUS.apiError);
        assert.match(failure.error, /HTTP 401/);
    });

    test('the worker reasoning and words are recorded, apart from what it then did', async () => {
        const { log, parsed } = collectingLog();
        const turn = toolCallResponse([{ name: 'report_done', args: { summary: 'fatto' } }]);
        turn.choices[0].message.reasoning_content = 'Prima leggo il file,\npoi decido.';
        turn.choices[0].message.content = 'Chiudo con il rapporto.';

        await runDelegation({
            api: API,
            budget: resolveBudget({}),
            request: {
                task: 'Un incarico che si chiude subito.',
                workingDirectory: root.dir,
                allowedPaths: ['src/**'],
                acceptanceCriteria: ['il ragionamento è registrato']
            },
            fetchImpl: scriptedFetch([turn]),
            sleepImpl: recordingSleep(),
            log
        });

        const said = parsed().find((e) => e.ev === 'assistant_message');
        assert.equal(said.round, 1);
        assert.equal(said.reasoning.text, 'Prima leggo il file,\npoi decido.');
        assert.equal(said.reasoning.truncated, false);
        assert.equal(said.text.text, 'Chiudo con il rapporto.');

        const events = parsed().map((e) => e.ev);
        assert.ok(
            events.indexOf('assistant_message') < events.indexOf('tool_call'),
            'what the worker thought comes before what it did, in the order it happened'
        );
    });

    test('NOSAI_DEEPSEEK_THOUGHTS=0 keeps the words out and leaves the events in', async () => {
        const { log, parsed } = collectingLog({ [LOG_THOUGHTS_ENV]: '0' });
        const turn = toolCallResponse([{ name: 'report_done', args: { summary: 'fatto' } }]);
        turn.choices[0].message.reasoning_content = 'SENTINELLA-DEL-PENSIERO';

        await runDelegation({
            api: API,
            budget: resolveBudget({}),
            request: {
                task: 'Un incarico che si chiude subito.',
                workingDirectory: root.dir,
                allowedPaths: ['src/**'],
                acceptanceCriteria: ['il ragionamento non è registrato']
            },
            fetchImpl: scriptedFetch([turn]),
            sleepImpl: recordingSleep(),
            log
        });

        const events = parsed();
        assert.equal(events.find((e) => e.ev === 'assistant_message'), undefined);
        assert.ok(events.some((e) => e.ev === 'tool_call'), 'the events are still there');
        assert.ok(!JSON.stringify(events).includes('SENTINELLA-DEL-PENSIERO'));
    });
});

describe('the log stays out of the test runner', () => {
    test('under node --test nothing is written to the default directory', () => {
        const log = createEventLog({ env: { NODE_TEST_CONTEXT: 'child-v8' } });
        assert.equal(log.enabled, false);
        assert.equal(log.file, null);
    });

    test('an explicit directory is honoured even under the runner', () => {
        const root = makeTempRoot('nosai-log-runner-');
        try {
            const log = createEventLog({ env: { NODE_TEST_CONTEXT: 'child-v8' }, dir: root.dir });
            assert.equal(log.enabled, true);
            log.event('delegation_start', {});
            assert.ok(fs.existsSync(log.file));
        } finally {
            root.cleanup();
        }
    });

    test('this very suite runs with the guard on, so the real log is untouched', () => {
        assert.notEqual(process.env.NODE_TEST_CONTEXT, undefined);
        assert.equal(createEventLog().enabled, false);
    });
});
