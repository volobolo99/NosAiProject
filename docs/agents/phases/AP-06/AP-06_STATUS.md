# AP-06 — Quest Intelligence — Stato finale

## Ambito

Grafo quest tipizzato + esecuzione/verifica dell'unico obiettivo con un
canale dati reale indipendente dal gap OCR/ML (`Collect`). Vedi
`AP-06_A1_STATUS.md` (A1+A3: `QuestGraphContracts`/`QuestGraphPlanner`) e
la sua sezione "AP-06/A2+A4" (indagine, decisione, `AssessCollectProgress`).

## A2+A4 — `CollectCommand` (DeepSeek)

Consegnato (commit `3149230`): comando operatore
`--collect <x> <y> <vnum> [<requiredCount>] [--watch <n>]` — cammina via
`WalkCommand.Execute` (riusato), verifica via cattura di rete live reale
(`LiveScope`, mirror di `Gate1ObservationChannel.FromPackets`) +
`GameplayObservationProjector` + `AssessCollectProgress` prima/dopo. 7
test (`CollectCommandTests.cs`).

## A5 — Audit indipendente

Report completo: `AP-06_A5_AUDIT.md`. Un difetto reale trovato e
corretto in A6 (stesso pattern del difetto AP-05: `Run` non gestiva un
argomento vuoto). Osservazione architetturale non bloccante: come
`--walk`/`--scout`, `--collect` costruisce un `OccupancyView` sempre
`null` — si rifiuterebbe al primo passo contro un client reale finché un
feed di occupazione live non esiste, limite condiviso e preesistente, non
introdotto da questa consegna.

## A6 — Integrazione finale

Applicata l'unica correzione richiesta: `CollectCommand.Run` ora rifiuta
`[REFUSED] collect_requires_non_blank_vnum_and_positive_rounds` invece di
lanciare, su `vnum` vuoto o `rounds < 1`. Due test di regressione
aggiunti.

**Evidenza:**
```
dotnet build NosAi.sln -c Release → 0 Warning(s), 0 Error(s)
dotnet test NosAi.Runtime.Tests -c Release --filter "~CollectCommandTests" → 7/7
dotnet test NosAi.Runtime.Tests -c Release → 1978/2036, 0 falliti, 58 skip
dotnet test NosAi.Core.Tests -c Release → 579/579, 0 falliti
```

## Livello di verifica finale — AP-06

**`Integrated`**: A1+A2+A3+A4 compilano puliti e passano tutti i test
combinati, incluso il difetto trovato dall'audit e corretto qui. **Non
`Verified`**: nessun client reale in questo ambiente; `--collect` si
rifiuterebbe comunque al primo passo per il limite di occupazione
condiviso (vedi A5).

## Item aperti

- `Kill`/`Dialogue`/`Interact`/`Deliver`: bloccati, vedi `AP-06_A1_STATUS.md`.
- Feed di occupazione live per `--walk`/`--scout`/`--collect`: nessuno
  esiste ancora — segnalato da A5, non affrontato qui (condiviso da tre
  comandi già integrati, richiede una decisione di design propria).
