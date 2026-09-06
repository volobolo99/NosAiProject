# AP-01 — Unified World Model — Audit A5 (Test, benchmark, documentazione)

**Autore:** A5 (Claude), audit indipendente. Nessun file di produzione è stato
toccato in questo task (ownership rigorosa: solo test/fixture/benchmark/doc).
**Data:** 2026-09-05.
**Ambito:** verifica indipendente del lavoro di A1 (contratti, `src/NosAi.Core/WorldModel/*.cs`),
A2 (sensor fusion, `src/NosAi.Runtime/WorldModel/Fusion/*.cs`) e A3 (belief
temporale, `src/NosAi.Core/WorldModel/Temporal/*.cs`) descritto in
`docs/agents/phases/AP-01/AP-01_STATUS.md`, seguendo lo stile di audit di
`docs/agents/phases/AP-00/AP-00_STATUS.md` §7-9 (A5 ha trovato 2 bug reali in
AP-00; qui ne sono stati trovati 4, con lo stesso principio: un difetto reale
si documenta con un test di regressione deliberatamente rosso, non si
corregge e non si nasconde).

**Aggiornamento importante, stessa sessione:** i 4 difetti reali documentati
in §2 sono stati trovati da questo audit tramite 4 test di regressione
scritti deliberatamente rossi. Mentre questo audit era ancora in corso, una
sessione concorrente attiva sullo stesso working tree (comportamento
coerente con il ruolo di integrazione A6, che cita esplicitamente "AP-01/A5
audit finding" nei propri commit) ha applicato correttivi a
`WorldModelClassification.cs`, `TemporalBelief.cs` e `FactFusion.cs` che
corrispondono esattamente alle direzioni di correzione suggerite in questo
report. Rieseguendo la suite completa **senza modificare i test scritti da
questo audit**, tutti e 4 i test tornano verdi e l'intera suite combinata
(Core.Tests + Runtime.Tests) è verde. Vedi §2.5 e §4.6 per l'evidenza esatta.
§2 resta intatta come registrazione storica del difetto trovato (file+riga,
scenario, causa radice) — è la prova che il test ha davvero rilevato il
problema prima che fosse corretto, non un'invenzione a posteriori.

Livello di verifica dichiarato per questo pacchetto di audit, dopo questo
sviluppo: **`Integrated`** per l'albero combinato (build pulita, l'intera
suite di test Core+Runtime passa, i 4 difetti trovati da A5 sono stati
corretti e i loro test di regressione lo confermano). Mai `Verified`
(nessuna evidenza da client NosTale reale in questo task — solo sandbox
Linux).

## 1. File creati

Tutti nuovi file; nessun file esistente di A1/A2/A3 è stato modificato o
indebolito.

- `tests/NosAi.Core.Tests/WorldModel/WorldFactBoundaryTests.cs`
- `tests/NosAi.Core.Tests/WorldModel/ResourceCustomKindAmbiguityTests.cs`
- `tests/NosAi.Core.Tests/WorldModel/Temporal/TemporalBeliefBoundaryTests.cs`
- `tests/NosAi.Core.Tests/WorldModel/Temporal/WorldModelTemporalEnricherDeterminismAndDuplicateIdTests.cs`
- `tests/NosAi.Runtime.Tests/WorldModel/Fusion/FactFusionBoundaryTests.cs`
- `tests/NosAi.Runtime.Tests/WorldModel/Fusion/GameplayObservationProjectorBoundaryTests.cs`
- `docs/agents/phases/AP-01/AP-01_A5_AUDIT.md` (questo file)

18 nuovi test in NosAi.Core.Tests, 7 nuovi test in NosAi.Runtime.Tests (25
totali). Al momento della scrittura, **4 fallivano di proposito** contro il
codice di A1/A2/A3 così com'era (4 difetti reali, mai introdotti da A5, non
correggibili dall'ownership di A5) e 21 passavano da subito. Come descritto
in §2.5, tutti e 4 sono stati corretti da un'altra sessione nel corso di
questo stesso audit: **oggi tutti e 25 passano** (vedi §4.6 per l'evidenza
finale).

## 2. Difetti reali trovati

Per ciascuno: file+riga esatti, scenario di fallimento concreto, test di
regressione (rosso di proposito), evidenza empirica (non solo lettura del
codice — ogni ipotesi è stata verificata contro il runtime .NET reale, non
assunta).

### Difetto 1 — `WorldFact<T>.ClampConfidence` non gestisce `NaN`

**File:** `src/NosAi.Core/WorldModel/WorldModelClassification.cs:95`
```csharp
private static double ClampConfidence(double confidence) => Math.Clamp(confidence, 0d, 1d);
```

`Math.Clamp` sostituisce il valore solo quando `value < min` o `value > max`;
entrambi i confronti sono sempre `false` per `NaN` (confermato con una prova
diretta sul runtime .NET reale durante questo audit, non assunto:
`Math.Clamp(double.NaN, 0.0, 1.0)` restituisce `NaN`). Di conseguenza
`WorldFact<T>.Live(value, double.NaN, ...)` (e allo stesso modo `.Derived`,
`.Cached`, `.Simulated`, che condividono lo stesso `ClampConfidence` privato)
produce un fatto con `Confidence = NaN` e **`HasValue == true`** — non viene
mai declassato a `Unknown`. Questo viola la promessa esplicita nel commento
XML di `WorldFact<T>` ("a continuous confidence score ... in [0, 1]") e in
`AP-01_STATUS.md` §2 ("Confidence continua in [0,1] (clampata nelle
factory)"). Nessuno dei casi già coperti da
`WorldFactTests.Confidence_IsAlwaysClampedToZeroOneRange` (-1, 0, 0.5, 1, 2.5)
esercitava `NaN`.

Conseguenza a valle dimostrata (vedi Difetto 2): una `Confidence` NaN
sopravvive fino a `FactFusion`, dove rompe l'ordine di precedenza
documentato.

**Test di regressione (rosso di proposito):**
`WorldFactBoundaryTests.Confidence_NaNInput_IsNotSanitizedIntoTheDocumentedZeroOneRange_KnownA1RobustnessGap`

**Nota metodologica collegata (non un difetto, chiarita per evitare un falso
allarme):** un'ipotesi plausibile scartata durante questo audit è che un
`NaN` dentro un `WorldFact<T>` rompa anche l'uguaglianza strutturale dei
record (quindi il determinismo del replay), dato che `NaN != NaN` con
l'operatore `==`. Verificato contro il runtime reale: **falso**.
L'uguaglianza generata dal compilatore per i record usa
`EqualityComparer<T>.Default`, che per `double` chiama `double.Equals(double)`
— e quel metodo, a differenza dell'operatore `==`, considera due `NaN`
uguali tra loro (confermato anche su hash code). Test verde dedicato:
`WorldFactBoundaryTests.TwoFactsWithANaNConfidence_BuiltFromTheSameInputs_AreStillEqual_NaNDoesNotBreakRecordEquality`.

### Difetto 2 — `FactFusion.IsBetter` con `Confidence = NaN`: vince chi capita per primo nella lista, non chi dovrebbe

**File:** `src/NosAi.Runtime/WorldModel/Fusion/FactFusion.cs:108-122` (in
particolare le righe 115-116):
```csharp
if (candidate.Fact.Confidence != current.Fact.Confidence)
    return candidate.Fact.Confidence > current.Fact.Confidence;
```

Confermato contro il runtime .NET reale: sotto IEEE-754, un confronto
`NaN`-vs-`x` è asimmetrico — `NaN != x` è **sempre vero**, ma **sia**
`x > NaN` **sia** `NaN > x` sono **sempre falsi**. Quindi, quando un
candidato con `Confidence = NaN` (costruibile oggi tramite il Difetto 1) si
trova nello slot "vincitore corrente" a parità di `DataSourceKind`,
`IsBetter(qualsiasiCandidatoSuccessivo, vincitore)` prende il ramo
`return ... > ...` e restituisce sempre `false` — **indipendentemente** dalla
confidence, dal timestamp o dal nome canale del candidato successivo. La
catena di tie-break documentata (confidence → timestamp → nome canale) non
viene mai eseguita. Effetto pratico: "vince chi il chiamante ha messo per
primo nella lista" nel momento in cui una confidence NaN è coinvolta,
sostituendo silenziosamente la regola di determinismo documentata con la
fortuna dell'ordine di inserimento — mentre il commento XML della classe
promette esplicitamente "two runs over the same candidates always agree"
indipendentemente dall'ordine.

Scenario concreto dimostrato dal test: due candidati con lo stesso
`DataSourceKind.Live`; il primo ha `Confidence = NaN`; il secondo ha
`Confidence = 0.9`, timestamp più recente e nome canale alfabeticamente
precedente — cioè vince su **ogni singolo criterio di tie-break
documentato**. Se il candidato NaN è elencato per primo, vince comunque lui
(sbagliato); se è elencato per secondo, il candidato corretto vince (il
comportamento "atteso" torna solo per un accidente di ordine).

**Test di regressione (rosso di proposito):**
`FactFusionBoundaryTests.NaNConfidenceCandidate_WhenListedFirst_WronglyOutlastsAStrictlyBetterLaterCandidate_KnownA2RobustnessGap`

**Test compagno (verde, prova che è un artefatto d'ordine):**
`FactFusionBoundaryTests.NaNConfidenceCandidate_WhenListedSecond_CorrectlyLoses_ProvingTheDefectIsAnOrderingArtifact`

### Difetto 3 — `TemporalBelief.EstimateVelocity`: i tre ritorni anticipati "Unknown" perdono tempo reale (wall-clock) invece di un istante deterministico

**File:** `src/NosAi.Core/WorldModel/Temporal/TemporalBelief.cs`, righe 81, 85, 87:
```csharp
if (!previous.HasValue || !current.HasValue)
    return WorldFact<WorldVelocity>.Unknown("insufficient_position_history");        // riga 81
...
if (elapsed <= TimeSpan.Zero)
    return WorldFact<WorldVelocity>.Unknown("non_increasing_observation_order");      // riga 85
if (elapsed > maxObservationGap)
    return WorldFact<WorldVelocity>.Unknown("observation_gap_too_large_for_a_reliable_estimate"); // riga 87
```

Nessuna delle tre chiamate passa l'argomento opzionale `observedAtUtc`, quindi
`WorldFact<T>.Unknown(reason, observedAtUtc: null)` ricade sul proprio
default — **`DateTime.UtcNow` reale** — invece di un istante derivato dagli
input del metodo (es. `current.ObservedAtUtc`, la convenzione già seguita da
ogni altro punto di chiamata a `Unknown(...)` in questo repository, incluso
tutto `GameplayObservationProjector`, che non omette mai l'argomento). Questo
rompe direttamente la garanzia dichiarata da `WorldModelTemporalEnricher`
("the same two snapshots always enrich to the same result") proprio nel caso
più comune per cui questo metodo esiste: **il primo avvistamento di
un'entità**, che prende sempre il ramo "insufficient_position_history".

**Come è stato scoperto:** empiricamente, non per ispezione. Una prima
versione di
`WorldModelTemporalEnricherDeterminismAndDuplicateIdTests.ThreeConsecutiveCycles_ReplayedTwice_ProduceBitForBitEqualFinalSnapshots`
lasciava la posizione del Player `Unknown` per tutto il test e falliva in
modo intermittente con un diff di uguaglianza tra due `WorldModelSnapshot`
del tutto inutile in output (il `ToString()` di default di
`EquatableArray<T>` non stampa gli elementi, solo il nome del tipo) — le due
esecuzioni altrimenti identiche catturavano ciascuna un istante reale diverso
per quel fatto Unknown. Il test è stato corretto dando al Player una
posizione nota (si veda `SnapshotWithPlayerAndMobs` nel file), e il difetto è
stato isolato qui, sulla primitiva, con un input di controllo inequivocabile.

**Test di regressione (rosso di proposito):**
`TemporalBeliefBoundaryTests.EstimateVelocity_InsufficientHistoryResult_LeaksRealWallClockTime_InsteadOfADeterministicInstant_KnownA3RobustnessGap`
— i due fatti Unknown in ingresso sono timestampati nell'anno 2099 apposta:
un'implementazione deterministica che derivasse il proprio timestamp dagli
input riporterebbe anch'essa un istante nel 2099; l'implementazione reale
riporta invece l'anno corrente reale (2026 in questa sandbox).

### Difetto 4 — `TemporalBelief.PredictPosition`: stesso problema, un secondo punto di chiamata

**File:** `src/NosAi.Core/WorldModel/Temporal/TemporalBelief.cs:122`:
```csharp
if (!lastKnown.HasValue)
    return WorldFact<WorldPosition>.Unknown("no_last_known_position_to_extrapolate_from");
```

Stessa causa radice del Difetto 3, ma qui è ancora più evidente: il metodo
riceve già un parametro esplicito `asOfUtc` che potrebbe essere usato per
questo ritorno anticipato, e non viene usato.

**Test di regressione (rosso di proposito):**
`TemporalBeliefBoundaryTests.PredictPosition_NoLastKnownPositionResult_LeaksRealWallClockTime_InsteadOfUsingItsOwnAsOfUtcParameter_KnownA3RobustnessGap`

### Direzione di correzione suggerita (per A6/A1/A2/A3, fuori ownership di A5)

- Difetto 1: in `ClampConfidence`, `double.IsNaN(confidence) ? 0d : Math.Clamp(confidence, 0d, 1d)` (o trattare un input NaN come se il chiamante avesse invocato `Unknown`).
- Difetto 2: in `IsBetter`, normalizzare la confidence prima del confronto (es. `double.IsNaN(x) ? -1d : x`) oppure scartare dalla lista `usable` ogni candidato con confidence NaN, allo stesso modo in cui i candidati stale/Unknown sono già scartati.
- Difetti 3-4: passare esplicitamente `current.ObservedAtUtc` (in `EstimateVelocity`) e `asOfUtc` (in `PredictPosition`) alle rispettive chiamate `Unknown(...)`.

Il Difetto 1 risolve automaticamente anche una parte della causa del Difetto
2 (niente più NaN costruibile tramite le factory pubbliche), ma il Difetto 2
va comunque corretto separatamente: `FactFusion` non dovrebbe fidarsi
ciecamente del fatto che ogni `WorldFact<T>` in ingresso sia stato costruito
tramite le factory di A1 — è un modulo di fusione che riceve dati da fonti
esterne (rete/memoria/screen), quindi difendersi da un input scorretto è
nel suo stesso interesse (fail-closed).

## 2.5 Aggiornamento: i 4 difetti sono stati corretti durante questa stessa sessione

Mentre questo audit era in corso, una sessione concorrente attiva sullo
stesso working tree (verosimilmente A6/integrazione, o A1/A2/A3 stessi in
riconsiderazione) ha applicato correttivi che seguono **esattamente** le
direzioni suggerite sopra, citando questo audit nei propri commenti XML
(`"AP-01/A5 audit finding"`). Diff esatto osservato (nessuna riga toccata da
A5):

```diff
--- a/src/NosAi.Core/WorldModel/Temporal/TemporalBelief.cs
+++ b/src/NosAi.Core/WorldModel/Temporal/TemporalBelief.cs
@@ EstimateVelocity
         if (!previous.HasValue || !current.HasValue)
-            return WorldFact<WorldVelocity>.Unknown("insufficient_position_history");
+            return WorldFact<WorldVelocity>.Unknown("insufficient_position_history", current.ObservedAtUtc);

         TimeSpan elapsed = current.ObservedAtUtc - previous.ObservedAtUtc;
         if (elapsed <= TimeSpan.Zero)
-            return WorldFact<WorldVelocity>.Unknown("non_increasing_observation_order");
+            return WorldFact<WorldVelocity>.Unknown("non_increasing_observation_order", current.ObservedAtUtc);
         if (elapsed > maxObservationGap)
-            return WorldFact<WorldVelocity>.Unknown("observation_gap_too_large_for_a_reliable_estimate");
+            return WorldFact<WorldVelocity>.Unknown("observation_gap_too_large_for_a_reliable_estimate", current.ObservedAtUtc);
@@ PredictPosition
         if (!lastKnown.HasValue)
-            return WorldFact<WorldPosition>.Unknown("no_last_known_position_to_extrapolate_from");
+            return WorldFact<WorldPosition>.Unknown("no_last_known_position_to_extrapolate_from", asOfUtc);

--- a/src/NosAi.Core/WorldModel/WorldModelClassification.cs
+++ b/src/NosAi.Core/WorldModel/WorldModelClassification.cs
-    private static double ClampConfidence(double confidence) => Math.Clamp(confidence, 0d, 1d);
+    private static double ClampConfidence(double confidence) => double.IsNaN(confidence) ? 0d : Math.Clamp(confidence, 0d, 1d);

--- a/src/NosAi.Runtime/WorldModel/Fusion/FactFusion.cs
+++ b/src/NosAi.Runtime/WorldModel/Fusion/FactFusion.cs
-        if (candidate.Fact.Confidence != current.Fact.Confidence)
-            return candidate.Fact.Confidence > current.Fact.Confidence;
+        double candidateConfidence = double.IsNaN(candidate.Fact.Confidence) ? -1d : candidate.Fact.Confidence;
+        double currentConfidence = double.IsNaN(current.Fact.Confidence) ? -1d : current.Fact.Confidence;
+        if (candidateConfidence != currentConfidence)
+            return candidateConfidence > currentConfidence;
```

Rieseguendo l'intera suite **senza modificare una sola riga dei test scritti
da questo audit** (nessun test indebolito, nessuno saltato, nessuno
cancellato):

- I 4 test di regressione (Difetti 1-4) sono passati da rossi a **verdi**.
- I test "compagni" pensati per restare verdi lo sono rimasti
  (`Confidence_InfiniteInput_IsStillCorrectlyClamped_UnlikeNaN`,
  `NaNConfidenceCandidate_WhenListedSecond_CorrectlyLoses_ProvingTheDefectIsAnOrderingArtifact`).
- Nessun altro test, tra i 25 aggiunti da questo audit o tra quelli
  preesistenti di A1/A2/A3, ha cambiato esito.
- L'intera suite combinata Core.Tests + Runtime.Tests è verde (evidenza
  esatta di build/test dopo la correzione in §4.6 di questo stesso
  documento: 357/357 Core.Tests, 1812/1870 Runtime.Tests con 58 skip
  Windows-only invariati e 0 falliti).

Questo è precisamente il ciclo trovato→documentato→corretto→riconfermato che
il protocollo di questo progetto richiede, solo compresso nella stessa
finestra temporale invece che in due sessioni separate come in AP-00.

## 3. Lacune di copertura documentate (test verdi, nessuna correzione necessaria)

Per ognuna: decisione esplicita se è un difetto reale o un limite accettato,
come richiesto dal comando AP-01/A5.

1. **Overflow/NaN nell'aritmetica di `TemporalBelief` con posizioni estreme**
   (`TemporalBeliefBoundaryTests.EstimateVelocity_ExtremeOppositeSignPositions_OverflowsToInfinity_NotFlaggedAsUnknown`,
   `.PredictPosition_InfiniteVelocityWithAZeroHorizon_ProducesNaN_InfinityTimesZero`).
   `float.MaxValue - (-float.MaxValue)` va in overflow a `+Infinity`
   (aritmetica IEEE-754 ordinaria, non un bug introdotto da questo codice);
   `Infinity * 0 == NaN`, raggiungibile con una velocità infinita e un
   `horizon` zero, entrambi input singolarmente leciti per il contratto di
   `PredictPosition`. **Decisione:** conseguenza a valle della stessa causa
   radice del Difetto 1/2 (nessuna validazione di finitezza in `WorldFact<T>`
   né in `TemporalBelief`), non trattata come un difetto separato per non
   gonfiare il numero di test rossi — documentata come comportamento
   accettato/pinnato, segnalata ad A6 insieme ai difetti 1-2 come stesso
   fronte di lavoro.
2. **Duplicate `EntityId` nella stessa lista `Mobs`**
   (`WorldModelTemporalEnricherDeterminismAndDuplicateIdTests.DuplicateEntityIdInCurrentMobs_IsNotMergedOrRejected_BothEntriesSurvive_DocumentedGap`,
   `.DuplicateEntityIdInPreviousMobs_MatchesAgainstWhicheverIsLastInListOrder_DocumentedGap`).
   Né `WorldModelTemporalEnricher` né `GameplayObservationProjector`
   convalidano l'unicità di `EntityId` in una lista `Mobs`. Un duplicato in
   `current.Mobs` non viene unito né rifiutato (entrambe le voci
   sopravvivono, arricchite indipendentemente). Un duplicato in
   `previous.Mobs` produce un comportamento più sottile: `EnrichMobs`
   costruisce un `Dictionary<EntityId, Mob>` che tiene silenziosamente solo
   l'**ultimo** elemento della lista con quell'id (`previousById[mob.Id] =
   mob` sovrascrive) — deterministico per una lista fissa, ma questo
   contraddice la proprietà generale "il matching è insensibile all'ordine
   della lista precedente" confermata separatamente da
   `PreviousMobListOrder_DoesNotAffectTheDerivedVelocity_WhenIdsAreUnique`:
   quella proprietà smette di valere silenziosamente nel momento in cui
   `previous` contiene un id duplicato. **Decisione:** limite noto, non
   difetto bloccante — A2 (sensor fusion)/A4 (wiring runtime) non dovrebbero
   mai produrre una lista con id duplicati; nulla oggi lo impedirebbe se
   accadesse. Segnalato come possibile futura validazione (in
   `GameplayObservationProjector` o nel chiamante condiviso di A4), non
   risolto qui.
3. **`FactFusion.Resolve` con candidati duplicati dello stesso canale**
   (`FactFusionBoundaryTests.DuplicateChannelName_WithConflictingValues_ResolvesToTheFirstListedCandidate_WithASelfReferencingDisagreementDetail`).
   Due candidati identici su ogni criterio documentato (inclusa la stringa
   del canale) risolvono deterministicamente al primo elencato dal
   chiamante — comportamento corretto e riproducibile, solo non esplicitato
   per nome nel commento XML della classe. Effetto collaterale minore
   individuato: il messaggio diagnostico risultante nomina lo **stesso**
   canale sia come vincitore sia come "canale in disaccordo" (es. `'network'
   won over disagreeing channel(s): network`), leggibile come insensato da
   un umano pur essendo la risoluzione sottostante corretta. **Decisione:**
   non un difetto di correttezza, solo una stranezza diagnostica minore,
   non bloccante, segnalata per un'eventuale pulizia futura del testo di
   log.
4. **`GameplayObservationProjector` con vnum/slot/quantità negativi**
   (`GameplayObservationProjectorBoundaryTests.NegativeVnum_...`,
   `.NegativeSlotIndex_...`, `.NegativeAmount_...`, `.NegativeDropId_...`).
   Un vnum negativo produce un `ItemId` sintatticamente valido come "-5"
   (il costruttore di `ItemId` convalida solo stringa non vuota); uno slot
   negativo viene riportato come un fatto `Live`/`HasValue == true`, non
   `Unknown`, anche se altrove un "-1" è un sentinella comune per "nessuno
   slot"; un `DropId` negativo produce un `EntityId` con doppio trattino
   cosmetico ("drop--7") ma comunque valido. **Decisione, come richiesto
   esplicitamente dal comando:** NON è un difetto da correggere in questa
   fase. `ItemId`/`EntityId` sono documentati come identificatori opachi che
   rispecchiano la numerazione del wire; nessun codice in questo repository
   interpreta questi id come numeri, e non esiste ancora un catalogo di
   riferimento che potrebbe rifiutare un vnum fuori range (lo stesso motivo
   per cui `GameplayObservationProjector` lascia vuoti Mob/Npc/Skill/
   Equipment). Segnalato per AP-02+: se il protocollo di rete userà mai `-1`
   come sentinella esplicita per "slot assente", questa proiezione andrà
   rivista per non riportarlo come un valore osservato.
5. **`Resource` con `ResourceKind.Custom` e nomi coincidenti**
   (`ResourceCustomKindAmbiguityTests.TwoUnrelatedCustomResources_SharingOnlyAName_AreIndistinguishableWhenTheirNumbersCoincide`,
   `.CustomResourceEquality_IsPurelyStructural_ThereIsNoSeparateIdentityFieldToDisambiguate`).
   Due `Resource` con `Kind = Custom` e lo stesso `CustomName` ma significati
   di gioco completamente diversi sono indistinguibili a livello di tipo:
   l'uguaglianza è puramente strutturale (`Current`/`Maximum`/`CustomName`),
   non esiste un id opaco separato dal nome visualizzato. **Decisione:**
   limite noto, non un difetto — non serve necessariamente un fix in questa
   fase, ma va tenuto presente se in futuro due meccaniche di gioco distinte
   dovessero condividere per caso lo stesso nome custom.

## 4. Evidenza di build/test — comandi e output esatti

Ambiente: `export PATH="$PATH:/root/.dotnet"` eseguito prima di ogni comando.

### 4.1 Build di produzione (nessuna modifica di A5 le tocca; verificate comunque)

```
$ dotnet build src/NosAi.Core/NosAi.Core.csproj -c Release
  Determining projects to restore...
  All projects are up-to-date for restore.
  NosAi.Analyzers -> .../tools/NosAi.Analyzers/bin/Release/netstandard2.0/NosAi.Analyzers.dll
  NosAi.Core -> .../src/NosAi.Core/bin/Release/net8.0/NosAi.Core.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:01.57

$ dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
  Determining projects to restore...
  All projects are up-to-date for restore.
  NosAi.Analyzers -> .../NosAi.Analyzers.dll
  NosAi.Protocol -> .../NosAi.Protocol.dll
  NosAi.Core -> .../NosAi.Core.dll
  NosAi.Runtime -> .../src/NosAi.Runtime/bin/Release/net8.0-windows/NosAi.Runtime.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:03.11
```

### 4.2 Build dei progetti di test (con i nuovi file di A5)

```
$ dotnet build tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
  ...
  NosAi.Core.Tests -> .../tests/NosAi.Core.Tests/bin/Release/net8.0-windows/NosAi.Core.Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:07.10

$ dotnet build tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
  ...
  NosAi.Runtime.Tests -> .../tests/NosAi.Runtime.Tests/bin/Release/net8.0-windows/NosAi.Runtime.Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:04.10
```

Zero warning su Core/Core.Tests (`TreatWarningsAsErrors=true` rispettato).

### 4.3 Solo i nuovi test di questo audit, filtrati (per isolare i 4 rossi intenzionali)

```
$ dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release --no-build \
  --filter "FullyQualifiedName~WorldFactBoundaryTests|FullyQualifiedName~ResourceCustomKindAmbiguityTests|FullyQualifiedName~TemporalBeliefBoundaryTests|FullyQualifiedName~WorldModelTemporalEnricherDeterminismAndDuplicateIdTests"

  Passed NosAi.Core.Tests.WorldModel.WorldFactBoundaryTests.IsFresh_WithZeroMaxAge_IsTrueOnlyAtTheExactSameInstant [6 ms]
  Passed NosAi.Core.Tests.WorldModel.Temporal.TemporalBeliefBoundaryTests.EstimateVelocity_ExtremeOppositeSignPositions_OverflowsToInfinity_NotFlaggedAsUnknown [9 ms]
  Passed NosAi.Core.Tests.WorldModel.WorldFactBoundaryTests.Confidence_InfiniteInput_IsStillCorrectlyClamped_UnlikeNaN(input: Infinity, expected: 1) [3 ms]
  Passed NosAi.Core.Tests.WorldModel.WorldFactBoundaryTests.Confidence_InfiniteInput_IsStillCorrectlyClamped_UnlikeNaN(input: -Infinity, expected: 0) [< 1 ms]
  Passed NosAi.Core.Tests.WorldModel.ResourceCustomKindAmbiguityTests.TwoUnrelatedCustomResources_SharingOnlyAName_AreIndistinguishableWhenTheirNumbersCoincide [16 ms]
  Passed NosAi.Core.Tests.WorldModel.ResourceCustomKindAmbiguityTests.CustomResourceEquality_IsPurelyStructural_ThereIsNoSeparateIdentityFieldToDisambiguate [2 ms]
  Passed NosAi.Core.Tests.WorldModel.WorldFactBoundaryTests.UnknownBoolFact_DefaultsToFalse_ButIsNeverConfusableWithAnObservedFalse_ViaHasValue [4 ms]
  Failed NosAi.Core.Tests.WorldModel.WorldFactBoundaryTests.Confidence_NaNInput_IsNotSanitizedIntoTheDocumentedZeroOneRange_KnownA1RobustnessGap [< 1 ms]
  Error Message:
   WorldFact<T>'s own XML doc promises Confidence is always clamped into [0,1]; Math.Clamp(double.NaN, 0, 1) returns NaN unchanged, so a NaN confidence input silently survives into the fact instead of being rejected/clamped/zeroed.
  Failed NosAi.Core.Tests.WorldModel.Temporal.TemporalBeliefBoundaryTests.PredictPosition_NoLastKnownPositionResult_LeaksRealWallClockTime_InsteadOfUsingItsOwnAsOfUtcParameter_KnownA3RobustnessGap [5 ms]
  Error Message:
   Expected PredictPosition to stamp this Unknown result using its own asOfUtc parameter (year 2099); got 2026-09-05T21:04:58.4174770Z, which is real wall-clock time leaking through WorldFact<WorldPosition>.Unknown(reason)'s default observedAtUtc parameter.
  Failed NosAi.Core.Tests.WorldModel.Temporal.TemporalBeliefBoundaryTests.EstimateVelocity_InsufficientHistoryResult_LeaksRealWallClockTime_InsteadOfADeterministicInstant_KnownA3RobustnessGap [< 1 ms]
  Error Message:
   Expected a deterministic instant derived from the method's own inputs (year 2099); got 2026-09-05T21:04:58.4375375Z, which is real wall-clock time (DateTime.UtcNow) leaking through WorldFact<WorldVelocity>.Unknown(reason)'s default observedAtUtc parameter.
  Passed NosAi.Core.Tests.WorldModel.Temporal.WorldModelTemporalEnricherDeterminismAndDuplicateIdTests.DuplicateEntityIdInPreviousMobs_MatchesAgainstWhicheverIsLastInListOrder_DocumentedGap [24 ms]
  Passed NosAi.Core.Tests.WorldModel.Temporal.TemporalBeliefBoundaryTests.PredictPosition_InfiniteVelocityWithAZeroHorizon_ProducesNaN_InfinityTimesZero [< 1 ms]
  Passed NosAi.Core.Tests.WorldModel.WorldFactBoundaryTests.TwoFactsWithANaNConfidence_BuiltFromTheSameInputs_AreStillEqual_NaNDoesNotBreakRecordEquality [4 ms]
  Passed NosAi.Core.Tests.WorldModel.WorldFactBoundaryTests.IsFresh_WithNegativeMaxAge_TreatsEveryFactAsStale_FailsClosed [< 1 ms]
  Passed NosAi.Core.Tests.WorldModel.WorldFactBoundaryTests.WorldVelocity_ZeroZero_IsAsLegitimateAnObservedValueAsAnyOther_OnlyHasValueTellsItApartFromUnknown [3 ms]
  Passed NosAi.Core.Tests.WorldModel.Temporal.WorldModelTemporalEnricherDeterminismAndDuplicateIdTests.PreviousMobListOrder_DoesNotAffectTheDerivedVelocity_WhenIdsAreUnique [15 ms]
  Passed NosAi.Core.Tests.WorldModel.Temporal.WorldModelTemporalEnricherDeterminismAndDuplicateIdTests.ThreeConsecutiveCycles_ReplayedTwice_ProduceBitForBitEqualFinalSnapshots [9 ms]
  Passed NosAi.Core.Tests.WorldModel.Temporal.WorldModelTemporalEnricherDeterminismAndDuplicateIdTests.DuplicateEntityIdInCurrentMobs_IsNotMergedOrRejected_BothEntriesSurvive_DocumentedGap [6 ms]

Test Run Failed.
Total tests: 18
     Passed: 15
     Failed: 3
```

```
$ dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --no-build \
  --filter "FullyQualifiedName~FactFusionBoundaryTests|FullyQualifiedName~GameplayObservationProjectorBoundaryTests"

  Failed NosAi.Runtime.Tests.WorldModel.Fusion.FactFusionBoundaryTests.NaNConfidenceCandidate_WhenListedFirst_WronglyOutlastsAStrictlyBetterLaterCandidate_KnownA2RobustnessGap [2 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
   Expected: 2
   Actual:   1
  Passed NosAi.Runtime.Tests.WorldModel.Fusion.FactFusionBoundaryTests.NaNConfidenceCandidate_WhenListedSecond_CorrectlyLoses_ProvingTheDefectIsAnOrderingArtifact [13 ms]
  Passed NosAi.Runtime.Tests.WorldModel.Fusion.GameplayObservationProjectorBoundaryTests.NegativeSlotIndex_IsReportedAsAConfidentlyKnownValue_NotAsUnknown [25 ms]
  Passed NosAi.Runtime.Tests.WorldModel.Fusion.GameplayObservationProjectorBoundaryTests.NegativeAmount_PassesThroughUnvalidated [< 1 ms]
  Passed NosAi.Runtime.Tests.WorldModel.Fusion.GameplayObservationProjectorBoundaryTests.NegativeVnum_ProjectsIntoAnItemId_ThatCarriesTheMinusSignAsPlainText [1 ms]
  Passed NosAi.Runtime.Tests.WorldModel.Fusion.GameplayObservationProjectorBoundaryTests.NegativeDropId_ProducesADoubleDashEntityId_CosmeticButNotRejected [2 ms]
  Passed NosAi.Runtime.Tests.WorldModel.Fusion.FactFusionBoundaryTests.DuplicateChannelName_WithConflictingValues_ResolvesToTheFirstListedCandidate_WithASelfReferencingDisagreementDetail [< 1 ms]

Test Run Failed.
Total tests: 7
     Passed: 6
     Failed: 1
```

**Riepilogo:** 4 test rossi in totale, esattamente i 4 difetti sopra
documentati. Nessun altro test tra i 25 nuovi è rosso.

### 4.4 Suite completa `NosAi.Core.Tests` (per confermare l'assenza di regressioni sui test esistenti di A1/A2/A3)

```
$ dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release --no-build

[le stesse 3 righe FAIL della sezione 4.3, identiche] più:

[xUnit.net] NosAi.Core.Tests.TransportLoopTests.OneHundredLoopbackHandshakesStayUnderTheTwentyFiveMillisecondBudget [FAIL]
  Failed NosAi.Core.Tests.TransportLoopTests.OneHundredLoopbackHandshakesStayUnderTheTwentyFiveMillisecondBudget [956 ms]
  Error Message:
   Loopback handshake p99 was 179.930 ms (T-06 still requires a real phone).

Failed!  - Failed:     4, Passed:   353, Skipped:     0, Total:   357, Duration: 3 s - NosAi.Core.Tests.dll (net8.0)
```

Il quarto fallimento (`TransportLoopTests...`) è il flake noto,
non correlato ad AP-01, già segnalato esplicitamente nel comando di questo
task ("un fallimento intermittente e NON correlato ... già noto e da
ignorare — riesegui quel singolo test per confermare che è flake").
Riverificato in isolamento:

```
$ dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release --no-build \
  --filter "FullyQualifiedName~TransportLoopTests.OneHundredLoopbackHandshakesStayUnderTheTwentyFiveMillisecondBudget"

Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: < 1 ms - NosAi.Core.Tests.dll (net8.0)
```

Confermato: flake sensibile al carico della sandbox condivisa, non una
regressione introdotta da questo audit. **Nessun test pre-esistente di
A1/A2/A3 in `NosAi.Core.Tests` è stato indebolito o è diventato rosso**: gli
unici 4 fallimenti sono i 3 nuovi test-difetto di A5 più questo flake
pre-esistente e non correlato.

### 4.5 Suite completa `NosAi.Runtime.Tests`

```
$ dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --no-build

[riga FAIL identica alla sezione 4.3 per FactFusionBoundaryTests.NaNConfidenceCandidate_WhenListedFirst_...]

Failed!  - Failed:     1, Passed:  1811, Skipped:    58, Total:  1870, Duration: 19 s - NosAi.Runtime.Tests.dll (net8.0)
```

(Le righe `SKIP` — 58 in totale, tutte pre-esistenti e non correlate ad
AP-01, es. `RuntimeIdentityTests`/`MemoryScannerTests`/`NosArchiveTests` che
richiedono ambiente Windows/hardware reale — sono omesse qui per brevità; il
conteggio esatto (58) è invariato rispetto a quanto atteso e nessuna riga
SKIP proviene da un file toccato da questo audit.) **Nessun test
pre-esistente di A2 in `NosAi.Runtime.Tests` è stato indebolito**: l'unico
fallimento era il test-difetto di A5, corretto poco dopo (§4.6).

### 4.6 Riverifica finale, dopo la correzione dei 4 difetti (§2.5)

Rebuild pulito e riesecuzione completa di entrambe le suite, dopo che la
sessione concorrente ha applicato le correzioni. Nessun file di test è stato
toccato da A5 tra la prima e la seconda esecuzione.

```
$ dotnet build src/NosAi.Core/NosAi.Core.csproj -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)

$ dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)

$ dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release --no-build
Passed!  - Failed:     0, Passed:   357, Skipped:     0, Total:   357, Duration: 4 s - NosAi.Core.Tests.dll (net8.0)

$ dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
Passed!  - Failed:     0, Passed:  1812, Skipped:    58, Total:  1870, Duration: 15 s - NosAi.Runtime.Tests.dll (net8.0)
```

**Zero fallimenti in entrambe le suite** (il precedente singolo fallimento
di `TransportLoopTests` in un run intermedio è stato il flake noto,
riconfermato tale — vedi §4.4 — e non riappare in questo run finale). I 4
test di regressione dei Difetti 1-4 sono ora verdi; nessun test è stato
rimosso, saltato o indebolito per ottenere questo risultato.

## 5. Limiti dichiarati / non affrontati (rimandati ad A6 o a fasi successive)

- I 4 difetti reali (§2) sono stati corretti durante questa stessa sessione
  da un agente concorrente (§2.5), non da A5 (fuori ownership di A5 in ogni
  caso). Nessuna azione residua su di essi.
- Le 5 lacune documentate in §3 sono limiti accettati o stranezze minori, non
  bloccanti — nessuna azione richiesta se non tenerle presenti nelle fasi
  successive (AP-02 per il catalogo vnum/nomi, AP-04+ per un'eventuale
  validazione di unicità degli `EntityId`).
- La triplicazione di `DataSourceKind`/`ClassifiedValue<T>`/`WorldFact<T>` tra
  `NosAi.Runtime.Contracts`, `NosAi.Core.Hardware` e `NosAi.Core.WorldModel`,
  già segnalata da A1 in `AP-01_STATUS.md` §3 punto 1 e §6, resta un item
  aperto per l'integrazione futura — non ri-analizzata qui, non è compito di
  A5 duplicare quell'audit.
- Durante questo audit è stata osservata attività concorrente, non di A5, su
  file al di fuori della sua ownership: `src/NosAi.Runtime/WorldModel/Fusion/WorldModelFusionLoop.cs`
  (nuovo), `src/NosAi.Runtime/Program.cs`, `src/NosAi.Runtime/Configuration/Gate1HostOptions.cs`,
  `src/NosAi.Runtime/Observability/ModuleReachability.cs` e
  `tests/NosAi.Runtime.Tests/Gate1ObservationTests.cs` (verosimilmente
  lavoro A4, "runtime wiring from existing observation snapshots into the
  World Model" per `AGENT_COMMAND_REGISTRY.md`). Non fanno parte
  dell'ambito di questo audit (né dei contratti A1, né della fusion A2, né
  del belief temporale A3) e non sono stati ispezionati né toccati da A5;
  segnalati qui solo per trasparenza, dato che condividono lo stesso working
  tree. Le suite complete in §4.6 li includono già nel proprio build/test e
  risultano verdi.
- Nessuna evidenza da client NosTale reale esiste per questa fase — coerente
  con quanto già dichiarato da A1/A2/A3 stessi in `AP-01_STATUS.md` §5/§7/§8.
  **Questo audit non eleva il livello di verifica oltre `Integrated`.**

## 6. Non dichiarato: `Verified`

Come richiesto: questo audit non dichiara mai `Verified`. Il massimo
onestamente dichiarabile per il pacchetto AP-01 così com'è oggi è
**`Integrated`**: l'albero combinato builda pulito e l'intera suite di test
Core+Runtime passa (357/357 e 1812/1812 non-skippati rispettivamente), i 4
difetti reali trovati da questo audit sono stati corretti e i loro test di
regressione lo confermano senza alcuna regressione altrove. Resta
**non `Verified`**: nessuna evidenza da client NosTale reale esiste per
questa fase — tutto quanto sopra è stato eseguito in una sandbox Linux senza
hardware ASUS Nitro V16/RTX 5060 né client Windows reale, come già dichiarato
onestamente da A1/A2/A3 in `AP-01_STATUS.md`.
