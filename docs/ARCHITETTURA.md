# NosAi — Architettura completa e modello di comunicazione

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk

## 1. Mappa generale

```text
SESSIONE / SCHEDULER
        │
RISORSE ── POLICY ── PROVIDER ROUTER ── MEMORIA ── STORAGE SSD
        │                                  │             │
CONTROLLO RUNTIME ── WATCHDOG ── RECOVERY ── SQLITE/WAL
        │
EVENTBUS / TRACE
        │
PERCEZIONE → WORLDSTATE(vN) → SIMULAZIONE → RANKING
                                      │
                                 ORCHESTRATOR
                                      │
                                  PLANNER
                                      │
                              GUARD / TRUST / SAFETY
                                      │
                             EXECUTOR / ADAPTER
                                      │
                                  VERIFIER
                                      │
                              WORLDSTATE(vN+1)
                                      │
                         PASS ───────┴──── FAIL
                                                │
                              CONTEXT SLIMMING / RECOVERY
```

## 2. Deployment PC su SSD dedicato

Il runtime PC di NosAi è progettato per il volume esterno dedicato `NOSAI-SSD`, Crucial X6 CT2000X6SSD9 da 2 TB. Windows rimane sul disco interno; codice NosAi, runtime locale, modelli, memoria persistente, SQLite, log, cache, configurazione e artefatti applicativi sono allocati sul volume dedicato.

Il root viene risolto a runtime tramite etichetta del volume e non tramite una lettera fissa. Il modulo `nosai.storage.volume` valida presenza, NTFS, accessibilità e spazio minimo senza formattare il dispositivo. `nosai.storage.paths` costruisce il layout canonico.

Il deployment è **portable-by-volume**, non bootable: cambiare la lettera assegnata da Windows non deve cambiare i percorsi logici di NosAi.

## 3. Storage layer

Layout canonico:

```text
<NOSAI-SSD>:\NosAi\
├── app\ runtime\ models\
├── data\db\ state\ evidence\ exports\
├── cache\ logs\ temp\ backups\ config\ tools\
```

Il volume è considerato una dipendenza infrastrutturale del runtime. Se non è disponibile, il launcher deve rifiutare l'avvio delle funzioni che richiedono persistenza. Sono vietati path applicativi relativi dipendenti dalla directory corrente.

## 4. SQLite

La policy SQLite è centralizzata in `nosai/storage/sqlite_policy.py` e viene applicata da `NosAiSqliteLogger`.

Profilo corrente:

- `journal_mode=WAL`;
- `synchronous=FULL` per la persistenza critica;
- `busy_timeout=5000 ms`;
- cache di 64 MiB;
- `journal_size_limit=64 MiB`;
- `auto_vacuum=INCREMENTAL`.

WAL e i file ausiliari restano sul volume locale. Il checkpoint controllato è disponibile tramite la policy storage. Questa scelta sostituisce il precedente `synchronous=NORMAL` del logger, mantenendo comunque la policy configurabile.

## 5. PC Play Guard e sicurezza storage

PC Play Guard deve osservare lo stato del volume e impedire che una perdita dello storage produca nuove scritture incontrollate. Gli stati progettuali sono `STORAGE_SAFE`, `STORAGE_BUSY` e `STORAGE_ERROR`.

In caso di scomparsa del volume: blocco nuove scritture, evento critico, sospensione delle attività dipendenti dalla persistenza, tentativo di recovery/reconnect e arresto sicuro se la persistenza non è garantibile.

## 6. Topologia PC-Phone

La topologia resta composta da:

- **Play AI / Executor:** runtime sul volume `NOSAI-SSD`, eseguito sul PC; unico confine di esecuzione diretta.
- **PC Play Guard:** supervisione deterministica sul PC Windows.
- **phone Guard AI:** applicazione Android `com.nosai.guard`, barriera esterna con autorità ALLOW/DENY.

Questa separazione è coerente con il Contratto di Comunicazione NosAi allegato. fileciteturn14file0L2-L2

## 7. Provisioning smartphone

È stata aggiunta `nosai/phone/provisioning.py`. Il manager usa esclusivamente l'ADB isolato nel volume `tools\adb\adb.exe`, attende un device autorizzato, verifica `com.nosai.guard`, installa `runtime\GuardAi.apk` se assente e avvia l'app.

Il provisioning non scarica componenti dall'esterno e non opera su un dispositivo non autorizzato. Il flusso segue il modello di onboarding definito nella specifica architetturale allegata. fileciteturn14file1L48-L52

## 8. Protocollo PC-Phone

La specifica allegata definisce un frame binario con header di 12 byte, Magic `0x4E4F5341`, lunghezza payload, contatore di sequenza monotono e payload JSON cifrato AES-GCM-256. fileciteturn14file0L40-L40

Il repository non dichiara ancora questo wire protocol completo come produzione: il contratto deve essere integrato con una macchina a stati, autenticazione delle sessioni, test di interoperabilità e gestione dei limiti di frame.

## 9. Fail-closed PC-Phone

La specifica allegata richiede heartbeat a 1000 ms e fail-closed dopo 2 heartbeat mancanti o flag hardware critico, con stop delle scritture, checkpoint SQLite, modalità degradata ed evento critico. fileciteturn14file0L44-L44

Questa è una **specifica di integrazione da completare**, non viene presentata come già completamente cablata nel runtime.

## 10. Resto dell'architettura

Restano validi EventBus bounded, WorldState versionato, RecoveryController, circuit breaker, Watchdog, Context Slimming, Trust Tier, Executor/Adapter, Verifier, provider routing, percezione, Protobuf e sicurezza di sessione già documentati nel repository.

## 11. Regola di validazione

L'integrazione SSD, SQLite e phone provisioning non è considerata produttiva finché non passano i test sul PC reale e, per il percorso PC-Phone, sullo smartphone reale. Nessuna fase successiva deve essere considerata completata in presenza di test falliti.

**Versione progetto: 1.0 Beta.**
