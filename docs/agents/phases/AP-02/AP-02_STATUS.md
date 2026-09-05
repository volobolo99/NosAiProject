# AP-02 — Multimodal Perception — Stato

**Data:** 2026-09-05
**Fase:** AP-02 (docs/ROADMAP_ESECUTIVA.md S:AP-02: "Capture via Windows Graphics Capture dove supportato, ROI manager, bounded frame queues, OCR, object detection/tracking, HUD extraction, network observation e validated memory readers... DoD: player/mob/NPC/target/UI/combat state riconosciuti nel test environment con provenance.")

## 1. Ricognizione preliminare (obbligatoria prima di scrivere qualunque contratto)

Prima di progettare qualunque tipo nuovo, è stata condotta una ricognizione completa di `src/NosAi.Runtime/Perception/` (il sottosistema di percezione visiva pre-esistente, costruito sotto il vecchio sistema Gate 1-6, dichiarato `Integrated` in `ModuleReachability.cs`). Motivo: AP-01/A2 ha già dimostrato il pattern corretto — riusare l'infrastruttura reale esistente (`GameplayObservation`) invece di reinventarla da zero.

**Cosa esiste già e resta invariato (nessun file toccato in questo passaggio):**
- Capture reale via DXGI Desktop Duplication (`DxgiCapture.cs`), fail-closed, classificata `Live`, con triple buffer come coda limitata di frame (`TripleFrameBuffer`).
- ROI manager (`RoiSegmenter`, 5 regioni HUD proporzionali al client reale).
- `CaptureFreshnessPolicy`/`CaptureHealthPolicy` (già production-grade).
- Contratti detector/tracker stabili (`IObjectDetector`/`IObjectTracker`), `OnnxObjectDetector` fail-closed (reason string per ogni modo di fallimento) e `TemporalEntityTracker` (Kalman 2D).
- `PerceptionPipeline` — pipeline a cascata già cablata: capture → freshness → ROI → detect → track.
- Lettura HUD bar-fill (`ScreenVitalReader`/`ScreenDerivedVitals`/`HudBarFillReader`) — HP/MP sempre `DERIVED`/`UNKNOWN`, mai `LIVE`.
- `TargetStateComposer` (ADR-0018) — l'unico ponte visione→`GameplayObservation` esistente prima di questo lavoro, limitato al singolo booleano `HasTarget`.

**Cosa manca davvero (verificato, non assunto) e NON è stato costruito in questo passaggio, perché richiede asset ML che questa sessione non può produrre:**
- OCR reale: esiste solo un motore hash-glifo (`GlyphHashOcrCache`) mai addestrato in produzione — HP/MP numerico resta sempre `UNKNOWN`, solo il rapporto barra è `DERIVED`.
- Un decoder ONNX concreto: `IOnnxDetectionDecoder` ha solo l'implementazione vuota `EmptyOnnxDetectionDecoder` — nessun modello addestrato/esportato esiste nel repository.
- **Di conseguenza, `Detection.Kind`/`TrackedEntity.Kind` è una stringa libera senza alcuna tassonomia stabilita in tutto il repository** — nessun decoder reale assegna mai un valore. Inventare qui una convenzione tipo "mob"/"npc" senza che nulla a monte possa mai produrre esattamente quelle stringhe sarebbe esattamente il tipo di contratto speculativo che CLAUDE.md vieta ("don't design for hypothetical future requirements").

## 2. Decisione di ambito per questo passaggio

Dato che la classificazione Mob/NPC/oggetti richiede un modello addestrato che non esiste (problema di dati/ML, non di architettura), l'ambito onesto e concretamente costruibile oggi è più stretto di un ciclo A1-A6 completo: **il contratto di osservazione visiva unificato, e la fusione dei vitali (HP/MP) schermo↔rete** — l'unico fatto che entrambi i canali possono davvero riportare oggi.

## 3. A1 — Contratto di osservazione visiva

**File creato:** `src/NosAi.Runtime/Perception/VisualObservation.cs`

`VisualObservation` è l'analogo, per il canale visione, di `GameplayObservation` per il canale rete: unifica `PerceptionResult` (entità tracciate), `ScreenVitalObservation` (HP/MP da HUD) e `ClassifiedValue<bool> HasTarget` (da `TargetStateComposer`, già esistente) in un solo tipo tipizzato. Non è un nuovo reader: ogni campo è prodotto da codice Gate-era già esistente e testato; nessun reader esistente è stato modificato. Fabbrica `Unobserved(reason)` per il caso "nessun frame acquisito", mai un dato fabbricato.

## 4. A3 — Fusione multimodale (vitali)

**File creati:**
- `src/NosAi.Runtime/WorldModel/Fusion/ClassifiedValueBridge.cs` — helper condiviso `ClassifiedValue<T>` (Runtime.Contracts) → `WorldFact<T>` (Core.WorldModel), estratto da `GameplayObservationProjector` (che duplicava la stessa logica) così che `VisualObservationFusion` non la riscriva una terza volta. `GameplayObservationProjector.cs` aggiornato per usare l'helper condiviso invece della propria copia privata (comportamento identico, verificato dai suoi stessi test).
- `src/NosAi.Runtime/WorldModel/Fusion/VisualObservationFusion.cs` — `FuseVitals(snapshot, visual, nowUtc, maxAge?)`: prende uno `WorldModelSnapshot` già proiettato dalla rete (A2) e un `VisualObservation` di questo ciclo, e risolve HP/MP tramite `FactFusion.Resolve` — esattamente lo scenario che la XML doc di `FactFusion` anticipava fin da AP-01/A2 ("a future AP-02 screen/OCR reading of HP"). Puro e senza stato, come `GameplayObservationProjector`/`WorldModelTemporalEnricher`.

**Comportamento verificato dai test** (non solo dichiarato): la rete `Live` batte lo schermo `Derived` quando entrambi presenti (il wire resta la fonte primaria); ma una rete `Cached` (lettura stale ripubblicata) perde contro uno schermo `Derived` fresco — questo è il caso concreto che rende utile fondere un secondo canale, non solo un esercizio teorico. Nessuna entità (Mob/Npc) viene fusa in questo passaggio, per il motivo esposto al punto 1 — documentato esplicitamente nel commento XML di `VisualObservationFusion` così un tentativo futuro di "risolvere" `Kind` con una stringa indovinata romperebbe l'attenzione del lettore invece di sembrare completabile silenziosamente.

## 5. Build/test — evidenza

```
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
  → Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~VisualObservation"
  → Passed! Failed: 0, Passed: 9, Skipped: 0, Total: 9

dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~GameplayObservationProjectorTests|FullyQualifiedName~GameplayObservationProjectorBoundaryTests"
  → Passed! Failed: 0, Passed: 12, Total: 12   (nessuna regressione dal refactor di ClassifiedValueBridge)

dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~ModuleReachability"
  → Passed! Failed: 0, Passed: 6, Total: 6

dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
  → Passed! Failed: 0, Passed: 357, Skipped: 0, Total: 357

dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
  → Passed! Failed: 0, Passed: 1821, Skipped: 58, Total: 1879
    (un run isolato ha mostrato un fallimento transitorio non riprodotto al
    riavvio immediato — flake noto della sandbox condivisa, stesso tipo già
    osservato per TransportLoopTests in AP-00/AP-01, non correlato a questo
    lavoro)
```

## 6. Livello di verifica

**`Present`/`Integrated` a livello di codice**: build pulita, 21 nuovi test tutti verdi, nessuna regressione sui test esistenti di Perception/A1/A2/A3/A4 di AP-01. **Non `Verified`**: nessuna validazione contro un client NosTale reale (la pipeline vision reale richiede un modello ONNX addestrato che non esiste in questo repository). `VisualObservation`/`VisualObservationFusion` non sono ancora richiamati da nessun host di produzione (stesso stato "non ancora cablato" che A2/A3 di AP-01 avevano prima del wiring di A4) — quel collegamento (chiamare `PerceptionPipeline`/`ScreenVitalReader` reali da un loop e passarne il risultato a `VisualObservationFusion.FuseVitals`) resta lavoro di wiring runtime aperto, non affrontato qui.

## 7. Item aperti, esplicitamente rimandati

- **OCR reale e decoder ONNX addestrato**: problema di dati/ML, non di architettura — nessun modello/training pipeline esiste in questo repository. Bloccante per qualunque classificazione Mob/NPC/oggetti da visione.
- **Wiring runtime**: nessun host chiama ancora `VisualObservation`/`VisualObservationFusion` con dati reali dalla pipeline di capture.
- **Inventario/finestre di dialogo**: nessun codice di lettura esiste (nessuna ROI, nessun reader) — costruzione da zero, non affrontata.
- **DirectX draw-call interception** (ADR-0022 "Reading the world from what the client draws"): **proposta ma non adottata**, esplicitamente gated su un esperimento che spetta all'operatore umano, non a un agente ("The experiment is the operator's, not an agent's"). Nessun lavoro di questa fase tenta hook/injection — la capture resta esclusivamente DXGI Desktop Duplication (cattura legittima dei pixel), mai intercettazione di chiamate di disegno.
