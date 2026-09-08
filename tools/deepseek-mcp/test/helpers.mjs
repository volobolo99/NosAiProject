/** Shared fixtures: a throwaway working directory and a scriptable fake API. */

import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

/** Creates an isolated temp tree and returns its real path plus a cleanup hook. */
export function makeTempRoot(prefix = 'nosai-ds-') {
    const dir = fs.realpathSync.native(fs.mkdtempSync(path.join(os.tmpdir(), prefix)));
    return {
        dir,
        write(rel, content) {
            const abs = path.join(dir, ...rel.split('/'));
            fs.mkdirSync(path.dirname(abs), { recursive: true });
            fs.writeFileSync(abs, content, 'utf8');
            return abs;
        },
        read(rel) {
            return fs.readFileSync(path.join(dir, ...rel.split('/')), 'utf8');
        },
        exists(rel) {
            return fs.existsSync(path.join(dir, ...rel.split('/')));
        },
        cleanup() {
            fs.rmSync(dir, { recursive: true, force: true });
        }
    };
}

/** Builds an assistant message that asks for the given tool calls. */
export function toolCallResponse(calls, { usage, content = '' } = {}) {
    return {
        id: 'cmpl-test',
        model: 'deepseek-v4-flash',
        choices: [
            {
                index: 0,
                finish_reason: 'tool_calls',
                message: {
                    role: 'assistant',
                    content,
                    tool_calls: calls.map((c, i) => ({
                        id: c.id ?? 'call_' + i,
                        type: 'function',
                        function: { name: c.name, arguments: JSON.stringify(c.args ?? {}) }
                    }))
                }
            }
        ],
        usage: usage ?? { prompt_tokens: 10, completion_tokens: 5, total_tokens: 15 }
    };
}

/** Builds a plain text assistant message with no tool calls. */
export function textResponse(text, { usage } = {}) {
    return {
        id: 'cmpl-test',
        model: 'deepseek-v4-flash',
        choices: [{ index: 0, finish_reason: 'stop', message: { role: 'assistant', content: text } }],
        usage: usage ?? { prompt_tokens: 4, completion_tokens: 2, total_tokens: 6 }
    };
}

/**
 * A fetch stand-in driven by a queue of scripted turns.
 * Each entry is either a JSON body (HTTP 200), or { status, body } for failures.
 */
export function scriptedFetch(turns) {
    const calls = [];
    let i = 0;
    const impl = async (url, init) => {
        const body = init?.body ? JSON.parse(init.body) : undefined;
        calls.push({ url, headers: init?.headers ?? {}, body, method: init?.method ?? 'GET' });
        if (i >= turns.length) {
            throw new Error('scriptedFetch ran out of turns after ' + i + ' calls');
        }
        const turn = turns[i];
        i += 1;
        const status = turn.status ?? 200;
        const payload = turn.status ? turn.body : turn;
        const text = typeof payload === 'string' ? payload : JSON.stringify(payload ?? {});
        return {
            ok: status >= 200 && status < 300,
            status,
            async text() {
                return text;
            }
        };
    };
    impl.calls = calls;
    impl.remaining = () => turns.length - i;
    return impl;
}

/** Records sleeps instead of performing them, so retry tests stay instant. */
export function recordingSleep() {
    const waited = [];
    const impl = async (ms) => {
        waited.push(ms);
    };
    impl.waited = waited;
    return impl;
}
