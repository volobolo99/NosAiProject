# INDICE DI ROUTING — NosAiProject

Mappa file → responsabilità → tipi pubblici. Serve a individuare il file esatto senza esplorare il repo.
Generato il 2026-09-02 da scansione diretta di `C:\Users\volob\Desktop\NosAiProject`.
Notazione: `percorso (righe) — responsabilità. Tipi: ...`

Rigenerare quando si aggiungono/rimuovono file o si spostano namespace. Non serve rigenerarlo per modifiche interne ai metodi.

---

## 1. Struttura di primo livello

| Percorso | Contenuto |
|---|---|
| `src/` | 10 progetti .NET 8 (258 file `.cs`) — codice di produzione |
| `nosai/` | pacchetto Python (93 moduli) — implementazione parallela/prototipale |
| `tests/` | 3 progetti di test .NET + ~40 test Python |
| `docs/` | 47 documenti + 19 ADR |
| `proto/` | `nosai_network_v1.proto` — schema del protocollo di rete |
| `scripts/` | `build/test/validate` in PowerShell e bash |
| `tools/` | `NosAi.Analyzers`, `find-vitals.ps1`, `windivert` |
| `data/` | configurazioni, chiavi (`guard_public_key.pem`, `runtime_identity.dpapi`), catture `.noscap`, evidenze test |
| Radice | `NosAi.sln`, `Directory.Build.props`, `NosAi.cmd`, `CLAUDE.md`, `NOSAI_MASTER_ROADMAP.md` |

---

## 2. src/ — progetti .NET

### NosAi.Core (4 file) — contratti di pipeline, nessuna dipendenza

- `IMonotonicClock.cs` (26) — orologio monotono per timestamp deterministici. Tipi: `IMonotonicClock`, `MonotonicClock`
- `IPipelineStage.cs` (27) — contratto di stadio. Tipi: `IPipelineStage`
- `PipelineStage.cs` (27) — base astratta. Tipi: `PipelineStage`
- `StageResult.cs` (43) — esito di stadio con codice di guasto. Tipi: `StageResult`, `FaultCode`

### NosAi.Protocol (4 file) — protocollo di trasporto NosAi

- `WireProtocol.cs` (253) — **frame binario a 12 byte**, buffer da pool, guardia di sequenza. Tipi: `WireMessageType`, `WireMessageTypes`, `WireHeader`, `PooledWireBuffer`, `SequenceGuard`
- `SessionCipher.cs` (401) — cifratura di sessione e scambio effimero. Tipi: `EphemeralKeyExchange`, `SessionCipher`
- `SessionTranscript.cs` (216) — transcript di handshake per binding. Tipi: `HandshakeRole`, `SessionTranscript`
- `DiscoveryProtocol.cs` (107) — discovery in LAN. Tipi: `DiscoveryProtocol`

### NosAi.Security (9 file) — Noise, CapBAC, framing autenticato

- `NoiseSession.cs` (218) — sessione Noise XX lato applicativo. Tipi: `NoiseHandshakeState`, `INoiseSession`, `NoiseXxSession`
- `SequenceGuard.cs` (112) — anti-replay; `Rekey()` cambia chiave **senza** azzerare il contatore (limite 2^64). Tipi: `SequenceGuard`
- `FrameCodec.cs` (155) — codifica/decodifica frame + tag. Tipi: `FrameTagCalculator`, `FrameCodec`
- `NosFrameHeader.cs` (39) — header di frame `Pack = 1`. Tipi: `NosFrameHeader`
- `FrameOpCode.cs` (16) — opcode di frame. Tipi: `FrameOpCode`
- `LengthPrefixedRecord.cs` (79) — record con prefisso di lunghezza. Tipi: `LengthPrefixedRecord`
- `CapabilityToken.cs` (87) — token CapBAC. Tipi: `CapabilityToken`
- `CapabilityValidator.cs` (96) — validazione HMAC-SHA256 dei token. Tipi: `CapabilityVerdict`, `ICapabilityValidator`, `HmacCapabilityValidator`
- `CapabilityScope.cs` (12) — ambito di capacità. Tipi: `CapabilityScope`

### NosAi.Storage (4 file) — journal SQLite su volume NOSAI-SSD

- `SqliteEventJournal.cs` (270) — journal eventi WAL. Tipi: `SqliteEventJournal`
- `IEventJournal.cs` (67) — contratto e record. Tipi: `JournalRecord`, `IEventJournal`
- `SqliteJournalOptions.cs` (18) — PRAGMA e opzioni. Tipi: `SqliteJournalOptions`
- `VolumeLocator.cs` (72) — individuazione del volume per label. Tipi: `VolumeLocator`

### NosAi.Adapter (3 file) — aggancio al processo di gioco

- `Win32ProcessAdapter.cs` (222) — attach al processo NosTale. Tipi: `Win32ProcessAdapter`
- `IGameProcessAdapter.cs` (65) — contratto + geometria finestra. Tipi: `WindowGeometry`, `IGameProcessAdapter`
- `ProcessAttachOptions.cs` (17) — opzioni di attach. Tipi: `ProcessAttachOptions`

### NosAi.GuardClient (5 file) — client PC verso il nodo Guard mobile

- `GuardAiClient.cs` (420) — sessione e chiamate al Guard. Tipi: `GuardProtocolException`, `GuardSession`, `GuardAiClient`
- `DeviceSigner.cs` (96) — firma dispositivo e custodia chiavi. Tipi: `IDeviceSigner`, `DeviceKeyCustody`, `RsaDeviceSigner`
- `GuardReconnectPolicy.cs` (128) — politica di riconnessione. Tipi: `ReconnectDecision`, `GuardReconnectPolicy`
- `GuardSnapshotView.cs` (158) — vista snapshot con campi classificati. Tipi: `ClassifiedField`, `GuardSnapshotView`
- `RuntimeDiscovery.cs` (176) — scoperta del runtime. Tipi: `DiscoveredRuntime`, `RuntimeDiscovery`

### NosAi.GuardAi.App (11 file) — app MAUI Android (nodo Guard)

- `GuardConnectionService.cs` (354) — connessione e stato del link. Tipi: `GuardLinkState`, `GuardStatus`, `GuardConnectionService`
- `KeystoreDeviceSigner.cs` (228) — firma via Android Keystore. Tipi: `KeystoreDeviceSigner`
- `DeviceIdentity.cs` (141) — identità del dispositivo. Tipi: `DeviceIdentity`
- `RuntimePin.cs` (151) — pinning del runtime. Tipi: `RuntimePin`
- `TransportPreference.cs` (55) — scelta USB/Wi-Fi. Tipi: `GuardTransport`, `TransportPreference`
- `MainPage.xaml.cs` (179), `AppShell.xaml.cs` (9), `App.xaml.cs` (11), `MauiProgram.cs` (24) — UI e bootstrap
- `Platforms/Android/MainActivity.cs` (10), `MainApplication.cs` (15)

### NosAi.Host (3 file) — host di processo

- `NosAiHost.cs` (473) — bootstrap e ciclo di vita. Tipi: `HostOptions`, `HostBootstrapResult`, `NosAiHost`
- `DashboardHub.cs` (59) — hub telemetria dashboard. Tipi: `TelemetryFrame`, `DashboardHub`
- `Program.cs` (127) — entry point

### NosAi.ControlPanel (24 file) — WPF operatore

- `MainWindow.xaml.cs` (620) — finestra principale. Tipi: `MainWindow`
- `PerceptionProbe.cs` (333) — sonda percezione dal pannello. Tipi: `ClientWindowLookup`, `PerceptionProbe`, `PerceptionProbeResult`
- `AttachedSnapshot.cs` (249) — snapshot del processo agganciato. Tipi: `AttachedSnapshot`
- `RuntimeSession.cs` (194) — sessione verso il runtime. Tipi: `SessionKind`, `RuntimeSession`
- `OperatorSettings.cs` (161) — impostazioni operatore. Tipi: `OperatorSettings`
- `SnapshotView.cs` (155) — rendering campi. Tipi: `DisplayField`, `SnapshotView`
- `DecisionInspect.cs` (87), `ToolRunner.cs` (84), `EventLogInspect.cs` (79), `NetworkInspect.cs` (56), `ResilienceInspect.cs` (50), `ChannelView.cs` (44), `UiLogger.cs` (44), `ObserveGameDetector.cs` (43), `OperatorHealth.cs` (36), `SecurityInspect.cs` (36), `WorkspaceLocator.cs` (29), `SuiteCatalog.cs` (28), `App.xaml.cs` (27), `OperatorLogFile.cs` (26), `LocalPortProbe.cs` (24), `HudCropStore.cs` (21), `ElevationInspect.cs` (19), `AutoSetup.cs` (60)

---

## 3. src/NosAi.Runtime — il cuore (191 file)

### Contracts / Configuration — contratti e avvio

- `Contracts/RuntimeContracts.cs` (67) — **contratti del percorso critico**. Tipi: `TrustTier`, `ActionKind`, `CandidateAction`, `GuardDecision`, `IGuardAi`, `ISafetyGate`, `AgentStep`, `AgentPlan`, `VerificationResult`
- `Contracts/DataClassification.cs` (77) — provenienza del dato (reale vs derivato). Tipi: `DataSourceKind`, `DataSourceKindText`, `ClassifiedValue`
- `Configuration/Gate1HostOptions.cs` (260) — opzioni Gate 1 e loader. Tipi: `Gate1HostOptions`, `Gate1HostOptionsLoader`
- `Configuration/RuntimeEnvironmentValidator.cs` (187) — verifica ambiente all'avvio. Tipi: `EnvironmentCheckStatus`, `EnvironmentCheck`, `EnvironmentReport`, `RuntimeEnvironmentException`, `RuntimeEnvironmentValidator`
- `Configuration/RuntimeResult.cs` (16) — risultato/errore. Tipi: `RuntimeError`, `RuntimeResult`
- `Program.cs` (471) — entry point del runtime (`--gate N`)

### Gate1 — PC ↔ NosTale ↔ Mobile ↔ Dashboard

- `Gate1/Gate1Runtime.cs` (1011) — runtime del gate, canale Guard, handshake. Tipi: `SessionAuth`, `HandshakeSession`, `Gate1ConnectionSnapshot`, `GuardChannelBindException`, `GuardAiNetworkChannel`, `Gate1RuntimeSnapshot`
- `Gate1/Gate1BootstrapHost.cs` (1181) — host di bootstrap + server operatore + dashboard HTML. Tipi: `Gate1BootstrapHost`, `Gate1OperatorServer`, `Gate1DashboardHtml`
- `Gate1/Gate1CanonicalSnapshot.cs` (382) — contratto canonico dello snapshot. Tipi: `Gate1SnapshotContract`, `RuntimeHealthStatus`, `Gate1HardwareView`, `Gate1ClientView`, `Gate1GuardSessionView`, `Gate1SafetyView`, `Gate1ResilienceView`
- `Gate1/Gate1ObservationChannel.cs` (318) — canale osservazione. Tipi: `Gate1GameObservationView`, `Gate1ObservationChannel`
- `Gate1/RuntimeIdentity.cs` (371) — identità del runtime (DPAPI). Tipi: `RuntimeIdentityException`, `RuntimeIdentity`
- `Gate1/DiscoveryResponder.cs` (127) — risposta al discovery. Tipi: `DiscoveryResponder`
- `Gate1/Gate1TestRunner.cs` (603) — suite di certificazione Gate 1

### Gate2 — Observe → WorldState

- `Gate2/Gate2Runtime.cs` (792) — stato del mondo e eventi. Tipi: `DataProvenance`, `EntityType`, `Position2D`, `WorldEntity`, `ControlledPlayerState`, `WorldStateSnapshot`, `EventPriority`, `RuntimeEvent`
- `Gate2/Gate2WorldModel.cs` (113) — riduttore di osservazioni. Tipi: `ObservationBatch`, `Gate2WorldModelPolicy`, `WorldModelReducer`
- `Gate2/Gate2DeltaSync.cs` (283) — codec delta e tracker. Tipi: `WorldStateDeltaCodec`, `SyncUpdate`, `DeltaSyncTracker`
- `Gate2/Gate2ContextSlimming.cs` (145) — compressione contesto/errori. Tipi: `ExceptionSignature`, `ErrorHistoryCompressor`, `SlimmedEntity`, `SlimmedWorldContext`, `WorldContextSlimmer`
- `Gate2/EventLogReplay.cs` (317) — replay del log eventi. Tipi: `EventLogRecord`, `EventLogEntry`, `EventLogGap`, `EventLogReplay`, `EventLogReader`, `Gate2EventSchema`
- `Gate2/EventLogDiagnostics.cs` (157) — diagnostica dei buchi nel log. Tipi: `EventLogTailEntry`, `EventLogGapReport`, `EventLogHealth`, `EventLogDiagnostics`
- `Gate2/Gate2SessionStore.cs` (225), `Gate2Sqlite.cs` (76) — persistenza
- `Gate2/Gate2IntegratedEngine.cs` (91), `Gate2TestRunnerChecks.cs` (455)

### Gate3 — Simulation → Ranking → Planner → Execute → Verify

- `Gate3/Gate3Runtime.cs` (1323) — **percorso critico completo**. Tipi: `ExecutionResult`, `VerificationOutcome`, `VerificationResult`, `SimulationEngine`, `TacticalRankingEngine`, `ActionPlanner`, `AuthorizedActionExecutor`
- `Gate3/Gate3DecisionLoop.cs` (350) — ciclo decisionale. Tipi: `Gate3LoopCycle`, `Gate3LoopView`, `Gate3DecisionLoop`
- `Gate3/Gate3WorldState.cs` (333) — sorgenti di stato del mondo. Tipi: `Gate3WorldState`, `IWorldStateSource`, `GameplayProviderWorldStateSource`, `Gate1SnapshotWorldStateSource`
- `Gate3/Gate3Effector.cs` (176) — effettori con gate di policy. Tipi: `ExecutionState`, `IActionEffector`, `DisabledActionEffector`, `ActionEffectorFactory`, `PolicyGatedActionEffector`
- `Gate3/InputActionEffector.cs` (306) — attuazione input + proiezione schermo. Tipi: `IScreenProjection`, `UncalibratedScreenProjection`, `InputActionEffector`
- `Gate3/Gate3Observation.cs` (101), `Gate3ReplayProbe.cs` (150)

### Gate4/5/6

- `Gate4/Gate4Runtime.cs` (294) — progressione e obiettivi. Tipi: `GoalType`, `StrategyLifecycleStatus`, `SpecialistCardType`, `ResourceInventory`, `CharacterProgressionProfile`, `QuestDataProvenance`
- `Gate5/Gate5Runtime.cs` (614) — routing tra provider decisionali. Tipi: `ProviderType`, `ProviderRoutingPolicy`, `DecisionSuggestion`, `IDecisionProvider`, `HeuristicRuleProvider`, `SimulatedLocalInferenceProvider`
- `Gate5/Gate5Integration.cs` (361) — integrazione + runner
- `Gate6/Gate6Runtime.cs` (621) — esecuzione autorizzata e verifica. Tipi: `AuthorizedActionExecutor`, `ActionExecutionVerifier`, `SimulatedGameWorld`
- `Gate4/Gate4TestRunner.cs` (259)

### Safety / Security / Guard — fail-closed e crittografia

- `Safety/SafetyGate.cs` (91) — **gate fail-closed**. Tipi: `SafetyGate`
- `Safety/RuntimeSafetyController.cs` (193) — interruttori di sicurezza. Tipi: `SafetySwitch`, `SafetySwitchChange`, `RuntimeSafetyController`
- `Safety/ImmediateHalt.cs` (86) — arresto immediato operatore: disarma, poi abortisce. Tipi: `IImmediateHaltTarget`, `RuntimeImmediateHaltTarget`, `ImmediateHaltResult`, `ImmediateHalt`
- `Safety/RuntimeSafetyPolicy.cs` (14), `Safety/LiveInputAuthorization.cs` (21)
- `Security/NoiseProtocol.cs` (613) — **implementazione Noise**. Tipi: `NoiseCipherState`, `NoiseSymmetricState`, `NoiseHandshakeState`, `ReplayWindow`, `NoiseTransport`
- `Security/EphemeralSession.cs` (217) — X25519 e provisioning chiavi statiche. Tipi: `X25519Identity`, `EphemeralSession`, `StaticKeyProvisioning`
- `Security/RuntimeAuthorization.cs` (198) — principal e capacità. Tipi: `SecurityPrincipal`, `RuntimeCapability`, `AuthorizationDecision`, `IRuntimeAuthorizationPolicy`, `Gate1AuthorizationPolicy`
- `Security/ExecutionIntegrity.cs` (16), `Security/IPacketManipulator.cs` (24)
- `Security/NoiseProtocolChecks.cs` (229), `Security/EphemeralSessionTestRunner.cs` (198)
- `Guard/GuardAi.cs` (17) — adattatore Guard

### Perception — cattura schermo, HUD, proiezione

- `Perception/PerceptionPipeline.cs` (551) — pipeline percettiva. Tipi: `CaptureFrame`, `IFrameSource`, `SyntheticFrameSource`, `UnavailableFrameSource`, `PixelRect`, `RoiKind`, `RegionOfInterest`, `RoiSegmenter`
- `Perception/DxgiCapture.cs` (438) — Desktop Duplication triple-buffer. Tipi: `TripleFrameBuffer`, `CaptureUnavailable`, `DxgiDesktopDuplicationSource`, `TripleBufferedCapture`
- `Perception/DxgiInterop.cs` (261) — interop D3D11/DXGI
- `Perception/ScreenProjectionCalibration.cs` (696) e `ScreenProjectionAutoCalibrator.cs` (625) — **calibrazione mondo→schermo**
- `Perception/CalibratedScreenProjection.cs` (237), `ScreenProjectionProbe.cs` (227), `ScreenProjectionWatcher.cs` (162)
- `Perception/GeometryEpoch.cs` (361) — epoche di geometria finestra. Tipi: `GeometryShape`, `GeometryStamp`, `GeometryEpoch`
- `Perception/HudGlyphAtlas.cs` (292), `HudProbe.cs` (236), `HudBarFillReader.cs` (215), `HudGlyphExtractor.cs` (120), `HudGlyphTraining.cs` (114), `HudCropWriter.cs` (90) — lettura HUD
- `Perception/TargetRoiCalibration.cs` (253), `TargetStateComposer.cs` (111), `TargetFrameReader.cs` (74), `ScreenTargetFrameSource.cs` (79) — bersaglio
- `Perception/ScreenVitalReader.cs` (175), `ScreenDerivedVitals.cs` (110) — vitali da schermo
- `Perception/ClientWindowDpiProbe.cs` (218), `DpiAwarenessRegime.cs` (180), `ClientWindowLocator.cs` (128)
- `Perception/PerceptionContracts.cs` (28), `NullPerceptionProvider.cs` (7), `PerceptionPipelineTestRunner.cs` (304)

### Perception/Network — decodifica del protocollo NosTale

- `Perception/Network/ProtocolMap.cs` (487) — **mappa dichiarativa dei messaggi**. Tipi: `FieldSpec`, `MessageSpec`, `ProtocolMap`, `PlayerVitalsSpec`, `ConfigurableProtocolDecoder`
- `Perception/Network/GameTrafficObserver.cs` (415) — osservazione traffico. Tipi: `GameEventKind`, `EntitySighting`, `GameEvent`, `PlayerVitals`, `DecodedObservations`, `IGamePacketDecoder`
- `Perception/Network/NosTaleWorldProtocolDecoder.cs` (390), `NosTaleWorldDecoder.cs` (172)
- `Perception/Network/NetworkWorldFeed.cs` (267) — feed verso il world model. Tipi: `NetworkWorldFeed`, `TrafficRecorder`
- `Perception/Network/MessageFramer.cs` (143), `NetworkObservationContracts.cs` (70), `NetworkObservationSources.cs` (129), `ScopedGameTrafficFilter.cs` (62), `SyntheticProtocolDecoder.cs` (94)
- `Perception/Network/NetworkDecoderChecks.cs` (303), `NetworkObservationTestRunner.cs` (245)

### LiveIntegration — processo reale, memoria, cattura pacchetti

- `LiveIntegration/NosTaleClientLayout.cs` (756) — **layout del client e firme di memoria**. Tipi: `SignatureByte`, `NosTaleClientLayout`, `PlayerObjectReading`, `MapEntityReading`, `MapEntityKind`
- `LiveIntegration/RealClientConnector.cs` (533) — baseline del client reale. Tipi: `ClientBaselineAvailability`, `ClientBaselineSnapshot`, `RealClientConnector`
- `LiveIntegration/ProcessMemoryReader.cs` (319), `MemoryScanProbe.cs` (255), `MemoryScanner.cs` (180), `PlayerObjectProbe.cs` (238), `ClientMemorySession.cs` (223)
- `LiveIntegration/GameplayProvider.cs` (322) — contratto di gameplay. Tipi: `GameplayObservation`, `IGameplayProvider`, `NetworkGameplayProvider`, `UnavailableGameplayProvider`
- `LiveIntegration/MemoryGameplayProvider.cs` (255), `TargetAwareGameplayProvider.cs` (95), `NetworkWorldStateObserver.cs` (32)
- `LiveIntegration/ClientNetworkObserver.cs` (266) — connessioni TCP del client
- `LiveIntegration/Capture/` — `ScopedLiveCaptureBackend.cs` (285), `TcpStreamReassembler.cs` (228), `WorldChannelReplay.cs` (228), `CaptureFile.cs` (202), `ReassembledObservationSource.cs` (184), `NosTaleWorldFramer.cs` (171), `GameTrafficCaptureEngine.cs` (163), `CaptureAnalysis.cs` (161), `WinDivertPacketSource.cs` (153), `ProtocolMapFramer.cs` (148), `Ipv4TcpParser.cs` (134), `GameStreamFramer.cs` (88), `TcpSegment.cs` (48), `IPacketSource.cs` (47), `InMemoryPacketSource.cs` (42)

### LowLevel — attuazione input e autorità di sessione

- `LowLevel/SessionActuationAuthority.cs` (818) — **autorità di attuazione e integrità di processo**. Tipi: `IntegrityLevel`, `IProcessIntegrityReader`, `SessionAuthorityVerdict`, `SessionActuationAuthority`, `Win32ProcessIntegrityReader`
- `LowLevel/GatedInputBackend.cs` (409) — input con gate. Tipi: `InputRefusal`, `GatedInputBackend`, `RecordingInputBackend`
- `LowLevel/HumanInputMonitor.cs` (382) — rilevamento input umano. Tipi: `IHumanInputMonitor`, `HumanInputMonitor`, `NotWatchingHumanInput`
- `LowLevel/CommitPointValidator.cs` (354) — validazione del punto di commit. Tipi: `ICommitEnvironment`, `CommitRequest`, `CommitDecision`, `CommitPointValidator`, `Win32CommitEnvironment`
- `LowLevel/InputEnvironmentProbe.cs` (323), `InputGuardsProbe.cs` (251), `InputAuthorityProbe.cs`, `Win32InputBackend.cs` (285), `KeybindMap.cs` (222), `ActuationScope.cs` (165), `VirtualKeys.cs` (108), `InputControlTestRunner.cs` (348)

### Navigation

- `Navigation/Pathfinding/NavigationPathfinding.cs` (805) — **A\* e ostacoli dinamici**. Tipi: `TileType`, `PathFailureReason`, `NavigationStatus`, `GridPoint`, `MapPortal`, `DynamicHazardZone`, `CalculatedPathResult`, `MapGridData`, `AStar`
- `Navigation/MapIdFinder.cs` (698) — individuazione dell'id mappa. Tipi: `MapIdAnchorKind`, `MapIdHit`, `MapIdAnchors`, `MapIdCandidates`, `MapIdFinder`
- `Navigation/MapGridExtractor.cs` (492), `MapGrid.cs` (349), `StaticGeometryLayer.cs` (268), `MapGridSetIdentity.cs` (239), `MapGridCheck.cs` (128), `IMapGridLoader.cs` (96), `BinaryMapGridLoader.cs` (78)

### AI / Decisione / Tattica

- `AI/Decision/UtilityDecisionEngine.cs` (248) — motore a utilità + loader regole. Tipi: `UtilityDecisionEngine`, `RuleFileEntry`, `RuleFileCondition`, `DecisionRuleLoader`
- `AI/Decision/UtilityDecisionContracts.cs` (176) — contratti di regola. Tipi: `DecisionContext`, `ConditionOperator`, `RuleCondition`, `DecisionRule`, `RuleSkipReason`, `SkippedRule`, `DecisionOutcome`
- `AI/Decision/UtilityRuleProvider.cs` (137), `DecisionEngineTestRunner.cs` (302)
- `AI/LocalInference/LocalAiInferenceEngine.cs` (150) — inferenza locale **senza autorità di esecuzione**. Tipi: `ModelQuantization`, `HardwareComputeDevice`, `LocalModelConfig`, `AiRecommendedIntent`, `AiInferenceResult`, `CapBacPromptSanitizer`
- `Tactical/TacticalRanking.cs` (17), `TacticalPlanner.cs` (25), `Simulation.cs` (25) — ranking/planner/simulatore deterministico
- `Autonomy/AutonomyPipeline.cs` (939) — **pipeline di autonomia end-to-end**. Tipi: `TrustTier`, `RuntimeMode`, `ActionType`, `RecoveryState`, `RecoveryHaltTransition`, `RecoveryController`, `RecoveryStrategy`, `MapPoint`, `ActionTarget`, `Entity`, `Position`, `InventorySlot`
- `Autonomy/TargetSelector.cs` (234) — selezione bersaglio. Tipi: `SelectableEntity`, `TargetSelectionPolicy`, `TargetChoice`, `TargetSelector`
- `Learning/PredictionLedger.cs` (240) — calibrazione previsione/osservazione. Tipi: `Prediction`, `Observation`, `LearningOutcome`, `Calibration`, `PredictionLedger`
- `PlayAi/UtilityAi.cs` (15), `Humanizer/DeterministicHumanizer.cs` (123), `Humanizer/HumanizerContracts.cs` (17)

### Orchestrazione / Host / Hardware

- `Host/NosAiMasterRuntimeHost.cs` (715) — host master. Tipi: `MasterHostStatus`, `MasterSystemTelemetry`, `MasterTrustManager`, `MasterSafetyGate`, `EmbeddedControlCenterServer`
- `Orchestration/AutonomousAgentRuntime.cs` (105), `AutonomousOrchestratorLoop.cs` (50), `Orchestrator.cs` (57), `RuntimeComposition.cs` (105)
- `Hardware/Autoscale/HardwareAutoscaleController.cs` (174) — **budget e soglie termiche**. Tipi: `RuntimePerformanceMode`, `HardwareTelemetrySnapshot`, `RuntimeBudgetParameters`, `HardwareAutoscaleController`
- `Hardware/LiveHardwareTelemetry.cs` (155), `HardwareProbe.cs` (61), `HardwareProfileStore.cs` (46), `HardwareAutoSettings.cs` (38), `AutoSetManager.cs` (20), `HardwareProfilePaths.cs` (10)
- `Capabilities/NosAiCapabilityKernel.cs` (86) — kernel di capacità. Tipi: `INavigationCapability`, `IEconomyCapability`, `NosAiCapabilityKernel`
- `Network/Gateway/ControlPanelGatewayEngine.cs` (269) — gateway verso il pannello + rate limiter
- `Storage/Infrastructure/StorageInfrastructure.cs` (719) — **percorsi, benchmark, backup su NOSAI-SSD**. Tipi: `StorageDirectoryKind`, `StorageDriveDescriptor`, `StorageBenchmarkResult`, `BackupSnapshotManifest`, `StoragePathResolver`
- `Observability/HaltDiagnosticsDumper.cs` (130) — dump su transizione a halt. Tipi: `HaltDiagnosticDump`, `HaltDiagnosticsDumper`, `HaltDiagnosticsContext`, `CommitPointRefusalDump`, `SessionAuthorityDump`
- `Observability/PipelineStageBoard.cs` (81) — ultimo esito per stage. Tipi: `StageOutcomeDump`, `PipelineStageBoard`
- `Observability/RuntimeLogger.cs` (98), `ModuleReachability.cs` (222), `Telemetry/TelemetryMetrics.cs` (35)
- `Operator/HaltCli.cs` (59) — `--halt` verso un runtime già in ascolto. Tipi: `HaltCli`
- `Operator/OperatorMenu.cs` (508) — menu operatore da console
- `WorldModel/WorldState.cs` (35), `Adapters/NosTaleGameAdapter.cs` (95), `Adapters/IGameAdapter.cs` (13)

### Dominio di gioco

- `GameData/GameReferenceDatabase.cs` (631), `NosDataTable.cs` (423), `NosArchive.cs` (374), `ReferenceImporter.cs` (271) — archivi e tabelle di riferimento NosTale
- `Raids/Dodekatheon/DodekatheonRaidOrchestrator.cs` (193), `Raids/Orchestration/RaidOrchestration.cs` (130)
- `Economy/Inventory/InventoryEconomy.cs` (139) + runner (208)
- `Events/InstantBattle/InstantBattleAndEventEngine.cs` (73)
- `Miniland/Production/MinilandProductionEngine.cs` (131)

### Testing (infrastruttura di certificazione)

- `Testing/TestSuiteRunner.cs` (461), `TestEvidence.cs` (343), `TestConsoleService.cs` (254), `TestConsoleHtml.cs` (212), `TestEvidenceProtocol.cs` (188), `GateCertificationRunner.cs` (132), `CertificationSuites.cs` (95)

---

## 4. nosai/ — pacchetto Python (93 moduli)

- `core/` — `contracts.py`, `orchestrator.py`, `safety.py`, `simulation.py`, `simulation_policy.py`, `tactical_ranking.py`, `world_model.py`, `coordinated_action_manager.py`, `data_classification.py`
- `runtime/` — `engine.py`, `agent_loop.py`, `closed_loop.py`, `orchestrator_bridge.py`, `policy.py`, `provider_router.py`, `trust.py`, `watchdog.py`, `hardware_watchdog.py`, `recovery.py`, `adaptive_throttling.py`, `context_slimming.py`, `session.py`, `session_protocol.py`, `state.py`, `timeouts.py`, `tools.py`, `resources.py`, `memory.py`, `events.py`, `evaluation.py`, `integration.py`, `contracts.py`, `hardware.py`
- `network/` — `wire_protocol.py`, `session_cipher.py`, `session_transcript.py`, `session_auth.py`, `crypto_auth.py`
- `security/` — `ephemeral_session.py`
- `perception/` — `pipeline.py`, `vision.py`, `tracking.py`, `game_state.py`, `world_adapter.py`, `contracts.py`
- `tactical/` — `search.py`, `combat_engine.py`, `stochastic.py`, `scheduling.py`, `threat.py`, `action_model.py`, `play_ai.py`
- `phone/` — `adb.py`, `build.py`, `deploy.py`, `enroll.py`, `guard_client.py`, `onboarding_engine.py`, `provisioning.py`
- `guard/` — `protocol.py`, `runtime.py`; `bringup/` — `guard_server.py`, `protocol.py`
- `storage/` — `volume.py`, `paths.py`, `sqlite_policy.py`, `health.py`; `persistence/sqlite_logger.py`
- `dashboard/server.py`, `party/{partner,pet}.py`, `miniland/automation.py`, `ai/rule_based.py`, `telemetry/advanced.py`, `testing/evidence.py`

---

## 5. docs/ — dove sta scritto cosa

**Architettura e regole**: `ARCHITETTURA.md`, `NOSAI_ARCHITECTURE_BASELINE.md`, `SOURCE_OF_TRUTH.md`, `REGOLE_PROGETTO.md`, `GLOSSARIO.md`, `REQUISITI.md`, `METADATI_PROGETTO.md`

**Gate**: `GATE1_CHECKLIST.md`, `GATE1_COMPONENT_MAP.md`, `GATE3_PIPELINE.md`, `GATE4_PROGRESSION.md`, `CATALOGO_AZIONI_E_POSTCONDIZIONI.md`, `CERTIFICAZIONI/gate1.md`

**Controllo personaggio**: `CONTROLLO_PERSONAGGIO_ARCHITETTURA.md`, `_ATTUAZIONE.md`, `_ROADMAP.md`

**Sicurezza e rete**: `CRITTOGRAFIA_NOISE_E_CHIAVI_EFFIMERE.md`, `INTEGRAZIONE_RSA_SESSION_AUTH.md`, `SICUREZZA.md`, `PROTOCOLLO_NOSTALE.md`, `WIFI_BRINGUP.md`

**Runtime e infrastruttura**: `PERSISTENZA_SQLITE_E_SHARED_MEMORY.md`, `HARDWARE_BASELINE_AND_AUTOSCALE.md`, `RECOVERY_WATCHDOG.md`, `EXTERNAL_SSD_DEPLOYMENT.md`, `DASHBOARD.md`, `PROGRESSION_ENGINE_SPEC.md`

**Ordine dei lavori**: `PIANO_CAPACITA.md` — quale capacità si costruisce, in che ordine, e chi la fa

**Processo**: `BUILD_TEST_RELEASE.md`, `TESTING.md`, `TEST_RIMANDATI.md`, `RELEASE_CHECKLIST.md`, `GIT_WORKFLOW.md`, `CONTRIBUTING.md`, `SESSIONI_CURSOR.md`, `AGENT_EXECUTION_CHECKLIST.md`, `AGENT_DEVELOPMENT_ENVIRONMENT.md`

**Stato e audit**: `STATO_IMPLEMENTAZIONE.md`, `AUDIT_TECNICO.md`, `AUDIT_SOTTOSISTEMI_2026-08-30.md`, `NOSAI_BASELINE_AUDIT_2026-08-30.md`, `CHANGELOG.md`, `ROADMAP.md`, `ROADMAP_ESECUTIVA.md`, `PIANO_OPERATIVO.md`

**ADR (20)**: 0001 architettura canonica · 0002 separazione dati reali/demo · 0003 autorità di sicurezza · 0004 verifica prima del rilascio · 0005 contratti versionati · 0006 canale telefono canonico · 0007 trasporto Wi-Fi · 0008 handshake mutuo · 0009 cifratura payload · 0010 custodia chiavi · 0011 sessione Guard singola · 0012 sorgente di osservazione · 0013 injection non adottata · 0014 l'operatore sceglie il data path · 0015 roadmap esecutiva canonica · 0016 pianificare su osservazione parziale · 0017 addestrare il lettore schermo dal filo · 0018 stabilire il bersaglio dallo schermo · 0019 canale di attuazione · 0020 un solo percorso di autorizzazione all'atto (*proposto*)

---

## 6. Rotte rapide: intento → file

| Se devi toccare... | Apri |
|---|---|
| Frame binario a 12 byte, sequenze | `NosAi.Protocol/WireProtocol.cs`, `NosAi.Security/SequenceGuard.cs` |
| Handshake Noise / chiavi effimere | `Runtime/Security/NoiseProtocol.cs`, `EphemeralSession.cs`, `NosAi.Security/NoiseSession.cs` |
| Token CapBAC HMAC-SHA256 | `NosAi.Security/CapabilityToken.cs`, `CapabilityValidator.cs` |
| Safety Gate fail-closed | `Runtime/Safety/SafetyGate.cs`, `RuntimeSafetyController.cs` |
| Arresto immediato / dump halt | `Runtime/Safety/ImmediateHalt.cs`, `Observability/HaltDiagnosticsDumper.cs`, `Operator/HaltCli.cs` |
| Resilienza e salute registro | `Runtime/Gate1/Gate1CanonicalSnapshot.cs` (`Gate1ResilienceView`), `NosAi.ControlPanel/ResilienceInspect.cs`, `EventLogInspect.cs` |
| Trust Tier | `Runtime/Contracts/RuntimeContracts.cs`, `Host/NosAiMasterRuntimeHost.cs` |
| Percorso critico Observe→Verify | `Runtime/Gate3/Gate3Runtime.cs`, `Gate3DecisionLoop.cs` |
| WorldState e delta | `Runtime/Gate2/Gate2Runtime.cs`, `Gate2DeltaSync.cs`, `Gate2WorldModel.cs` |
| Lettura memoria del client | `Runtime/LiveIntegration/NosTaleClientLayout.cs`, `ProcessMemoryReader.cs` |
| Decodifica pacchetti NosTale | `Runtime/Perception/Network/ProtocolMap.cs`, `NosTaleWorldProtocolDecoder.cs` |
| Cattura schermo / HUD | `Runtime/Perception/DxgiCapture.cs`, `HudProbe.cs`, `HudBarFillReader.cs` |
| Calibrazione mondo→schermo | `Runtime/Perception/ScreenProjectionCalibration.cs`, `ScreenProjectionAutoCalibrator.cs` |
| Invio input / autorità attuazione | `Runtime/LowLevel/SessionActuationAuthority.cs`, `GatedInputBackend.cs`, `Win32InputBackend.cs` |
| Pathfinding e griglie mappa | `Runtime/Navigation/Pathfinding/NavigationPathfinding.cs`, `MapGrid.cs` |
| Ranking tattico | `Runtime/Tactical/TacticalRanking.cs`, `Gate3/Gate3Runtime.cs` (`TacticalRankingEngine`) |
| Post-condizione e verifica di un'azione | `docs/CATALOGO_AZIONI_E_POSTCONDIZIONI.md`, `Gate3/Gate3Runtime.cs` (`ActionExecutionVerifier`) |
| SQLite WAL / volume NOSAI-SSD | `NosAi.Storage/SqliteEventJournal.cs`, `VolumeLocator.cs`, `Runtime/Storage/Infrastructure/StorageInfrastructure.cs` |
| Soglie termiche / autoscale | `Runtime/Hardware/Autoscale/HardwareAutoscaleController.cs` |
| Nodo Guard mobile | `NosAi.GuardClient/GuardAiClient.cs`, `NosAi.GuardAi.App/GuardConnectionService.cs` |
| Dashboard e pannello operatore | `Runtime/Gate1/Gate1BootstrapHost.cs`, `NosAi.ControlPanel/MainWindow.xaml.cs` |
| Suite di certificazione | `Runtime/Testing/TestSuiteRunner.cs`, `GateCertificationRunner.cs`, `Gate1/Gate1TestRunner.cs` |
