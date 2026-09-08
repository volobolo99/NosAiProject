#!/usr/bin/env node
/**
 * Starts the server exactly the way .mcp.json does — `node tools/deepseek-mcp/src/server.mjs`
 * from the repository root — speaks MCP to it over stdio, and lists the tools.
 *
 * This is the check that the registered command line actually works, as opposed
 * to the in-memory transport the unit tests use. It performs no API call.
 */

import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { Client } from '@modelcontextprotocol/client';
import { StdioClientTransport } from '@modelcontextprotocol/client/stdio';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '..', '..', '..');
const serverArg = 'tools/deepseek-mcp/src/server.mjs';

async function main() {
    console.log('cwd     : ' + repoRoot);
    console.log('comando : node ' + serverArg);

    const transport = new StdioClientTransport({
        command: process.execPath,
        args: [serverArg],
        cwd: repoRoot,
        stderr: 'pipe'
    });
    const client = new Client({ name: 'nosai-registration-check', version: '1.0.0' });

    await client.connect(transport);
    try {
        const info = client.getServerVersion?.();
        if (info) console.log('server  : ' + info.name + ' ' + info.version);
        const { tools } = await client.listTools();
        console.log('strumenti pubblicati: ' + tools.length);
        for (const tool of tools) {
            console.log('  - ' + tool.name);
            console.log('    campi obbligatori: ' + (tool.inputSchema.required ?? []).join(', '));
            const models = tool.inputSchema.properties?.model?.enum;
            if (models) console.log('    modelli ammessi  : ' + models.join(', '));
        }
        const ok = tools.length === 1 && tools[0].name === 'delegate_to_deepseek';
        console.log('\n' + (ok ? 'Registrazione stdio verificata.' : 'ATTESO un solo strumento delegate_to_deepseek.'));
        if (!ok) process.exitCode = 1;
    } finally {
        await client.close();
    }
}

main().catch((err) => {
    console.error('FALLITO: ' + err.message);
    process.exitCode = 1;
});
