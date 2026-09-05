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
