#!/usr/bin/env node
/**
 * Local MCP server exposing a single tool, `delegate_to_deepseek`.
 *
 * Claude states an assignment and a file scope; DeepSeek carries it out through
 * the official API with a file-only tool surface confined to the working
 * directory. Builds and tests are deliberately not delegated: they stay with
 * Claude, which already holds those permissions.
 *
 * Transport is stdio, so stdout carries JSON-RPC only. Everything diagnostic
 * goes to stderr.
 */

import { pathToFileURL } from 'node:url';

import { McpServer } from '@modelcontextprotocol/server';
import { StdioServerTransport } from '@modelcontextprotocol/server/stdio';
import * as z from 'zod';

import { DELEGATION_ACTIVE_ENV, SUPPORTED_MODELS, readConfig, resolveBudget, ConfigError } from './config.mjs';
import { runDelegation, STATUS } from './agentLoop.mjs';

/**
 * Recursion guard. If the flag is already set in the inherited environment this
 * process was started from inside a delegation, and it refuses to delegate again.
 * The worker has no tool that could reach this server, so this is the second lock
 * on a door that has no handle on the inside.
 */
const STARTED_INSIDE_DELEGATION = process.env[DELEGATION_ACTIVE_ENV] === '1';
let activeDelegations = 0;

const inputSchema = z.object({
    task: z
        .string()
        .min(20, 'the assignment must actually state the work')
        .describe(
            'The assignment for DeepSeek: objective, starting state, confirmed references, scope, ' +
                'edge cases and expected behaviour. Written in Italian.'
        ),
    workingDirectory: z
        .string()
        .min(1)
        .describe('Absolute path of the directory the worker is confined to. Must already exist.'),
    allowedPaths: z
        .array(z.string().min(1))
        .min(1)
        .describe(
            'Globs relative to workingDirectory scoping every read and write, e.g. ["src/Foo/**", "tests/Foo/**"]. ' +
                '.git, node_modules, bin and obj are always denied.'
        ),
    acceptanceCriteria: z
        .array(z.string().min(1))
        .min(1)
        .describe('Observable criteria the result must satisfy. Given to the worker verbatim.'),
    context: z.string().optional().describe('Confirmed references from the architect: file:line, contracts, decisions.'),
    model: z
        .enum(SUPPORTED_MODELS)
        .optional()
        .describe('Worker model. Defaults to DEEPSEEK_MODEL, and to deepseek-v4-flash when that is unset.'),
    readOnly: z.boolean().optional().describe('When true the worker may read and report but never write.'),
    maxApiRounds: z.number().int().optional().describe('Maximum API round trips. Default 24, hard cap 80.'),
    maxToolCalls: z.number().int().optional().describe('Maximum worker tool calls. Default 60, hard cap 300.'),
    timeoutSeconds: z.number().int().optional().describe('Overall wall-clock ceiling. Default 600, hard cap 3600.'),
    maxRetries: z.number().int().optional().describe('Retries per API call on transient failures. Default 2, cap 5.')
});

function formatReport(result) {
    const lines = [];
    lines.push('stato: ' + result.status);
    lines.push('modello: ' + result.model + (result.modelSource ? ' (da ' + result.modelSource + ')' : ''));
    lines.push(
        'giri API: ' + result.rounds + '/' + (result.budget?.maxApiRounds ?? '?') +
            '   chiamate strumento: ' + result.toolCalls + '/' + (result.budget?.maxToolCalls ?? '?') +
            '   rifiutate: ' + (result.refusedCalls ?? 0)
    );
    lines.push('durata: ' + Math.round((result.durationMs ?? 0) / 1000) + ' s');

    if (result.usage) {
        const u = result.usage;
        lines.push(
            'token: prompt ' + u.promptTokens + ', completion ' + u.completionTokens + ', totale ' + u.totalTokens +
                ' (cache hit ' + u.promptCacheHitTokens + ', miss ' + u.promptCacheMissTokens + ')'
        );
        lines.push(
            'Nota: questo e il consumo riportato dall API per questa chiamata, non un tetto di spesa garantito.'
        );
    }

    lines.push('');
    lines.push('MODIFICHE EFFETTIVE SU DISCO');
    if (!result.changes || result.changes.length === 0) {
        lines.push('  nessun file toccato');
    } else {
        for (const c of result.changes) {
            lines.push('  ' + c.action.padEnd(9) + ' ' + c.path + '  ' + c.bytesBefore + ' -> ' + c.bytesAfter + ' B');
        }
    }

    if (result.report) {
        lines.push('');
        lines.push('RAPPORTO DEL LAVORATORE');
        lines.push(result.report.summary || '(vuoto)');
        if (result.report.acceptanceCriteriaMet?.length > 0) {
            lines.push('Criteri di accettazione:');
            for (const a of result.report.acceptanceCriteriaMet) lines.push('  - ' + a);
        }
        if (result.report.blockers?.length > 0) {
            lines.push('Blocchi dichiarati:');
            for (const b of result.report.blockers) lines.push('  - ' + b);
        }
    } else if (result.finalText) {
        lines.push('');
        lines.push('ULTIMO MESSAGGIO (nessun report_done)');
        lines.push(result.finalText.slice(0, 4000));
    }

    if (result.error) {
        lines.push('');
        lines.push('ERRORE API: ' + result.error);
    }
    if (result.errors?.length > 0) {
        lines.push('');
        lines.push('CHIAMATE RIFIUTATE O FALLITE (' + result.errors.length + ')');
        for (const e of result.errors.slice(0, 40)) lines.push('  - ' + e);
    }

    lines.push('');
    lines.push(
        result.status === STATUS.completed
            ? 'Il lavoratore dichiara di aver finito. Build e test non sono stati eseguiti: li esegue Claude.'
            : 'Esito non completo: leggi stato ed errori prima di integrare.'
    );
    return lines.join('\n');
}

export function createServer() {
    const server = new McpServer(
        { name: 'nosai-deepseek', version: '1.0.0' },
        { capabilities: { tools: {} } }
    );

    server.registerTool(
        'delegate_to_deepseek',
        {
            title: 'Delega un incarico a DeepSeek',
            description:
                'Assign a scoped implementation task to DeepSeek through the official API. The worker can list, ' +
                'search, read, write and edit files inside workingDirectory, limited to allowedPaths. It has no ' +
                'shell, cannot build or run tests, and cannot delegate further. Returns status, the files that ' +
                'actually changed on disk, refused calls, errors and reported token usage.',
            inputSchema,
            annotations: {
                title: 'Delega un incarico a DeepSeek',
                readOnlyHint: false,
                destructiveHint: true,
                idempotentHint: false,
                openWorldHint: true
            }
        },
        async (args) => {
            if (STARTED_INSIDE_DELEGATION) {
                return {
                    isError: true,
                    content: [
                        {
                            type: 'text',
                            text:
                                'Delega rifiutata: questo processo e stato avviato dentro una delega (' +
                                DELEGATION_ACTIVE_ENV + '=1). Le deleghe ricorsive sono vietate.'
                        }
                    ]
                };
            }

            let api;
            try {
                api = readConfig(process.env, args.model);
            } catch (err) {
                return {
                    isError: true,
                    content: [
                        {
                            type: 'text',
                            text:
                                (err instanceof ConfigError ? 'Configurazione non valida: ' : 'Errore: ') + err.message
                        }
                    ]
                };
            }

            const budget = resolveBudget(args);

            activeDelegations += 1;
            process.env[DELEGATION_ACTIVE_ENV] = '1';
            try {
                const result = await runDelegation({
                    api,
                    budget,
                    request: {
                        task: args.task,
                        workingDirectory: args.workingDirectory,
                        allowedPaths: args.allowedPaths,
                        acceptanceCriteria: args.acceptanceCriteria,
                        context: args.context,
                        readOnly: args.readOnly === true
                    }
                });
                result.modelSource = api.modelSource;
                const failed =
                    result.status === STATUS.apiError ||
                    result.status === STATUS.setupError ||
                    result.status === STATUS.timeout;
                return {
                    isError: failed,
                    content: [{ type: 'text', text: formatReport(result) }]
                };
            } finally {
                activeDelegations -= 1;
                if (activeDelegations === 0) delete process.env[DELEGATION_ACTIVE_ENV];
            }
        }
    );

    return server;
}

async function main() {
    const server = createServer();
    const transport = new StdioServerTransport();
    await server.connect(transport);
    process.stderr.write('[nosai-deepseek] MCP server ready on stdio\n');
}

// Only start the transport when run as a program; tests import createServer.
if (process.argv[1] && import.meta.url === new URL('file://' + process.argv[1].replace(/\\/g, '/')).href) {
    main().catch((err) => {
        process.stderr.write('[nosai-deepseek] fatal: ' + err.message + '\n');
        process.exit(1);
    });
}
