/**
 * The delegation loop: Claude states the assignment, DeepSeek works it with
 * the file tools, and the loop stops on a report, a budget ceiling, the
 * deadline or an API error. Nothing here decides anything about the code —
 * it only carries messages and enforces the ceilings.
 */

import { chatCompletion, readUsage, DeepSeekApiError } from './deepseekClient.mjs';
import { excerpt, nullEventLog, toolArgFields } from './eventLog.mjs';
import { createSandbox, SandboxError } from './sandbox.mjs';
import { ChangeJournal, createWorkerTools } from './workerTools.mjs';

/** Status values a delegation can end with. */
export const STATUS = {
    completed: 'completed',
    blocked: 'blocked',
    budgetExhausted: 'budget_exhausted',
    timeout: 'timeout',
    apiError: 'api_error',
    setupError: 'setup_error',
    noReport: 'stopped_without_report'
};

function buildSystemPrompt(sandbox, readOnly) {
    return [
        'You are the implementation programmer on a delegated assignment. Claude is the architect and',
        'reviewer: it wrote the assignment, and it will review and integrate whatever you produce.',
        '',
        'Your only capability is the file tools listed for you. You cannot run a shell, build, run tests,',
        'reach the network, or delegate any part of this work to another model. Do not claim to have built',
        'or tested anything: Claude runs the build and the tests after you finish.',
        '',
        'Working directory: ' + sandbox.root,
        'Paths you may touch (globs, relative to the working directory):',
        ...sandbox.allowedPaths.map((p) => '  - ' + p),
        readOnly
            ? 'This assignment is READ-ONLY: inspect and report, do not write.'
            : 'Anything outside those globs is refused by the tool layer. Do not try to work around it: if the',
        readOnly ? '' : 'assignment cannot be done inside that scope, say so through report_done and stop.',
        '',
        'Rules:',
        '1. Read before you write. Never guess the contents of a file you have not read.',
        '2. Every file you write must be complete and compilable: no TODO, no FIXME, no pseudocode, no',
        '   ellipsis, no partial method, no commented-out replacement code.',
        '3. Prefer edit_file for small changes; use write_file when you create a file or rewrite it whole.',
        '4. Match the surrounding code: naming, indentation, comment density, and the language already used',
        '   for identifiers and code comments (English).',
        '5. When you are done, or when you are blocked, call report_done exactly once. Write its summary,',
        '   acceptance lines and blockers in Italian - the operator reads them.',
        '6. Do not invent evidence. If you did not verify something, say it is unverified.'
    ]
        .filter((line) => line !== '')
        .join('\n');
}

function buildTaskMessage({ task, acceptanceCriteria, context }) {
    const parts = ['ASSIGNMENT', task.trim()];
    if (Array.isArray(acceptanceCriteria) && acceptanceCriteria.length > 0) {
        parts.push('', 'ACCEPTANCE CRITERIA (each one must be observably true when you finish)');
        acceptanceCriteria.forEach((c, i) => parts.push(i + 1 + '. ' + c));
    }
    if (typeof context === 'string' && context.trim() !== '') {
        parts.push('', 'CONFIRMED CONTEXT FROM THE ARCHITECT', context.trim());
    }
    parts.push('', 'Start by reading the files you need. Finish with report_done.');
    return parts.join('\n');
}

function parseArguments(raw) {
    if (raw === undefined || raw === null || raw === '') return {};
    if (typeof raw === 'object') return raw;
    return JSON.parse(raw);
}

function accumulateUsage(total, usage) {
    if (!usage) return total;
    const next = { ...total };
    for (const key of Object.keys(next)) {
        if (typeof usage[key] === 'number') next[key] += usage[key];
    }
    return next;
}

/**
 * Runs one delegation end to end.
 *
 * @param {object} options
 * @param {object} options.api  { apiKey, baseUrl, model }
 * @param {object} options.budget from resolveBudget
 * @param {object} options.request { task, workingDirectory, allowedPaths, acceptanceCriteria, context, readOnly }
 * @param {Function} [options.fetchImpl] injected for tests
 * @param {Function} [options.sleepImpl] injected for tests
 * @param {Function} [options.now] injected for tests
 * @param {object} [options.log] event log from createEventLog; the null log by default
 */
export async function runDelegation({
    api,
    budget,
    request,
    fetchImpl,
    sleepImpl,
    now = () => Date.now(),
    log = nullEventLog
}) {
    const started = now();
    let sandbox;
    try {
        sandbox = createSandbox({
            root: request.workingDirectory,
            allowedPaths: request.allowedPaths,
            readOnly: request.readOnly === true
        });
    } catch (err) {
        const setupError = err instanceof SandboxError ? '[' + err.code + '] ' + err.message : err.message;
        log.event('setup_error', { error: excerpt(setupError) });
        return {
            status: STATUS.setupError,
            model: api.model,
            error: setupError,
            rounds: 0,
            toolCalls: 0,
            changes: [],
            errors: [],
            usage: null,
            durationMs: now() - started
        };
    }

    const journal = new ChangeJournal(sandbox);
    const tools = createWorkerTools(sandbox, journal);

    const messages = [
        { role: 'system', content: buildSystemPrompt(sandbox, sandbox.readOnly) },
        { role: 'user', content: buildTaskMessage(request) }
    ];

    const controller = new AbortController();
    const deadline = setTimeout(() => controller.abort(), budget.timeoutSeconds * 1000);

    let rounds = 0;
    let toolCalls = 0;
    let refusedCalls = 0;
    let silentRounds = 0;
    let status = STATUS.budgetExhausted;
    let report = null;
    let apiError = null;
    let lastText = '';
    const errors = [];
    let usage = {
        promptTokens: 0,
        completionTokens: 0,
        totalTokens: 0,
        promptCacheHitTokens: 0,
        promptCacheMissTokens: 0
    };

    try {
        while (rounds < budget.maxApiRounds) {
            if (controller.signal.aborted) {
                status = STATUS.timeout;
                break;
            }
            rounds += 1;

            const roundStarted = now();
            log.event('api_request_start', { round: rounds });

            let response;
            try {
                response = await chatCompletion({
                    apiKey: api.apiKey,
                    baseUrl: api.baseUrl,
                    model: api.model,
                    messages,
                    tools: tools.definitions,
                    maxRetries: budget.maxRetries,
                    requestTimeoutMs: budget.requestTimeoutSeconds * 1000,
                    signal: controller.signal,
                    fetchImpl,
                    sleepImpl
                });
            } catch (err) {
                if (controller.signal.aborted) {
                    status = STATUS.timeout;
                } else {
                    status = STATUS.apiError;
                    apiError =
                        err instanceof DeepSeekApiError
                            ? 'HTTP ' + (err.status || 0) + ': ' + err.message
                            : err.message;
                    errors.push(apiError);
                }
                log.event('api_request_error', {
                    round: rounds,
                    ms: now() - roundStarted,
                    status,
                    error: excerpt(apiError ?? 'aborted')
                });
                break;
            }

            const roundUsage = readUsage(response);
            usage = accumulateUsage(usage, roundUsage);
            const choice = response?.choices?.[0];
            const message = choice?.message;
            log.event('api_request_end', {
                round: rounds,
                ms: now() - roundStarted,
                finishReason: choice?.finish_reason ?? null,
                toolCallsRequested: Array.isArray(message?.tool_calls) ? message.tool_calls.length : 0,
                usage: roundUsage ?? null
            });
            if (!message) {
                status = STATUS.apiError;
                apiError = 'DeepSeek response carried no message';
                errors.push(apiError);
                log.event('api_request_error', { round: rounds, status, error: apiError });
                break;
            }
            if (typeof message.content === 'string' && message.content.trim() !== '') {
                lastText = message.content.trim();
            }

            const calls = Array.isArray(message.tool_calls) ? message.tool_calls : [];
            // The assistant turn goes back verbatim; DeepSeek requires the tool_calls
            // it emitted to be present when the tool results arrive.
            messages.push({
                role: 'assistant',
                content: message.content ?? '',
                ...(calls.length > 0 ? { tool_calls: calls } : {})
            });

            if (calls.length === 0) {
                silentRounds += 1;
                if (silentRounds >= 3) {
                    status = STATUS.noReport;
                    break;
                }
                messages.push({
                    role: 'user',
                    content:
                        'You answered with text and no tool call. Text alone does not change any file. ' +
                        'Use the file tools to do the work, then call report_done exactly once.'
                });
                continue;
            }
            silentRounds = 0;

            let finished = false;
            for (const call of calls) {
                // report_done is exempt from the ceiling: it writes nothing, and without the
                // exemption a delegation that hits the limit could never close with a report.
                const isTerminator = call.function?.name === 'report_done';
                if (!isTerminator && toolCalls >= budget.maxToolCalls) {
                    messages.push({
                        role: 'tool',
                        tool_call_id: call.id,
                        content: 'ERROR [BUDGET] tool call limit of ' + budget.maxToolCalls + ' reached. ' +
                            'Call report_done now with what you have.'
                    });
                    log.event('tool_call', {
                        round: rounds,
                        name: call.function?.name ?? '(unnamed)',
                        ok: false,
                        reason: 'BUDGET: tool call limit of ' + budget.maxToolCalls + ' reached'
                    });
                    continue;
                }
                toolCalls += 1;

                let args;
                try {
                    args = parseArguments(call.function?.arguments);
                } catch (err) {
                    refusedCalls += 1;
                    const text = 'ERROR [BAD_ARGUMENTS] arguments were not valid JSON: ' + err.message;
                    errors.push(call.function?.name + ': ' + text);
                    messages.push({ role: 'tool', tool_call_id: call.id, content: text });
                    log.event('tool_call', {
                        round: rounds,
                        name: call.function?.name ?? '(unnamed)',
                        ok: false,
                        reason: excerpt(text)
                    });
                    continue;
                }

                const result = tools.invoke(call.function?.name, args);
                if (!result.ok) {
                    refusedCalls += 1;
                    errors.push(call.function?.name + ': ' + result.text);
                }
                log.event('tool_call', {
                    round: rounds,
                    name: call.function?.name ?? '(unnamed)',
                    ...toolArgFields(args),
                    ok: result.ok !== false,
                    reason: result.ok !== false ? undefined : excerpt(result.text)
                });
                messages.push({ role: 'tool', tool_call_id: call.id, content: result.text });

                if (result.done) {
                    report = result.done;
                    finished = true;
                }
            }

            if (finished) {
                status = report.blockers && report.blockers.length > 0 ? STATUS.blocked : STATUS.completed;
                break;
            }
            if (toolCalls >= budget.maxToolCalls && rounds >= budget.maxApiRounds) {
                status = STATUS.budgetExhausted;
                break;
            }
        }
    } finally {
        clearTimeout(deadline);
    }

    if (controller.signal.aborted && status !== STATUS.apiError) {
        status = STATUS.timeout;
    }

    const changes = journal.summary();
    for (const change of changes) {
        log.event('file_change', {
            action: change.action,
            path: change.path,
            bytesBefore: change.bytesBefore,
            bytesAfter: change.bytesAfter
        });
    }
    if (report) {
        // The report's own text goes back to Claude in the tool result; the log
        // keeps only its shape, so the file stays a record of events.
        log.event('worker_report', {
            acceptanceCriteriaMet: report.acceptanceCriteriaMet?.length ?? 0,
            blockers: report.blockers?.length ?? 0
        });
    }

    return {
        status,
        model: api.model,
        rounds,
        toolCalls,
        refusedCalls,
        changes,
        report,
        finalText: lastText,
        errors,
        error: apiError,
        usage,
        budget,
        durationMs: now() - started
    };
}
