# Sessione DeepSeek del 2026-09-08 — registro

**Architetto:** Claude (sessione `nosaiproject-04`).
**Programmatore:** DeepSeek, modello `deepseek-v4-flash`.
**Avvio:** 2026-09-08 03:46:13 UTC (05:46 locale).
**Scadenze:** nessun nuovo incarico dopo le 10:16 locali; fine assoluta 10:46.
**Spesa:** nessun budget monetario; credito DeepSeek esistente, consumo misurato
per chiamata. Nessuna ricarica, nessun acquisto.

## Perimetro isolato

Directory Git dedicata, per non interferire con la sessione peer
`nosaiproject-09` che lavora nel checkout principale:

- worktree `C:\Users\volob\Desktop\NosAiProject-sessione-deepseek`
- ramo `sessione-deepseek-q140`, base `35f99a3` (`s8-unequip-by-click`)
- catture `data/*.noscap` (8 file, ignorate da git) copiate dal checkout
  principale, perche' servono alla verifica e un worktree non le eredita.

## Baseline misurata prima di qualunque modifica

| Controllo | Comando | Esito |
|---|---|---|
| Build | `dotnet build NosAi.sln -c Release` | **0 errori, 0 avvisi** |
| Test Runtime | `dotnet test tests/NosAi.Runtime.Tests -c Release --no-build` | **2720 superati, 2 falliti, 9 ignorati, 2731 totali**, 1 m 34 s |

Falliti di baseline, da non confondere con una regressione:
`GuardAdmissionTests.ASilentPeerIsDroppedByTheAdmissionDeadline_NotTheHeartbeatOne`
(`Assert.False`, test a orologio da parete) piu' un secondo non catturato
nell'output troncato. **Confrontare i nomi dei falliti, mai i totali.**

## Coda scelta

Quattro incarichi gia' preparati e ordinati per dipendenze, presi da
`docs/agents/EXECUTION_QUEUE.md` (unici in stato `PRONTO`):

| Q | Fase | Contenuto | Dipendenza | Stato |
|---|---|---|---|---|
| Q-140 | AP-05 | `out` come evento distinto dalla morte; vnum abilita' da `su[5]` e `ct[7]`; vista cronologica in `--wire-inspect` | — | **BLOCCATO (canale)** |
| Q-143 | AP-02 | quattro punti in cui il pannello dice il falso | parallelo a Q-140 | non avviato |
| Q-144 | AP-02 | Percezione/Mappa/Bersaglio, cinque fatti storti | parallelo | non avviato |
| Q-142 | AP-05 | il catalogo delle abilita' incontra il filo | dopo Q-140 | non avviato |

Nessun ampliamento: la coda e' quella gia' prevista dal progetto.

## Blocco — causa esatta

L'incarico Q-140 e' stato formulato per intero e invocato davvero con
`delegate_to_deepseek`. Il server MCP ha risposto, quindi **il trasporto e'
vivo**, ma ha rifiutato con la propria validazione:

```
Configurazione non valida: DEEPSEEK_API_KEY is not set in the server
process environment. Set it in the shell that launches Claude Code,
then restart the session.
```

Misurato, non supposto:

- `DEEPSEEK_API_KEY` **assente** in tutti e tre gli ambiti Windows —
  processo, utente, macchina (`[Environment]::GetEnvironmentVariable`).
- **Assente dal disco**: nessun `.env`, nessun `.cursor/mcp.json`;
  `.claude/settings.local.json` nomina `deepseek` solo per abilitare il
  server MCP di progetto, e non contiene chiavi.
- `tools/deepseek-mcp/src/config.mjs:93` legge `env.DEEPSEEK_API_KEY` dal
  proprio `process.env` a ogni chiamata.

Perche' non e' rimediabile da questa sessione: il server MCP e' un processo
figlio avviato da Claude Code all'apertura della sessione. Una variabile
impostata ora da shell non entra nell'ambiente di un processo gia' in corso, e
nessuno strumento disponibile puo' iniettarla. **Serve un riavvio di Claude Code
con la chiave nell'ambiente.**

La sessione precedente funzionava perche' la chiave era esportata nella shell
che l'aveva avviata: era transitoria, ed e' morta con quella shell.

## Rimedio, una riga

In PowerShell, con il valore reale al posto del segnaposto:

```powershell
[Environment]::SetEnvironmentVariable('DEEPSEEK_API_KEY','<la-chiave>','User')
[Environment]::SetEnvironmentVariable('DEEPSEEK_MODEL','deepseek-v4-flash','User')
```

Poi **chiudere e riaprire Claude Code** in
`C:\Users\volob\Desktop\NosAiProject`. L'ambito `User` la rende persistente,
cosi' il guasto non si ripete al prossimo avvio.

Verifica dopo il riavvio, senza spendere token di lavoro:

```powershell
node tools/deepseek-mcp/scripts/check-connection.mjs
```

## Consumi

Una sola chiamata DeepSeek in questa sessione, ed e' quella della prova di
collegamento precedente: 9244 token totali (prompt 8388, completion 856).
L'invocazione di Q-140 e' stata **rifiutata prima di raggiungere l'API**, quindi
non ha consumato nulla.

## Cosa e' pronto per la ripresa

Il worktree, il ramo, le catture e la baseline restano in piedi. L'incarico
Q-140 e' gia' scritto per intero (obiettivo, stato di partenza, riferimenti
`file:riga` confermati, perimetro, vincoli, casi di errore, quindici criteri di
accettazione, materiale da restituire) e va solo reinviato.
