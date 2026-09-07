# AP-02 — Multimodal Perception — Audit A5 (Test, benchmark, documentazione)

**Autore:** A5 (Claude), audit indipendente. Nessun file di produzione è stato
toccato in questo task (ownership rigorosa: solo test/fixture/benchmark/doc,
sotto `tests/NosAi.Runtime.Tests/` e questo stesso file).
**Data:** 2026-09-05.
**Ambito:** verifica indipendente di `VisualObservation` (A1),
`ClassifiedValueBridge`/`VisualObservationFusion` (A3), descritti in
`docs/agents/phases/AP-02/AP-02_STATUS.md`, seguendo lo stesso metodo e stile
di `docs/agents/phases/AP-01/AP-01_A5_AUDIT.md` (verifica empirica contro il
runtime .NET reale, non solo ispezione del codice; un difetto reale si
documenta con un test di regressione deliberatamente rosso, non si corregge
e non si nasconde).

**Nota sul contesto della sessione:** durante questo audit una sessione
concorrente attiva sullo stesso working tree (vedi commit `3cc62a0 docs: mark
AP-02 Q-016/Q-017 in progress (background agents dispatched)`) ha modificato
`src/NosAi.Runtime/Program.cs` e
`src/NosAi.Runtime/WorldModel/Fusion/WorldModelFusionLoop.cs`, e aggiunto
`src/NosAi.Runtime/Perception/ScreenVitalsCapture.cs`,
`tests/NosAi.Runtime.Tests/Perception/ScreenVitalsCaptureTests.cs` e
`tests/NosAi.Runtime.Tests/WorldModel/Fusion/WorldModelFusionLoopVisualWiringTests.cs`
(verosimilmente A2/A4, wiring runtime di `VisualObservation` nel loop di
fusione). Questi file non sono stati toccati né ispezionati in profondità da
questo audit (fuori ambito: non sono i tre file A1/A3 assegnati). Per una
finestra di alcuni minuti la loro presenza in stato intermedio ha reso il
progetto di test non compilabile (errori `CS0104`/`CS1503` tutti e soli nei
loro file, mai nei file di questo audit — verificato riga per riga
nell'output del compilatore). Il polling ripetuto della build (stesso
principio già usato in AP-01/A5 per la stessa situazione) ha confermato che
si è stabilizzata da sola pochi minuti dopo; tutta l'evidenza di build/test
in §4 è presa DOPO questa stabilizzazione, su un albero verificato pulito
(`git status --porcelain` invariato tra l'ultima build e l'ultima esecuzione
dei test).

Livello di verifica dichiarato per questo pacchetto di audit: **`Present`**
per i 4 nuovi file di test (build pulita, nessun file di produzione toccato)
e **`Integrated`** per l'affermazione "nessuna regressione sui test
esistenti" (l'intera suite combinata Core.Tests + Runtime.Tests è verde a
parte i 4 test di regressione deliberatamente rossi di questo stesso audit).
Mai `Verified` (nessuna evidenza da client NosTale reale in questo task —
solo sandbox Linux, coerente con quanto già dichiarato da A1/A3 in
`AP-02_STATUS.md` §5-6).

## 1. File creati

Tutti nuovi file sotto `tests/NosAi.Runtime.Tests/`; nessun file esistente è
stato modificato o indebolito.

- `tests/NosAi.Runtime.Tests/WorldModel/Fusion/VisualObservationFusionBoundaryTests.cs`
- `tests/NosAi.Runtime.Tests/WorldModel/Fusion/ClassifiedValueBridgeEquivalenceTests.cs`
- `tests/NosAi.Runtime.Tests/WorldModel/Fusion/MultimodalPipelineDeterminismTests.cs`
- `tests/NosAi.Runtime.Tests/VisualObservationBoundaryTests.cs`
- `docs/agents/phases/AP-02/AP-02_A5_AUDIT.md` (questo file)

42 nuovi test in `NosAi.Runtime.Tests`. Al momento della scrittura, **4
falliscono di proposito** (4 difetti reali, mai introdotti da A5, non
correggibili dall'ownership di A5) e 38 passano da subito. Nessun test
pre-esistente ha cambiato esito.

## 2. Difetti reali trovati

Per ciascuno: file+riga esatti, scenario di fallimento concreto, test di
regressione (rosso di proposito), evidenza empirica.

**Nota di ambito onesta:** i due difetti seguenti NON sono nei tre file
elencati come ownership esclusiva di A1/A3 per questa fase
(`VisualObservation.cs`, `ClassifiedValueBridge.cs`,
`VisualObservationFusion.cs`). Sono stati trovati esattamente con il metodo
che il comando di questo audit ha richiesto esplicitamente al punto 4
("incatena `VisualObservationFusion.FuseVitals` con
`GameplayObservationProjector.Project` e `WorldModelTemporalEnricher.Enrich`
in sequenza su più cicli e verifica determinismo"): costruendo quella catena,
un test pensato per essere VERDE (determinismo end-to-end su 3 cicli) ha
fallito per una causa esterna ad `AP-02`, isolata poi con un test dedicato.
Segnalati qui per trasparenza, come A5 ha già fatto in AP-01/A5 §5 per
attività concorrente osservata fuori ambito — non corretti, non è ownership
di A5 in ogni caso.

### Difetto 1 — `WorldModelTemporalEnricher.EnrichMobs`: il caso "nessun avvistamento precedente" perde `nowUtc`

**File:** `src/NosAi.Core/WorldModel/Temporal/WorldModelTemporalEnricher.cs:68`
```csharp
WorldFact<WorldVelocity> velocity = previousById.TryGetValue(mob.Id, out Mob? matched)
    ? TemporalBelief.EstimateVelocity(matched.Position, mob.Position, maxObservationGap)
    : WorldFact<WorldVelocity>.Unknown("no_prior_sighting_of_this_entity");   // riga 68: nessun observedAtUtc
```

Stessa causa radice dei Difetti 3-4 già trovati e corretti in AP-01/A5
(`TemporalBelief.EstimateVelocity`/`PredictPosition`), ma qui in un quarto
punto di chiamata, un livello sopra, nel chiamante che consuma
`TemporalBelief` — mai toccato dalla correzione precedente.
`WorldFact<T>.Unknown(reason, observedAtUtc: null)` ricade sul proprio
default (`DateTime.UtcNow` reale) invece di un istante deterministico
derivato dagli input del metodo. Rompe direttamente la garanzia dichiarata
dalla classe stessa ("the same two snapshots always enrich to the same
result") proprio per il caso più comune: la prima volta che un mob viene
visto.

**Non raggiungibile dal wiring reale odierno**: `GameplayObservationProjector.Project`
lascia sempre `WorldModelSnapshot.Mobs` vuoto (limite di ambito esplicito di
AP-02, vedi `AP-02_STATUS.md` §1) — quindi questo ramo non regredisce nulla
di ciò che AP-02 ha spedito. Diventerà raggiungibile appena una fase futura
collegherà `PerceptionResult.Entities`/avvistamenti di rete a
`WorldModelSnapshot.Mobs` — esattamente il prossimo passo che le stesse note
XML di `VisualObservationFusion` indicano. Trovato qui, prima che quella fase
inizi, incatenando le primitive AP-02 con `WorldModelTemporalEnricher` come
richiesto dal punto 4 del comando, con un `Mob` costruito a mano per simulare
esattamente ciò che quel collegamento futuro passerebbe a `Enrich` — non un
contratto ipotetico inventato qui, perché `EnrichMobs` e questo ramo esistono
già oggi.

**Test di regressione (rosso di proposito):**
`MultimodalPipelineDeterminismTests.EnrichMobs_FirstSightingOfANewMob_LeaksRealWallClockTime_InsteadOfADeterministicInstant_KnownWorldModelTemporalEnricherGap`
(input timestampati anno 2099, come nello stile AP-01/A5, per rendere il leak
inequivocabile) e il companion
`.EnrichMobs_FirstSightingOfANewMob_ReplayedTwice_DoesNotProduceBitForBitEqualResults_KnownWorldModelTemporalEnricherGap`.

> Nota 2026-09-07: il difetto è stato corretto e i due test sono verdi da
> allora. Sono stati rinominati in
> `EnrichMobs_FirstSightingOfANewMob_StampsTheInstantItWasGiven_NotRealWallClockTime`
> e `..._ReplayedTwice_ProducesBitForBitEqualResults`, perché i nomi
> continuavano ad affermare un fallimento che non avveniva più. I nomi citati
> qui sopra restano quelli del momento in cui l'audit fu scritto.

**Correzione suggerita (fuori ownership di A5):** passare
`mob.Position.ObservedAtUtc` (o il `nowUtc` del chiamante) a quella chiamata
`Unknown(...)`, esattamente come già fatto per i tre punti analoghi in
`TemporalBelief.cs`.

### Difetto 2 — `GameplayObservation.Unobserved`: SEDICI campi ignorano il parametro `atUtc`

**File:** `src/NosAi.Runtime/LiveIntegration/GameplayProvider.cs:196-216`
```csharp
public static GameplayObservation Unobserved(string reason, DateTime? atUtc = null) => new(
    ClassifiedValue<int>.Unknown(reason),      // Hp -- nessun observedAtUtc
    ClassifiedValue<int>.Unknown(reason),      // MaxHp
    ClassifiedValue<int>.Unknown(reason),      // Mp
    ClassifiedValue<int>.Unknown(reason),      // MaxMp
    ClassifiedValue<bool>.Unknown(reason),     // HasTarget
    ClassifiedValue<bool>.Unknown(reason),     // InCombat
    ClassifiedValue<int>.Unknown(reason),      // EntitiesInView
    atUtc ?? DateTime.UtcNow)                  // <- SOLO questo campo usa atUtc
{
    Entities = ClassifiedValue<IReadOnlyList<SelectableEntity>>.Unknown(reason),
    PlayerPosition = ClassifiedValue<MapPoint>.Unknown(reason),
    MapId = ClassifiedValue<int>.Unknown(reason),
    StandingCell = ClassifiedValue<MapPoint>.Unknown(reason),
    HitBy = ClassifiedValue<Aggressor>.Unknown(reason),
    SelectedTarget = ClassifiedValue<TargetedEntity>.Unknown(reason),
    SkillsReady = ClassifiedValue<IReadOnlyList<SkillReady>>.Unknown(reason),
    Inventory = ClassifiedValue<IReadOnlyList<InventorySlotReading>>.Unknown(reason),
    LastPickup = ClassifiedValue<ItemPickup>.Unknown(reason),
    GroundItems = ClassifiedValue<IReadOnlyList<GroundItem>>.Unknown(reason),
};
```

Il campo posizionale `ObservedAtUtc` del record usa correttamente
`atUtc ?? DateTime.UtcNow`; **tutti e sedici** i campi classificati per campo
(vitali, posizione, mappa, target, inventario, ecc.) sono invece costruiti
via `ClassifiedValue<T>.Unknown(reason)` **senza** l'argomento
`observedAtUtc`, che quindi ricade sul proprio default reale
(`ClassifiedValue<T>.Unknown`: `new(default!, DataSourceKind.Unknown,
DateTime.UtcNow, false, warning, reason)`), **completamente scollegato** dal
parametro `atUtc` che il chiamante ha esplicitamente fornito.

**Impatto concreto, verificato empiricamente (non assunto):** costruendo una
catena `GameplayObservation.Unobserved("reason", nowUtc) with { Hp = ...,
MaxHp = ... }` (esattamente il pattern già usato da
`VisualObservationFusionTests.NetworkOnlySnapshot`, dal comando di questo
stesso audit al punto 4, e presumibilmente da qualunque test/chiamante futuro
che costruisca una baseline "non osservato" e sovrascriva solo alcuni campi)
senza sovrascrivere ANCHE `MapId`/`PlayerPosition`/etc., il `MapId` (e ogni
altro campo lasciato al default) porta un `ObservedAtUtc` reale invece di
`nowUtc`. Questo si propaga fedelmente attraverso
`ClassifiedValueBridge.ToWorldFact` (che preserva l'`ObservedAtUtc` ricevuto,
comportamento corretto e verificato — vedi §3) fino a
`Player.CurrentMap.ObservedAtUtc` in `WorldModelSnapshot`, rendendo due
esecuzioni altrimenti identiche di `GameplayObservationProjector.Project`
NON uguali bit-per-bit.

**Effetto collaterale rilevante, trovato dallo stesso test:** lo stesso fatto
logico "mappa non osservata" è rappresentato DUE volte in uno snapshot
proiettato: `Player.CurrentMap` (soggetto al leak appena descritto) e `Map`
stesso, costruito via `MapModel.Unknown(..., nowUtc)` in
`GameplayObservationProjector.Project`, che invece incanala correttamente
`nowUtc` e non è affetto. Una rappresentazione dello stesso fatto è
deterministica, l'altra no.

**Test di regressione (rosso di proposito):**
`MultimodalPipelineDeterminismTests.GameplayObservationUnobserved_LeavesEveryUnoverriddenFieldWallClockStamped_InsteadOfUsingItsOwnAtUtcParameter_KnownGameplayObservationGap`
(isolato, verifica diretta `observation.MapId.ObservedAtUtc` contro un
`atUtc` fissato all'anno 2099) e il companion
`.GameplayObservationUnobserved_CalledTwiceWithIdenticalArguments_DoesNotProduceEqualResults_KnownGameplayObservationGap`

> Nota 2026-09-07: corretto anche questo; i due test sono verdi e rinominati in
> `GameplayObservationUnobserved_StampsEveryUnoverriddenFieldWithItsOwnAtUtcParameter`
> e `..._CalledTwiceWithIdenticalArguments_ProducesEqualResults`.
(due chiamate identiche non producono record uguali).

**Workaround applicato nel test "verde" di questo audit** (non una
correzione del difetto, solo per evitare che un test pensato per verificare
il determinismo di AP-02 fallisse per una causa esterna ad AP-02):
`MultimodalPipelineDeterminismTests.RunOneCycle` sovrascrive esplicitamente
Hp/MaxHp/Mp/MaxMp/PlayerPosition/MapId ad ogni ciclo — commentato nel codice
con riferimento a questo stesso difetto.

**Correzione suggerita (fuori ownership di A5, file Gate-era/C1 pre-AP-01,
probabilmente il più vecchio dei tre difetti di questa classe trovati finora
nel repository):** passare `atUtc ?? DateTime.UtcNow` (lo stesso valore già
calcolato per il campo posizionale) a ciascuna delle sedici chiamate
`ClassifiedValue<T>.Unknown(reason)` in questo blocco.

## 3. Trovato e verificato: NESSUN difetto (item esplicitamente richiesti dal comando)

### 3.1 `ClassifiedValueBridge` — equivalenza con il codice pre-refactor confermata

`git show be8f12b -- src/NosAi.Runtime/WorldModel/Fusion/GameplayObservationProjector.cs`
mostra che i vecchi metodi privati `ToWorldFact`/`MapSource` sono stati
spostati parola per parola (solo i siti di chiamata sono stati rinominati;
il ramo di default `_ => WorldFact<T>.Unknown("source_unknown",
observedAtUtc)` è identico). Confermato anche empiricamente, non solo dal
diff: `ClassifiedValueBridgeEquivalenceTests.cs` reimplementa la logica
pre-refactor in locale (trascrizione, non parafrasi) e la confronta con
`ClassifiedValueBridge.WithSource`/`ToWorldFact` per ogni `DataSourceKind`
(Live/Derived/Cached/Simulated/Unknown) e per entrambi gli stati di
`HasValue`. Tutti i 9 test di questo file sono verdi. Le suite pre-esistenti
`GameplayObservationProjectorTests`/`GameplayObservationProjectorBoundaryTests`
(A1/A2, non toccate da questo audit) continuano a passare invariate — vedi
§4. **Nessuna differenza di comportamento trovata.**

### 3.2 `ResourceKind.Custom` (es. "Combo Gauge") — preservato intatto

`VisualObservationFusionBoundaryTests.CustomResourceKind_IsPreservedUnchanged_ByPassingThroughTheElseBranch`:
una risorsa `Custom` iniettata nello snapshot di rete sopravvive identica
(stessa istanza, stesso `CustomName`, stessi valori) alla fusione dei
vitali, che tocca solo `ResourceKind.Health`/`Mana`. Verde, nessun difetto.

### 3.3 `maxAge`: entrambi i canali più vecchi della finestra → onestamente Unknown

`VisualObservationFusionBoundaryTests.BothChannelsOlderThanMaxAge_ResultIsHonestlyUnknown_NotTheStaleValue`
verifica il caso reale (non il caso "già Unknown" già coperto dal test
esistente `BothChannelsUnknown_ResultIsUnknown_NeverAFabricatedZero`): due
letture legittime (`Live`/`Derived`) ma entrambe più vecchie di `maxAge`
producono un risultato onestamente Unknown, stampato con il `nowUtc` del
chiamante (non un leak di wall-clock) grazie a `FactFusion.cs:84`, che passa
esplicitamente `nowUtc` al proprio fallback `Unknown(...)`. Verificato anche
il confine esatto (`ExactlyAtMaxAgeBoundary_StillCompetes...`): un candidato
esattamente all'età limite compete ancora, coerente con il contratto
documentato di `IsFresh` (`<=`). **Nessun buco trovato.**

### 3.4 Confidence: `ScreenVitalPair.Confidence` non sopravvive nel `WorldFact.Confidence` fuso — limite accettato, non difetto

Verificato (`ScreenVitalPairConfidence_NeverSurvivesIntoTheFusedWorldFactConfidence_AcceptedLimit`,
3 valori di confidence 0.85/0.9/1.0): il risultato fuso ha sempre
`Confidence == 1.0`, qualunque fosse la confidence reale dello schermo.
**Decisione, come richiesto esplicitamente dal comando:** limite accettato,
non difetto, per due motivi verificati (non assunti):

1. Il lato rete non ha MAI avuto una confidence propria con cui confrontarsi:
   `NosAi.Runtime.Contracts.ClassifiedValue<T>` non ha affatto un campo
   `Confidence` (solo `Source`/`ObservedAtUtc`/`HasObservedValue`) — quindi
   l'appiattimento è simmetrico su entrambi i canali, non un canale che perde
   informazione rispetto all'altro.
2. L'unico consumatore di `WorldFact.Confidence` in questo percorso è il
   tie-break di `FactFusion` a parità di `DataSourceKind`, e quel tie-break è
   inerte qui perché entrambi i lati sono sempre 1.0 — dimostrato dal test
   companion `WhenBothChannelsShareTheSameRank_TieBreakIgnoresTheOriginalScreenConfidence_BecauseBothAreFlattenedToOne`,
   che mostra il vincitore deciso dal timestamp (criterio successivo), non
   dalla confidence reale dello schermo (0.85, deliberatamente bassa in
   quel test) anche quando questa avrebbe potuto suggerire un esito diverso.

**Segnalato per il futuro:** dal momento in cui una fase successiva (es.
AP-05, risk scoring in combattimento) inizierà a leggere
`Resource.Current.Confidence` per pesare la fiducia nel valore stesso
(non solo per il tie-break interno), questo appiattimento inizierà a
scartare silenziosamente informazione reale (un OCR 0.85 appena sopra soglia
vs. uno 0.99 pulito, oggi indistinguibili).

### 3.5 Overflow/NaN — irrilevante qui, verificato

`ScreenVitalPair.Current`/`Maximum` sono `int`, non `double`: la conversione
`(double)v` in `ClassifiedValueBridge.ToWorldFact` è un widening che non può
produrre NaN/overflow per NESSUN valore a 32 bit, verificato empiricamente
su `int.MinValue`/`int.MaxValue`/`0`/`-1`
(`IntToDoubleWideningConversion_NeverProducesNaNOrInfinity_ForAny32BitInt`) e
end-to-end fondendo `int.MaxValue` come lettura schermo
(`ExtremeScreenVitalIntValues_FuseWithoutProducingNaNOrInfinity`). A
differenza di `TemporalBelief` (AP-01/A5 Difetto/lacuna nota: sottrazione fra
posizioni `float` può andare in overflow), qui non c'è aritmetica di
velocità: la classe di problema semplicemente non si applica. **Nessun buco
trovato.**

### 3.6 `VisualObservation.Unobserved` — ogni campo classificato è onestamente Unknown

`VisualObservationBoundaryTests.Unobserved_EveryClassifiedField_HasValueIsFalse_NoneSlipsThroughAsAnObservedFalseOrZero`
verifica tutti i campi `ClassifiedValue<T>`/`ScreenVitalPair`/`ScreenBarFill`
(inclusa la loro `Confidence`, sempre 0). **Nessun campo "quasi-Unknown"
trovato.**

`HpGlyphs`/`MpGlyphs`/`TrainedGlyphs` (interi semplici, non classificati,
design ereditato da `ScreenVitalObservation` Gate-era, non introdotto né
modificabile da A1 in questo passaggio) sono impostati a 0 nel fallback.
Verificato (`Unobserved_GlyphCounts_AreZero_ConsistentWithTheOneRealConsumersOwnZeroMeansUnknownConvention`):
per `TrainedGlyphs` questo NON è ambiguo — l'unico consumatore reale
(`NosAi.ControlPanel.PerceptionProbe`) già tratta
`TrainedGlyphs == 0` come equivalente a UNKNOWN in visualizzazione,
confermando che 0 è la convenzione stabilita sia in una lettura reale sia in
questo fallback. Per `HpGlyphs`/`MpGlyphs` nessuna convenzione analoga
esiste nel loro unico consumatore (`PerceptionProbe` mostra il conteggio
grezzo etichettato "DERIVED" incondizionatamente) — quindi in linea di
principio una lettura reale con zero glifi e questo fallback sono
indistinguibili per valore. **Non consequenziale oggi**, verificato: nessun
codice nel repository legge `VisualObservation.Vitals.HpGlyphs`/`MpGlyphs`
(il solo lettore, `PerceptionProbe`, legge da una vera chiamata a
`ScreenVitalReader`, mai da `VisualObservation.Unobserved`). Documentato come
limite di design ereditato pre-esistente, non un nuovo difetto AP-02.

### 3.7 Duplicato `ResourceKind.Health` — limite noto, non difetto bloccante

`VisualObservationFusionBoundaryTests.DuplicateHealthResourceInSnapshot_BothSlotsSurvive_ButBothAreOverwrittenFromOnlyTheFirstDuplicate`
verifica il comportamento esatto quando `snapshot.Player.Status.Resources`
contiene due entry `Health` (scenario che
`GameplayObservationProjector.Project` — l'unico produttore reale di
snapshot — non può oggi generare, poiché costruisce sempre esattamente una
Health e una Mana). `FindResource` ritorna solo la prima corrispondenza
(usata per calcolare il valore fuso); il ciclo che ricostruisce la lista
sostituisce OGNI entry il cui `Kind` corrisponde. Risultato verificato: le
DUE posizioni sopravvivono nella lista (né unite né rifiutate), ma
**entrambe** vengono sovrascritte con lo STESSO valore fuso, calcolato solo
dal primo duplicato — i dati del secondo (nel test: 30/50 contro l'80/100 del
primo) sono scartati silenziosamente, senza segnalazione di disaccordo.
**Decisione, per analogia diretta con la stessa lacuna già documentata e
accettata in AP-01/A5 §3.2 per `EntityId` duplicati:** limite noto, non
difetto bloccante — nessun produttore reale oggi crea questo input, ma nulla
lo impedirebbe se accadesse. Segnalato per un'eventuale validazione futura
(in `GameplayObservationProjector` o nel chiamante condiviso di A4).

### 3.8 Placeholder `"no_network_reading"` senza `nowUtc` — verificato empiricamente INERTE, non un difetto

**File:** `src/NosAi.Runtime/WorldModel/Fusion/VisualObservationFusion.cs:114-115`
```csharp
WorldFact<double> networkCurrent = network?.Current ?? WorldFact<double>.Unknown("no_network_reading");
WorldFact<double> networkMaximum = network?.Maximum ?? WorldFact<double>.Unknown("no_network_reading");
```

Stessa forma sintattica dei Difetti 1-2 sopra (nessun `observedAtUtc`
esplicito, quindi `DateTime.UtcNow` reale invece di `nowUtc`). Verificato
PERÒ empiricamente, non assunto, che qui è innocuo: questo placeholder ha
sempre `HasValue == false` (è un fatto `DataSourceKind.Unknown`), e
`WorldFact<T>.IsFresh` è definito come `HasValue && ...` — il corto-circuito
su `HasValue` esclude questo candidato da `FactFusion.Resolve`'s `usable`
PRIMA che il suo `ObservedAtUtc` sia mai confrontato con qualsiasi cosa. Il
timestamp che perde è scritto ma mai letto.
`VisualObservationFusionBoundaryTests.NetworkResourceEntirelyAbsent_WallClockLeakInTheUnknownPlaceholder_DoesNotBreakDeterminism_VerifiedAcrossARealDelay`
lo dimostra chiamando `FuseVitals` due volte, con un `Task.Delay(50ms)` reale
in mezzo, su uno snapshot il cui lato rete non ha alcuna risorsa
Health/Mana (forzando esattamente questo ramo): i due risultati sono
identici bit-per-bit. **Verde, nessun difetto** — ma documentato come
imperfezione di stile/convenzione (deviazione dalla convenzione seguita
ovunque altrove nel repository, incluso da `GameplayObservationProjector`),
segnalabile per pulizia difensiva futura (un cambiamento di una riga: passare
`nowUtc` in entrambi i punti), non archiviato come test rosso perché non
esiste alcuna differenza di output osservabile contro cui farlo fallire.

## 4. Evidenza di build/test — comandi e output esatti

Ambiente: `export PATH="$PATH:/root/.dotnet"` eseguito prima di ogni comando.
Tutta l'evidenza sotto è presa DOPO la stabilizzazione dell'attività
concorrente descritta in apertura (`git status --porcelain` invariato tra i
comandi).

### 4.1 Build di produzione (nessuna modifica di A5 le tocca)

```
$ dotnet build src/NosAi.Core/NosAi.Core.csproj -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:01.51

$ dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:02.95
```

### 4.2 Build del progetto di test (con i nuovi file di A5)

```
$ dotnet build tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
  ...
  NosAi.Runtime.Tests -> .../tests/NosAi.Runtime.Tests/bin/Release/net8.0-windows/NosAi.Runtime.Tests.dll

Build succeeded.

.../CognitiveObservabilityBridgeTests.cs(35,22): warning xUnit2031: Do not use a Where
clause to filter before calling Assert.Single. [...]  (file pre-esistente,
non toccato da A5)
    1 Warning(s)
    0 Error(s)
```

### 4.3 Solo i nuovi test di questo audit, filtrati (per isolare i 4 rossi intenzionali)

```
$ dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --no-build \
  --filter "FullyQualifiedName~VisualObservationFusionBoundaryTests|FullyQualifiedName~ClassifiedValueBridgeEquivalenceTests|FullyQualifiedName~MultimodalPipelineDeterminismTests|FullyQualifiedName~VisualObservationBoundaryTests"

Failed NosAi.Runtime.Tests.WorldModel.Fusion.MultimodalPipelineDeterminismTests.GameplayObservationUnobserved_CalledTwiceWithIdenticalArguments_DoesNotProduceEqualResults_KnownGameplayObservationGap [36 ms]
  Error Message:
   Assert.Equal() Failure: Values differ [...]

Failed NosAi.Runtime.Tests.WorldModel.Fusion.MultimodalPipelineDeterminismTests.EnrichMobs_FirstSightingOfANewMob_LeaksRealWallClockTime_InsteadOfADeterministicInstant_KnownWorldModelTemporalEnricherGap [19 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: 2099-01-01T00:00:00.0000000Z
Actual:   2026-09-05T21:48:00.0832742Z

Failed NosAi.Runtime.Tests.WorldModel.Fusion.MultimodalPipelineDeterminismTests.EnrichMobs_FirstSightingOfANewMob_ReplayedTwice_DoesNotProduceBitForBitEqualResults_KnownWorldModelTemporalEnricherGap [11 ms]
  Error Message:
   Assert.Equal() Failure: Values differ [...]

Failed NosAi.Runtime.Tests.WorldModel.Fusion.MultimodalPipelineDeterminismTests.GameplayObservationUnobserved_LeavesEveryUnoverriddenFieldWallClockStamped_InsteadOfUsingItsOwnAtUtcParameter_KnownGameplayObservationGap [< 1 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: 2099-01-01T00:00:00.0000000Z
Actual:   2026-09-05T21:48:00.1045256Z

Failed!  - Failed:     4, Passed:    38, Skipped:     0, Total:    42, Duration: 131 ms - NosAi.Runtime.Tests.dll (net8.0)
```

Esattamente i 4 difetti documentati in §2 (2 per `WorldModelTemporalEnricher`,
2 per `GameplayObservation.Unobserved`). Nessun altro test tra i 42 nuovi è
rosso.

### 4.4 Suite completa `NosAi.Core.Tests`

```
$ dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release --no-build

Passed!  - Failed:     0, Passed:   357, Skipped:     0, Total:   357, Duration: 4 s - NosAi.Core.Tests.dll (net8.0)
```

Nessuna regressione: 357/357, invariato rispetto ad AP-01/A5.

### 4.5 Suite completa `NosAi.Runtime.Tests`

```
$ dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --no-build

[... 58 righe SKIP, tutte pre-esistenti e non correlate ad AP-02
 (RuntimeIdentityTests/GameReferenceDatabaseTests/MapGridExtractorTests/ecc.,
 richiedono ambiente Windows/hardware reale) ...]

Failed!  - Failed:     4, Passed:  1876, Skipped:    58, Total:  1938, Duration: 15 s - NosAi.Runtime.Tests.dll (net8.0)
```

I soli 4 fallimenti, confermati con `grep -E "\[FAIL\]"` sull'output
completo, sono ESATTAMENTE i 4 test di regressione di questo audit (stessi
nomi di §4.3) — nessun test pre-esistente di Perception/AP-01/AP-02 è
diventato rosso, e **nessun flake** è comparso in questa esecuzione (il
comando di questo audit segnalava un possibile flake noto della sandbox
condivisa; non si è manifestato in questo run).

## 5. Limiti dichiarati / non affrontati

- I Difetti 1-2 (§2) sono reali ma fuori ownership di A5 in questa fase (uno
  in `NosAi.Core.WorldModel.Temporal`, l'altro in
  `NosAi.Runtime.LiveIntegration`, nessuno dei due tra i tre file assegnati
  ad A1/A3) — non corretti, lasciati con test di regressione rossi per
  l'integrazione successiva, come richiesto dal protocollo di questo
  progetto.
- Gli item 3.1-3.8 sono limiti accettati o comportamento onesto già corretto,
  non bloccanti — nessuna azione richiesta oltre tenerli presenti (in
  particolare 3.4 per una futura fase di risk-scoring in combattimento, e
  3.7 per un'eventuale validazione di unicità di `ResourceKind` in una
  fase futura di sensor fusion).
- Attività concorrente osservata fuori ambito durante questo audit (apertura
  di questo documento): `src/NosAi.Runtime/Program.cs`,
  `src/NosAi.Runtime/WorldModel/Fusion/WorldModelFusionLoop.cs`,
  `src/NosAi.Runtime/Perception/ScreenVitalsCapture.cs`,
  `tests/NosAi.Runtime.Tests/Perception/ScreenVitalsCaptureTests.cs`,
  `tests/NosAi.Runtime.Tests/WorldModel/Fusion/WorldModelFusionLoopVisualWiringTests.cs`
  — non ispezionati in profondità, non toccati, segnalati solo per
  trasparenza dato che condividono lo stesso working tree. La suite completa
  in §4.5 li include già nel proprio build/test.
- Nessuna evidenza da client NosTale reale esiste per questa fase — coerente
  con quanto già dichiarato da A1/A3 in `AP-02_STATUS.md` §5-6. **Questo
  audit non eleva il livello di verifica oltre `Integrated`.**
- La triplicazione di `DataSourceKind`/`ClassifiedValue<T>`/`WorldFact<T>`
  tra `NosAi.Runtime.Contracts`, `NosAi.Core.Hardware` e
  `NosAi.Core.WorldModel`, già segnalata da A1 in AP-01 e ri-osservata qui
  come causa diretta della necessità di alias `RuntimeDataSourceKind` nei
  nuovi test (`DataSourceKind` è altrimenti ambiguo in qualunque file che
  importi sia `NosAi.Runtime.Contracts` sia `NosAi.Core.WorldModel`), resta
  un item aperto per l'integrazione futura — non ri-analizzata qui.

## 6. Non dichiarato: `Verified`

Come richiesto: questo audit non dichiara mai `Verified`. Il massimo
onestamente dichiarabile per il pacchetto AP-02 così com'è oggi è
**`Integrated`** per l'albero combinato (build pulita, 357/357 Core.Tests,
1876/1876 Runtime.Tests non-skippati più i 4 test di regressione
deliberatamente rossi di questo stesso audit, 58 skip Windows-only
invariati) e **`Present`** per i 4 nuovi file di questo audit. Resta **non
`Verified`**: nessuna evidenza da client NosTale reale esiste per questa
fase — tutto quanto sopra è stato eseguito in una sandbox Linux senza
hardware ASUS Nitro V16/RTX 5060 né client Windows reale.
