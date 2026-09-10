# NosAiProject — Protocollo di coordinamento multi-agente

**Scopo:** permettere a un modello Direttore/CTO di distribuire il lavoro a modelli specialisti con contesto breve, ownership disgiunta, contratti verificabili e integrazione controllata.

## 1. Precedenza delle istruzioni

In caso di conflitto usare questo ordine:

1. `.claude/CLAUDE.md` per le regole operative degli agenti;
2. `docs/SOURCE_OF_TRUTH.md` per architettura e prodotto;
3. ADR accettati in `docs/adr/`;
4. `contracts/ledger.json` e `docs/CONTRACT_MAP.md` per stato e firme;
5. `docs/agents/EXECUTION_QUEUE.md` per il prossimo task;
6. documenti di fase e note storiche.

Un modello non può risolvere un conflitto inventando una scelta: deve aprire un ADR o restituire `blocked`.

## 2. Ruoli e autorità

| Ruolo | Compito | Può modificare codice | Può promuovere su `main` |
|---|---|---:|---:|
| Direttore/CTO | scomposizione, dipendenze, routing, decisioni e integrazione | solo secondo le regole di `.claude/CLAUDE.md` | no, richiede gate |
| MCP Chief | watchdog salute/configurazione MCP, proposte di miglioramento, binding e rollback | solo aree non protette tramite proposta | no; promozione richiede test, shadow, audit e conferma operatore |
| Contract agent | firme, tipi, invarianti, schema e test contract | sì nel proprio perimetro | no |
| Coding agent | implementazione dei corpi e test del task assegnato | sì solo nei file assegnati | no |
| Testing agent | test, riproduzione, classificazione dei fallimenti | test e fixture assegnati | no |
| Security/Auditor | invarianti, segreti, confini, veto e rollback | audit e patch autorizzate | veto sì; merge no |
| Documentation agent | indice, report, changelog, ledger e mappe | documentazione assegnata | no |
| Integrator | verifica diff, test aggregati, aggiornamento ledger e merge | sì sui file di integrazione | sì dopo tutti i gate |

Il Direttore MCP può proporre modifiche al proprio server, ma non può modificare o disattivare policy, audit, test di sicurezza, contratti di governance o meccanismi di rollback. Il **MCP Chief** espone controlli su richiesta, binding staged ed evidenze firmate. La supervisione persistente è progettata in LAB-03 e non è certificata dal solo health endpoint. Non ha autorità di esecuzione di gioco, override o esportazione segreti; l’Auditor indipendente mantiene veto e rollback.

## 3. Pacchetto minimo di un incarico

Ogni incarico è un oggetto conforme a `schemas/agent_message.schema.json`:

```json
{
  "task_id": "Q-000",
  "objective": "una riga verificabile",
  "input": "fatti osservati e versione del codice",
  "expected_output": "file e risultato attesi",
  "files": ["percorso/owned.cs", "tests/owned_tests.cs"],
  "dependencies": ["C-000", "Q-previous"],
  "risks": ["rischio concreto"],
  "required_tests": ["comando esatto"],
  "status": "pending",
  "model": "deepseek-v4-flash",
  "summary": "",
  "confidence": 0.0
}
```

Il Direttore deve inviare soltanto il contesto necessario: CID, file, firme, invarianti, dipendenze, test e vincoli. Non inviare l’intera cronologia o l’intero repository.

## 4. Ciclo di vita

```
DISCOVER → CONTRACT → SKELETON → IMPLEMENT → PREFLIGHT
→ TEST → AUDIT → INTEGRATE → DOCUMENT → MERGE
```

- **DISCOVER:** trova CID, file canonico e test in `docs/CONTRACT_MAP.md`.
- **CONTRACT:** se manca un contratto, scriverlo prima del codice.
- **SKELETON:** creare solo firme e tipi approvati.
- **IMPLEMENT:** il coding agent riempie i corpi senza alterare le firme.
- **PREFLIGHT:** confrontare codice e contratto.
- **TEST:** eseguire test positivi, negativi, deterministici e di confine.
- **AUDIT:** un agente indipendente cerca regressioni, bypass e dati inventati.
- **INTEGRATE:** l’integratore risolve conflitti e ripete la suite interessata.
- **DOCUMENT:** aggiornare ledger, indice, changelog e status.
- **MERGE:** solo con evidenza registrata.

Stati validi: `pending`, `in_progress`, `done`, `blocked`. Per i contratti usare anche `DRAFT`, `MERGED`, `TEST_VERIFIED`, `BLOCKED` e `DROPPED` secondo il vocabolario in `docs/PROTOCOL_TOKENS.md`.

## 5. Distribuzione e parallelismo

Il Direttore costruisce un grafo aciclico:

- task con file disgiunti possono partire in parallelo;
- task che modificano lo stesso contratto, schema o composition root sono seriali;
- un task non parte se una dipendenza è `BLOCKED`;
- l’integratore è l’unico punto che aggiorna lo stato globale dopo l’audit;
- ogni agente deve dichiarare i file toccati prima di iniziare;
- merge e aggiornamenti dello stesso file non vengono eseguiti in parallelo.

Ownership di default:

| Area | Owner primario | Revisore |
|---|---|---|
| `src/NosAi.Core`, contratti World Model | Contract agent | Security/Auditor |
| `src/NosAi.Runtime` Gate 1–4 | Coding agent | Integrator + Auditor |
| `src/NosAi.Security`, protocollo, segreti | Security agent | Auditor indipendente |
| `nosai/mcp`, `scripts/mcp_*` | MCP coding agent | Auditor MCP |
| `tests/` | Testing agent | Integrator |
| `docs/`, `contracts/`, `schemas/` | Documentation agent | Direttore |
| `.github/workflows/` | Integrator | Security agent |

## 6. Handoff obbligatorio

Ogni agente restituisce:

- stato e `task_id`;
- elenco esatto dei file creati/modificati;
- CID coinvolti;
- comandi eseguiti e risultato;
- deviazioni dal contratto;
- rischi residui;
- confidence 0–1;
- richiesta successiva, oppure motivo `blocked`.

Un risultato senza test, file o motivazione non è integrabile. Un agente non può dichiarare `done` se ha lasciato placeholder, stub, TODO o test saltati senza motivo.

## 7. Audit e rollback

L’Auditor deve verificare:

- firme e schema invariati;
- dati provenienti da fonti osservabili;
- preservazione di `UNKNOWN`;
- timeout, cancellazione e code limitate;
- nessun bypass di Guard/Trust/Safety;
- nessun segreto in output, log o commit;
- test positivi e negativi;
- compatibilità con i moduli adiacenti.

Se un controllo fallisce:

1. stato `BLOCKED`;
2. registrazione dell’errore nel report;
3. massimo numero di correzioni previsto dal runbook;
4. rollback al checkpoint precedente se il difetto persiste;
5. nessuna promozione forzata.

## 8. Percorso rapido di ricerca

```bash
rg -n "CID|NomeTipo|NomeMetodo" docs contracts src nosai tests
rg -n "TODO|FIXME|TBD|XXX|NotImplementedError" .
python -m compileall -q nosai
python -m pytest -q tests
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
```

Ordine di lettura per un nuovo task:

`SOURCE_OF_TRUTH → CONTRACT_MAP → SYSTEM_MAP → EXECUTION_QUEUE → file/test → ADR`.

## 9. Supervisione continua del MCP Chief

Il Chief espone `mcp_chief_health` e gli endpoint dashboard `/api/mcp/chief`. Il controllo è read-only e può essere eseguito periodicamente dal processo operativo o da un monitor esterno. Le modifiche passano sempre da proposta → shadow/test → audit indipendente → conferma operatore → promozione atomica. Se un controllo fallisce, il Chief raccomanda sospensione o rollback; non può auto-rimuovere i propri guardrail.

## 10. Stato dell’automazione

Il repository contiene il protocollo MCP per proposta, audit e rollback e la coda documentale per il coordinamento. La distribuzione completamente autonoma di task verso provider esterni resta una capacità da verificare in ambiente operativo: la documentazione non la considera già funzionante solo perché esistono `director.py` o gli schemi.

## 11. Definition of Done del coordinamento

Un task è chiuso solo quando:

- contratto e ownership sono presenti;
- implementazione e test sono nel perimetro;
- preflight e audit sono registrati;
- il modulo comunica con i dipendenti adiacenti;
- ledger, mappa, queue e changelog sono coerenti;
- CI o il comando locale dichiarato è stato eseguito;
- eventuali limiti real-environment sono esplicitamente marcati.

## Programmazione Chief e Research Lab

Entrata per coordinatore e worker: [AI_PROGRAMMING_GUIDE.md](mcp/AI_PROGRAMMING_GUIDE.md).
I pacchetti LAB estendono questo protocollo con ownership, dipendenze e criteri mirati.
LAB-01 (evidenze) e LAB-02 (stato transazionale) sono presenti, ma non autorizzano
rilasci autonomi. La roadmap futura prevede rilasci nel mandato senza conferme
ripetute; il runtime non va considerato dotato di tale autorità prima della chiusura
di LAB-03..LAB-07.
