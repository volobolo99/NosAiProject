# Roadmap Esecutiva — NosAi 1.0 Beta

volobolo99/NosAiProject · .NET 8 · Windows 11 · volume NOSAI\-SSD · SQLite WAL · destinazione repository: `docs/ROADMAP_ESECUTIVA.md` · revisione 31 agosto 2026

Table of Contents

## 1\. Regole di ingaggio

### 1\.1 Percorso critico deterministico

L'ordine degli stage è fisso, totale e non riordinabile. Ogni Gate integra uno o più stage **reali** e non può essere dichiarato chiuso finché lo stage precedente non è certificato.

```
Observe -> WorldState -> Simulation -> Ranking -> Orchestrator
        -> Planner -> Guard -> Trust -> Safety -> Execute -> Verify
```

### 1\.2 Invarianti non negoziabili

Validi su tutti i Gate, verificati automaticamente in CI a partire dal Gate 1.

| ID | Invariante | Punto di enforcement |
| --- | --- | --- |
| INV\-01 | Nessun LLM, modulo euristico o stocastico ha autorità di esecuzione diretta | `ISafetyGate` \+ `ICapabilityValidator` |
| INV\-02 | Il percorso critico è deterministico e ordinato | `PipelineStage` \+ `StageOrderValidator` |
| INV\-03 | Il Safety Gate è fail\-closed: timeout o anomalia \= blocco | `SafetyGate` budget 5 ms |
| INV\-04 | Nessun mock, stub o dato sintetico sul percorso critico | Analyzer `NOSAI0001` \+ gate di CI |
| INV\-05 | Storage su volume `NOSAI-SSD`, `journal_mode=WAL`, `synchronous=FULL`, `busy_timeout=5000` | `SqliteJournalOptions` |
| INV\-06 | Il Trust Tier non è mai incrementato autonomamente | `ITrustLedger.TryPromoteAsync` \+ firma Ed25519 umana |
| INV\-07 | Zero\-allocation sul percorso critico | `ArrayPool<T>`, `Span<T>`, `struct Pack = 1`, gate BenchmarkDotNet |
| INV\-08 | GPU ≥ 80 °C o rete \> 2000 ms → fail\-closed \+ backup SQLite | `IWatchdog` |

### 1\.3 Grafo delle dipendenze fra progetti

Regola dura per Cursor: **nessun riferimento inverso, nessuna dipendenza circolare**. `NosAi.Core` non referenzia nulla.

| Progetto | Referenzia | Contiene |
| --- | --- | --- |
| `NosAi.Core` | — | Contratti pipeline, `WorldState`, ragionamento, Safety Gate |
| `NosAi.Perception` | `Core` | Sensori fisici, cattura frame, Kalman 2D, termica |
| `NosAi.Adapter` | `Core` | Attach al processo NosTale reale, input sink |
| `NosAi.Security` | `Core` | CapBAC, Noise, `SequenceGuard`, Guard node, Trust ledger |
| `NosAi.Storage` | `Core` | Journal SQLite WAL, replay, trust ledger store |
| `NosAi.Host` | tutti | Composition root, watchdog runtime, dashboard |

### 1\.4 Definition of Done comune a ogni Gate

Un Gate è chiuso solo quando tutte e otto le voci sono verde. Nessuna eccezione, nessuna chiusura parziale.

1. `dotnet build -c Release` senza warning (`TreatWarningsAsErrors=true`).
2. Tutti i test del Gate verdi: `dotnet test --filter "Category=Gate<N>"`.
3. Analyzer `NOSAI0001` (divieto mock sul percorso critico) senza violazioni.
4. Benchmark di allocazione: 0 byte allocati sul percorso critico per 10.000 cicli.
5. Latenza p99 dello stage entro il budget dichiarato nel Gate.
6. Journal SQLite integro: catena hash verificata end\-to\-end.
7. Test negativi (fail\-closed) eseguiti e documentati.
8. Validazione fisica human\-in\-the\-loop firmata dall'operatore nel registro `docs/CERTIFICAZIONI/gate<N>.md`.

### 1\.5 Riepilogo dei Gate

| Gate | Titolo | Stage integrati | Budget p99 |
| --- | --- | --- | --- |
| 1 | Spina dorsale fisica | trasporto, storage, sicurezza di canale | 25 ms handshake |
| 2 | Percezione deterministica | Observe → WorldState | 8 ms |
| 3 | Simulazione e ranking | Simulation → Ranking | 12 ms |
| 4 | Orchestrazione e pianificazione | Orchestrator → Planner | 6 ms |
| 5 | Guard AI mobile e Trust | Guard → Trust | 400 ms Guard |
| 6 | Safety Gate e Watchdog | Safety | 5 ms |
| 7 | Esecuzione e verifica | Execute → Verify | 15 ms |
| 8 | Certificazione 1.0 Beta | ciclo completo | 50 ms end\-to\-end |

* * *

## 2\. Gate 1 — Spina dorsale fisica: PC ↔ NosTale ↔ Mobile ↔ Dashboard

### 2\.1 Obiettivo tecnico

Stabilire il canale end\-to\-end **reale** su cui poggeranno tutti i Gate successivi: attach al processo NosTale in esecuzione, protocollo binario a 12 byte con `SequenceGuard`, sessione Noise verso il nodo mobile, autorizzazione CapBAC HMAC\-SHA256, journal SQLite WAL su `NOSAI-SSD`, dashboard di telemetria live. Nessuna decisione, nessuna azione: solo trasporto, autorizzazione e persistenza, tutti fisici.

### 2\.2 Contratti e strutture dati

```csharp
// NosAi.Core
namespace NosAi.Core;

public enum PipelineStage : byte
{
    Observe = 0, WorldState = 1, Simulation = 2, Ranking = 3, Orchestrator = 4,
    Planner = 5, Guard = 6, Trust = 7, Safety = 8, Execute = 9, Verify = 10
}

public readonly record struct StageResult(PipelineStage Stage, bool Ok, long ElapsedTicks, FaultCode Fault);

public enum FaultCode : ushort { None = 0, Timeout = 1, ScopeDenied = 2, Replay = 3, Thermal = 4, Network = 5, Journal = 6 }

public interface IMonotonicClock
{
    long Ticks { get; }
    long UnixMillis { get; }
}

public interface IPipelineStage<TIn, TOut>
{
    PipelineStage Stage { get; }
    bool TryExecute(in TIn input, out TOut output, out FaultCode fault);
}
```

```csharp
// NosAi.Adapter
namespace NosAi.Adapter;

public interface IGameProcessAdapter : IDisposable
{
    int ProcessId { get; }
    bool IsAttached { get; }
    bool TryAttach(in ProcessAttachOptions options, out FaultCode fault);
    int ReadRegion(nuint address, Span<byte> destination);
    WindowGeometry Geometry { get; }
}

public readonly record struct ProcessAttachOptions(
    string ProcessName,
    string ExpectedModule,
    string ModuleSha256,
    int TimeoutMs);

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct WindowGeometry
{
    public readonly int X, Y, Width, Height;
}
```

```csharp
// NosAi.Security
namespace NosAi.Security;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 12)]
public readonly struct NosFrameHeader
{
    public readonly byte Version;    // offset 0
    public readonly byte OpCode;     // offset 1
    public readonly ushort Length;   // offset 2..3   payload length, big-endian
    public readonly uint Sequence;   // offset 4..7   monotono, big-endian
    public readonly uint Tag;        // offset 8..11  HMAC-SHA256 troncato a 32 bit
}

public readonly record struct CapabilityToken(
    ulong SubjectId,
    uint Scope,
    long NotBeforeUnixMs,
    long NotAfterUnixMs,
    ReadOnlyMemory<byte> Mac);

public interface ICapabilityValidator
{
    CapabilityVerdict Validate(in CapabilityToken token, PipelineStage stage, uint requestedScope, long nowUnixMs);
}

public readonly record struct CapabilityVerdict(bool Granted, FaultCode Fault, uint EffectiveScope);

public sealed class SequenceGuard
{
    public SequenceGuard(int windowBits = 1024);
    public bool TryAccept(uint sequence);
    public uint HighWaterMark { get; }
}

public interface INoiseSession
{
    NoiseHandshakeState State { get; }
    int WriteMessage(ReadOnlySpan<byte> payload, Span<byte> destination);
    int ReadMessage(ReadOnlySpan<byte> message, Span<byte> destination);
    void Rekey();
}

public enum NoiseHandshakeState : byte { Idle = 0, SentE = 1, SentEe = 2, Transport = 3, Failed = 4 }
```

```csharp
// NosAi.Storage
namespace NosAi.Storage;

public interface IEventJournal : IAsyncDisposable
{
    long Append(in JournalRecord record);
    IAsyncEnumerable<JournalRecord> ReplayAsync(long fromSequence, CancellationToken ct);
    bool VerifyChain(long fromSequence, out long firstBrokenSequence);
}

public readonly record struct JournalRecord(
    long Sequence,
    long UnixMillis,
    PipelineStage Stage,
    ReadOnlyMemory<byte> Payload,
    ReadOnlyMemory<byte> ChainHash);

public sealed record SqliteJournalOptions(string VolumeLabel = "NOSAI-SSD", string FileName = "nosai.db")
{
    public string JournalMode => "WAL";
    public string Synchronous => "FULL";
    public int BusyTimeoutMs => 5000;
}
```

```csharp
// NosAi.Host
namespace NosAi.Host;

public sealed class NosAiHost : IAsyncDisposable
{
    public static NosAiHost Compose(HostOptions options);
    public ValueTask RunAsync(CancellationToken ct);
}

public sealed class DashboardHub
{
    public void Publish(in TelemetryFrame frame);
}
```

### 2\.3 Compito di Claude

- **Layout binario 12 byte.** Ordinamento big\-endian su tutti i campi multi\-byte. `Version` fissa a `0x01`; `OpCode` come enum chiuso; `Length` limita il payload a 4096 byte (valori superiori \= frame scartato prima di qualunque allocazione).
- **Tag HMAC.** `Tag = primi 4 byte di HMAC-SHA256(K_session, Version ‖ OpCode ‖ Length ‖ Sequence ‖ Payload)`. Confronto a tempo costante (`CryptographicOperations.FixedTimeEquals`). Il troncamento a 32 bit è accettabile solo perché `SequenceGuard` impedisce il brute\-force per replay: documentare il vincolo.
- **SequenceGuard.** Finestra scorrevole di 1024 bit su `Span<ulong>` di 16 elementi. Regole: `seq > HighWaterMark` → shift della finestra e accettazione; `seq ≤ HighWaterMark - 1024` → rifiuto (troppo vecchio); bit già impostato → rifiuto (replay). Nessuna allocazione, nessun lock: singolo writer per sessione.
- **Handshake Noise.** Pattern `Noise_XX_25519_ChaChaPoly_SHA256`, tre messaggi. Derivazione chiavi via HKDF\-SHA256. Rekey obbligatorio ogni 2^20 messaggi o 15 minuti, quello che arriva prima. Stato `Failed` è terminale: nessun tentativo automatico di ripresa sulla stessa sessione.
- **CapBAC.** `Mac = HMAC-SHA256(K_root, SubjectId ‖ Scope ‖ NotBefore ‖ NotAfter)` con serializzazione canonica big\-endian a lunghezza fissa. Delega per attenuazione: `Scope_figlio ⊆ Scope_padre` verificato con `(child & ~parent) == 0`. Tolleranza di clock skew ±2000 ms; oltre, il token è rifiutato con `FaultCode.Timeout`.
- **Catena hash del journal.** `ChainHash_n = SHA256(ChainHash_{n-1} ‖ Sequence ‖ UnixMillis ‖ Stage ‖ Payload)`, con `ChainHash_0` \= SHA256 del genesis record contenente l'identificativo di sessione. Rende rilevabile qualunque manomissione o buco nel journal.

### 2\.4 Compito di Cursor

- **File da creare:** `src/NosAi.Core/{PipelineStage.cs, StageResult.cs, IMonotonicClock.cs, IPipelineStage.cs}`; `src/NosAi.Adapter/{IGameProcessAdapter.cs, ProcessAttachOptions.cs, Win32ProcessAdapter.cs}`; `src/NosAi.Security/{NosFrameHeader.cs, FrameCodec.cs, SequenceGuard.cs, CapabilityToken.cs, CapabilityValidator.cs, NoiseSession.cs}`; `src/NosAi.Storage/{IEventJournal.cs, SqliteEventJournal.cs, SqliteJournalOptions.cs, VolumeLocator.cs}`; `src/NosAi.Host/{NosAiHost.cs, DashboardHub.cs, Program.cs}`.
- **Configurazione `.csproj`\:** `Directory.Build.props` alla radice con `<TargetFramework>net8.0</TargetFramework>`, `<LangVersion>12</LangVersion>`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<InvariantGlobalization>true</InvariantGlobalization>`, `<TieredPGO>true</TieredPGO>`, `<ServerGarbageCollection>false</ServerGarbageCollection>`, `<ConcurrentGarbageCollection>false</ConcurrentGarbageCollection>`. `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` **solo** in `NosAi.Adapter`.
- **Zero\-allocation:** codifica e decodifica frame interamente su `Span<byte>` con `BinaryPrimitives.WriteUInt32BigEndian` / `ReadUInt32BigEndian`; buffer payload da `ArrayPool<byte>.Shared` con `try/finally` per il ritorno; nessun `async`/`await` nel codec; `[SkipLocalsInit]` su `FrameCodec`; nessuna `string` costruita sul percorso di trasporto (i codici di errore sono enum, non messaggi).
- **Storage:** `VolumeLocator` risolve il percorso dal *label* `NOSAI-SSD` tramite `DriveInfo.GetDrives()`, mai da una lettera di unità hardcoded. Connessione SQLite aperta con `PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000;` eseguiti in questo ordine subito dopo l'apertura, con verifica del valore restituito.
- **Analyzer:** progetto `tools/NosAi.Analyzers` con la regola `NOSAI0001` che segnala come errore ogni tipo il cui nome contiene `Mock`, `Fake`, `Stub`, `Dummy` o `Synthetic` referenziato da un assembly del percorso critico.

### 2\.5 Criteri di accettazione e test

**Comandi di verifica**

```
dotnet build -c Release
dotnet test --filter "Category=Gate1"
dotnet run --project src/NosAi.Host -- --gate 1 --attach NostaleClientX --verify-chain
sqlite3 <NOSAI-SSD>/nosai.db "PRAGMA journal_mode; PRAGMA synchronous; PRAGMA busy_timeout;"
```

**Esiti attesi**

| Verifica | Soglia |
| --- | --- |
| `PRAGMA` di SQLite | `wal` / `2` (FULL) / `5000` |
| Handshake Noise su nodo mobile reale | completato, p99 \< 25 ms su 100 tentativi |
| Frame corrotto (un bit invertito nel payload) | scartato, `FaultCode` registrato, nessuna eccezione |
| Frame replay (stessa `Sequence`) | rifiutato da `SequenceGuard`, contatore incrementato |
| Token CapBAC scaduto o con scope esteso | `Granted = false` |
| Allocazioni codec su 10.000 frame | 0 byte (`GC.GetAllocatedBytesForCurrentThread()` delta) |
| Catena hash del journal | valida su 10.000 record |

**Validazione fisica human\-in\-the\-loop.** Con NosTale realmente in esecuzione: l'operatore verifica sulla dashboard il conteggio frame crescente, sul telefono lo stato `Transport` della sessione Noise, e stacca fisicamente la rete del dispositivo mobile confermando che l'host registra la disconnessione e persiste su SQLite senza perdita di record. Firma su `docs/CERTIFICAZIONI/gate1.md`.

* * *

## 3\. Gate 2 — Percezione deterministica: Observe → WorldState

### 3\.1 Obiettivo tecnico

Acquisizione sensoriale reale dal client NosTale in esecuzione (cattura frame e letture strutturate via `IGameProcessAdapter`), tracciamento delle entità con filtro di Kalman 2D, fusione in un `WorldState` immutabile e versionato. Nessun dato sintetico: il banco di prova è il journal registrato nel Gate 1.

### 3\.2 Contratti e strutture dati

```csharp
// NosAi.Perception
namespace NosAi.Perception;

public interface IFrameSource : IDisposable
{
    bool TryAcquire(out FrameLease lease);
}

public readonly ref struct FrameLease
{
    public ReadOnlySpan<byte> Pixels { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public long CaptureTicks { get; }
    public void Dispose();
}

public interface ISensor
{
    SensorId Id { get; }
    bool TryRead(in FrameLease frame, Span<EntityObservation> destination, out int count);
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct EntityObservation
{
    public uint EntityId;
    public float X, Y;
    public float Confidence;
    public long Ticks;
}

public sealed class Kalman2DFilter
{
    public void Predict(float dt);
    public void Update(in Vector2 measurement);
    public Vector2 Position { get; }
    public Vector2 Velocity { get; }
    public float NormalizedInnovationSquared { get; }
    public float MahalanobisSquared(in Vector2 measurement);
}

public interface ITrackAssociator
{
    int Associate(ReadOnlySpan<EntityObservation> observations, Span<int> assignments);
}

public enum TrackPhase : byte { Tentative = 0, Confirmed = 1, Coasted = 2, Deleted = 3 }
```

```csharp
// NosAi.Core
namespace NosAi.Core;

public sealed record WorldState(
    long Version,
    long UnixMillis,
    ReadOnlyMemory<EntitySnapshot> Entities,
    SelfSnapshot Self,
    MapSnapshot Map)
{
    public ReadOnlySpan<byte> ComputeDigest(Span<byte> destination);
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct EntitySnapshot
{
    public readonly uint EntityId;
    public readonly float X, Y, Vx, Vy;
    public readonly float Confidence;
    public readonly TrackPhase Phase;
}

public interface IWorldStateBuilder
{
    WorldState Build(ReadOnlySpan<EntitySnapshot> fused, in SelfSnapshot self, in MapSnapshot map, long version);
}
```

### 3\.3 Compito di Claude

Modello a velocità costante, stato `x = [px, py, vx, vy]ᵀ`.

- **Transizione.** `F = [[1,0,dt,0],[0,1,0,dt],[0,0,1,0],[0,0,0,1]]`.
- **Rumore di processo** (accelerazione bianca discretizzata, intensità `q`):
  `Q = q · [[dt⁴/4, 0, dt³/2, 0], [0, dt⁴/4, 0, dt³/2], [dt³/2, 0, dt², 0], [0, dt³/2, 0, dt²]]`.
- **Osservazione.** `H = [[1,0,0,0],[0,1,0,0]]`, `R = diag(σ²ₓ, σ²ᵧ)` con `σ` calibrata sul rumore misurato del sensore, non stimata a occhio.
- **Predict.** `x ← F·x`; `P ← F·P·Fᵀ + Q`.
- **Update.** `y = z − H·x`; `S = H·P·Hᵀ + R`; `K = P·Hᵀ·S⁻¹`; `x ← x + K·y`; forma di Joseph per la covarianza: `P ← (I − K·H)·P·(I − K·H)ᵀ + K·R·Kᵀ` (stabilità numerica in singola precisione, obbligatoria).
- **Gating.** Distanza di Mahalanobis `d² = yᵀ·S⁻¹·y`; associazione ammessa solo se `d² ≤ 9.21` (χ² a 2 gradi di libertà, 99%).
- **Consistenza.** NIS medio su finestra di 50 campioni deve cadere in `[1.0, 3.0]`; fuori intervallo il filtro è mal calibrato e il Gate non si chiude.
- **Associazione deterministica.** Greedy sul costo di Mahalanobis crescente, con tie\-break lessicografico su `EntityId`\: due esecuzioni sugli stessi dati devono produrre lo stesso assegnamento, bit per bit.
- **Ciclo di vita della traccia.** `Tentative` → `Confirmed` con regola M/N \= 3 su 5 frame; `Confirmed` → `Coasted` dopo 1 frame senza misura, massimo 5 frame di coasting; poi `Deleted`. La confidenza decade linearmente durante il coasting.

### 3\.4 Compito di Cursor

- **File:** `src/NosAi.Perception/{IFrameSource.cs, FrameLease.cs, DesktopDuplicationFrameSource.cs, ISensor.cs, EntityObservation.cs, Kalman2DFilter.cs, GreedyTrackAssociator.cs, TrackTable.cs}`; `src/NosAi.Core/{WorldState.cs, EntitySnapshot.cs, WorldStateBuilder.cs}`.
- **Zero\-allocation:** matrici 4×4 e 4×2 come campi `float` inline nella classe del filtro (nessun `float[,]`, nessuna `Matrix4x4` allocata per chiamata); `stackalloc` per i buffer di associazione fino a 256 tracce, `ArrayPool<int>` oltre; buffer pixel da `ArrayPool<byte>.Shared` con lease `ref struct`; `System.Numerics.Vector2` per posizione e velocità; nessun LINQ, nessun `IEnumerable` nel loop di percezione; `TrackTable` come array preallocato con free\-list, mai `List<T>` che cresce.
- **`.csproj`\:** aggiungere `<ItemGroup>` con analyzer di allocazione (`ClrHeapAllocationAnalyzer`) su `NosAi.Perception`; abilitare `<Optimize>true</Optimize>` anche in Debug per i benchmark.
- **Benchmark:** progetto `bench/NosAi.Bench` con BenchmarkDotNet, `[MemoryDiagnoser]`, job `ShortRun` per la CI e `Default` per la certificazione.

### 3\.5 Criteri di accettazione e test

**Comandi**

```
dotnet test --filter "Category=Gate2"
dotnet run -c Release --project bench/NosAi.Bench -- --filter *Perception*
dotnet run --project src/NosAi.Host -- --gate 2 --replay <NOSAI-SSD>/nosai.db --from 0
```

**Esiti attesi**

| Verifica | Soglia |
| --- | --- |
| Latenza Observe → WorldState | p99 \< 8 ms |
| Allocazioni su 10.000 frame reali | 0 byte sul percorso critico |
| NIS medio su 50 campioni | ∈ \[1.0, 3.0\] |
| Determinismo dell'associazione | due repliche sullo stesso journal → digest `WorldState` identici |
| Traccia persa e recuperata | coasting ≤ 5 frame, nessun cambio di `EntityId` |
| Frame corrotto o parziale | scartato, `WorldState` non avanza di versione |

**Validazione fisica human\-in\-the\-loop.** Con NosTale in esecuzione, la dashboard mostra un overlay dei track sovrapposto alla finestra reale del gioco. L'operatore muove il personaggio e conferma visivamente che i marker seguono le entità senza salti di identità per almeno 5 minuti continuativi. Firma su `docs/CERTIFICAZIONI/gate2.md`.

* * *

## 4\. Gate 3 — Simulazione e ranking: Simulation → Ranking

### 4\.1 Obiettivo tecnico

Trasformare un `WorldState` in una lista ordinata di azioni candidate, tramite Active Inference (minimizzazione dell'Expected Free Energy), utilità multi\-attributo MAUT e bonus esplorativo UCB1. Lo stage produce **solo** un ranking: nessuna autorità di esecuzione, nessun effetto collaterale.

### 4\.2 Contratti e strutture dati

```csharp
// NosAi.Core
namespace NosAi.Core;

public readonly record struct ActionCandidate(ushort ActionId, uint TargetEntityId, float PriorProbability);

public interface ISimulator
{
    int Rollout(in WorldState state, ReadOnlySpan<ActionCandidate> candidates,
                Span<RolloutResult> results, int horizon, ulong seed);
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct RolloutResult
{
    public readonly ushort ActionId;
    public readonly float PragmaticValue;
    public readonly float EpistemicValue;
    public readonly float ExpectedFreeEnergy;
    public readonly float Risk;
}

public interface IActionRanker
{
    int Rank(ReadOnlySpan<RolloutResult> results, in MautWeights weights, Span<RankedAction> ranked);
}

public readonly record struct RankedAction(ushort ActionId, float Utility, float UpperConfidenceBound, byte Rank);

public sealed record MautWeights(float Survival, float Efficiency, float Stealth, float Progress)
{
    public bool IsNormalized { get; }
    public static MautWeights Normalized(float survival, float efficiency, float stealth, float progress);
}

public sealed class Ucb1Bandit
{
    public Ucb1Bandit(int arms, float explorationC = 1.0f);
    public float Score(int arm);
    public void Observe(int arm, float reward);
    public long TotalPulls { get; }
}

public sealed class ActiveInferenceEngine : ISimulator
{
    public ActiveInferenceEngine(GenerativeModel model, float precisionGamma = 4.0f);
}
```

### 4\.3 Compito di Claude

- **Expected Free Energy.** Per ogni politica `π`\:
  `G(π) = −E_q(o|π)[ ln p(o|C) ] − E_q(o|π)[ D_KL( q(s|o,π) ‖ q(s|π) ) ]`
  dove il primo termine è il **valore pragmatico** (aderenza alle preferenze `C` sugli esiti) e il secondo il **valore epistemico** (guadagno informativo atteso). Selezione: `π* = argmin_π G(π)`.
- **Posterior sulle politiche.** `q(π) = softmax(−γ · G(π))` con precisione `γ = 4.0` di default. Implementare il softmax sottraendo il massimo prima dell'esponenziale (stabilità numerica in `float`).
- **Normalizzazione MAUT.** `U(a) = Σᵢ wᵢ · uᵢ(a)` con `Σ wᵢ = 1` e `uᵢ ∈ [0,1]` per normalizzazione min\-max **sul batch corrente**, con clamp esplicito e gestione del caso degenere `max = min` → `uᵢ = 0.5` per tutti. Proprietà da garantire: monotonicità rispetto a ciascun attributo a parità degli altri.
- **UCB1.** `score(a) = Û(a) + c · sqrt( (2 · ln N) / nₐ )` con `c = 1.0`; se `nₐ = 0` lo score è `+∞` (esplorazione forzata una sola volta per braccio). `N` è il totale delle estrazioni.
- **Fusione dei tre segnali.** `FinalScore(a) = α·(1 − Ĝₙₒᵣₘ(a)) + β·U_MAUT(a) + κ·UcbBonus(a)` con `α + β = 1` (default `α = 0.4`, `β = 0.6`) e `κ = 0.15`. Vincolo duro: il bonus esplorativo può **riordinare** i candidati ma non può mai reintrodurre un candidato marcato `Risk ≥ RiskThreshold`, che viene eliminato prima del ranking.
- **Determinismo.** RNG `xoshiro256**` con seed esplicito passato a `Rollout`. Divieto assoluto di `Random.Shared`, `DateTime.Now`, `Guid.NewGuid()` e di qualunque iterazione su strutture con ordinamento non garantito sul percorso critico. Ordinamento finale stabile con tie\-break su `ActionId` crescente.

### 4\.4 Compito di Cursor

- **File:** `src/NosAi.Core/Reasoning/{ActionCandidate.cs, ISimulator.cs, RolloutResult.cs, ActiveInferenceEngine.cs, GenerativeModel.cs, IActionRanker.cs, MautRanker.cs, MautWeights.cs, Ucb1Bandit.cs, Xoshiro256.cs}`.
- **Zero\-allocation:** `stackalloc Span<RolloutResult>` per batch ≤ 256 candidati, `ArrayPool<RolloutResult>` oltre soglia; `readonly struct` per tutti i risultati; `MathF.Log`, `MathF.Sqrt`, `MathF.Exp` (mai `Math` in `double` sul percorso critico); `[MethodImpl(MethodImplOptions.AggressiveInlining)]` sulle funzioni di utilità; ordinamento in\-place con `Span<T>.Sort` e comparer `struct` (nessun delegate allocato).
- **`.csproj`\:** nessuna nuova dipendenza esterna in `NosAi.Core`; l'RNG è implementato in casa per garantire riproducibilità cross\-runtime.
- **Test infrastrutturali:** progetto `tests/NosAi.Core.Tests` con FsCheck per i property test di monotonicità MAUT.

### 4\.5 Criteri di accettazione e test

**Comandi**

```
dotnet test --filter "Category=Gate3"
dotnet run --project src/NosAi.Host -- --gate 3 --replay <NOSAI-SSD>/nosai.db --seed 0xC0FFEE --hash-output
dotnet run -c Release --project bench/NosAi.Bench -- --filter *Ranking*
```

**Esiti attesi**

| Verifica | Soglia |
| --- | --- |
| Determinismo bit\-exact | 1\.000 esecuzioni con stesso seed e stesso journal → SHA256 dell'output identico |
| Monotonicità MAUT | property test su 10.000 casi generati, 0 controesempi |
| Convergenza UCB1 | su bandit a ricompensa nota, regret sublineare in 5.000 pull |
| Latenza Simulation → Ranking | p99 \< 12 ms con 64 candidati e orizzonte 8 |
| Allocazioni | 0 byte su 10.000 cicli |
| Candidato con `Risk ≥ soglia` | assente dal ranking, indipendentemente dal bonus UCB1 |

**Validazione fisica human\-in\-the\-loop.** L'operatore osserva sulla dashboard il ranking prodotto in tempo reale durante una sessione NosTale reale, **senza esecuzione** (modalità dry\-run: lo stage Execute non è ancora integrato). Conferma che le prime tre azioni proposte siano plausibili in almeno 30 situazioni di gioco distinte. Firma su `docs/CERTIFICAZIONI/gate3.md`.

* * *

## 5\. Gate 4 — Orchestrazione e pianificazione: Orchestrator → Planner

### 5\.1 Obiettivo tecnico

Arbitrare fra obiettivi concorrenti, selezionare l'azione dal ranking del Gate 3 e trasformarla in una sequenza di `PlanStep` con scope richiesto e deadline. Il piano è una **proposta**\: non è eseguibile finché Guard, Trust e Safety non lo autorizzano.

### 5\.2 Contratti e strutture dati

```csharp
// NosAi.Core
namespace NosAi.Core;

public readonly record struct GoalId(ushort Value);

public enum GoalClass : byte { Safety = 0, Survival = 1, Objective = 2, Opportunistic = 3 }

public interface IOrchestrator
{
    OrchestrationDecision Decide(in WorldState state, ReadOnlySpan<RankedAction> ranked, long nowUnixMs);
}

public readonly record struct OrchestrationDecision(
    GoalId ActiveGoal,
    GoalClass Class,
    ushort SelectedActionId,
    float Confidence,
    OrchestrationReason Reason);

public enum OrchestrationReason : byte { Continuation = 0, Preemption = 1, HysteresisHold = 2, NoViableAction = 3 }

public sealed class GoalStack
{
    public GoalStack(int capacity = 8);
    public bool TryPush(GoalId goal, GoalClass cls, long nowUnixMs);
    public GoalId Active { get; }
    public long ActiveSinceUnixMs { get; }
}

public interface IPlanner
{
    int Plan(in OrchestrationDecision decision, in WorldState state, Span<PlanStep> steps, out FaultCode fault);
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct PlanStep
{
    public readonly ushort ActionId;
    public readonly uint TargetEntityId;
    public readonly ushort DelayMs;
    public readonly ushort TimeoutMs;
    public readonly uint RequiredScope;
}

public readonly record struct ActionIntent(
    ReadOnlyMemory<PlanStep> Steps,
    long DeadlineUnixMs,
    uint RequiredScope,
    TrustTier MinimumTier);
```

### 5\.3 Compito di Claude

- **Arbitraggio lessicografico.** Ordine di classe rigido: `Safety > Survival > Objective > Opportunistic`. Un goal di classe superiore prelaziona sempre; a parità di classe decide l'utilità del Gate 3.
- **Isteresi anti\-oscillazione.** Il goal attivo cambia solo se `U_nuovo > U_corrente · (1 + ε)` con `ε = 0.15`, **e** se il goal corrente è attivo da almeno 750 ms. Eccezione unica: la classe `Safety` prelaziona istantaneamente, ignorando l'isteresi.
- **Budget temporale deadline\-monotonic.** Budget di ciclo 50 ms. Il piano è valido solo se `Σ (DelayMs + TimeoutMs) ≤ (DeadlineUnixMs − nowUnixMs) · 0.8`\: il 20% è margine riservato a Guard, Trust, Safety ed Execute. Piano oltre budget → `FaultCode.Timeout`, nessun troncamento automatico.
- **Precondizioni e scope.** Ogni `PlanStep` dichiara il proprio `RequiredScope`. Lo scope dell'intento è l'OR di tutti gli step. Il piano è invalido — non "degradato" — se `(RequiredScope & ~TokenScope) != 0`.
- **Plan repair.** Alla prima post\-condizione fallita (Gate 7) il piano è abortito interamente. Divieto di re\-plan opportunistico nello stesso tick: il ciclo successivo riparte da `Observe`. Questo mantiene la pipeline aciclica e il comportamento riproducibile in replay.
- **Confine con i moduli non deterministici.** Un eventuale suggerimento LLM o euristico entra nel sistema **esclusivamente** come `ActionCandidate` con `PriorProbability` in ingresso al Gate 3. Non può creare `PlanStep`, non può alterare `RequiredScope`, non può scrivere sul `GoalStack`. Questo confine è codificato: `IPlanner` non ha alcuna dipendenza verso moduli advisory.

### 5\.4 Compito di Cursor

- **File:** `src/NosAi.Core/Planning/{IOrchestrator.cs, LexicographicOrchestrator.cs, GoalStack.cs, GoalId.cs, IPlanner.cs, DeadlinePlanner.cs, PlanStep.cs, ActionIntent.cs}`.
- **Zero\-allocation:** `GoalStack` su array fisso di 8 elementi, nessuna crescita dinamica; `Span<PlanStep>` fornito dal chiamante, il planner non alloca mai; nessun `async` in questi due stage; `ReadOnlyMemory<PlanStep>` costruito su segmento pooled con lease esplicito rilasciato dopo il Verify.
- **Architettura:** verificare con un test di architettura (NetArchTest) che `NosAi.Core.Planning` non referenzi alcun namespace advisory e che nessun tipo esterno implementi `IPlanner`.

### 5\.5 Criteri di accettazione e test

**Comandi**

```
dotnet test --filter "Category=Gate4"
dotnet run --project src/NosAi.Host -- --gate 4 --replay <NOSAI-SSD>/nosai.db --dry-run
```

**Esiti attesi**

| Verifica | Soglia |
| --- | --- |
| Nessun `ActionIntent` con scope oltre il token | property test, 0 controesempi su 10.000 casi |
| Anti\-oscillazione | su traccia registrata di 10 minuti, ≤ 1 cambio di goal ogni 750 ms |
| Prelazione `Safety` | latenza di prelazione \< 1 tick, sempre |
| Budget di piano | 0 piani emessi oltre l'80% della deadline |
| Test di architettura | `IPlanner` implementato solo in `NosAi.Core.Planning` |
| Allocazioni | 0 byte su 10.000 cicli |

**Validazione fisica human\-in\-the\-loop.** In dry\-run su sessione NosTale reale, l'operatore verifica sulla dashboard la sequenza goal → piano per 30 minuti, confermando l'assenza di oscillazioni percepibili e la coerenza fra goal dichiarato e step proposti. Firma su `docs/CERTIFICAZIONI/gate4.md`.

* * *

## 6\. Gate 5 — Guard AI mobile e Trust Tier: Guard → Trust

### 6\.1 Obiettivo tecnico

Integrare il nodo Guard AI su dispositivo mobile **fisico** come secondo parere indipendente sull'`ActionIntent`, e il ledger del Trust Tier con avanzamento esclusivamente human\-in\-the\-loop. Il canale è quello certificato al Gate 1.

### 6\.2 Contratti e strutture dati

```csharp
// NosAi.Security
namespace NosAi.Security;

public enum TrustTier : byte { Quarantined = 0, Observer = 1, Assisted = 2, Supervised = 3, Autonomous = 4 }

public interface IGuardNode
{
    ValueTask<GuardVerdict> ReviewAsync(in ActionIntent intent, ReadOnlyMemory<byte> worldDigest, CancellationToken ct);
    GuardNodeHealth Health { get; }
}

public readonly record struct GuardVerdict(
    bool Approved,
    byte Confidence,
    GuardReason Reason,
    long LatencyMs,
    ReadOnlyMemory<byte> Signature);

public enum GuardReason : byte { Ok = 0, ScopeMismatch = 1, AnomalousPattern = 2, RateExceeded = 3, Timeout = 4, Unreachable = 5 }

public readonly record struct GuardNodeHealth(bool Online, long LastSeenUnixMs, int ConsecutiveFailures, long RttEwmaMs);

public interface ITrustLedger
{
    TrustTier Current { get; }
    ValueTask<bool> TryPromoteAsync(TrustTier target, in HumanApproval approval, CancellationToken ct);
    ValueTask DemoteAsync(TrustTier target, DemotionReason reason, CancellationToken ct);
    bool VerifyIntegrity(out long firstBrokenSequence);
}

public readonly record struct HumanApproval(
    string OperatorId,
    long UnixMillis,
    ReadOnlyMemory<byte> ChallengeNonce,
    ReadOnlyMemory<byte> Ed25519Signature);

public enum DemotionReason : byte { GuardLatency = 0, GuardRejections = 1, WatchdogTrip = 2, VerifyFailures = 3, LedgerBroken = 4, OperatorRequest = 5 }
```

### 6\.3 Compito di Claude

- **Isolamento informativo del Guard.** Il nodo mobile riceve solo `ActionIntent` e il digest `SHA256` del `WorldState` canonicalizzato, mai i frame grezzi. Il Guard è un revisore indipendente, non una replica del pianificatore: condividere lo stato completo ne annullerebbe il valore come secondo parere.
- **Quorum per tier.** `Observer` e `Assisted`\: verdetto informativo, l'azione resta bloccata a valle. `Supervised`\: richiesto un verdetto `Approved` con `Confidence ≥ 160`. `Autonomous`\: richiesti 2 verdetti concordi su una finestra scorrevole di 3, con `Confidence ≥ 200`.
- **Timeout fail\-closed.** Budget Guard 400 ms. Alla scadenza il verdetto è **negativo implicito** con `GuardReason.Timeout`. Non esiste alcun percorso che produca `Approved = true` in assenza di una risposta firmata: l'assenza di risposta non è mai consenso.
- **Promozione.** `TryPromoteAsync` verifica la firma Ed25519 dell'operatore su `(byte)target ‖ ChallengeNonce ‖ UnixMillis`. Il nonce è emesso dall'host, valido 120 s, monouso, tracciato in una finestra anti\-riuso. Nessuna promozione di più di un tier per approvazione. Nessun percorso di codice — controller adattivi inclusi — può scrivere un tier crescente senza passare da qui.
- **Demozione automatica e immediata.** `RttEwmaMs > 2000`, oppure 3 verdetti negativi consecutivi, oppure trip del watchdog, oppure catena del ledger rotta → `Quarantined` istantaneo. La demozione non richiede approvazione e non ha isteresi.
- **Ledger a catena.** `Hashₙ = SHA256(Hashₙ₋₁ ‖ (byte)tier ‖ (byte)reason ‖ OperatorId ‖ UnixMillis)`. Verifica integrale all'avvio del processo: catena rotta → avvio in `Quarantined` con log bloccante, mai avvio ottimistico.

### 6\.4 Compito di Cursor

- **File:** `src/NosAi.Security/Guard/{IGuardNode.cs, MobileGuardNode.cs, GuardVerdict.cs, GuardQuorum.cs}`; `src/NosAi.Security/Trust/{ITrustLedger.cs, TrustTier.cs, HumanApproval.cs, ChainedTrustLedger.cs, NonceRegistry.cs}`; `src/NosAi.Storage/{TrustLedgerStore.cs}`.
- **Trasporto:** riuso della `INoiseSession` del Gate 1, nessun nuovo canale. Backpressure con `Channel<T>` bounded capacità 32 e `BoundedChannelFullMode.DropWrite` \+ contatore di drop esposto in telemetria. Retry con backoff esponenziale (max 3 tentativi, 50/100/200 ms) **fuori** dal percorso critico: sul percorso critico scatta prima il timeout fail\-closed.
- **Storage:** tabella `trust_ledger(sequence INTEGER PRIMARY KEY, unix_ms INTEGER NOT NULL, tier INTEGER NOT NULL, reason INTEGER NOT NULL, operator_id TEXT NOT NULL, chain_hash BLOB NOT NULL)` con indice su `unix_ms`. Scritture in transazione singola, `synchronous=FULL` già garantito dalle pragma di Gate 1.
- **`.csproj`\:** aggiungere `NSec.Cryptography` (o `System.Security.Cryptography` con Ed25519 via `NSec`) al solo `NosAi.Security`; pinnare la versione esatta, nessun range flottante.

### 6\.5 Criteri di accettazione e test

**Comandi**

```
dotnet test --filter "Category=Gate5"
dotnet run --project src/NosAi.Host -- --gate 5 --guard-endpoint <mobile> --require-human-approval
dotnet run --project src/NosAi.Host -- --verify-trust-chain
```

**Esiti attesi**

| Verifica | Soglia |
| --- | --- |
| Test riflessivo sui percorsi di scrittura del tier | tutti passano da `TryPromoteAsync`, 0 eccezioni |
| Promozione con firma non valida o nonce riusato | rifiutata, tier invariato, evento a journal |
| Guard irraggiungibile | verdetto negativo entro 400 ms, azione bloccata |
| Latenza Guard \> 2000 ms | demozione a `Quarantined` entro 1 tick |
| Ledger manomesso (record alterato a mano) | avvio in `Quarantined`, `firstBrokenSequence` corretta |
| Quorum `Autonomous` con 1 solo verdetto | azione bloccata |

**Validazione fisica human\-in\-the\-loop.** Con il nodo Guard AI installato sul dispositivo mobile reale e la sessione NosTale attiva: (a) l'operatore firma una promozione da `Observer` ad `Assisted` e verifica la nuova entry nel ledger; (b) disattiva fisicamente il Wi\-Fi del telefono e cronometra il blocco del sistema, che deve avvenire entro 2000 ms; (c) tenta una promozione da un dispositivo non autorizzato e verifica il rifiuto. Firma su `docs/CERTIFICAZIONI/gate5.md`.

* * *

## 7\. Gate 6 — Safety Gate fail\-closed e Watchdog: Safety

### 7\.1 Obiettivo tecnico

Integrare l'unico punto del sistema che concede l'autorizzazione a eseguire, e il watchdog che lo forza in blocco su anomalia termica, di rete o di liveness. Da questo Gate in poi nessuna azione può raggiungere l'adapter senza un `ExecutionToken` emesso qui.

### 7\.2 Contratti e strutture dati

```csharp
// NosAi.Core
namespace NosAi.Core;

public interface ISafetyGate
{
    SafetyVerdict Evaluate(in ActionIntent intent, in SafetyContext context);
}

public readonly record struct SafetyVerdict(bool Allowed, SafetyRule ViolatedRule, long EvaluationTicks, ExecutionToken Token);

[Flags]
public enum SafetyRule : uint
{
    None = 0, ThermalLimit = 1, NetworkLatency = 2, TrustTierTooLow = 4, ScopeViolation = 8,
    RateLimit = 16, DeadmanTimeout = 32, JournalUnavailable = 64, GuardRejected = 128, LedgerBroken = 256
}

public readonly record struct SafetyContext(
    TrustTier Tier,
    uint GrantedScope,
    float GpuCelsius,
    long NetworkRttMs,
    bool JournalHealthy,
    bool GuardApproved,
    long NowUnixMs);

public interface IWatchdog
{
    WatchdogState State { get; }
    void Heartbeat(PipelineStage stage);
    event Action<WatchdogTrip> Tripped;
    void Reset(in HumanApproval approval);
}

public enum WatchdogState : byte { Closed = 0, Armed = 1, Open = 2, Tripped = 3 }

public readonly record struct WatchdogTrip(SafetyRule Rule, long UnixMillis, float Observed, float Threshold);
```

```csharp
// NosAi.Perception
namespace NosAi.Perception;

public interface IThermalSensor
{
    float GpuCelsius { get; }
    float CpuCelsius { get; }
    long SampleTicks { get; }
    bool IsHealthy { get; }
}
```

### 7\.3 Compito di Claude

- **Macchina a stati fail\-closed.** `Closed → Armed → Open → Tripped`. La transizione verso `Armed` richiede l'esito positivo di **tutte** le regole; `Open` autorizza un singolo ciclo e decade automaticamente. Da `Tripped` si esce solo con `Reset(in HumanApproval)`\: nessun recupero automatico, mai.
- **Il timeout è un esito, non un'eccezione.** Budget di valutazione 5 ms misurato con `Stopwatch.GetTimestamp()`. Superato il budget, il metodo **restituisce** `Allowed = false, ViolatedRule = DeadmanTimeout`. Nessun `try/catch` può produrre `Allowed = true`\: la struttura di ritorno è inizializzata a "negato" e viene resa positiva solo in coda a tutte le verifiche.
- **Isteresi termica.** Trip a `GpuCelsius ≥ 80.0`; rientro consentito solo sotto `72.0 °C` mantenuti per 30 s continuativi. Campionamento a 1 Hz, mediana mobile su 5 campioni per rifiutare gli spike del sensore. Sensore `IsHealthy == false` → trattato come sopra soglia (fail\-closed anche sul guasto della misura).
- **Rete.** RTT misurato sull'echo Noise del Gate 1, stima EWMA con `α = 0.2`. Trip a `RTT > 2000 ms` o a 3 heartbeat mancanti consecutivi. **Prima** di dichiarare `Tripped`, flush sincrono del journal SQLite: `synchronous=FULL` garantisce la durabilità del backup dello stato.
- **Deadman per stage.** Ogni stage batte il proprio heartbeat entro il budget dichiarato. Assenza di heartbeat oltre 250 ms su qualunque stage → trip globale, anche se tutte le altre regole sono verdi.
- **Ordine di valutazione fisso.** Le regole sono valutate in ordine di costo crescente e in cortocircuito: `LedgerBroken → TrustTierTooLow → ScopeViolation → GuardRejected → JournalUnavailable → ThermalLimit → NetworkLatency → RateLimit`. L'esito non dipende dall'ordine di arrivo dei campioni dei sensori: proprietà da verificare con fuzzing.
- **Emissione del token.** `ExecutionToken.SafetySignature = HMAC-SHA256(K_session, DeadlineUnixMs ‖ GrantedScope ‖ (byte)Tier ‖ IntentDigest)`. Il token è valido per un solo intento e per una sola finestra temporale.

### 7\.4 Compito di Cursor

- **File:** `src/NosAi.Core/Safety/{ISafetyGate.cs, SafetyGate.cs, SafetyRule.cs, SafetyContext.cs, SafetyVerdict.cs, IWatchdog.cs, DeadmanWatchdog.cs, RuleOrder.cs}`; `src/NosAi.Perception/Thermal/{IThermalSensor.cs, NvmlThermalSensor.cs, MedianFilter5.cs}`.
- **Vincoli sul percorso di valutazione:** single\-writer, nessun `async`/`await`, nessun `lock`, nessuna allocazione. Lo stato condiviso è letto da campi `volatile` su struct immutabili pubblicate atomicamente (pattern publish\-by\-reference). `[SkipLocalsInit]` su `SafetyGate`. `PeriodicTimer` per il campionamento sensori vive fuori dal percorso critico e pubblica snapshot.
- **`.csproj`\:** dipendenza NVML/`LibreHardwareMonitorLib` nel solo `NosAi.Perception`, versione pinnata. Nessuna dipendenza hardware in `NosAi.Core`\: il Gate riceve valori già campionati via `SafetyContext`.
- **Test di robustezza:** progetto `tests/NosAi.Safety.Tests` con un test che inietta un `throw` in **ogni** dipendenza del gate a turno e asserisce `Allowed == false` in tutti i casi.

### 7\.5 Criteri di accettazione e test

**Comandi**

```
dotnet test --filter "Category=Gate6"
dotnet run --project src/NosAi.Host -- --gate 6 --inject-fault thermal
dotnet run --project src/NosAi.Host -- --gate 6 --inject-fault network
dotnet run --project src/NosAi.Host -- --gate 6 --fuzz-sensor-order --iterations 100000
```

**Esiti attesi**

| Verifica | Soglia |
| --- | --- |
| Eccezione iniettata in qualunque dipendenza | `Allowed = false` in 100% dei casi |
| Ritardo iniettato \> 5 ms | `ViolatedRule = DeadmanTimeout`, nessuna eccezione propagata |
| GPU reale portata a ≥ 80 °C | trip entro 2 campioni (≤ 2 s), log e journal coerenti |
| Rientro termico | consentito solo dopo 30 s sotto 72 °C |
| Cavo di rete staccato | trip entro 2000 ms, journal flushato prima del `Tripped` |
| Fuzzing sull'ordine dei sensori | verdetto invariante, 0 divergenze su 100.000 permutazioni |
| Uscita da `Tripped` senza `HumanApproval` | impossibile, test dedicato |

**Validazione fisica human\-in\-the\-loop.** L'operatore esegue uno stress reale della GPU fino al superamento degli 80 °C e verifica il blocco a schermo e a journal; stacca fisicamente il cavo di rete e cronometra; infine tenta un reset senza firma e conferma il rifiuto. Firma su `docs/CERTIFICAZIONI/gate6.md`.

* * *

## 8\. Gate 7 — Esecuzione e verifica: Execute → Verify

### 8\.1 Obiettivo tecnico

Chiudere il ciclo: dispatch dell'input reale verso il processo NosTale sotto `ExecutionToken`, e verifica della post\-condizione osservabile sul `WorldState` successivo, con quarantena su divergenza. È il primo Gate in cui il sistema agisce fisicamente sul gioco.

### 8\.2 Contratti e strutture dati

```csharp
// NosAi.Adapter
namespace NosAi.Adapter;

public interface IInputSink
{
    bool TryDispatch(in PlanStep step, in ExecutionToken token, out ExecutionReceipt receipt, out FaultCode fault);
}

public readonly record struct ExecutionToken(
    long DeadlineUnixMs,
    uint GrantedScope,
    TrustTier Tier,
    ReadOnlyMemory<byte> IntentDigest,
    ReadOnlyMemory<byte> SafetySignature);

public readonly record struct ExecutionReceipt(
    ushort ActionId,
    long DispatchedTicks,
    long AcknowledgedTicks,
    bool Delivered,
    FaultCode Fault);
```

```csharp
// NosAi.Core
namespace NosAi.Core;

public interface IVerifier
{
    VerificationResult Verify(in ExecutionReceipt receipt, in WorldState before, in WorldState after);
}

public readonly record struct VerificationResult(bool PostConditionMet, float Divergence, VerificationAction Next);

public enum VerificationAction : byte { Continue = 0, Replan = 1, Quarantine = 2, HardStop = 3 }

public interface IPostCondition
{
    ushort ActionId { get; }
    float Divergence(in WorldState before, in WorldState after);
}
```

### 8\.3 Compito di Claude

- **Token obbligatorio.** `IInputSink.TryDispatch` verifica `SafetySignature` a tempo costante, controlla `DeadlineUnixMs > now` e `step.RequiredScope ⊆ token.GrantedScope`, e confronta `IntentDigest` con il digest dell'intento corrente. Fallita anche una sola verifica → `Delivered = false`. Non esiste alcun overload, flag di debug o percorso alternativo che consenta il dispatch senza token: proprietà verificata dal test di architettura.
- **Compensazione della latenza.** `t_target = t_dispatch + RTT_ewma / 2`, con `RTT_ewma` aggiornato con `α = 0.2`. Se `|t_effettivo − t_target| > 30 ms` lo step è **annullato**, non recuperato: un input fuori finestra è indistinguibile da un input errato.
- **Post\-condizione e divergenza.** Ogni `ActionId` dichiara un predicato osservabile su `WorldState`. `Divergence ∈ [0,1]` è la distanza normalizzata fra effetto atteso e osservato. Soglie: `< 0.15` → `Continue`; `< 0.40` → `Replan`; `< 0.70` → `Quarantine`; `≥ 0.70` → `HardStop`.
- **Effetto sul Trust.** 3 verifiche fallite entro 60 s → `DemoteAsync(Quarantined, VerifyFailures)`. Nessun esito di verifica, per quanto positivo, può produrre una promozione: la promozione resta esclusivamente umana (INV\-06).
- **Giornalazione obbligatoria.** `ExecutionReceipt` e `VerificationResult` sono appesi alla catena hash **prima** che il ciclo successivo inizi. Un journal non disponibile è già bloccante al Gate 6 (`SafetyRule.JournalUnavailable`): nessuna azione non giornalata è possibile per costruzione.

### 8\.4 Compito di Cursor

- **File:** `src/NosAi.Adapter/Input/{IInputSink.cs, Win32InputSink.cs, ExecutionToken.cs, ExecutionReceipt.cs, TokenVerifier.cs}`; `src/NosAi.Core/Verification/{IVerifier.cs, PostConditionVerifier.cs, IPostCondition.cs, PostConditionTable.cs}`.
- **Timing:** `Stopwatch.GetTimestamp()` per tutte le misure; divieto di `Thread.Sleep` e `Task.Delay` sul percorso di dispatch — usare `SpinWait` con soglia e timer ad alta risoluzione (`timeBeginPeriod(1)` incapsulato e ripristinato in `Dispose`).
- **Zero\-allocation:** nessuna allocazione nel dispatch; `PostConditionTable` come array indicizzato per `ActionId` popolato all'avvio, mai `Dictionary` sul percorso critico; buffer di digest da `stackalloc byte[32]`.
- **Journal:** batch di append con flush sincrono ai confini di ciclo, mai a metà di un intento.

### 8\.5 Criteri di accettazione e test

**Comandi**

```
dotnet test --filter "Category=Gate7"
dotnet run --project src/NosAi.Host -- --gate 7 --live --killswitch-armed
dotnet run --project src/NosAi.Host -- --verify-chain --expect-pairs
```

**Esiti attesi**

| Verifica | Soglia |
| --- | --- |
| Token contraffatto, scaduto o con scope eccedente | dispatch rifiutato, 100% dei casi |
| Dispatch senza token | impossibile (test di architettura \+ test runtime) |
| Latenza Execute → Verify | p99 \< 15 ms |
| Journal dopo N azioni | N receipt \+ N verdetti, catena hash valida |
| 3 verifiche fallite in 60 s | `TrustTier = Quarantined` |
| Verifiche riuscite consecutive | tier invariato (nessuna auto\-promozione) |
| Allocazioni sul dispatch | 0 byte |

**Validazione fisica human\-in\-the\-loop.** Sessione NosTale reale con operatore presente e mano sul kill\-switch: 200 azioni consecutive con verifica, in tier `Supervised`. L'operatore conferma che ogni azione osservata a schermo corrisponde a un receipt e a un verdetto nel journal, e attiva il kill\-switch almeno una volta verificando l'arresto entro un tick. Firma su `docs/CERTIFICAZIONI/gate7.md`.

* * *

## 9\. Gate 8 — Consolidamento, osservabilità e certificazione 1.0 Beta

### 9\.1 Obiettivo tecnico

Chiudere la release: telemetria completa su tutti gli stage, replay deterministico del journal, soak test di 24 ore su hardware reale, packaging e checklist di certificazione firmata.

### 9\.2 Contratti e strutture dati

```csharp
// NosAi.Storage
namespace NosAi.Storage;

public interface IReplayEngine
{
    IAsyncEnumerable<StageResult> ReplayAsync(long fromSequence, long toSequence, CancellationToken ct);
    ValueTask<ReplayDivergence> CompareAsync(long fromSequence, long toSequence, CancellationToken ct);
}

public sealed record ReplayDivergence(bool IsIdentical, long FirstDivergentSequence, PipelineStage Stage, string Detail);
```

```csharp
// NosAi.Host
namespace NosAi.Host;

public interface ITelemetrySink
{
    void Record(PipelineStage stage, long elapsedTicks, bool ok, FaultCode fault);
    TelemetrySnapshot Snapshot();
}

public readonly record struct TelemetrySnapshot(
    long Cycles,
    long P50Micros,
    long P99Micros,
    long SafetyDenials,
    long GuardTimeouts,
    long WatchdogTrips,
    TrustTier Tier);
```

### 9\.3 Compito di Claude

- **SLO della release.**
  - Ciclo end\-to\-end `Observe → Verify`\: p99 ≤ 50 ms.
  - Disponibilità del Safety Gate: 100% — ogni intento ha un verdetto registrato, nessun verdetto mancante è tollerato.
  - Falsi negativi di sicurezza (azione eseguita senza verdetto positivo): **0**. Metrica bloccante, non soggetta a error budget.
  - Error budget mensile 0,5% sui cicli **non** di sicurezza (timeout di percezione, replan).
- **Percentili corretti.** Stimare p50/p99 con istogramma a bucket logaritmici (HdrHistogram\-like) su interi di microsecondi, mai con medie mobili: la media nasconde esattamente le code che qui contano.
- **Criterio di replay.** Rieseguire il journal deve produrre la stessa sequenza di `StageResult`. Poiché nessuno stage del percorso critico è non deterministico (INV\-02, seed espliciti al Gate 3), **qualunque** divergenza è un difetto bloccante, non una tolleranza da calibrare.
- **Checklist di certificazione.** Documento `docs/CERTIFICAZIONI/release-1.0-beta.md` con: gli otto invarianti verificati uno per uno, gli otto Gate firmati, l'esito del soak, l'hash finale della catena del journal e la firma dell'operatore.

### 9\.4 Compito di Cursor

- **File:** `src/NosAi.Storage/Replay/{IReplayEngine.cs, JournalReplayEngine.cs, ReplayDivergence.cs}`; `src/NosAi.Host/Telemetry/{ITelemetrySink.cs, EventSourceTelemetrySink.cs, LatencyHistogram.cs}`; `src/NosAi.Host/Dashboard/` (vista completa degli 11 stage).
- **`.csproj` e packaging:** `<PublishSingleFile>true</PublishSingleFile>`, `<SelfContained>true</SelfContained>`, `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`, `<InvariantGlobalization>true</InvariantGlobalization>`, `<DebugType>embedded</DebugType>`. Nessun trimming aggressivo: `NosAi.Adapter` usa riflessione P/Invoke.
- **CI:** workflow GitHub Actions `ci.yml` con job `build` (warning as error), `test` (tutti i filtri `Gate1`..`Gate8`), `analyzers` (`NOSAI0001`), `bench-gate` (fallisce se le allocazioni sul percorso critico sono `> 0`), `arch-test` (NetArchTest). I test che richiedono hardware reale sono marcati `Category=Physical` ed esclusi dalla CI ma **obbligatori** nella checklist di certificazione.
- **Telemetria:** `EventSource` dedicato `NosAi-Pipeline` con counter per stage; nessuna scrittura bloccante sul percorso critico.

### 9\.5 Criteri di accettazione e test

**Comandi**

```
dotnet test --filter "Category!=Physical"
dotnet run --project src/NosAi.Host -- --soak --hours 24 --live
dotnet run --project src/NosAi.Host -- --replay-compare --from 0 --to <last>
dotnet run -c Release --project bench/NosAi.Bench -- --filter * --exporters json
dotnet publish src/NosAi.Host -c Release -r win-x64
```

**Esiti attesi**

| Verifica | Soglia |
| --- | --- |
| Soak 24 h su hardware reale | 0 crash, 0 trip non spiegati, tier finale coerente |
| Collezioni Gen2 durante il soak | 0 sul percorso critico |
| Replay dell'intero journal | `IsIdentical = true`, divergenza 0 |
| Ciclo end\-to\-end | p99 ≤ 50 ms sull'intero soak |
| Falsi negativi di sicurezza | 0, assoluto |
| Catena hash del journal a fine soak | valida dal genesis all'ultimo record |
| Publish single\-file | avvio e attach riusciti su macchina pulita |

**Validazione fisica human\-in\-the\-loop.** L'operatore avvia il soak di 24 ore con NosTale, nodo Guard mobile e volume `NOSAI-SSD` reali; a fine corsa verifica la dashboard, esegue il replay comparativo e firma la checklist di release. Firma su `docs/CERTIFICAZIONI/release-1.0-beta.md`.

* * *

## 10\. Regola di transizione fra Gate

Un Gate `N+1` non può essere aperto — né in progettazione, né in scaffolding — finché il Gate `N` non ha tutte e otto le voci della Definition of Done verdi e la firma fisica dell'operatore. In caso di regressione su un Gate già chiuso, tutti i Gate successivi sono automaticamente riaperti: la certificazione non è retroattiva.

**Fuori perimetro per la 1.0 Beta.** Multi\-account, ottimizzazione economica di lungo periodo, apprendimento online dei pesi MAUT, esecuzione headless senza operatore e qualunque tier oltre `Supervised` in produzione. Questi temi non vanno progettati né scaffoldati prima della chiusura del Gate 8.
