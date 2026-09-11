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

Simbolo canonico gia' individuato in una sessione precedente e narrato per esteso nel
campo `metrics` del contratto; qui solo la forma strutturata.

| CID | Firma canonica | Sorgente | Prova da eseguire |
|---|---|---|---|
| C-201 | `BoundedEventBus(int capacity = 5000); TryPublish(RuntimeEvent); Subscribe(Action<RuntimeEvent>); DisposeAsync(); private DispatchLoopAsync()` | `src/NosAi.Runtime/Gate2/Gate2Runtime.cs` | `tests/NosAi.Runtime.Tests/Gate2Tests.cs` |
| C-202 | `EntityId(string Value); private static WorldModelTemporalEnricher.EnrichMobs(EquatableArray<Mob>, EquatableArray<Mob>, DateTime, TimeSpan, TimeSpan)` | `src/NosAi.Core/WorldModel/Identifiers.cs`, `src/NosAi.Core/WorldModel/Temporal/WorldModelTemporalEnricher.cs` | `tests/NosAi.Core.Tests/WorldModel/IdentifiersTests.cs`, `tests/NosAi.Core.Tests/WorldModel/Temporal/WorldModelTemporalEnricherDeterminismAndDuplicateIdTests.cs` |
| C-301 | `SequenceRoutine(IReadOnlyList<IRoutineNode>); SelectorRoutine(IReadOnlyList<IRoutineNode>); Evaluate(in PlannerWorldState); PlannerGoalStack(int capacity = 8); TryPush(PlannerGoalId, GoalClass, long); TryPop()` | `src/NosAi.Core/Planning/DeterministicRoutine.cs`, `src/NosAi.Core/Planning/PlannerGoalStack.cs` | `tests/NosAi.Core.Tests/Planning/` (nessun test nomina i simboli per nome) |
| C-302 | `DeterministicGoapPlanner(IReadOnlyList<GoapAction>, int maxNodes = 256); TryPlan(ReadOnlySpan<GoapFact>, ReadOnlySpan<GoapFact>, Span<PlanStep>, out int, out FaultCode)` | `src/NosAi.Core/Planning/Goap/DeterministicGoapPlanner.cs` | `tests/NosAi.Core.Tests/Planning/DeterministicGoapPlannerTests.cs` |
| C-303 | `NosAiOrchestrator.__init__(self, provider, safety_gate=None, action_manager=None, action_ranker=None); tick(self, world_state, goal, world_model=None) -> TickResult` | `nosai/core/orchestrator.py` | `tests/test_orchestrator.py` |
| C-305 | `enum RecoveryStrategy; enum RecoveryState; RecoveryController(TrustBoundary, int maxRetries = 2, TimeProvider? = null, int windowSize = DefaultWindowSize, int probeSuccessesToClose = DefaultProbeSuccessesToClose, TimeSpan? = null, TimeSpan? = null)` | `src/NosAi.Runtime/Safety/RecoveryController.cs` | `tests/NosAi.Runtime.Tests/RecoveryCircuitBreakerTests.cs` |
| C-403 | `HmacCapabilityValidator(ReadOnlySpan<byte>); Validate(in CapabilityToken, PipelineStage, uint, long); FrameTagCalculator(ReadOnlySpan<byte>); FrameCodec.Encode(...); FrameCodec.TryDecode(...); SlidingWindowSequenceGuard(int windowBits = 1024); TryAccept(uint); NoiseXxSession(bool, byte[]); WriteMessage(...); ReadMessage(...); enum FrameOpCode` | `src/NosAi.Security/CapabilityValidator.cs`, `FrameCodec.cs`, `SequenceGuard.cs`, `NoiseSession.cs`, `FrameOpCode.cs` | `tests/NosAi.Core.Tests/CapabilityValidatorTests.cs`, `tests/NosAi.Core.Tests/FrameCodecTests.cs` (SequenceGuard e NoiseSession senza test dedicato, solo esercizio indiretto via NosAiHost) |

## Firme ancora da precisare

| CID | Primo percorso di ricerca | Consegna richiesta |
|---|---|---|
| C-105 | da definire | Decisione sul perimetro di hook memoria/DLL, contratto e test; nessuna implementazione implicita |
| C-204 | da definire | ADR sulla proprietà dello stato C# o Python, schema di sincronizzazione e test round-trip |
| C-304 | da definire | Decisione se FSM sostituisce o affianca Planner/Orchestrator, ADR e test |
| C-402 | da definire | Corpus, harness di fuzzing, limiti e report riproducibile |

C-003/C-004/C-005 hanno firme proposte ma non equivalgono a implementazioni verificate.
C-404 è una procedura di rilascio con firma n/a, non una funzione mancante.

## Criterio di chiusura

Una voce passa a `RESOLVED` quando la firma è confrontata con sorgente e consumatori
e il contratto è versionato. L'esecuzione dei test e la prova firmata restano prerequisiti
separati per passare a `VERIFIED`; nessuna promozione automatica da `MERGED` a `VERIFIED`.

Usare `docs/FUNCTION_INDEX.md` per localizzare candidati e
`docs/CONTRACT_MAP.md` per lo stato sintetico.