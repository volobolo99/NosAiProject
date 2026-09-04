# NosAiProject — Roadmap Esecutiva Autonomous Player

**Versione:** 2.0  
**Data:** 2026-09-05  
**Stato:** CANONICA  
**Target:** giocatore autonomo per ambiente privato/test riproducibile

## 1. Obiettivo

Portare NosAiProject da insieme di sottosistemi a **Autonomous Player**: un agente capace di osservare il client, costruire un modello coerente del mondo, esplorare mappe sconosciute, navigare, combattere, comprendere ed eseguire missioni, gestire inventario/equipaggiamento/progressione, apprendere dalle esperienze e recuperare autonomamente dagli errori.

L'autonomia è operativa, non onniscienza: dati insufficienti o conflittuali diventano `UNKNOWN` e possono causare replan, attesa o safe-stop.

## 2. Vincolo di osservazione e controllo

### Consentito

- risorse hardware del PC: CPU, GPU/NPU, RAM, storage;
- software/API locali del PC;
- rete visibile al client;
- memoria locale del processo client quando legittimamente leggibile dal runtime;
- cattura schermo/pixel, OCR, computer vision e audio disponibile al PC;
- telemetria e memoria persistente NosAi;
- meccanismi software di controllo del client compatibili con il confine non privilegiato;
- mouse e tastiera come dispositivi **permessi ma non obbligatori**.

### Vietato

- server database, console, admin/GM/mod tools;
- API privilegiate o debug-only;
- credenziali segrete/amministrative;
- informazioni nascoste non disponibili al normale client;
- modifiche al server finalizzate a rivelare stato nascosto;
- hardware/periferiche esterne di automazione oltre a mouse e tastiera;
- qualunque scorciatoia che trasformi un'informazione privilegiata in gameplay truth.

## 3. Percorso canonico

```text
Observe
 -> Sensor Fusion
 -> World Model
 -> Simulation/Prediction
 -> Ranking/Utility
 -> Strategic Orchestrator
 -> HTN/GOAP Planner
 -> Guard
 -> Trust/Authorization
 -> Safety Gate
 -> Execute
 -> Verify
 -> Re-observe
```

Nessun LLM, modello ML, planner euristico o modulo stocastico ha autorità di esecuzione diretta.

## 4. Fasi di sviluppo

### AP-01 — Unified World Model

**Obiettivo:** modello semantico unico e versionato.

Entità minime: Player, Map, Tile/Polygon, Portal, Mob, NPC, Drop, Quest, InventoryItem, EquipmentItem, Skill, Buff, Debuff, Cooldown, Resource, Action, Goal.

Ogni fatto importante deve avere provenance, confidence, timestamp e freshness.

**DoD:** fusione Network/Memory/Screen/Local; conflitti gestiti; `UNKNOWN` preservato; snapshot/replay deterministici.

### AP-02 — Multimodal Perception

**Obiettivo:** osservazione reale del client.

Componenti:

- frame capture;
- ROI manager;
- OCR;
- object detection/tracking;
- HUD/state extraction;
- network observation;
- validated process-memory readers;
- sensor fusion e confidence scoring.

**DoD:** riconoscimento riproducibile di player, mob, NPC, target, UI essenziale e stato di combattimento in ambiente test.

### AP-03 — Map Reconstruction

**Obiettivo:** il giocatore deve saper imparare una mappa senza una mappa hardcoded.

Pipeline:

```text
Observation -> Geometry -> Walkability -> Landmarks -> Portals
            -> Map Graph -> NavMesh/Local Grid -> Persistent Map
```

Stimare dimensioni, confini, zone raggiungibili, ostacoli, transizioni e punti di interesse. Usare rappresentazione gerarchica e versionamento della mappa.

**DoD:** esplorazione parziale produce una mappa persistente con confidence e provenance; nuove osservazioni aggiornano la mappa senza distruggerne la storia.

### AP-04 — Exploration & Navigation

**Obiettivo:** raggiungere obiettivi su mappe note e sconosciute.

- hierarchical pathfinding;
- navmesh/local grid;
- dynamic obstacle handling;
- frontier exploration;
- stuck detection;
- local/global replan;
- map transition handling.

**DoD:** il sistema può scoprire, memorizzare e attraversare progressivamente una mappa senza percorsi preconfezionati.

### AP-05 — Combat Intelligence

**Obiettivo:** combattere contro classi di mob osservate e scegliere dinamicamente le azioni migliori.

```text
Combat Observation
 -> Candidate Generation
 -> Hard Constraints
 -> Short-Horizon Simulation
 -> Utility/Risk Ranking
 -> Action/Combo Prefix
 -> Execute
 -> Verify
 -> Learn
```

Il modello considera danno atteso, tempo, cooldown, MP/risorse, posizione, distanza, sopravvivenza, crowd risk, escape probability, mission objective ed esperienza storica.

La combo non è una macro fissa: è una policy adattiva con opening, continuation e recovery branches.

**DoD:** decisioni spiegabili e replayabili; aggiornamento dell'efficacia per mob/build; recovery dopo interruzione o fallimento.

### AP-06 — Quest Intelligence

**Obiettivo:** leggere, comprendere e pianificare missioni tramite sole evidenze client-observable.

```text
Quest UI/Dialog
 -> OCR/Network/Memory evidence
 -> Semantic Extraction
 -> Quest Graph
 -> Subgoals
 -> HTN/GOAP
 -> Execute
 -> Verify
```

Supportare travel, dialogue, collect, kill, interact, deliver, conditional objectives, quantities, prerequisites e multi-step chains.

**DoD:** missione non hardcoded convertita in grafo verificabile e completata quando tutte le condizioni osservabili risultano soddisfatte.

### AP-07 — Character / Inventory / Equipment Optimization

**Obiettivo:** gestire autonomamente crescita e configurazione del personaggio.

Valutare:

- statistiche;
- DPS e survivability;
- risorse;
- sinergie equipaggiamento;
- efficacia contro mob/missioni;
- costo di upgrade;
- opportunity cost;
- rischio.

Azioni: equip/unequip, confronto, uso risorse, upgrade quando disponibile, gestione inventario, vendita/conservazione quando osservabile e consentito.

**DoD:** decisioni motivate dal modello del personaggio e verificate dopo ogni modifica.

### AP-08 — Strategic Autonomy + Hierarchical Planning

**Obiettivo:** trasformare obiettivi di alto livello in piani eseguibili.

Strategic layer: utility, priorità, rischio, missioni, progressione.  
Tactical layer: HTN/GOAP.  
Reactive layer: regole deterministiche per interruzioni e recovery.

**DoD:** nessuna azione viene eseguita senza attraversare planner → guard → trust → safety.

### AP-09 — Memory / Learning / Simulation

**Obiettivo:** migliorare tra sessioni senza contaminare la truth layer.

Memorie: working, episodic, semantic, procedural, spatial, combat, quest, character, failure e reasoning.

Persistire decisione, osservazione, outcome, confidence, provenance e causa del fallimento. Retrieval ibrido e provenance-aware.

La simulazione locale valuta possibili conseguenze; non diventa mai fonte privilegiata di stato reale.

Apprendimento RL/world-model può essere sviluppato offline/sandboxed e introdotto solo dopo validazione indipendente.

**DoD:** restart/replay mantiene conoscenza utile; memoria vecchia non sovrascrive silenziosamente evidenza fresca.

### AP-10 — Full Autonomous Player Certification

Scenario end-to-end:

1. avvio pulito;
2. attach al client;
3. acquisizione osservazioni;
4. riconoscimento player/world;
5. ingresso in una mappa;
6. esplorazione e ricostruzione;
7. navigazione verso obiettivo;
8. riconoscimento e combattimento;
9. loot/inventory;
10. lettura e completamento di una missione multi-step;
11. valutazione equipaggiamento/progressione;
12. gestione errore/disconnessione/ostacolo;
13. persistenza memoria;
14. verifica completa dell'evidenza.

**Certificazione:** nessun comando di gameplay umano durante il segmento autonomo; nessun dato privilegiato; nessun hardware esterno di automazione; tutte le azioni passano dal percorso di autorizzazione e Safety.

## 5. Quality gates

Ogni fase richiede:

- build Release senza warning;
- unit + integration tests;
- test negativi/fail-closed;
- benchmark quando il componente è sul percorso critico;
- replay deterministico dove applicabile;
- evidenza reale per integrazioni client;
- provenance verificabile;
- nessuna regressione delle boundary rules.

`Present`, `Integrated`, `Done`, `Verified` restano livelli distinti. Nessuna fase è `Verified` per sola presenza del codice.

## 6. Priorità tecnica immediata

1. sostituire il GOAP prototipale con planner deterministico cost-aware e bounded;
2. collegare World Model a CharacterActionPlanner;
3. introdurre contratti Map/Entity/Combat/Quest/Inventory;
4. costruire sensor fusion reale Network/Memory/Screen;
5. introdurre Map Reconstruction + navmesh abstraction;
6. introdurre Combat Engine con simulator/ranking;
7. introdurre Quest Graph + planner;
8. introdurre Character Build Optimizer;
9. completare Memory/Failure Learning;
10. eseguire AP-10 solo dopo integrazione e test delle fasi precedenti.

## 7. Third-party policy

Consultare prima `third_party/`. Conservare sempre licenze e provenance. I file GPL/LGPL/MIT/Apache/ZLib presenti nel vault non devono essere eliminati automaticamente. Il codice terzo è riferimento finché non viene deliberatamente integrato, verificato e compatibilizzato con la boundary non privilegiata.

## 8. Document hierarchy

- `docs/ROADMAP_ESECUTIVA.md` — ordine canonico dello sviluppo;
- `docs/NOSAI_AUTONOMOUS_PLAYER_SPEC.md` — capacità e confini del prodotto;
- `docs/SOURCE_OF_TRUTH.md` — indice delle autorità;
- `docs/adr/` — decisioni architetturali accettate;
- `CLAUDE.md` e `.cursor/rules/` — regole degli agenti;
- `third_party/` — vault di codice e ricerca con provenance/licenze.
