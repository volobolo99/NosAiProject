import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { test, describe, before, after } from 'node:test';

import { createSandbox, globToRegExp, SandboxError } from '../src/sandbox.mjs';
import { makeTempRoot } from './helpers.mjs';

describe('globToRegExp', () => {
    test('* stays inside one segment', () => {
        assert.ok(globToRegExp('src/*.cs').test('src/A.cs'));
        assert.ok(!globToRegExp('src/*.cs').test('src/sub/A.cs'));
    });

    test('**/ spans zero or more directories', () => {
        const re = globToRegExp('src/**/*.cs');
        assert.ok(re.test('src/A.cs'));
        assert.ok(re.test('src/a/b/C.cs'));
        assert.ok(!re.test('tests/A.cs'));
    });

    test('trailing ** spans the rest of the path', () => {
        const re = globToRegExp('src/**');
        assert.ok(re.test('src/a/b/c.txt'));
        assert.ok(!re.test('srcx/a.txt'));
    });

    test('dots are literal, not wildcards', () => {
        assert.ok(!globToRegExp('a.cs').test('axcs'));
    });
});

describe('createSandbox scope validation', () => {
    let root;
    before(() => {
        root = makeTempRoot();
        root.write('src/app.txt', 'hello');
        root.write('secrets/keys.txt', 'nope');
    });
    after(() => root.cleanup());

    test('rejects a missing working directory', () => {
        assert.throws(
            () => createSandbox({ root: path.join(root.dir, 'nope-does-not-exist'), allowedPaths: ['**'] }),
            (e) => e instanceof SandboxError && e.code === 'INVALID_ROOT'
        );
    });

    test('rejects an empty allowedPaths list', () => {
        assert.throws(
            () => createSandbox({ root: root.dir, allowedPaths: [] }),
            (e) => e.code === 'INVALID_SCOPE'
        );
    });

    test('rejects absolute or parent-relative allowedPaths entries', () => {
        assert.throws(
            () => createSandbox({ root: root.dir, allowedPaths: ['../outside/**'] }),
            (e) => e.code === 'INVALID_SCOPE'
        );
        assert.throws(
            () => createSandbox({ root: root.dir, allowedPaths: [path.join(root.dir, 'src') + '/**'] }),
            (e) => e.code === 'INVALID_SCOPE'
        );
    });
});

describe('path confinement', () => {
    let root;
    let sandbox;
    before(() => {
        root = makeTempRoot();
        root.write('src/app.txt', 'hello');
        root.write('src/nested/deep.txt', 'deep');
        root.write('secrets/keys.txt', 'nope');
        root.write('.git/config', 'gitdata');
        sandbox = createSandbox({ root: root.dir, allowedPaths: ['src/**'] });
    });
    after(() => root.cleanup());

    test('accepts a file inside the scope', () => {
        const r = sandbox.forRead('src/app.txt');
        assert.equal(r.rel, 'src/app.txt');
        assert.equal(r.exists, true);
        assert.equal(fs.readFileSync(r.abs, 'utf8'), 'hello');
    });

    test('accepts a nested file and normalizes backslashes', () => {
        assert.equal(sandbox.forRead('src\\nested\\deep.txt').rel, 'src/nested/deep.txt');
    });

    test('refuses a parent-relative escape', () => {
        assert.throws(
            () => sandbox.forRead('../outside.txt'),
            (e) => e.code === 'OUT_OF_SCOPE'
        );
        assert.throws(
            () => sandbox.forRead('src/../../outside.txt'),
            (e) => e.code === 'OUT_OF_SCOPE'
        );
    });

    test('refuses an absolute path outside the root', () => {
        const outside = path.join(path.dirname(root.dir), 'elsewhere.txt');
        assert.throws(
            () => sandbox.forWrite(outside),
            (e) => e.code === 'OUT_OF_SCOPE'
        );
    });

    test('refuses a path inside the root but outside allowedPaths', () => {
        assert.throws(
            () => sandbox.forRead('secrets/keys.txt'),
            (e) => e.code === 'OUT_OF_SCOPE'
        );
    });

    test('refuses .git even when allowedPaths would admit it', () => {
        const wide = createSandbox({ root: root.dir, allowedPaths: ['**'] });
        assert.throws(
            () => wide.forRead('.git/config'),
            (e) => e.code === 'DENIED'
        );
        assert.throws(
            () => wide.forWrite('.git/hooks/pre-commit'),
            (e) => e.code === 'DENIED'
        );
    });

    test('refuses a NUL byte in the path', () => {
        assert.throws(
            () => sandbox.forRead('src/a\0b.txt'),
            (e) => e.code === 'INVALID_PATH'
        );
    });

    test('reports a missing file as NOT_FOUND on read, but allows it on write', () => {
        assert.throws(
            () => sandbox.forRead('src/new.txt'),
            (e) => e.code === 'NOT_FOUND'
        );
        const w = sandbox.forWrite('src/new.txt');
        assert.equal(w.exists, false);
        assert.equal(w.rel, 'src/new.txt');
    });

    test('read-only sandboxes refuse writes but allow reads', () => {
        const ro = createSandbox({ root: root.dir, allowedPaths: ['src/**'], readOnly: true });
        assert.equal(ro.forRead('src/app.txt').rel, 'src/app.txt');
        assert.throws(
            () => ro.forWrite('src/app.txt'),
            (e) => e.code === 'READ_ONLY'
        );
    });
});

describe('link traversal', () => {
    let root;
    let outsideDir;
    let linkCreated = false;
    let skipReason = '';

    before(() => {
        root = makeTempRoot();
        root.write('src/app.txt', 'hello');
        outsideDir = makeTempRoot('nosai-out-');
        outsideDir.write('loot.txt', 'secret');
        try {
            // A directory junction needs no elevation on Windows; on POSIX a dir symlink does the same job.
            fs.symlinkSync(outsideDir.dir, path.join(root.dir, 'src', 'escape'), 'junction');
            linkCreated = true;
        } catch (err) {
            skipReason = 'cannot create a directory link here: ' + err.code;
        }
    });
    after(() => {
        root.cleanup();
        outsideDir.cleanup();
    });

    test('a link pointing outside the root is refused for read', (t) => {
        if (!linkCreated) return t.skip(skipReason);
        const sandbox = createSandbox({ root: root.dir, allowedPaths: ['src/**'] });
        assert.throws(
            () => sandbox.forRead('src/escape/loot.txt'),
            (e) => e.code === 'OUT_OF_SCOPE' && /through a link|outside/.test(e.message)
        );
    });

    test('a link pointing outside the root is refused for write', (t) => {
        if (!linkCreated) return t.skip(skipReason);
        const sandbox = createSandbox({ root: root.dir, allowedPaths: ['src/**'] });
        assert.throws(
            () => sandbox.forWrite('src/escape/planted.txt'),
            (e) => e.code === 'OUT_OF_SCOPE'
        );
        assert.equal(fs.existsSync(path.join(outsideDir.dir, 'planted.txt')), false);
    });

    test('a link pointing outside the root is refused for listing', (t) => {
        if (!linkCreated) return t.skip(skipReason);
        const sandbox = createSandbox({ root: root.dir, allowedPaths: ['src/**'] });
        assert.throws(
            () => sandbox.forListing('src/escape'),
            (e) => e.code === 'OUT_OF_SCOPE'
        );
    });
});
