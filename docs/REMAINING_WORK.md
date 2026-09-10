# NosAiProject — Lavoro residuo e criteri di completamento

**Scopo:** elenco operativo delle attività ancora necessarie. Non usare la percentuale globale come prova di funzionamento: ogni voce richiede l’evidenza indicata.

## Priorità P0 — sbloccare la verifica

| ID | Attività | Dipendenze | Evidenza di chiusura |
|---|---|---|---|
| R-001 | Rendere verdi le GitHub Actions e acquisire i log dei job falliti | accesso ai log/runner | run `NosAi CI` e Windows completate senza failure oppure failure spiegata e registrata |
| R-002 | Decidere quale stack possiede lo stato canonico (C# o Python) | C-204 | ADR accettato, schema di sincronizzazione, test di round-trip e ownership esplicita |
| R-003 | Verificare l’esecuzione reale del MCP Director → worker → Auditor | MCP Hub, policy, provider | replay di un task in shadow mode, handoff valido, veto/rollback dimostrati |
| R-004 | Chiudere la specifica di rilascio end-to-end | C-404 | build pulita, test, artefatti e checklist registrati |

## Priorità P1 — colmare i blocchi funzionali

| ID | Attività | Riferimento | Evidenza |
|---|---|---|---|
| R-101 | Fornire asset/back-end ML per OCR, object detection e tracking | AP-02/Q-014 | cattura reale con confidence/provenance e test su replay |
| R-102 | Completare dati reali di skill, danno, costo e cooldown | AP-05 | simulazione e ranking confrontati con catture reali |
| R-103 | Validare combattimento su client reale | Q-038/Q-039 | esecuzione autorizzata, receipt e post-condition su Windows |
| R-104 | Completare canale e verifica equip/unequip/upgrade | AP-07, Q-089 | primitive di azione, verifica prima/dopo e test negativi |
| R-105 | Completare fonti reali per mob/NPC/dialogue/interact/deliver | AP-06 | obiettivi quest grounded e completamento multi-step |
| R-106 | Eseguire la certificazione di autonomia su hardware reale | AP-10 | report con evidenza per ogni stadio, non solo codice presente |

## Priorità P2 — hardening e decisioni architetturali

| ID | Attività | Riferimento | Decisione richiesta |
|---|---|---|---|
| R-201 | Decidere se introdurre codice C/C++ e quindi ASan/ctypes | C-003–C-005 | aprire ramo nativo oppure riscrivere la FASE 5 sugli stack reali |
| R-202 | Decidere se una FSM sostituisce o affianca Planner/Orchestrator | C-304 | un’unica autorità di decisione, ADR e test |
| R-203 | Decidere l’eventuale hook memoria/DLL | C-105 | contratto, limiti e test; nessuna implementazione implicita |
| R-204 | Aggiungere fuzzing ai parser di input | C-402 | corpus, harness, esecuzione riproducibile e report |
| R-205 | Completare la cascata free-first del MCP | `scripts/free_first.py`, docs/mcp | test provider, fallback, quote, timeout e funzionamento reale |

## Ordine consigliato

```
R-001 → R-002 → R-003
      ↘ R-101/R-102/R-104/R-105 in parallelo su file disgiunti
R-004 → R-106 → rilascio
R-201/R-202/R-203 prima di introdurre nuove autorità o nuovi stack
```

## Regola di avanzamento

Una voce passa a `DONE` solo con:

- contratto/CID collegato;
- file e test reali;
- comando eseguito;
- risultato registrato;
- audit indipendente quando cambia sicurezza o autorità;
- aggiornamento di ledger, queue e documentazione.

Se l’evidenza dipende da un client Windows, provider online, GPU o hardware non disponibile, lo stato resta `BLOCKED` o `PRESENT`, mai `VERIFIED`.

## Collegamenti

- Mappa contratti: `docs/CONTRACT_MAP.md`
- Mappa sistema: `docs/SYSTEM_MAP.md`
- Coordinamento agenti: `docs/AGENT_COORDINATION.md`
- Coda attiva: `docs/agents/EXECUTION_QUEUE.md`
- Ledger: `contracts/ledger.json`
