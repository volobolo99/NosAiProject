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
| `assistant_message` | cosa il lavoratore ha ragionato (`reasoning`) e cosa ha detto (`text`) in quel giro, con il conteggio dei caratteri e se e' stato troncato |
| `worker_report` | quanti criteri dichiarati soddisfatti e quanti blocchi |
| `delegation_end` | stato finale, giri, chiamate, rifiuti, durata, token totali |
| `delegation_refused` | delega respinta prima di partire (ricorsione, configurazione) |
| `setup_error` | perimetro non valido: nessuna chiamata API e' stata fatta |

Il registro contiene le parole del lavoratore — ragionamento e testo di ogni
giro — perche' servono a capire *perche'* ha fatto quello che ha fatto. Non
contiene mai chiavi, contenuti dei file scritti, o i messaggi di sistema.
`reasoning_content` esiste solo sui modelli che lo espongono: dove manca,
l'evento porta il solo `text`, e la sua assenza non e' un errore.

Le due cose restano separate per costruzione: `assistant_message` e'
un'intenzione, `tool_call` e `file_change` sono quello che e' successo davvero.
Leggerle come una cosa sola e' il modo in cui un piano si scambia per una
modifica su disco.

Seguirlo in diretta:

```powershell
Get-Content -Wait -Tail 20 "C:\Users\volob\Desktop\NosAiProject\tools\deepseek-mcp\logs\delegations.jsonl"
```

Una delega sola, leggibile:

```powershell
Get-Content "C:\Users\volob\Desktop\NosAiProject\tools\deepseek-mcp\logs\delegations.jsonl" | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $_.id -eq "<id>" } | Format-Table ts, ev, name, path, status
```

`NOSAI_DEEPSEEK_LOG=0` spegne tutto il registro; `NOSAI_DEEPSEEK_THOUGHTS=0`
tiene fuori solo ragionamento e testo, lasciando gli eventi;
`NOSAI_DEEPSEEK_LOG_DIR` ne sposta la cartella. Il file cresce a ogni delega e
non e' versionato. Sotto `node --test` il registro reale non viene mai scritto.

## Guardare DeepSeek al lavoro

`Get-Content -Wait` mostra il JSON grezzo. Per leggerlo come lo leggerebbe una
persona — un giro per volta, con il ragionamento indentato sotto la sua riga e
il file toccato accanto allo strumento che l'ha toccato:

```powershell
node "C:\Users\volob\Desktop\NosAiProject\tools\deepseek-mcp\scripts\watch.mjs" --last
```

| Argomento | Cosa fa |
|---|---|
| *(nessuno)* | segue solo cio' che accade da adesso; se il registro non esiste, aspetta la prima delega |
| `--last` | ristampa l'ultima delega dall'inizio, poi segue |
| `--all` | tutto il registro, poi segue |
| `--id <id>` | una delega sola, dal suo inizio |
| `--no-follow` | stampa e termina |
| `--no-color`, `--file <percorso>` | senza colori; su un registro diverso da quello predefinito |

Il visore e' un programma a se': legge il file e nient'altro. Non parla con
l'API, non tocca il repository, e non puo' influenzare una delega in corso.

Esempio di una delega bloccata dal perimetro:

```
08:31:02 DELEGA d20260908063102-a1b2
          modello deepseek-v4-pro (da DEEPSEEK_MODEL)
          perimetro src/NosAi.Runtime/Tactical/**, tests/NosAi.Runtime.Tests/**
08:31:09   <- giro 1: 7.2 s   fine=tool_calls   strumenti chiesti 2   token 4.528 (cache hit 3.840)
08:31:09 giro 1 — RAGIONA (176 caratteri)
          | Non posso scrivere l'esecutore senza aver letto ClickTargetExecutor.
08:31:09   USA read_file  src/NosAi.Runtime/Tactical/ClickTargetExecutor.cs
08:31:09   USA read_file  src/NosAi.Runtime/Perception/InventoryPanelRoiCalibration.cs   RIFIUTATO — ERROR [OUT_OF_SCOPE]
08:33:40   FILE created  src/NosAi.Runtime/Tactical/UnequipExecutor.cs   0 -> 14.203 B
08:33:40 FINE blocked   2 giri, 4 strumenti (1 rifiutati), 1 file, 158.7 s, token 13.902
```

## Il quadro delle deleghe

Il visore risponde a «cosa sta succedendo adesso», un evento per volta. Dopo
esserti allontanato, o quando piu' sessioni hanno delegato insieme e il registro
le ha intrecciate, la domanda e' un'altra: «cosa e' successo, e cosa e' ancora
aperto». Il rapporto raggruppa il registro per delega — la piu' recente in alto —
e scrive una pagina che si apre da disco:

```powershell
node "C:\Users\volob\Desktop\NosAiProject\tools\deepseek-mcp\scripts\report.mjs"
```

| Argomento | Cosa fa |
|---|---|
| *(nessuno)* | scrive `logs/report.html` |
| `--markdown` | scrive `logs/report.md`, da incollare in un documento |
| `--stdout` | stampa invece di scrivere |
| `--out <percorso>` | sceglie il file da scrivere |
| `--file <registro>` | legge un registro diverso da quello predefinito |

Ogni delega diventa una scheda: modello, cartella, perimetro, tetti, giri,
strumenti (con i rifiutati), file toccati, token, durata, incarico. Il bordo
dice lo stato — verde in corso, ambra ferma da un po', grigio conclusa, rosso
rifiutata o fallita.

Una delega senza evento di chiusura e' riportata come aperta, mai come fallita:
il registro dice cio' che e' stato scritto, e il silenzio non e' un esito. Una
che non parla da due minuti e' segnata «ferma da», che e' un'osservazione, non
un verdetto.

La pagina e' un file solo: nessuno script, nessun carattere o foglio di stile da
scaricare. Serve perche' si apre spesso mentre una delega e' ancora in corso, e
una pagina che dipende da qualcosa che non riesce a raggiungere e' una pagina
che mente sullo stato del lavoro. Non si aggiorna da sola: e' un'istantanea, e
va rigenerata per vedere il seguito. Tutto cio' che non e' un numero — incarichi,
percorsi, messaggi d'errore — viene scritto dal modello o preso dal filesystem,
quindi finisce nella pagina come testo e mai come marcatura.

## La pagina viva

`report.mjs` produce un'istantanea da archiviare, senza script dentro.
`dashboard.mjs` fa la cosa opposta: una pagina che **si aggiorna da sola** e
mostra la cronologia completa di ogni delega — giri, ragionamento, strumenti,
file — con il diario di tutti gli agenti in basso a sinistra.

```powershell
node "C:\Users\volob\Desktop\NosAiProject\tools\deepseek-mcp\scripts\dashboard.mjs"
```

Poi apri <http://127.0.0.1:7717>. La pagina interroga `/api/state` ogni secondo
e mezzo: le deleghe aperte hanno un punto che pulsa, quelle ferme da oltre due
minuti lo dicono — «ferma» non e' «fallita», e la pagina non decide al posto di
chi legge.

| Argomento | Cosa fa |
|---|---|
| `--port <n>` | porta diversa dalla 7717 |
| `--export <file.html>` | scrive un'istantanea della pagina viva e termina |
| `--file <registro>`, `--activity <logact.md>` | sorgenti diverse da quelle predefinite |

Il server ascolta **solo** su `127.0.0.1`, serve la pagina e il proprio JSON e
nient'altro. Come gli altri due visori legge i file e basta: non parla con
l'API, non tocca il repository, non puo' influenzare una delega in corso.

## Il diario in Markdown

Ogni evento viene appeso anche a `logact.md` nella radice del repository, in
Markdown: aprilo in anteprima nell'editor e lo vedi crescere mentre gli agenti
lavorano.

```
### 11:00:00 · deepseek-v4-pro · `aaaa`

**Perimetro** `src/NosAi.Runtime/Tactical/**`
**Tetti** 24 giri · 60 strumenti · 600 s

- 11:00:07 `aaaa` giro 1 — 7.2 s, fine `tool_calls`, token 4.528
- 11:00:07 `aaaa` **ragiona** (78 caratteri)
  > Leggo ClickTargetExecutor prima di scrivere.
- 11:00:08 `aaaa` `read_file` `src/NosAi.Runtime/Tactical/ClickTargetExecutor.cs`
- 11:02:39 `bbbb` `read_file` — **rifiutato**: fuori perimetro
```

Piu' agenti scrivono nello stesso file nello stesso momento: ogni riga porta le
ultime quattro lettere dell'id della delega che l'ha scritta, cosi' due lavori
paralleli restano distinguibili. `NOSAI_ACTIVITY_MD` sposta il file, `=0` lo
disattiva. Il diario e' un di piu': se la sua scrittura fallisce si spegne da
solo, senza toccare il registro JSON ne' la delega.
