# AP-02 / A2+A4 — DeepSeek — il pannello dice il falso in quattro punti

**Si può fare in parallelo a Q-140**: nessun file in comune. Q-140 sta in
`src/NosAi.Runtime/Perception/Network/`, `LiveIntegration/GameplayProvider.cs`,
`Observability/WireInspectCommand.cs` e `Program.cs`. **Questo task non tocca
nessuno di quelli.**

## Cosa possiedi

- `src/NosAi.ControlPanel/PracticalTestCenterWindow.xaml.cs`
- `src/NosAi.ControlPanel/CognitiveRuntimeTraceBridge.cs`
- `src/NosAi.ControlPanel/CognitiveMemoryWindow.xaml.cs`
- `src/NosAi.ControlPanel/GameplayWireReader.cs`
- `src/NosAi.ControlPanel/CombatInspect.cs`
- file di test nuovi in `tests/NosAi.ControlPanel.Tests/`

**Non toccare** `MainWindow.xaml(.cs)`, `SurroundingsInspect.cs`,
`ScreenCalibrationInspect.cs`, `CatalogueNames.cs`, `WireMessageInspect.cs` —
appena corretti da Claude — né niente sotto `src/NosAi.Runtime/`.

**Compila solo il tuo progetto**: `dotnet build src/NosAi.ControlPanel` e
`dotnet build tests/NosAi.ControlPanel.Tests`, mai `dotnet build NosAi.sln`. Un
altro agente compila la soluzione in parallelo, e un `error MSB3027 … il file è
bloccato` **è contesa, non una tua regressione**: aspetta e riprova.

**Cartella**: `C:\Users\volob\Desktop\NosAiProject`. `git pull`, **`git push` tu**.

**Se la specifica e il codice non concordano, ha ragione il codice**: fermati e
riferisci.

---

## Perché questo task esiste

C'è una regola permanente dell'utente: **il pannello è la superficie con cui si
eseguono i test sul client reale, e deve rispecchiare dati reali.** Un audit del
2026-09-08 ha trovato quindici punti in cui non lo fa; Claude ne ha corretti
cinque, questi quattro restano e sono i più gravi fra quelli rimasti.

L'invariante che violano è scritta in `CLAUDE.md`: **«Unknown is not zero, false
or empty»** e «Real, derived, cached and simulated data remain explicitly
distinguishable».

---

## Parte 1 — il test T5 è sempre «Blocked», e per una chiave che non esiste

`PracticalTestCenterWindow.xaml.cs:188`:

```csharp
result = HasProperty(snapshot, "mapWorld") || HasProperty(snapshot, "entities")
    ? PracticalTestResult.Unknown : PracticalTestResult.Blocked;
```

Cerca `mapWorld` ed `entities` **alla radice** dello snapshot. Le chiavi di
radice sono, e le elenca `Gate1CanonicalSnapshot.cs:166-229`:

```
contractVersion, runtimeStatus, capturedAtUtc, correlationId, warning,
hardware, client, guard, safety, gameObservation, resilience
```

Le entità stanno in `client.gameplayBaseline.value.entities`. **T5 verdetta quindi
`Blocked` anche con la lista piena**, e mostra come evidenza `canonical.map/entities`,
un percorso che non esiste.

**Cosa fare**: leggere il percorso vero. `OperatorApiSnapshot.cs:10-24` documenta
la stessa deriva già corretta altrove — leggilo prima, e segui quella forma.

**Cosa non fare**: non promuovere T5 a `Pass`. Il verdetto onesto con entità
presenti resta `Unknown` — le entità ci sono, la verifica prima/dopo di
movimento e ripianificazione no, ed è ciò che il `detail` già dice.

## Parte 2 — T8 dà un motivo falso

`PracticalTestCenterWindow.xaml.cs:204` stampa
`character_inventory_state_not_published`. **L'inventario è pubblicato**:
`GameplayObservation.ToWire()` (`GameplayProvider.cs:279-340`) scrive `inventory`
con kind, slot, vnum, quantità e rarità, popolato dal vivo da
`NetworkGameplayProvider` (`:827-840`).

**Cosa fare**: leggere l'inventario dallo snapshot e dare il verdetto che i dati
sostengono. Se resta bloccato, **il motivo dev'essere quello vero** — per
esempio che `InventoryKind` non distingue ancora equipaggiato da zaino (è il
limite reale, aperto in `docs/TEST_RIMANDATI.md` § T-12) — non che il contratto
non esista.

**La regola generale, che vale per tutti i verdetti di quella finestra**: un
motivo va verificato prima di essere stampato. Controlla **anche gli altri**
(T7 `quest_state_not_published`, e ogni `Blocked` costante) e correggi quelli
falsi. Quelli che risultano veri lasciali stare e **dillo nel report**.

## Parte 3 — un valore mai osservato si disegna come cella vuota

`PracticalTestCenterWindow.xaml.cs:147-152`:

```csharp
if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("value", out var classified))
    return classified.ToString();
```

Per un `ClassifiedValue` UNKNOWN il filo scrive `"value": null`
(`DataClassification.cs:80`), e `JsonElement.ToString()` su `Null` restituisce
**stringa vuota**. Il `failureReason`, che è nello stesso oggetto JSON, viene
buttato. Un HP mai osservato appare come una riga «HP» vuota.

**Cosa fare**: quando il valore è nullo, mostrare `UNKNOWN · <failureReason>`.
Se anche il motivo manca, dirlo: una riga vuota non è un dato.

**Nello stesso metodo, riga 132-136**: la provenienza mostrata è una stringa
scelta a mano (`"Local"`, `"Network"`, `"Screen"`) passata dal chiamante, e il
campo `source` del JSON — che porta LIVE / DERIVED / CACHED / SIMULATED /
UNKNOWN — **non viene mai letto**. Un valore CACHED o SIMULATED è oggi
indistinguibile da uno LIVE, che è precisamente ciò che l'architettura vieta.

**Cosa fare**: leggere `source` dal JSON e mostrarlo. La stringa scritta a mano
può restare come etichetta di *categoria* (da dove viene la misura), ma non deve
occupare il posto della provenienza.

## Parte 4 — confidenza e rischio sono numeri inventati, stampati come percentuali

`CognitiveRuntimeTraceBridge.cs:132-148`:

```csharp
private static double ConfidenceFor(CycleOutcome outcome) => outcome switch
{
    CycleOutcome.Confirmed => 1.0,
    CycleOutcome.Unverified => 0.5,
    CycleOutcome.NoCandidate => 0.8,
    _ => 0.0
};
```

Due `switch` costanti sull'esito, e `CognitiveMemoryWindow.xaml.cs:64` li stampa
come `Confidence {…:P0} · Risk {…:P0}` — cioè **«Confidence 50% · Risk 50%»**,
che a chi guarda sembra una misura.

**Non esiste nessuna misura dietro**: né `Gate3LoopCycle`
(`Gate3DecisionLoop.cs:10`) né `StageOutcomeDump` (`PipelineStageBoard.cs:5`)
portano una confidenza o un rischio. Sono due numeri scelti a tavolino.

**Cosa fare**: togliere i due numeri inventati e mostrare al loro posto ciò che
esiste davvero — l'esito del ciclo, che è un fatto. Dove l'interfaccia vuole una
confidenza, deve dire **UNKNOWN con il motivo**, non una percentuale.

**Cosa non fare**: non inventare una formula che li calcoli. Il dato non c'è, e
un numero derivato da nulla resterebbe un numero inventato con un'aria più
rispettabile. Se un giorno il runtime pubblicherà una confidenza, quella riga si
riempirà da sola.

---

## Test (file nuovi in `tests/NosAi.ControlPanel.Tests/`)

`PracticalTestCenterTests.cs` esiste ed è di 37 righe: **non copre nessuno di
questi comportamenti**. Non modificarlo, aggiungi file nuovi.

1. Uno snapshot con entità popolate: T5 **non** è `Blocked`.
2. Uno snapshot senza entità: T5 è `Blocked`, e l'evidenza nomina un percorso che
   esiste davvero nel documento.
3. Uno snapshot con `inventory` popolato: T8 non stampa più
   `character_inventory_state_not_published`.
4. Un `ClassifiedValue` UNKNOWN con motivo: la cella mostra `UNKNOWN` **e** il
   motivo, e non è vuota.
5. Un valore CACHED e uno LIVE con lo stesso contenuto **non** si disegnano
   uguali. È il test che vale la Parte 3.
6. Nessuna vista stampa una percentuale di confidenza o di rischio per un ciclo
   che non ne porta una. Asserisci sul testo prodotto, non sul metodo privato.
7. Per ogni motivo costante che hai verificato **vero** e lasciato, un test che
   lo fissa — così chi lo cambierà dovrà cambiare anche il test.

## Definition of done

- `dotnet build src/NosAi.ControlPanel` e `dotnet build tests/NosAi.ControlPanel.Tests`: 0/0.
- `dotnet test tests/NosAi.ControlPanel.Tests`: 0 falliti, totale riportato
  (erano **121** il 2026-09-08).
- Nel report: l'elenco dei motivi costanti che hai controllato, con **vero** o
  **falso** accanto a ognuno e la riga che lo dimostra.
- Livello: **Integrated**.

## Fuori scope

- Nessuna modifica a `src/NosAi.Runtime/`. Se un dato ti serve e il runtime non
  lo pubblica, **fermati e riferisci**: aggiungerlo è un'altra decisione.
- Nessun ridisegno dell'interfaccia. Si correggono i fatti mostrati, non il
  layout.
- **Nessun aggiornamento ai documenti** — REGOLA #2.
