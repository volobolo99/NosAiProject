/**
 * Minimal client for the official DeepSeek API (OpenAI-compatible surface,
 * https://api.deepseek.com). Only the two endpoints this server needs:
 * POST /chat/completions and GET /models.
 *
 * The API key travels in the Authorization header and nowhere else. Error
 * messages are built from status codes and response bodies with the header
 * never interpolated, so a thrown error can be logged safely.
 */

export class DeepSeekApiError extends Error {
    constructor(message, { status = 0, retryable = false, body = undefined } = {}) {
        super(message);
        this.name = 'DeepSeekApiError';
        this.status = status;
        this.retryable = retryable;
        this.body = body;
    }
}

const RETRYABLE_STATUS = new Set([408, 429, 500, 502, 503, 504]);

function sleep(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
}

async function readBody(response) {
    const text = await response.text();
    try {
        return { text, json: JSON.parse(text) };
    } catch {
        return { text, json: undefined };
    }
}

function describeError(status, body) {
    const apiMessage = body.json?.error?.message ?? body.json?.message;
    const detail = apiMessage ?? (body.text ? body.text.slice(0, 500) : '(empty body)');
    return 'DeepSeek API returned HTTP ' + status + ': ' + detail;
}

/**
 * One chat completion call, with bounded retries on transient failures.
 *
 * @param {object} options
 * @param {string} options.apiKey
 * @param {string} options.baseUrl
 * @param {string} options.model
 * @param {Array} options.messages
 * @param {Array} [options.tools]
 * @param {number} [options.maxRetries]
 * @param {number} [options.requestTimeoutMs]
 * @param {AbortSignal} [options.signal] overall delegation deadline
 * @param {Function} [options.fetchImpl] injected for tests
 * @param {Function} [options.sleepImpl] injected for tests
 */
export async function chatCompletion({
    apiKey,
    baseUrl,
    model,
    messages,
    tools,
    temperature = 0,
    maxTokens = undefined,
    maxRetries = 2,
    requestTimeoutMs = 180000,
    signal,
    fetchImpl = globalThis.fetch,
    sleepImpl = sleep
}) {
    const url = baseUrl + '/chat/completions';
    const payload = { model, messages, temperature, stream: false };
    if (typeof maxTokens === 'number') payload.max_tokens = maxTokens;
    if (tools && tools.length > 0) {
        payload.tools = tools;
        payload.tool_choice = 'auto';
    }

    let lastError;
    for (let attempt = 0; attempt <= maxRetries; attempt += 1) {
        if (signal?.aborted) {
            throw new DeepSeekApiError('delegation deadline reached before the request was sent', { retryable: false });
        }
        const perRequest = new AbortController();
        const onAbort = () => perRequest.abort();
        signal?.addEventListener('abort', onAbort, { once: true });
        const timer = setTimeout(() => perRequest.abort(), requestTimeoutMs);
        try {
            const response = await fetchImpl(url, {
                method: 'POST',
                headers: {
                    'content-type': 'application/json',
                    accept: 'application/json',
                    authorization: 'Bearer ' + apiKey
                },
                body: JSON.stringify(payload),
                signal: perRequest.signal
            });
            const body = await readBody(response);
            if (response.ok) {
                if (!body.json) {
                    throw new DeepSeekApiError('DeepSeek API returned a non-JSON body', {
                        status: response.status,
                        retryable: false,
                        body: body.text.slice(0, 500)
                    });
                }
                return body.json;
            }
            const retryable = RETRYABLE_STATUS.has(response.status);
            lastError = new DeepSeekApiError(describeError(response.status, body), {
                status: response.status,
                retryable,
                body: body.json?.error ?? undefined
            });
            if (!retryable) throw lastError;
        } catch (err) {
            if (err instanceof DeepSeekApiError) {
                if (!err.retryable) throw err;
                lastError = err;
            } else if (signal?.aborted) {
                throw new DeepSeekApiError('delegation deadline reached during the request', { retryable: false });
            } else if (err.name === 'AbortError') {
                lastError = new DeepSeekApiError(
                    'request timed out after ' + requestTimeoutMs + ' ms',
                    { retryable: true }
                );
            } else {
                lastError = new DeepSeekApiError('network failure calling DeepSeek: ' + err.message, {
                    retryable: true
                });
            }
        } finally {
            clearTimeout(timer);
            signal?.removeEventListener('abort', onAbort);
        }

        if (attempt < maxRetries) {
            // 1s, 2s, 4s ... capped; enough to clear a rate limit without stalling the session.
            await sleepImpl(Math.min(8000, 1000 * 2 ** attempt));
        }
    }
    throw lastError ?? new DeepSeekApiError('DeepSeek call failed with no recorded error', { retryable: false });
}

/**
 * Lists the models the account can actually call.
 * Used to confirm the resolved model before spending a real completion.
 */
export async function listModels({
    apiKey,
    baseUrl,
    requestTimeoutMs = 30000,
    fetchImpl = globalThis.fetch
}) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), requestTimeoutMs);
    try {
        const response = await fetchImpl(baseUrl + '/models', {
            method: 'GET',
            headers: { accept: 'application/json', authorization: 'Bearer ' + apiKey },
            signal: controller.signal
        });
        const body = await readBody(response);
        if (!response.ok) {
            throw new DeepSeekApiError(describeError(response.status, body), {
                status: response.status,
                retryable: RETRYABLE_STATUS.has(response.status)
            });
        }
        const data = Array.isArray(body.json?.data) ? body.json.data : [];
        return data.map((m) => m.id).filter((id) => typeof id === 'string');
    } finally {
        clearTimeout(timer);
    }
}

/** Normalizes the usage block; DeepSeek adds cache counters to the OpenAI shape. */
export function readUsage(response) {
    const u = response?.usage;
    if (!u) return null;
    return {
        promptTokens: u.prompt_tokens ?? null,
        completionTokens: u.completion_tokens ?? null,
        totalTokens: u.total_tokens ?? null,
        promptCacheHitTokens: u.prompt_cache_hit_tokens ?? null,
        promptCacheMissTokens: u.prompt_cache_miss_tokens ?? null
    };
}
