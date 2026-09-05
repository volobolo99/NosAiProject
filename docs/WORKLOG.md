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
