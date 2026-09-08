# nosai-deepseek — collegamento MCP locale fra Claude e DeepSeek

Server MCP locale che espone un solo strumento, `delegate_to_deepseek`. Claude
formula l'incarico e il perimetro; DeepSeek lo esegue tramite l'API ufficiale con
una superficie di strumenti limitata ai file, confinata alla directory di lavoro.
Nessun copia-incolla.

Claude resta architetto e revisore: build, test e integrazione restano a Claude,
che ha già quei permessi. Il lavoratore non ha shell.

## Prerequisiti

Il processo eredita dall'ambiente:

| Variabile | Obbligatoria | Valore |
|---|---|---|
| `DEEPSEEK_API_KEY` | sì | letta a runtime, mai stampata né scritta su file |
| `DEEPSEEK_BASE_URL` | no | default `https://api.deepseek.com` |
| `DEEPSEEK_MODEL` | no | `deepseek-v4-flash` (default) oppure `deepseek-v4-pro` |

Un `DEEPSEEK_MODEL` diverso da quei due valori è un errore esplicito: il server
non sostituisce nulla e non passa mai a `pro` da solo, nemmeno dopo un fallimento.

Node ≥ 20 (`engines`). Dipendenze: `npm install` in questa cartella.

## Caricare il server in Claude Code

La registrazione è già in `.mcp.json` alla radice del repository, ambito di
progetto:

```json
{ "mcpServers": { "deepseek": { "command": "node", "args": ["tools/deepseek-mcp/src/server.mjs"] } } }
```

Equivalente da riga di comando, se serve rifarla:

```powershell
claude mcp add deepseek --scope project -- node tools/deepseek-mcp/src/server.mjs
```

**Un server di progetto richiede l'approvazione al primo uso e una sessione
nuova**: dopo la registrazione, riavvia Claude Code nella cartella del repository
e approva `deepseek` quando lo chiede. `claude mcp list` deve mostrarlo connesso.

## Argomenti di `delegate_to_deepseek`

| Campo | Obbl. | Significato |
|---|---|---|
| `task` | sì | l'incarico: obiettivo, stato di partenza, riferimenti confermati, casi limite |
| `workingDirectory` | sì | directory esistente a cui il lavoratore è confinato |
| `allowedPaths` | sì | glob relativi che delimitano ogni lettura e scrittura |
| `acceptanceCriteria` | sì | criteri osservabili, passati al lavoratore alla lettera |
| `context` | no | riferimenti confermati dall'architetto (`file:riga`, contratti, decisioni) |
| `model` | no | `deepseek-v4-flash` o `deepseek-v4-pro`; prevale su `DEEPSEEK_MODEL` |
| `readOnly` | no | il lavoratore ispeziona e riferisce, non scrive |
| `maxApiRounds` | no | default 24, tetto 80 |
| `maxToolCalls` | no | default 60, tetto 300 |
| `timeoutSeconds` | no | default 600, tetto 3600 |
| `maxRetries` | no | default 2, tetto 5 |

Ritorna: stato, modello e origine della scelta, giri e chiamate consumati,
**le modifiche realmente presenti su disco** (confronto SHA-256 prima/dopo, così
una riscrittura identica risulta `unchanged` e non `modified`), chiamate
rifiutate, errori e token riportati dall'API. Il consumo di token **non** è un
tetto di spesa garantito: è ciò che l'API ha dichiarato per quelle chiamate.

## Confine

Il lavoratore ha sei strumenti — `list_files`, `search_files`, `read_file`,
`write_file`, `edit_file`, `report_done` — e nient'altro.

- Ogni percorso è risolto attraverso link e giunzioni **prima** del controllo di
  contenimento: un collegamento che punta fuori dalla radice è rifiutato anche se
  il percorso lessicale sembra interno.
- `.git/**`, `**/node_modules/**`, `**/bin/**`, `**/obj/**` sono sempre negati,
  qualunque cosa dicano gli `allowedPaths`.
- Niente shell, niente processi, niente rete, niente build, niente test.
- Nessuna delega ricorsiva: il lavoratore non ha alcuno strumento che raggiunga
  questo server, e il server rifiuta di partire dentro una delega già attiva
  (`NOSAI_DEEPSEEK_DELEGATION_ACTIVE=1`).

## Verifiche

```powershell
npm test                          # 100 test, API simulata
node scripts/check-registration.mjs   # avvia il server su stdio come fa .mcp.json
node scripts/check-connection.mjs     # una richiesta reale minima; --model per forzare il modello
```

`check-connection` risolve il modello, conferma con `GET /models` che l'account
lo offre davvero, poi spende una sola completion minima. Stampa presenza e
lunghezza della chiave, mai il valore.

## Incarico dimostrativo (cartella temporanea, innocuo)

Prepara la cartella:

```powershell
$demo = Join-Path $env:TEMP "nosai-deepseek-demo"
New-Item -ItemType Directory -Force $demo | Out-Null
New-Item -ItemType Directory -Force (Join-Path $demo "src") | Out-Null
Set-Content -Encoding utf8 (Join-Path $demo "src\greeting.txt") "riga uno`nriga due`n"
Write-Output $demo
```

Poi, in Claude Code, chiedi la delega con questi argomenti — nessun file del
progetto è nel perimetro:

- `workingDirectory`: il percorso stampato sopra
- `allowedPaths`: `["src/**"]`
- `task`: «Leggi `src/greeting.txt`. Aggiungi in fondo la riga `riga tre`,
  lasciando invariate le due righe esistenti e l'a capo finale. Non creare altri
  file.»
- `acceptanceCriteria`: `["src/greeting.txt contiene esattamente tre righe",
  "la terza riga e 'riga tre'", "nessun altro file e stato creato o modificato"]`

Verifica dopo l'esecuzione:

```powershell
Get-Content (Join-Path $env:TEMP "nosai-deepseek-demo\src\greeting.txt")
Get-ChildItem -Recurse (Join-Path $env:TEMP "nosai-deepseek-demo")
```

Il rapporto restituito deve elencare `modified  src/greeting.txt` e nessun altro
file. Se elenca `unchanged`, il lavoratore ha riscritto byte identici: non è una
modifica.

## Registro operativo

Ogni delega scrive eventi in `tools/deepseek-mcp/logs/delegations.jsonl`, una
riga JSON per evento, in aggiunta al rapporto finale che il server restituisce a
Claude. Il file e' l'unica via per seguire una delega **mentre accade**: il
trasporto e' stdio, quindi su stdout passa solo JSON-RPC e nient'altro.

Ogni riga porta `ts` (UTC), `id` (identificativo della delega) ed `ev`:

| `ev` | Cosa registra |
|---|---|
| `delegation_start` | modello e origine, cartella di lavoro, perimetro, tetti, estratto dell'incarico |
| `api_request_start` / `api_request_end` | inizio e fine di ogni giro API, con durata, `finish_reason`, strumenti richiesti e token del giro |
| `api_request_error` | giro fallito, con stato ed errore |
| `tool_call` | strumento invocato, file o pattern interessato, esito, motivo del rifiuto |
| `file_change` | file toccato davvero: azione e byte prima/dopo |
| `worker_report` | quanti criteri dichiarati soddisfatti e quanti blocchi |
| `delegation_end` | stato finale, giri, chiamate, rifiuti, durata, token totali |
| `delegation_refused` | delega respinta prima di partire (ricorsione, configurazione) |
| `setup_error` | perimetro non valido: nessuna chiamata API e' stata fatta |

Il registro non contiene mai chiavi, contenuti dei file, testo dei messaggi o
ragionamento del modello: solo eventi verificabili.

Seguirlo in diretta:

```powershell
Get-Content -Wait -Tail 20 "C:\Users\volob\Desktop\NosAiProject\tools\deepseek-mcp\logs\delegations.jsonl"
```

Una delega sola, leggibile:

```powershell
Get-Content "C:\Users\volob\Desktop\NosAiProject\tools\deepseek-mcp\logs\delegations.jsonl" | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $_.id -eq "<id>" } | Format-Table ts, ev, name, path, status
```

`NOSAI_DEEPSEEK_LOG=0` lo spegne; `NOSAI_DEEPSEEK_LOG_DIR` ne sposta la cartella.
Il file cresce a ogni delega e non e' versionato.
