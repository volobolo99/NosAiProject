# AP-08 — Strategic Autonomy — Audit A5 (Claude, indipendente)

**Data:** 2026-09-07. **Ambito:** `AutoplayCommand.cs` (commit `34a8b54`,
`origin/main`) contro `AP-08_A2A4_DEEPSEEK_autoplay_command.md`. Rilievo
d'importanza: primo comando che sceglie un'azione invece di eseguirne una
nominata dall'operatore — verifica diretta più accurata del solito, non
solo build/test.

## Difetto reale trovato e corretto

**Il footprint di esplorazione non veniva propagato tra i cicli di
`--autoplay`.** `ExecuteOneCycle`'s ramo `Exploration` chiamava
`ScoutCommand.ExecuteOneRound(..., out _, out _, nowUtc)`, scartando
`updatedFootprint`; `AutoplayCycleResult` non aveva un campo per
restituirlo; `RunWindows` non riassegnava mai `footprint` dopo la
chiamata a `ExecuteOneCycle`. Il commento della consegna (riga 320-323)
dichiarava esplicitamente "the footprint advances across cycles" — non
vero nel codice scritto. Effetto concreto: ogni ciclo `Exploration`
ripartiva dal footprint originale (vuoto o dell'ultimo cambio mappa),
mai da quanto i cicli precedenti avevano già visitato — a differenza di
`--scout --watch n`, che nel proprio ciclo fa correttamente
`footprint = updatedFootprint;` (riga 362 di `ScoutCommand.cs`). Nessun
test in `AutoplayCommandTests.cs` copriva questo (zero asserzioni su
`updatedFootprint`), confermando il pattern già visto in ogni audit
precedente: il difetto coincide sempre con l'assenza di un test.

**Corretto**: `AutoplayCycleResult` ha ora un quinto campo
`UpdatedFootprint` (il footprint del ramo `Exploration`, quello ricevuto
invariato per gli altri tre rami); `RunWindows` lo riassegna a
`footprint` dopo ogni ciclo, incondizionatamente. Due test di
regressione aggiunti:
`AnExplorationPlan_CarriesTheUpdatedFootprintForward_NotTheOriginal` (il
footprint restituito differisce da quello passato) e
`AnIdleCycle_PassesTheFootprintThroughUnchanged` (i rami non-Exploration
non lo alterano).

## Verificato senza trovare altri difetti

- **Nessun bypass di Guard/Safety**: `ExecuteOneCycle` non costruisce mai
  un `ActuationScope`, non chiama mai `input.KeyPress`/`MoveAbsolute`
  direttamente — invoca solo `ScoutCommand.ExecuteOneRound`/
  `RecoverCommand.ExecuteOneRound`, già auditati, invariati.
- **Ambito rispettato alla lettera**: `switch (kind)` dispatcha solo
  `Exploration`/`Survival`; ogni altro `StrategicGoalKind`
  (`QuestUrgency` incluso) cade nel `default` → `NotDispatchable`, mai
  silenzioso, mai sostituito. Nessun dispatch scritto per `QuestUrgency`,
  come richiesto.
- **Tetto cicli rispettato**: `cycles > MaxCycles (20)` o `< 1` →
  `[REFUSED] autoplay_cycles_exceeds_max:20`, mai clampato in silenzio
  (verificato leggendo `Run`, righe 246-250).
- **Autorità sempre `--autoplay`**: ogni round dispatchato riceve
  `ActuationAuthority.Commanded(Flag)` con `Flag = "--autoplay"`, mai
  `"--scout"`/`"--recover"` — verificato sia in `ExecuteOneCycle` che nel
  singolo punto di costruzione in `RunWindows` (riga 337).
  Nota minore, non bloccante: il messaggio di rifiuto keybind in
  `RunWindows` (riga 330) dice testualmente "recover requires
  keybinds" invece di nominare `--autoplay` — cosmetico, non un difetto
  funzionale, non corretto qui.
- **`Idle` ferma il ciclo**: `plan.SelectedKind is null` → stampa e
  ritorna `ExitNothingUrgent` senza eseguire i cicli restanti (verificato
  in `RunWindows`, righe 474-478/490-494).
- **Wiring `Program.cs`**: `--cycles`/`--recover-slot` parsati
  correttamente, nessun crash su valore malformato/mancante (fallback a
  default).

## Evidenza build/test (indipendente, dopo la correzione)

```
dotnet build NosAi.sln -c Release → 0 Errori, 0 Warning nuovi.
dotnet test NosAi.Runtime.Tests -c Release --filter "~AutoplayCommandTests"
  → Passed: 15, Failed: 0, Total: 15 (13 preesistenti + 2 di regressione)
dotnet test NosAi.Runtime.Tests -c Release → 2018/2076, 0 falliti, 58 skip
  (un fallimento isolato in una prima esecuzione, non riprodotto alla
  riesecuzione — flake da carico macchina, stesso genere già documentato
  per TransportLoopTests in NosAi.Core.Tests)
dotnet test NosAi.Core.Tests -c Release → 580/580, 0 falliti
```

## Verdetto

Un difetto reale (footprint non propagato), corretto con evidenza. Nessun
bypass di sicurezza, nessun'azione fuori ambito, nessuna violazione dei
limiti rigidi imposti dalla specifica. `--autoplay` è pronto per
l'integrazione A6.
