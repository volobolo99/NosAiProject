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

## 6. A4 (Claude, agente in background) — Wiring runtime

**File creato:** `src/NosAi.Runtime/Perception/ScreenVitalsCapture.cs` — sequenza reale: `host.Capture().Client.ProcessId` → `ClientWindowLocator.TryFind` → `DxgiDesktopDuplicationSource.TryCreate` (aperto una sola volta, riusato; ritentato da zero se la creazione fallisce) → `PerceptionPipeline` con `NullObjectDetector` (nessun modello ONNX addestrato esiste — scelta onesta, non un compromesso) → `ScreenVitalReader.Read(...)` sullo stesso frame → `VisualObservation`. Ogni modo di fallimento (process id assente, piattaforma non Windows, finestra non trovata, DXGI non disponibile, frame non acquisito) produce `VisualObservation.Unobserved(reason)` con motivo specifico, mai un'eccezione.

**File modificati (solo additivi):**
- `src/NosAi.Runtime/WorldModel/Fusion/WorldModelFusionLoop.cs` — nuovo parametro opzionale del costruttore `Func<VisualObservation>? visualSource`; se fornito, dopo il ciclo rete+temporal-belief applica `VisualObservationFusion.FuseVitals`. Un'eccezione da `visualSource` non fa fallire il ciclo rete (loggata, sostituita con `Unobserved`). Nessun default cambiato → nessuna regressione per chi non lo passa.
- `src/NosAi.Runtime/Program.cs` — se `--fuse-world-model` è attivo, costruisce anche un `ScreenVitalsCapture` e lo passa come `visualSource`. Nessun nuovo flag CLI.

**Limite dichiarato**: `HasTarget` prodotto da `ScreenVitalsCapture` resta sempre `Unknown` — `TargetStateComposer.Compose` richiede una `TargetRoiCalibration` calibrata che nessun codice di questo pass costruisce. Fail-closed su Linux confermato con test reale (`ScreenVitalsCaptureTests.cs`), non solo dichiarato.

## 7. A5 (Claude, agente in background) — Audit indipendente

42 nuovi test, **2 difetti reali trovati** (stessa classe di bug del leak wall-clock già corretta 4 volte in AP-01/A6, ora in un quarto e un quinto punto di chiamata mai toccati prima), con evidenza empirica, non solo ispezione. Report completo: `docs/agents/phases/AP-02/AP-02_A5_AUDIT.md`.

1. `WorldModelTemporalEnricher.EnrichMobs` (riga 68): il ramo "nessun avvistamento precedente" ometteva l'istante nella chiamata `Unknown(...)`, facendo trapelare `DateTime.UtcNow` reale — quarto punto di chiamata con lo stesso difetto strutturale di AP-01/A5, mai coperto prima perché `Mobs` è sempre vuoto nel wiring odierno; trovato incatenando le primitive come richiesto dal comando.
2. `GameplayObservation.Unobserved` (`GameplayProvider.cs`): **tutti e 16** i campi per-campo chiamavano `ClassifiedValue<T>.Unknown(reason)` ignorando il parametro esplicito `atUtc` del metodo — solo il campo posizionale `ObservedAtUtc` lo rispettava. Rompeva il determinismo di `GameplayObservationProjector.Project` ogni volta che si costruisce una baseline `Unobserved` sovrascrivendo solo alcuni campi — esattamente il pattern usato da molti test di questa stessa sessione (`GameplayObservation.Unobserved("reason", Now) with { Hp = ... }`).

Verificato inoltre, senza trovare difetto: `ClassifiedValueBridge` equivalente byte-per-byte al vecchio codice privato pre-refactor; `ResourceKind.Custom` preservato intatto nella fusione; entrambi i canali più vecchi di `maxAge` → onestamente `Unknown`; confidence persa nel bridge (sempre 1.0) è un limite accettato e motivato, non un difetto (segnalato come rischio futuro per AP-05).

## 8. A6 (Claude) — Integrazione finale AP-02

Applicate entrambe le correzioni suggerite dall'audit A5:

1. **`src/NosAi.Core/WorldModel/Temporal/WorldModelTemporalEnricher.cs`** — `EnrichMobs`: il ramo senza avvistamento precedente ora passa esplicitamente `mob.Position.ObservedAtUtc`.
2. **`src/NosAi.Runtime/Contracts/DataClassification.cs`** — `ClassifiedValue<T>.Unknown` guadagna un parametro opzionale `observedAtUtc` (stessa forma già offerta da `WorldFact<T>.Unknown` in Core.WorldModel), backward-compatible (276 siti di chiamata esistenti invariati, nessuno passa il nuovo parametro). **`src/NosAi.Runtime/LiveIntegration/GameplayProvider.cs`** — `GameplayObservation.Unobserved` ora risolve l'istante una sola volta e lo passa a tutti e 17 i campi classificati, inclusi i 10 aggiuntivi C1.

**Evidenza:**
```
dotnet build src/NosAi.Core/NosAi.Core.csproj -c Release      → 0 Warning(s), 0 Error(s)
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release → 0 Warning(s), 0 Error(s)

dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~MultimodalPipelineDeterminismTests"
  → Passed! Failed: 0, Passed: 5, Total: 5   (entrambi i difetti ora verdi)

dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
  → Passed! Failed: 0, Passed: 357, Skipped: 0, Total: 357

dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
  → Passed! Failed: 0, Passed: 1880, Skipped: 58, Total: 1938
```

Nessuna regressione: tutti i test pre-esistenti di A1/A2/A3/A4/A5 restano verdi.

## 9. Livello di verifica finale — AP-02 (Multimodal Perception, ambito vitali)

**`Integrated`**: A1 (contratto `VisualObservation`) + A3 (fusione vitali) + A4 (wiring runtime con capture DXGI reale, fail-closed su Linux) costruiscono un albero unico che compila pulito e passa tutti i test combinati, inclusi i 2 difetti reali trovati dall'audit indipendente A5 e corretti in questo passaggio. **Non `Verified`**: nessuna validazione contro un client NosTale reale — l'intero percorso DXGI/`ClientWindowLocator` resta esercitato solo nel suo ramo di fallimento onesto in questo ambiente Linux.

## 10. Item aperti, esplicitamente rimandati

- **OCR reale e decoder ONNX addestrato**: problema di dati/ML, non di architettura — nessun modello/training pipeline esiste in questo repository. Bloccante per qualunque classificazione Mob/NPC/oggetti da visione.
- ~~**`HasTarget` da `TargetStateComposer`**: non cablato in `ScreenVitalsCapture`~~ — **risolto** (Q-067, DeepSeek, commit `4249418`, audit A5 senza difetti). Vedi §11.
- **Inventario da schermo**: **non vale la pena** (indagine, questa sessione) — il canale di rete (`ivn`/`get`/`drop`, già usato da `--collect`) fornisce già conteggi esatti; un reader da schermo darebbe solo un'icona presente/assente su un dato già migliore.
- ~~**Finestre di dialogo**: nessun codice di lettura esiste~~ — **contenuto testuale** resta bloccato da OCR/ML (nessun canale di rete per dialogo/quest text). **Presenza/assenza** di un pannello aperto costruita in questo passaggio (A1+A3, Claude) — vedi §12.
- **DirectX draw-call interception** (ADR-0022 "Reading the world from what the client draws"): **proposta ma non adottata**, esplicitamente gated su un esperimento che spetta all'operatore umano, non a un agente ("The experiment is the operator's, not an agent's"). Nessun lavoro di questa fase tenta hook/injection — la capture resta esclusivamente DXGI Desktop Duplication (cattura legittima dei pixel), mai intercettazione di chiamate di disegno.
- **Gestione `DXGI_ERROR_ACCESS_LOST` a metà sessione** (lock/unlock desktop, reset GPU): `ScreenVitalsCapture` non la distingue da un frame semplicemente non disponibile — richiederebbe modificare `DxgiCapture.cs`, fuori ambito per questo pass (sola lettura).
- ~~Triplicazione di `DataSourceKind`~~ — **chiusa** con `docs/adr/ADR-0026-datasourcekind-intentional-bounded-context-duplication.md`: confermata duplicazione intenzionale per bounded context, non un difetto.

## 11. A2+A4 addendum — `TargetStateComposer` cablato in `ScreenVitalsCapture` (Q-067)

Gap dichiarato al §10 chiuso: `HasTarget` non era più `Unknown` per una
ragione di codice (`"target_state_composer_not_wired_in_this_pass"`), ma
solo per la ragione di dati onesta già gestita da
`TargetStateComposer.Compose` stesso
(`target_roi_not_calibrated`, finché un operatore non calibra via
`HudProbe`). Indagine preliminare (questa sessione) ha trovato il
meccanismo già completamente reale e già in uso lato wire
(`TargetAwareGameplayProvider`) — nessun nuovo tipo di dominio necessario,
solo wiring.

Consegnato da DeepSeek (commit `4249418`): `ScreenVitalsCapture` accetta
ora `targetCalibration`/`wire` opzionali; nuovo `SingleFrameSource`
(nested, privato) preserva l'invariante "un frame, più lettori" — nessuna
seconda acquisizione DXGI reale per ciclo. `wire: null` esplicito e
documentato (nessun `IPlayerAttackObserver` disponibile al punto di
composizione in `Program.cs` oggi — stessa degradazione già accettata da
`TargetAwareGameplayProvider`).

**Audit indipendente (Claude, A5)**: rilettura riga per riga dei 3 file
diff-ati contro la specifica
(`AP-02_A2A4_DEEPSEEK_target_state_wiring.md`), poi build/test in un
worktree isolato puntato su `origin/main`. **Nessun difetto trovato** —
terza consegna DeepSeek pulita di questa sessione (dopo `--recover` e il
ledger wiring AP-09). Verificato: nessuna seconda acquisizione frame
(`SingleFrameSource` restituisce sempre lo stesso `CaptureFrame`), i due
nuovi test di regressione provano che i parametri opzionali sono inerti
sul percorso fail-closed esistente (nessuna regressione sulle ragioni di
rifiuto già testate), il secondo test è scritto per essere
host-indipendente (funziona sia su questo sandbox Linux sia su un
eventuale host Windows reale) — un miglioramento non richiesto dalla
specifica ma corretto.

**Evidenza build/test (indipendente)**:
```
dotnet build NosAi.sln -c Release → 0 Errori, 1 Warning preesistente non collegato.
dotnet test .../NosAi.Runtime.Tests.csproj --filter "~ScreenVitalsCaptureTests" → 11/11
dotnet test .../NosAi.Runtime.Tests.csproj → 2024/2082, 0 falliti, 58 skip
dotnet test .../NosAi.Core.Tests.csproj → 623/623, 0 falliti
```

**Limite dichiarato, invariato**: il valore composto reale di `HasTarget`
(`Derived(true/false)` una volta calibrato) resta non esercitato da
nessun test in questo ambiente — stesso limite già dichiarato per il
resto del ramo "frame acquisito" di `Capture()` (la lettura vitali sulla
stessa riga soffre della stessa cosa). Non peggiorato da questa consegna,
non risolto da essa. Livello: `Present` per il codice nuovo, `Integrated`
solo con conferma umana su un client Windows reale con una calibrazione
reale.

## 12. A1+A3 — Rilevamento presenza/assenza finestra di dialogo (nuova capacità)

Su richiesta esplicita dell'utente (nessun client reale disponibile per
verificare la tecnica: approvato comunque, calibrazione da operatore
richiesta prima che funzioni davvero). Chiude la seconda metà del gap
§10 "Finestre di dialogo": il contenuto testuale resta bloccato da
OCR/ML, ma la sola presenza/assenza di un pannello aperto è leggibile da
schermo senza OCR.

**Perché una tecnica diversa da `TargetFrameReader`**: la barra HP/MP ha
una famiglia di colori fissa e nota (rosso/verde, `HudBarFillReader`) —
un pannello di dialogo no, e indovinare l'estetica UI reale di NosTale
senza un client per verificarla avrebbe violato la disciplina
anti-fabbricazione del progetto. Tecnica scelta: **delta da baseline** —
la ROI calibrata registra una volta la propria media colore B/G/R su un
crop confermato "vuoto" dall'operatore; ogni lettura successiva calcola
la propria media B/G/R sull'intero crop e la confronta per distanza
euclidea con la baseline; sopra una soglia (`DefaultPresentThreshold =
24.0`, dichiarata esplicitamente scelta di giudizio non calibrata, non
una costante misurata) è `Present`, altrimenti `Absent`. Agnostica
rispetto all'estetica del gioco — funziona su qualunque pannello che
cambi visibilmente i pixel della propria regione.

**File consegnati** (`src/NosAi.Runtime/Perception/`):
- `DialogRoiCalibration.cs` — stesso schema di `TargetRoiCalibration`
  (fatta salva l'estensione con `BaselineMeanB/G/R`), stesso formato file
  a righe con intestazione magica (`nosai-dialog-roi`, versione 1),
  `RelativePath = data/perception/dialog-roi.calibration` (gitignored,
  machine-specific), `Uncalibrated` singleton come default onesto,
  `Confirmed(...)` valida sia i limiti della regione sia il range 0-255
  di ogni media di canale.
- `DialogWindowReader.cs` — puro, deterministico: `Read(bgra, width,
  height, baselineMeanB, baselineMeanG, baselineMeanR, presentThreshold)`
  calcola la media B/G/R sull'intero crop in aritmetica `long` (stessa
  disciplina anti-overflow di `TargetFrameReader`), poi la distanza
  euclidea dalla baseline. Confine incluso (`divergence == threshold` →
  `Absent`, non `Present`).
- `DialogWindowStateComposer.cs` — `Compose(calibration, observation) ->
  ClassifiedValue<bool>`. **Nessuna riconciliazione lato wire** (a
  differenza di `TargetStateComposer`): nessun opcode NosTale per
  dialogo/quest text esiste in questo repository (verificato per grep su
  `PROTOCOLLO_NOSTALE.md`/`NosTaleWorldProtocolDecoder.cs`) — lo schermo
  è l'unica fonte, quindi il composer traduce soltanto lo stato del
  reader, non lo contraddice né lo conferma.
- `ScreenDialogWindowSource.cs` — stesso ordine di controlli di
  `ScreenTargetFrameSource` (calibrazione → area client → frame →
  risoluzione ROI → dentro il frame), stesso pattern `Refused(reason,
  atUtc)`. Verificato per audit incrociato: il ramo "area client
  degenere" riusa `DialogRoiCalibration.NotCalibratedReason` esattamente
  come `ScreenTargetFrameSource` fa con `TargetRoiCalibration` — non un
  difetto, fedeltà intenzionale al precedente.

**Test**: `DialogRoiCalibrationTests.cs`, `DialogWindowReaderTests.cs`,
`DialogWindowStateComposerTests.cs`, `ScreenDialogWindowSourceTests.cs` —
38 test nuovi, tutti verdi.

**Evidenza build/test**:
```
dotnet build NosAi.sln -c Release → 0 Errori, 1 Warning preesistente non collegato
dotnet test .../NosAi.Runtime.Tests.csproj --filter "~Dialog" → 38/38
dotnet test .../NosAi.Runtime.Tests.csproj → 2062/2120, 0 falliti, 58 skip
dotnet test .../NosAi.Core.Tests.csproj → 630/630, 0 falliti
```

**Livello**: `Present` — contratti e algoritmo puri, testati, compilano
puliti. Non `Integrated`: nessun chiamante runtime ancora (wiring in
`ScreenVitalsCapture`/`Program.cs` da specificare per DeepSeek, stesso
schema di Q-067). Non `Verified`: nessun client reale per confermare
`DefaultPresentThreshold` contro un vero pannello NosTale — obbligo
esplicito prima di fidarsi del risultato in produzione.
