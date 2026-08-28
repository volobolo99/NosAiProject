# Analisi del repository legacy `volobolo99/NosAi`

**Data analisi:** 2026-08-29  
**Target canonico:** `volobolo99/NosAiProject`  
**Versione progetto:** 1.0 Beta (immutata)

## Risultato

`volobolo99/NosAi` contiene una base molto più ampia e matura sul piano di test, osservabilità, valutazione e componenti AI rispetto al suo ruolo attuale di repository legacy. `NosAiProject`, però, possiede già un'architettura più strutturata per runtime, sicurezza, WorldState, percezione, orchestrazione, recovery, dashboard, persistenza e C#.

Per questo motivo **non viene effettuata una copia indiscriminata**. Le componenti vengono classificate in tre gruppi:

1. **Importare/adattare:** componenti isolate e non conflittuali.
2. **Riutilizzare come specifica:** idee, contratti, test strategy, evidence model e osservabilità.
3. **Non importare direttamente:** moduli che duplicano componenti già presenti o che dipendono da un'architettura differente.

## Componenti legacy di valore

### 1. Replay buffer — IMPORTATO

Fonte: `app/ai/replay_buffer.py`.

Il componente fornisce una struttura bounded per transizioni, sampling deterministico e persistenza JSONL. È stato adattato in `nosai/runtime/replay.py` con `ReplayTransition` e test dedicati in `tests/test_runtime_replay.py`.

Il componente è offline/observation-only e non esegue azioni sul client.

### 2. Brain pipeline — DA ADATTARE, NON COPIARE

Fonte: `app/ai/brain_pipeline.py`.

La pipeline legacy separa osservazione e decisione e normalizza mapping/dataclass/oggetti in un'osservazione stabile. Il concetto è utile, ma `NosAiProject` dispone già di WorldState, percezione, orchestrazione e contratti propri. La copia letterale creerebbe un secondo modello di stato.

Decisione: riutilizzare il pattern nella futura integrazione Perception -> WorldState -> Brain, mantenendo `nosai/core/contracts.py` come contratto canonico.

### 3. Contratti AI versionati — DA FONDERE CON I CONTRATTI ESISTENTI

Fonte: `app/ai/contracts.py`.

Sono interessanti i concetti di provenance, contract version, confidence, rationale, alternatives, outcome, reward evidence e memory record. Non viene sostituito il contratto esistente: questi campi saranno integrati solo quando coperti da test di compatibilità.

### 4. Memory evidence — DA ADATTARE

Fonte: `app/memory/evidence.py`.

Il principio importante è che la memoria **consiglia** il brain senza controllare direttamente l'esecuzione. Questo è coerente con l'architettura di `NosAiProject` e va mantenuto nel futuro modulo memory/evaluation.

### 5. Navigation memory bridge — DA ADATTARE

Fonte: `app/ai/navigation_memory.py`.

Il pattern proposal-only + replay è utile: la navigazione produce proposte registrabili senza eseguire azioni. Non viene copiato direttamente perché i tipi minimap/client legacy non coincidono con quelli del progetto canonico.

### 6. Test Center / evidence model — RIUTILIZZARE COME SPECIFICA

Dal legacy risultano particolarmente utili:

- stati distinti `NOT_RUN`, `RUNNING`, `PASS`, `FAIL`, `PARTIAL`;
- JUnit/coverage separati dal risultato CI;
- provenance tramite commit, run e artifact;
- impossibilità di trasformare uno stage non eseguito in un falso PASS;
- security/SBOM come evidenze indipendenti.

Questi concetti sono più importanti del codice legacy e devono essere assorbiti nel Test Center di `NosAiProject`.

## Stato di qualità osservato nel legacy

L'evidenza `.nosai/test-center/latest.json` del legacy indica:

- CLI: `SUCCESS`;
- static: `SUCCESS`;
- security: `PASS`;
- SBOM: `PASS`;
- E2E: `NOT_RUN`;
- quality: `FAILURE`;
- stato complessivo: `FAIL`.

Quindi il repository legacy **non viene trattato come baseline verde** e nessun suo risultato viene usato per autorizzare l'avanzamento di `NosAiProject`.

## Principio di integrazione

`NosAiProject` rimane il repository canonico. Il legacy è una fonte di idee, codice selezionato e regressioni utili. Ogni importazione deve avere:

1. provenienza documentata;
2. test unitari/contrattuali;
3. verifica CI;
4. verifica su PC;
5. verifica su smartphone per le superfici utente interessate;
6. nessun passaggio alla fase successiva prima del superamento dei gate.

La versione ufficiale resta **1.0 Beta**.
