# NosAi — Stato dell'implementazione

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk  
**Aggiornato:** 2026-08-29

## Regola di avanzamento

Il progetto non avanza in base al semplice completamento di singoli file. Avanza attraverso **obiettivi significativi verificabili**.

Il primo obiettivo operativo obbligatorio è raggiungere un collegamento reale e verificato:

`NosAi PC ↔ client NosTale ↔ rete ↔ Guard AI smartphone`

con acquisizione dei primi dati di base del client e del PC e visualizzazione/gestione corretta nella dashboard al livello raggiunto.

Ogni obiettivo significativo crea un gate. Il gate deve essere superato con test pertinenti prima di iniziare implementazioni successive. Un test fallito blocca l'avanzamento fino alla correzione e alla ripetizione del test con esito positivo.

## 🟢 Implementato a livello di codice

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
- Throttling adattivo del runtime.
- Timeout fail-fast e contratto Protobuf v3.
- Nucleo crittografico X25519 + HKDF-SHA256 + ChaCha20-Poly1305.
- Persistenza SQLite iniziale per sessioni/traiettorie.
- Controller Miniland tramite adapter.
- Framing binario PC↔telefono con `MAGIC/VERSION/TYPE/PAYLOAD_LEN/SEQ`, `SequenceGuard` e delta encoding deterministico del WorldState.
- Fondazione deployment su storage dedicato e provisioning ADB di Guard AI.
- Gate 4 integrato a livello di codice: Progression Engine V2, DAG missioni, sblocco SP, Beta-Binomiale, UCB1/MAUT e Knowledge Base.
- Suite automatica `Gate4TestRunner` integrata nel runtime principale.
- Gate 5 integrato a livello di codice: Provider Router local-first, Hardware Baseline, storage discovery e Eye AI View.
- Control Center REST loopback e `Gate5IntegratedEngine`/`Gate5TestRunner` integrati nel runtime principale.

Questa sezione indica presenza di codice/fondazioni verificabili; **non equivale al superamento del gate operativo reale**.

## 🟡 Fondazioni da integrare e verificare

- Collegamento reale NosAi ↔ client NosTale.
- Lettura affidabile dei dati di base necessari dal client.
- Acquisizione dei dati di base del PC nel runtime operativo.
- Avvio e collegamento reale di Guard AI sullo smartphone.
- Autenticazione e interoperabilità end-to-end PC ↔ smartphone.
- Heartbeat, STATUS, gestione riconnessione e fail-closed nel trasporto completo.
- Applicazione della cifratura autenticata al framing PC-Phone.
- Dashboard collegata al runtime reale e completa per il livello di sviluppo corrente.
- Persistenza EventBus, audit/replay durevole e trasporto tra processi.
- PredictionEvaluator e metriche produttive.
- Generazione binding Protobuf C++/TypeScript.
- Discovery hardware e benchmark reali.
- Shared Memory nativa e N-API.
- Persistenza analitica completa.
- Sandbox strumenti e capability enforcement.
- Backend produttivi DXGI, Triple Buffer, YOLO, OCR, Kalman e mapping specifico.
- Adapter live del gioco.
- Provider locale `llama.cpp` e provider cloud produttivi.
- Benchmark IPC e Saturazione Controllata.
- Integrazione Miniland con client reale.
- ArrayPool/Memory/Span e caricamento modelli on-demand nel percorso C#/.NET 8.

## 🔴 Gate 1 — non ancora superato

### NosAi PC

- [ ] Avvio affidabile sul PC.
- [ ] Acquisizione dati di base del PC.
- [ ] Collegamento controllato al client NosTale.
- [ ] Lettura dei dati di base necessari.
- [ ] Validazione provenienza, correttezza e freschezza dei dati.
- [ ] Gestione client assente, dati incompleti e disconnessione.

### Guard AI smartphone

- [ ] Avvio affidabile.
- [ ] Connessione a NosAi sul PC.
- [ ] Autenticazione della sessione.
- [ ] Scambio HELLO / CAPABILITIES / HEARTBEAT / STATUS.
- [ ] Ricezione dei primi dati di base.
- [ ] Verifica integrità, provenienza e freschezza.
- [ ] Gestione disconnessione e riconnessione.

### Dashboard

- [ ] Avvio affidabile.
- [ ] Connessione al runtime corretto.
- [ ] Visualizzazione dei dati realmente disponibili.
- [ ] Stato PC/NosAi/Guard AI coerente.
- [ ] Controlli disponibili solo se realmente implementati e autorizzati.
- [ ] Gestione errori e disconnessioni.
- [ ] Funzionamento al 100% di tutte le funzioni previste per questo livello.

### Prove obbligatorie del Gate 1

- [ ] Test PC.
- [ ] Test smartphone.
- [ ] Test NosAi ↔ client NosTale.
- [ ] Test NosAi ↔ Guard AI.
- [ ] Test PC ↔ smartphone.
- [ ] Test dashboard.
- [ ] Test errore/disconnessione/riconnessione.
- [ ] Nessuna regressione bloccante.
- [ ] Documentazione coerente con il risultato osservato.

**Finché tutti i punti pertinenti non sono superati, non si procede alle implementazioni successive non necessarie al Gate 1.**

## Nota sui Gate 4 e 5

Gate 4 e Gate 5 sono stati integrati nel repository come blocchi software e relative suite di certificazione invocabili. Non vengono marcati come **superati** perché il criterio ufficiale del progetto richiede prima la validazione operativa del percorso Gate 1 e, per le integrazioni successive, prove pertinenti sul sistema reale.

## Validazione successiva

Dopo il Gate 1, ogni nuovo obiettivo significativo deve avere:

1. implementazione completa del blocco interessato;
2. test automatici;
3. test di integrazione;
4. test PC quando il PC è coinvolto;
5. test smartphone quando lo smartphone è coinvolto;
6. test PC ↔ smartphone quando la comunicazione è coinvolta;
7. verifica della dashboard quando il cambiamento la interessa;
8. verifica di assenza di regressioni;
9. aggiornamento della documentazione;
10. approvazione del gate prima del successivo obiettivo.

## Nota sui benchmark

Le prestazioni numeriche delle specifiche sono obiettivi di benchmark finché non sono state misurate sul sistema di riferimento.

## Storage previsto

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

## Stato di sviluppo corrente

Il progetto possiede una base software ampia, ora comprendente anche le implementazioni software Gate 4 e Gate 5. Il progetto **non deve essere considerato oltre il Gate 1** finché il percorso reale PC ↔ NosTale ↔ smartphone e la dashboard del relativo livello non sono stati verificati con esito positivo.
