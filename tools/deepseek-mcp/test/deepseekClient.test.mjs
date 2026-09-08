import assert from 'node:assert/strict';
import { test, describe } from 'node:test';

import { chatCompletion, listModels, readUsage, DeepSeekApiError } from '../src/deepseekClient.mjs';
import { scriptedFetch, recordingSleep, textResponse } from './helpers.mjs';

const API = { apiKey: 'sk-test-SENTINEL-KEY', baseUrl: 'https://api.deepseek.com', model: 'deepseek-v4-flash' };
const MESSAGES = [{ role: 'user', content: 'ping' }];

describe('chatCompletion', () => {
    test('posts to /chat/completions and returns the parsed body', async () => {
        const fetchImpl = scriptedFetch([textResponse('pong')]);
        const res = await chatCompletion({ ...API, messages: MESSAGES, fetchImpl });
        assert.equal(res.choices[0].message.content, 'pong');
        assert.equal(fetchImpl.calls[0].url, 'https://api.deepseek.com/chat/completions');
        assert.equal(fetchImpl.calls[0].method, 'POST');
        assert.equal(fetchImpl.calls[0].body.model, 'deepseek-v4-flash');
        assert.equal(fetchImpl.calls[0].body.stream, false);
    });

    test('sends the key only in the Authorization header, never in url or body', async () => {
        const fetchImpl = scriptedFetch([textResponse('pong')]);
        await chatCompletion({ ...API, messages: MESSAGES, fetchImpl });
        const call = fetchImpl.calls[0];
        assert.equal(call.headers.authorization, 'Bearer sk-test-SENTINEL-KEY');
        assert.ok(!call.url.includes('SENTINEL'));
        assert.ok(!JSON.stringify(call.body).includes('SENTINEL'));
    });

    test('passes tools and sets tool_choice when tools are supplied', async () => {
        const fetchImpl = scriptedFetch([textResponse('pong')]);
        const tools = [{ type: 'function', function: { name: 'noop', description: 'x', parameters: { type: 'object', properties: {} } } }];
        await chatCompletion({ ...API, messages: MESSAGES, tools, fetchImpl });
        assert.equal(fetchImpl.calls[0].body.tool_choice, 'auto');
        assert.equal(fetchImpl.calls[0].body.tools.length, 1);
    });

    test('retries a 429 and succeeds on the next attempt', async () => {
        const fetchImpl = scriptedFetch([
            { status: 429, body: { error: { message: 'rate limit' } } },
            textResponse('pong')
        ]);
        const sleepImpl = recordingSleep();
        const res = await chatCompletion({
            ...API, messages: MESSAGES, fetchImpl, sleepImpl, maxRetries: 2, jitterImpl: () => 0
        });
        assert.equal(res.choices[0].message.content, 'pong');
        assert.equal(fetchImpl.calls.length, 2);
        assert.deepEqual(sleepImpl.waited, [1000]);
    });

    test('retries 500 and 503 up to maxRetries, then throws the last error', async () => {
        const fetchImpl = scriptedFetch([
            { status: 500, body: { error: { message: 'boom' } } },
            { status: 503, body: { error: { message: 'busy' } } },
            { status: 502, body: { error: { message: 'gateway' } } }
        ]);
        const sleepImpl = recordingSleep();
        await assert.rejects(
            () => chatCompletion({ jitterImpl: () => 0, ...API, messages: MESSAGES, fetchImpl, sleepImpl, maxRetries: 2 }),
            (err) => err instanceof DeepSeekApiError && err.status === 502 && /gateway/.test(err.message)
        );
        assert.equal(fetchImpl.calls.length, 3);
        assert.deepEqual(sleepImpl.waited, [1000, 2000]);
    });

    test('does not retry a 401 and never echoes the key in the error', async () => {
        const fetchImpl = scriptedFetch([{ status: 401, body: { error: { message: 'Authentication Fails' } } }]);
        const sleepImpl = recordingSleep();
        await assert.rejects(
            () => chatCompletion({ jitterImpl: () => 0, ...API, messages: MESSAGES, fetchImpl, sleepImpl, maxRetries: 3 }),
            (err) => {
                assert.equal(err.status, 401);
                assert.ok(!err.message.includes('SENTINEL'), 'error message must not carry the key');
                return /Authentication Fails/.test(err.message);
            }
        );
        assert.equal(fetchImpl.calls.length, 1, 'a 401 must not be retried');
        assert.deepEqual(sleepImpl.waited, []);
    });

    test('does not retry a 400 model error and does not fall back to another model', async () => {
        const fetchImpl = scriptedFetch([
            { status: 400, body: { error: { message: 'Model Not Exist' } } }
        ]);
        await assert.rejects(
            () => chatCompletion({ ...API, model: 'deepseek-v4-pro', messages: MESSAGES, fetchImpl, sleepImpl: recordingSleep() }),
            (err) => err.status === 400 && /Model Not Exist/.test(err.message)
        );
        assert.equal(fetchImpl.calls.length, 1);
        assert.equal(fetchImpl.calls[0].body.model, 'deepseek-v4-pro');
    });

    test('a 200 with a non-JSON body is an error, not a silent empty result', async () => {
        const fetchImpl = scriptedFetch([{ status: 200, body: '<html>gateway page</html>' }]);
        await assert.rejects(
            () => chatCompletion({ jitterImpl: () => 0, ...API, messages: MESSAGES, fetchImpl, sleepImpl: recordingSleep() }),
            (err) => /non-JSON body/.test(err.message)
        );
    });

    test('a network failure is retried, then surfaces as an API error', async () => {
        let calls = 0;
        const fetchImpl = async () => {
            calls += 1;
            throw new Error('ECONNRESET');
        };
        const sleepImpl = recordingSleep();
        await assert.rejects(
            () => chatCompletion({ jitterImpl: () => 0, ...API, messages: MESSAGES, fetchImpl, sleepImpl, maxRetries: 1 }),
            (err) => err instanceof DeepSeekApiError && /network failure/.test(err.message)
        );
        assert.equal(calls, 2);
    });

    test('an already aborted deadline stops the call before it is sent', async () => {
        const controller = new AbortController();
        controller.abort();
        let called = false;
        const fetchImpl = async () => {
            called = true;
            return { ok: true, status: 200, text: async () => '{}' };
        };
        await assert.rejects(
            () => chatCompletion({ ...API, messages: MESSAGES, fetchImpl, signal: controller.signal }),
            (err) => /deadline reached/.test(err.message)
        );
        assert.equal(called, false);
    });
});

describe('listModels', () => {
    test('returns the model identifiers the account can call', async () => {
        const fetchImpl = scriptedFetch([
            { status: 200, body: { object: 'list', data: [{ id: 'deepseek-v4-flash' }, { id: 'deepseek-v4-pro' }] } }
        ]);
        const ids = await listModels({ apiKey: API.apiKey, baseUrl: API.baseUrl, fetchImpl });
        assert.deepEqual(ids, ['deepseek-v4-flash', 'deepseek-v4-pro']);
        assert.equal(fetchImpl.calls[0].url, 'https://api.deepseek.com/models');
        assert.equal(fetchImpl.calls[0].headers.authorization, 'Bearer sk-test-SENTINEL-KEY');
    });

    test('surfaces an authentication failure without echoing the key', async () => {
        const fetchImpl = scriptedFetch([{ status: 401, body: { error: { message: 'Authentication Fails' } } }]);
        await assert.rejects(
            () => listModels({ apiKey: API.apiKey, baseUrl: API.baseUrl, fetchImpl }),
            (err) => err.status === 401 && !err.message.includes('SENTINEL')
        );
    });
});

describe('readUsage', () => {
    test('normalizes the OpenAI fields plus the DeepSeek cache counters', () => {
        const usage = readUsage({
            usage: {
                prompt_tokens: 100,
                completion_tokens: 20,
                total_tokens: 120,
                prompt_cache_hit_tokens: 64,
                prompt_cache_miss_tokens: 36
            }
        });
        assert.deepEqual(usage, {
            promptTokens: 100,
            completionTokens: 20,
            totalTokens: 120,
            promptCacheHitTokens: 64,
            promptCacheMissTokens: 36
        });
    });

    test('returns null when the response carries no usage block', () => {
        assert.equal(readUsage({}), null);
    });
});

describe('retry backoff', () => {
    test('the jitter is added on top of the exponential delay', async () => {
        let calls = 0;
        const fetchImpl = async () => {
            calls += 1;
            if (calls <= 2) return new Response('{"error":{"message":"busy"}}', { status: 503 });
            return new Response(JSON.stringify({ choices: [{ message: { content: 'ok' } }] }), { status: 200 });
        };
        const sleepImpl = recordingSleep();

        await chatCompletion({
            ...API, messages: MESSAGES, fetchImpl, sleepImpl, maxRetries: 2, jitterImpl: () => 137
        });

        // Without the jitter these would be exactly 1000 and 2000: the offset is real,
        // and it is the same offset the injected function returned.
        assert.deepEqual(sleepImpl.waited, [1137, 2137]);
    });
});
