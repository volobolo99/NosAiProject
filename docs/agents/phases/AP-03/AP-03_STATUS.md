# AP-03 — Map Reconstruction — Stato finale

## 1. Ambito

Ricostruire mappe da osservazioni reali, salvabili/aggiornabili
incrementalmente senza perdere la storia precedente
(`docs/ROADMAP_ESECUTIVA.md` S:AP-03). Fonte reale unica per questo passaggio:
la griglia statica del client (`NosAi.Runtime.Navigation.MapGrid`, estratta
dagli archivi reali via `MapGridExtractor`) — già reale, già testata, non
simulata. Nessuna classificazione Mob/NPC/oggetti da visione: stesso blocco
ML/dati di AP-02 (nessun modello addestrato in questo repository), non
riaffrontato qui.

## 2. A1 — `MapObservationBatch`

`src/NosAi.Core/WorldModel/Reconstruction/MapObservationBatch.cs`. Evidenza
osservazione-side (tile/portali) per una singola passata di scansione,
analoga a `GameplayObservation`/`VisualObservation` per il canale di
ricostruzione mappa. 5 test.

## 3. A3 — `MapReconstructionFusion.Merge`

`src/NosAi.Core/WorldModel/Reconstruction/MapReconstructionFusion.cs`. Merge
puro e deterministico: unione last-write-wins per coordinata/id, bounds
monotoni (mai restringono), idempotente (un batch che non cambia nulla
ritorna la stessa istanza, nessun incremento di versione). 8 test, incluso
un test di determinismo end-to-end.

## 4. A2 — `MapGridObservationProjector`

`src/NosAi.Runtime/WorldModel/Fusion/MapGridObservationProjector.cs`.
Bridge da `MapGrid` (client reale) a `MapObservationBatch`, un `Tile` per
cella, classificato `Cached` (mai `Live`) — coerente con la documentazione
già esistente su `MapGrid`. Nessun portale generato: la griglia non porta
identità/destinazione dei portali. 7 test.

## 5. A4 — `MapModelStore` + `MapReconstructionSource`

`src/NosAi.Storage/MapModelStore.cs` — store SQLite WAL/FULL/busy_timeout=5000
(stessa disciplina di `SqliteEventJournal`), round-trip a fedeltà completa
(ogni classificazione `WorldFact<T>` preservata, non solo il valore grezzo).

`src/NosAi.Runtime/WorldModel/Fusion/MapReconstructionSource.cs` — pipeline
grid→projector→merge→persistenza, con cache per map id: una mappa già
ricostruita non ritocca più il file grid né lo store finché il map id non
cambia (requisito di prestazione verificato per test, non solo dichiarato).

Wiring additivo in `WorldModelFusionLoop.cs` (nuovo parametro opzionale
`mapSource`) e `Program.cs` (dietro `--fuse-world-model` esistente, nessun
nuovo flag). Zero cambio di comportamento per chi non lo passa.

21 test complessivi tra i due file.

## 6. A5 — Audit indipendente

Report completo: `AP-03_A5_AUDIT.md`. **Un difetto reale trovato**: il
percorso di lettura di `MapReconstructionSource.Resolve`
(`LoadPersistedOrUnknown`) non era exception-safe — uno store SQLite non
leggibile (tabella mancante, file corrotto) faceva propagare l'eccezione
fuori da `Resolve`, contraddicendo il suo stesso contratto dichiarato "non
lancia mai". Riprodotto empiricamente con un test rosso dedicato. Altri 7
punti di audit (wall-clock leak, claim di caching, parsing `MapId`,
idempotenza a scala reale — griglia 200×150/30.000 tile —, fedeltà
round-trip storage, semantica di `MapModel.Version`, sicurezza dalle
eccezioni, determinismo end-to-end) verificati **senza** trovare difetti,
ciascuno con un test dedicato, non solo per ispezione.

## 7. A6 — Integrazione finale

Applicata l'unica correzione richiesta: `LoadPersistedOrUnknown` ora
avvolge `_store.TryLoad` in un `try/catch`, simmetrico al trattamento già
esistente in `PersistIfPossible` per il percorso di scrittura — un
`MapModelStore` che non si può leggere in questo ciclo produce un
`MapModel.Unknown` onesto invece di un'eccezione, e la ricostruzione
prosegue dalla baseline Unknown invece di far fallire il ciclo di fusione.

**Evidenza:**
```
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release  → 0 Warning(s), 0 Error(s)
dotnet test tests/NosAi.Runtime.Tests/... --filter MapReconstructionSourceExceptionSafetyTests
  → Passed! Failed: 0, Passed: 2, Total: 2  (il test rosso di A5 ora è verde)
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
  → Passed! Failed: 0, Passed: 388, Skipped: 0, Total: 388
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
  → Passed! Failed: 0, Passed: 1921, Skipped: 58, Total: 1979
```

Nessuna regressione rispetto ai conteggi riportati da A4/A5.

## 8. Livello di verifica finale — AP-03

**`Integrated`**: A1+A2+A3+A4 costruiscono un albero unico che compila
pulito e passa tutti i test combinati, incluso il difetto reale trovato
dall'audit indipendente A5 e corretto in questo passaggio. **Non
`Verified`**: nessuna validazione contro un client NosTale reale — il
percorso di estrazione/lettura griglia resta esercitato solo con file
`.grid` reali ma non con l'intero client live in esecuzione in questo
ambiente Linux.

## 9. Item aperti, esplicitamente rimandati

- **Portali**: `Portal`/`EquatableArray<Portal>` restano sempre vuoti in
  ogni `MapModel` prodotto oggi — nessuna fonte dati reale esiste ancora
  per identità/destinazione dei portali. Un router multi-mappa via portali
  esiste nel codice legacy (`NosAi.Navigation.Pathfinding.WorldMapPortalRouter`)
  ma gira su un grafo di 5 mappe/4 portali **hardcoded** (fixture di test,
  non dati reali) ed è raggiungibile solo dalla propria suite di test, non
  dalla produzione — non è una base su cui costruire senza prima avere una
  fonte reale di dati portale (es. parsing di una tabella portali dal
  client, stesso genere di lavoro di `MapGridExtractor` per la geometria).
- **`DXGI_ERROR_ACCESS_LOST` a metà sessione**: non affrontato in questa
  fase, stesso limite già segnalato in AP-02.
- Triplicazione `DataSourceKind`/`ClassifiedValue<T>`/`WorldFact<T>` tra
  `NosAi.Runtime.Contracts`, `NosAi.Core.Hardware` e `NosAi.Core.WorldModel`
  — segnalata da AP-01/A5, ancora aperta, non ri-analizzata qui.
