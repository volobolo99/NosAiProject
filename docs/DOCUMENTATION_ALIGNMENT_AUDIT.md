# Audit documentale e firme contrattuali

Snapshot storico: `3e4a8c42d75a73ccf5c6b739705e6293b059827e`.
Riconciliazione corrente: 2026-09-10. Questo documento controlla la coerenza
documentale e dei riferimenti; non certifica build, provider, client o hardware.

## Disallineamenti risolti

1. **Mappa delle fasi:** `ROADMAP_ESECUTIVA`, `CONTRACT_MAP` e
   `contracts/ledger.json` usano ora `product_phases` e i CID come chiavi
   canoniche; le etichette storiche dei gate restano solo come compatibilità.
2. **Tabelle dei contratti:** stato, titolo, firma, sorgente e test sono in colonne
   coerenti; il ledger JSON resta l'autorità machine-readable.
3. **Istruzioni agli agenti:** la precedenza tra coordinatore e implementatore è
   esplicitata prima dell'assegnazione; le modifiche devono seguire il protocollo
   in `.claude/CLAUDE.md`.
4. **Hardware:** Acer Nitro V16 AI, Ryzen 7 260, RTX 5060, 16 GB RAM e SSD 1024 GB
   sono registrati come profilo dichiarato dall'operatore, non come telemetria
   rilevata. AutoSet e le prove runtime restano necessari.
5. **MCP Chief:** le promozioni usano tre record di evidenza firmati e freschi
   (`tests`, `shadow`, `audit`) con executor indipendenti; i booleani
   `checks` non sono più una prova accettabile.
6. **Stato condiviso:** binding, proposte, lease, osservazioni e idempotenza hanno
   come autorità `data/mcp/state.sqlite3`; `role_bindings.json` è solo export
   compatibile e viene migrato con backup.
7. **Parser/index:** i quattro file che avevano diagnostica Tree-sitter sono stati
   corretti; `scripts/build_function_index.py` ora registra posizione, excerpt,
   versione grammar e supporta `--strict`.
8. **Verifica automatica:** `scripts/verify_mcp_contracts.py --strict` controlla
   JSON, link a sorgenti/test, firme risolte e presenza dei CID nella mappa; è
   eseguito dalla GitHub Action Linux.

## Firme risolte il 2026-09-10

| CID | Firma canonica | Sorgente | Prova |
|---|---|---|---|
| C-103 | `GameTrafficCaptureEngine(IPacketSource, Func<StreamDirection, IGameStreamFramer>? = null); Run(CancellationToken = default, TimeSpan? = null); Pump(CapturedPacket); Snapshot()` | `src/NosAi.Runtime/LiveIntegration/Capture/GameTrafficCaptureEngine.cs` | `tests/NosAi.Runtime.Tests/CaptureEngineTests.cs` |
| C-104 | `bool WireHeader.TryRead(ReadOnlySpan<byte>, out WireHeader, out string?)` | `src/NosAi.Protocol/WireProtocol.cs` | `HeartbeatPayloadTests.cs`, `DiscoveryTests.cs` |
| C-106 | `long CaptureFile.Record(IPacketSource, string, CancellationToken = default, TimeSpan? = null); CaptureFileSource Open(string)` | `src/NosAi.Runtime/LiveIntegration/Capture/CaptureFile.cs` | `DecideReplayAsOfCaptureTests.cs` |
| C-203 | `WorldModelSnapshot RunOnce(Gate1CanonicalSnapshot, DateTime)` | `src/NosAi.Runtime/WorldModel/Fusion/WorldModelFusionLoop.cs` | `tests/NosAi.Runtime.Tests/WorldModel/Fusion/WorldModelFusionLoopTests.cs` |
| C-401 | `SessionCipher.ForRuntime(ReadOnlySpan<byte>); ForPhone(ReadOnlySpan<byte>); SealFrameInto(Span<byte>, WireMessageType, uint, ReadOnlySpan<byte>); TryOpenFrame(ReadOnlySpan<byte>, ReadOnlySpan<byte>, out byte[], out string?)` | `src/NosAi.Protocol/SessionCipher.cs` | C# e Python session-cipher tests |

La firma risolta non equivale a comportamento verificato: la prova deve essere
eseguita nell'ambiente indicato e registrata secondo il ledger.

## Firme risolte il 2026-09-11

| CID | Firma canonica | Sorgente | Prova |
|---|---|---|---|
| C-204 | Canale 2: `RoleBindingConfiguration.Load(string path); RoleBinding? TryGetBinding(string employeeId)`. Canale 1: `DecisionTelemetryWriter.Append(Gate3LoopCycle cycle)`, agganciato in `Gate3DecisionLoop` come parametro opzionale `IDecisionTelemetrySink? telemetry = null` | `src/NosAi.Runtime/Configuration/RoleBindingConfiguration.cs`, `src/NosAi.Runtime/Observability/DecisionTelemetryWriter.cs` | `tests/NosAi.Runtime.Tests/RoleBindingConfigurationTests.cs`, `tests/NosAi.Runtime.Tests/DecisionTelemetryWriterTests.cs`, `Gate3DecisionLoopTests.cs` |

ADR-0030 aveva riformulato l'oggetto del contratto (due canali asimmetrici invece di
uno stato condiviso) prima che la firma potesse essere precisata: vedi
`docs/adr/ADR-0030-csharp-owns-game-state-python-governs-models.md`.

## Firme ancora da precisare

Restano **10** voci con `signature_status: UNRESOLVED`:

| CID | Decisione necessaria |
|---|---|
| C-105 | perimetro di hook memoria/DLL, versione e test |
| C-201 | dispatcher canonico, firma, errori e thread-safety |
| C-202 | perimetro pubblico della correlazione entità |
| C-301 | implementazione HTN canonica |
| C-302 | implementazione GOAP canonica |
| C-303 | orchestratore unico e sua firma |
| C-304 | FSM sostituisce o affianca Planner/Orchestrator |
| C-305 | implementazione canonica di recovery/reconnect |
| C-402 | corpus e harness di fuzzing |
| C-403 | superficie pubblica di `NosAi.Security` |

I dettagli operativi sono in [CONTRACT_SIGNATURE_TASKS.md](CONTRACT_SIGNATURE_TASKS.md).
C-003/004/005 sono proposte bloccate per assenza di stack nativo; C-404 è una
procedura di rilascio e non una funzione.

## Limiti dell'indice

`FUNCTION_INDEX.json` è una mappa sintattica: non è un call graph, non include
codice generato/runtime e non espande varianti del preprocessore. Una rigenerazione
completa deve usare la revisione Git dichiarata; la modalità `--strict` fallisce
se resta una diagnostica parser. Consultare [FUNCTION_INDEX.md](FUNCTION_INDEX.md).

## Esito

La documentazione e i riferimenti machine-readable sono riallineati per la fase di
sviluppo. Le verifiche ambientali e i test di comportamento restano attività
esplicite in [REMAINING_WORK.md](REMAINING_WORK.md), non vengono dedotti dalla
presenza dei file.
