# AP-06 — Quest Intelligence — Audit A5 (Claude, indipendente)

**Data:** 2026-09-07. **Ambito:** consegna DeepSeek `CollectCommand.cs`
(commit `3149230`, `origin/main`) contro
`AP-06_A2A4_DEEPSEEK_collect_command.md`. Verifica diretta (build/test
indipendenti in worktree isolato), non un audit in background: ambito
ristretto a un solo comando, stesso metodo di AP-04/A5, AP-05/A5.

## Difetto reale trovato e corretto

**`CollectCommand.Run` lanciava un'eccezione non gestita su `vnum`
presente ma vuoto** (`--collect 10 20 ""`) — identico, per pattern e
causa, al difetto già trovato e corretto in `EngageCommand.Run`
(`AP-05_A5_AUDIT.md`): `ArgumentException.ThrowIfNullOrWhiteSpace(vnum)`
girava prima di ogni gestione `[REFUSED]`, e `Program.cs` controlla solo
il conteggio degli argomenti (`collectIndex + 3 >= args.Length`), non il
contenuto. Corretto con lo stesso schema: `[REFUSED]
collect_requires_non_blank_vnum_and_positive_rounds` +
`WalkCommand.ExitAbandoned`, per `vnum` vuoto o `rounds < 1`. Due test di
regressione aggiunti (`Run_BlankVnum_IsRefusedCleanly_NeverThrows`,
`Run_ZeroRounds_IsRefusedCleanly_NeverThrows`).

## Verificato senza trovare altri difetti

- `ExecuteOneRound` è puro rispetto a I/O e clock (before/after già
  catturati, walk guidato dal rig iniettato) — stessa disciplina di
  `ScoutCommand`/`EngageCommand`.
- `AssessCollectProgress` è chiamato correttamente su entrambe le
  osservazioni con lo stesso `target`; nessuna fabbricazione di dati.
- `LiveScope.TryOpen` compone una cattura WinDivert reale, non un mock —
  mirror fedele di `Gate1ObservationChannel.FromPackets`, con
  disposizione ordinata (capture → connector → auth) documentata e
  corretta.
- `Program.cs`: parsing `<x> <y>` (interi), `--watch <n>`, conteggio
  argomenti — nessun crash su input malformato oltre al `vnum` vuoto già
  corretto.

## Osservazione architetturale (non un difetto di questa consegna)

`RunWindowsCore` costruisce `new OccupancyView(null, now)` per ogni
round — **stesso pattern già presente, invariato, in
`WalkCommand.RunWindows`/`ScoutCommand.RunWindows`** (righe già in `main`
prima di questa consegna). `OccupancyFreshness.Evaluate` rifiuta sempre
quando `Entities == null` (`NeverObservedReason`), quindi **`--walk`,
`--scout` e ora `--collect` si rifiutano tutti al primo passo contro un
client reale**, per costruzione: nessun feed di occupazione live è mai
cablato in nessuno dei tre comandi. Non è un difetto introdotto da questa
consegna — DeepSeek ha seguito fedelmente un pattern già consolidato — ma
è lo stesso genere di limite già segnalato per `--engage` (nessun ponte
al Safety Gate reale): tutti e tre i comandi restano `Present`/`Integrated`
per test, non funzionanti end-to-end contro un client live finché un feed
di occupazione reale non viene cablato. Segnalato per una fase futura,
non affrontato qui (fuori ambito per questa consegna, condiviso da tre
comandi già integrati).

## Evidenza build/test

```
dotnet build NosAi.sln -c Release → 0 Errori, 0 Warning nuovi.
dotnet test NosAi.Runtime.Tests -c Release --filter "~CollectCommandTests"
  → Passed: 7, Failed: 0, Total: 7 (5 preesistenti + 2 di regressione)
dotnet test NosAi.Runtime.Tests -c Release → 1978/2036, 0 falliti, 58 skip
dotnet test NosAi.Core.Tests -c Release → 579/579 (1 flake isolato
  TransportLoopTests, non collegato, verde alla riesecuzione)
```

## Verdetto

Un difetto reale, stesso pattern di AP-05, corretto. Nessun altro difetto
trovato. `--collect` è pronto per l'integrazione A6.
