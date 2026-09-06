# AP-03 — Map Reconstruction — Audit A5 (Claude, indipendente)

**Autore:** A5 (Claude), audit indipendente. Nessun file A1-A4 è stato
toccato in questo task (ownership rigorosa: solo nuovi file di
test sotto `tests/NosAi.Core.Tests/` e `tests/NosAi.Runtime.Tests/`, più
questo stesso file).
**Data:** 2026-09-05.
**Ambito:** verifica indipendente end-to-end di A1 (`MapObservationBatch`),
A2 (`MapGridObservationProjector`), A3 (`MapReconstructionFusion.Merge`), A4
(`MapModelStore`, `MapReconstructionSource`, il wiring
`WorldModelFusionLoop`/`Program.cs`), seguendo lo stesso metodo di
`docs/agents/phases/AP-01/AP-01_A5_AUDIT.md` e
`docs/agents/phases/AP-02/AP-02_A5_AUDIT.md`: verifica empirica contro il
runtime .NET reale, mai fiducia nel self-report di un agente precedente. Un
difetto reale si documenta con un test di regressione deliberatamente rosso,
non si corregge e non si nasconde (fuori ownership di A5 in ogni caso).

**Nota sullo stato dell'albero:** al momento di questo audit il lavoro di A4
(`MapReconstructionSource.cs`, `MapModelStore.cs`,
`WorldModelFusionLoop.cs`/`Program.cs`/`NosAi.Runtime.csproj` come edit
additivi, più i test A4 stessi) è presente nel working tree ma non ancora
committato (`git status --porcelain` lo mostra come modifiche/file non
tracciati sopra il commit `bc0a5ec feat(AP-03): map observation contract,
incremental merge, grid projector`). Questo audit verifica esattamente
questo stato dell'albero, non un commit ipotetico futuro.

Livello di verifica dichiarato per questo pacchetto di audit: **`Present`**
per i 4 nuovi file di test di questo audit (build pulita, nessun file di
produzione toccato) e **`Integrated`** per l'affermazione "nessuna
regressione sui test esistenti" (l'intera suite combinata Core.Tests +
Runtime.Tests è verde a parte l'unico test di regressione deliberatamente
rosso di questo stesso audit). Mai `Verified` -- nessuna evidenza da client
NosTale reale in questo task, solo sandbox Linux (nessun file `.grid` reale
è stato letto: ogni test, di A4 e di questo audit, usa un file sintetico
scritto a mano nel formato di `MapGridFormat` -- non esiste un
`AP-03_STATUS.md` in questa fase da cui citare una dichiarazione equivalente
di A4, quindi questo è verificato direttamente sui file di test stessi,
non per citazione).

## 1. File creati

Tutti nuovi; nessun file A1-A4 esistente è stato modificato o indebolito.

- `tests/NosAi.Runtime.Tests/WorldModel/Fusion/MapReconstructionFusionAtGridScaleTests.cs`
  (6 test) -- item 4 (idempotenza a scala reale) e item 8 (determinismo
  end-to-end), più la verifica richiesta su `EquatableArray<Tile>` a scala.
- `tests/NosAi.Runtime.Tests/WorldModel/Fusion/MapReconstructionSourceMapIdParsingTests.cs`
  (10 test) -- item 3 (round-trip di `MapId`).
- `tests/NosAi.Runtime.Tests/WorldModel/Fusion/MapReconstructionSourceExceptionSafetyTests.cs`
  (2 test) -- item 7 (exception safety) e la metà lato-SQLite dell'item 2
  (caching claim).
- `tests/NosAi.Core.Tests/MapModelStoreSimulatedSourceAndBoundaryConfidenceTests.cs`
  (1 test) -- item 5 (round-trip fidelity), copertura supplementare rispetto
  al già solido `MapModelStoreTests.cs` di A4.
- `docs/agents/phases/AP-03/AP-03_A5_AUDIT.md` (questo file).

**19 nuovi test in totale. 1 fallisce di proposito** (1 difetto reale, non
introdotto da A5, non correggibile dall'ownership di A5): item 7. **18
passano da subito.** Nessun test pre-esistente di A1-A4 ha cambiato esito.

## 2. Difetto reale trovato

### Difetto 1 — `MapReconstructionSource.Resolve` NON è exception-safe sul suo percorso di lettura: una `SqliteException` sul primo `TryLoad` per un nuovo map id esce non gestita, contraddicendo il contratto documentato dalla classe stessa

**Severità: Media** (contenuta oggi dal solo chiamante di produzione
esistente, ma è una violazione diretta e riproducibile del contratto
"`Resolve` non lancia mai" che la classe dichiara tre volte nella propria
documentazione XML -- vedi §2.1 sotto per l'analisi dell'impatto reale).

**File/riga esatti:**

- `src/NosAi.Storage/MapModelStore.cs:135` -- `TryLoad`:
  ```csharp
  if (command.ExecuteScalar() is not string json)
      return false;
  ```
  Nessun `try`/`catch` attorno a `ExecuteScalar()`. Confrontare con lo stesso
  file, `Save` (righe 92-109): anche `Save` non ha un proprio `try`/`catch`
  interno, ma il suo *chiamante* (`MapReconstructionSource.PersistIfPossible`,
  vedi sotto) lo protegge esplicitamente -- `TryLoad` non ha l'equivalente.

- `src/NosAi.Runtime/WorldModel/Fusion/MapReconstructionSource.cs:170-176` --
  `LoadPersistedOrUnknown`:
  ```csharp
  private MapModel LoadPersistedOrUnknown(MapId mapId, DateTime nowUtc)
  {
      if (_store is not null && _store.TryLoad(mapId, out MapModel persisted))
          return persisted;

      return MapModel.Unknown(mapId, MapNotPersistedReason, nowUtc);
  }
  ```
  Nessun `try`/`catch` attorno a `_store.TryLoad(...)`. Confrontare con lo
  stesso file, `PersistIfPossible` (righe 196-209), che avvolge
  esplicitamente `_store.Save(map)`:
  ```csharp
  private void PersistIfPossible(MapModel map)
  {
      if (_store is null)
          return;

      try
      {
          _store.Save(map);
      }
      catch (Exception ex)
      {
          _logger?.Error("MapReconstructionSource failed to persist a reconstructed map; continuing in-memory for this session.", ex);
      }
  }
  ```
  Il percorso di scrittura ha esattamente la disciplina che la classe rivendica
  ("Fail-soft at every step ... Resolve never throws", righe 48-58 della
  stessa classe); il percorso di lettura, strutturalmente identico, non ce
  l'ha. Non sembra una scelta deliberata -- nulla nel codice o nei commenti
  la giustifica -- ma un'asimmetria, lo stesso genere di svista che AP-01/A5
  e AP-02/A5 hanno già trovato più volte in questo repository sotto forme
  diverse.

**Input che fa fallire (riprodotto empiricamente, non ipotizzato):**
1. Si costruisce un `MapReconstructionSource` reale (volume etichettato
   reale + directory grid reale).
2. Una prima `Resolve` per `map-1` va a buon fine: lo store si apre, lo
   schema `map_models` viene creato e la riga viene scritta.
3. Una **seconda connessione SQLite indipendente**, puntata sullo stesso
   file di database, esegue `DROP TABLE map_models` e si chiude -- un
   proxy realistico per "il file persistito si corrompe/cambia sotto i
   piedi del processo" (disco pieno durante una scrittura precedente,
   corruzione esterna, un secondo processo che tocca lo stesso file).
4. Si chiama `Resolve` per `map-2`, un id **mai risolto prima da questa
   stessa istanza** (cache-miss deliberato, cosi' che
   `LoadPersistedOrUnknown` sia costretto a interrogare davvero lo store
   invece di rispondere dalla cache in-memory).

**Atteso (per il contratto documentato dalla classe):** `Resolve` completa
normalmente, restituendo una ricostruzione onestamente vuota per `map-2`
(esattamente come se nulla fosse mai stato persistito), con l'errore
registrato dal logger -- mai un'eccezione che esce dal metodo.

**Effettivo (riprodotto da
`MapReconstructionSourceExceptionSafetyTests.Resolve_WhenThePersistedStoreThrowsOnRead_StillCompletesWithoutThrowing_KnownMapReconstructionSourceGap`,
rosso di proposito):**
```
Microsoft.Data.Sqlite.SqliteException : SQLite Error 1: 'no such table: map_models'.
   at Microsoft.Data.Sqlite.SqliteCommand.ExecuteScalar()
   at NosAi.Storage.MapModelStore.TryLoad(MapId mapId, MapModel& map) in .../src/NosAi.Storage/MapModelStore.cs:line 135
   at NosAi.Runtime.WorldModel.Fusion.MapReconstructionSource.LoadPersistedOrUnknown(MapId mapId, DateTime nowUtc) in .../MapReconstructionSource.cs:line 172
   at NosAi.Runtime.WorldModel.Fusion.MapReconstructionSource.Resolve(WorldModelSnapshot networkSnapshot, DateTime nowUtc) in .../MapReconstructionSource.cs:line 152
```
L'eccezione esce letteralmente da `Resolve`, non da un livello più esterno.

#### 2.1 Impatto reale con il wiring di produzione odierno -- contenuto, non azzerato

Con il solo chiamante di produzione che esiste oggi
(`WorldModelFusionLoop.RunOnce`, riga 256-267), l'impatto è limitato: quel
metodo avvolge già l'intera chiamata `_mapSource(result)` in un proprio
`try`/`catch`, esattamente come i test di A4 in
`WorldModelFusionLoopMapWiringTests.MapSource_ThatThrows_*` già dimostrano.
Quindi il ciclo di fusione **non** si interrompe oggi se `Resolve` lancia:
il fatto viene loggato una seconda volta (dal logger di
`WorldModelFusionLoop`) e il campo `Map` resta quello del solo canale di
rete per quel ciclo. Per questo la severità è "Media" e non "Alta/bloccante".

Resta comunque un difetto reale e non un falso positivo, per tre ragioni
verificate, non presunte:
1. La documentazione XML della classe (righe 48-58 di
   `MapReconstructionSource.cs`) rivendica esplicitamente questa garanzia
   come proprietà di `Resolve` stesso, non come proprietà del suo unico
   chiamante odierno -- un futuro secondo chiamante (uno strumento
   diagnostico, un test, un refactor di `WorldModelFusionLoop` che cambi
   l'ordine delle protezioni) erediterebbe silenziosamente questa lacuna.
2. È un'asimmetria interna ingiustificata all'interno della stessa classe:
   il percorso di scrittura ha la protezione, quello di lettura no, per un
   tipo di fallimento (I/O SQLite) identico su entrambi i lati.
3. `docs/agents/AGENT_WORK_PROTOCOL.md` e
   `docs/NOSAI_ARCHITECTURE_BASELINE.md` chiedono esplicitamente "Fail
   closed where safety requires it" e una disciplina di robustezza uniforme
   su ogni nuovo confine di I/O (esattamente il punto 7 del comando di
   questo stesso audit).

**Correzione suggerita per A6 (un pass, nessuna riprogettazione):** avvolgere
`_store.TryLoad(mapId, out MapModel persisted)` in `LoadPersistedOrUnknown`
in un `try`/`catch` che rispecchi esattamente quello già presente in
`PersistIfPossible` -- log con `_logger?.Error(...)` e fallback a
`MapModel.Unknown(mapId, MapNotPersistedReason, nowUtc)`, la stessa risposta
già prevista per "mai persistito". Nessuna modifica di firma pubblica
necessaria.

## 3. Verificato per empirico: NESSUN difetto trovato (item esplicitamente richiesti dal comando)

### 3.1 Item 1 -- Wall-clock leak: NESSUNO trovato nei file nuovi di AP-03

Ogni singola chiamata a `.Unknown(...)`/`.Empty(...)` in tutti i file A1-A4
di questa fase è stata elencata (non campionata) via grep mirato e
verificata a mano:

- `MapObservationBatch.Empty` (definizione, riga 43): fallback
  `observedAtUtc ?? DateTime.UtcNow` esiste ma è il *default del parametro
  opzionale della factory stessa* -- ogni sito di chiamata reale in questa
  fase lo passa esplicitamente (vedi sotto), quindi il default non è mai
  raggiunto in produzione.
- `MapGridObservationProjector.Project` (riga 51): `MapObservationBatch.Empty(mapId, "map_grid_not_loaded", observedAtUtc)` -- esplicito.
- `MapReconstructionSource.LoadPersistedOrUnknown` (riga 175):
  `MapModel.Unknown(mapId, MapNotPersistedReason, nowUtc)` -- esplicito.
- `MapReconstructionSource.ProjectGridEvidence` (righe 187, 191): entrambe le
  chiamate a `MapObservationBatch.Empty(...)` passano `nowUtc` esplicitamente.
- `MapModelStore.TryLoad` (riga 127): `MapModel.Unknown(mapId, "map_not_persisted", DateTime.MinValue)` --
  non è un leak: è un sentinella deterministico dichiarato tale nel proprio
  commento ("this sentinel is either overwritten below on a hit, or left for
  a caller who supplies their own instant"), mai esposto a un chiamante che
  lo tratti come un istante reale (`MapReconstructionSource.LoadPersistedOrUnknown`
  usa il ramo `true` di `TryLoad` o il proprio `nowUtc` nel ramo `false`, mai
  questo sentinella).
- `WorldModelFusionLoop.cs`/`Program.cs`: nessuna nuova chiamata
  `.Unknown(...)`/`.Empty(...)` introdotta da AP-03 in questi due file
  (l'unica preesistente, `WorldModelSnapshot.Unknown("no_prior_fusion_cycle")`
  come inizializzatore di campo, è AP-01, non AP-03, e non ha un istante
  disponibile a quel punto per definizione).

**Nessun leak trovato.** La disciplina che le sei correzioni precedenti in
questo repository hanno instillato (ogni commento nei file A4 la cita
esplicitamente) risulta effettivamente rispettata qui.

### 3.2 Item 2 -- La caching claim di `MapReconstructionSource.Resolve`: confermata su ENTRAMBI i lati, non solo quello che A4 aveva già testato

A4 aveva già `MapReconstructionSourceTests.Resolve_SecondCallForTheSameMap_UsesTheCache_AndNeverTouchesTheGridOrStoreAgain`,
che cancella la directory grid e prova per riferimento (`Assert.Same`) che
la seconda `Resolve` per lo stesso map id non la ritocca. Rieseguito qui e
confermato verde (vedi §4). Questo audit aggiunge la metà che quel test non
copriva -- il lato SQLite -- con un meccanismo diverso e indipendente:
`MapReconstructionSourceExceptionSafetyTests.Resolve_SecondCallForTheSameMap_TrulyNeverWritesToTheSqliteFileOnDisk`
osserva `File.GetLastWriteTimeUtc`/lunghezza del file `.db` reale prima e
dopo la seconda `Resolve` per lo stesso map id: entrambi invariati. **Nessun
buco trovato** -- la claim di A4 è vera su entrambi i lati, confermata per
test e non per lettura del codice, come richiesto.

### 3.3 Item 3 -- Round-trip di `MapId`: fail-closed confermato su ogni caso richiesto dal comando

`MapReconstructionSourceMapIdParsingTests` (10 test) esercita
`TryParseNumericMapId` esclusivamente attraverso il contratto pubblico di
`Resolve` (mai per riflessione sul metodo privato):

| Input | Comportamento | Esito |
|---|---|---|
| `"map-"` (suffisso vuoto) | non parsa | `Resolve` ritorna `snapshot.Map` per riferimento -- **fail-closed** |
| `"map-abc"` | non parsa | idem -- **fail-closed** |
| `"map-1.5"` | non parsa | idem -- **fail-closed** |
| `"map-99999999999"` (overflow `int`) | non parsa | idem -- **fail-closed** |
| `"map--99999999999"` (overflow negativo) | non parsa | idem -- **fail-closed** |
| `"unknown-map"` (sentinella) | prefisso non combacia | idem -- **fail-closed** |
| `"Map-1"` (maiuscola) | `StartsWith` è `Ordinal`, case-sensitive | idem -- **fail-closed** |
| `"map"` (senza trattino) | prefisso non combacia | idem -- **fail-closed** |
| `"map-007"` (zeri iniziali) | parsa a `7`, decimale, mai ottale | pipeline reale eseguita, nessun file grid trovato -> ricostruzione onestamente vuota, **mai un'eccezione, mai un tile inventato** |
| `"map--5"` (numero negativo) | parsa a `-5` | idem -- pipeline reale eseguita, nessun file `-5.grid`, ricostruzione onestamente vuota |

Il caso "numero negativo" merita una nota: non è una stringa
avversariale inventata qui. È esattamente ciò che
`GameplayObservationProjector.Project` produrrebbe da solo se l'id numerico
di mappa arrivato dal wire fosse mai negativo (`$"map-{id}"` con `id == -5`
concatena letteralmente a `"map--5"`), quindi il parser *deve* saperlo
recuperare correttamente per restare coerente con se stesso -- e lo fa. A
valle, `MapGridExtractor.TryInfo` cerca un file `"-5.grid"` che
semplicemente non esiste (`File.Exists` risponde `false`, nessuna eccezione,
percorso già validato in `MapGridExtractor.TryInfo`/`BinaryMapGridLoader`
per input troncati/malformati -- vedi `MapGridFormat.MaxCells`, prodotto a
64 bit per non poter mai wrappare), quindi il fallimento si propaga come
"grid non trovata" e la mappa resta onestamente vuota. **Nessun crash,
nessun dato inventato: verificato, non un difetto**, solo un caso che in
pratica non può originarsi da un id di mappa reale del gioco (sempre
non-negativo) ma che il parser gestisce comunque senza sorprese.

**Nota di indurimento non bloccante (non un difetto):** `TryParseNumericMapId`
usa `NumberStyles.Integer`, che ammette anche un segno `+` iniziale e
spazi bianchi iniziali/finali -- forme che `int.ToString()` (l'unico
produttore reale della stringa) non genera mai. `NumberStyles.None` sarebbe
strettamente più stretto e più onesto sul contratto reale, ma oggi è morto
codice difensivo, non un bug osservabile: nessun input che il sistema
produce davvero può raggiungere quel ramo. Segnalato per un'eventuale
pulizia futura, non richiesto per l'integrazione.

### 3.4 Item 4 -- Idempotenza di `Merge` a scala reale (30.000 tile, non 1-2): confermata

`MapReconstructionFusionAtGridScaleTests` costruisce una griglia 200x150
(30.000 celle, ~1 su 11 bloccata secondo un pattern deterministico basato
sulle coordinate, non banale tutto-uguale) e la fa passare per la catena
reale `MapGridObservationProjector.Project` -> `MapReconstructionFusion.Merge`:

- Lo stesso batch (identica istanza) fuso due volte di seguito produce lo
  stesso riferimento `MapModel`, `Version` invariata (`Assert.Same`).
- Un batch value-equal ma costruito da una SECONDA chiamata `Project`
  indipendente (istanza diversa, stesso contenuto) produce anch'esso lo
  stesso riferimento -- prova diretta che l'uguaglianza per valore di
  `EquatableArray<Tile>`, non l'identità del batch, è ciò che decide
  l'idempotenza, esattamente il meccanismo che il comando chiedeva di
  verificare a questa scala.
- Un singolo tile cambiato su 30.000 fa scattare esattamente un incremento
  di versione e lascia gli altri 29.999 intatti nel valore atteso.

**Nessun bug O(n²) o di keying del dizionario trovato a questa scala.**
`MapReconstructionFusion.MergeByKey` resta corretto e con lo stesso
comportamento osservabile della sua stessa suite a piccola scala (A3).

### 3.5 Item 5 -- Fedeltà del round trip di `MapModelStore`: confermata, inclusi i punti non coperti da A4

La suite di A4 (`MapModelStoreTests.cs`, 5 test, già molto accurata) verifica
per ogni tile/portal/bound: `Value`, `Source`, `Confidence`,
`ObservedAtUtc`, `HasObservedValue`, `Reason` -- confermata verde qui senza
modifiche. Il gap non coperto da A4 (`DataSourceKind.Simulated` e i valori
di confidence limite 0.0/1.0 esatti) è stato chiuso da
`MapModelStoreSimulatedSourceAndBoundaryConfidenceTests`: entrambi
sopravvivono al round trip senza perdita né arrotondamento. Il test
"salvare due volte sovrascrive" di A4
(`SavingTheSameMapIdTwice_OverwritesRatherThanDuplicating`, con `SELECT
COUNT(*)` diretto sulla tabella) è stato riletto riga per riga e riconfermato
corretto (usa `ON CONFLICT(map_id) DO UPDATE`, non un `INSERT` semplice).
**Nessun buco trovato.**

### 3.6 Item 6 -- Il cambio di semantica di `MapModel.Version`: verificato, NESSUN consumatore rotto

Prima di AP-03, `GameplayObservationProjector.Project` (riga 97) stampava
sempre `MapModel.Version` con il contatore globale `version` del ciclo di
fusione. Dopo il wiring di A4, quando `mapSource` è presente
`WorldModelFusionLoop.RunOnce` sostituisce interamente `result.Map` con il
ritorno di `MapReconstructionSource.Resolve`, la cui `Version` è ora il
contatore indipendente e per-mappa che `MapReconstructionFusion.Merge`
mantiene (`current.Version + 1` solo quando qualcosa cambia davvero). Le due
sequenze di numeri sono oggi effettivamente scollegate.

Verificato con un grep esaustivo (non un campione) su tutto il repository,
codice di produzione e test:
```
grep -rn "\.Map\.Version" --include=*.cs .
```
**Zero risultati** in tutto `src/` e `tests/` oltre ai commenti XML che
descrivono proprio questo comportamento in `MapModelStore.cs` e
`MapReconstructionFusion.cs`. Nessun confronto `snapshot.Version` contro
`Map.Version` esiste in nessun punto del codice o dei test esistenti (i due
soli usi di `map.Version`/`.Version` isolati trovati sono asserzioni dirette
contro una costante letterale -- `Assert.Equal(0, map.Version)` in
`MapModelTests.cs` e `MapModelStoreTests.cs` -- mai un confronto tra i due
contatori). `WorldState.Version` (in `src/NosAi.Core/WorldState.cs`) è un
tipo Gate-era completamente separato (`WorldState`/`MapSnapshot`, non
`WorldModelSnapshot`/`MapModel`) e non condivide alcuna relazione con questa
domanda.

**Conclusione: cambio di semantica reale e onestamente documentato dal
codice stesso, ma innocuo oggi -- nessun consumatore esistente assumeva
l'accoppiamento precedente.** Nessuna correzione richiesta. Rimane un item
da tenere presente per qualunque fase futura che aggiunga un nuovo lettore
di `Map.Version` assumendo (erroneamente) che coincida ancora con
`snapshot.Version`.

### 3.7 Item 7 (parte non difettosa) -- Il confine di I/O `MapGridExtractor.TryInfo`/`BinaryMapGridLoader`: già a prova di eccezioni

Non modificato da AP-03 (file preesistenti, fuori ownership di questa fase),
ma consumato direttamente da `MapReconstructionSource.ProjectGridEvidence`
senza alcun `try`/`catch` proprio -- quindi rilevante per lo stesso item 7.
Riletto riga per riga: `TryInfo` avvolge già la propria `File.ReadAllBytes`
in un `try`/`catch (IOException)` esplicito, e `BinaryMapGridLoader.TryLoad`
calcola il prodotto larghezza x altezza in aritmetico a 64 bit apposta per
non poter mai avvolgere a un valore piccolo che un file troncato
soddisferebbe per errore (`MapGridFormat.MaxCells`, commento esplicito in
`BinaryMapGridLoader.cs`). Un id di mappa negativo (vedi §3.3) genera solo un
nome file `"-N.grid"` sintatticamente valido che semplicemente non esiste --
`File.Exists` risponde `false` senza eccezioni. **Nessun buco trovato in
questo confine**, a differenza del confine SQLite (Difetto 1, §2).

### 3.8 Item 8 -- Determinismo della pipeline end-to-end: confermato a scala reale

`MapReconstructionFusionAtGridScaleTests.FullPipeline_TwoIndependentProjectAndMergeRuns_ProduceBitForBitEqualMapModels`
esegue due volte da zero, con due griglie 200x150 costruite
indipendentemente ma byte-per-byte identiche e lo stesso istante, l'intera
catena `Project` -> `Merge` (partendo entrambe da un `MapModel.Unknown`
altrettanto indipendente): i due `MapModel` risultanti sono
`Assert.Equal` -- non solo ogni stadio preso isolatamente, la catena intera.
**Nessuna asimmetria trovata.**

### 3.9 Verificato senza trovare un difetto: item aggiuntivi richiesti dal comando

- **`EquatableArray<Tile>` a scala di griglia (30.000 elementi):**
  confermato che l'uguaglianza strutturale regge (due batch value-equal ma
  con istanze sottostanti diverse confrontano uguali) e che una singola
  differenza sull'ULTIMO elemento (non il primo, per escludere un
  cortocircuito accidentale sulla sola lunghezza) viene comunque rilevata
  (`EquatableArray_OfTiles_HoldsStructuralEqualityAtGridScale_NotJustForAHandful`).
- **`NosAi.Core` a dipendenza zero da `NosAi.Runtime`:** confermato --
  `NosAi.Core.csproj` non dichiara alcun `ProjectReference` (il commento nel
  file stesso lo rivendica: "NosAi.Core non referenzia nulla"), invariato da
  AP-03.
- **`NosAi.Storage` non indebolisce `SqliteEventJournal`/`VolumeLocator`:**
  confermato via `git diff bc0a5ec --stat -- src/NosAi.Storage/`: l'unica
  modifica in quella directory è l'aggiunta del nuovo file
  `MapModelStore.cs`; nessun file esistente in `NosAi.Storage` risulta
  toccato.
- **La correzione della riflessione di A4 su `WorldModelFusionLoop`/`Program.cs`
  (parametro extra citato nel comando):** vedi §5 sotto -- ricontrollata
  riga per riga e confermata corretta.

## 4. Il ragionamento di A4 su `ObservedAtUtc` (segnalato dal comando di questo audit): verificato riga per riga, corretto

Il comando §3 di `AP-03_A4_CLAUDE_persistence_and_wiring.md` (riga 239)
tipizzava letteralmente `mapSource: mapReconstruction!.Resolve` come se
`Resolve` avesse la firma `Func<WorldModelSnapshot, MapModel>` -- un solo
parametro -- mentre `MapReconstructionSource.Resolve` ne richiede due
(`WorldModelSnapshot`, `DateTime nowUtc`). A4 ha risolto scrivendo una
funzione locale in `Program.cs` (righe 681-682):
```csharp
NosAi.Core.WorldModel.MapModel ResolveMap(NosAi.Core.WorldModel.WorldModelSnapshot snapshot) =>
    mapReconstruction!.Resolve(snapshot, snapshot.ObservedAtUtc);
```
motivando che `WorldModelSnapshot.ObservedAtUtc` è già timbrato con il
`nowUtc` di quel ciclo e non viene mai mutato in seguito.

**Verificato tracciando l'intera catena, non assunto:**
1. `GameplayObservationProjector.Project(gameplay, _playerId, version, nowUtc)`
   (chiamato in `WorldModelFusionLoop.RunOnce`, riga 232) costruisce
   `projected` con `ObservedAtUtc = nowUtc` (secondo parametro posizionale
   di `WorldModelSnapshot`, riga 20 del record).
2. `WorldModelTemporalEnricher.Enrich(previous, projected, nowUtc, ...)`
   ritorna `current with { Player = ..., Mobs = ... }` (riga 40-44 di
   `WorldModelTemporalEnricher.cs`) -- **non tocca `ObservedAtUtc`**, quindi
   `enriched.ObservedAtUtc == projected.ObservedAtUtc == nowUtc`.
3. Se `_visualSource` è presente, `VisualObservationFusion.FuseVitals(enriched, visual, nowUtc)`
   ritorna `snapshot with { Player = fusedPlayer }` (riga 93 di
   `VisualObservationFusion.cs`) -- **anche questo non tocca
   `ObservedAtUtc`**, quindi `result.ObservedAtUtc` resta `nowUtc` anche
   dopo la fusione dei vitali.
4. `_mapSource(result)` (riga 260 di `WorldModelFusionLoop.cs`) riceve
   quindi esattamente lo stesso `result` il cui `ObservedAtUtc` non è mai
   stato toccato da nessuno dei due stadi intermedi: `result.ObservedAtUtc == nowUtc`
   con certezza, non "quasi sempre" o "nella pratica".

**Conclusione: il ragionamento di A4 è corretto.** `snapshot.ObservedAtUtc`
letto dentro `ResolveMap` è sempre esattamente il `nowUtc` di quel ciclo di
`RunOnce`, mai un istante stantio o di un ciclo diverso -- non c'è qui il
settimo bug della classe "wall-clock leak" che l'item 1 del comando temeva.
`WorldModelFusionLoopMapWiringTests.MapSource_ReceivesThisCyclesFusedSnapshotAsItsArgument`
(test di A4 già esistente) conferma empiricamente lo stesso fatto
(`Assert.Equal(T0, received.ObservedAtUtc)`); questo audit lo riconferma
per lettura formale della catena, come richiesto esplicitamente dal task.

## 5. Evidenza di build/test -- comandi e output esatti

Ambiente: `export PATH="$PATH:/root/.dotnet"` eseguito prima di ogni comando.

### 5.1 Build di produzione

```
$ dotnet build src/NosAi.Core/NosAi.Core.csproj -c Release
Build succeeded.  0 Warning(s)  0 Error(s)

$ dotnet build src/NosAi.Storage/NosAi.Storage.csproj -c Release
Build succeeded.  0 Warning(s)  0 Error(s)

$ dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
Build succeeded.  0 Warning(s)  0 Error(s)
```

### 5.2 Build dei progetti di test (con i 4 nuovi file di questo audit)

```
$ dotnet build tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
Build succeeded.  1 Warning(s) (xUnit2031, file pre-esistente CognitiveObservabilityBridgeTests.cs, non toccato da A5)  0 Error(s)

$ dotnet build tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
Build succeeded.  0 Warning(s)  0 Error(s)
```

### 5.3 Solo i 19 nuovi test di questo audit (per isolare l'unico rosso di proposito)

```
$ dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --no-build \
  --filter "FullyQualifiedName~MapReconstructionFusionAtGridScaleTests|FullyQualifiedName~MapReconstructionSourceMapIdParsingTests|FullyQualifiedName~MapReconstructionSourceExceptionSafetyTests"

Failed NosAi.Runtime.Tests.WorldModel.Fusion.MapReconstructionSourceExceptionSafetyTests.Resolve_WhenThePersistedStoreThrowsOnRead_StillCompletesWithoutThrowing_KnownMapReconstructionSourceGap [23 ms]
  Microsoft.Data.Sqlite.SqliteException : SQLite Error 1: 'no such table: map_models'.

Failed!  - Failed: 1, Passed: 17, Skipped: 0, Total: 18

$ dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release --no-build \
  --filter "FullyQualifiedName~MapModelStoreSimulatedSourceAndBoundaryConfidenceTests"

Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1
```
Esattamente il difetto documentato al §2, nessun altro test tra i 19 nuovi è
rosso.

### 5.4 Suite completa `NosAi.Core.Tests`

```
$ dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release --no-build
Passed!  - Failed: 0, Passed: 376, Skipped: 0, Total: 376
```
(Un'esecuzione precedente della stessa suite invariata ha mostrato 1 fallimento
isolato in `TransportLoopTests.OneHundredLoopbackHandshakesStayUnderTheTwentyFiveMillisecondBudget`,
un benchmark a budget di tempo fisso pre-esistente e non correlato ad AP-03;
la riesecuzione immediata, senza alcuna modifica, è tornata verde -- flake
noto di sandbox condivisa, stesso principio già documentato da AP-02/A5.
375 baseline + 1 nuovo test di questo audit = 376.

Una riesecuzione ancora più tarda, dopo la comparsa dell'attività concorrente
fuori ambito segnalata al §6, ha mostrato 388/388 verdi -- i 12 test in più
appartengono a `ExplorationContractsTests.cs`, non sono di questo audit e
non toccano alcun file AP-03; il numero AP-03-specifico che conta resta
quello del sottoinsieme filtrato al §5.6, invariato a 19/19 in entrambe le
esecuzioni.)

### 5.5 Suite completa `NosAi.Runtime.Tests`

```
$ dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --no-build
Failed!  - Failed: 1, Passed: 1920, Skipped: 58, Total: 1979
```
L'unico fallimento è esattamente il test di regressione del Difetto 1 (§2).
58 skip Windows-only pre-esistenti, invariati. 1903 baseline + 18 nuovi = 1921
attesi meno l'1 rosso = 1920 passati -- combacia esattamente.

### 5.6 Sottoinsieme filtrato AP-03 (`~Reconstruction|~MapModelStore|~MapGrid`)

```
$ dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release --no-build \
  --filter "FullyQualifiedName~Reconstruction|FullyQualifiedName~MapModelStore|FullyQualifiedName~MapGrid"
Passed!  - Failed: 0, Passed: 18, Skipped: 0, Total: 18

$ dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --no-build \
  --filter "FullyQualifiedName~Reconstruction|FullyQualifiedName~MapModelStore|FullyQualifiedName~MapGrid"
Failed!  - Failed: 1, Passed: 96, Skipped: 1, Total: 98
```
(Skip: `MapGridExtractorTests.EveryRealNStcDataMapLoadsThroughTheBinaryLoader`,
pre-esistente, richiede dati client reali non disponibili in sandbox --
invariato, non introdotto da questo audit.)

## 6. Limiti dichiarati / non affrontati

- Il Difetto 1 (§2) è reale ma fuori ownership di A5 in questa fase --
  non corretto, lasciato con un test di regressione rosso per A6, con
  fix suggerito a un solo pass (§2, ultimo paragrafo).
- Nessuna evidenza da client NosTale reale esiste per questa fase --
  coerente con quanto già dichiarato da A1-A4. Ogni file `.grid` usato nei
  test (di A4 e di questo audit) è sintetico, scritto a mano nel formato
  documentato da `MapGridFormat`. **Questo audit non eleva il livello di
  verifica oltre `Integrated`.**
- La nota di indurimento non bloccante su `NumberStyles.Integer` (§3.3) e
  quella sulla dipendenza da un host con almeno un volume etichettato
  scrivibile per `MapReconstructionSourceTests`/questo audit (stessa
  precondizione già richiesta da A4) restano item aperti non bloccanti.
- `git status --porcelain` è rimasto invariato tra l'ultima build e l'ultima
  esecuzione dei test AP-03-specifici di questo audit (§5.3/§5.6): nessuna
  attività concorrente ha toccato i file A1-A4 o i nuovi file di questo
  audit durante quella finestra.
- **Attività concorrente osservata fuori ambito, a fine task** (stesso
  principio di trasparenza già applicato da AP-01/A5 §5 e AP-02/A5 §5 per
  situazioni analoghe): dopo l'ultima esecuzione delle suite complete (§5.4-5.5),
  sono comparsi due file nuovi, non creati da questo audit e mai ispezionati
  in profondità -- `src/NosAi.Core/WorldModel/Exploration/ExplorationContracts.cs`
  e `tests/NosAi.Core.Tests/WorldModel/Exploration/ExplorationContractsTests.cs`
  (timestamp 23:10-23:11, dominio "Exploration" -- verosimilmente un agente
  AP-04 attivo in parallelo sullo stesso working tree condiviso). Fuori
  ambito per questo audit (non sono tra i file A1-A4 di AP-03 elencati nel
  comando), non toccati, segnalati solo per trasparenza dato che condividono
  lo stesso albero. Essendo un progetto SDK-style, il progetto di test li
  include automaticamente per glob; non invalidano l'evidenza raccolta ai
  §5.3/§5.6 (raccolta prima della loro comparsa) né la build di produzione
  di AP-03 (`src/NosAi.Core`, `src/NosAi.Storage`, `src/NosAi.Runtime`), che
  non dipende da quella nuova directory.

## 7. Verdetto

**AP-03 è pronta per l'integrazione A6**, con un solo blocco puntuale e a
basso rischio da chiudere prima o durante l'integrazione: il Difetto 1
(§2) -- un fix di una decina di righe, un `try`/`catch` che rispecchia un
pattern già presente cinque righe più sotto nello stesso file, nessuna
modifica di firma pubblica, nessun impatto sul wiring di
`WorldModelFusionLoop`/`Program.cs` (già protetto un livello più esterno).
Nessun altro item dei sette rimanenti richiesti dal comando ha prodotto un
difetto: la caching claim di A4, il round-trip di `MapId`, l'idempotenza di
`Merge` a scala reale, la fedeltà di `MapModelStore`, il cambio di
semantica di `MapModel.Version` e il determinismo end-to-end sono stati
tutti riverificati per test empirico a questo audit, non per lettura del
codice, e sono risultati corretti così come implementati.
