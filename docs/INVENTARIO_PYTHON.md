# Inventario Python — `nosai/`

**Data:** 2 settembre 2026
**Ambito:** i 18 pacchetti sotto `nosai/` (`nosai` radice + 17 sottopacchetti).
**Regola di marcatura:** una sola parola per pacchetto. `VIVO` = invocato o citato
fuori da `nosai/` in `scripts/`, `.github/` o documentazione operativa.
`COPERTO` = esiste l'equivalente C# **e** i test C# lo coprono. `ORFANO` = nessuno
lo importa e non ha equivalente. `INCERTO` se `COPERTO` e `ORFANO` non si
decidono — restare è meglio che tagliare male.

`scripts/` e `.github/` non nominano nessun modulo. CI fa `compileall nosai`
sull'albero intero. I test Python stanno nella colonna Test, non in VIVO.
Import interni = altri pacchetti `nosai.*` (non se stesso).

Righe = file `.py` del pacchetto, newline.

| Pacchetto | Righe | Importato da `nosai/` | Fuori (`scripts/`, CI, docs) | Test Python | Equivalente C# | Marca |
|---|---:|---|---|---|---|---|
| `nosai` (radice) | 3 | namespace di tutti | CI `compileall nosai`; `pyproject.toml` | — | — | VIVO |
| `nosai.ai` | 26 | — | — | `tests/test_orchestrator.py`, `test_orchestrator_party.py`, `test_orchestrator_ranking.py` | `src/NosAi.Runtime/AI/Decision/UtilityRuleProvider.cs` | INCERTO |
| `nosai.bringup` | 122 | — | `docs/WIFI_BRINGUP.md` (`python -m nosai.bringup.guard_server`) | `tests/test_bringup_protocol.py` | — (il canale canonico è Gate 1; questo endpoint è dichiarato non canonico) | VIVO |
| `nosai.core` | 626 | `ai`, `dashboard`, `guard`, `perception`, `runtime`, `tactical` | — | `test_core_safety.py`, `test_simulation.py`, `test_tactical_simulation_ranking.py`, `test_orchestrator*.py`, `test_coordinated_action_manager.py`, `test_world_model_party.py`, `test_gate1_classification.py`, altri tattici | `src/NosAi.Runtime/Contracts/RuntimeContracts.cs`, `Safety/SafetyGate.cs` | INCERTO |
| `nosai.dashboard` | 235 | — | `docs/DASHBOARD.md`, `docs/GATE1_CHECKLIST.md`; entry `nosai-dashboard` | `tests/test_dashboard_runtime_link.py`, `test_gate1_classification.py` | — (il pannello C# è un altro processo) | VIVO |
| `nosai.guard` | 96 | `runtime`, `telemetry` | — | `tests/test_guard_protocol.py`, `test_advanced_telemetry.py` | `src/NosAi.Runtime/Safety/SafetyGate.cs` | INCERTO |
| `nosai.miniland` | 98 | — | — | `tests/test_miniland.py` | `src/NosAi.Runtime/Miniland/Production/MinilandProductionEngine.cs` | INCERTO |
| `nosai.network` | 650 | `phone` | — | `tests/network/test_wire_protocol.py`, `test_worldstate_delta.py`, `test_session_cipher.py`, `test_session_transcript.py`, `test_crypto_auth.py`, `test_guard_client_conformance.py` | `src/NosAi.Protocol` (framing NOSA) | INCERTO |
| `nosai.party` | 172 | `core` | — | `tests/test_party.py`, `test_world_model_party.py`, `test_orchestrator_party.py` | — | INCERTO |
| `nosai.perception` | 233 | — | — | `tests/test_perception_layers.py`, `test_perception_pipeline.py` | `src/NosAi.Runtime/Perception/PerceptionPipeline.cs` (`PerceptionPipelineTestRunner`) | COPERTO |
| `nosai.persistence` | 84 | — | `docs/PERSISTENZA_SQLITE_E_SHARED_MEMORY.md` | — | `src/NosAi.Storage/SqliteEventJournal.cs` | VIVO |
| `nosai.phone` | 1573 | — | `docs/GATE1_CHECKLIST.md`, `docs/INTEGRAZIONE_RSA_SESSION_AUTH.md`, `docs/adr/ADR-0007`, `ADR-0008`, `ADR-0010` | `tests/test_phone_adb.py`, `test_phone_build.py`, `test_phone_enroll.py`, `test_phone_discovery_defaults.py`, `test_onboarding_engine.py`, `test_guard_client_conformance.py` | `src/NosAi.GuardClient` (client sul filo, non ADB) | VIVO |
| `nosai.runtime` | 1380 | — | — | `tests/test_agent_runtime.py`, `test_agent_runtime_expansion.py`, `test_adaptive_throttling.py`, `test_runtime_optimizations.py`, `test_gate1_classification.py` | `src/NosAi.Runtime/Program.cs`, `Host/NosAiMasterRuntimeHost.cs` | INCERTO |
| `nosai.security` | 99 | — | — | `tests/test_ephemeral_session.py`, `tests/stress_test_cifratura.py` | `src/NosAi.Runtime/Security/EphemeralSession.cs` (`EphemeralSessionTestRunner`) | COPERTO |
| `nosai.storage` | 275 | `persistence` | — | `tests/storage/test_paths.py`, `test_sqlite_policy.py` | `src/NosAi.Runtime/Storage/Infrastructure/StorageInfrastructure.cs` | INCERTO |
| `nosai.tactical` | 2337 | — | — | `tests/test_tactical_search.py`, `test_tactical_stochastic.py`, `test_tactical_scheduling.py`, `test_tactical_threat.py`, `test_stochastic_combat_engine.py` | `src/NosAi.Runtime/Tactical/Simulation.cs` | INCERTO |
| `nosai.telemetry` | 74 | — | — | `tests/test_advanced_telemetry.py` | `src/NosAi.Runtime/Telemetry/TelemetryMetrics.cs` | INCERTO |
| `nosai.testing` | 67 | — | — | `tests/storage/test_sqlite_policy.py` | `src/NosAi.Runtime/Testing/TestEvidenceProtocol.cs` | INCERTO |

## Perché due soli COPERTO

`perception` e `security` hanno un file C# che si dichiara (o si comporta come)
la stessa superficie, e una suite C# che la esegue. Nessun pacchetto VIVO li
importa.

`core`, `network`, `storage`, `party`, `guard` hanno equivalenti parziali ma
restano dipendenze di qualcosa di VIVO o di un prototipo senza controparte
completa: tagliarli romperebbe `dashboard` / `phone` / `persistence` senza
toccare `src/`, che questa sessione non può fare.

`tactical` (MCTS / matrici stocastiche), `miniland` (pesca vs stazioni di
produzione), `ai` (regole vs utility), `telemetry` (record C# senza test C# sul
collettore), `runtime` Python (loop agente vs host C#): **INCERTO**.

Nessun ORFANO: ogni pacchetto è importato da `nosai/`, dai test, o dalla
documentazione.

## Taglio

Cancellati solo i COPERTO e i test che li coprono. ORFANO e INCERTO restano.
