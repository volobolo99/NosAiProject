/**
 * Runtime configuration for the DeepSeek delegation server.
 *
 * The API key is read from the environment at call time and never stored,
 * logged or echoed back. Only its presence is ever reported.
 */

export const DEFAULT_BASE_URL = 'https://api.deepseek.com';

/**
 * Worker models this server accepts. Identifiers verified against the official
 * model list at https://api-docs.deepseek.com/quick_start/pricing
 * (deepseek-v4-flash, deepseek-v4-pro, deepseek-v4-flash-vision-exp).
 * The vision model is not offered here: this worker only edits text files.
 */
export const SUPPORTED_MODELS = ['deepseek-v4-flash', 'deepseek-v4-pro'];

/**
 * Model used when DEEPSEEK_MODEL is not defined. Flash by operator decision:
 * pro is opt-in, never a silent upgrade and never an error fallback.
 */
export const DEFAULT_MODEL = 'deepseek-v4-flash';

/** Environment flag set while a delegation is running; blocks recursive delegation. */
export const DELEGATION_ACTIVE_ENV = 'NOSAI_DEEPSEEK_DELEGATION_ACTIVE';

/** Budget ceilings. Requested values are clamped into these ranges. */
export const BUDGET = {
    maxApiRounds: { def: 24, min: 1, max: 80 },
    maxToolCalls: { def: 60, min: 1, max: 300 },
    timeoutSeconds: { def: 600, min: 10, max: 3600 },
    maxRetries: { def: 2, min: 0, max: 5 },
    // A single reasoning answer on a large assignment can run past three minutes.
    // 300 keeps that answer alive and still leaves a full second attempt inside
    // the 600 s overall ceiling above.
    requestTimeoutSeconds: { def: 300, min: 10, max: 600 }
};

/** Hard caps on what a single worker tool call may move. */
export const IO_LIMITS = {
    maxReadBytes: 400000,
    maxWriteBytes: 400000,
    maxListEntries: 400,
    maxSearchResults: 120,
    maxSearchFileBytes: 2000000,
    maxToolResultChars: 60000
};

/** Paths the worker may never touch, whatever `allowedPaths` says. */
export const ALWAYS_DENIED = ['.git/**', '**/node_modules/**', '**/bin/**', '**/obj/**'];

export class ConfigError extends Error {
    constructor(message) {
        super(message);
        this.name = 'ConfigError';
    }
}

/**
 * Resolves the worker model.
 *
 * Precedence: explicit tool argument, then DEEPSEEK_MODEL, then DEFAULT_MODEL.
 * An unsupported identifier is an error at either level: the caller is told
 * what it asked for and what is accepted, and nothing is substituted.
 */
export function resolveModel(requested, env = process.env) {
    const fromArg = typeof requested === 'string' && requested.trim() !== '' ? requested.trim() : undefined;
    const rawEnv = env.DEEPSEEK_MODEL;
    const fromEnv = typeof rawEnv === 'string' && rawEnv.trim() !== '' ? rawEnv.trim() : undefined;

    if (fromArg !== undefined) {
        if (!SUPPORTED_MODELS.includes(fromArg)) {
            throw new ConfigError(
                'unsupported model argument "' + fromArg + '". Accepted: ' + SUPPORTED_MODELS.join(', ')
            );
        }
        return { model: fromArg, source: 'argument' };
    }
    if (fromEnv !== undefined) {
        if (!SUPPORTED_MODELS.includes(fromEnv)) {
            throw new ConfigError(
                'DEEPSEEK_MODEL is set to "' + fromEnv + '", which this server does not accept. ' +
                    'Accepted: ' + SUPPORTED_MODELS.join(', ') + '. No substitution is made.'
            );
        }
        return { model: fromEnv, source: 'DEEPSEEK_MODEL' };
    }
    return { model: DEFAULT_MODEL, source: 'default' };
}

/**
 * Reads API configuration from the environment.
 * Returns the key so the HTTP layer can send it; callers must never log it.
 */
export function readConfig(env = process.env, requestedModel = undefined) {
    const apiKey = env.DEEPSEEK_API_KEY;
    if (typeof apiKey !== 'string' || apiKey.trim().length === 0) {
        throw new ConfigError(
            'DEEPSEEK_API_KEY is not set in the server process environment. ' +
                'Set it in the shell that launches Claude Code, then restart the session.'
        );
    }
    const baseUrl = (env.DEEPSEEK_BASE_URL || DEFAULT_BASE_URL).replace(/\/+$/, '');
    const { model, source } = resolveModel(requestedModel, env);
    return { apiKey: apiKey.trim(), baseUrl, model, modelSource: source };
}

/** Clamps a requested budget value into its allowed range. */
export function clamp(name, requested) {
    const spec = BUDGET[name];
    if (!spec) throw new ConfigError('unknown budget key: ' + name);
    if (requested === undefined || requested === null) return spec.def;
    const n = Math.trunc(Number(requested));
    if (!Number.isFinite(n)) return spec.def;
    return Math.min(spec.max, Math.max(spec.min, n));
}

/** Builds the effective budget for one delegation. */
export function resolveBudget(args = {}) {
    return {
        maxApiRounds: clamp('maxApiRounds', args.maxApiRounds),
        maxToolCalls: clamp('maxToolCalls', args.maxToolCalls),
        timeoutSeconds: clamp('timeoutSeconds', args.timeoutSeconds),
        maxRetries: clamp('maxRetries', args.maxRetries),
        requestTimeoutSeconds: clamp('requestTimeoutSeconds', args.requestTimeoutSeconds)
    };
}
