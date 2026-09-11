# NosAi — WORKLOG

Registro operativo permanente delle modifiche al repository.

Regola: ogni intervento deve aggiungere una voce con:
- data;
- obiettivo;
- file toccati;
- perché;
- cosa è stato fatto in breve;
- stato/verifica.

---

## 2026-09-05 — Introduzione registro operativo

**Obiettivo:** creare un punto unico dove ChatGPT/Claude/Cursor possano vedere rapidamente cosa è stato modificato nel progetto.

**File toccati**
- `docs/WORKLOG.md` — creato.

**Perché**
- Evitare modifiche non tracciate e rendere più semplice capire cronologia, motivazione e impatto dei lavori.

**Cosa è stato fatto**
- Creato questo registro permanente.
- Da questo intervento in poi ogni lavoro deve essere annotato qui.

**Stato**
- COMPLETATO.


## 2026-09-05 — Perception: freshness gate dei frame

**Obiettivo:** impedire che frame vecchi o con timestamp anomali entrino nella pipeline percettiva e quindi nel WorldState.

**File toccati**
- `src/NosAi.Runtime/Perception/CaptureFreshnessPolicy.cs` — creato.
- `src/NosAi.Runtime/Perception/PerceptionPipeline.cs` — modificato.
- `tests/NosAi.Runtime.Tests/CaptureFreshnessPolicyTests.cs` — creato.
- `docs/WORKLOG.md` — aggiornato.

**Perché**
- Un backend di cattura può temporaneamente fornire dati stantii dopo lag, desktop switch o rallentamenti.
- La pipeline deve essere fail-closed anche sul tempo: un frame disponibile ma troppo vecchio non è una osservazione affidabile.
- Un timestamp troppo nel futuro indica clock/skew anomalo e non deve essere accettato silenziosamente.

**Cosa è stato fatto**
- Aggiunta `CaptureFreshnessPolicy` con `MaxAge` e `FutureTolerance` configurabili.
- Default: frame massimo 500 ms, tolleranza timestamp futuro 100 ms.
- `PerceptionPipeline` ora valida temporalmente ogni frame prima di ROI/detection/tracking.
- Frame stale -> `Unknown` con `stale_frame_rejected`.
- Timestamp futuro oltre tolleranza -> `Unknown` con `future_timestamp_rejected`.
- Aggiunta injection del clock per test deterministici.
- Aggiunti test per frame fresh/stale/future e per verificare che il detector non venga eseguito su frame rifiutati.

**Stato**
- IMPLEMENTATO.
- Test aggiunti al progetto xUnit; esecuzione CI da verificare sul workflow successivo.


## 2026-09-05 — Perception: capture health telemetry

**Obiettivo:** rendere il backend di cattura osservabile e diagnosticabile durante l'esecuzione.

**File toccati**
- `src/NosAi.Runtime/Perception/CaptureHealth.cs` — creato.
- `src/NosAi.Runtime/Perception/DxgiCapture.cs` — modificato.
- `tests/NosAi.Runtime.Tests/CaptureHealthTests.cs` — creato.
- `src/NosAi.Runtime/Perception/PerceptionPipelineTestRunner.cs` — modificato.
- `docs/WORKLOG.md` — aggiornato.

**Perché**
- Il triple buffer può perdere frame per backpressure senza che questo significhi automaticamente errore.
- Il backend può invece soffrire starvation/fallimenti di acquisizione prolungati.
- Serviva distinguere in modo deterministico HEALTHY / DEGRADED / UNHEALTHY senza alterare o inventare dati percettivi.

**Cosa è stato fatto**
- Aggiunto `CaptureHealthPolicy` e `CaptureHealthSnapshot`.
- Aggiunte metriche: acquisizioni riuscite, frame pubblicati, frame scartati, fallimenti acquisizione, drop ratio e failure ratio.
- Aggiunta classificazione:
  - `Healthy` — funzionamento normale;
  - `Degraded` — backpressure/failure rate elevato;
  - `Unhealthy` — starvation o drop severo.
- Aggiunto stato `warming_up` per evitare falsi allarmi con pochi campioni.
- `TripleBufferedCapture` ora espone `SuccessfulAcquisitions` e `GetHealthSnapshot()`.
- Estesa la suite di certificazione Perception con il controllo della health classification.
- Aggiunti test xUnit dedicati.

**Stato**
- IMPLEMENTATO su `main`.
- Verifica strutturale completata; stato CI verificato separatamente dopo il commit.


## 2026-09-05 — Perception: detector/tracker production boundary

**Obiettivo:** rendere la pipeline percettiva indipendente da uno specifico motore di object detection o tracking, così da poter collegare ONNX Runtime, DirectML/CUDA/TensorRT e un futuro adapter ByteTrack senza riscrivere il core della pipeline.

**File toccati**
- `src/NosAi.Runtime/Perception/DetectionContracts.cs` — creato.
- `src/NosAi.Runtime/Perception/PerceptionPipeline.cs` — modificato.
- `tests/NosAi.Runtime.Tests/DetectionBoundaryTests.cs` — creato.
- `docs/WORKLOG.md` — aggiornato.

**Perché**
- La pipeline precedente riceveva direttamente un `Func<CaptureFrame, IReadOnlyList<Detection>>`, sufficiente per test ma troppo accoppiato per un backend di inferenza produttivo.
- Il tracker concreto `TemporalEntityTracker` era incorporato come tipo specifico.
- Servono contratti stabili per poter benchmarkare e sostituire detector/tracker in base all'hardware senza modificare il ciclo percettivo.

**Cosa è stato fatto**
- Creato `IObjectDetector` con nome backend e metodo `Detect`.
- Creato `IObjectTracker` con `ActiveTrackCount` e metodo `Track`.
- Creato `DelegateObjectDetector` per mantenere compatibilità con il codice/test esistente.
- Creato `NullObjectDetector` fail-closed: nessun modello disponibile significa zero detection, non dati inventati.
- `TemporalEntityTracker` implementa ora `IObjectTracker` mantenendo invariata la sua API pubblica precedente.
- `PerceptionPipeline` dipende ora dai contratti e non da implementazioni specifiche.
- Conservato il costruttore basato su delegate per backward compatibility.
- Aggiunti test per detector sostituibile, tracker sostituibile, compatibilità delegate e comportamento fail-closed del null detector.

**Stato**
- IMPLEMENTATO su `main`.
- Boundary pronta per adapter ONNX/ByteTrack.
- Test xUnit aggiunti; stato CI verificato separatamente.


## 2026-09-05 — Perception: primo adapter ONNX dietro IObjectDetector

**Obiettivo:** collegare un runtime di inferenza ONNX alla nuova boundary `IObjectDetector` senza accoppiare NosAi a YOLO o a una specifica famiglia di modelli.

**File toccati**
- `src/NosAi.Runtime/Perception/OnnxObjectDetector.cs` — creato.
- `src/NosAi.Runtime/NosAi.Runtime.csproj` — modificato.
- `tests/NosAi.Runtime.Tests/OnnxObjectDetectorTests.cs` — creato.
- `src/NosAi.Runtime/Perception/PerceptionPipelineTestRunner.cs` — modificato.
- `docs/WORKLOG.md` — aggiornato.

**Perché**
- La production boundary detector/tracker era pronta ma non esisteva ancora un backend di inferenza reale.
- NosAi deve poter cambiare modello/provider tramite AutoSet senza riscrivere la pipeline.
- Le semantiche degli output variano fra YOLO, RT-DETR e altri modelli: il runtime non deve assumerne una specifica.

**Cosa è stato fatto**
- Aggiunta dipendenza `Microsoft.ML.OnnxRuntime 1.29.0`.
- Creato `OnnxObjectDetector : IObjectDetector, IDisposable`.
- Creato `OnnxDetectorOptions` per model path, input name, width/height e pixel scale.
- Creato `IOnnxDetectionDecoder`: la sessione ONNX produce tensor output grezzi e il decoder specifico del modello li converte in `Detection`.
- Creato `OnnxTensorOutput` per separare gli output dal lifetime nativo di ONNX Runtime.
- Creato preprocessing deterministico BGRA -> RGB NCHW con resize nearest-neighbour.
- Aggiunto `TryCreate` fail-closed con reason code per modello mancante/input assente/runtime initialization/IO/access.
- Creato `EmptyOnnxDetectionDecoder` che restituisce zero detection finché non viene installato un decoder/model spec valido.
- Aggiunti test per modello mancante, ordine canali RGB/NCHW, resize deterministico e decoder vuoto.
- Estesa la certification suite Perception con check ONNX fail-closed.

**Stato**
- IMPLEMENTATO su `main`.
- Adapter ONNX base pronto.
- Manca volutamente il decoder di una specifica architettura di detector e un modello validato.
- Prossimo passo: model manifest + decoder specifico benchmarkabile, senza rendere il modello obbligatorio.


## 2026-09-08 — Collegamento MCP locale Claude → DeepSeek (`tools/deepseek-mcp`)

**File**
- `tools/deepseek-mcp/src/{config,sandbox,workerTools,deepseekClient,agentLoop,server}.mjs` — creati.
- `tools/deepseek-mcp/test/{helpers,config,sandbox,workerTools,deepseekClient,agentLoop,server}.*.mjs` — creati.
- `tools/deepseek-mcp/scripts/{check-connection,check-registration}.mjs` — creati.
- `tools/deepseek-mcp/{package.json,README.md}` — creati.
- `.mcp.json` — creato.
- `.gitignore` — aggiornato.
- `docs/INDICE_REPO.md`, `docs/WORKLOG.md` — aggiornati.

**Perché**
- Gli incarichi a DeepSeek passavano per copia-incolla manuale in Cursor: nessuna tracciabilità di cosa fosse stato realmente scritto su disco, nessun perimetro applicato dal programma.

**Cosa è stato fatto**
- Server MCP su stdio con delega confinata e rendicontata verso DeepSeek.
- Nessuna shell al lavoratore; build/test restano al coordinatore.
- Tetti di giri API, tool call, timeout e retry.
- Rendiconto delle modifiche reali tramite SHA-256.

**Stato**
- IMPLEMENTATO e VERIFICATO su `main`.


## 2026-09-11 — Perception: model contract + YOLOv8 decoder

**Obiettivo:** chiudere il passo successivo della boundary ONNX: definire un contratto versionato per il modello e installare un decoder concreto e benchmarkabile, mantenendo il modello opzionale e senza promuovere dati sintetici a evidenza reale.

**File toccati**
- `src/NosAi.Runtime/Perception/YoloV8DetectionDecoder.cs` — creato.
- `models/perception/nosai-yolov8.manifest.json` — creato.
- `tests/NosAi.Runtime.Tests/YoloV8DetectionDecoderTests.cs` — creato.
- `docs/WORKLOG.md` — aggiornato.

**Perché**
- L'adapter ONNX esistente aveva già separato inferenza e decoder, ma mancava una prima implementazione concreta.
- Senza manifest non era esplicito quali input/output e classi il runtime si aspettasse.
- Il progetto non deve mai considerare un modello non validato come asset produttivo.

**Cosa è stato fatto**
- Aggiunto `YoloV8DetectionDecoder` per i layout ONNX comuni `[1,channels,candidates]` e `[1,candidates,channels]`.
- Gestiti `cx,cy,w,h`, coordinate normalizzate o pixel, soglia di confidenza e limite massimo di detection.
- Ordinamento deterministico per confidenza/area.
- Output senza informazione HP non viene trasformato in una falsa percentuale: `HpRatio` resta `NaN` come valore di assenza informativa nell'attuale contract.
- Aggiunto manifest versionato con input 640x640 RGB/NCHW, classi NosAi iniziali e campi di provenienza obbligatori da completare.
- Aggiunti test per entrambi i layout, scaling, soglia e comportamento fail-closed sull'HP.

**Stato**
- IMPLEMENTATO sul branch `feat/perception-model-contract`.
- Verifica automatica CI/build non eseguita da questo ambiente: il commit va validato dal workflow del repository prima del merge.
- **Non VERIFIED come ML reale**: manca ancora `nosai-yolov8.onnx` addestrato/validato e relativo report di provenienza.
- Prossimo passo: acquisire dataset/replay reali autorizzati, addestrare o selezionare un modello, calcolare SHA-256, compilare la provenance e collegare il manifest all'AutoSet/provider selection.
