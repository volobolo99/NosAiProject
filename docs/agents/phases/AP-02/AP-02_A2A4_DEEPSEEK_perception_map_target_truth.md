# AP-02 / A2+A4 — DeepSeek — Percezione, Mappa e Bersaglio: cinque fatti storti

**Parallelo a Q-140 e a Q-143**: nessun file in comune con nessuno dei due.

## Cosa possiedi

- `src/NosAi.ControlPanel/PerceptionProbe.cs`
- `src/NosAi.ControlPanel/MapInspect.cs`
- `src/NosAi.ControlPanel/TargetInspect.cs`
- `src/NosAi.ControlPanel/AttachedSnapshot.cs`
- `src/NosAi.Runtime/Perception/HudCropWriter.cs` — **solo** se la Parte 1 lo
  richiede davvero, e riferendolo
- file di test nuovi in `tests/NosAi.ControlPanel.Tests/`

**Non toccare**: `MainWindow.xaml(.cs)`, `SurroundingsInspect.cs`,
`ScreenCalibrationInspect.cs`, `CatalogueNames.cs`, `WireMessageInspect.cs`
(appena corretti da Claude); `PracticalTestCenterWindow.xaml.cs`,
`CognitiveRuntimeTraceBridge.cs`, `CognitiveMemoryWindow.xaml.cs`,
`GameplayWireReader.cs`, `CombatInspect.cs` (sono di Q-143); niente sotto
`src/NosAi.Runtime/Perception/Network/`, `LiveIntegration/`, `Observability/`,
né `Program.cs` (sono di Q-140).

**Compila solo i tuoi progetti**: `dotnet build src/NosAi.ControlPanel` e
`dotnet build tests/NosAi.ControlPanel.Tests`, mai `dotnet build NosAi.sln`. Altri
agenti compilano in parallelo: `error MSB3027 … il file è bloccato` **è contesa,
non una tua regressione** — aspetta e riprova.

**Cartella**: `C:\Users\volob\Desktop\NosAiProject`. `git pull`, **`git push` tu**.

**Se la specifica e il codice non concordano, ha ragione il codice**: fermati e
riferisci.

---

## Perché

Regola permanente dell'utente: **il pannello è la superficie con cui si eseguono
i test sul client reale, e deve rispecchiare dati reali.** Un audit del
2026-09-08 ha trovato quindici punti in cui non lo fa. Claude ne ha corretti
cinque (`a976c66`), quattro sono in Q-143, questi cinque restano.

L'invariante violata è in `CLAUDE.md`: **«Unknown is not zero, false or empty»** e
«Real, derived, cached and simulated data remain explicitly distinguishable».

---

## Parte 1 — «Ritagli HUD» è marcato LIVE anche quando non è stato scritto niente

`PerceptionProbe.cs:314`:

```csharp
new DisplayField("Ritagli HUD", cropDir is null ? "UNKNOWN · crop_not_saved" : cropDir,
                 cropDir is null ? "UNKNOWN" : "LIVE")
```

`HudCropWriter.TrySave` (`src/NosAi.Runtime/Perception/HudCropWriter.cs:57-63`)
restituisce la directory **incondizionatamente**, e `WriteBmp` (`:104-106`) esce
**senza scrivere** quando il ritaglio è vuoto — cioè ogni volta che la ROI cade
fuori dal fotogramma. Quindi `cropDir` non nullo non significa «ho salvato»:
significa «avevo una cartella dove salvare». L'unico caso in cui esce UNKNOWN è
la radice del repository nulla o un fotogramma senza pixel.

Su disco oggi convivono `hp_latest.bmp` e `mp_latest.bmp` del 2026-09-07 22:01 e
`inventory_panel_latest.bmp` del 2026-09-07 15:32: **bitmap di due sessioni
diverse, sotto un'unica etichetta LIVE e senza data per riga.**

**Cosa fare**: far sapere a chi chiama **quali** ritagli sono stati davvero
scritti in questa passata, e mostrare per ognuno l'istante del file. Un ritaglio
di sette ore fa non è LIVE. Se questo richiede di cambiare il valore di ritorno di
`HudCropWriter.TrySave`, fallo — è nella tua ownership — e **riferiscilo nel
report**, perché è l'unico file fuori dal pannello che tocchi.

## Parte 2 — un motivo costante che è falso in quel ramo

`PerceptionProbe.cs:336-337`, nel ramo «nessun fotogramma acquisito»:

```csharp
new DisplayField("HP attuale", "UNKNOWN · ocr_glyphs_not_trained", "UNKNOWN"),
new DisplayField("HP massimo", "UNKNOWN · ocr_glyphs_not_trained", "UNKNOWN"),
```

In quel ramo **nessun fotogramma è stato acquisito** (`:103-113`) e l'OCR non è
mai partito: la riga sopra lo dice giusto (`no_frame_within_budget`). La stringa è
costante, quindi anche con l'atlante pienamente addestrato il pannello direbbe
«glifi non addestrati» — mandando a indagare la cosa sbagliata.

**Cosa fare**: il motivo dev'essere quello vero del ramo in cui si trova.
Controlla **anche gli altri motivi costanti** dello stesso file e correggi quelli
falsi; quelli veri lasciali e **elencali nel report** con la riga che lo dimostra.

## Parte 3 — «Identità verificata» non può mai essere sì

`MapInspect.cs:238` passa sempre `currentIdentity: null`, quindi `MayLoad`
fallisce chiuso con `map_grids_current_identity_unknown`
(`src/NosAi.Runtime/Navigation/MapGridSetIdentity.cs:195-199`).

Il risultato: **«griglie identiche al manifest» e «impronta del client ignota»
producono la stessa identica stringa**, mentre il ritaglio 31×31 che la vista
disegna viene proprio da quelle griglie.

Metà dell'identità è calcolabile **dal solo disco**: 777 file `.grid` sotto
`D:\NosAi\data\maps` più `maps.manifest` con la sua impronta. La documentazione
del tipo dice che modificare o troncare un `.grid` invalida quanto una patch del
client — quindi quella metà è il controllo che conta di più per il rischio reale.

**Cosa fare**: calcolare la parte calcolabile e mostrarla, **tenendo separate le
due metà**. Se l'impronta del client resta ignota, il verdetto complessivo resta
prudente — ma «le griglie sono intatte» è un fatto vero che oggi non viene detto.

**Cosa non fare**: non dichiarare l'identità verificata quando metà è ignota, e
non toccare `MapGridSetIdentity` (è fuori dalla tua ownership). Se il tipo non
espone ciò che ti serve, **fermati e riferisci**.

## Parte 4 — l'istante di osservazione viene rifabbricato con l'orologio del pannello

`AttachedSnapshot.cs:270-273`:

```csharp
DataSourceKind.Cached => ClassifiedValue<T>.Cached(value, DateTime.UtcNow),
```

`observedAtUtc` è nel JSON **accanto al valore** e viene scartato: al suo posto
si mette l'ora in cui il pannello ha letto lo snapshot. Vale per
`ClientProcessId`, `ObservationLastHp`, `ObservationLastMaxHp`.

Oggi nessuna vista stampa quell'età, quindi l'effetto è latente — **ma
`ObservationLastHp` è l'etichetta con cui `PerceptionProbe.TrainFromWire`
addestra i glifi dell'HUD**, e quel percorso non ha modo di sapere se il numero
che sta usando come verità è di un secondo o di dieci minuti fa.

**Cosa fare**: leggere `observedAtUtc` dal JSON e passarlo. Quando manca, la
risposta onesta non è «adesso»: `Live` e `Derived` hanno costruttori che l'istante
lo prendono — guarda `DataClassification.cs` prima di decidere la forma.

## Parte 5 — due fatti veri, letti e non mostrati

- `TargetInspect.cs:249-252`: le quattro frazioni della ROI bersaglio sono
  parsate (`TargetRoiCalibration.cs:196-199`; nel file reale
  `0.3789 0.5052 0.2246 0.2995`) e **mai stampate**. La riga dice solo quando e
  su che risoluzione. Le frazioni sono ciò che il tipo stesso dichiara essere
  l'intera questione.
- `TargetInspect.cs:317-318`: `process=2484` viene letto da
  `data/target_candidates.txt` e passato a `:325`, ma **non compare in nessun
  campo** — mentre l'identità del processo è ciò che decide se gli indirizzi nudi
  valgono ancora qualcosa (`TargetIdFinder.cs:479, 499-500`).

**Cosa fare**: mostrarli. Sono già in mano, e nessuno dei due richiede una fonte
nuova.

---

## Test (file nuovi in `tests/NosAi.ControlPanel.Tests/`)

1. Ritaglio non scritto: la riga **non** è LIVE. Ritaglio scritto: porta il suo
   istante.
2. Ramo «nessun fotogramma»: il motivo nomina il fotogramma, non i glifi.
3. Ogni motivo costante che hai lasciato perché vero: un test che lo fissa.
4. Griglie intatte con impronta del client ignota, e griglie **non** intatte:
   le due situazioni non producono la stessa stringa. È il test che vale la
   Parte 3.
5. Uno snapshot con `observedAtUtc` esplicito: il valore classificato porta
   **quell'**istante, non l'ora corrente. Usa un istante lontano dal presente,
   così un ritorno a `UtcNow` fallisce visibilmente.
6. Le quattro frazioni della ROI e l'id di processo compaiono nel testo mostrato.
7. Nessun test nuovo può asserire su un metodo privato: asserisci sul testo che
   la vista produce.

**Un `Assert.All` o un `foreach` su una raccolta che potrebbe essere vuota non
asserisce niente**: asserisci prima che non lo sia. Sei asserzioni di questo tipo
sono state trovate e corrette nel repository il 2026-09-08.

## Definition of done

- `dotnet build src/NosAi.ControlPanel` e `dotnet build tests/NosAi.ControlPanel.Tests`: 0/0.
- `dotnet test tests/NosAi.ControlPanel.Tests`: 0 falliti, totale riportato
  (erano **121** il 2026-09-08, prima di Q-143).
- Nel report: l'elenco dei motivi costanti controllati, con **vero** o **falso**
  accanto a ognuno e la riga che lo dimostra; e se hai toccato `HudCropWriter`,
  cosa hai cambiato e perché non si poteva evitare.
- Livello: **Integrated**.

## Fuori scope

- Nessun ridisegno dell'interfaccia: si correggono i fatti mostrati, non il
  layout.
- Nessuna nuova fonte di dati. Se un fatto ti serve e nessuno lo pubblica,
  **fermati e riferisci**.
- **Nessun aggiornamento ai documenti** — REGOLA #2.
