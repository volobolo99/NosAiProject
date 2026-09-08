/**
 * End-to-end checks over the real MCP wire: a client and the server talk to
 * each other in memory, so `tools/list` and `tools/call` exercise the same
 * code path Claude Code uses over stdio.
 */

import assert from 'node:assert/strict';
import { test, describe, beforeEach, afterEach } from 'node:test';

import { Client, InMemoryTransport } from '@modelcontextprotocol/client';

import { DELEGATION_ACTIVE_ENV } from '../src/config.mjs';
import { makeTempRoot, toolCallResponse } from './helpers.mjs';

/** Connects a fresh client to a freshly built server; the module is re-imported per call. */
async function connect(moduleQuery = '') {
    const { createServer } = await import('../src/server.mjs' + moduleQuery);
    const server = createServer();
    const [clientTransport, serverTransport] = InMemoryTransport.createLinkedPair();
    const client = new Client({ name: 'test-client', version: '1.0.0' });
    await Promise.all([server.connect(serverTransport), client.connect(clientTransport)]);
    return { client, close: () => Promise.all([client.close(), server.close()]) };
}

/** Replaces global fetch with a scripted stand-in; the server does not take an injection point. */
function stubFetch(turns) {
    const calls = [];
    let i = 0;
    globalThis.fetch = async (url, init) => {
        calls.push({ url, body: init?.body ? JSON.parse(init.body) : undefined, headers: init?.headers ?? {} });
        const turn = turns[Math.min(i, turns.length - 1)];
        i += 1;
        const status = turn.status ?? 200;
        const payload = turn.status ? turn.body : turn;
        return { ok: status >= 200 && status < 300, status, text: async () => JSON.stringify(payload ?? {}) };
    };
    return calls;
}

const DONE = toolCallResponse([
    { name: 'report_done', args: { summary: 'Fatto.', acceptanceCriteriaMet: ['criterio 1: ok'], blockers: [] } }
]);

function delegation(root, extra = {}) {
    return {
        task: 'Aggiungi la riga beta al file di prova, senza uscire dal perimetro assegnato.',
        workingDirectory: root.dir,
        allowedPaths: ['src/**'],
        acceptanceCriteria: ['src/app.txt contiene beta'],
        ...extra
    };
}

describe('MCP surface', () => {
    let realFetch;
    let envBackup;
    let root;

    beforeEach(() => {
        realFetch = globalThis.fetch;
        envBackup = {
            key: process.env.DEEPSEEK_API_KEY,
            model: process.env.DEEPSEEK_MODEL,
            base: process.env.DEEPSEEK_BASE_URL,
            active: process.env[DELEGATION_ACTIVE_ENV]
        };
        process.env.DEEPSEEK_API_KEY = 'sk-test-SENTINEL-KEY';
        delete process.env.DEEPSEEK_MODEL;
        delete process.env[DELEGATION_ACTIVE_ENV];
        root = makeTempRoot();
        root.write('src/app.txt', 'alpha\n');
    });

    afterEach(() => {
        globalThis.fetch = realFetch;
        for (const [name, value] of [
            ['DEEPSEEK_API_KEY', envBackup.key],
            ['DEEPSEEK_MODEL', envBackup.model],
            ['DEEPSEEK_BASE_URL', envBackup.base],
            [DELEGATION_ACTIVE_ENV, envBackup.active]
        ]) {
            if (value === undefined) delete process.env[name];
            else process.env[name] = value;
        }
        root.cleanup();
    });

    test('publishes exactly one tool, delegate_to_deepseek', async () => {
        const { client, close } = await connect();
        try {
            const { tools } = await client.listTools();
            assert.equal(tools.length, 1);
            const tool = tools[0];
            assert.equal(tool.name, 'delegate_to_deepseek');
            const required = tool.inputSchema.required ?? [];
            for (const field of ['task', 'workingDirectory', 'allowedPaths', 'acceptanceCriteria']) {
                assert.ok(required.includes(field), field + ' must be required');
            }
            assert.deepEqual(tool.inputSchema.properties.model.enum, ['deepseek-v4-flash', 'deepseek-v4-pro']);
        } finally {
            await close();
        }
    });

    test('runs a delegation and reports the file that actually changed', async () => {
        const calls = stubFetch([
            toolCallResponse([{ name: 'write_file', args: { path: 'src/app.txt', content: 'alpha\nbeta\n' } }]),
            DONE
        ]);
        const { client, close } = await connect();
        try {
            const res = await client.callTool({ name: 'delegate_to_deepseek', arguments: delegation(root) });
            const text = res.content[0].text;
            assert.equal(res.isError ?? false, false, text);
            assert.match(text, /stato: completed/);
            assert.match(text, /modello: deepseek-v4-flash \(da default\)/);
            assert.match(text, /modified {2}src\/app\.txt/);
            assert.match(text, /non un tetto di spesa garantito/);
            assert.equal(root.read('src/app.txt'), 'alpha\nbeta\n');
            assert.equal(calls[0].url, 'https://api.deepseek.com/chat/completions');
            assert.equal(calls[0].body.model, 'deepseek-v4-flash');
        } finally {
            await close();
        }
    });

    test('DEEPSEEK_MODEL=deepseek-v4-pro selects pro for the actual request', async () => {
        process.env.DEEPSEEK_MODEL = 'deepseek-v4-pro';
        const calls = stubFetch([DONE]);
        const { client, close } = await connect();
        try {
            const res = await client.callTool({ name: 'delegate_to_deepseek', arguments: delegation(root) });
            assert.match(res.content[0].text, /modello: deepseek-v4-pro \(da DEEPSEEK_MODEL\)/);
            assert.equal(calls[0].body.model, 'deepseek-v4-pro');
        } finally {
            await close();
        }
    });

    test('an explicit model argument overrides the environment', async () => {
        process.env.DEEPSEEK_MODEL = 'deepseek-v4-flash';
        const calls = stubFetch([DONE]);
        const { client, close } = await connect();
        try {
            const res = await client.callTool({
                name: 'delegate_to_deepseek',
                arguments: delegation(root, { model: 'deepseek-v4-pro' })
            });
            assert.match(res.content[0].text, /modello: deepseek-v4-pro \(da argument\)/);
            assert.equal(calls[0].body.model, 'deepseek-v4-pro');
        } finally {
            await close();
        }
    });

    test('an unsupported DEEPSEEK_MODEL fails the call instead of substituting one', async () => {
        process.env.DEEPSEEK_MODEL = 'deepseek-chat';
        const calls = stubFetch([DONE]);
        const { client, close } = await connect();
        try {
            const res = await client.callTool({ name: 'delegate_to_deepseek', arguments: delegation(root) });
            assert.equal(res.isError, true);
            assert.match(res.content[0].text, /Configurazione non valida/);
            assert.match(res.content[0].text, /deepseek-chat/);
            assert.equal(calls.length, 0, 'no request may be sent with an unknown model');
        } finally {
            await close();
        }
    });

    test('a missing API key fails with an actionable message and sends nothing', async () => {
        delete process.env.DEEPSEEK_API_KEY;
        const calls = stubFetch([DONE]);
        const { client, close } = await connect();
        try {
            const res = await client.callTool({ name: 'delegate_to_deepseek', arguments: delegation(root) });
            assert.equal(res.isError, true);
            assert.match(res.content[0].text, /DEEPSEEK_API_KEY is not set/);
            assert.equal(calls.length, 0);
        } finally {
            await close();
        }
    });

    test('an API failure comes back as an error result, not a silent success', async () => {
        stubFetch([{ status: 401, body: { error: { message: 'Authentication Fails' } } }]);
        const { client, close } = await connect();
        try {
            const res = await client.callTool({
                name: 'delegate_to_deepseek',
                arguments: delegation(root, { maxRetries: 0 })
            });
            assert.equal(res.isError, true);
            const text = res.content[0].text;
            assert.match(text, /stato: api_error/);
            assert.match(text, /HTTP 401/);
            assert.ok(!text.includes('SENTINEL'), 'the report must never carry the key');
            assert.match(text, /nessun file toccato/);
        } finally {
            await close();
        }
    });

    test('a working directory that does not exist is refused before any request', async () => {
        const calls = stubFetch([DONE]);
        const { client, close } = await connect();
        try {
            const res = await client.callTool({
                name: 'delegate_to_deepseek',
                arguments: delegation(root, { workingDirectory: root.dir + '-ghost' })
            });
            assert.equal(res.isError, true);
            assert.match(res.content[0].text, /stato: setup_error/);
            assert.equal(calls.length, 0);
        } finally {
            await close();
        }
    });

    test('the schema rejects an empty allowedPaths list', async () => {
        const { client, close } = await connect();
        try {
            // The SDK validates the input schema and returns an error result rather than throwing.
            const res = await client.callTool({
                name: 'delegate_to_deepseek',
                arguments: delegation(root, { allowedPaths: [] })
            });
            assert.equal(res.isError, true);
            assert.match(res.content[0].text, /Input validation error.*allowedPaths/s);
        } finally {
            await close();
        }
    });

    test('a server started inside a delegation refuses to delegate again', async () => {
        process.env[DELEGATION_ACTIVE_ENV] = '1';
        const calls = stubFetch([DONE]);
        // A fresh module instance re-reads the guard at import time, as a spawned process would.
        const { client, close } = await connect('?recursion-guard');
        try {
            const res = await client.callTool({ name: 'delegate_to_deepseek', arguments: delegation(root) });
            assert.equal(res.isError, true);
            assert.match(res.content[0].text, /deleghe ricorsive sono vietate/);
            assert.equal(calls.length, 0);
        } finally {
            await close();
        }
    });

    test('the flag is set during a delegation and cleared afterwards', async () => {
        let flagDuringCall;
        globalThis.fetch = async () => {
            flagDuringCall = process.env[DELEGATION_ACTIVE_ENV];
            return { ok: true, status: 200, text: async () => JSON.stringify(DONE) };
        };
        const { client, close } = await connect();
        try {
            await client.callTool({ name: 'delegate_to_deepseek', arguments: delegation(root) });
            assert.equal(flagDuringCall, '1', 'the guard must be set while the worker runs');
            assert.equal(process.env[DELEGATION_ACTIVE_ENV], undefined, 'and cleared when it ends');
        } finally {
            await close();
        }
    });
});
