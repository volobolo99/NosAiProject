# NosAiProject — Coda di esecuzione multi-agente

**Scopo:** un solo file da cui partire. Ogni voce ha un ID, chi la esegue, da cosa dipende, e dove sta il comando dettagliato. Aggiornato da Claude ad ogni avanzamento — questo file è la fonte di verità su "cosa è il prossimo passo", non `docs/agents/AGENT_COMMAND_REGISTRY.md` (che resta la mappa di ownership per dominio, statica) né i singoli file in `docs/agents/phases/` (che restano i comandi dettagliati per singolo task).

**Come si usa:** guarda la prima voce con stato `PENDING`. Se l'esecutore è `Claude`, dillo a Claude di partire (es. "vai con Q-004"). Se è `DeepSeek`, apri il file comando indicato e passalo a DeepSeek. Quando un task finisce, l'esecutore aggiorna lo stato qui.

**Stati:** `PENDING` (non iniziato) · `IN_PROGRESS` · `DONE` · `BLOCKED` (dipendenza non soddisfatta).

---

## Coda attiva (AP-00 → AP-03)

| ID | Fase | Task | Esecutore | Dipende da | Comando | Stato |
|---|---|---|---|---|---|---|
| Q-001 | AP-00 | A1 — Contratti capacità hardware | Claude | — | `docs/agents/phases/AP-00/A1_CLAUDE_hardware_contracts.md` | **DONE** |
| Q-002 | AP-00 | A2 — Profiling Python hardware | Claude (Cursor non disp.) | — | `docs/agents/phases/AP-00/A2_CURSOR_runtime_profiling.md` | **DONE** |
| Q-003 | AP-00 | A3 — AI budget policy | Claude | — | `docs/agents/phases/AP-00/A3_CLAUDE_ai_budget_policy.md` | **DONE** |
| Q-004 | AP-00 | A4 — Runtime capability gate | Claude (Cursor non disp.) | Q-001 | `docs/agents/phases/AP-00/A4_CURSOR_runtime_gate.md` | **DONE** |
| Q-005 | AP-00 | A5 — Test/benchmark/doc | Claude | Q-001, Q-003, Q-004 | `docs/agents/phases/AP-00/A5_CLAUDE_tests_docs.md` | **DONE** |
| Q-006 | AP-00 | A6 — Integrazione finale (dedup `InferenceTier` Hardware/Scheduling, build/test completo, commit) | **Claude** | Q-005 | `docs/agents/phases/AP-00/A6_CLAUDE_integration_gate.md` | **DONE** |
| Q-007 | AP-01 | A1 — Contratti World Model (Player/Map/Tile/Portal/Mob/NPC/Drop/Quest/Inventory/Equipment/Skill/Buff/Debuff/Cooldown/Resource/Action/Goal) | **Claude** | Q-006 | `docs/agents/phases/AP-01/A1_CLAUDE_world_model_contracts.md` | **DONE** |
| Q-008 | AP-01 | A2 — Sensor Fusion (Network/Memory/Screen/Local → World Model) | Claude (DeepSeek/Cursor non ancora attivi) | Q-007 | `docs/agents/phases/AP-01/A2_DEEPSEEK_sensor_fusion.md` | **DONE** |
| Q-009 | AP-01 | A3 — Temporal belief (decadimento confidence continuo) + derived state (velocità stimata) + prediction (estrapolazione posizione, advisory-only) | **Claude** | Q-007 | `src/NosAi.Core/WorldModel/Temporal/` (nessun comando dedicato scritto: ambito chiarito in conversazione con l'utente, non serviva un file separato) | **DONE** |
| Q-010 | AP-01 | A4 — Runtime wiring (WorldModelFusionLoop, flag `--fuse-world-model`, ModuleReachability → Integrated) | Claude (agente in background) | Q-007, Q-008, Q-009 | (comando dato in linea all'agente, non un file separato — vedi AP-01_STATUS.md §9) | **DONE** |
| Q-011 | AP-01 | A5 — Test/benchmark/doc AP-01 (audit indipendente, 4 difetti reali trovati) | Claude (agente in background) | Q-007, Q-008, Q-009, Q-010 | `docs/agents/phases/AP-01/AP-01_A5_AUDIT.md` | **DONE** |
| Q-012 | AP-01 | A6 — Integrazione finale AP-01 (4 correzioni applicate, build/test combinati verdi) | **Claude** | Q-011 | (vedi AP-01_STATUS.md §11) | **DONE** |
| Q-013 | AP-02 | A1 — Contratto `VisualObservation` (osservazione visiva unificata con provenance) | **Claude** | Q-012 | (vedi AP-02_STATUS.md §3; nessun file comando separato — ricognizione approfondita ha sostituito la stesura preventiva) | **DONE** |
| Q-014 | AP-02 | A2 — Screen/OCR/CV adapters | Claude (Cursor non disp.) | Q-013 | in gran parte già presente da prima (DXGI capture, ROI, ONNX detector contract — vedi AP-02_STATUS.md §1); OCR reale/modello addestrato bloccati da asset ML non producibili in questo ambiente | **BLOCKED** (asset ML mancanti) |
| Q-015 | AP-02 | A3 — Fusione multimodale vitali (HP/MP schermo↔rete via FactFusion) | **Claude** | Q-013 | (vedi AP-02_STATUS.md §4) | **DONE** (ambito ristretto ai vitali; fusione entità Mob/Npc rimandata, stesso blocco ML di Q-014) |
| Q-016 | AP-02 | A4 — Wiring runtime (`ScreenVitalsCapture`: DXGI reale + `ScreenVitalReader` + `NullObjectDetector`, cablato in `WorldModelFusionLoop`/`Program.cs`) | Claude (agente in background) | Q-013, Q-015 | (vedi AP-02_STATUS.md §6) | **DONE** |
| Q-017 | AP-02 | A5 — Audit indipendente AP-02 (2 difetti reali trovati) | Claude (agente in background) | Q-013, Q-015 | `docs/agents/phases/AP-02/AP-02_A5_AUDIT.md` | **DONE** |
| Q-018 | AP-02 | A6 — Integrazione finale AP-02 (2 correzioni applicate, build/test combinati verdi) | **Claude** | Q-017 | (vedi AP-02_STATUS.md §8) | **DONE** |
| Q-019 | AP-03 | A1 — Contratto `MapObservationBatch` (evidenza tile/portali osservazione-side) | **Claude** | Q-018 | `docs/agents/phases/AP-03/AP-03_A1_CLAUDE_map_observation_contract.md` | **DONE** |
| Q-020 | AP-03 | A2 — Projector `MapGrid` (client-archive reale) → `MapObservationBatch`, classificato Cached | **Claude** | Q-019 | (nessun file comando separato, ambito piccolo e meccanico — vedi diff `MapGridObservationProjector.cs`) | **DONE** |
| Q-021 | AP-03 | A3 — Algoritmo di fusione incrementale `MapReconstructionFusion.Merge` (mai perdita di storia, bounds monotoni, idempotente) | **Claude** | Q-019 | (vedi diff `MapReconstructionFusion.cs`) | **DONE** |
| Q-022 | AP-03 | A4 — Persistenza SQLite (`MapModelStore`, WAL/FULL/busy_timeout=5000) + wiring runtime (`MapReconstructionSource` in `WorldModelFusionLoop`/`Program.cs`, dietro `--fuse-world-model`) | Claude (agente in background) | Q-020, Q-021 | `docs/agents/phases/AP-03/AP-03_A4_CLAUDE_persistence_and_wiring.md` | **IN_PROGRESS** |
| Q-023 | AP-03 | A5 — Audit indipendente AP-03 | Claude (agente in background) | Q-022 | (da scrivere quando Q-022 completa) | PENDING |
| Q-024 | AP-03 | A6 — Integrazione finale AP-03 | **Claude** | Q-023 | (vedi AP-03_STATUS.md quando esiste) | PENDING |

## Regola per Q-014/Q-015/Q-016/Q-017/Q-018 e per tutte le fasi successive

I comandi dettagliati per i task oltre Q-008 **non sono ancora scritti di proposito**: scriverli ora, prima che i contratti di AP-01 esistano davvero, rischierebbe di fissare dettagli sbagliati che poi vanno disfatti (esattamente il tipo di "casino" da evitare). La regola è: **quando una voce `PENDING` diventa la prima della coda, Claude scrive il suo comando dettagliato (stile dei file già prodotti oggi, non gli stub telegrafici originali) prima di farla partire**, poi la esegue lui stesso o la assegna a DeepSeek a seconda della colonna Esecutore.

## Fasi successive (AP-04 → AP-10) — solo sequenza, nessun dettaglio ancora

Dalla roadmap canonica (`docs/ROADMAP_ESECUTIVA.md`). AP-03 è ora attiva (Q-019+ sopra); le fasi seguenti non hanno ancora task numerati in coda — verranno aggiunti qui, con lo stesso formato sopra, quando AP-03 sarà `Integrated`.

| Fase | Obiettivo |
|---|---|
| AP-04 | Exploration & Navigation — scoperta/attraversamento senza percorsi hardcoded |
| AP-05 | Combat Intelligence — policy di combattimento adattiva con recovery |
| AP-06 | Quest Intelligence — missioni non hardcoded → grafo verificabile |
| AP-07 | Character/Inventory/Equipment — azioni di equip motivate e verificate |
| AP-08 | Strategic Autonomy + HTN — ogni azione deriva da un piano verificabile |
| AP-09 | Memory/Learning/Simulation — conoscenza persistente cross-sessione |
| AP-10 | Full Autonomous Certification — scenario end-to-end senza comandi umani |

## Criterio di avanzamento fase

Una fase (AP-XX) non inizia finché la precedente non è almeno `Integrated` (build+test combinati verdi) — mai `Verified` come precondizione di partenza, dato che `Verified` richiede hardware reale non disponibile in questo ambiente di sviluppo. Questo è il criterio già in uso oggi (AP-00 Q-006 come gate prima di iniziare AP-01 Q-007).

**Eccezione dichiarata (2026-09-05):** i soli contratti A1 di una fase possono partire prima che la fase precedente sia `Integrated`, quando dipendono solo da contratti di una fase ancora più a monte già `Integrated` (non da come la fase precedente li popola a runtime) — questo per permettere a Claude e DeepSeek di lavorare in parallelo invece che in sequenza rigida (istruzione esplicita dell'utente: "programmare tutti insieme e in multi-agente per finire prima"). Esempio concreto: AP-04/A1 dipende da `MapModel`/`Tile`/`TileCoordinate`/`TileTraversability`/`Portal`, stabili da AP-01, non dal lavoro ancora aperto in AP-03/A5-A6. L'implementazione A2/A4 di una fase resta invece gated: non parte finché la fase precedente non è `Integrated`, perché quella sì dipende dal comportamento a runtime, non solo dalla forma dei contratti.

## Chi fa cosa, in breve

**Principio (istruzione esplicita dell'utente, 2026-09-05): Claude gestisce il lavoro — ownership, contratti fondanti, audit, integrazione — ma continua anche lei a scrivere codice reale, su compiti più piccoli; DeepSeek riceve il carico più pesante di sviluppo/compilazione file. Lavorano in parallelo, non in sequenza rigida, per finire prima.** Dettaglio operativo completo in `docs/agents/DEEPSEEK_TASKS.md`.

- **DeepSeek: ATTIVO dal 2026-09-05, prende il ruolo di Cursor INTERAMENTE** — ogni riga "A2 Cursor"/"A4 Cursor" in `docs/agents/AGENT_COMMAND_REGISTRY.md`, per qualunque fase, è ora "A2 DeepSeek"/"A4 DeepSeek" (v1.1). Storicamente in questo progetto A2+A4 sono anche stati il carico di lavoro più pesante per fase (es. AP-03/A4 da solo: 8 file, 1421 righe) — coerente con l'assegnare a DeepSeek il lavoro più pesante. Appena il contratto A1 minimo di cui A2/A4 hanno bisogno esiste, DeepSeek parte subito, in parallelo col resto del lavoro di Claude sulla stessa fase — non in coda dopo. I task già completati da Claude "al posto di Cursor/DeepSeek" prima dell'attivazione (Q-002, Q-004, Q-008, Q-014→Q-018, Q-020, Q-022) restano `DONE`/`Integrated` così come sono — non vengono rifatti.
- **Claude**: scrive i comandi dettagliati per ogni task; scrive/esegue A1/A3 (contratti/algoritmi, tipicamente i pezzi più piccoli e per primi, per sbloccare DeepSeek il prima possibile) e A5/A6 (audit indipendente, integrazione finale). Continua a programmare in ogni fase, non solo a scrivere specifiche.
- **Cursor**: non più assegnato a nulla di nuovo dalla data sopra. Se torna disponibile, i ruoli possono essere ridistribuiti su richiesta esplicita dell'utente.
