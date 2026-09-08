/**
 * The tool surface the delegated worker gets.
 *
 * Files only: list, search, read, write, edit, and a terminator. There is no
 * shell, no process spawn, no network and no delegation tool, so the worker
 * cannot build, test, reach outside the machine or start another delegation.
 * Builds and tests stay with Claude, which already holds those permissions.
 */

import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';

import { IO_LIMITS } from './config.mjs';
import { SandboxError, globToRegExp } from './sandbox.mjs';

/** Directories never worth walking into during a search or listing. */
const SKIP_DIRS = new Set(['.git', 'node_modules', 'bin', 'obj', '.vs', '.venv', 'TestResults']);

function sha256(buffer) {
    return crypto.createHash('sha256').update(buffer).digest('hex');
}

function looksBinary(buffer) {
    const window = buffer.subarray(0, Math.min(buffer.length, 8000));
    return window.includes(0);
}

function truncate(text, limit = IO_LIMITS.maxToolResultChars) {
    if (text.length <= limit) return text;
    // Cut on a line boundary so the hint can name a line the worker actually saw.
    // A truncation that does not say how to continue is a dead end: the worker
    // either guesses the rest or stops.
    const cut = text.slice(0, limit);
    const lastBreak = cut.lastIndexOf('\n');
    const body = lastBreak > 0 ? cut.slice(0, lastBreak) : cut;
    const lastLine = body.slice(body.lastIndexOf('\n') + 1);
    const numbered = /^\s*(\d+)\t/.exec(lastLine);
    if (!numbered) {
        return body + '\n[... truncated at ' + limit + ' characters ...]';
    }
    const next = Number(numbered[1]) + 1;
    return (
        body +
        '\n[... truncated at ' + limit + ' characters. Shown through line ' + numbered[1] +
        '. Call read_file again on the same path with startLine=' + next +
        ' to continue. Do not guess what follows. ...]'
    );
}

/**
 * Records what the delegation actually changed on disk.
 *
 * The state before the first touch is captured once per file, so the final
 * report can say `unchanged` for a file the worker rewrote with identical
 * bytes instead of claiming a modification that did not happen.
 */
export class ChangeJournal {
    constructor(sandbox) {
        this.sandbox = sandbox;
        this.before = new Map();
    }

    capture(rel, abs) {
        if (this.before.has(rel)) return;
        try {
            const buf = fs.readFileSync(abs);
            this.before.set(rel, { existed: true, hash: sha256(buf), bytes: buf.length });
        } catch (err) {
            if (err.code !== 'ENOENT') throw err;
            this.before.set(rel, { existed: false, hash: null, bytes: 0 });
        }
    }

    /** Compares the captured state against what is on disk right now. */
    summary() {
        const changes = [];
        for (const [rel, before] of this.before) {
            const abs = path.join(this.sandbox.root, ...rel.split('/'));
            let after = { existed: false, hash: null, bytes: 0 };
            try {
                const buf = fs.readFileSync(abs);
                after = { existed: true, hash: sha256(buf), bytes: buf.length };
            } catch (err) {
                if (err.code !== 'ENOENT') throw err;
            }
            let action;
            if (!before.existed && after.existed) action = 'created';
            else if (before.existed && !after.existed) action = 'deleted';
            else if (before.hash === after.hash) action = 'unchanged';
            else action = 'modified';
            changes.push({
                path: rel,
                action,
                bytesBefore: before.bytes,
                bytesAfter: after.bytes,
                sha256Before: before.hash,
                sha256After: after.hash
            });
        }
        changes.sort((a, b) => a.path.localeCompare(b.path));
        return changes;
    }
}

/** OpenAI-compatible tool definitions sent to the DeepSeek API. */
export const TOOL_DEFINITIONS = [
    {
        type: 'function',
        function: {
            name: 'list_files',
            description:
                'List files and directories inside the working directory. Returns relative paths, ' +
                'marking which ones are inside your allowed scope.',
            parameters: {
                type: 'object',
                properties: {
                    directory: { type: 'string', description: 'Directory relative to the working directory. Defaults to the root.' },
                    depth: { type: 'integer', description: 'How many levels to descend. 1 = the directory itself. Default 2, max 6.' }
                },
                required: []
            }
        }
    },
    {
        type: 'function',
        function: {
            name: 'search_files',
            description: 'Search file contents with a JavaScript regular expression. Returns matching lines with paths and line numbers.',
            parameters: {
                type: 'object',
                properties: {
                    pattern: { type: 'string', description: 'JavaScript regular expression source.' },
                    glob: { type: 'string', description: 'Optional glob filter on the relative path, e.g. "src/**/*.cs".' },
                    ignoreCase: { type: 'boolean', description: 'Case-insensitive match. Default false.' },
                    maxResults: { type: 'integer', description: 'Maximum matching lines to return. Default 60.' }
                },
                required: ['pattern']
            }
        }
    },
    {
        type: 'function',
        function: {
            name: 'read_file',
            description: 'Read a text file inside the allowed scope. Output is line-numbered.',
            parameters: {
                type: 'object',
                properties: {
                    path: { type: 'string', description: 'File path relative to the working directory.' },
                    startLine: { type: 'integer', description: '1-based first line. Default 1.' },
                    lineCount: { type: 'integer', description: 'How many lines to return. Default: the whole file.' }
                },
                required: ['path']
            }
        }
    },
    {
        type: 'function',
        function: {
            name: 'write_file',
            description:
                'Create or fully overwrite a file inside the allowed scope. Write the complete final content: ' +
                'no placeholders, no TODO, no ellipsis, no commented-out replacement code.',
            parameters: {
                type: 'object',
                properties: {
                    path: { type: 'string', description: 'File path relative to the working directory.' },
                    content: { type: 'string', description: 'Complete file content.' }
                },
                required: ['path', 'content']
            }
        }
    },
    {
        type: 'function',
        function: {
            name: 'edit_file',
            description:
                'Replace an exact substring in an existing file. The old text must appear exactly once ' +
                'unless replaceAll is true. Prefer this over write_file for small changes.',
            parameters: {
                type: 'object',
                properties: {
                    path: { type: 'string', description: 'File path relative to the working directory.' },
                    oldText: { type: 'string', description: 'Exact text to replace, including indentation.' },
                    newText: { type: 'string', description: 'Replacement text.' },
                    replaceAll: { type: 'boolean', description: 'Replace every occurrence. Default false.' }
                },
                required: ['path', 'oldText', 'newText']
            }
        }
    },
    {
        type: 'function',
        function: {
            name: 'report_done',
            description:
                'End the assignment. Call this exactly once, when the work is complete or when you are blocked. ' +
                'Write summary and blockers in Italian: they are read by the operator.',
            parameters: {
                type: 'object',
                properties: {
                    summary: { type: 'string', description: 'What you changed and why, in Italian.' },
                    acceptanceCriteriaMet: {
                        type: 'array',
                        items: { type: 'string' },
                        description: 'One line per acceptance criterion: the criterion and the evidence that it holds.'
                    },
                    blockers: {
                        type: 'array',
                        items: { type: 'string' },
                        description: 'Anything you could not do and why. Empty when nothing is blocked.'
                    }
                },
                required: ['summary']
            }
        }
    }
];

function walk(sandbox, startAbs, startRel, maxDepth, limit) {
    const entries = [];
    const queue = [{ abs: startAbs, rel: startRel, depth: 1 }];
    while (queue.length > 0 && entries.length < limit) {
        const node = queue.shift();
        let dirents;
        try {
            dirents = fs.readdirSync(node.abs, { withFileTypes: true });
        } catch {
            continue;
        }
        dirents.sort((a, b) => a.name.localeCompare(b.name));
        for (const d of dirents) {
            if (entries.length >= limit) break;
            if (d.isDirectory() && SKIP_DIRS.has(d.name)) continue;
            const rel = node.rel === '.' ? d.name : node.rel + '/' + d.name;
            const abs = path.join(node.abs, d.name);
            if (d.isDirectory()) {
                entries.push({ path: rel + '/', kind: 'dir', inScope: false });
                if (node.depth < maxDepth) queue.push({ abs, rel, depth: node.depth + 1 });
            } else if (d.isFile()) {
                let bytes = 0;
                try {
                    bytes = fs.statSync(abs).size;
                } catch {
                    bytes = -1;
                }
                entries.push({ path: rel, kind: 'file', bytes, inScope: sandbox.inScope(rel) });
            } else if (d.isSymbolicLink()) {
                entries.push({ path: rel, kind: 'link', inScope: false });
            }
        }
    }
    return entries;
}

function collectSearchTargets(sandbox, globFilter, limit) {
    const re = globFilter ? globToRegExp(globFilter) : null;
    const out = [];
    const queue = [{ abs: sandbox.root, rel: '.' }];
    while (queue.length > 0 && out.length < limit) {
        const node = queue.shift();
        let dirents;
        try {
            dirents = fs.readdirSync(node.abs, { withFileTypes: true });
        } catch {
            continue;
        }
        dirents.sort((a, b) => a.name.localeCompare(b.name));
        for (const d of dirents) {
            if (out.length >= limit) break;
            if (d.isDirectory() && SKIP_DIRS.has(d.name)) continue;
            const rel = node.rel === '.' ? d.name : node.rel + '/' + d.name;
            const abs = path.join(node.abs, d.name);
            if (d.isDirectory()) {
                queue.push({ abs, rel });
            } else if (d.isFile()) {
                if (sandbox.isDenied(rel)) continue;
                if (re && !re.test(rel)) continue;
                out.push({ abs, rel });
            }
        }
    }
    return out;
}

/**
 * Builds the executable side of the worker tool surface.
 *
 * @param {object} sandbox from createSandbox
 * @param {ChangeJournal} journal
 */
export function createWorkerTools(sandbox, journal) {
    function readTextFile(abs, rel) {
        const stat = fs.statSync(abs);
        if (stat.size > IO_LIMITS.maxReadBytes) {
            throw new SandboxError(
                'file is ' + stat.size + ' bytes, over the ' + IO_LIMITS.maxReadBytes + ' byte read limit: ' + rel,
                'TOO_LARGE'
            );
        }
        const buf = fs.readFileSync(abs);
        if (looksBinary(buf)) {
            throw new SandboxError('file looks binary, refusing to read as text: ' + rel, 'BINARY');
        }
        return buf.toString('utf8');
    }

    const handlers = {
        list_files(args) {
            const target = sandbox.forListing(args.directory);
            const depth = Math.min(6, Math.max(1, Math.trunc(Number(args.depth ?? 2)) || 2));
            const entries = walk(sandbox, target.abs, target.rel, depth, IO_LIMITS.maxListEntries);
            const lines = entries.map((e) => {
                if (e.kind === 'dir') return e.path;
                if (e.kind === 'link') return e.path + '  [link, not readable]';
                return e.path + '  (' + e.bytes + ' B)' + (e.inScope ? '' : '  [outside allowedPaths]');
            });
            return {
                text:
                    'directory: ' + target.rel + '\nallowedPaths: ' + sandbox.allowedPaths.join(', ') + '\n' +
                    (lines.length === 0 ? '(empty)' : lines.join('\n')) +
                    (entries.length >= IO_LIMITS.maxListEntries ? '\n[... listing truncated ...]' : '')
            };
        },

        search_files(args) {
            if (typeof args.pattern !== 'string' || args.pattern === '') {
                throw new SandboxError('pattern is required', 'INVALID_ARGS');
            }
            let re;
            try {
                re = new RegExp(args.pattern, args.ignoreCase ? 'i' : '');
            } catch (err) {
                throw new SandboxError('invalid regular expression: ' + err.message, 'INVALID_ARGS');
            }
            const maxResults = Math.min(
                IO_LIMITS.maxSearchResults,
                Math.max(1, Math.trunc(Number(args.maxResults ?? 60)) || 60)
            );
            const targets = collectSearchTargets(sandbox, args.glob, 5000);
            const hits = [];
            let scanned = 0;
            for (const t of targets) {
                if (hits.length >= maxResults) break;
                let buf;
                try {
                    const stat = fs.statSync(t.abs);
                    if (stat.size > IO_LIMITS.maxSearchFileBytes) continue;
                    buf = fs.readFileSync(t.abs);
                } catch {
                    continue;
                }
                if (looksBinary(buf)) continue;
                scanned += 1;
                const lines = buf.toString('utf8').split(/\r?\n/);
                for (let i = 0; i < lines.length; i += 1) {
                    if (hits.length >= maxResults) break;
                    if (re.test(lines[i])) {
                        hits.push(t.rel + ':' + (i + 1) + ': ' + lines[i].slice(0, 400));
                    }
                }
            }
            return {
                text:
                    'scanned ' + scanned + ' files, ' + hits.length + ' matching lines' +
                    (hits.length >= maxResults ? ' (limit reached)' : '') + '\n' +
                    (hits.length === 0 ? '(no match)' : hits.join('\n'))
            };
        },

        read_file(args) {
            const target = sandbox.forRead(args.path);
            const content = readTextFile(target.abs, target.rel);
            const lines = content.split(/\r?\n/);
            const start = Math.max(1, Math.trunc(Number(args.startLine ?? 1)) || 1);
            const count =
                args.lineCount === undefined || args.lineCount === null
                    ? lines.length
                    : Math.max(1, Math.trunc(Number(args.lineCount)) || 1);
            const slice = lines.slice(start - 1, start - 1 + count);
            const numbered = slice.map((l, i) => String(start + i).padStart(5, ' ') + '\t' + l).join('\n');
            return {
                text:
                    target.rel + ' (' + lines.length + ' lines total, showing ' + slice.length + ' from line ' + start + ')\n' +
                    numbered
            };
        },

        write_file(args) {
            if (typeof args.content !== 'string') {
                throw new SandboxError('content must be a string', 'INVALID_ARGS');
            }
            const bytes = Buffer.byteLength(args.content, 'utf8');
            if (bytes > IO_LIMITS.maxWriteBytes) {
                throw new SandboxError(
                    'content is ' + bytes + ' bytes, over the ' + IO_LIMITS.maxWriteBytes + ' byte write limit',
                    'TOO_LARGE'
                );
            }
            const target = sandbox.forWrite(args.path);
            journal.capture(target.rel, target.abs);
            fs.mkdirSync(path.dirname(target.abs), { recursive: true });
            fs.writeFileSync(target.abs, args.content, 'utf8');
            return { text: 'wrote ' + target.rel + ' (' + bytes + ' bytes)', mutated: true };
        },

        edit_file(args) {
            if (typeof args.oldText !== 'string' || args.oldText === '') {
                throw new SandboxError('oldText must be a non-empty string', 'INVALID_ARGS');
            }
            if (typeof args.newText !== 'string') {
                throw new SandboxError('newText must be a string', 'INVALID_ARGS');
            }
            const target = sandbox.forWrite(args.path);
            if (!target.exists) {
                throw new SandboxError('file does not exist, use write_file to create it: ' + target.rel, 'NOT_FOUND');
            }
            const content = readTextFile(target.abs, target.rel);
            const occurrences = content.split(args.oldText).length - 1;
            if (occurrences === 0) {
                // A bare "not found" costs a whole round: the worker re-reads the file
                // and guesses again. Naming where the first line does appear turns the
                // retry into one corrected call.
                const firstLine = args.oldText.split(/\r?\n/)[0].trim();
                const near = [];
                if (firstLine.length >= 8) {
                    const lines = content.split(/\r?\n/);
                    for (let i = 0; i < lines.length && near.length < 3; i += 1) {
                        if (lines[i].includes(firstLine)) near.push(i + 1);
                    }
                }
                throw new SandboxError(
                    'oldText not found in ' + target.rel +
                        (near.length > 0
                            ? '. Its first line appears at line ' + near.join(', ') +
                              ': read those lines and copy the text exactly, indentation included.'
                            : '. Its first line appears nowhere in the file: read the file before editing.'),
                    'NO_MATCH'
                );
            }
            if (occurrences > 1 && !args.replaceAll) {
                throw new SandboxError(
                    'oldText appears ' + occurrences + ' times in ' + target.rel +
                        '; pass replaceAll or give a longer unique excerpt',
                    'AMBIGUOUS'
                );
            }
            const updated = args.replaceAll
                ? content.split(args.oldText).join(args.newText)
                : content.replace(args.oldText, args.newText);
            const bytes = Buffer.byteLength(updated, 'utf8');
            if (bytes > IO_LIMITS.maxWriteBytes) {
                throw new SandboxError('result is ' + bytes + ' bytes, over the write limit', 'TOO_LARGE');
            }
            journal.capture(target.rel, target.abs);
            fs.writeFileSync(target.abs, updated, 'utf8');
            return {
                text: 'edited ' + target.rel + ' (' + (args.replaceAll ? occurrences : 1) + ' replacement(s), ' + bytes + ' bytes)',
                mutated: true
            };
        },

        report_done(args) {
            return {
                text: 'assignment closed',
                done: {
                    summary: typeof args.summary === 'string' ? args.summary : '',
                    acceptanceCriteriaMet: Array.isArray(args.acceptanceCriteriaMet) ? args.acceptanceCriteriaMet : [],
                    blockers: Array.isArray(args.blockers) ? args.blockers : []
                }
            };
        }
    };

    return {
        definitions: TOOL_DEFINITIONS,
        names: Object.keys(handlers),
        /**
         * Runs one worker tool call. Never throws: a refused or failed call comes
         * back as an error string so the worker can correct itself in the next round.
         */
        invoke(name, args) {
            const handler = handlers[name];
            if (!handler) {
                return { ok: false, text: 'ERROR unknown tool "' + name + '". Available: ' + Object.keys(handlers).join(', ') };
            }
            try {
                const result = handler(args ?? {});
                return { ok: true, text: truncate(result.text), done: result.done, mutated: result.mutated === true };
            } catch (err) {
                const code = err instanceof SandboxError ? err.code : err.code || 'ERROR';
                return { ok: false, text: 'ERROR [' + code + '] ' + err.message };
            }
        }
    };
}
