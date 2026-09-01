# Certificazione Gate 1 — Spina dorsale fisica

**Roadmap:** `docs/ROADMAP_ESECUTIVA.md` §2 (Gate 1 — Spina dorsale fisica: PC ↔ NosTale ↔ Mobile ↔ Dashboard)
**ADR:** `docs/adr/ADR-0015-adopt-roadmap-esecutiva-as-canonical-architecture.md`
**Data evidenza locale:** 2026-09-01
**Stato:** **NON CHIUSO.** 6 delle 8 voci della Definition of Done (§1.4) sono verdi con evidenza locale; 2 richiedono hardware reale e la firma dell'operatore (`docs/TEST_RIMANDATI.md` T-06, T-07). Per la regola di transizione dei Gate (§1.4, §1.5), Gate 2 non può iniziare — nemmeno in scaffolding — finché queste due righe non sono chiuse con evidenza.

Questo documento registra cosa è stato verificato localmente, con quale comando e quale esito, e non anticipa alcuna voce che non è stata effettivamente eseguita.

## Riepilogo Definition of Done (§1.4)

| # | Voce | Stato | Evidenza |
| --- | --- | --- | --- |
| 1 | `dotnet build -c Release` senza warning (`TreatWarningsAsErrors=true`) | ✅ Verde | §1 sotto |
| 2 | Tutti i test del Gate verdi (`dotnet test --filter "Category=Gate1"`) | ✅ Verde | §2 sotto |
| 3 | Analyzer `NOSAI0001` senza violazioni | ✅ Verde | §3 sotto |
| 4 | Benchmark di allocazione: 0 byte su 10.000 cicli | ✅ Verde | §4 sotto |
| 5 | Latenza p99 dello stage entro il budget dichiarato (25 ms handshake) | ⛔ Aperto — richiede nodo mobile reale | `docs/TEST_RIMANDATI.md` T-06 |
| 6 | Journal SQLite integro: catena hash verificata end-to-end | ✅ Verde | §6 sotto |
| 7 | Test negativi (fail-closed) eseguiti e documentati | ✅ Verde | §7 sotto |
| 8 | Validazione fisica human-in-the-loop firmata dall'operatore | ⛔ Aperto — richiede NosTale, telefono e firma | `docs/TEST_RIMANDATI.md` T-07, §8 sotto |

## 1. Build Release senza warning

Le sei librerie del Gate 1 (`NosAi.Core`, `NosAi.Adapter`, `NosAi.Security`, `NosAi.Storage`, `NosAi.Host`, `NosAi.Analyzers`) sono compilate con `TreatWarningsAsErrors=true` da `Directory.Build.props`.

```
dotnet build tests\NosAi.Core.Tests\NosAi.Core.Tests.csproj -c Release
```

Esito: `Compilazione completata. Avvisi: 0  Errori: 0` per tutte le sei librerie (compilate transitivamente come dipendenze del progetto di test).

Nota: `dotnet build NosAi.sln -c Release` fallisce, ma per una causa estranea al Gate 1: la fase di AOT Android di `NosAi.GuardAi.App` (architettura precedente, non toccata da questo lavoro) restituisce `Invalid argument` sul toolchain Mono AOT in questo ambiente sandbox. Non è un warning né un errore introdotto da questo Gate; è tracciato come rischio ambientale nel report finale, non come voce di DoD del Gate 1.

## 2. Test del Gate verdi

```
dotnet test tests\NosAi.Core.Tests\NosAi.Core.Tests.csproj --filter "Category=Gate1"
```

Esito: **66/66 superati** (0 falliti), sia in configurazione Debug che Release. Copertura: `FrameCodec`, `SequenceGuard`, `CapabilityToken`/`HmacCapabilityValidator`, `NoiseXxSession`, `SqliteEventJournal`, `VolumeLocator`, `Win32ProcessAdapter`, `NosAiHost`, `NoMockOnCriticalPathAnalyzer`, `TransportLoopTests` (TCP loopback reale: handshake Noise, CapBAC, heartbeat, replay, disconnessione). Nessun mock: i test contro SQLite scrivono su file reali in `%TEMP%`, `Win32ProcessAdapter` usa le API Win32 reali (senza mai collegarsi a un processo di sistema in esecuzione), `NoiseXxSession` esegue l'handshake `Noise.NET` reale, `TransportLoopTests` apre un `TcpListener` e un `TcpClient` sullo stesso host.

Esecuzione dell'intera suite della repository nello stesso passaggio, per rilevare regressioni fuori dal Gate 1:

```
dotnet test NosAi.sln
```

Esito: **610/610 superati** (27 `NosAi.ControlPanel.Tests` + 66 `NosAi.Core.Tests` + 517 `NosAi.Runtime.Tests`), architettura precedente compresa.

## 3. Analyzer `NOSAI0001` senza violazioni

`tools/NosAi.Analyzers/NoMockOnCriticalPathAnalyzer.cs` implementa INV-04: segnala come **errore** ogni tipo il cui nome contiene `Mock`, `Fake`, `Stub`, `Dummy` o `Synthetic`, sia quando è dichiarato sia quando è solo referenziato, all'interno di `NosAi.Core`, `NosAi.Adapter`, `NosAi.Security`, `NosAi.Storage`, `NosAi.Host` (`NosAi.Perception` non esiste ancora; l'analyzer è già cablato per includerlo quando verrà creato, tramite la stessa condizione in `Directory.Build.props`).

- **Zero violazioni sul codice reale:** la build pulita di §1 lo dimostra — se l'analyzer avesse trovato una corrispondenza in una qualunque delle sei librerie, `TreatWarningsAsErrors=true` avrebbe fatto fallire la build.
- **L'analyzer funziona davvero**, non solo "non trova nulla perché non gira": `tests/NosAi.Core.Tests/NoMockOnCriticalPathAnalyzerTests.cs` lo esegue attraverso la vera pipeline Roslyn (`CompilationWithAnalyzers`) su codice compilato al momento, 9 casi:
  - tipo dichiarato con ciascuna delle 5 parole vietate → segnalato (5 casi, `[Theory]`);
  - tipo definito in un **altro** assembly e solo referenziato (`new NetworkStub()`) → segnalato lo stesso, non solo le dichiarazioni locali;
  - variabile locale chiamata `mockData` → **non** segnalata (l'analyzer guarda i tipi, non ogni identificatore);
  - stringa letterale contenente "stub" → **non** segnalata;
  - codice di produzione ordinario → nessuna diagnostica.

```
dotnet test tests\NosAi.Core.Tests\NosAi.Core.Tests.csproj --filter "FullyQualifiedName~NoMockOnCriticalPathAnalyzerTests"
```

Esito: **9/9 superati**.

## 4. Zero allocazioni su 10.000 cicli

`FrameCodecTests.EncodingTenThousandFramesWithAWarmedUpCalculatorDoesNotGrowManagedHeapUsage` misura il delta di `GC.GetAllocatedBytesForCurrentThread()` prima/dopo 10.000 cicli di `FrameCodec.Encode` con un `FrameTagCalculator` già riscaldato — esattamente la soglia e il metodo indicati in §2.5 della roadmap ("Allocazioni codec su 10.000 frame: 0 byte, `GC.GetAllocatedBytesForCurrentThread()` delta").

Esito: **0 byte allocati**, verificato dal test (superato in entrambi gli esiti di §2). Non esiste ancora un progetto `bench/NosAi.Bench` con BenchmarkDotNet: la roadmap lo richiede esplicitamente solo per i benchmark dei Gate successivi (`*Perception*`, `*Ranking*`); per il Gate 1 il criterio di accettazione in §2.5 è soddisfatto dal test in-process descritto sopra, con lo stesso metodo di misura.

## 5. Latenza p99 dell'handshake — APERTO (loopback verde, telefono no)

Soglia dichiarata: handshake Noise completato su nodo mobile reale, p99 < 25 ms su 100 tentativi.

- **Loopback TCP reale** (`TransportLoopTests.OneHundredLoopbackHandshakesStayUnderTheTwentyFiveMillisecondBudget`): 100 handshake `Noise_XX_25519_ChaChaPoly_SHA256` su `127.0.0.1`, p99 < 25 ms. Non chiude T-06.
- **Telefono reale:** ancora aperto. L'host ora ascolta (`NosAi.Host --gate 1 --attach <process> --module-sha256 <hex> --listen [--bind 0.0.0.0]`). L'app Guard attuale parla `WireHeader` (`NosAi.Protocol`), non `NosFrameHeader` + Noise XX: T-06 richiede un iniziatore sul telefono per il protocollo nuovo, non l'APK preesistente. Tracciato come `docs/TEST_RIMANDATI.md` T-06.

## 6. Integrità della catena hash del journal

```
dotnet test tests\NosAi.Core.Tests\NosAi.Core.Tests.csproj --filter "FullyQualifiedName~SqliteEventJournalTests"
```

- `VerifyChainIsValidAcrossTenThousandRecords`: 10.000 record scritti su un database SQLite reale in `%TEMP%`, `VerifyChain` conferma la catena intatta end-to-end — la soglia esatta di §2.5 ("valida su 10.000 record"), non un campione più piccolo.
- `VerifyChainDetectsATamperedRecordAtTheCorrectSequence`: un payload alterato direttamente via `UPDATE` SQL viene rilevato da `VerifyChain`, che riporta la sequenza esatta della manomissione.
- `ReopeningTheSameDatabaseResumesTheSequenceAndPreservesTheChain`: chiudere e riaprire lo stesso file `.db` continua la sequenza e la catena senza spezzarla.
- `JournalAppliesAndVerifiesTheWalFullSynchronousBusyTimeoutPolicy`: `journal_mode=WAL` è confermato leggibile da una connessione indipendente (l'unica delle tre pragma persistita nel file SQLite); `synchronous=FULL` e `busy_timeout=5000` sono verificati e forzati a fallire in modo esplicito (`ApplyPolicyOrThrow`) sulla connessione del journal stesso al momento dell'apertura, perché sono impostazioni per-connessione che SQLite non persiste nel file.

Esito: **9/9 superati** (incluso l'append/replay di base e la ripetizione dei test precedenti a scala minore per isolare eventuali problemi specifici del volume di dati).

## 7. Test negativi (fail-closed)

Elenco dei test negativi eseguiti, con il file che li contiene:

- **Frame corrotto** (`FrameCodecTests`): un singolo bit alterato nel payload è scartato senza eccezioni; tag HMAC manomesso rifiutato; frame codificato con una chiave di sessione diversa non decodifica; frame troncato rifiutato; versione di protocollo sbagliata rifiutata; lunghezza dichiarata oltre il buffer o oltre `MaxPayloadLength` rifiutata prima di qualunque allocazione.
- **Replay** (`SequenceGuardTests`): replay esatto dell'high-water-mark rifiutato; replay di una sequenza più vecchia rifiutata; sequenza troppo vecchia rispetto alla finestra rifiutata; dimensioni di finestra non valide rifiutate dal costruttore.
- **CapBAC** (`CapabilityValidatorTests`): token scaduto o non ancora valido oltre la tolleranza di clock-skew rifiutato con `FaultCode.Timeout`; MAC manomesso rifiutato; token firmato con una root key diversa rifiutato; richiesta di scope oltre quanto concesso dal token negata.
- **Noise** (`NoiseSessionTests`): un ciphertext di trasporto manomesso fa fallire la decodifica e porta la sessione in `Failed`, stato terminale — nessun tentativo automatico di ripresa; `Rekey()` prima del completamento dell'handshake lancia.
- **Adapter** (`Win32ProcessAdapterTests`): tentativo di attach a un processo inesistente fallisce in modo chiuso (nessuna eccezione, nessun valore plausibile inventato); lettura di memoria prima dell'attach lancia invece di restituire dati; uso dopo `Dispose` lancia.
- **Host** (`NosAiHostTests`): un bootstrap senza client NosTale attaccato registra `FaultCode.AttachFailed` sia nel journal sia nella telemetria pubblicata, non un successo silenzioso.
- **Trasporto TCP** (`TransportLoopTests`): replay della stessa `Sequence` applicativa rifiutato e journalato; token CapBAC scaduto negato e sessione chiusa; chiusura TCP improvvisa journalata come `FaultCode.Network` con catena hash ancora integra.

Tutti i casi sopra sono nella suite dei 66 test di §2; nessuno usa un mock, uno stub o un doppio di test — sono tutti condotti contro le implementazioni reali (SQLite su file reale, `Noise.NET` reale, API Win32 reali, HMAC-SHA256 reale, TCP loopback reale).

## 8. Validazione fisica human-in-the-loop — APERTO

L'host è ora in grado di servire la sessione: `dotnet run --project src/NosAi.Host -- --gate 1 --attach NostaleClientX --module-sha256 <hex> --verify-chain --listen`. La console stampa `status=transport`, il conteggio `frames=` e `status=disconnected` alla caduta del peer; il journal SQLite registra handshake, frame e disconnect. Manca ancora: processo target realmente in esecuzione, un iniziatore Noise XX sul telefono (l'APK Guard attuale non parla questo protocollo), e la firma sotto. Tracciato come `docs/TEST_RIMANDATI.md` T-07.

**Firma dell'operatore:** _(da compilare dopo l'esecuzione fisica — non compilare in anticipo)_

- Data: ______________
- Operatore: ______________
- Esito: ______________

## Promemoria `docs/TEST_RIMANDATI.md` — righe aperte rilevanti per questo Gate

- **T-06** — Handshake Noise su nodo mobile reale, p99 < 25 ms su 100 tentativi.
- **T-07** — Validazione fisica human-in-the-loop del Gate 1 (dashboard, stato Transport sul telefono, disconnessione di rete).

Le righe T-01..T-05 (architettura precedente) restano aperte e non sono toccate da questo Gate.
