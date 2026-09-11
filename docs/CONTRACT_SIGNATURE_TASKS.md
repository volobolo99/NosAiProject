# Risoluzione delle firme contrattuali

La documentazione indica quali contratti sono pronti per l'implementazione e quali
richiedono ancora una decisione. Non sostituire una firma non definita con un metodo
scelto arbitrariamente.

## Firme risolte il 2026-09-10

| CID | Firma canonica | Sorgente | Prova da eseguire |
|---|---|---|---|
| C-103 | `GameTrafficCaptureEngine(IPacketSource, Func<StreamDirection, IGameStreamFramer>? = null); Run(CancellationToken = default, TimeSpan? = null); Pump(CapturedPacket); Snapshot()` | `src/NosAi.Runtime/LiveIntegration/Capture/GameTrafficCaptureEngine.cs` | `tests/NosAi.Runtime.Tests/CaptureEngineTests.cs` |
| C-104 | `bool WireHeader.TryRead(ReadOnlySpan<byte>, out WireHeader, out string?)` | `src/NosAi.Protocol/WireProtocol.cs` | `tests/NosAi.Runtime.Tests/HeartbeatPayloadTests.cs`, `tests/NosAi.Runtime.Tests/DiscoveryTests.cs` |
| C-106 | `long CaptureFile.Record(IPacketSource, string, CancellationToken = default, TimeSpan? = null); CaptureFileSource Open(string)` | `src/NosAi.Runtime/LiveIntegration/Capture/CaptureFile.cs` | `tests/NosAi.Runtime.Tests/DecideReplayAsOfCaptureTests.cs` |
| C-203 | `WorldModelSnapshot RunOnce(Gate1CanonicalSnapshot, DateTime)` | `src/NosAi.Runtime/WorldModel/Fusion/WorldModelFusionLoop.cs` | `tests/NosAi.Runtime.Tests/WorldModel/Fusion/WorldModelFusionLoopTests.cs` |
| C-401 | `SessionCipher.ForRuntime(ReadOnlySpan<byte>); ForPhone(ReadOnlySpan<byte>); SealFrameInto(Span<byte>, WireMessageType, uint, ReadOnlySpan<byte>); TryOpenFrame(ReadOnlySpan<byte>, ReadOnlySpan<byte>, out byte[], out string?)` | `src/NosAi.Protocol/SessionCipher.cs` | `tests/NosAi.Runtime.Tests/SessionCipherTests.cs`, `tests/test_session_cipher.py` |

Queste firme sono state estratte dal sorgente canonico e dai consumatori presenti.
Lo stato del ledger resta storico finché le prove indicate non vengono eseguite in un
ambiente disponibile; la risoluzione della firma non certifica il comportamento.

## Firme risolte il 2026-09-11

| CID | Firma canonica | Sorgente | Prova da eseguire |
|---|---|---|---|
| C-204 | Canale 2: `RoleBindingConfiguration.Load(string path); RoleBinding? TryGetBinding(string employeeId)`. Canale 1: `DecisionTelemetryWriter.Append(Gate3LoopCycle cycle)`, agganciato in Gate3DecisionLoop tramite `IDecisionTelemetrySink? telemetry = null` | `src/NosAi.Runtime/Configuration/RoleBindingConfiguration.cs`, `src/NosAi.Runtime/Observability/DecisionTelemetryWriter.cs` | `tests/NosAi.Runtime.Tests/RoleBindingConfigurationTests.cs`, `tests/NosAi.Runtime.Tests/DecisionTelemetryWriterTests.cs`, `tests/NosAi.Runtime.Tests/Gate3DecisionLoopTests.cs` |

Queste firme sono state estratte dal sorgente canonico e dai consumatori presenti.
Lo stato del ledger resta storico finché le prove indicate non vengono eseguite in un
ambiente disponibile; la risoluzione della firma non certifica il comportamento.

## Firme ancora da precisare

| CID | Primo percorso di ricerca | Consegna richiesta |
|---|---|---|
| C-105 | da definire | Decisione sul perimetro di hook memoria/DLL, contratto e test; nessuna implementazione implicita |
| C-201 | da confermare fra i 16 sorgenti che citano dispatch | Simbolo canonico, firma completa, tipi/unità/errori, test e revisione del contratto |
| C-202 | src/NosAi.Core/WorldModel/ | Perimetro pubblico per correlazione identificativi, firma, test e revisione |
| C-301 | da confermare fra i 4 sorgenti che citano HTN | Simbolo canonico, firma completa e test |
| C-302 | da confermare fra i 5 sorgenti che citano GOAP | Simbolo canonico, firma completa e test |
| C-303 | da confermare fra i 30 sorgenti che citano Orchestrator | Unica implementazione canonica, firma e test |
| C-304 | da definire | Decisione se FSM sostituisce o affianca Planner/Orchestrator, ADR e test |
| C-305 | da confermare fra i 44 sorgenti che citano recovery o reconnect | Simbolo canonico, firma completa e test |
| C-402 | da definire | Corpus, harness di fuzzing, limiti e report riproducibile |
| C-403 | src/NosAi.Security/ | Perimetro pubblico della superficie di sicurezza, firma e test |

C-003/C-004/C-005 hanno firme proposte ma non equivalgono a implementazioni verificate.
C-404 è una procedura di rilascio con firma n/a, non una funzione mancante.

## Criterio di chiusura

Una voce passa a `RESOLVED` quando la firma è confrontata con sorgente e consumatori
e il contratto è versionato. L'esecuzione dei test e la prova firmata restano prerequisiti
separati per passare a `VERIFIED`; nessuna promozione automatica da `MERGED` a `VERIFIED`.

Usare `docs/FUNCTION_INDEX.md` per localizzare candidati e
`docs/CONTRACT_MAP.md` per lo stato sintetico.