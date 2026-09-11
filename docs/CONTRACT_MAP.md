# NosAiProject — Mappa dei contratti

Fonte: contracts/ledger.json. I gruppi legacy non sono numeri AP della roadmap.
Allineata al ledger il 2026-09-11. Stati storici conservati: non sono stati rivalidati eseguendo il runtime. DROPPED indica una decisione di non implementare, non una funzionalita completata.
Per firme mancanti consultare [CONTRACT_SIGNATURE_TASKS.md](CONTRACT_SIGNATURE_TASKS.md).

| CID | Fase prodotto | Contratto | Stato ledger | Firma | File | Test |
|---|---|---|---|---|---|---|
| C-001 | AP-00 | Build e suite .NET riproducibili | MERGED | dotnet build NosAi.sln -c Release | scripts/build.ps1 | scripts/test.ps1 |
| C-002 | AP-00 | Suite Python e CI | MERGED | python -m pytest tests/ | pyproject.toml | tests/ |
| C-003 | AP-00 | Harness AddressSanitizer | DROPPED | def run_asan_pipeline(target: str, timeout_s: int = 300) -> AsanReport | scripts/run_asan_pipeline.py | tests/test_asan_pipeline.py |
| C-004 | AP-00 | Gatekeeper dei test Python | DROPPED | def gatekeeper(paths: list[str]) -> GateResult | scripts/gatekeeper.py | tests/test_gatekeeper.py |
| C-005 | AP-00 | Bridge ctypes verso il modulo nativo | DROPPED | def load_native(path: str \| None = None) -> NativeHandle | nosai/bridge/native.py | tests/test_native_bridge.py |
| C-006 | AP-00 | Instradamento dei modelli: DeepSeek nativo, mai da OpenRouter | TEST_VERIFIED | def call_deepseek(model_id: str, prompt: str, system_prompt: str, temperature: float = 0.1, max_tokens: int = 4096) -> str | scripts/mcp_server.py | tests/test_model_routing.py |
| C-101 | AP-02 | Sorgente di pacchetti astratta | MERGED | bool TryRead(TimeSpan timeout, out CapturedPacket packet) | src/NosAi.Runtime/LiveIntegration/Capture/IPacketSource.cs | tests/NosAi.Runtime.Tests/LiveIntegration/Capture |
| C-102 | AP-02 | Cattura live via WinDivert | MERGED | static WinDivertPacketSource? TryOpen(IPAddress serverAddress, int serverPort, out string? failureReason) | src/NosAi.Runtime/LiveIntegration/Capture/WinDivertPacketSource.cs | tests/NosAi.Runtime.Tests/CaptureEngineTests.cs |
| C-103 | AP-02 | Motore di cattura del traffico di gioco | MERGED | GameTrafficCaptureEngine(IPacketSource source, Func<StreamDirection, IGameStreamFramer>? framerFactory = null); CaptureSummary Run(CancellationToken cancellationToken = default, TimeSpan? readTimeout = null); void Pump(CapturedPacket packet); CaptureSummary Snapshot() | src/NosAi.Runtime/LiveIntegration/Capture/GameTrafficCaptureEngine.cs | tests/NosAi.Runtime.Tests/CaptureEngineTests.cs |
| C-104 | AP-02 | Parsing e registro degli opcode | MERGED | bool WireHeader.TryRead(ReadOnlySpan<byte> source, out WireHeader header, out string? error) | src/NosAi.Protocol/WireProtocol.cs | tests/NosAi.Runtime.Tests/HeartbeatPayloadTests.cs; tests/NosAi.Runtime.Tests/DiscoveryTests.cs |
| C-105 | AP-02 | Hook di memoria o DLL nel client | DROPPED | da definire | da definire | da definire |
| C-106 | AP-02, AP-09 | Replay deterministico di una cattura | MERGED | long CaptureFile.Record(IPacketSource source, string path, CancellationToken cancellationToken = default, TimeSpan? readTimeout = null); CaptureFileSource CaptureFile.Open(string path) | src/NosAi.Runtime/LiveIntegration/Capture/CaptureFile.cs | tests/NosAi.Runtime.Tests/DecideReplayAsOfCaptureTests.cs |
| C-201 | AP-01 | Dispatcher thread-safe degli eventi | MERGED | da confermare | da confermare fra i 16 sorgenti che citano dispatch | da confermare |
| C-202 | AP-01 | Correlazione degli identificativi di entita' | MERGED | da confermare | src/NosAi.Core/WorldModel/ | tests/NosAi.Core.Tests/ |
| C-203 | AP-01 | Fusione delle osservazioni nel World Model | MERGED | WorldModelSnapshot RunOnce(Gate1CanonicalSnapshot snapshot, DateTime nowUtc) | src/NosAi.Runtime/WorldModel/Fusion/WorldModelFusionLoop.cs | tests/NosAi.Runtime.Tests/WorldModel/Fusion/WorldModelFusionLoopTests.cs |
| C-204 | AP-01 | Sincronizzazione fra runtime C# e nosai/ Python | TEST_VERIFIED | Canale 2: RoleBindingConfiguration.Load(string path); RoleBinding? TryGetBinding(string employeeId). Canale 1: DecisionTelemetryWriter.Append(Gate3LoopCycle cycle), agganciato in Gate3DecisionLoop tramite IDecisionTelemetrySink? telemetry = null | src/NosAi.Runtime/Configuration/RoleBindingConfiguration.cs; src/NosAi.Runtime/Observability/DecisionTelemetryWriter.cs | tests/NosAi.Runtime.Tests/RoleBindingConfigurationTests.cs; tests/NosAi.Runtime.Tests/DecisionTelemetryWriterTests.cs; tests/NosAi.Runtime.Tests/Gate3DecisionLoopTests.cs |
| C-301 | AP-08 | Pianificazione gerarchica HTN | MERGED | da confermare | da confermare fra i 4 sorgenti che citano HTN | da confermare |
| C-302 | AP-08 | Pianificazione GOAP | MERGED | da confermare | da confermare fra i 5 sorgenti che citano GOAP | da confermare |
| C-303 | AP-08 | Orchestratore strategico | MERGED | da confermare | da confermare fra i 30 sorgenti che citano Orchestrator | tests/test_orchestrator.py |
| C-304 | AP-08 | Macchina a stati finiti esplicita | DROPPED | da definire | da definire | da definire |
| C-305 | AP-08 | Recupero dopo disconnessione | MERGED | da confermare | da confermare fra i 44 sorgenti che citano recovery o reconnect | da confermare |
| C-401 | AP-00, AP-10 | Cifratura di sessione e autenticazione | MERGED | static SessionCipher ForRuntime(ReadOnlySpan<byte> sessionMaterial); static SessionCipher ForPhone(ReadOnlySpan<byte> sessionMaterial); void SealFrameInto(Span<byte> destination, WireMessageType type, uint sequence, ReadOnlySpan<byte> plaintext); bool TryOpenFrame(ReadOnlySpan<byte> headerBytes, ReadOnlySpan<byte> payload, out byte[] plaintext, out string? reason) | src/NosAi.Protocol/SessionCipher.cs | tests/NosAi.Runtime.Tests/SessionCipherTests.cs; tests/test_session_cipher.py |
| C-402 | AP-10 | Fuzzing sui pacchetti corrotti | TEST_VERIFIED | public static bool WireProtocolFuzzTestRunner.RunAll() | src/NosAi.Runtime/Testing/WireProtocolFuzzTestRunner.cs | tests/NosAi.Runtime.Tests/WireProtocolFuzzTests.cs |
| C-403 | AP-00, AP-10 | Superficie di sicurezza del runtime | MERGED | da confermare | src/NosAi.Security/ | tests/test_crypto_auth.py |
| C-404 | AP-10 | Procedura di rilascio verificata | VERIFIED | n/a | docs/BUILD_TEST_RELEASE.md | scripts/validate.ps1 |
| ORCH-001 | Tooling trasversale | Routing per costo, registro dei consumi, messaggi fra agenti | TEST_VERIFIED | route_task(kind: TaskKind) -> ModelRoute | nosai/orchestration/routing.py | tests/test_orchestration.py |

Contratti standalone MCP: [mcp-hub-001](../contracts/mcp-hub-001.json), [mcp-research-lab-001](../contracts/mcp-research-lab-001.json).
Non sono inclusi nei conteggi dei gruppi legacy. Il contratto Lab è DRAFT e i pacchetti PLANNED.


Firme risolte il 2026-09-10 dal sorgente canonico: C-103, C-104, C-106, C-203 e C-401. Il campo Stato resta quello storico del ledger finché i comandi di verifica non vengono rieseguiti.
