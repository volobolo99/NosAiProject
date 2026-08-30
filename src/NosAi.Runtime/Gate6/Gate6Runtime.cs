// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Autore: Volodymyr Ryzhuk
// Gate 6 — Integrazione di sistema end-to-end: confini di sicurezza unificati,
//          ciclo chiuso su mondo dichiaratamente simulato, verifica dei vincoli
//          non negoziabili e certificazione locale di integrazione
// ============================================================================
//
// Onestà dei dati: il ciclo chiuso di questo gate gira su un mondo SIMULATO ed
// è etichettato come tale in ogni risultato. I check di protocollo e identità
// usano i componenti canonici reali (NosAi.Protocol, Gate 1 SessionAuth): il
// Gate 6 certifica l'integrazione, non una copia divergente dei contratti.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Gate6
{
    #region 1. Contratti canonici unificati e invarianti di sicurezza

    public enum TrustTier : byte
    {
        Tier0_ReadOnly = 0,
        Tier1_Assisted = 1,
        Tier2_SemiAutonomous = 2,
        Tier3_AutonomousRestricted = 3,
        Tier4_FullAutonomous = 4
    }

    public enum RuntimeMode : byte
    {
        Normal = 0,
        Degraded = 1,
        Recovery = 2,
        Cooling = 3,
        Stopped = 4
    }

    public enum ActionType : byte
    {
        None = 0,
        MoveToPosition = 1,
        TargetEntity = 2,
        UseBasicAttack = 3,
        UseSkill = 4,
        UseConsumable = 5,
        CollectGroundItem = 6,
        RestAndRecover = 7,
        EmergencyFlee = 8
    }

    public readonly record struct Position2D(int X, int Y)
    {
        public double DistanceTo(Position2D other)
        {
            int dx = X - other.X;
            int dy = Y - other.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    public sealed record ActionCandidate(
        Guid CandidateId,
        ActionType Type,
        string TargetId,
        int TargetX,
        int TargetY,
        int SkillOrItemId,
        TrustTier RequiredTrust,
        string Rationale
    );

    public sealed record PredictedOutcome(
        Guid CandidateId,
        int ExpectedHpDelta,
        int ExpectedMpDelta,
        int ExpectedTimeMs,
        float SuccessProbability,
        float RiskScore,
        string StateSignatureAfter
    );

    public sealed class SafetyToken
    {
        public Guid TokenId { get; }
        public Guid CandidateId { get; }
        public DateTime IssuedAtUtc { get; }
        public DateTime ExpiresAtUtc { get; }
        public TrustTier GrantedTier { get; }
        public byte[] Signature { get; }

        private bool _consumed;
        private readonly object _lock = new();

        public SafetyToken(Guid candidateId, TrustTier grantedTier, byte[] signature, TimeSpan ttl)
        {
            TokenId = Guid.NewGuid();
            CandidateId = candidateId;
            IssuedAtUtc = DateTime.UtcNow;
            ExpiresAtUtc = IssuedAtUtc + ttl;
            GrantedTier = grantedTier;
            Signature = signature;
        }

        public bool TryConsume()
        {
            lock (_lock)
            {
                if (_consumed || DateTime.UtcNow > ExpiresAtUtc)
                    return false;

                _consumed = true;
                return true;
            }
        }
    }

    /// <summary>
    /// Execution outcome. <see cref="Source"/> declares where the execution really
    /// happened: this gate only ever executes against the simulated world, and the
    /// result says so instead of impersonating a real client action.
    /// </summary>
    public sealed record ExecutionResult(
        Guid CandidateId,
        bool ExecutionInitiated,
        bool ExecutionCompleted,
        int ActualDurationMs,
        string? ErrorMessage,
        DataSourceKind Source
    );

    public sealed record VerificationResult(
        Guid CandidateId,
        bool IsSuccess,
        float DiscrepancyScore,
        string AnalysisReport
    );

    #endregion

    #region 2. Confini di sicurezza e modello delle autorità

    public sealed class TrustBoundary
    {
        private TrustTier _currentTrust;
        private readonly object _lock = new();

        public TrustTier CurrentTier
        {
            get { lock (_lock) { return _currentTrust; } }
        }

        public TrustBoundary(TrustTier initialTier = TrustTier.Tier2_SemiAutonomous)
        {
            _currentTrust = initialTier;
        }

        public bool IsAuthorized(TrustTier requiredTier)
        {
            lock (_lock) { return _currentTrust >= requiredTier; }
        }

        public void DowngradeTrust(TrustTier newTier)
        {
            lock (_lock)
            {
                if (newTier < _currentTrust)
                    _currentTrust = newTier;
            }
        }
    }

    public sealed class GuardPolicyEngine
    {
        public bool EvaluatePolicy(ActionCandidate candidate, PredictedOutcome outcome, RuntimeMode currentMode, out string? violation)
        {
            violation = null;

            if (currentMode == RuntimeMode.Stopped)
            {
                violation = "Stato runtime STOPPED: azioni inibite dal Watchdog fail-closed.";
                return false;
            }

            if (currentMode == RuntimeMode.Cooling && candidate.Type is ActionType.UseSkill or ActionType.UseBasicAttack)
            {
                violation = "Stato runtime COOLING: throttling termico attivo, inibito combattimento non essenziale.";
                return false;
            }

            if (outcome.RiskScore > 0.75f && candidate.Type != ActionType.EmergencyFlee)
            {
                violation = $"Rischio stimato eccessivo ({outcome.RiskScore:P1} > 75%).";
                return false;
            }

            return true;
        }
    }

    public sealed class SafetyGate
    {
        private readonly TrustBoundary _trustBoundary;
        private readonly GuardPolicyEngine _guardPolicy;
        private readonly byte[] _hmacKey;

        public SafetyGate(TrustBoundary trustBoundary, GuardPolicyEngine guardPolicy)
        {
            _trustBoundary = trustBoundary;
            _guardPolicy = guardPolicy;
            _hmacKey = new byte[32];
            RandomNumberGenerator.Fill(_hmacKey);
        }

        public bool TryAuthorize(ActionCandidate candidate, PredictedOutcome outcome, RuntimeMode currentMode, out SafetyToken? token, out string? rejectionReason)
        {
            token = null;

            if (!_guardPolicy.EvaluatePolicy(candidate, outcome, currentMode, out rejectionReason))
                return false;

            if (!_trustBoundary.IsAuthorized(candidate.RequiredTrust))
            {
                rejectionReason = $"Trust insufficiente: Richiesto {candidate.RequiredTrust}, Corrente {_trustBoundary.CurrentTier}.";
                return false;
            }

            byte[] signature = HMACSHA256.HashData(_hmacKey, candidate.CandidateId.ToByteArray());
            token = new SafetyToken(candidate.CandidateId, _trustBoundary.CurrentTier, signature, TimeSpan.FromMilliseconds(1500));
            rejectionReason = null;
            return true;
        }

        public bool ValidateToken(SafetyToken token)
        {
            byte[] expected = HMACSHA256.HashData(_hmacKey, token.CandidateId.ToByteArray());
            return CryptographicOperations.FixedTimeEquals(expected, token.Signature);
        }
    }

    /// <summary>
    /// Executes an authorized action against the simulated world. Token binding,
    /// signature validation and single consumption are real security logic; the
    /// execution itself is simulation and its result is labeled as such.
    /// </summary>
    public sealed class AuthorizedActionExecutor
    {
        private readonly SafetyGate _safetyGate;
        private readonly SimulatedGameWorld _world;

        public AuthorizedActionExecutor(SafetyGate safetyGate, SimulatedGameWorld world)
        {
            _safetyGate = safetyGate;
            _world = world;
        }

        public async Task<ExecutionResult> ExecuteAsync(ActionCandidate candidate, PredictedOutcome predicted, SafetyToken token, CancellationToken ct = default)
        {
            // CandidateId is a Guid: the value comparison is the effective (and
            // intended) binding check between candidate and token.
            if (candidate.CandidateId != token.CandidateId)
                return new ExecutionResult(candidate.CandidateId, false, false, 0, "SafetyToken non associato al candidato.", DataSourceKind.Simulated);

            if (!_safetyGate.ValidateToken(token))
                return new ExecutionResult(candidate.CandidateId, false, false, 0, "Firma SafetyToken non valida o contraffatta.", DataSourceKind.Simulated);

            if (!token.TryConsume())
                return new ExecutionResult(candidate.CandidateId, false, false, 0, "SafetyToken già consumato o scaduto.", DataSourceKind.Simulated);

            var sw = Stopwatch.StartNew();
            await Task.Delay(20, ct).ConfigureAwait(false);
            _world.ApplyAction(predicted);
            sw.Stop();

            return new ExecutionResult(candidate.CandidateId, true, true, (int)sw.ElapsedMilliseconds, null, DataSourceKind.Simulated);
        }
    }

    public sealed class ActionExecutionVerifier
    {
        public VerificationResult Verify(ActionCandidate candidate, PredictedOutcome predicted, ExecutionResult exec, int actualHpAfter, int actualMpAfter)
        {
            if (!exec.ExecutionCompleted)
                return new VerificationResult(candidate.CandidateId, false, 1.0f, $"Esecuzione fallita: {exec.ErrorMessage}");

            string observedSig = $"POST_HP_{actualHpAfter}_MP_{actualMpAfter}";
            bool matches = predicted.StateSignatureAfter == observedSig;

            return new VerificationResult(candidate.CandidateId, matches, matches ? 0.0f : 0.40f,
                matches ? "Verifica confermata (mondo simulato)." : $"Discrepanza: Atteso {predicted.StateSignatureAfter}, Rilevato {observedSig}");
        }
    }

    public sealed class RecoveryController
    {
        private readonly TrustBoundary _trustBoundary;
        private int _consecutiveFailures;

        public int ConsecutiveFailures => _consecutiveFailures;

        public RecoveryController(TrustBoundary trustBoundary) => _trustBoundary = trustBoundary;

        public void HandleFailure(VerificationResult verification, ref RuntimeMode mode)
        {
            _consecutiveFailures++;

            if (_consecutiveFailures <= 2)
                mode = RuntimeMode.Recovery;
            else if (_consecutiveFailures == 3)
            {
                _trustBoundary.DowngradeTrust(TrustTier.Tier1_Assisted);
                mode = RuntimeMode.Degraded;
            }
            else
            {
                _trustBoundary.DowngradeTrust(TrustTier.Tier0_ReadOnly);
                mode = RuntimeMode.Stopped;
            }
        }

        public void Reset() => _consecutiveFailures = 0;
    }

    #endregion

    #region 3. Mondo simulato esplicito

    /// <summary>
    /// The deterministic world this gate's closed loop runs against. It is
    /// simulation by definition (<see cref="Source"/>), applies outcomes on its
    /// own copy of the state, and supports discrepancy injection so the
    /// verification and recovery paths can be certified against a world that
    /// really diverged — not against the prediction restated.
    /// </summary>
    public sealed class SimulatedGameWorld
    {
        public const DataSourceKind Source = DataSourceKind.Simulated;

        private int _pendingHpDiscrepancy;

        public int CurrentHp { get; private set; }
        public int CurrentMp { get; private set; }
        public int MaxHp { get; }

        public SimulatedGameWorld(int initialHp = 1000, int initialMp = 500, int maxHp = 1000)
        {
            if (maxHp <= 0 || initialHp < 0 || initialHp > maxHp || initialMp < 0)
                throw new ArgumentOutOfRangeException(nameof(initialHp), "Invalid simulated world state.");
            CurrentHp = initialHp;
            CurrentMp = initialMp;
            MaxHp = maxHp;
        }

        /// <summary>Makes the next applied action diverge from its prediction by the given HP error.</summary>
        public void InjectHpDiscrepancy(int hpError) => _pendingHpDiscrepancy = hpError;

        public void ApplyAction(PredictedOutcome predicted)
        {
            ArgumentNullException.ThrowIfNull(predicted);
            int discrepancy = _pendingHpDiscrepancy;
            _pendingHpDiscrepancy = 0;
            CurrentHp = Math.Clamp(CurrentHp + predicted.ExpectedHpDelta + discrepancy, 0, MaxHp);
            CurrentMp = Math.Max(0, CurrentMp + predicted.ExpectedMpDelta);
        }
    }

    #endregion

    #region 4. Orchestratore di sistema unificato NosAi (Gate 6)

    public sealed class NosAiSystemRuntime : IAsyncDisposable
    {
        public const string Version = "1.0 Beta";
        public const string Author = "Volodymyr Ryzhuk";

        private readonly TrustBoundary _trustBoundary;
        private readonly GuardPolicyEngine _guardPolicy;
        private readonly SafetyGate _safetyGate;
        private readonly AuthorizedActionExecutor _executor;
        private readonly ActionExecutionVerifier _verifier;
        private readonly RecoveryController _recovery;
        private readonly SimulatedGameWorld _world;

        private RuntimeMode _currentMode = RuntimeMode.Normal;
        private double? _gpuTemperatureCelsius;
        private ulong _cycleCounter;

        public RuntimeMode CurrentMode => _currentMode;
        public TrustTier CurrentTrust => _trustBoundary.CurrentTier;

        /// <summary>Last reported GPU temperature; null means unknown, not cool (fail closed).</summary>
        public double? GpuTemperature => _gpuTemperatureCelsius;

        public SimulatedGameWorld World => _world;
        public int ConsecutiveFailures => _recovery.ConsecutiveFailures;

        public NosAiSystemRuntime(SimulatedGameWorld? world = null)
        {
            _world = world ?? new SimulatedGameWorld();
            _trustBoundary = new TrustBoundary(TrustTier.Tier2_SemiAutonomous);
            _guardPolicy = new GuardPolicyEngine();
            _safetyGate = new SafetyGate(_trustBoundary, _guardPolicy);
            _executor = new AuthorizedActionExecutor(_safetyGate, _world);
            _verifier = new ActionExecutionVerifier();
            _recovery = new RecoveryController(_trustBoundary);
        }

        public void UpdateHardwareTemperature(double temperatureCelsius)
        {
            _gpuTemperatureCelsius = temperatureCelsius;
            if (temperatureCelsius >= 80.0)
                _currentMode = RuntimeMode.Cooling;
            else if (_currentMode == RuntimeMode.Cooling && temperatureCelsius < 75.0)
                _currentMode = RuntimeMode.Normal;
        }

        /// <summary>
        /// The temperature source went away. Unknown is not cool: an active
        /// Cooling state stays latched until a real temperature clears it.
        /// </summary>
        public void ReportTemperatureUnknown() => _gpuTemperatureCelsius = null;

        /// <summary>
        /// One closed-loop step (Plan → Safety → Execute → Verify) against the
        /// simulated world. Every report is prefixed with the world's provenance.
        /// </summary>
        public async Task<(bool Success, string Report)> ExecuteStepAsync(CancellationToken ct = default)
        {
            _cycleCounter++;
            int currentHp = _world.CurrentHp;
            int currentMp = _world.CurrentMp;
            int maxHp = _world.MaxHp;

            var candidate = new ActionCandidate(
                Guid.NewGuid(),
                currentHp < maxHp * 0.35 ? ActionType.UseConsumable : ActionType.UseSkill,
                "SIM_TARGET_MOB_01",
                120, 85,
                currentHp < maxHp * 0.35 ? 101 : 201,
                TrustTier.Tier2_SemiAutonomous,
                "Azione pianificata sul mondo simulato del Gate 6."
            );

            int hpDelta = candidate.Type == ActionType.UseConsumable ? +300 : -15;
            int mpDelta = candidate.Type == ActionType.UseConsumable ? +100 : -35;
            var outcome = new PredictedOutcome(
                candidate.CandidateId,
                hpDelta,
                mpDelta,
                250,
                0.96f,
                0.08f,
                $"POST_HP_{Math.Clamp(currentHp + hpDelta, 0, maxHp)}_MP_{Math.Max(0, currentMp + mpDelta)}"
            );

            if (!_safetyGate.TryAuthorize(candidate, outcome, _currentMode, out SafetyToken? token, out string? rejectReason))
                return (false, $"[SIMULATED] Blocco Safety Gate: {rejectReason}");

            ExecutionResult exec = await _executor.ExecuteAsync(candidate, outcome, token!, ct).ConfigureAwait(false);
            VerificationResult verif = _verifier.Verify(candidate, outcome, exec, _world.CurrentHp, _world.CurrentMp);

            if (verif.IsSuccess)
            {
                _recovery.Reset();
                return (true, $"[SIMULATED] Ciclo {_cycleCounter} eseguito: {candidate.Type}. {verif.AnalysisReport}");
            }

            _recovery.HandleFailure(verif, ref _currentMode);
            return (false, $"[SIMULATED] Fallimento verifica: {verif.AnalysisReport} -> Stato Runtime: {_currentMode}");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    #endregion

    #region 5. Suite di certificazione di integrazione Gate 6

    public static class Gate6ReleaseCertifier
    {
        /// <summary>
        /// Runs every Gate 6 integration check and reports each one by name.
        /// </summary>
        /// <remarks>
        /// Same contract as the other gate runners: no short-circuit, a throwing
        /// check is a named failure. Protocol and identity checks integrate the
        /// canonical components (NosAi.Protocol wire format, Gate 1 SessionAuth,
        /// Gate 4 progression DAG, Gate 5 provider router) instead of local copies.
        /// A green run is local integration evidence only — it is NOT release
        /// validation and never claims the runtime is ready for real use.
        /// </remarks>
        public static async Task<bool> RunFullReleaseCertificationAsync()
        {
            Console.WriteLine("=== Gate 6 integration checks ===");

            bool allPassed = true;
            allPassed &= Run("Canonical NOSA header round-trips and rejects corrupted magic", TestCanonicalWireHeader);
            allPassed &= Run("Canonical sequence guard rejects replay and skip", TestCanonicalSequenceGuard);
            allPassed &= Run("RSA-2048 challenge is single use end to end", TestRsaChallengeSingleUse);
            allPassed &= Run("Safety token is HMAC-bound and single consumption", TestSafetyGateAndTokenConsumption);
            allPassed &= Run("Foreign and tampered safety tokens are rejected", TestForgedTokenRejection);
            allPassed &= Run("Guard policy blocks cooling combat, high risk and stopped mode", TestGuardPolicyBoundaries);
            allPassed &= await RunAsync("Closed loop succeeds against the simulated world and says so", TestClosedLoopSimulatedSuccessAsync).ConfigureAwait(false);
            allPassed &= await RunAsync("Injected discrepancy fails verification and engages recovery", TestClosedLoopDiscrepancyRecoveryAsync).ConfigureAwait(false);
            allPassed &= Run("Thermal watchdog cools at 80C and unknown stays latched", TestThermalWatchdogFailClosed);
            allPassed &= Run("Recovery only downgrades trust and reset never elevates it", TestRecoveryTrustInviolability);
            allPassed &= Run("Progression DAG gates SP unlocks on real prerequisites", TestProgressionDagIntegration);
            allPassed &= await RunAsync("Strict local-only routing never reaches the cloud slot", TestStrictLocalOnlyIntegrationAsync).ConfigureAwait(false);
            allPassed &= await RunAsync("Unauthorized cloud escalation fails closed at the router", TestCloudEscalationFailClosedIntegrationAsync).ConfigureAwait(false);
            allPassed &= Run("Hardware fingerprint stays anonymized", TestFingerprintAnonymized);

            Console.WriteLine(allPassed
                ? "=== Gate 6 checks passed. Local only: this is not real-environment verification. ==="
                : "=== Gate 6 checks FAILED. See the lines marked FAIL above. ===");
            return allPassed;
        }

        private static bool Run(string name, Func<bool> check)
        {
            try { return Report(name, check(), null); }
            catch (Exception ex) { return Report(name, false, $"{ex.GetType().Name}: {ex.Message}"); }
        }

        private static async Task<bool> RunAsync(string name, Func<Task<bool>> check)
        {
            try { return Report(name, await check().ConfigureAwait(false), null); }
            catch (Exception ex) { return Report(name, false, $"{ex.GetType().Name}: {ex.Message}"); }
        }

        private static bool Report(string name, bool passed, string? error)
        {
            var detail = error is null ? string.Empty : $" [{error}]";
            Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {name}{detail}");
            return passed;
        }

        // -------------------------------------------------------- canonical protocol

        private static bool TestCanonicalWireHeader()
        {
            var header = new NosAi.Runtime.Gate1.WireHeader(NosAi.Runtime.Gate1.WireMessageType.Heartbeat, 128, 7);
            Span<byte> buffer = stackalloc byte[NosAi.Runtime.Gate1.WireHeader.HeaderSize];
            header.WriteTo(buffer);

            if (!NosAi.Runtime.Gate1.WireHeader.TryRead(buffer, out var decoded, out _) || decoded != header)
                return false;

            // A corrupted magic (e.g. the historic NOS1 drift) must be rejected by name.
            buffer[3] = (byte)'1';
            return !NosAi.Runtime.Gate1.WireHeader.TryRead(buffer, out _, out string? error)
                && error == "invalid_magic";
        }

        private static bool TestCanonicalSequenceGuard()
        {
            var guard = new NosAi.Runtime.Gate1.SequenceGuard();
            return guard.ValidateAndAdvance(1, out _)
                && guard.ValidateAndAdvance(2, out _)
                && !guard.ValidateAndAdvance(2, out _)
                && !guard.ValidateAndAdvance(4, out _);
        }

        private static bool TestRsaChallengeSingleUse()
        {
            using var deviceKey = RSA.Create(2048);
            using var auth = new NosAi.Runtime.Gate1.SessionAuth(deviceKey.ExportSubjectPublicKeyInfoPem());

            // Adapted to the version 2 handshake: both sides now sign a session
            // transcript rather than a raw challenge, so the phone can no longer be
            // used as a signing oracle. See ADR-0008.
            byte[] clientNonce = NosAi.Runtime.Gate1.SessionTranscript.CreateNonce();
            if (!auth.TryBeginHandshake(clientNonce, out byte[] serverNonce)) return false;

            byte[] signature = deviceKey.SignHash(
                NosAi.Runtime.Gate1.SessionTranscript.Compute(
                    NosAi.Runtime.Gate1.HandshakeRole.Client, clientNonce, serverNonce),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            if (!auth.VerifyAndConsume(signature)) return false;
            // Replaying the same valid signature must fail: the challenge is consumed.
            return !auth.VerifyAndConsume(signature);
        }

        // -------------------------------------------------------- safety boundary

        private static bool TestSafetyGateAndTokenConsumption()
        {
            var trust = new TrustBoundary(TrustTier.Tier3_AutonomousRestricted);
            var gate = new SafetyGate(trust, new GuardPolicyEngine());

            var candidate = new ActionCandidate(Guid.NewGuid(), ActionType.UseSkill, "MOB", 0, 0, 201, TrustTier.Tier2_SemiAutonomous, "Test");
            var outcome = new PredictedOutcome(candidate.CandidateId, 0, -35, 200, 0.95f, 0.1f, "SIG");

            if (!gate.TryAuthorize(candidate, outcome, RuntimeMode.Normal, out SafetyToken? token, out _))
                return false;

            if (!gate.ValidateToken(token!)) return false;
            if (!token!.TryConsume()) return false;
            return !token.TryConsume();
        }

        private static bool TestForgedTokenRejection()
        {
            var trust = new TrustBoundary(TrustTier.Tier3_AutonomousRestricted);
            var gateA = new SafetyGate(trust, new GuardPolicyEngine());
            var gateB = new SafetyGate(trust, new GuardPolicyEngine());

            var candidate = new ActionCandidate(Guid.NewGuid(), ActionType.UseSkill, "MOB", 0, 0, 201, TrustTier.Tier2_SemiAutonomous, "Test");
            var outcome = new PredictedOutcome(candidate.CandidateId, 0, -35, 200, 0.95f, 0.1f, "SIG");
            if (!gateA.TryAuthorize(candidate, outcome, RuntimeMode.Normal, out SafetyToken? token, out _))
                return false;

            // A token minted by another gate's key must not validate here.
            if (gateB.ValidateToken(token!)) return false;

            // A tampered signature must not validate anywhere.
            var tampered = new SafetyToken(candidate.CandidateId, TrustTier.Tier2_SemiAutonomous,
                new byte[32], TimeSpan.FromSeconds(2));
            return !gateA.ValidateToken(tampered);
        }

        private static bool TestGuardPolicyBoundaries()
        {
            var policy = new GuardPolicyEngine();
            var candidate = new ActionCandidate(Guid.NewGuid(), ActionType.UseSkill, "MOB", 0, 0, 201, TrustTier.Tier2_SemiAutonomous, "Test");
            var safeOutcome = new PredictedOutcome(candidate.CandidateId, -10, -35, 200, 0.95f, 0.1f, "SIG");
            var riskyOutcome = safeOutcome with { RiskScore = 0.9f };
            var flee = candidate with { Type = ActionType.EmergencyFlee };

            return policy.EvaluatePolicy(candidate, safeOutcome, RuntimeMode.Normal, out _)
                && !policy.EvaluatePolicy(candidate, safeOutcome, RuntimeMode.Cooling, out _)
                && !policy.EvaluatePolicy(candidate, safeOutcome, RuntimeMode.Stopped, out _)
                && !policy.EvaluatePolicy(candidate, riskyOutcome, RuntimeMode.Normal, out _)
                && policy.EvaluatePolicy(flee, riskyOutcome, RuntimeMode.Normal, out _);
        }

        // -------------------------------------------------------- closed loop

        private static async Task<bool> TestClosedLoopSimulatedSuccessAsync()
        {
            await using var runtime = new NosAiSystemRuntime(new SimulatedGameWorld(1000, 500, 1000));
            var (success, report) = await runtime.ExecuteStepAsync().ConfigureAwait(false);
            return success
                && report.StartsWith("[SIMULATED]", StringComparison.Ordinal)
                && runtime.World.CurrentHp == 985;
        }

        private static async Task<bool> TestClosedLoopDiscrepancyRecoveryAsync()
        {
            var world = new SimulatedGameWorld(1000, 500, 1000);
            await using var runtime = new NosAiSystemRuntime(world);

            // The world diverges from the prediction: verification must fail
            // honestly and the recovery controller must engage.
            world.InjectHpDiscrepancy(-50);
            var (success, report) = await runtime.ExecuteStepAsync().ConfigureAwait(false);
            if (success || !report.Contains("Discrepanza", StringComparison.Ordinal)) return false;
            if (runtime.CurrentMode != RuntimeMode.Recovery || runtime.ConsecutiveFailures != 1) return false;

            // A clean follow-up cycle verifies and resets the failure streak.
            var (recovered, _) = await runtime.ExecuteStepAsync().ConfigureAwait(false);
            return recovered && runtime.ConsecutiveFailures == 0;
        }

        private static bool TestThermalWatchdogFailClosed()
        {
            var runtime = new NosAiSystemRuntime();
            runtime.UpdateHardwareTemperature(72.0);
            if (runtime.CurrentMode != RuntimeMode.Normal) return false;

            runtime.UpdateHardwareTemperature(82.5);
            if (runtime.CurrentMode != RuntimeMode.Cooling) return false;

            // Unknown is not cool: losing the temperature source must not clear Cooling.
            runtime.ReportTemperatureUnknown();
            if (runtime.CurrentMode != RuntimeMode.Cooling || runtime.GpuTemperature is not null) return false;

            runtime.UpdateHardwareTemperature(70.0);
            return runtime.CurrentMode == RuntimeMode.Normal;
        }

        private static bool TestRecoveryTrustInviolability()
        {
            var trust = new TrustBoundary(TrustTier.Tier2_SemiAutonomous);
            var recovery = new RecoveryController(trust);
            var mode = RuntimeMode.Normal;
            var failVerif = new VerificationResult(Guid.NewGuid(), false, 1.0f, "Fallimento simulato");

            for (int i = 0; i < 5; i++)
                recovery.HandleFailure(failVerif, ref mode);

            if (trust.CurrentTier != TrustTier.Tier0_ReadOnly || mode != RuntimeMode.Stopped) return false;

            // Reset clears the failure streak, never the trust downgrade.
            recovery.Reset();
            return recovery.ConsecutiveFailures == 0 && trust.CurrentTier == TrustTier.Tier0_ReadOnly;
        }

        // -------------------------------------------------------- cross-gate integration

        private static bool TestProgressionDagIntegration()
        {
            var engine = new NosAi.Runtime.Gate4.ProgressionEngineV2(new NosAi.Runtime.Gate4.KnowledgeBaseManager());
            var richInventory = new NosAi.Runtime.Gate4.ResourceInventory(1_000_000, 500, 200, 100, 50, 99, 99);

            var profile = new NosAi.Runtime.Gate4.CharacterProgressionProfile(
                1, "GateSixProbe", 99, 99, 1, 1, richInventory,
                ImmutableHashSet<NosAi.Runtime.Gate4.SpecialistCardType>.Empty,
                ImmutableHashSet.Create("ACT1_Q1_NOSVILLE_START", "ACT1_Q2_TS_12"));

            var available = engine.GetAvailableQuests(profile).Select(q => q.QuestId).ToHashSet(StringComparer.Ordinal);
            // SP1 is reachable (its prerequisite chain is complete); SP2 is gated
            // behind SP1 and must NOT be offered yet.
            if (!available.Contains("SP1_QUEST_UNLOCK") || available.Contains("SP2_QUEST_UNLOCK")) return false;

            var afterSp1 = profile with { CompletedQuestIds = profile.CompletedQuestIds.Add("SP1_QUEST_UNLOCK") };
            var availableAfter = engine.GetAvailableQuests(afterSp1).Select(q => q.QuestId).ToHashSet(StringComparer.Ordinal);
            return availableAfter.Contains("SP2_QUEST_UNLOCK") && !availableAfter.Contains("SP1_QUEST_UNLOCK");
        }

        private static async Task<bool> TestStrictLocalOnlyIntegrationAsync()
        {
            var router = new NosAi.Runtime.Gate5.ProviderRouter(NosAi.Runtime.Gate5.ProviderRoutingPolicy.StrictLocalOnly);
            var complex = await router.RouteAndExecuteAsync("COMPLEX_MAP_PLANNING", requiresComplexReasoning: true).ConfigureAwait(false);
            var simple = await router.RouteAndExecuteAsync("ROUTINE_TICK").ConfigureAwait(false);
            return complex.SourceProvider != NosAi.Runtime.Gate5.ProviderType.CloudEscalation
                && simple.SourceProvider == NosAi.Runtime.Gate5.ProviderType.HeuristicRuleEngine;
        }

        private sealed class FailingLocalProvider : NosAi.Runtime.Gate5.IDecisionProvider
        {
            public NosAi.Runtime.Gate5.ProviderType Type => NosAi.Runtime.Gate5.ProviderType.LocalLlamaCpp;
            public bool IsLoaded => false;
            public Task<bool> LoadModelAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
            public Task<NosAi.Runtime.Gate5.DecisionSuggestion> GenerateDecisionAsync(string promptContext, CancellationToken cancellationToken = default) =>
                throw new InvalidOperationException("local_provider_down");
            public Task UnloadModelAsync() => Task.CompletedTask;
        }

        private static async Task<bool> TestCloudEscalationFailClosedIntegrationAsync()
        {
            var router = new NosAi.Runtime.Gate5.ProviderRouter(
                NosAi.Runtime.Gate5.ProviderRoutingPolicy.LocalWithCloudFallback,
                new Dictionary<NosAi.Runtime.Gate5.ProviderType, NosAi.Runtime.Gate5.IDecisionProvider>
                { [NosAi.Runtime.Gate5.ProviderType.LocalLlamaCpp] = new FailingLocalProvider() });

            try
            {
                await router.RouteAndExecuteAsync("COMPLEX_MAP_PLANNING", requiresComplexReasoning: true).ConfigureAwait(false);
                return false;
            }
            catch (InvalidOperationException)
            {
                // Fail closed confirmed. With explicit authorization the same call
                // reaches the (simulated) cloud slot and is labeled as such.
                router.AuthorizeCloudEscalation();
                var escalated = await router.RouteAndExecuteAsync("COMPLEX_MAP_PLANNING", requiresComplexReasoning: true).ConfigureAwait(false);
                return escalated.SourceProvider == NosAi.Runtime.Gate5.ProviderType.CloudEscalation
                    && escalated.Source == DataSourceKind.Simulated;
            }
        }

        private static bool TestFingerprintAnonymized()
        {
            string fingerprint = new NosAi.Runtime.Gate5.HardwareBaselineProfiler().Fingerprint;
            return fingerprint.Length == 16
                && fingerprint.All(Uri.IsHexDigit)
                && !fingerprint.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        }
    }

    #endregion

    #region 6. Entry point legacy

    // StartupObject pins NosAi.Runtime.Program as the real entry point; this Main
    // is retained only because several imported gate sources declare their own.
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            bool success = await Gate6ReleaseCertifier.RunFullReleaseCertificationAsync();
            return success ? 0 : 1;
        }
    }

    #endregion
}
