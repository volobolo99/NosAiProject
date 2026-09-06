# AP-08 — Strategic Autonomy — Stato finale

## Ambito

Strategic Utility → HTN → GOAP → reactive rules → Guard/Trust/Safety.
Questa fase copre "Strategic Utility" (A1+A3, `AP-08_A1_STATUS.md`) e la
tappa "reactive rules" (A2+A4): il primo comando che sceglie un'azione
invece di eseguirne una nominata dall'operatore — costruito solo dopo
decisione esplicita dell'utente sull'ambito. HTN/GOAP restano non
affrontati (dipendono da gap di esecuzione ancora aperti).

## A1+A3 — `StrategyContracts`/`StrategyPlanner`

Vedi `AP-08_A1_STATUS.md`: 3 dei 7 `StrategicGoalKind` valutabili oggi
(Survival, QuestUrgency, Exploration) con dati reali; gli altri 4 non
hanno un assessor per mancanza di dati reali.

## A2+A4 — `--autoplay` (DeepSeek)

Indagine (`AP-08_A1_STATUS.md` §"AP-08/A2+A4"): la risposta Survival
reale (`UseConsumable`) era lavoro AP-05 (`--recover`, vedi
`AP-05_STATUS.md` §8bis), non nuova infrastruttura AP-08. Il vero gap
AP-08 era l'assenza di un orchestratore. Specifica:
`AP-08_A2A4_DEEPSEEK_autoplay_command.md`, con limiti rigidi imposti
dall'utente: solo Survival/Exploration dispatchati, tetto di 20 cicli
mai clampato, autorità sempre `--autoplay`, nessun bypass Guard/Safety.

Consegnato (commit `34a8b54`): `AutoplayCommand.cs` —
`--autoplay [--cycles <n>] [--recover-slot <slot>]`. `ExecuteOneCycle`
(puro, testabile) sceglie al più uno tra
`ScoutCommand.ExecuteOneRound`/`RecoverCommand.ExecuteOneRound` da
`StrategyPlanner.SelectStrategicPlan`. 13 test (`AutoplayCommandTests.cs`).

## A5 — Audit indipendente

Report completo: `AP-08_A5_AUDIT.md`. **Un difetto reale trovato**: il
footprint di esplorazione non veniva propagato tra cicli (scartato con
`out _`, mai riassegnato in `RunWindows`) — ogni ciclo `Exploration`
ripartiva da zero invece di ricordare le celle già visitate nei cicli
precedenti della stessa invocazione. Nessun bypass di sicurezza, nessuna
violazione dei limiti imposti dalla specifica (dispatch, tetto cicli,
attribuzione autorità tutti verificati corretti).

## A6 — Integrazione finale

Applicata l'unica correzione richiesta: `AutoplayCycleResult` porta ora
un quinto campo `UpdatedFootprint` (il footprint aggiornato dal ramo
Exploration, invariato per gli altri rami); `RunWindows` lo riassegna a
`footprint` dopo ogni ciclo. 2 test di regressione aggiunti.

**Evidenza:**
```
dotnet build NosAi.sln -c Release → 0 Warning(s), 0 Error(s)
dotnet test NosAi.Runtime.Tests -c Release --filter "~AutoplayCommandTests" → 15/15
dotnet test NosAi.Runtime.Tests -c Release → 2018/2076, 0 falliti, 58 skip
dotnet test NosAi.Core.Tests -c Release → 580/580, 0 falliti
```

## Livello di verifica finale — AP-08

**`Integrated`**: A1+A2+A3+A4 compilano puliti e passano tutti i test
combinati, incluso il difetto trovato dall'audit e corretto qui. **Non
`Verified`**: nessun client reale in questo ambiente; `--autoplay`
eredita gli stessi limiti dei comandi che dispatcha (`--scout`/`--recover`
non funzionanti end-to-end contro il gate di produzione armato per gli
stessi motivi già documentati in `AP-05_STATUS.md`/`AP-06_A5_AUDIT.md`).

## Item aperti

- `QuestUrgency`/`Recovery`/`Progression`/`Farming`/`Optimization`: nessun
  dispatch, per mancanza di dati reali (`AP-08_A1_STATUS.md`) — non un
  difetto, un limite dichiarato.
- HTN/GOAP: non affrontati, dipendono dai gap di esecuzione ancora
  aperti in AP-07.
