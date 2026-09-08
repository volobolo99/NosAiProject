import assert from 'node:assert/strict';
import { test, describe } from 'node:test';

import {
    QUIET_AFTER_MS,
    escapeHtml,
    groupDelegations,
    summarise
} from '../src/eventReport.mjs';

const T0 = Date.parse('2026-09-08T08:00:00.000Z');

function at(offsetMs) {
    return new Date(T0 + offsetMs).toISOString();
}

function start(id, offsetMs, extra = {}) {
    return {
        ts: at(offsetMs),
        id,
        ev: 'delegation_start',
        model: 'deepseek-v4-flash',
        modelSource: 'default',
        workingDirectory: 'C:\\repo',
        allowedPaths: ['src/'],
        readOnly: false,
        task: 'incarico',
        taskChars: 8,
        budget: { maxApiRounds: 24, maxToolCalls: 60, timeoutSeconds: 600 },
        ...extra
    };
}

describe('groupDelegations', () => {
    test('events of several delegations are separated by id', () => {
        const events = [
            start('aaa', 0),
            start('bbb', 1000),
            { ts: at(2000), id: 'aaa', ev: 'tool_call', name: 'read_file', ok: true },
            { ts: at(3000), id: 'bbb', ev: 'tool_call', name: 'read_file', ok: true },
            { ts: at(4000), id: 'bbb', ev: 'tool_call', name: 'write_file', ok: false, reason: 'fuori perimetro' }
        ];

        const list = groupDelegations(events, T0 + 5000);

        assert.equal(list.length, 2);
        const byId = Object.fromEntries(list.map((d) => [d.id, d]));
        assert.equal(byId.aaa.toolCalls, 1);
        assert.equal(byId.bbb.toolCalls, 2);
        assert.equal(byId.bbb.refusedCalls, 1);
        assert.equal(byId.aaa.refusedCalls, 0);
    });

    test('the most recently started delegation comes first', () => {
        const list = groupDelegations([start('vecchia', 0), start('nuova', 60000)], T0 + 60000);
        assert.deepEqual(list.map((d) => d.id), ['nuova', 'vecchia']);
    });

    test('a delegation with no closing event is open, never failed', () => {
        const list = groupDelegations([start('aaa', 0)], T0 + 1000);
        assert.equal(list[0].open, true);
        assert.equal(list[0].status, null);
        assert.equal(list[0].error, null);
    });

    test('an open delegation silent past the threshold is quiet, and one that just spoke is not', () => {
        const events = [start('aaa', 0), { ts: at(1000), id: 'aaa', ev: 'api_request_start', round: 1 }];

        const fresh = groupDelegations(events, T0 + 1000 + QUIET_AFTER_MS - 1);
        assert.equal(fresh[0].quiet, false);

        const stale = groupDelegations(events, T0 + 1000 + QUIET_AFTER_MS + 1);
        assert.equal(stale[0].quiet, true);
        assert.equal(stale[0].open, true, 'quiet is a state of an open delegation, not an outcome');
    });

    test('a closed delegation is never called quiet however old it is', () => {
        const events = [
            start('aaa', 0),
            { ts: at(1000), id: 'aaa', ev: 'delegation_end', status: 'ok', durationMs: 1000, usage: { totalTokens: 10 } }
        ];
        const list = groupDelegations(events, T0 + 1000 + QUIET_AFTER_MS * 100);
        assert.equal(list[0].open, false);
        assert.equal(list[0].quiet, false);
    });

    test('the closing total replaces the running sum rather than adding to it', () => {
        const events = [
            start('aaa', 0),
            { ts: at(1000), id: 'aaa', ev: 'api_request_end', round: 1, usage: { totalTokens: 400 } },
            { ts: at(2000), id: 'aaa', ev: 'api_request_end', round: 2, usage: { totalTokens: 600 } },
            { ts: at(3000), id: 'aaa', ev: 'delegation_end', status: 'ok', durationMs: 3000, usage: { totalTokens: 1000 } }
        ];
        const list = groupDelegations(events, T0 + 4000);
        assert.equal(list[0].totalTokens, 1000);
        assert.equal(list[0].rounds, 2);
    });

    test('a refusal and a setup error carry their reason', () => {
        const list = groupDelegations(
            [
                { ts: at(0), id: 'aaa', ev: 'delegation_refused', reason: 'delega ricorsiva' },
                { ts: at(1000), id: 'bbb', ev: 'setup_error', error: 'chiave assente' }
            ],
            T0 + 2000
        );
        const byId = Object.fromEntries(list.map((d) => [d.id, d]));
        assert.equal(byId.aaa.status, 'refused');
        assert.equal(byId.aaa.error, 'delega ricorsiva');
        assert.equal(byId.bbb.status, 'setup_error');
        assert.equal(byId.bbb.error, 'chiave assente');
        assert.equal(byId.bbb.open, false);
    });

    test('a line without an id is skipped instead of opening a delegation of its own', () => {
        const list = groupDelegations([{ ts: at(0), ev: 'tool_call', name: 'read_file' }, start('aaa', 1000)], T0 + 2000);
        assert.deepEqual(list.map((d) => d.id), ['aaa']);
    });

    test('an open delegation gets a duration from its own events, so the page is not blank', () => {
        const events = [start('aaa', 0), { ts: at(7000), id: 'aaa', ev: 'api_request_start', round: 1 }];
        const list = groupDelegations(events, T0 + 8000);
        assert.equal(list[0].durationMs, 7000);
    });
});

describe('summarise', () => {
    test('it counts what is running, what is quiet and what was touched', () => {
        const events = [
            start('aperta', 0),
            start('ferma', 1000),
            start('chiusa', 2000),
            { ts: at(3000), id: 'aperta', ev: 'file_change', action: 'modified', path: 'a.cs', bytesBefore: 1, bytesAfter: 2 },
            { ts: at(3000), id: 'chiusa', ev: 'delegation_end', status: 'ok', durationMs: 1000, usage: { totalTokens: 500 } }
        ];
        // 'ferma' last spoke at its start; the clock is past the quiet threshold for it.
        const list = groupDelegations(events, T0 + 1000 + QUIET_AFTER_MS + 1);
        const s = summarise(list);

        assert.equal(s.total, 3);
        assert.equal(s.open, 2);
        assert.equal(s.quiet, 1);
        assert.equal(s.files, 1);
        assert.equal(s.tokens, 500);
    });
});

describe('escapeHtml', () => {
    test('every character that could close a tag or an attribute is neutralised', () => {
        assert.equal(escapeHtml('<script>'), '&lt;script&gt;');
        assert.equal(escapeHtml('a & b'), 'a &amp; b');
        assert.equal(escapeHtml('"quoted"'), '&quot;quoted&quot;');
        assert.equal(escapeHtml("it's"), 'it&#39;s');
    });

    test('the ampersand is escaped first, so an escape is never escaped twice', () => {
        assert.equal(escapeHtml('&lt;'), '&amp;lt;');
    });

    test('null and undefined become empty text, not the words', () => {
        assert.equal(escapeHtml(null), '');
        assert.equal(escapeHtml(undefined), '');
    });
});
