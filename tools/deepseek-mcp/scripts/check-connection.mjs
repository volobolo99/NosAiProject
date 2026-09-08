#!/usr/bin/env node
/**
 * One minimal live call against the official DeepSeek API, to prove the link works.
 *
 * It resolves the model the same way the MCP tool does, confirms that model is
 * offered to this account through GET /models, then spends a single very small
 * completion. It touches no project file and prints no credential: the key is
 * read from the environment straight into the Authorization header, and only
 * its presence and length are reported.
 *
 * Usage: node scripts/check-connection.mjs [--model deepseek-v4-pro]
 */

import { readConfig, SUPPORTED_MODELS, ConfigError } from '../src/config.mjs';
import { chatCompletion, listModels, readUsage, DeepSeekApiError } from '../src/deepseekClient.mjs';

function parseArgs(argv) {
    const out = {};
    for (let i = 0; i < argv.length; i += 1) {
        if (argv[i] === '--model') {
            out.model = argv[i + 1];
            i += 1;
        }
    }
    return out;
}

async function main() {
    const args = parseArgs(process.argv.slice(2));

    let cfg;
    try {
        cfg = readConfig(process.env, args.model);
    } catch (err) {
        if (err instanceof ConfigError) {
            console.error('CONFIGURAZIONE: ' + err.message);
            process.exitCode = 2;
            return;
        }
        throw err;
    }

    console.log('base url        : ' + cfg.baseUrl);
    console.log('chiave API      : presente nell ambiente (' + process.env.DEEPSEEK_API_KEY.length + ' caratteri, non stampata)');
    console.log('DEEPSEEK_MODEL  : ' + (process.env.DEEPSEEK_MODEL ?? '(non definita)'));
    console.log('modello risolto : ' + cfg.model + '  (origine: ' + cfg.modelSource + ')');
    console.log('modelli ammessi : ' + SUPPORTED_MODELS.join(', '));

    console.log('\n[1/2] GET /models — quali modelli risponde questo account');
    let available;
    try {
        available = await listModels({ apiKey: cfg.apiKey, baseUrl: cfg.baseUrl });
    } catch (err) {
        console.error('  FALLITO: ' + err.message);
        process.exitCode = 1;
        return;
    }
    console.log('  offerti: ' + (available.length > 0 ? available.join(', ') : '(elenco vuoto)'));
    if (!available.includes(cfg.model)) {
        console.error(
            '  ATTENZIONE: "' + cfg.model + '" non compare fra i modelli offerti. ' +
                'Nessuna sostituzione automatica: correggi DEEPSEEK_MODEL o l argomento --model.'
        );
        process.exitCode = 1;
        return;
    }
    console.log('  "' + cfg.model + '" e disponibile.');

    console.log('\n[2/2] POST /chat/completions — una sola richiesta minima');
    let response;
    try {
        response = await chatCompletion({
            apiKey: cfg.apiKey,
            baseUrl: cfg.baseUrl,
            model: cfg.model,
            messages: [{ role: 'user', content: 'Rispondi con la sola parola: pronto' }],
            // 8 token non bastano: il modello chiude per `length` prima di emettere il testo.
            maxTokens: 64,
            maxRetries: 0,
            requestTimeoutMs: 60000
        });
    } catch (err) {
        console.error('  FALLITO: ' + (err instanceof DeepSeekApiError ? 'HTTP ' + err.status + ' — ' : '') + err.message);
        process.exitCode = 1;
        return;
    }

    const choice = response.choices?.[0];
    const usage = readUsage(response);
    console.log('  modello usato dall API : ' + (response.model ?? '(non riportato)'));
    console.log('  finish_reason          : ' + (choice?.finish_reason ?? '(assente)'));
    console.log('  risposta               : ' + JSON.stringify(choice?.message?.content ?? ''));
    if (usage) {
        console.log(
            '  token                  : prompt ' + usage.promptTokens +
                ', completion ' + usage.completionTokens +
                ', totale ' + usage.totalTokens
        );
    }
    console.log('\nCollegamento verificato con ' + (response.model ?? cfg.model) + '.');
}

main().catch((err) => {
    console.error('ERRORE NON GESTITO: ' + err.message);
    process.exitCode = 1;
});
