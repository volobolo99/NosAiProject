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

WAL e i file ausiliari restano sul volume locale. Il checkpoint controllato è disponibile tramite la policy storage.

## 5. PC Play Guard e sicurezza storage

PC Play Guard deve osservare lo stato del volume e impedire che una perdita dello storage produca nuove scritture incontrollate. Gli stati progettuali sono `STORAGE_SAFE`, `STORAGE_BUSY` e `STORAGE_ERROR`.

In caso di scomparsa del volume: blocco nuove scritture, evento critico, sospensione delle attività dipendenti dalla persistenza, tentativo di recovery/reconnect e arresto sicuro se la persistenza non è garantibile.

## 6. Topologia PC-Phone

La topologia resta composta da:

- **Play AI / Executor:** runtime sul volume `NOSAI-SSD`, eseguito sul PC; unico confine di esecuzione diretta.
- **PC Play Guard:** supervisione deterministica sul PC Windows.
- **phone Guard AI:** applicazione Android `com.nosai.guard`, barriera esterna con autorità ALLOW/DENY.

## 7. Provisioning e onboarding smartphone

`nosai/phone/provisioning.py` fornisce il provisioning ADB di base. `nosai/phone/onboarding_engine.py` aggiunge l'orchestrazione deterministica: ADB isolato nel volume, attesa di device autorizzato, installazione condizionata dell'APK locale `runtime\GuardAi.apk`, forwarding TCP `6100`, avvio di `com.nosai.guard` e costruzione del primo `SESSION_HELLO` con sequenza 1.

Il provisioning non scarica componenti dall'esterno e non opera su un dispositivo non autorizzato.

## 8. Autenticazione RSA SESSION_AUTH

`nosai/network/crypto_auth.py` implementa `NosAiCryptoAuthManager`:

- challenge casuale di 32 byte;
- rappresentazione wire in esadecimale;
- verifica della firma Guard AI con RSA-2048, SHA-256 e PKCS#1 v1.5;
- caricamento della sola chiave pubblica PEM dal volume dedicato;
- digest SHA-256 della challenge per audit;
- consumo della challenge dopo ogni tentativo, per impedire il riuso del nonce.

Le chiavi private non sono gestite dal runtime PC e non devono entrare nel repository.

## 9. Protocollo PC-Phone

`nosai/network/wire_protocol.py` implementa il frame binario da 12 byte:
`MAGIC(4) | VERSION(1) | TYPE(1) | PAYLOAD_LEN(2) | SEQ(4)`.

`SequenceGuard` accetta esclusivamente la sequenza attesa. Gap, duplicati e regressioni devono essere trattati dal livello di sessione come condizioni fail-closed.

Le primitive di framing e autenticazione sono ora presenti nel repository, ma il wire protocol completo non è ancora dichiarato produzione: restano da integrare trasporto TCP con macchina a stati, AES-GCM-256, heartbeat, timeout e interoperabilità completa con l'APK reale.

## 10. Fail-closed PC-Phone

La specifica di progetto richiede heartbeat a 1000 ms e fail-closed dopo 2 heartbeat mancanti o flag hardware critico, con stop delle scritture, checkpoint SQLite, modalità degradata ed evento critico.

Questa parte resta un **traguardo di integrazione da completare e validare**; la presenza delle primitive RSA/framing non equivale all'abilitazione dell'azione live.

## 11. Resto dell'architettura

Restano validi EventBus bounded, WorldState versionato, RecoveryController, circuit breaker, Watchdog, Context Slimming, Trust Tier, Executor/Adapter, Verifier, provider routing, percezione, Protobuf e sicurezza di sessione già documentati nel repository.

## 12. Regola di validazione

L'integrazione SSD, RSA, framing, onboarding e percorso PC-Phone non è considerata produttiva finché non passano i test automatici e i test sul PC reale e, per il percorso PC-Phone, sullo smartphone reale. Nessuna fase successiva deve essere considerata completata in presenza di test falliti.

**Versione progetto: 1.0 Beta.**
