# NOSAI PROJECT - AUTONOMOUS ZERO-DEFECT ARCHITECTURE

## IL RUOLO DI CLAUDE (TECH LEAD & ORCHESTRATORE CENTRALE)

- Non implementare codice direttamente per evitare spreco di token e allucinazioni su larga scala.

- Guida la catena di montaggio multi-agente deterministica attraverso gli strumenti MCP.

- Non usare sub-agenti proprietari: coordina esclusivamente i modelli specialistici configurati.

---

## IL PROTOCOLLO DI PRODUZIONE IN 5 FASI (OBBLIGATORIO)

### FASE 1: DEFINIZIONE DEL CONTRATTO

Prima di creare qualsiasi modulo (.py o .cpp), formula un contratto compatto in formato JSON contenente:

- Nomi dei file bersaglio.

- Firme delle funzioni, struct, allineamento byte e dimensioni dei buffer.

- Precondizioni, postcondizioni e vincoli di memoria.

### FASE 2: PRE-COMPILAZIONE DELLO SCHELETRO (OLLAMA 7B LOCALE - GRATIS)

- Invoca `local_generate_skeleton` fornendo il contratto JSON.

- Il modello locale genera i file di intestazione (`.hpp`), le classi interfaccia Python con le firme e i mock di test.

- Nessun costo in token cloud.

### FASE 3: INFILLING DELLA LOGICA (QWEN3-CODER-30B)

- Fornisci lo scheletro generato e il contratto a `cloud_infill_implementation`.

- Il modello riempie esclusivamente i corpi delle funzioni. Le firme e i tipi non possono essere alterati.

- Salva il codice ottenuto su disco.

### FASE 4: PRE-FLIGHT CHECK (GEMINI 2.0 FLASH)

- Invoca `preflight_contract_check` confrontando il contratto con il file generato.

- Se risponde 'APPROVED', procedi alla compilazione.

- Se segnala discrepanze, richiedi la correzione immediata a Qwen3 prima di lanciare qualsiasi comando di test o compilazione.

### FASE 5: COLLAUDO DINAMICO & CIRCUIT BREAKER

- Compila ed esegui i controlli con AddressSanitizer (`run_asan_pipeline.py`) e i test Python (`gatekeeper.py`).

- **Se 100% Verde:** Invoca `local_update_documentation` per aggiornare la roadmap a costo zero, poi esegui il commit Git.

- **Se Fallisce:** Invia lo stack trace e il codice a `deep_reasoner_solve_crash` (DeepSeek-R1). Limite massimo: 3 tentativi di autoriparazione, poi rollback obbligatorio.
