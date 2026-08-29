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
- Timeout fail-fast e contratto Protobuf v3.
- Nucleo crittografico X25519 + HKDF-SHA256 + ChaCha20-Poly1305.
- Persistenza SQLite per sessioni/traiettorie.
- Controller Miniland tramite adapter.
- <b>Storage dedicato Crucial X6:</b> discovery del volume `NOSAI-SSD`, validazione NTFS, soglia spazio, layout canonico e bootstrap Windows non distruttivo.
- <b>Policy SQLite centralizzata:</b> WAL, FULL, busy timeout, cache, limite WAL e incremental vacuum.
- <b>Provisioning Guard AI:</b> manager ADB su `tools\adb\adb.exe`, attesa device autorizzato, verifica/installazione `com.nosai.guard` e bootstrap dell'app.
- Documentazione architetturale aggiornata con deployment SSD e integrazione PC-Phone.

## 🟡 Fondazioni — non complete per la produzione

- Persistenza EventBus, audit/replay durevole e trasporto tra processi.
- PredictionEvaluator e metriche produttive.
- Ranking basato su evidenza e lifecycle della conoscenza.
- Integrazione produttiva Guard AI / Watchdog / Recovery tra PC e telefono.
- Wire protocol PC-Phone AES-GCM completo e interoperabile.
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

## 🔴 Non ancora validato

### Deployment SSD

L'implementazione software è presente, ma la validazione fisica richiede il Crucial X6 CT2000X6SSD9 collegato al PC Windows 11. Devono essere verificati: lettera di unità variabile, volume assente, NTFS, read-only, I/O, spazio, benchmark, perdita simulata, recovery/reconnect e assenza di scritture applicative involontarie sul disco interno.

### PC-Phone

Il provisioning ADB è implementato come fondazione. Restano da integrare e testare il server TCP su porta inoltrata 6100, frame binary 12-byte, AES-GCM-256, sequence counter, handshake, heartbeat 1000 ms e fail-closed secondo il contratto allegato. fileciteturn14file0L40-L44

### APK

`GuardAi.apk` deve essere fornito/buildato e verificato nel percorso `runtime\GuardAi.apk` del volume. Il repository non deve fingere che un APK inesistente sia disponibile.

## Nuova struttura storage

```text
<NOSAI-SSD>:\NosAi\
├── app\
├── runtime\
├── models\
├── data\db\
├── data\state\
├── data\evidence\
├── data\exports\
├── cache\
├── logs\
├── temp\
├── backups\
├── config\
└── tools\
```

## Ordine corrente

1. Test PC reale del deployment SSD e della policy SQLite.
2. Integrare storage health con Watchdog/Recovery e stato fail-closed.
3. Test reale di provisioning Android.
4. Implementare il wire protocol PC-Phone secondo contratto.
5. Integrare heartbeat/fail-closed PC-Phone.
6. Solo dopo esito positivo: proseguire con le successive integrazioni runtime.

**Regola invariabile:** nessuna fase successiva viene considerata completata finché i test PC e Smartphone pertinenti non hanno esito positivo.
