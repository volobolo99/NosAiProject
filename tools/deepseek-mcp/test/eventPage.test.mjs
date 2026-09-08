import assert from 'node:assert/strict';
import { test, describe } from 'node:test';

import { renderPage, sinceLabel, statusOf } from '../src/eventPage.mjs';

function pageOf(overrides = {}) {
    return renderPage({
        delegations: [],
        summary: { total: 0, open: 0, quiet: 0, files: 0, tokens: 0 },
        activity: [],
        activityFile: 'C:\\repo\\logact.md',
        logFile: 'C:\\repo\\tools\\deepseek-mcp\\logs\\delegations.jsonl',
        live: true,
        generatedAt: '2026-09-08T11:00:00.000Z',
        ...overrides
    });
}

describe('the live page', () => {
    test('is one self-contained document with no external resource', () => {
        const html = pageOf();
        assert.match(html, /^<!doctype html>/);
        assert.ok(!/src="https?:/.test(html), 'no script is fetched from anywhere');
        assert.ok(!/href="https?:/.test(html), 'no stylesheet or font is fetched from anywhere');
        assert.match(html, /<script id="data" type="application\/json">/);
    });

    test('carries its data as inert JSON, not as markup', () => {
        const html = pageOf({
            delegations: [
                {
                    id: 'd1',
                    model: 'deepseek-v4-pro',
                    open: true,
                    quiet: false,
                    events: [],
                    fileChanges: [],
                    rounds: 1,
                    toolCalls: 0,
                    totalTokens: 10,
                    allowedPaths: []
                }
            ]
        });
        const json = html.split('<script id="data" type="application/json">')[1].split('</script>')[0];
        const data = JSON.parse(json.replace(/\\u003c/g, '<'));
        assert.equal(data.delegations[0].model, 'deepseek-v4-pro');
        assert.equal(data.live, true);
    });

    test('a reasoning block that contains markup cannot close the data tag', () => {
        const hostile = '</script><img src=x onerror=alert(1)>';
        const html = pageOf({
            delegations: [
                {
                    id: 'd1',
                    model: 'x',
                    open: false,
                    quiet: false,
                    allowedPaths: [],
                    fileChanges: [],
                    events: [
                        {
                            ts: '2026-09-08T11:00:00.000Z',
                            id: 'd1',
                            ev: 'assistant_message',
                            round: 1,
                            reasoning: { text: hostile, chars: hostile.length, truncated: false }
                        }
                    ]
                }
            ]
        });
        const dataBlock = html.split('<script id="data" type="application/json">')[1];
        assert.ok(!dataBlock.startsWith('</script>'), 'the payload never terminates its own tag');
        assert.ok(!html.includes('<img src=x'), 'the hostile markup is not present as markup');
        assert.ok(html.includes('\\u003c/script>'), 'every < in the payload is escaped');
    });

    test('a snapshot says it is a snapshot', () => {
        const live = pageOf({ live: true });
        const still = pageOf({ live: false });
        assert.match(live, /"live":true/);
        assert.match(still, /"live":false/);
    });
});

describe('lo stato di una delega', () => {
    test("una delega senza evento di chiusura e' aperta, mai fallita", () => {
        const st = statusOf({ open: true, quiet: false });
        assert.equal(st.cls, 'open');
        assert.equal(st.label, 'in corso');
        assert.notEqual(st.cls, 'failed', 'senza status la delega aperta non e mai fallita');
    });

    test("una delega silenziosa dice da quanto tace, e resta aperta", () => {
        const st = statusOf({ open: true, quiet: true, sinceMs: 90 * 60000 });
        assert.equal(st.cls, 'open');
        assert.ok(st.label.includes('ferma da 1 h 30 min'));
    });

    test("una delega rifiutata appare come fallita", () => {
        const refused = statusOf({ open: false, status: 'refused' });
        assert.equal(refused.cls, 'failed');
        assert.equal(refused.label, 'refused');
        const done = statusOf({ open: false, status: 'completed' });
        assert.equal(done.cls, 'closed');
        assert.equal(done.label, 'completata');
    });

    test("l'attesa e' scritta nell'unita' che un lettore usa", () => {
        assert.equal(sinceLabel(45000), '45 s');
        assert.equal(sinceLabel(300000), '5 min');
        assert.equal(sinceLabel(3600000), '60 min');
        assert.equal(sinceLabel(5400000), '1 h 30 min');
        assert.equal(sinceLabel(null), '?');
    });

    test('un registro vuoto produce una pagina che lo dice', () => {
        const html = pageOf();
        assert.ok(html.includes('Nessuna delega registrata'));
        const json = html.split('<script id="data" type="application/json">')[1].split('</script>')[0];
        const data = JSON.parse(json.replace(/\\u003c/g, '<'));
        assert.equal(data.delegations.length, 0);
    });

    test('la pagina spedisce la logica esportata, non una copia', () => {
        const html = pageOf();
        assert.ok(html.includes('function statusOf'));
        assert.ok(html.includes('function sinceLabel'));
        assert.equal(html.split('function statusOf').length - 1, 1);
    });
});
