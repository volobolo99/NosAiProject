# INDICE DI ROUTING — NosAiProject

Mappa file → responsabilità → tipi pubblici. Serve a individuare il file esatto senza esplorare il repo.
Generato il 2026-09-02 da scansione diretta di `C:\Users\volob\Desktop\NosAiProject`.
Notazione: `percorso (righe) — responsabilità. Tipi: ...`

Rigenerare quando si aggiungono/rimuovono file o si spostano namespace. Non serve rigenerarlo per modifiche interne ai metodi.

---

## 1. Struttura di primo livello

| Percorso | Contenuto |
|---|---|
| `src/` | 10 progetti .NET 8 (291 file `.cs`) — codice di produzione |
| `nosai/` | pacchetto Python (84 file `.py`) — ricerca, prototipi e tooling |
| `tests/` | 3 progetti di test .NET + 37 test Python |
| `docs/` | 51 documenti + 21 ADR |
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

- `NosAiHost.cs` (474) — bootstrap e ciclo di vita. Tipi: `HostOptions`, `HostBootstrapResult`, `NosAiHost`
- `DashboardHub.cs` (59) — hub telemetria dashboard. Tipi: `TelemetryFrame`, `DashboardHub`
- `Program.cs` (127) — entry point

### NosAi.ControlPanel (32 file) — WPF operatore

- `MainWindow.xaml.cs` (773) — finestra principale. Tipi: `MainWindow`
- `PerceptionProbe.cs` (334) — sonda percezione dal pannello. Tipi: `ClientWindowLookup`, `PerceptionProbe`, `PerceptionProbeResult`
- `AttachedSnapshot.cs` (276) — snapshot del processo agganciato. Tipi: `AttachedSnapshot`
- `TargetInspect.cs` (411) — caccia all'id bersaglio e ROI. Tipi: `TargetHuntKind`, `TargetRoiKind`, `TargetHuntView`, `TargetInspect`
- `MapInspect.cs` — cella di stazione e griglia. Tipi: `MapCellDraw`, `StandingCellKind`, `MapView`, `MapWorldReading`, `MapInspect`
- `GameplayWireReader.cs` — entità, colpo, bersaglio, id mappa e cella di appoggio dal payload `gameplayBaseline`. Tipi: `GameplayPanelRead`, `GameplayWireReader`
- `RuntimeSession.cs` (194) — sessione verso il runtime. Tipi: `SessionKind`, `RuntimeSession`
- `OperatorSettings.cs` (161) — impostazioni operatore. Tipi: `OperatorSettings`
- `SnapshotView.cs` (155) — rendering campi. Tipi: `DisplayField`, `SnapshotView`
- `SurroundingsInspect.cs` (148) — dintorni osservati. Tipi: `SurroundingsKind`, `NearbyEntityRow`, `SurroundingsView`, `SurroundingsInspect`
- `MapGridManifest.cs` (112) — identità registrata delle griglie estratte. Tipi: `MapGridManifest`
- `CombatInspect.cs` (96) — ultimo colpo e bersaglio a tre valori. Tipi: `CombatView`, `CombatInspect`
- `KeybindsInspect.cs` (60) — tasti configurati e intenti scoperti. Tipi: `KeybindsView`, `KeybindsInspect`
- `DecisionInspect.cs` (87), `ToolRunner.cs` (84), `EventLogInspect.cs` (79), `NetworkInspect.cs` (56), `ResilienceInspect.cs` (50), `ChannelView.cs` (44), `UiLogger.cs` (44), `ObserveGameDetector.cs` (43), `OperatorHealth.cs` (36), `SecurityInspect.cs` (36), `WorkspaceLocator.cs` (29), `SuiteCatalog.cs` (28), `App.xaml.cs` (27), `OperatorLogFile.cs` (26), `LocalPortProbe.cs` (24), `HudCropStore.cs` (21), `ElevationInspect.cs` (19), `AutoSetup.cs` (60)

---

## 3. src/NosAi.Runtime — il cuore (216 file)

### Contracts / Configuration — contratti e avvio

- `Contracts/TrustTier.cs` (18) — livello di autonomia concesso. Tipi: `TrustTier`
- `Contracts/CandidateAction.cs` (32) — candidato del percorso Guard/Safety. Tipi: `ActionKind`, `CandidateAction`, `GuardDecision`, `IGuardAi`, `ISafetyGate`
- `Contracts/ActionCandidate.cs` (170) — azione del ciclo Gate 3 e bersaglio tipizzato. Tipi: `ActionType`, `ActionTarget`, `ActionCandidate`
- `Contracts/DataClassification.cs` (78) — provenienza del dato (reale vs derivato). Tipi: `DataSourceKind`, `DataSourceKindText`, `ClassifiedValue`
- `Contracts/MapPoint.cs` (13) — punto di mappa. Tipi: `MapPoint`
- `Contracts/PredictedOutcome.cs` (11) — esito predetto. Tipi: `PredictedOutcome`
- `Contracts/RuntimeMode.cs` (12) — stato operativo che gela quali azioni possono partire. Tipi: `RuntimeMode`
- `Configuration/Gate1HostOptions.cs` (261) — opzioni Gate 1 e loader. Tipi: `Gate1HostOptions`, `Gate1HostOptionsLoader`
- `Configuration/RuntimeEnvironmentValidator.cs` (187) — verifica ambiente all'avvio. Tipi: `EnvironmentCheckStatus`, `EnvironmentCheck`, `EnvironmentReport`, `RuntimeEnvironmentException`, `RuntimeEnvironmentValidator`
- `Configuration/RuntimeResult.cs` (16) — risultato/errore. Tipi: `RuntimeError`, `RuntimeResult`
- `Program.cs` (548) — entry point del runtime (`--world-replay`, `--reference-info`, `--step`, `--keybinds-check`)

### Gate1 — PC ↔ NosTale ↔ Mobile ↔ Dashboard

- `Gate1/Gate1Runtime.cs` (1017) — runtime del gate, canale Guard, handshake. Tipi: `SessionAuth`, `HandshakeSession`, `Gate1ConnectionSnapshot`, `GuardChannelBindException`, `GuardAiNetworkChannel`, `Gate1RuntimeSnapshotProvider`
- `Gate1/Gate1BootstrapHost.cs` (1384) — host di bootstrap + server operatore + dashboard HTML. Tipi: `Gate1BootstrapHost`, `Gate1OperatorServer`, `Gate1DashboardHtml`
- `Gate1/Gate1CanonicalSnapshot.cs` (401) — contratto canonico dello snapshot. Tipi: `Gate1SnapshotContract`, `RuntimeHealthStatus`, `Gate1HardwareView`, `Gate1ClientView`, `Gate1GuardSessionView`, `Gate1SafetyView`, `Gate1ResilienceView`
- `Gate1/Gate1ObservationChannel.cs` (319) — canale osservazione. Tipi: `Gate1GameObservationView`, `Gate1ObservationChannel`
- `Gate1/RuntimeIdentity.cs` (371) — identità del runtime (DPAPI). Tipi: `RuntimeIdentityException`, `RuntimeIdentity`
- `Gate1/DiscoveryResponder.cs` (127) — risposta al discovery. Tipi: `DiscoveryResponder`
- `Gate1/Gate1TestRunner.cs` (604) — suite di certificazione Gate 1

### Gate2 — Observe → WorldState

- `Gate2/Gate2Runtime.cs` (784) — stato del mondo e eventi. Tipi: `DataProvenance`, `EntityType`, `WorldEntity`, `ControlledPlayerState`, `WorldStateSnapshot`, `EventPriority`, `RuntimeEvent`
- `Gate2/Gate2WorldModel.cs` (113) — riduttore di osservazioni. Tipi: `ObservationBatch`, `Gate2WorldModelPolicy`, `WorldModelReducer`
- `Gate2/Gate2DeltaSync.cs` (283) — codec delta e tracker. Tipi: `WorldStateDeltaCodec`, `SyncUpdate`, `DeltaSyncTracker`
- `Gate2/Gate2ContextSlimming.cs` (145) — compressione contesto/errori. Tipi: `ExceptionSignature`, `ErrorHistoryCompressor`, `SlimmedEntity`, `SlimmedWorldContext`, `WorldContextSlimmer`
- `Gate2/EventLogReplay.cs` (317) — replay del log eventi. Tipi: `EventLogRecord`, `EventLogEntry`, `EventLogGap`, `EventLogReplay`, `EventLogReader`, `Gate2EventSchema`
- `Gate2/EventLogDiagnostics.cs` (157) — diagnostica dei buchi nel log. Tipi: `EventLogTailEntry`, `EventLogGapReport`, `EventLogHealth`, `EventLogDiagnostics`
- `Gate2/Gate2SessionStore.cs` (225), `Gate2Sqlite.cs` (76) — persistenza
- `Gate2/Gate2IntegratedEngine.cs` (91), `Gate2TestRunnerChecks.cs` (455)

### Gate3 — Simulation → Ranking → Planner → Execute → Verify

- `Gate3/Gate3Runtime.cs` (1886) — **percorso critico completo**. Tipi: `ExecutionResult`, `VerificationOutcome`, `VerificationResult`, `SimulationEngine`, `TacticalRankingEngine`, `ActionPlanner`, `ReactionPolicy`, `AuthorizedActionExecutor`, `ActionExecutionVerifier`, `CycleOutcome`, `Gate3CycleResult`, `Gate3ExecutionOrchestrator`, `Gate3TestRunner`
- `Gate3/PostConditions.cs` (876) — catalogo normativo in codice: cosa promette un'azione e come si controlla. Tipi: `IPostCondition`, `PostConditionInput`, `PostConditionVerdict`, `DivergenceBands`, `PostConditionTable`, `MoveToPositionPostCondition`, `TargetEntityPostCondition`, `UseBasicAttackPostCondition`, `UseSkillPostCondition`, `UseConsumablePostCondition`, `CollectGroundItemPostCondition`, `EmergencyFleePostCondition`
- `Gate3/Gate3DecisionLoop.cs` (353) — ciclo decisionale. Tipi: `Gate3LoopCycle`, `Gate3LoopView`, `Gate3DecisionLoop`
- `Gate3/Gate3WorldState.cs` (412) — sorgenti di stato del mondo. Tipi: `Gate3WorldState`, `IWorldStateSource`, `GameplayProviderWorldStateSource`, `Gate1SnapshotWorldStateSource`
- `Gate3/Gate3Effector.cs` (193) — effettori con gate di policy. Tipi: `ExecutionState`, `IActionEffector`, `DisabledActionEffector`, `ActionEffectorFactory`, `PolicyGatedActionEffector`
- `Gate3/InputActionEffector.cs` (358) — attuazione input + proiezione schermo. Tipi: `IScreenProjection`, `UncalibratedScreenProjection`, `InputActionEffector`
- `Gate3/Gate3Observation.cs` (162), `Gate3ReplayProbe.cs` (151)

### Gate4/5/6

- `Gate4/Gate4Runtime.cs` (295) — progressione e obiettivi. Tipi: `GoalType`, `StrategyLifecycleStatus`, `SpecialistCardType`, `ResourceInventory`, `CharacterProgressionProfile`, `QuestDataProvenance`
- `Gate5/Gate5Runtime.cs` (615) — routing tra provider decisionali. Tipi: `ProviderType`, `ProviderRoutingPolicy`, `DecisionSuggestion`, `IDecisionProvider`, `HeuristicRuleProvider`, `SimulatedLocalInferenceProvider`
- `Gate5/Gate5Integration.cs` (361) — integrazione + runner
- `Gate6/Gate6Runtime.cs` (633) — esecuzione autorizzata e verifica. Tipi: `AuthorizedActionExecutor`, `ActionExecutionVerifier`, `SimulatedGameWorld`
- `Gate4/Gate4TestRunner.cs` (259)

### Safety / Security / Guard — fail-closed e crittografia

- `Safety/CapabilityAuthorizationGate.cs` (92) — **autorità fail-closed** su `ISafetyGate` (ADR-0003). Tipi: `CapabilityAuthorizationGate`
- `Safety/ActionTokenIssuer.cs` (153) — token monouso firmato per un atto. Tipi: `SafetyToken`, `ActionTokenIssuer`
- `Safety/ActionIntentDigest.cs` (189) — byte canonici firmati dal token (ADR-0020). Tipi: `ActionIntentDigest`
- `Safety/TrustBoundary.cs` (45) — livello di autonomia, solo in discesa. Tipi: `TrustBoundary`
- `Safety/GuardPolicyEngine.cs` (88) — politica Guard con rischio e vincoli. Tipi: `GuardEvaluationResult`, `GuardPolicyEngine`
- `Safety/RecoveryController.cs` (555) — cosa fare dopo una verifica fallita. Tipi: `RecoveryStrategy`, `RecoveryState`, `RecoveryHaltTransition`, `RecoveryController`
- `Safety/RuntimeSafetyController.cs` (194) — interruttori di sicurezza. Tipi: `SafetySwitch`, `SafetySwitchChange`, `RuntimeSafetyController`
- `Safety/ImmediateHalt.cs` (102) — arresto immediato operatore: disarma, poi abortisce. Tipi: `IImmediateHaltTarget`, `RuntimeImmediateHaltTarget`, `ImmediateHaltResult`, `ImmediateHalt`
- `Safety/RuntimeSafetyPolicy.cs` (15), `Safety/LiveInputAuthorization.cs` (22)
- `Security/NoiseProtocol.cs` (613) — **implementazione Noise**. Tipi: `NoiseCipherState`, `NoiseSymmetricState`, `NoiseHandshakeState`, `ReplayWindow`, `NoiseTransport`
- `Security/EphemeralSession.cs` (217) — X25519 e provisioning chiavi statiche. Tipi: `X25519Identity`, `EphemeralSession`, `StaticKeyProvisioning`
- `Security/RuntimeAuthorization.cs` (198) — principal e capacità. Tipi: `SecurityPrincipal`, `RuntimeCapability`, `AuthorizationDecision`, `IRuntimeAuthorizationPolicy`, `Gate1AuthorizationPolicy`
- `Security/ExecutionIntegrity.cs` (16), `Security/IPacketManipulator.cs` (24)
- `Security/NoiseProtocolChecks.cs` (229), `Security/EphemeralSessionTestRunner.cs` (198)
- `Guard/GuardAi.cs` (17) — adattatore Guard

### Perception — cattura schermo, HUD, proiezione

- `Perception/PerceptionPipeline.cs` (552) — pipeline percettiva. Tipi: `CaptureFrame`, `IFrameSource`, `SyntheticFrameSource`, `UnavailableFrameSource`, `PixelRect`, `RoiKind`, `RegionOfInterest`, `RoiSegmenter`
- `Perception/DxgiCapture.cs` (438) — Desktop Duplication triple-buffer. Tipi: `TripleFrameBuffer`, `CaptureUnavailable`, `DxgiDesktopDuplicationSource`, `TripleBufferedCapture`
- `Perception/DxgiInterop.cs` (261) — interop D3D11/DXGI
- `Perception/ScreenProjectionCalibration.cs` (697) e `ScreenProjectionAutoCalibrator.cs` (626) — **calibrazione mondo→schermo**
- `Perception/CalibratedScreenProjection.cs` (248), `ScreenProjectionProbe.cs` (228), `ScreenProjectionWatcher.cs` (162)
- `Perception/GeometryEpoch.cs` (361) — epoche di geometria finestra. Tipi: `GeometryShape`, `GeometryStamp`, `GeometryEpoch`
- `Perception/HudGlyphAtlas.cs` (292), `HudProbe.cs` (254), `HudBarFillReader.cs` (216), `HudGlyphExtractor.cs` (120), `HudGlyphTraining.cs` (114), `HudCropWriter.cs` (90) — lettura HUD
- `Perception/TargetRoiCalibration.cs` (253), `TargetStateComposer.cs` (112), `TargetFrameReader.cs` (74), `ScreenTargetFrameSource.cs` (79) — bersaglio
- `Perception/ScreenVitalReader.cs` (175), `ScreenDerivedVitals.cs` (110) — vitali da schermo
- `Perception/ClientWindowDpiProbe.cs` (218), `DpiAwarenessRegime.cs` (180), `ClientWindowLocator.cs` (128)
- `Perception/PerceptionContracts.cs` (28), `NullPerceptionProvider.cs` (7), `PerceptionPipelineTestRunner.cs` (304)

### Perception/Network — decodifica del protocollo NosTale

- `Perception/Network/ProtocolMap.cs` (493) — **mappa dichiarativa dei messaggi**. Tipi: `FieldSpec`, `MessageSpec`, `ProtocolMap`, `PlayerVitalsSpec`, `ConfigurableProtocolDecoder`
- `Perception/Network/GameTrafficObserver.cs` (650) — osservazione traffico e contratti decodificati. Tipi: `GameEventKind`, `EntitySighting`, `GameEvent`, `Aggressor`, `PlayerHit`, `TargetedEntity`, `PlayerTargetSelection`, `SkillReady`, `InventorySlotReading`, `ItemPickup`, `GroundItem`, `PlayerVitals`, `DecodedObservations`, `IGamePacketDecoder`, `NetworkObservationReport`, `GameTrafficObserver`
- `Perception/Network/NosTaleWorldProtocolDecoder.cs` (650) — dodici opcode; i cinque nuovi (`sr`, `ivn`, `get`, `drop`, `ct`) alimentano le post-condizioni. Tipi: `NosTaleWorldProtocolDecoder`
- `Perception/Network/NosTaleWorldDecoder.cs` (173)
- `Perception/Network/NetworkWorldFeed.cs` (283) — feed verso il world model. Tipi: `NetworkWorldFeed`, `TrafficRecorder`
- `Perception/Network/MessageFramer.cs` (143), `NetworkObservationContracts.cs` (71), `NetworkObservationSources.cs` (129), `ScopedGameTrafficFilter.cs` (62), `SyntheticProtocolDecoder.cs` (94)
- `Perception/Network/NetworkDecoderChecks.cs` (304), `NetworkObservationTestRunner.cs` (245)

### LiveIntegration — processo reale, memoria, cattura pacchetti

- `LiveIntegration/NosTaleClientLayout.cs` (819) — **layout del client e firme di memoria**. Tipi: `SignatureByte`, `NosTaleClientLayout`, `PlayerObjectReading`, `MapEntityReading`, `MapEntityKind`
- `LiveIntegration/RealClientConnector.cs` (533) — baseline del client reale. Tipi: `ClientBaselineAvailability`, `ClientBaselineSnapshot`, `RealClientConnector`
- `LiveIntegration/ProcessMemoryReader.cs` (320), `MemoryScanProbe.cs` (255), `MemoryScanner.cs` (180), `PlayerObjectProbe.cs` (238), `ClientMemorySession.cs` (223)
- `LiveIntegration/GameplayProvider.cs` (837) — contratto di gameplay. Tipi: `GameplayObservation`, `IGameplayProvider`, `NetworkGameplayProvider`, `UnavailableGameplayProvider`
- `LiveIntegration/MemoryGameplayProvider.cs` (255), `TargetAwareGameplayProvider.cs` (96), `PositionAwareGameplayProvider.cs` (79), `MemoryMapWorldProvider.cs`, `ClientMapWorldSource.cs`, `NetworkWorldStateObserver.cs` (32)
- `LiveIntegration/ClientNetworkObserver.cs` (266) — connessioni TCP del client
- `LiveIntegration/Capture/` — `ScopedLiveCaptureBackend.cs` (286), `TcpStreamReassembler.cs` (228), `WorldChannelReplay.cs` (228), `CaptureFile.cs` (202), `ReassembledObservationSource.cs` (184), `NosTaleWorldFramer.cs` (171), `GameTrafficCaptureEngine.cs` (163), `CaptureAnalysis.cs` (161), `WinDivertPacketSource.cs` (153), `ProtocolMapFramer.cs` (148), `Ipv4TcpParser.cs` (134), `GameStreamFramer.cs` (88), `TcpSegment.cs` (48), `IPacketSource.cs` (47), `InMemoryPacketSource.cs` (42)

### LowLevel — attuazione input e autorità di sessione

- `LowLevel/SessionActuationAuthority.cs` (827) — **autorità di attuazione e integrità di processo**. Tipi: `IntegrityLevel`, `IProcessIntegrityReader`, `SessionAuthorityVerdict`, `SessionActuationAuthority`, `Win32ProcessIntegrityReader`
- `LowLevel/GatedInputBackend.cs` (476) — input con gate. Tipi: `InputRefusal`, `GatedInputBackend`, `RecordingInputBackend`
- `LowLevel/HumanInputMonitor.cs` (383) — rilevamento input umano. Tipi: `IHumanInputMonitor`, `HumanInputMonitor`, `NotWatchingHumanInput`
- `LowLevel/CommitPointValidator.cs` (355) — validazione del punto di commit. Tipi: `ICommitEnvironment`, `CommitRequest`, `CommitDecision`, `CommitPointValidator`, `Win32CommitEnvironment`
- `LowLevel/ActuationAuthority.cs` (148) — sotto quale autorità si emette un atto (ADR-0020). Tipi: `ActuationAuthorityKind`, `ActuationAuthority`
- `LowLevel/KeybindsCheck.cs` (183) — file tasti vs intenti del runtime. Tipi: `KeybindsCheckReport`, `KeybindCheckEntry`, `KeybindsCheck`
- `LowLevel/NosTaleDefaultKeyCatalog.cs` (96) — catalogo tasti di default NosTale. Tipi: `DefaultKeyClass`, `DefaultKeyDeclaration`, `NosTaleDefaultKeyCatalog`
- `LowLevel/InputEnvironmentProbe.cs` (324), `InputGuardsProbe.cs` (252), `InputAuthorityProbe.cs` (236), `Win32InputBackend.cs` (286), `KeybindMap.cs` (233), `ActuationScope.cs` (178), `VirtualKeys.cs` (108), `InputControlTestRunner.cs` (350)

### Navigation

- `Navigation/Pathfinding/NavigationPathfinding.cs` (806) — **A\* e ostacoli dinamici**. Tipi: `TileType`, `PathFailureReason`, `NavigationStatus`, `GridPoint`, `MapPortal`, `DynamicHazardZone`, `CalculatedPathResult`, `MapGridData`, `AStarPathfinder`, `PathSmoother`, `WorldMapPortalRouter`, `NavigationExecutionController`
- `Navigation/MapIdFinder.cs` (699) — individuazione dell'id mappa. Tipi: `MapIdAnchorKind`, `MapIdHit`, `MapIdAnchors`, `MapIdCandidates`, `MapIdFinder`
- `Navigation/TargetIdFinder.cs` (521) — dove il client tiene l'id dell'entità selezionata. Tipi: `TargetIdHit`, `TargetIdCandidates`, `TargetIdFinder`
- `Navigation/PathWalkController.cs` (520) — camminare un percorso e sapere quando smettere. Tipi: `WalkOutcome`, `WalkDecision`, `ReplanPolicy`, `PathWalkController`
- `Navigation/PathRevalidation.cs` (270) — ammissione del percorso e rivalidazione per segmento. Tipi: `PathAdmission`, `SegmentRevalidation`, `PathRevalidation`
- `Navigation/StepGuardChain.cs` (369) — guardie di un passo, in ordine di corto circuito. Tipi: `StepGuard`, `StepGuardState`, `StepGuardOutcome`, `StepRequest`, `StepAuthorization`, `StepGuardChain`
- `Navigation/MovementVerifier.cs` (267) — se il passo è avvenuto. Tipi: `MovementOutcome`, `PositionReading`, `MovementVerification`, `MovementVerifier`
- `Navigation/OccupancyFreshness.cs` (222) — freschezza dell'occupazione al momento dell'atto. Tipi: `OccupancyView`, `OccupancyVerdict`, `OccupancyFreshness`
- `Navigation/SingleStepCommand.cs` (462) — comando `--step`. Tipi: `SingleStepRun`, `SingleStepCommand`
- `Navigation/SingleStepExecutor.cs` (225) — un passo, dall'autorizzazione al verdetto. Tipi: `StepReport`, `SingleStepExecutor`
- `Navigation/MapGridExtractor.cs` (493), `MapGrid.cs` (350), `StaticGeometryLayer.cs` (268), `MapGridSetIdentity.cs` (239), `MapGridCheck.cs` (128), `IMapGridLoader.cs` (96), `BinaryMapGridLoader.cs` (78)

### AI / Decisione / Tattica / Autonomia

- `AI/Decision/UtilityDecisionEngine.cs` (248) — motore a utilità + loader regole. Tipi: `UtilityDecisionEngine`, `RuleFileEntry`, `RuleFileCondition`, `DecisionRuleLoader`
- `AI/Decision/UtilityDecisionContracts.cs` (176) — contratti di regola. Tipi: `DecisionContext`, `ConditionOperator`, `RuleCondition`, `DecisionRule`, `RuleSkipReason`, `SkippedRule`, `DecisionOutcome`
- `AI/Decision/UtilityRuleProvider.cs` (137), `DecisionEngineTestRunner.cs` (302)
- `AI/LocalInference/LocalAiInferenceEngine.cs` (151) — inferenza locale **senza autorità di esecuzione**. Tipi: `ModelQuantization`, `HardwareComputeDevice`, `LocalModelConfig`, `AiRecommendedIntent`, `AiInferenceResult`, `CapBacPromptSanitizer`
- `Tactical/TacticalRanking.cs` (17), `TacticalPlanner.cs` (25), `Simulation.cs` (25) — ranking/planner/simulatore deterministico
- `Autonomy/GoalStack.cs` (177) — cosa il runtime sta cercando e con quali vnum. Tipi: `Goal`, `GoalStack`
- `Autonomy/TargetEstablishment.cs` (153) — cosa è attaccabile e su quale evidenza. Tipi: `TargetEvidence`, `TargetVerdict`, `TargetEstablishment`
- `Autonomy/TargetSelector.cs` (280) — selezione bersaglio. Tipi: `SelectableEntity`, `TargetSelectionPolicy`, `TargetChoice`, `TargetSelector`
- `Learning/PredictionLedger.cs` (240) — calibrazione previsione/osservazione. Tipi: `Prediction`, `Observation`, `LearningOutcome`, `Calibration`, `PredictionLedger`
- `PlayAi/UtilityAi.cs` (16), `Humanizer/DeterministicHumanizer.cs` (123), `Humanizer/HumanizerContracts.cs` (17)

### Orchestrazione / Host / Hardware

- `Host/NosAiMasterRuntimeHost.cs` (718) — host master. Tipi: `MasterHostStatus`, `MasterSystemTelemetry`, `MasterTrustManager`, `MasterSafetyGate`, `EmbeddedControlCenterServer`
- `Orchestration/RuntimeComposition.cs` (106) — radice di composizione del grafo runtime. Tipi: `RuntimeComposition`, `RuntimeComponents`
- `Hardware/Autoscale/HardwareAutoscaleController.cs` (174) — **budget e soglie termiche**. Tipi: `RuntimePerformanceMode`, `HardwareTelemetrySnapshot`, `RuntimeBudgetParameters`, `HardwareAutoscaleController`
- `Hardware/LiveHardwareTelemetry.cs` (155), `HardwareProbe.cs` (61), `HardwareProfileStore.cs` (46), `HardwareAutoSettings.cs` (38), `AutoSetManager.cs` (20), `HardwareProfilePaths.cs` (10)
- `Capabilities/NosAiCapabilityKernel.cs` (86) — kernel di capacità. Tipi: `INavigationCapability`, `IEconomyCapability`, `NosAiCapabilityKernel`
- `Network/Gateway/ControlPanelGatewayEngine.cs` (269) — gateway verso il pannello + rate limiter
- `Storage/Infrastructure/StorageInfrastructure.cs` (719) — **percorsi, benchmark, backup su NOSAI-SSD**. Tipi: `StorageDirectoryKind`, `StorageDriveDescriptor`, `StorageBenchmarkResult`, `BackupSnapshotManifest`, `StoragePathResolver`
- `Observability/WorldReplayCommand.cs` (457) — CLI `--world-replay`. Tipi: `WorldReplayEntityRow`, `WorldReplayReport`, `WorldReplayCommand`
- `Observability/DecideReplayCommand.cs` (253) — CLI `--decide-replay`. Tipi: `DecideReplayCycleRow`, `DecideReplayReport`, `DecideReplayCommand`
- `Observability/ReferenceInfoCommand.cs` (87) — CLI `--reference-info`. Tipi: `ReferenceInfoCommand`
- `Observability/HaltDiagnosticsDumper.cs` (148) — dump su transizione a halt. Tipi: `HaltDiagnosticDump`, `HaltDiagnosticsDumper`
- `Observability/PipelineStageBoard.cs` (93) — ultimo esito per stage. Tipi: `StageOutcomeDump`, `PipelineStageBoard`, `CommitPointRefusalDump`, `SessionAuthorityDump`, `HaltDiagnosticsContext`
- `Observability/RuntimeLogger.cs` (98), `ModuleReachability.cs` (222), `Telemetry/TelemetryMetrics.cs` (35)
- `Operator/HaltCli.cs` (67) — `--halt` verso un runtime già in ascolto. Tipi: `HaltCli`
- `Operator/OperatorMenu.cs` (849) — menu operatore da console
- `WorldModel/WorldState.cs` (35), `Adapters/NosTaleGameAdapter.cs` (96), `Adapters/IGameAdapter.cs` (13)

### Dominio di gioco

- `GameData/GameReferenceLocator.cs` (143) — dove sta `reference.db` sul volume NOSAI-SSD, senza crearlo. Tipi: `GameReferenceLocation`, `GameReferenceLocator`
- `GameData/GameReferenceDatabase.cs` (657), `NosDataTable.cs` (424), `NosArchive.cs` (375), `ReferenceImporter.cs` (272) — archivi e tabelle di riferimento NosTale
- `Raids/Dodekatheon/DodekatheonRaidOrchestrator.cs` (193), `Raids/Orchestration/RaidOrchestration.cs` (130)
- `Economy/Inventory/InventoryEconomy.cs` (139) + runner (208)
- `Events/InstantBattle/InstantBattleAndEventEngine.cs` (73)
- `Miniland/Production/MinilandProductionEngine.cs` (131)

### Testing (infrastruttura di certificazione)

- `Testing/TestSuiteRunner.cs` (462), `TestEvidence.cs` (343), `TestConsoleService.cs` (254), `TestConsoleHtml.cs` (212), `TestEvidenceProtocol.cs` (188), `GateCertificationRunner.cs` (132), `CertificationSuites.cs` (95)

---

## 4. nosai/ — pacchetto Python (84 file `.py`)

Dopo il taglio R4 restano i pacchetti in `docs/INVENTARIO_PYTHON.md` che non sono `COPERTO`. Cancellati `nosai/perception/` e `nosai/security/`.

- `core/` — `contracts.py`, `orchestrator.py`, `safety.py`, `simulation.py`, `simulation_policy.py`, `tactical_ranking.py`, `world_model.py`, `coordinated_action_manager.py`, `data_classification.py`
- `runtime/` — `engine.py`, `agent_loop.py`, `closed_loop.py`, `orchestrator_bridge.py`, `policy.py`, `provider_router.py`, `trust.py`, `watchdog.py`, `hardware_watchdog.py`, `recovery.py`, `adaptive_throttling.py`, `context_slimming.py`, `session.py`, `session_protocol.py`, `state.py`, `timeouts.py`, `tools.py`, `resources.py`, `memory.py`, `events.py`, `evaluation.py`, `integration.py`, `contracts.py`, `hardware.py`
- `network/` — `wire_protocol.py`, `session_cipher.py`, `session_transcript.py`, `session_auth.py`, `crypto_auth.py`
- `tactical/` — `search.py`, `combat_engine.py`, `stochastic.py`, `scheduling.py`, `threat.py`, `action_model.py`, `play_ai.py`
- `phone/` — `adb.py`, `build.py`, `deploy.py`, `enroll.py`, `guard_client.py`, `onboarding_engine.py`, `provisioning.py`
- `guard/` — `protocol.py`, `runtime.py`; `bringup/` — `guard_server.py`, `protocol.py`
- `storage/` — `volume.py`, `paths.py`, `sqlite_policy.py`, `health.py`; `persistence/sqlite_logger.py`
- `dashboard/server.py`, `party/{partner,pet}.py`, `miniland/automation.py`, `ai/rule_based.py`, `telemetry/advanced.py`, `testing/evidence.py`

---

## 5. docs/ — dove sta scritto cosa

**Architettura e regole**: `ARCHITETTURA.md`, `NOSAI_ARCHITECTURE_BASELINE.md`, `SOURCE_OF_TRUTH.md`, `REGOLE_PROGETTO.md`, `GLOSSARIO.md`, `REQUISITI.md`, `METADATI_PROGETTO.md`

**Gate**: `GATE1_CHECKLIST.md`, `GATE1_COMPONENT_MAP.md`, `GATE3_PIPELINE.md`, `GATE4_PROGRESSION.md`, `CATALOGO_AZIONI_E_POSTCONDIZIONI.md`, `CERTIFICAZIONI/gate1.md`

**Controllo personaggio**: `CONTROLLO_PERSONAGGIO_ARCHITETTURA.md`, `CONTROLLO_PERSONAGGIO_ATTUAZIONE.md`, `CONTROLLO_PERSONAGGIO_ROADMAP.md`, `TASTI_E_BERSAGLIO.md`

**Osservazione dalla memoria del client**: `MAPPA_MEMORIA_CLIENT_CANDIDATI.md`, `SPEC_ESTENSIONE_LAYOUT_MEMORIA.md`, `SPEC_SHIM_NATIVO_X86.md` (non approvata)

**Sicurezza e rete**: `CRITTOGRAFIA_NOISE_E_CHIAVI_EFFIMERE.md`, `INTEGRAZIONE_RSA_SESSION_AUTH.md`, `SICUREZZA.md`, `PROTOCOLLO_NOSTALE.md`, `WIFI_BRINGUP.md`

**Runtime e infrastruttura**: `PERSISTENZA_SQLITE_E_SHARED_MEMORY.md`, `HARDWARE_BASELINE_AND_AUTOSCALE.md`, `RECOVERY_WATCHDOG.md`, `EXTERNAL_SSD_DEPLOYMENT.md`, `DASHBOARD.md`, `PROGRESSION_ENGINE_SPEC.md`

**Ordine dei lavori**: `PIANO_CAPACITA.md` — quale capacità si costruisce, in che ordine, e chi la fa · `PIANO_DI_RIORDINO.md` — quale disordine si toglie, senza aggiungere capacità

**Processo**: `BUILD_TEST_RELEASE.md`, `TESTING.md`, `TEST_RIMANDATI.md`, `RELEASE_CHECKLIST.md`, `GIT_WORKFLOW.md`, `CONTRIBUTING.md`, `SESSIONI_CURSOR.md`, `AGENT_EXECUTION_CHECKLIST.md`, `AGENT_DEVELOPMENT_ENVIRONMENT.md`, `INVENTARIO_PYTHON.md`

**Stato e audit**: `STATO_IMPLEMENTAZIONE.md`, `AUDIT_TECNICO.md`, `AUDIT_SOTTOSISTEMI_2026-08-30.md`, `NOSAI_BASELINE_AUDIT_2026-08-30.md`, `CHANGELOG.md`, `ROADMAP.md`, `ROADMAP_ESECUTIVA.md`, `PIANO_OPERATIVO.md`

**ADR (21)**: 0001 architettura canonica · 0002 separazione dati reali/demo · 0003 autorità di sicurezza · 0004 verifica prima del rilascio · 0005 contratti versionati · 0006 canale telefono canonico · 0007 trasporto Wi-Fi · 0008 handshake mutuo · 0009 cifratura payload · 0010 custodia chiavi · 0011 sessione Guard singola · 0012 sorgente di osservazione · 0013 injection non adottata · 0014 l'operatore sceglie il data path · 0015 roadmap esecutiva canonica · 0016 pianificare su osservazione parziale · 0017 addestrare il lettore schermo dal filo · 0018 stabilire il bersaglio dallo schermo · 0019 canale di attuazione · 0020 un solo percorso di autorizzazione all'atto (*proposto*) · 0021 la memoria del client stabilisce il bersaglio (*proposto*)

---

## 6. Rotte rapide: intento → file

| Se devi toccare... | Apri |
|---|---|
| Frame binario a 12 byte, sequenze | `NosAi.Protocol/WireProtocol.cs`, `NosAi.Security/SequenceGuard.cs` |
| Handshake Noise / chiavi effimere | `Runtime/Security/NoiseProtocol.cs`, `EphemeralSession.cs`, `NosAi.Security/NoiseSession.cs` |
| Token CapBAC HMAC-SHA256 | `NosAi.Security/CapabilityToken.cs`, `CapabilityValidator.cs` |
| Safety Gate fail-closed | `Runtime/Safety/CapabilityAuthorizationGate.cs`, `ActionTokenIssuer.cs`, `RuntimeSafetyController.cs` |
| Token d'atto / digest firmato | `Runtime/Safety/ActionTokenIssuer.cs`, `ActionIntentDigest.cs`, `TrustBoundary.cs`, `GuardPolicyEngine.cs` |
| Arresto immediato / dump halt | `Runtime/Safety/ImmediateHalt.cs`, `Observability/HaltDiagnosticsDumper.cs`, `Operator/HaltCli.cs` |
| Resilienza e salute registro | `Runtime/Gate1/Gate1CanonicalSnapshot.cs` (`Gate1ResilienceView`), `NosAi.ControlPanel/ResilienceInspect.cs`, `EventLogInspect.cs` |
| Trust Tier | `Runtime/Contracts/TrustTier.cs`, `Host/NosAiMasterRuntimeHost.cs` |
| Obiettivo / cosa cercare | `Runtime/Autonomy/GoalStack.cs` |
| Bersaglio (evidenza, selezione, id in memoria) | `Runtime/Autonomy/TargetEstablishment.cs`, `TargetSelector.cs`, `Navigation/TargetIdFinder.cs` |
| Percorso critico Observe→Verify | `Runtime/Gate3/Gate3Runtime.cs`, `Gate3DecisionLoop.cs` |
| WorldState e delta | `Runtime/Gate2/Gate2Runtime.cs`, `Gate2DeltaSync.cs`, `Gate2WorldModel.cs` |
| Lettura memoria del client | `Runtime/LiveIntegration/NosTaleClientLayout.cs`, `ProcessMemoryReader.cs`, `PositionAwareGameplayProvider.cs` |
| Decodifica pacchetti NosTale | `Runtime/Perception/Network/ProtocolMap.cs`, `NosTaleWorldProtocolDecoder.cs` |
| Opcode post-condizione (`sr` `ivn` `get` `drop` `ct`) | `Runtime/Perception/Network/NosTaleWorldProtocolDecoder.cs`, `GameTrafficObserver.cs` |
| Cattura schermo / HUD | `Runtime/Perception/DxgiCapture.cs`, `HudProbe.cs`, `HudBarFillReader.cs` |
| Calibrazione mondo→schermo | `Runtime/Perception/ScreenProjectionCalibration.cs`, `ScreenProjectionAutoCalibrator.cs` |
| Invio input / autorità attuazione | `Runtime/LowLevel/SessionActuationAuthority.cs`, `ActuationAuthority.cs`, `GatedInputBackend.cs`, `Win32InputBackend.cs` |
| Tasti e keybind | `Runtime/LowLevel/KeybindsCheck.cs`, `NosTaleDefaultKeyCatalog.cs`, `KeybindMap.cs`, `docs/TASTI_E_BERSAGLIO.md` |
| Pathfinding e griglie mappa | `Runtime/Navigation/Pathfinding/NavigationPathfinding.cs`, `MapGrid.cs` |
| Cammino, guardie di passo, verifica movimento | `Runtime/Navigation/PathWalkController.cs`, `PathRevalidation.cs`, `StepGuardChain.cs`, `MovementVerifier.cs`, `OccupancyFreshness.cs`, `SingleStepExecutor.cs` |
| Ranking tattico | `Runtime/Tactical/TacticalRanking.cs`, `Gate3/Gate3Runtime.cs` (`TacticalRankingEngine`) |
| Post-condizione e verifica di un'azione | `docs/CATALOGO_AZIONI_E_POSTCONDIZIONI.md`, `Gate3/PostConditions.cs`, `Gate3/Gate3Runtime.cs` (`ActionExecutionVerifier`) |
| Catalogo di riferimento (vnum → nome) | `Runtime/GameData/GameReferenceLocator.cs`, `GameReferenceDatabase.cs` |
| SQLite WAL / volume NOSAI-SSD | `NosAi.Storage/SqliteEventJournal.cs`, `VolumeLocator.cs`, `Runtime/Storage/Infrastructure/StorageInfrastructure.cs` |
| Soglie termiche / autoscale | `Runtime/Hardware/Autoscale/HardwareAutoscaleController.cs` |
| Nodo Guard mobile | `NosAi.GuardClient/GuardAiClient.cs`, `NosAi.GuardAi.App/GuardConnectionService.cs` |
| Dashboard e pannello operatore | `Runtime/Gate1/Gate1BootstrapHost.cs`, `NosAi.ControlPanel/MainWindow.xaml.cs` |
| Suite di certificazione | `Runtime/Testing/TestSuiteRunner.cs`, `GateCertificationRunner.cs`, `Gate1/Gate1TestRunner.cs` |
