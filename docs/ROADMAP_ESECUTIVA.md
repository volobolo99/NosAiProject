# NosAiProject — Roadmap Esecutiva Autonomous Player

**Versione:** 2.1
**Data:** 2026-09-05
**Stato:** CANONICA
**Target:** giocatore autonomo per ambiente privato/test riproducibile, ottimizzato per ASUS Nitro V16 + RTX 5060 Laptop 8 GB class + 16 GB DDR5 + SSD esterno 2 TB

## 1. Obiettivo

Portare NosAiProject a un Autonomous Player capace di percepire il client, costruire il proprio modello del mondo, scoprire mappe, stimarne dimensioni e percorribilità, navigare, combattere, comprendere ed eseguire missioni, gestire inventario/equipaggiamento/progressione, imparare dalle esperienze e recuperare autonomamente dagli errori.

L'autonomia è operativa, non onniscienza: evidenza insufficiente o conflittuale = `UNKNOWN` → attesa, riconciliazione, replan o safe-stop.

## 2. Vincoli hardware e di accesso

### Hardware target

- ASUS Nitro V16;
- AMD Ryzen, modello esatto rilevato a runtime;
- 16 GB DDR5 RAM;
- NVIDIA RTX 5060 Laptop GPU, 8 GB-class GDDR7;
- SSD esterno dedicato da 2 TB per NosAiProject.

Il runtime deve rilevare SKU, CPU, GPU, driver, TGP/power state, RAM disponibile, temperature, VRAM e modalità/velocità del collegamento SSD. Nessun valore hardware specifico deve essere hardcoded quando può variare tra configurazioni Nitro V16.

### Accesso consentito

CPU/GPU/NPU/RAM/storage del PC, API Windows, processi locali, rete visibile al client, memoria del processo client quando legittimamente leggibile, screen/pixel/OCR/CV/audio disponibile, telemetria locale, database NosAi e software di controllo compatibile con il confine non privilegiato. Mouse e tastiera sono permessi ma opzionali.

### Vietato

Server DB, console/admin/GM/mod, API privilegiate, debug/hidden flags, credenziali amministrative, server modifications per esporre stato nascosto, hardware esterno di automazione oltre ai dispositivi input consentiti, informazioni non disponibili al normale client.

## 3. Percorso canonico

`Observe → Sensor Fusion → World Model → Simulation/Prediction → Ranking/Utility → Strategic Orchestrator → HTN/GOAP → Guard → Trust → Safety → Execute → Verify → Re-observe`

Nessun LLM/ML/planner euristico ha autorità diretta di esecuzione.

## 4. Fasi

### AP-00 — Hardware & Runtime Capability Foundation

Rilevare e governare CPU/GPU/RAM/VRAM/thermal/SSD capabilities. Introdurre budget per inferenza, capture, memoria e I/O. Validare provider ONNX/Windows ML disponibili e scegliere dinamicamente il backend.

**DoD:** profilo hardware riproducibile; nessun overcommit VRAM/RAM; benchmark archiviati.

### AP-01 — Unified World Model

Modello semantico versionato per Player, Map, Tile/Polygon, Portal, Mob, NPC, Drop, Quest, InventoryItem, EquipmentItem, Skill, Buff, Debuff, Cooldown, Resource, Action e Goal. Ogni fatto importante ha provenance, confidence, timestamp e freshness.

**DoD:** fusione Network/Memory/Screen/Local; conflitti gestiti; UNKNOWN preservato; replay deterministico.

### AP-02 — Multimodal Perception

Capture via Windows Graphics Capture dove supportato, ROI manager, bounded frame queues, OCR, object detection/tracking, HUD extraction, network observation e validated memory readers.

Pipeline a cascata: change/ROI detection → lightweight detector/tracker → OCR mirato → reasoning costoso solo se necessario.

**DoD:** player/mob/NPC/target/UI/combat state riconosciuti nel test environment con provenance.

### AP-03 — Map Reconstruction

Ricostruire mappe sconosciute da osservazioni. Stimare dimensioni, confini, walkability, ostacoli, landmarks, portals e transizioni. Persistenza versionata e incrementale.

**DoD:** una mappa parzialmente esplorata può essere salvata, aggiornata e ripresa senza perdere la storia precedente.

### AP-04 — Exploration & Navigation

Hierarchical pathfinding, tiled spatial model/navmesh, local path corridor, dynamic obstacles, frontier exploration, stuck detection, local/global replan e map transitions.

**DoD:** scoperta e attraversamento progressivo di mappe senza percorso hardcoded.

### AP-05 — Combat Intelligence

Candidate generation → hard constraints → short-horizon simulation → utility/risk → combo prefix → execute → verify → learn.

Ottimizzare per DPS, time-to-kill, cooldown, risorse, survivability, positioning, crowd risk, escape probability, mission value e storico per mob/build.

**DoD:** combat policy adattiva e replayabile, con recovery e apprendimento senza bypass Safety.

### AP-06 — Quest Intelligence

OCR/UI/network evidence → semantic extraction → Quest Graph → HTN/GOAP → execute → verify. Supportare travel, dialogue, collect, kill, interact, deliver, quantities, prerequisites, rewards e catene multi-step.

**DoD:** missioni non hardcoded convertite in grafo verificabile e completate quando gli obiettivi osservabili risultano soddisfatti.

### AP-07 — Character / Inventory / Equipment

Modello di build che valuta statistiche, DPS, survivability, resource efficiency, sinergie, enemy-specific performance, movement/utility, costo upgrade, opportunity cost e quest relevance.

**DoD:** equip/upgrade/inventory actions sono motivate, autorizzate e verificate.

### AP-08 — Strategic Autonomy + Hierarchical Planning

Strategic Utility → HTN → deterministic cost-aware GOAP → reactive rules → Guard/Trust/Safety. Gestire survival, recovery, quest urgency, progression, farming, exploration e optimization in modo contestuale.

**DoD:** ogni azione live deriva da un piano verificabile e attraversa l'unico execution path autorizzato.

### AP-09 — Memory / Learning / Simulation

Working, episodic, semantic, procedural, spatial, combat, quest, character, failure e reasoning memory. Retrieval ibrido, provenance/freshness aware. Action-outcome ledger. Simulazione deterministica locale per conseguenze a breve termine.

RL/world-model learning: offline/sandboxed → evaluation → shadow policy → constrained live ranking solo dopo validazione.

**DoD:** conoscenza utile persiste tra sessioni senza contaminare la truth layer.

### AP-10 — Full Autonomous Player Certification

Scenario completo: startup → attach → perception → map discovery → exploration → navigation → target recognition → combat → loot/inventory → multi-step quest → equipment/progression → recovery → persistence → evidence.

**DoD:** segmento autonomo senza gameplay commands umani, dati privilegiati o hardware di automazione esterno; funzionamento entro i budget misurati del laptop.

## 5. Resource-aware AI policy

```text
Tier 0: deterministic rules / geometry / cached knowledge
Tier 1: lightweight local ML
Tier 2: GPU-accelerated vision/embeddings
Tier 3: expensive local reasoning
```

Ogni job dichiara CPU/GPU/VRAM/RAM/thermal/latency budget. Il runtime sceglie il tier minimo che soddisfa confidence e deadline. Tier 3 non può bloccare Safety/recovery.

16 GB RAM e 8 GB VRAM sono vincoli reali: usare bounded queues, pooled buffers, lazy loading, ROI inference, quantizzazione quando valida, frame dropping sotto pressione e background jobs preemptible.

## 6. Storage policy

SSD esterno 2 TB = storage canonico del progetto. Separare hot/warm/cold data; misurare throughput/latency reale del collegamento USB; retention bounded per replay/dataset/log; SQLite WAL/FULL per persistenza critica.

## 7. Quality gates

Ogni fase richiede build Release senza warning, unit/integration tests, test negativi/fail-closed, benchmark quando rilevante, replay deterministico, provenance verificabile e nessuna regressione dei confini di accesso.

`Present ≠ Integrated ≠ Done ≠ Verified`.

## 8. Document hierarchy

- `docs/ROADMAP_ESECUTIVA.md` — ordine canonico;
- `docs/NOSAI_AUTONOMOUS_PLAYER_SPEC.md` — capacità e confini;
- `docs/NOSAI_ARCHITECTURE_BASELINE.md` — architettura;
- `docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md` — target hardware e performance policy;
- `docs/SOURCE_OF_TRUTH.md` — autorità documentali;
- `docs/research/` — evidenza di ricerca datata;
- `docs/adr/` — decisioni architetturali;
- `CLAUDE.md` / `.cursor/rules/` — regole agenti;
- `third_party/` — vault di codice, licenze e provenance.

## 9. Immediate implementation order

1. AP-00 hardware/capability profiling;
2. World Model contracts;
3. sensor fusion + capture/ROI;
4. map reconstruction;
5. hierarchical navigation;
6. combat model + simulator/ranking;
7. quest graph + planner;
8. character/build optimizer;
9. memory/action-outcome ledger;
10. end-to-end autonomous certification.
