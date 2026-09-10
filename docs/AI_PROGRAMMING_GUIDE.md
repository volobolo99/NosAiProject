# NosAiProject — Entrata per agenti programmatori
Questa guida organizza la lettura; non sostituisce roadmap, contratti o coda.
Versione prodotto invariata. Nessun stato di implementazione dedotto da questa guida.

## Percorso minimo
1. Una volta per sessione: AGENTS.md, .claude/CLAUDE.md, docs/SOURCE_OF_TRUTH.md.
2. Per trovare il lavoro: docs/agents/EXECUTION_QUEUE.md, voce pronta con dipendenze soddisfatte.
3. Per localizzare il dominio: [AI_DEVELOPMENT_MAP.json](AI_DEVELOPMENT_MAP.json).
4. Per capire il contratto: cercare CID nel ledger e nella CONTRACT_MAP, poi file/test.
5. Leggere solo il comando della coda e la sezione di dominio interessata.
6. Per integrazione MCP: [mcp/AI_PROGRAMMING_GUIDE.md](mcp/AI_PROGRAMMING_GUIDE.md)
   nel punto indicato in [AI_PROJECT_STEPS.md](AI_PROJECT_STEPS.md).
7. Per consegnare: [AI_DELIVERY_PROTOCOL.md](AI_DELIVERY_PROTOCOL.md).

## Fonti senza duplicazioni
| Domanda | Fonte |
|---|---|
| Quale task è prossimo? | agents/EXECUTION_QUEUE.md |
| Quale fase prodotto? | ROADMAP_ESECUTIVA.md |
| Chi comunica con chi? | SYSTEM_MAP.md |
| Quale contratto e quali blocchi? | CONTRACT_MAP.md e ../contracts/ledger.json |
| Quali file/simboli? | INDICE_REPO.md, poi ricerca nel dominio |
| Cosa manca? | REMAINING_WORK.md |
| Quali regole agenti? | ../AGENTS.md e ../.claude/CLAUDE.md |
| Come costruire e rilasciare? | BUILD_TEST_RELEASE.md |
| Chief/Research Lab? | mcp/AI_PROGRAMMING_GUIDE.md |

## Ricerca progressiva
Usare rg per un ID esatto e poi per un simbolo nel solo dominio:
```bash
rg -n 'Q-014' docs/agents/EXECUTION_QUEUE.md
rg -n 'C-204' contracts/ledger.json docs/CONTRACT_MAP.md
rg -n 'WorldModelFusionLoop' src/NosAi.Runtime/WorldModel tests
rg --files tests | rg 'WorldModel|Fusion'
```
Allargare a src/nosai/docs solo se il dominio non contiene risultati.
Non rileggere archivi, ricerca o third_party per ogni incarico.
Da third_party riusare solo dopo verifica di commit, licenza e dipendenze.
Il contratto completo non si ricostruisce dal nome del file.

## Disallineamenti da gestire
Le etichette AP del vecchio ledger non coincidono sempre con le AP della roadmap:
AP-03 nel ledger può indicare decisione, nella roadmap ricostruzione mappe.
Usare ID CID e scopo, mai associare automaticamente per numero.
La mappa contratti presenta intestazioni contratto/stato invertite rispetto ad alcune
righe: per leggere lo stato verificare il JSON del ledger.
C-204 proprietà C#/Python e C-304 pianificazione richiedono decisioni documentate.
La coda contiene istruzioni storiche di assegnazione diverse dalle regole permanenti:
risolvere la precedenza delle istruzioni applicabili prima di delegare, senza
riscrivere le regole o cambiare modelli implicitamente.
Le schede JSON sono un indice di lettura, non una nuova coda né un indice completo
delle funzioni. R-207 resta il lavoro per generare quest'ultimo.

## Indice funzioni e audit verificato

Consultare [FUNCTION_INDEX.md](FUNCTION_INDEX.md) per ricerca per file/simbolo,
source revision e copertura. [DOCUMENTATION_ALIGNMENT_AUDIT.md](DOCUMENTATION_ALIGNMENT_AUDIT.md)
elenca disallineamenti e 16 contratti con firme non precise. R-207 ha ora un indice
statico generato; restano revisione dei quattro errori parser e automazione della rigenerazione.
