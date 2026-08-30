# NosAi — Stato dell'implementazione

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk  
**Aggiornato:** 2026-08-30

## Regola di avanzamento

Il progetto non avanza in base al semplice completamento di singoli file. Avanza attraverso **obiettivi significativi verificabili**.

Il primo obiettivo operativo obbligatorio è raggiungere un collegamento reale e verificato:

`NosAi PC ↔ client NosTale ↔ rete ↔ Guard AI smartphone`

con acquisizione dei primi dati di base del client e del PC e visualizzazione/gestione corretta nella dashboard al livello raggiunto.

Ogni obiettivo significativo crea un gate. Il gate deve essere superato con test pertinenti prima di iniziare implementazioni successive. Un test fallito blocca l'avanzamento fino alla correzione e alla ripetizione del test con esito positivo.

---

## Classificazione di maturità adottata

| Livello | Significato |
|---|---|
| **Present** | Il codice o il contratto esiste nel repository. |
| **Partial** | Il blocco esiste ma è incompleto, simulato o non sufficientemente collegato. |
| **Integrated** | Il blocco è collegato ad altri componenti rilevanti del runtime. |
| **Verified** | Il blocco è coperto da test credibili o evidenze esecutive pertinenti. |
| **Operational** | Il blocco è confermato nel flusso reale previsto dal progetto. |

Questa classificazione deve essere usata per distinguere chiaramente tra presenza del codice e maturità operativa.

---

## Stato sintetico corrente

Il progetto possiede una base software ampia e coerente, ma il suo stato complessivo resta **non oltre il Gate 1** sul piano operativo.

La valutazione corrente è la seguente:

- molte aree sono **Present** o **Integrated**;
- alcune aree critiche restano **Partial**;
- il percorso reale minimo non è ancora **Verified** né **Operational** nel suo insieme.

---

## 🟢 Present o Integrated a livello di codice

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
- Sottosistema Navigation/Pathfinding presente nel repository con implementazione dedicata.
- Sottosistema Economy/Inventory presente nel repository con implementazione dedicata.

Questa sezione indica presenza di codice o integrazione parziale/funzionale nel repository; **non equivale al superamento del gate operativo reale**.

---

## 🟡 Aree Partial da integrare e verificare

- Collegamento reale NosAi ↔ client NosTale: **OS baseline verificato sul client reale**; gameplay HP/mappa/entità ancora UNKNOWN (manca il provider).
- Lettura affidabile dei dati di base necessari dal client (OS baseline presente; manca ancora un provider gameplay).
- Acquisizione dei dati di base del PC nel runtime operativo (RAM processo LIVE; CPU/GPU di sistema UNKNOWN se il probe non riporta valori).
- Avvio e collegamento reale di Guard AI sullo smartphone: l'app Android esiste (`src/NosAi.GuardAi.App`, .NET MAUI su `NosAi.GuardClient`) e compila, ma **non è ancora stata eseguita su un dispositivo fisico** contro il runtime su LAN. La chiave del dispositivo è ancora in memoria e non nell'Android Key Store.
- Autenticazione e interoperabilità end-to-end PC ↔ smartphone.
- Heartbeat, STATUS, gestione riconnessione e fail-closed nel trasporto completo.
- Applicazione della cifratura autenticata al framing PC-Phone nel flusso completo.
- Dashboard collegata solo al runtime reale e completa per il livello di sviluppo corrente.
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
- Test di integrazione runtime per Navigation/Pathfinding e Economy/Inventory e loro collegamento ai dati reali del client.
- Riallineamento della suite di test tra runtime C# attuale, test Python esistenti e prove end-to-end autorevoli.

---

## 🔴 Gate 1 — non ancora superato

### Stato di maturità del percorso critico

| Componente critico | Maturità attuale | Nota |
|---|---|---|
| **Bootstrap runtime PC** | **Integrated** | Avvio e bootstrap esistono, ma manca prova operativa completa sul sistema reale. |
| **Protocollo/sessione PC ↔ smartphone** | **Integrated** | Fondazione solida visibile a livello di codice, ma non ancora validata end-to-end nel sistema reale. |
| **Client connector NosTale** | **Verified** (OS baseline) | Validato contro NosTale reale: processo/PID/titolo/handle/responding/visible `LIVE`. Gameplay resta **Partial**: nessun provider. |
| **Guard AI smartphone** | **Partial** | Fondazioni presenti, integrazione reale da provare sul campo. |
| **Dashboard / Control Center** | **Partial** | Base tecnica presente, ma va resa coerente esclusivamente con segnali reali. |
| **Perception / acquisizione dati di gioco** | **Partial** | Contratti e fondazioni presenti; backend produttivi ancora incompleti. |
| **WorldState reale** | **Integrated** | Struttura presente, ma ancora dipendente dal completamento delle sorgenti reali. |

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

---

## Nota sui Gate 4 e 5

Gate 4 e Gate 5 sono presenti nel repository come blocchi software e relative suite di certificazione invocabili. Non vengono marcati come **Verified** o **Operational** sul piano del progetto complessivo perché il criterio ufficiale richiede prima la validazione operativa del percorso Gate 1 e, per le integrazioni successive, prove pertinenti sul sistema reale.

---

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

---

## Nota sui benchmark

Le prestazioni numeriche delle specifiche sono obiettivi di benchmark finché non sono state misurate sul sistema di riferimento.

---

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

---

## Stato di sviluppo corrente

Il progetto possiede una base software ampia, comprendente fondazioni Gate 1, Gate 4, Gate 5 e nuovi sottosistemi come Navigation/Pathfinding ed Economy/Inventory.

Il progetto **non deve essere considerato oltre il Gate 1** finché il percorso reale PC ↔ NosTale ↔ smartphone e la dashboard del relativo livello non sono stati verificati con esito positivo.

La priorità operativa corrente resta quindi:

**chiudere, misurare e validare il primo circuito reale del sistema.**
