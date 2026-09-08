import assert from 'node:assert/strict';
import { test, describe } from 'node:test';

import {
    BUDGET,
    ConfigError,
    DEFAULT_MODEL,
    SUPPORTED_MODELS,
    clamp,
    readConfig,
    resolveBudget,
    resolveModel
} from '../src/config.mjs';

describe('model selection', () => {
    test('accepts exactly the two v4 chat models', () => {
        assert.deepEqual(SUPPORTED_MODELS, ['deepseek-v4-flash', 'deepseek-v4-pro']);
    });

    test('falls back to flash when DEEPSEEK_MODEL is unset', () => {
        assert.equal(DEFAULT_MODEL, 'deepseek-v4-flash');
        assert.deepEqual(resolveModel(undefined, {}), { model: 'deepseek-v4-flash', source: 'default' });
    });

    test('an empty or blank DEEPSEEK_MODEL is treated as unset', () => {
        assert.equal(resolveModel(undefined, { DEEPSEEK_MODEL: '' }).model, 'deepseek-v4-flash');
        assert.equal(resolveModel(undefined, { DEEPSEEK_MODEL: '   ' }).source, 'default');
    });

    test('DEEPSEEK_MODEL selects flash', () => {
        assert.deepEqual(resolveModel(undefined, { DEEPSEEK_MODEL: 'deepseek-v4-flash' }), {
            model: 'deepseek-v4-flash',
            source: 'DEEPSEEK_MODEL'
        });
    });

    test('DEEPSEEK_MODEL selects pro', () => {
        assert.deepEqual(resolveModel(undefined, { DEEPSEEK_MODEL: 'deepseek-v4-pro' }), {
            model: 'deepseek-v4-pro',
            source: 'DEEPSEEK_MODEL'
        });
    });

    test('surrounding whitespace in DEEPSEEK_MODEL is tolerated', () => {
        assert.equal(resolveModel(undefined, { DEEPSEEK_MODEL: ' deepseek-v4-pro ' }).model, 'deepseek-v4-pro');
    });

    test('an explicit argument overrides the environment', () => {
        const r = resolveModel('deepseek-v4-pro', { DEEPSEEK_MODEL: 'deepseek-v4-flash' });
        assert.deepEqual(r, { model: 'deepseek-v4-pro', source: 'argument' });
    });

    test('an unsupported DEEPSEEK_MODEL is an error, never a silent substitution', () => {
        assert.throws(
            () => resolveModel(undefined, { DEEPSEEK_MODEL: 'deepseek-v3' }),
            (e) => e instanceof ConfigError && /No substitution is made/.test(e.message)
        );
    });

    test('the vision model is not accepted by this file-editing worker', () => {
        assert.throws(
            () => resolveModel(undefined, { DEEPSEEK_MODEL: 'deepseek-v4-flash-vision-exp' }),
            (e) => e instanceof ConfigError
        );
    });

    test('an unsupported explicit argument is an error too', () => {
        assert.throws(
            () => resolveModel('gpt-4o', {}),
            (e) => e instanceof ConfigError && /Accepted: deepseek-v4-flash, deepseek-v4-pro/.test(e.message)
        );
    });
});

describe('readConfig', () => {
    test('fails with an actionable message when the key is missing', () => {
        assert.throws(
            () => readConfig({}),
            (e) => e instanceof ConfigError && /DEEPSEEK_API_KEY is not set/.test(e.message)
        );
    });

    test('defaults the base url and the model', () => {
        const cfg = readConfig({ DEEPSEEK_API_KEY: 'sk-x' });
        assert.equal(cfg.baseUrl, 'https://api.deepseek.com');
        assert.equal(cfg.model, 'deepseek-v4-flash');
        assert.equal(cfg.modelSource, 'default');
    });

    test('strips a trailing slash from DEEPSEEK_BASE_URL', () => {
        const cfg = readConfig({ DEEPSEEK_API_KEY: 'sk-x', DEEPSEEK_BASE_URL: 'https://api.deepseek.com/' });
        assert.equal(cfg.baseUrl, 'https://api.deepseek.com');
    });

    test('carries the model source so the report can say where it came from', () => {
        const cfg = readConfig({ DEEPSEEK_API_KEY: 'sk-x', DEEPSEEK_MODEL: 'deepseek-v4-pro' });
        assert.equal(cfg.model, 'deepseek-v4-pro');
        assert.equal(cfg.modelSource, 'DEEPSEEK_MODEL');
    });
});

describe('budget clamping', () => {
    test('unset values take the default', () => {
        const b = resolveBudget({});
        assert.equal(b.maxApiRounds, BUDGET.maxApiRounds.def);
        assert.equal(b.maxToolCalls, BUDGET.maxToolCalls.def);
        assert.equal(b.timeoutSeconds, BUDGET.timeoutSeconds.def);
        assert.equal(b.maxRetries, BUDGET.maxRetries.def);
    });

    test('over-large requests are clamped to the hard cap', () => {
        const b = resolveBudget({ maxApiRounds: 100000, maxToolCalls: 99999, timeoutSeconds: 86400, maxRetries: 50 });
        assert.equal(b.maxApiRounds, BUDGET.maxApiRounds.max);
        assert.equal(b.maxToolCalls, BUDGET.maxToolCalls.max);
        assert.equal(b.timeoutSeconds, BUDGET.timeoutSeconds.max);
        assert.equal(b.maxRetries, BUDGET.maxRetries.max);
    });

    test('zero and negative requests are lifted to the floor', () => {
        const b = resolveBudget({ maxApiRounds: 0, maxToolCalls: -5, timeoutSeconds: 1 });
        assert.equal(b.maxApiRounds, BUDGET.maxApiRounds.min);
        assert.equal(b.maxToolCalls, BUDGET.maxToolCalls.min);
        assert.equal(b.timeoutSeconds, BUDGET.timeoutSeconds.min);
    });

    test('nonsense values fall back to the default rather than NaN', () => {
        assert.equal(clamp('maxApiRounds', 'many'), BUDGET.maxApiRounds.def);
        assert.equal(clamp('maxToolCalls', null), BUDGET.maxToolCalls.def);
    });

    test('an unknown budget key is a programming error', () => {
        assert.throws(() => clamp('maxSpendEuro', 5), ConfigError);
    });
});

describe('the two timeouts stay in proportion', () => {
    test('the default per-request ceiling leaves room for a second full attempt', () => {
        // A per-request ceiling that eats the whole wall-clock budget turns one
        // stalled call into the end of the delegation, with no retry left.
        assert.ok(
            BUDGET.requestTimeoutSeconds.def * 2 <= BUDGET.timeoutSeconds.def,
            'requestTimeoutSeconds.def must fit twice inside timeoutSeconds.def'
        );
    });

    test('a per-request ceiling can never exceed the overall one', () => {
        assert.ok(BUDGET.requestTimeoutSeconds.max <= BUDGET.timeoutSeconds.max);
    });
});
