import assert from 'node:assert/strict';
import { test, describe, beforeEach, afterEach } from 'node:test';

import { createSandbox } from '../src/sandbox.mjs';
import { ChangeJournal, createWorkerTools, TOOL_DEFINITIONS } from '../src/workerTools.mjs';
import { IO_LIMITS } from '../src/config.mjs';
import { makeTempRoot } from './helpers.mjs';

function build(allowedPaths = ['src/**'], readOnly = false) {
    const root = makeTempRoot();
    root.write('src/app.txt', 'alpha\nbeta\ngamma\n');
    root.write('src/nested/deep.cs', 'public class Deep { }\n');
    root.write('secrets/keys.txt', 'do-not-touch');
    const sandbox = createSandbox({ root: root.dir, allowedPaths, readOnly });
    const journal = new ChangeJournal(sandbox);
    return { root, sandbox, journal, tools: createWorkerTools(sandbox, journal) };
}

describe('worker tool surface', () => {
    test('exposes files only: no shell, no process, no delegation', () => {
        const names = TOOL_DEFINITIONS.map((d) => d.function.name).sort();
        assert.deepEqual(names, ['edit_file', 'list_files', 'read_file', 'report_done', 'search_files', 'write_file']);
        const serialized = JSON.stringify(TOOL_DEFINITIONS).toLowerCase();
        for (const forbidden of ['shell', 'exec', 'spawn', 'bash', 'powershell', 'delegate', 'http', 'fetch']) {
            assert.ok(!serialized.includes(forbidden), 'tool surface must not mention ' + forbidden);
        }
    });

    test('every definition carries a JSON Schema object', () => {
        for (const d of TOOL_DEFINITIONS) {
            assert.equal(d.type, 'function');
            assert.equal(d.function.parameters.type, 'object');
            assert.ok(d.function.description.length > 10);
        }
    });
});

describe('read and list', () => {
    let fx;
    beforeEach(() => {
        fx = build();
    });
    afterEach(() => fx.root.cleanup());

    test('read_file returns numbered lines', () => {
        const r = fx.tools.invoke('read_file', { path: 'src/app.txt' });
        assert.ok(r.ok);
        assert.match(r.text, /1\talpha/);
        assert.match(r.text, /3\tgamma/);
    });

    test('read_file honours startLine and lineCount', () => {
        const r = fx.tools.invoke('read_file', { path: 'src/app.txt', startLine: 2, lineCount: 1 });
        assert.match(r.text, /2\tbeta/);
        assert.ok(!r.text.includes('alpha'));
    });

    test('read_file refuses a path outside allowedPaths', () => {
        const r = fx.tools.invoke('read_file', { path: 'secrets/keys.txt' });
        assert.equal(r.ok, false);
        assert.match(r.text, /ERROR \[OUT_OF_SCOPE\]/);
    });

    test('read_file refuses an escape through ..', () => {
        const r = fx.tools.invoke('read_file', { path: '../../etc/passwd' });
        assert.equal(r.ok, false);
        assert.match(r.text, /OUT_OF_SCOPE/);
    });

    test('list_files marks entries outside the scope', () => {
        const r = fx.tools.invoke('list_files', { depth: 2 });
        assert.ok(r.ok);
        assert.match(r.text, /secrets\/keys\.txt.*outside allowedPaths/);
        assert.ok(/src\/app\.txt {2}\(\d+ B\)$/m.test(r.text), 'in-scope file carries no warning');
    });

    test('search_files finds a line and reports the count', () => {
        const r = fx.tools.invoke('search_files', { pattern: 'beta' });
        assert.ok(r.ok);
        assert.match(r.text, /src\/app\.txt:2: beta/);
    });

    test('search_files rejects an invalid regular expression', () => {
        const r = fx.tools.invoke('search_files', { pattern: '([' });
        assert.equal(r.ok, false);
        assert.match(r.text, /INVALID_ARGS/);
    });

    test('search_files honours the glob filter', () => {
        const r = fx.tools.invoke('search_files', { pattern: 'class', glob: 'src/**/*.cs' });
        assert.match(r.text, /deep\.cs:1/);
    });

    test('unknown tool names come back as an error, not a throw', () => {
        const r = fx.tools.invoke('run_shell', { cmd: 'dir' });
        assert.equal(r.ok, false);
        assert.match(r.text, /unknown tool "run_shell"/);
    });
});

describe('write and edit', () => {
    let fx;
    beforeEach(() => {
        fx = build();
    });
    afterEach(() => fx.root.cleanup());

    test('write_file creates a file and the journal calls it created', () => {
        const r = fx.tools.invoke('write_file', { path: 'src/new.txt', content: 'fresh\n' });
        assert.ok(r.ok, r.text);
        assert.equal(fx.root.read('src/new.txt'), 'fresh\n');
        const changes = fx.journal.summary();
        assert.equal(changes.length, 1);
        assert.equal(changes[0].action, 'created');
        assert.equal(changes[0].bytesBefore, 0);
        assert.equal(changes[0].bytesAfter, 6);
    });

    test('rewriting identical bytes is reported as unchanged, not modified', () => {
        const r = fx.tools.invoke('write_file', { path: 'src/app.txt', content: 'alpha\nbeta\ngamma\n' });
        assert.ok(r.ok);
        const changes = fx.journal.summary();
        assert.equal(changes[0].action, 'unchanged');
        assert.equal(changes[0].sha256Before, changes[0].sha256After);
    });

    test('a real rewrite is reported as modified', () => {
        fx.tools.invoke('write_file', { path: 'src/app.txt', content: 'delta\n' });
        const changes = fx.journal.summary();
        assert.equal(changes[0].action, 'modified');
        assert.notEqual(changes[0].sha256Before, changes[0].sha256After);
    });

    test('write_file refuses a path outside allowedPaths and leaves the file alone', () => {
        const r = fx.tools.invoke('write_file', { path: 'secrets/keys.txt', content: 'pwned' });
        assert.equal(r.ok, false);
        assert.match(r.text, /OUT_OF_SCOPE/);
        assert.equal(fx.root.read('secrets/keys.txt'), 'do-not-touch');
        assert.deepEqual(fx.journal.summary(), []);
    });

    test('write_file refuses content over the size limit', () => {
        const big = 'x'.repeat(IO_LIMITS.maxWriteBytes + 1);
        const r = fx.tools.invoke('write_file', { path: 'src/big.txt', content: big });
        assert.equal(r.ok, false);
        assert.match(r.text, /TOO_LARGE/);
        assert.equal(fx.root.exists('src/big.txt'), false);
    });

    test('edit_file replaces an exact unique excerpt', () => {
        const r = fx.tools.invoke('edit_file', { path: 'src/app.txt', oldText: 'beta', newText: 'BETA' });
        assert.ok(r.ok, r.text);
        assert.equal(fx.root.read('src/app.txt'), 'alpha\nBETA\ngamma\n');
    });

    test('edit_file refuses an excerpt that does not occur', () => {
        const r = fx.tools.invoke('edit_file', { path: 'src/app.txt', oldText: 'omega', newText: 'x' });
        assert.equal(r.ok, false);
        assert.match(r.text, /NO_MATCH/);
    });

    test('edit_file refuses an ambiguous excerpt unless replaceAll is set', () => {
        fx.root.write('src/dup.txt', 'a\na\n');
        const ambiguous = fx.tools.invoke('edit_file', { path: 'src/dup.txt', oldText: 'a', newText: 'b' });
        assert.equal(ambiguous.ok, false);
        assert.match(ambiguous.text, /AMBIGUOUS/);
        const all = fx.tools.invoke('edit_file', { path: 'src/dup.txt', oldText: 'a', newText: 'b', replaceAll: true });
        assert.ok(all.ok, all.text);
        assert.equal(fx.root.read('src/dup.txt'), 'b\nb\n');
    });

    test('edit_file refuses a file that does not exist', () => {
        const r = fx.tools.invoke('edit_file', { path: 'src/ghost.txt', oldText: 'a', newText: 'b' });
        assert.equal(r.ok, false);
        assert.match(r.text, /NOT_FOUND/);
    });

    test('a read-only delegation refuses every write', () => {
        const ro = build(['src/**'], true);
        try {
            const w = ro.tools.invoke('write_file', { path: 'src/new.txt', content: 'x' });
            const e = ro.tools.invoke('edit_file', { path: 'src/app.txt', oldText: 'beta', newText: 'B' });
            assert.equal(w.ok, false);
            assert.match(w.text, /READ_ONLY/);
            assert.equal(e.ok, false);
            assert.match(e.text, /READ_ONLY/);
            assert.equal(ro.root.read('src/app.txt'), 'alpha\nbeta\ngamma\n');
            assert.ok(ro.tools.invoke('read_file', { path: 'src/app.txt' }).ok);
        } finally {
            ro.root.cleanup();
        }
    });

    test('report_done carries the worker report back', () => {
        const r = fx.tools.invoke('report_done', {
            summary: 'fatto',
            acceptanceCriteriaMet: ['criterio 1: soddisfatto'],
            blockers: []
        });
        assert.ok(r.ok);
        assert.equal(r.done.summary, 'fatto');
        assert.deepEqual(r.done.blockers, []);
    });
});

describe('a truncated result says how to continue', () => {
    test('a read past the result cap names the next line to ask for', () => {
        const { root, sandbox, journal } = build();
        // One line per row, long enough that the whole file cannot fit in a single result.
        const lines = [];
        for (let i = 1; i <= 4000; i += 1) lines.push('riga ' + i + ' ' + 'x'.repeat(40));
        root.write('src/big.txt', lines.join('\n') + '\n');
        const tools = createWorkerTools(sandbox, journal);

        const out = tools.invoke('read_file', { path: 'src/big.txt' });

        assert.ok(out.ok);
        assert.ok(out.text.length <= IO_LIMITS.maxToolResultChars + 400, 'the result stays near the cap');
        const hint = /startLine=(\d+)/.exec(out.text);
        assert.ok(hint, 'the truncation notice must name the line to continue from');
        const next = Number(hint[1]);
        assert.ok(next > 1 && next < 4000, 'the continuation line is inside the file, not invented');

        // The named line is genuinely the one after the last line shown.
        const shown = /Shown through line (\d+)/.exec(out.text);
        assert.ok(shown);
        assert.equal(next, Number(shown[1]) + 1);

        // And asking for it actually returns the rest, so the hint is not decorative.
        const rest = tools.invoke('read_file', { path: 'src/big.txt', startLine: next });
        assert.ok(rest.ok);
        assert.ok(rest.text.includes('riga ' + next + ' '));
    });
});

describe('a failed edit says where to look', () => {
    test('oldText that does not match names the lines where its first line appears', () => {
        const { root, sandbox, journal } = build();
        root.write('src/anchor.txt', 'intestazione\npublic void Calibrate(int slot)\n{\n}\n');
        const tools = createWorkerTools(sandbox, journal);

        // Right first line, wrong indentation on the second: the classic near miss.
        // The anchor is long enough to be a real one; a four-character anchor is
        // deliberately not searched for, because it would point almost anywhere.
        const out = tools.invoke('edit_file', {
            path: 'src/anchor.txt',
            oldText: 'public void Calibrate(int slot)\n    {',
            newText: 'public void Calibrate(int slot)\n{'
        });

        assert.equal(out.ok, false);
        assert.match(out.text, /NO_MATCH/);
        assert.match(out.text, /line 2\b/, 'it names the line where the first line really is');
    });

    test('oldText whose first line is nowhere says so instead of pointing at a line', () => {
        const { sandbox, journal } = build();
        const tools = createWorkerTools(sandbox, journal);

        const out = tools.invoke('edit_file', {
            path: 'src/app.txt',
            oldText: 'questa riga non esiste affatto',
            newText: 'irrilevante'
        });

        assert.equal(out.ok, false);
        assert.match(out.text, /appears nowhere in the file/);
    });
});
