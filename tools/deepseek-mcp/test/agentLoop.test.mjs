import assert from 'node:assert/strict';
import path from 'node:path';
import { test, describe, beforeEach, afterEach } from 'node:test';

import { runDelegation, STATUS } from '../src/agentLoop.mjs';
import { resolveBudget } from '../src/config.mjs';
import { makeTempRoot, scriptedFetch, recordingSleep, toolCallResponse, textResponse } from './helpers.mjs';

const API = { apiKey: 'sk-test-SENTINEL-KEY', baseUrl: 'https://api.deepseek.com', model: 'deepseek-v4-flash' };

function request(root, overrides = {}) {
    return {
        task: 'Aggiungi una riga al file di prova, rispettando il perimetro.',
        workingDirectory: root.dir,
        allowedPaths: ['src/**'],
        acceptanceCriteria: ['src/app.txt contiene la riga nuova'],
        ...overrides
    };
}

function run(root, turns, overrides = {}, budgetArgs = {}) {
    const fetchImpl = scriptedFetch(turns);
    return runDelegation({
        api: API,
        budget: resolveBudget(budgetArgs),
        request: request(root, overrides),
        fetchImpl,
        sleepImpl: recordingSleep()
    }).then((result) => ({ result, fetchImpl }));
}

describe('delegation loop', () => {
    let root;
    beforeEach(() => {
        root = makeTempRoot();
        root.write('src/app.txt', 'alpha\n');
        root.write('secrets/keys.txt', 'do-not-touch');
    });
    afterEach(() => root.cleanup());

    test('reads, writes and closes with a report', async () => {
        const { result, fetchImpl } = await run(root, [
            toolCallResponse([{ name: 'read_file', args: { path: 'src/app.txt' } }]),
            toolCallResponse([{ name: 'write_file', args: { path: 'src/app.txt', content: 'alpha\nbeta\n' } }]),
            toolCallResponse([
                {
                    name: 'report_done',
                    args: { summary: 'Aggiunta la riga beta.', acceptanceCriteriaMet: ['criterio 1: verificato'], blockers: [] }
                }
            ])
        ]);

        assert.equal(result.status, STATUS.completed);
        assert.equal(result.rounds, 3);
        assert.equal(result.toolCalls, 3);
        assert.equal(result.refusedCalls, 0);
        assert.equal(root.read('src/app.txt'), 'alpha\nbeta\n');
        assert.deepEqual(
            result.changes.map((c) => [c.path, c.action]),
            [['src/app.txt', 'modified']]
        );
        assert.equal(result.report.summary, 'Aggiunta la riga beta.');
        assert.equal(fetchImpl.remaining(), 0);
    });

    test('the system prompt names the scope and forbids build, test and further delegation', async () => {
        const { fetchImpl } = await run(root, [
            toolCallResponse([{ name: 'report_done', args: { summary: 'niente da fare' } }])
        ]);
        const system = fetchImpl.calls[0].body.messages[0];
        assert.equal(system.role, 'system');
        assert.match(system.content, /cannot run a shell, build, run tests/);
        assert.match(system.content, /delegate any part of this work/);
        assert.match(system.content, /- src\/\*\*/);
        const user = fetchImpl.calls[0].body.messages[1];
        assert.match(user.content, /ACCEPTANCE CRITERIA/);
        assert.match(user.content, /src\/app\.txt contiene la riga nuova/);
    });

    test('tool results are sent back with the tool_call_id the model issued', async () => {
        const { fetchImpl } = await run(root, [
            toolCallResponse([{ id: 'call_abc', name: 'read_file', args: { path: 'src/app.txt' } }]),
            toolCallResponse([{ name: 'report_done', args: { summary: 'letto' } }])
        ]);
        const secondCallMessages = fetchImpl.calls[1].body.messages;
        const assistant = secondCallMessages.find((m) => m.role === 'assistant');
        const toolResult = secondCallMessages.find((m) => m.role === 'tool');
        assert.equal(assistant.tool_calls[0].id, 'call_abc');
        assert.equal(toolResult.tool_call_id, 'call_abc');
        assert.match(toolResult.content, /1\talpha/);
    });

    test('an out-of-scope write is refused, recorded, and leaves the file untouched', async () => {
        const { result } = await run(root, [
            toolCallResponse([{ name: 'write_file', args: { path: 'secrets/keys.txt', content: 'pwned' } }]),
            toolCallResponse([
                { name: 'report_done', args: { summary: 'Perimetro insufficiente.', blockers: ['secrets/ e fuori perimetro'] } }
            ])
        ]);
        assert.equal(result.status, STATUS.blocked);
        assert.equal(result.refusedCalls, 1);
        assert.equal(result.changes.length, 0);
        assert.equal(root.read('secrets/keys.txt'), 'do-not-touch');
        assert.match(result.errors[0], /OUT_OF_SCOPE/);
    });

    test('an escape above the working directory is refused', async () => {
        const outsideRel = '../' + path.basename(root.dir) + '/../planted.txt';
        const { result } = await run(root, [
            toolCallResponse([{ name: 'write_file', args: { path: outsideRel, content: 'x' } }]),
            toolCallResponse([{ name: 'report_done', args: { summary: 'bloccato', blockers: ['fuori perimetro'] } }])
        ]);
        assert.equal(result.refusedCalls, 1);
        assert.match(result.errors[0], /OUT_OF_SCOPE/);
        assert.equal(result.changes.length, 0);
    });

    test('a missing working directory ends as a setup error before any API call', async () => {
        const fetchImpl = scriptedFetch([]);
        const result = await runDelegation({
            api: API,
            budget: resolveBudget({}),
            request: request(root, { workingDirectory: path.join(root.dir, 'ghost') }),
            fetchImpl
        });
        assert.equal(result.status, STATUS.setupError);
        assert.match(result.error, /INVALID_ROOT/);
        assert.equal(fetchImpl.calls.length, 0);
    });

    test('the round ceiling ends the delegation as budget_exhausted', async () => {
        const turns = Array.from({ length: 3 }, () =>
            toolCallResponse([{ name: 'read_file', args: { path: 'src/app.txt' } }])
        );
        const { result } = await run(root, turns, {}, { maxApiRounds: 3 });
        assert.equal(result.status, STATUS.budgetExhausted);
        assert.equal(result.rounds, 3);
        assert.equal(result.report, null);
    });

    test('the tool-call ceiling refuses further calls and tells the worker to report', async () => {
        const { result, fetchImpl } = await run(
            root,
            [
                toolCallResponse([
                    { id: 'c1', name: 'read_file', args: { path: 'src/app.txt' } },
                    { id: 'c2', name: 'read_file', args: { path: 'src/app.txt' } },
                    { id: 'c3', name: 'read_file', args: { path: 'src/app.txt' } }
                ]),
                toolCallResponse([{ name: 'report_done', args: { summary: 'fermato dal budget' } }])
            ],
            {},
            { maxToolCalls: 2 }
        );
        const results = fetchImpl.calls[1].body.messages.filter((m) => m.role === 'tool');
        assert.equal(results.length, 3);
        assert.match(results[2].content, /ERROR \[BUDGET\] tool call limit of 2 reached/);
        // Two reads spent the budget, the third was refused, and report_done still got through.
        assert.equal(result.toolCalls, 3);
        assert.equal(result.status, STATUS.completed);
        assert.equal(result.report.summary, 'fermato dal budget');
    });

    test('an API error stops the loop and is reported, with no changes claimed', async () => {
        const { result } = await run(root, [
            { status: 401, body: { error: { message: 'Authentication Fails' } } }
        ]);
        assert.equal(result.status, STATUS.apiError);
        assert.match(result.error, /HTTP 401/);
        assert.ok(!result.error.includes('SENTINEL'));
        assert.equal(result.changes.length, 0);
    });

    test('a transient failure is retried inside the same round', async () => {
        const { result, fetchImpl } = await run(root, [
            { status: 429, body: { error: { message: 'rate limit' } } },
            toolCallResponse([{ name: 'report_done', args: { summary: 'ok dopo il ritento' } }])
        ]);
        assert.equal(result.status, STATUS.completed);
        assert.equal(result.rounds, 1, 'a retry is not a new round');
        assert.equal(fetchImpl.calls.length, 2);
    });

    test('three text-only answers end the delegation without a report', async () => {
        const { result, fetchImpl } = await run(root, [
            textResponse('Ecco come farei...'),
            textResponse('Ancora spiegazioni...'),
            textResponse('E altre spiegazioni...')
        ]);
        assert.equal(result.status, STATUS.noReport);
        assert.equal(result.changes.length, 0);
        assert.match(result.finalText, /altre spiegazioni/);
        const nudge = fetchImpl.calls[1].body.messages.find(
            (m) => m.role === 'user' && /does not change any file/.test(m.content)
        );
        assert.ok(nudge, 'the loop must tell the worker that text alone changes nothing');
    });

    test('malformed tool arguments are refused without crashing the loop', async () => {
        const badArgs = {
            id: 'cmpl',
            model: 'deepseek-v4-flash',
            choices: [
                {
                    message: {
                        role: 'assistant',
                        content: '',
                        tool_calls: [{ id: 'c1', type: 'function', function: { name: 'read_file', arguments: '{not json' } }]
                    }
                }
            ],
            usage: { prompt_tokens: 1, completion_tokens: 1, total_tokens: 2 }
        };
        const { result } = await run(root, [
            badArgs,
            toolCallResponse([{ name: 'report_done', args: { summary: 'recuperato' } }])
        ]);
        assert.equal(result.status, STATUS.completed);
        assert.equal(result.refusedCalls, 1);
        assert.match(result.errors[0], /BAD_ARGUMENTS/);
    });

    test('token usage is summed across every round', async () => {
        const { result } = await run(root, [
            toolCallResponse([{ name: 'read_file', args: { path: 'src/app.txt' } }], {
                usage: { prompt_tokens: 100, completion_tokens: 10, total_tokens: 110 }
            }),
            toolCallResponse([{ name: 'report_done', args: { summary: 'fatto' } }], {
                usage: { prompt_tokens: 200, completion_tokens: 20, total_tokens: 220, prompt_cache_hit_tokens: 64 }
            })
        ]);
        assert.equal(result.usage.promptTokens, 300);
        assert.equal(result.usage.completionTokens, 30);
        assert.equal(result.usage.totalTokens, 330);
        assert.equal(result.usage.promptCacheHitTokens, 64);
    });

    test('the wall-clock deadline ends the delegation as a timeout', async () => {
        // Honours the abort signal the way a real fetch does, so the deadline path is the one under test.
        const slowFetch = (url, init) =>
            new Promise((resolve, reject) => {
                init.signal.addEventListener(
                    'abort',
                    () => {
                        const err = new Error('The operation was aborted');
                        err.name = 'AbortError';
                        reject(err);
                    },
                    { once: true }
                );
            });
        const result = await runDelegation({
            api: API,
            // Sub-second deadline, set directly so resolveBudget's 10 s floor does not slow the suite.
            budget: { ...resolveBudget({}), timeoutSeconds: 0.2, requestTimeoutSeconds: 30 },
            request: request(root),
            fetchImpl: slowFetch,
            sleepImpl: recordingSleep(),
            // The loop reads the deadline from its own AbortController; shrink it for the test.
            now: () => Date.now()
        });
        assert.equal(result.status, STATUS.timeout);
    });

    test('a read-only delegation cannot write', async () => {
        const { result } = await run(
            root,
            [
                toolCallResponse([{ name: 'write_file', args: { path: 'src/app.txt', content: 'nuovo' } }]),
                toolCallResponse([{ name: 'report_done', args: { summary: 'sola lettura', blockers: ['scrittura vietata'] } }])
            ],
            { readOnly: true }
        );
        assert.equal(result.status, STATUS.blocked);
        assert.match(result.errors[0], /READ_ONLY/);
        assert.equal(root.read('src/app.txt'), 'alpha\n');
        assert.equal(result.changes.length, 0);
    });
});
