import assert from 'node:assert/strict';
import { test, describe } from 'node:test';

import { clockOf, formatEvent, parseLine } from '../src/eventView.mjs';

const START = {
    ts: '2026-09-08T06:31:02.000Z',
    id: 'd20260908063102-a1b2',
    ev: 'delegation_start',
    model: 'deepseek-v4-pro',
    modelSource: 'DEEPSEEK_MODEL',
    workingDirectory: 'C:\\Users\\volob\\Desktop\\NosAiProject',
    allowedPaths: ['src/NosAi.Runtime/Tactical/**'],
    readOnly: false,
    budget: { maxApiRounds: 24, maxToolCalls: 60, timeoutSeconds: 600 },
    taskChars: 1843,
    acceptanceCriteria: 3,
    task: 'Scrivi UnequipExecutor sul modello di ClickTargetExecutor.'
};

describe('the operator view of one event', () => {
    test('a delegation opens with model, scope, ceilings and the assignment quoted', () => {
        const out = formatEvent(START);
        assert.match(out, /DELEGA d20260908063102-a1b2/);
        assert.match(out, /modello deepseek-v4-pro \(da DEEPSEEK_MODEL\)/);
        assert.match(out, /perimetro src\/NosAi\.Runtime\/Tactical\/\*\*/);
        assert.match(out, /tetti 24 giri, 60 strumenti, 600 s/);
        assert.match(out, /\| Scrivi UnequipExecutor/, 'the assignment is quoted, not merged into the event line');
    });

    test('reading and saying are two different blocks, and truncation is declared', () => {
        const out = formatEvent({
            ts: START.ts,
            id: START.id,
            ev: 'assistant_message',
            round: 2,
            reasoning: { text: 'Prima leggo il file,\npoi decido.', chars: 5000, truncated: true },
            text: { text: 'Leggo i due file.', chars: 17, truncated: false }
        });
        assert.match(out, /giro 2 — RAGIONA \(5\.000 caratteri, troncato\)/);
        assert.match(out, /\| Prima leggo il file,\n\s+\| poi decido\./, 'line breaks in the reasoning survive');
        assert.match(out, /giro 2 — DICE \(17 caratteri\)/);
    });

    test('a refused tool call says so, with the reason', () => {
        const out = formatEvent({
            ts: START.ts,
            id: START.id,
            ev: 'tool_call',
            round: 1,
            name: 'write_file',
            path: '../fuori.txt',
            ok: false,
            reason: 'ERROR [OUT_OF_SCOPE] path is outside allowedPaths'
        });
        assert.match(out, /USA write_file {2}\.\.\/fuori\.txt {3}RIFIUTATO — ERROR \[OUT_OF_SCOPE\]/);
    });

    test('a successful tool call carries no verdict beyond the file it touched', () => {
        const out = formatEvent({ ts: START.ts, id: START.id, ev: 'tool_call', round: 1, name: 'read_file', path: 'src/a.cs', ok: true });
        assert.match(out, /USA read_file {2}src\/a\.cs$/);
        assert.ok(!out.includes('RIFIUTATO'));
    });

    test('the closing line reports status, ceilings consumed and tokens', () => {
        const out = formatEvent({
            ts: START.ts,
            id: START.id,
            ev: 'delegation_end',
            status: 'completed',
            rounds: 7,
            toolCalls: 12,
            refusedCalls: 1,
            filesChanged: 3,
            durationMs: 154000,
            usage: { totalTokens: 48213 }
        });
        assert.match(out, /FINE completed {3}7 giri, 12 strumenti \(1 rifiutati\), 3 file, 154\.0 s, token 48\.213/);
    });

    test('an unknown or half-written line never stops the view', () => {
        assert.equal(formatEvent(null), null);
        assert.equal(formatEvent({ ts: START.ts, id: 'x' }), null, 'no ev, no event');
        assert.equal(parseLine('{"ev":"delegation_st'), null);
        assert.equal(parseLine('   '), null);
        assert.equal(parseLine('{"ev":"tool_call"}').ev, 'tool_call');
    });

    test('an unreadable timestamp degrades to a placeholder instead of throwing', () => {
        assert.equal(clockOf('non una data'), '--:--:--');
        assert.match(clockOf('2026-09-08T06:31:02.000Z'), /^\d{2}:\d{2}:\d{2}$/);
    });
});
