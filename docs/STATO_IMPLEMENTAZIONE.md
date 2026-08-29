# NosAi — Stato dell'implementazione

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk  
**Aggiornato:** 2026-08-29

## 🟢 Implementato

- Contratti fondamentali e base decisionale deterministica.
- Confine Safety Gate e integrazione Orchestrator.
- World Model, Party, Pet e Partner.
- Coordinated Action Manager.
- Tactical Action Ranking e fondazioni Simulation/Lookahead.
- Contratti Perception, pipeline iniettabile, visione ROI e fondazione tracking.
- Fondazione Game State Evaluator e adapter Perception → WorldState.
- Fondazione Agent Runtime: sessioni, memoria, instradamento provider local-first, risorse, policy e Trust Tier 0–4.
- Ciclo Planner → Guard → Safety → Executor → Verifier multi-step.
- Retry/ripianificazione, checkpoint e watchdog indipendente.
- ToolRegistry, profilazione hardware, contratti LAN e protezione sequenza/replay.
- EventBus bounded, WorldState versionato e Context Slimming.
- RecoveryController adattivo, circuit breaker e Runtime/HW Watchdog.
- Throttling adattivo del runtime in base a temperatura GPU, pressione RAM, disconnessione LAN e guasti critici.
- Timeout fail-fast e contratto Protobuf v3.
- Nucleo crittografico X25519 + HKDF-SHA256 + ChaCha20-Poly1305.
- Persistenza SQLite per sessioni/traiettorie.
- Controller Miniland tramite adapter.
- Framing binario PC↔telefono con intestazione `MAGIC/VERSION/TYPE/PAYLOAD_LEN/SEQ`, `SequenceGuard` e delta encoding deterministico del WorldState.
- Storage dedicato Crucial X6: discovery del volume `NOSAI-SSD`, validazione NTFS, soglia spazio, layout canonico e bootstrap Windows non distruttivo.
- Policy SQLite centralizzata: WAL, FULL, busy timeout, cache, limite WAL e incremental vacuum.
- Provisioning Guard AI: manager ADB su `tools\\adb\\adb.exe`, attesa device autorizzato, verifica/installazione `com.nosai.guard` e bootstrap dell'app.
- Documentazione architetturale aggiornata con performance, deployment SSD e integrazione PC-Phone.

## 🟡 Fondazioni — non complete per la produzione

- Persistenza EventBus, audit/replay durevole e trasporto tra processi.
- PredictionEvaluator e metriche produttive.
- Ranking basato su evidenza e lifecycle della conoscenza.
- Integrazione produttiva Guard AI / Watchdog / Recovery tra PC e telefono.
- Applicazione della cifratura autenticata al framing PC-Phone e interoperabilità end-to-end.
- TLS/mTLS o Noise completo per il trasporto LAN.
- Generazione binding Protobuf C++/TypeScript.
- Discovery hardware e benchmark reali, incluso benchmark del Crucial X6.
- Shared Memory nativa e N-API.
- Persistenza analitica completa oltre al logger iniziale.
- Sandbox strumenti e capability enforcement.
- Backend produttivi DXGI, Triple Buffer, YOLO, OCR, Kalman e mapping specifico.
- Adapter live del gioco.
- Provider locale `llama.cpp` e provider cloud.
- Benchmark IPC e Saturazione Controllata.
- Integrazione Miniland con client reale.
- ArrayPool/Memory/Span e caricamento modelli on-demand nel percorso C#/.NET 8.

## 🔴 Non ancora validato

### Prestazioni e memoria

La logica di throttling e delta encoding è implementata e testabile, ma i valori prestazionali dichiarati nella specifica allegata restano obiettivi di benchmark. Devono essere misurati sul sistema di riferimento prima di essere considerati raggiunti.

### Deployment SSD

L'implementazione software è presente, ma la validazione fisica richiede il Crucial X6 CT2000X6SSD9 collegato al PC Windows 11. Devono essere verificati: lettera di unità variabile, volume assente, NTFS, read-only, I/O, spazio, benchmark, perdita simulata, recovery/reconnect e assenza di scritture applicative involontarie sul disco interno.

### PC-Phone

Il framing binario e `SequenceGuard` sono implementati. Restano da integrare e testare end-to-end: autenticazione crittografica sul frame, handshake completo, heartbeat 1000 ms, delta WorldState sul trasporto, server TCP sulla porta prevista e comportamento fail-closed.

### APK

`GuardAi.apk` deve essere fornito/buildato e verificato nel percorso `runtime\\GuardAi.apk` del volume. Il repository non deve fingere che un APK inesistente sia disponibile.

## Nuova struttura storage

```text
<NOSAI-SSD>:\\NosAi\\
├── app\\
├── runtime\\
├── models\\
├── data\\db\\
├── data\\state\\
├── data\\evidence\\
├── data\\exports\\
├── cache\\
├── logs\\
├── temp\\
├── backups\\
├── config\\
└── tools\\
```

## Ordine corrente

1. Test PC reale del deployment SSD e della policy SQLite.
2. Integrare storage health con Watchdog/Recovery e stato fail-closed.
3. Test reale di provisioning Android.
4. Completare autenticazione del wire protocol PC-Phone e interoperabilità end-to-end.
5. Integrare heartbeat/delta WorldState/fail-closed PC-Phone.
6. Integrare throttling nel percorso runtime C# e nei budget dei moduli non critici.
7. Implementare e benchmarkare ArrayPool/Memory/Span e caricamento modelli on-demand nel percorso C#.
8. Solo dopo esito positivo: proseguire con le successive integrazioni runtime.

**Regola invariabile:** nessuna fase successiva viene considerata completata finché i test PC e Smartphone pertinenti non hanno esito positivo.
