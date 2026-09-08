/**
 * Path confinement for the delegated worker.
 *
 * Every path the worker names is resolved to a real filesystem location and
 * must land inside the working directory. Symlinks, junctions and `..` are
 * resolved before the containment check, not after, so a link that points
 * outside the root is rejected even when its lexical path looks contained.
 */

import fs from 'node:fs';
import path from 'node:path';

import { ALWAYS_DENIED } from './config.mjs';

const CASE_INSENSITIVE = process.platform === 'win32';

export class SandboxError extends Error {
    constructor(message, code = 'OUT_OF_SCOPE') {
        super(message);
        this.name = 'SandboxError';
        this.code = code;
    }
}

/** Translates a glob (`**`, `*`, `?`) into an anchored regular expression. */
export function globToRegExp(pattern) {
    const normalized = pattern.replace(/\\/g, '/').replace(/^\.\//, '');
    let out = '';
    for (let i = 0; i < normalized.length; i += 1) {
        const c = normalized[i];
        if (c === '*') {
            if (normalized[i + 1] === '*') {
                // `**/` spans zero or more directories; a trailing `**` spans the rest.
                if (normalized[i + 2] === '/') {
                    out += '(?:[^/]+/)*';
                    i += 2;
                } else {
                    out += '.*';
                    i += 1;
                }
            } else {
                out += '[^/]*';
            }
        } else if (c === '?') {
            out += '[^/]';
        } else if ('\\^$.|+()[]{}'.includes(c)) {
            out += '\\' + c;
        } else {
            out += c;
        }
    }
    return new RegExp('^' + out + '$', CASE_INSENSITIVE ? 'i' : '');
}

function matchesAny(relPosix, patterns) {
    return patterns.some((p) => globToRegExp(p).test(relPosix));
}

function sameOrInside(child, parent) {
    const rel = path.relative(parent, child);
    if (rel === '') return true;
    if (path.isAbsolute(rel)) return false;
    return rel.split(path.sep)[0] !== '..';
}

function normalizeForCompare(p) {
    return CASE_INSENSITIVE ? p.toLowerCase() : p;
}

/** Resolves the deepest existing ancestor of `abs` through symlinks. */
function realExistingAncestor(abs) {
    let current = abs;
    const missing = [];
    for (;;) {
        try {
            return { real: fs.realpathSync.native(current), missing };
        } catch (err) {
            if (err.code !== 'ENOENT') throw err;
            const parent = path.dirname(current);
            if (parent === current) {
                throw new SandboxError('no existing ancestor for ' + abs, 'NOT_FOUND');
            }
            missing.unshift(path.basename(current));
            current = parent;
        }
    }
}

/**
 * Creates a sandbox rooted at `root`.
 *
 * @param {object} options
 * @param {string} options.root working directory; must already exist
 * @param {string[]} options.allowedPaths globs, relative to the root, that scope every access
 * @param {boolean} [options.readOnly] when true, every write is refused
 */
export function createSandbox({ root, allowedPaths, readOnly = false }) {
    if (typeof root !== 'string' || root.trim() === '') {
        throw new SandboxError('workingDirectory is required', 'INVALID_ROOT');
    }
    let realRoot;
    try {
        realRoot = fs.realpathSync.native(path.resolve(root));
    } catch (err) {
        throw new SandboxError('workingDirectory does not exist: ' + root + ' (' + err.code + ')', 'INVALID_ROOT');
    }
    if (!fs.statSync(realRoot).isDirectory()) {
        throw new SandboxError('workingDirectory is not a directory: ' + root, 'INVALID_ROOT');
    }
    if (!Array.isArray(allowedPaths) || allowedPaths.length === 0) {
        throw new SandboxError('allowedPaths must list at least one glob', 'INVALID_SCOPE');
    }
    for (const p of allowedPaths) {
        if (typeof p !== 'string' || p.trim() === '') {
            throw new SandboxError('allowedPaths entries must be non-empty strings', 'INVALID_SCOPE');
        }
        if (path.isAbsolute(p) || p.replace(/\\/g, '/').startsWith('../')) {
            throw new SandboxError(
                'allowedPaths entries must be relative to the working directory: ' + p,
                'INVALID_SCOPE'
            );
        }
    }

    /** Relative POSIX path of `abs` inside the root; throws when outside. */
    function relOf(abs) {
        const rel = path.relative(realRoot, abs);
        if (rel === '' || path.isAbsolute(rel) || rel.split(path.sep)[0] === '..') {
            throw new SandboxError('path escapes the working directory: ' + abs, 'OUT_OF_SCOPE');
        }
        return rel.split(path.sep).join('/');
    }

    function resolveCandidate(input) {
        if (typeof input !== 'string' || input.trim() === '') {
            throw new SandboxError('path is required', 'INVALID_PATH');
        }
        if (input.includes('\0')) {
            throw new SandboxError('path contains a NUL byte', 'INVALID_PATH');
        }
        const abs = path.isAbsolute(input) ? path.resolve(input) : path.resolve(realRoot, input);
        // Lexical containment first: cheap, and it rejects the obvious escapes.
        if (!sameOrInside(normalizeForCompare(abs), normalizeForCompare(realRoot))) {
            throw new SandboxError('path is outside the working directory: ' + input, 'OUT_OF_SCOPE');
        }
        return abs;
    }

    /** Resolves through links and re-checks containment on the real location. */
    function resolveReal(input, { mustExist }) {
        const abs = resolveCandidate(input);
        const { real, missing } = realExistingAncestor(abs);
        if (mustExist && missing.length > 0) {
            throw new SandboxError('file does not exist: ' + input, 'NOT_FOUND');
        }
        if (!sameOrInside(normalizeForCompare(real), normalizeForCompare(realRoot))) {
            throw new SandboxError(
                'path resolves outside the working directory through a link: ' + input,
                'OUT_OF_SCOPE'
            );
        }
        const resolved = missing.length > 0 ? path.join(real, ...missing) : real;
        if (!sameOrInside(normalizeForCompare(resolved), normalizeForCompare(realRoot))) {
            throw new SandboxError('path escapes the working directory: ' + input, 'OUT_OF_SCOPE');
        }
        return { abs: resolved, rel: relOf(resolved), exists: missing.length === 0 };
    }

    function assertScope(rel, mode) {
        if (matchesAny(rel, ALWAYS_DENIED)) {
            throw new SandboxError('path is permanently denied to the worker: ' + rel, 'DENIED');
        }
        if (!matchesAny(rel, allowedPaths)) {
            throw new SandboxError(
                'path is outside allowedPaths (' + allowedPaths.join(', ') + '): ' + rel,
                'OUT_OF_SCOPE'
            );
        }
        if (mode === 'write' && readOnly) {
            throw new SandboxError('delegation is read-only, cannot write: ' + rel, 'READ_ONLY');
        }
    }

    return {
        root: realRoot,
        allowedPaths: [...allowedPaths],
        readOnly,

        /** Resolves a path for reading; the file must exist and be in scope. */
        forRead(input) {
            const r = resolveReal(input, { mustExist: true });
            assertScope(r.rel, 'read');
            return r;
        },

        /** Resolves a path for writing; the file may be new but must be in scope. */
        forWrite(input) {
            const r = resolveReal(input, { mustExist: false });
            assertScope(r.rel, 'write');
            return r;
        },

        /** Resolves a directory for listing; stays inside the root but ignores allowedPaths. */
        forListing(input) {
            const target = input === undefined || input === null || input === '' ? '.' : input;
            const abs = resolveCandidate(target);
            const { real, missing } = realExistingAncestor(abs);
            if (missing.length > 0) {
                throw new SandboxError('directory does not exist: ' + target, 'NOT_FOUND');
            }
            if (!sameOrInside(normalizeForCompare(real), normalizeForCompare(realRoot))) {
                throw new SandboxError(
                    'directory resolves outside the working directory: ' + target,
                    'OUT_OF_SCOPE'
                );
            }
            const rel = real === realRoot ? '.' : relOf(real);
            if (rel !== '.' && matchesAny(rel, ALWAYS_DENIED)) {
                throw new SandboxError('directory is permanently denied: ' + rel, 'DENIED');
            }
            return { abs: real, rel };
        },

        /** True when a relative POSIX path is inside the declared scope. */
        inScope(rel) {
            return !matchesAny(rel, ALWAYS_DENIED) && matchesAny(rel, allowedPaths);
        },

        /** True when a relative POSIX path is permanently denied. */
        isDenied(rel) {
            return matchesAny(rel, ALWAYS_DENIED);
        },

        relOf
    };
}
