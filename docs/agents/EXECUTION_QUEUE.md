# NosAiProject — Coda di esecuzione multi-agente

**Scopo:** un solo file da cui partire. Ogni voce ha un ID, chi la esegue, da cosa dipende, e dove sta il comando dettagliato. Aggiornato da Claude ad ogni avanzamento — questo file è la fonte di verità su "cosa è il prossimo passo", non `docs/agents/AGENT_COMMAND_REGISTRY.md` (che resta la mappa di ownership per dominio, statica) né i singoli file in `docs/agents/phases/` (che restano i comandi dettagliati per singolo task).

**Come si usa:** guarda la prima voce con stato `PENDING`. Se l'esecutore è `Claude`, dillo a Claude di partire (es. "vai con Q-004"). Se è `DeepSeek`, apri il file comando indicato e passalo a DeepSeek. Quando un task finisce, l'esecutore aggiorna lo stato qui.

**Stati:** `PENDING` (non iniziato) · `IN_PROGRESS` · `DONE` · `BLOCKED` (dipendenza non soddisfatta).

---

## Coda attiva (AP-00 → AP-02)

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
| Q-013 | AP-02 | A1 — Contratti Perception (osservazioni multimodali con provenance) | **Claude** | Q-012 | ricognizione infrastruttura Perception esistente in corso, comando da scrivere subito dopo | IN_PROGRESS |
| Q-014 | AP-02 | A2 — Screen/OCR/CV adapters (client-observable) | Claude (Cursor non disp.) | Q-013 | da scrivere quando si arriva qui | PENDING |
| Q-015 | AP-02 | A3 — Fusione multimodale, confidence e gestione contraddizioni | Claude | Q-013 | da scrivere quando si arriva qui | PENDING |
| Q-016 | AP-02 | A4 — Runtime ingestion, throttling, lifecycle wiring | Claude (Cursor non disp.) | Q-013, Q-014 | da scrivere quando si arriva qui | PENDING |
| Q-017 | AP-02 | A5 — Test/benchmark/doc AP-02 | Claude | Q-013, Q-014, Q-015, Q-016 | da scrivere quando si arriva qui | PENDING |
| Q-018 | AP-02 | A6 — Integrazione finale AP-02 | Claude | Q-017 | da scrivere quando si arriva qui | PENDING |

## Regola per Q-014/Q-015/Q-016/Q-017/Q-018 e per tutte le fasi successive

I comandi dettagliati per i task oltre Q-008 **non sono ancora scritti di proposito**: scriverli ora, prima che i contratti di AP-01 esistano davvero, rischierebbe di fissare dettagli sbagliati che poi vanno disfatti (esattamente il tipo di "casino" da evitare). La regola è: **quando una voce `PENDING` diventa la prima della coda, Claude scrive il suo comando dettagliato (stile dei file già prodotti oggi, non gli stub telegrafici originali) prima di farla partire**, poi la esegue lui stesso o la assegna a DeepSeek a seconda della colonna Esecutore.

## Fasi successive (AP-03 → AP-10) — solo sequenza, nessun dettaglio ancora

Dalla roadmap canonica (`docs/ROADMAP_ESECUTIVA.md`). AP-02 è ora attiva (Q-013+ sopra); le fasi seguenti non hanno ancora task numerati in coda — verranno aggiunti qui, con lo stesso formato sopra, quando AP-02 sarà `Integrated`.

| Fase | Obiettivo |
|---|---|
| AP-03 | Map Reconstruction — mappe salvabili/aggiornabili incrementalmente |
| AP-04 | Exploration & Navigation — scoperta/attraversamento senza percorsi hardcoded |
| AP-05 | Combat Intelligence — policy di combattimento adattiva con recovery |
| AP-06 | Quest Intelligence — missioni non hardcoded → grafo verificabile |
| AP-07 | Character/Inventory/Equipment — azioni di equip motivate e verificate |
| AP-08 | Strategic Autonomy + HTN — ogni azione deriva da un piano verificabile |
| AP-09 | Memory/Learning/Simulation — conoscenza persistente cross-sessione |
| AP-10 | Full Autonomous Certification — scenario end-to-end senza comandi umani |

## Criterio di avanzamento fase

Una fase (AP-XX) non inizia finché la precedente non è almeno `Integrated` (build+test combinati verdi) — mai `Verified` come precondizione di partenza, dato che `Verified` richiede hardware reale non disponibile in questo ambiente di sviluppo. Questo è il criterio già in uso oggi (AP-00 Q-006 come gate prima di iniziare AP-01 Q-007).

## Chi fa cosa, in breve

- **DeepSeek**: scrive la maggior parte dei singoli file di implementazione nuovi, quando il task è assegnato a lui in questa coda.
- **Claude**: scrive i comandi dettagliati per ogni task man mano che diventa il prossimo in coda; esegue i task assegnati a "Claude"; per i task DeepSeek fa da integratore — collegamenti tra moduli, deduplicazione, risoluzione conflitti, build/test finale, commit.
- **Cursor**: non disponibile al momento di scrivere questo file. I suoi ruoli originali in AP-00 (A2, A4) sono stati eseguiti da Claude per non bloccare la coda; se torna disponibile, i ruoli futuri assegnati a "DeepSeek" possono essere ridistribuiti a Cursor su richiesta.
