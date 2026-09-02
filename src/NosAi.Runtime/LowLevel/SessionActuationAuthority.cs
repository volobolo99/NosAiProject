// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// LowLevel — Input authority bound to the session (P3)
// ============================================================================
//
// docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md § 4, DOMAIN-15.
//
// InputEnvironmentProbe already proves that SendInput reaches the desktop. What
// it cannot answer is whether it reaches *this* client: a medium-integrity
// process may not inject into a high-integrity foreground window, and the block
// is silent — neither the return value nor the last error names it. A runtime
// that cannot tell that case apart reads it as "the game is not responding",
// which is the reading under which a retry loop runs forever without ever being
// able to succeed.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using NosAi.Runtime.Perception;

namespace NosAi.Runtime.LowLevel;

/// <summary>
/// A Windows mandatory integrity level, or the absence of one.
/// </summary>
/// <remarks>
/// Unknown is a separate state and not the RID zero: untrusted <i>is</i> RID
/// <c>0x0000</c>, so a struct that used zero for "not read" would report the lowest
/// integrity in the system as a failed reading, and — worse — the other way round.
/// </remarks>
public readonly record struct IntegrityLevel
{
    private readonly bool _known;
    private readonly uint _rid;

    private IntegrityLevel(bool known, uint rid)
    {
        _known = known;
        _rid = rid;
    }

    /// <summary>The level could not be read. Compares below nothing and above nothing.</summary>
    public static IntegrityLevel Unknown => default;

    /// <summary>A level actually read from a token.</summary>
    public static IntegrityLevel FromRid(uint rid) => new(true, rid);

    /// <summary>False when the level could not be read.</summary>
    public bool IsKnown => _known;

    /// <summary>The mandatory RID. Meaningless unless <see cref="IsKnown"/>.</summary>
    public uint Rid => _rid;

    /// <summary>The label Windows uses, or the raw RID when it sits between two labels.</summary>
    public string Name => !_known
        ? "unknown"
        : _rid switch
        {
            0x0000 => "untrusted",
            0x1000 => "low",
            0x2000 => "medium",
            0x2100 => "medium_plus",
            0x3000 => "high",
            0x4000 => "system",
            0x5000 => "protected",
            _ => "0x" + _rid.ToString("X4", CultureInfo.InvariantCulture),
        };

    public override string ToString() => Name;
}

/// <summary>Reads the mandatory integrity level of a running process.</summary>
public interface IProcessIntegrityReader
{
    /// <summary>
    /// Reads one process's level, or says why it could not.
    /// </summary>
    /// <param name="failureReason">
    /// Non-null exactly when the returned level is <see cref="IntegrityLevel.Unknown"/>.
    /// </param>
    IntegrityLevel Read(int processId, out string? failureReason);
}

/// <summary>
/// Whether this runtime may act on this session, why not when it may not, and the
/// evidence behind either answer.
/// </summary>
/// <param name="IsActuating">Whether the decision level may be offered actuation at all.</param>
/// <param name="RefusalReason">Which check failed, named. Null when actuating.</param>
/// <param name="IsTerminal">
/// True when re-asking cannot change the answer without something outside the runtime
/// changing first. § 4's requirement that the failure stop looking like "the game is
/// not responding" is exactly this flag: a terminal verdict is latched and never
/// re-probed on its own.
/// </param>
/// <param name="Runtime">This process's integrity level.</param>
/// <param name="Client">The client process's integrity level.</param>
/// <param name="Window">The session window the verdict is about.</param>
/// <param name="ClientProcessId">The client process the verdict is about.</param>
/// <param name="PointerErrorPixels">
/// How far the pointer landed from where it was sent, in pixels. Only meaningful once
/// the probe ran; -1 when it did not.
/// </param>
/// <param name="TakenAtUtc">When the verdict was reached. What its validity is measured from.</param>
public readonly record struct SessionAuthorityVerdict(
    bool IsActuating,
    string? RefusalReason,
    bool IsTerminal,
    IntegrityLevel Runtime,
    IntegrityLevel Client,
    IntPtr Window,
    int ClientProcessId,
    int PointerErrorPixels,
    DateTimeOffset TakenAtUtc)
{
    /// <summary>Nothing has been verified yet. Not the same as verified and refused.</summary>
    public static SessionAuthorityVerdict None => new(
        false,
        SessionActuationAuthority.NoVerdictReason,
        false,
        IntegrityLevel.Unknown,
        IntegrityLevel.Unknown,
        IntPtr.Zero,
        0,
        -1,
        default);

    /// <summary>True when a verdict was actually reached, whichever way it went.</summary>
    public bool WasProbed => TakenAtUtc != default;
}

/// <summary>
/// Binds the outcome of input actuation to one session, and withholds the capability
/// from the decision level when that session cannot be acted on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two questions, and neither answers the other.</b> The integrity comparison is
/// structural and cheap: a process below the client's level cannot inject into it, and
/// that is knowable before anything is emitted. It is not sufficient — a matching level
/// is not proof that the path works — so it is followed by the harmless act § 4 asks
/// for: a pointer move of a few pixels inside the client, with no buttons, and a
/// re-read of where the pointer actually went. The act is what makes this evidence
/// rather than an assumption; the comparison is what stops the act from being the first
/// thing tried, and what gives the failure its name when it fails.
/// </para>
/// <para>
/// <b>Why the client must hold the foreground for the probe.</b> UIPI decides against
/// the window that owns the input queue at the moment of injection. Probing while some
/// other window is in front measures a different pair of processes, and a pass measured
/// against the wrong pair is worse than no measurement: it would authorise the act it
/// did not test.
/// </para>
/// <para>
/// <b>A terminal verdict is latched, and that is the point (§ 4).</b> When the runtime
/// sits below the client, or the pointer demonstrably does not go where it was sent,
/// asking again cannot change the answer — the client would have to be restarted, or
/// the runtime elevated. So the verdict sticks until <see cref="Reset"/> is called by
/// the operator or the session window changes. Without the latch the caller would
/// re-probe forever, moving the operator's pointer on every attempt, and read a
/// permanent condition as a transient one.
/// </para>
/// <para>
/// <b>Observation is unaffected.</b> A non-actuating session stays fully valid for
/// reading: this withholds a capability, it does not stop the pipeline. That division
/// is what ADR-0014 already draws between reading a client and driving it.
/// </para>
/// <para>
/// <b>The probe is verification, not action.</b> It goes through
/// <see cref="GatedInputBackend"/> like everything else — policy, an open
/// <see cref="ActuationScope"/>, and the release the scope guarantees — so it cannot
/// be a way around the gate. Nothing irreversible is emitted: a cursor move is the
/// one act that can be undone by moving the cursor back, and it is moved back.
/// </para>
/// </remarks>
public sealed class SessionActuationAuthority
{
    /// <summary>Reported before anything has been verified.</summary>
    public const string NoVerdictReason = "authority_not_verified";

    /// <summary>Reported when no session has been declared.</summary>
    public const string NoSessionReason = "authority_no_session";

    /// <summary>Reported when the foreground came back and the verdict has not been retaken.</summary>
    public const string ReverificationPendingReason = "authority_reverification_pending";

    /// <summary>Reported when the verdict is older than <see cref="Validity"/>.</summary>
    public const string ExpiredPrefix = "authority_verdict_expired";

    /// <summary>Reported when the runtime's own integrity level could not be read.</summary>
    public const string RuntimeIntegrityUnknownPrefix = "authority_runtime_integrity_unknown";

    /// <summary>Reported when the client's integrity level could not be read.</summary>
    public const string ClientIntegrityUnknownPrefix = "authority_client_integrity_unknown";

    /// <summary>Reported when this runtime sits below the client. Terminal.</summary>
    public const string IntegrityBelowClientPrefix = "authority_integrity_below_client";

    /// <summary>Reported when live input is switched off, so the probe cannot run.</summary>
    public const string InputNotArmedReason = "authority_live_input_not_armed";

    /// <summary>Reported when the client window did not hold the foreground during the probe.</summary>
    public const string WindowNotForegroundReason = "authority_window_not_foreground";

    /// <summary>Reported when the window geometry could not be read.</summary>
    public const string GeometryUnknownReason = "authority_geometry_unknown";

    /// <summary>Reported when the client area is too small to move the pointer inside it.</summary>
    public const string ClientAreaTooSmallReason = "authority_client_area_too_small";

    /// <summary>Reported when the cursor position could not be read.</summary>
    public const string CursorUnreadableReason = "authority_cursor_unreadable";

    /// <summary>Reported when the gate refused to open a scope for the probe.</summary>
    public const string ScopeRefusedPrefix = "authority_scope_refused";

    /// <summary>Reported when the pointer move was refused before it was emitted.</summary>
    public const string MoveRefusedReason = "authority_pointer_move_refused";

    /// <summary>Reported when the pointer did not land where it was sent. Terminal.</summary>
    public const string PointerDidNotMovePrefix = "authority_pointer_did_not_move";

    /// <summary>What the audit records as the authority behind the probe's pointer move.</summary>
    public const string ProbeAuthorityName = "session_authority_probe";

    /// <summary>
    /// How long a verdict stands before it has to be retaken.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The events § 4 names — a session opening, the foreground coming back — are
    /// observed and drive re-verification directly, so this is the backstop for the
    /// change nobody saw: an elevated window raised over the client, a desktop switch,
    /// a client that re-created its window without the runtime noticing.
    /// </para>
    /// <para>
    /// Sixty seconds is chosen against what the probe costs, not against how fast
    /// integrity changes. Re-verifying moves the operator's pointer by a few pixels and
    /// puts it back; once a minute that is barely perceptible, and it bounds the
    /// unobserved interval to something a person would notice going wrong anyway. It is
    /// a bound on ignorance, not a poll: nothing runs on a timer, the verdict simply
    /// stops counting as evidence once it is this old.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan DefaultValidity = TimeSpan.FromSeconds(60);

    /// <summary>How far the pointer may land from where it was sent and still count.</summary>
    /// <remarks>The same two pixels <see cref="InputEnvironmentProbe"/> allows, for the same reason: the compositor rounds.</remarks>
    public const int DefaultTolerancePixels = 2;

    /// <summary>How far the probe nudges the pointer when it is already inside the client.</summary>
    private const int NudgePixels = 4;

    /// <summary>How long the probe waits for the move to be observable, in total.</summary>
    private const int PollAttempts = 20;
    private const int PollIntervalMs = 2;

    private readonly IProcessIntegrityReader _integrity;
    private readonly ICommitEnvironment _environment;
    private readonly GatedInputBackend _input;
    private readonly Func<bool> _liveInputArmed;
    private readonly TimeProvider _clock;

    private readonly object _lock = new();
    private IntPtr _window;
    private int _clientProcessId;
    private bool _reverificationPending;
    private SessionAuthorityVerdict _verdict = SessionAuthorityVerdict.None;

    /// <param name="integrity">Reads the two integrity levels being compared.</param>
    /// <param name="environment">
    /// Supplies the foreground window and the window geometry. The same seam the commit
    /// point uses, so both answer from one reading of the desktop rather than two.
    /// </param>
    /// <param name="input">
    /// The gated boundary. Concrete on purpose: the probe emits real input and must not
    /// have a route that skips the gate.
    /// </param>
    /// <param name="liveInputArmed">
    /// Whether the operator has armed live input. Read per probe, never cached: a probe
    /// that ran against a stale copy would report a capability the gate would refuse.
    /// </param>
    public SessionActuationAuthority(
        IProcessIntegrityReader integrity,
        ICommitEnvironment environment,
        GatedInputBackend input,
        Func<bool> liveInputArmed,
        TimeProvider? clock = null,
        TimeSpan? validity = null,
        int tolerancePixels = DefaultTolerancePixels)
    {
        _integrity = integrity ?? throw new ArgumentNullException(nameof(integrity));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _liveInputArmed = liveInputArmed ?? throw new ArgumentNullException(nameof(liveInputArmed));
        _clock = clock ?? TimeProvider.System;
        Validity = validity ?? DefaultValidity;
        ArgumentOutOfRangeException.ThrowIfNegative(tolerancePixels);
        TolerancePixels = tolerancePixels;
    }

    /// <summary>How long a verdict counts as evidence.</summary>
    public TimeSpan Validity { get; }

    /// <summary>How far the pointer may land from where it was sent.</summary>
    public int TolerancePixels { get; }

    /// <summary>The verdict as it stands, whether or not it is still fresh.</summary>
    public SessionAuthorityVerdict Current
    {
        get { lock (_lock) return _verdict; }
    }

    /// <summary>Probes run. Diagnostic.</summary>
    public long ProbeCount { get; private set; }

    /// <summary>
    /// Declares which session everything after this is about, and discards whatever was
    /// known about the previous one — including a terminal verdict.
    /// </summary>
    /// <remarks>
    /// A new window is a new pair of processes, so nothing measured against the old one
    /// carries over. This is also the one automatic way out of a latch: a client
    /// restarted without elevation gets a fresh answer without the operator having to
    /// clear anything.
    /// </remarks>
    public void BeginSession(IntPtr window, int clientProcessId)
    {
        lock (_lock)
        {
            _window = window;
            _clientProcessId = clientProcessId;
            _reverificationPending = true;
            _verdict = SessionAuthorityVerdict.None;
        }
    }

    /// <summary>
    /// Notes that the client window has come back to the foreground, so the verdict has
    /// to be retaken before it counts again (§ 4).
    /// </summary>
    /// <remarks>
    /// A terminal verdict is not cleared by this: what was in front in the meantime
    /// cannot raise a runtime above the client it sits below.
    /// </remarks>
    public void NoteForegroundRestored()
    {
        lock (_lock)
        {
            if (!_verdict.IsTerminal)
                _reverificationPending = true;
        }
    }

    /// <summary>
    /// Clears a latched verdict on the operator's word, naming who asked.
    /// </summary>
    /// <remarks>
    /// The only way past a terminal refusal short of a new session. It clears the
    /// verdict; it does not assert a new one — the next <see cref="Verify"/> does that,
    /// and can perfectly well latch again.
    /// </remarks>
    public void Reset(string operatorReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorReason);
        lock (_lock)
        {
            _reverificationPending = true;
            _verdict = SessionAuthorityVerdict.None;
        }
    }

    /// <summary>
    /// Why actuation is not on offer right now, or null when it is.
    /// </summary>
    /// <remarks>
    /// A pure read: it never probes and never emits anything, so the decision level can
    /// ask it as often as it likes and asking cannot move the pointer. Verification is
    /// driven by the events § 4 names, not by whoever happened to read this.
    /// </remarks>
    public string? CurrentRefusal()
    {
        lock (_lock)
        {
            if (_window == IntPtr.Zero)
                return NoSessionReason;

            if (_verdict.IsTerminal)
                return _verdict.RefusalReason;

            if (_reverificationPending)
                return ReverificationPendingReason;

            if (!_verdict.WasProbed)
                return NoVerdictReason;

            if (!_verdict.IsActuating)
                return _verdict.RefusalReason;

            if (_verdict.Window != _window)
                return NoVerdictReason;

            TimeSpan age = _clock.GetUtcNow() - _verdict.TakenAtUtc;
            if (age < TimeSpan.Zero || age > Validity)
                return string.Create(CultureInfo.InvariantCulture,
                    $"{ExpiredPrefix}:{age.TotalMilliseconds:F0}ms_of_{Validity.TotalMilliseconds:F0}ms");

            return null;
        }
    }

    /// <summary>True exactly when <see cref="CurrentRefusal"/> is null.</summary>
    public bool IsActuating => CurrentRefusal() is null;

    /// <summary>
    /// Takes a verdict if and only if the standing one does not answer, and returns the
    /// refusal that remains — null when the session may be acted on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The entry point for whoever is about to act: a decision cycle before it offers
    /// the capability, the operator's command when it reports the state. On the path
    /// that is already verified it reads a field and returns, so calling it before every
    /// act costs nothing; it probes only when the answer has actually run out —
    /// re-verification outstanding after a foreground restore, or a verdict past
    /// <see cref="Validity"/>.
    /// </para>
    /// <para>
    /// It never re-probes a terminal verdict and never invents a session: with no window
    /// declared it says so and stops, because probing without a session would measure
    /// this runtime against whatever happened to be in front.
    /// </para>
    /// </remarks>
    public string? EnsureVerified()
    {
        string? refusal = CurrentRefusal();
        if (refusal is null || refusal == NoSessionReason)
            return refusal;

        lock (_lock)
        {
            if (_verdict.IsTerminal)
                return _verdict.RefusalReason;
        }

        Verify();
        return CurrentRefusal();
    }

    /// <summary>
    /// Compares the integrity levels and, if they allow it, performs the harmless act
    /// and reads back where the pointer went.
    /// </summary>
    /// <remarks>
    /// Called at session open and on foreground restore. It returns the latched verdict
    /// untouched when one is standing, so calling it in a loop does not turn a permanent
    /// condition into a pointer that twitches forever.
    /// </remarks>
    public SessionAuthorityVerdict Verify()
    {
        IntPtr window;
        int clientProcessId;
        lock (_lock)
        {
            if (_verdict.IsTerminal)
                return _verdict;

            window = _window;
            clientProcessId = _clientProcessId;
        }

        if (window == IntPtr.Zero)
            return Store(Refuse(window, clientProcessId, NoSessionReason, terminal: false));

        ProbeCount++;
        SessionAuthorityVerdict verdict = Probe(window, clientProcessId);
        return Store(verdict);
    }

    private SessionAuthorityVerdict Store(SessionAuthorityVerdict verdict)
    {
        lock (_lock)
        {
            _verdict = verdict;
            // Only a verdict that was actually reached clears the flag. A refusal for a
            // transient reason leaves re-verification outstanding, which is what keeps
            // "we could not check" from settling into "checked, and fine".
            _reverificationPending = !verdict.IsActuating && !verdict.IsTerminal;
        }

        return verdict;
    }

    private SessionAuthorityVerdict Probe(IntPtr window, int clientProcessId)
    {
        // 1. The structural comparison, before anything is emitted.
        IntegrityLevel runtime = _integrity.Read(Environment.ProcessId, out string? runtimeFailure);
        if (!runtime.IsKnown)
            return Refuse(window, clientProcessId, $"{RuntimeIntegrityUnknownPrefix}:{runtimeFailure ?? "unreadable"}",
                terminal: false, runtime, IntegrityLevel.Unknown);

        IntegrityLevel client = _integrity.Read(clientProcessId, out string? clientFailure);
        if (!client.IsKnown)
            return Refuse(window, clientProcessId, $"{ClientIntegrityUnknownPrefix}:{clientFailure ?? "unreadable"}",
                terminal: false, runtime, client);

        // Below the client is terminal: no amount of asking again lifts a token.
        if (runtime.Rid < client.Rid)
            return Refuse(window, clientProcessId,
                $"{IntegrityBelowClientPrefix}:{runtime.Name}_under_{client.Name}",
                terminal: true, runtime, client);

        // 2. The act needs the gate open, and the gate is the operator's switch.
        if (!_liveInputArmed())
            return Refuse(window, clientProcessId, InputNotArmedReason, terminal: false, runtime, client);

        GeometryEpoch epoch = _environment.ReadEpoch(window);
        if (!epoch.IsKnown)
            return Refuse(window, clientProcessId, GeometryUnknownReason, terminal: false, runtime, client);

        // UIPI decides against whatever owns the input queue now, so a probe taken while
        // something else is in front measures the wrong pair of processes.
        if (_environment.ForegroundWindow() != window)
            return Refuse(window, clientProcessId, WindowNotForegroundReason, terminal: false, runtime, client);

        if (!_input.TryGetCursorPosition(out int originX, out int originY))
            return Refuse(window, clientProcessId, CursorUnreadableReason, terminal: false, runtime, client);

        if (!TryChooseProbePoint(epoch.ClientArea, originX, originY, out int targetX, out int targetY))
            return Refuse(window, clientProcessId, ClientAreaTooSmallReason, terminal: false, runtime, client);

        var request = new CommitRequest(
            GeometryStamp.Take(window, _clock),
            targetX,
            targetY,
            epoch.Shape);

        // Named, because ADR-0020 § 2 forbids the state where the gate cannot say under
        // whose authority it is acting. The probe is not a planned act and has no token;
        // it is a verification a person or the host asked for, and the audit says so.
        ActuationAuthority authority = ActuationAuthority.Commanded(ProbeAuthorityName);

        if (!_input.TryBeginActuation(in request, in authority, out ActuationScope? scope, out string? scopeRefusal) || scope is null)
            return Refuse(window, clientProcessId, $"{ScopeRefusedPrefix}:{scopeRefusal ?? "unknown"}",
                terminal: false, runtime, client);

        int error;
        int landedX, landedY;
        try
        {
            if (!_input.MoveAbsolute(targetX, targetY))
                return Refuse(window, clientProcessId, MoveRefusedReason, terminal: false, runtime, client);

            error = Observe(targetX, targetY, out landedX, out landedY);
        }
        finally
        {
            // Put it back where the operator left it, then close the scope. Both happen
            // whatever the outcome: an unverified session is not a reason to leave the
            // pointer somewhere nobody put it.
            _input.MoveAbsolute(originX, originY);
            scope.Dispose();
        }

        if (error > TolerancePixels)
            return Refuse(window, clientProcessId,
                string.Create(CultureInfo.InvariantCulture,
                    $"{PointerDidNotMovePrefix}:wanted={targetX},{targetY}_got={landedX},{landedY}"),
                terminal: true, runtime, client, error);

        return new SessionAuthorityVerdict(
            IsActuating: true,
            RefusalReason: null,
            IsTerminal: false,
            Runtime: runtime,
            Client: client,
            Window: window,
            ClientProcessId: clientProcessId,
            PointerErrorPixels: error,
            TakenAtUtc: _clock.GetUtcNow());
    }

    /// <summary>
    /// Polls for the move to become observable and returns how far off it landed.
    /// </summary>
    /// <remarks>
    /// The compositor applies the move asynchronously, so a single read straight after
    /// the call would report a failure that is only a race. Polling stops as soon as the
    /// reading is within tolerance, so the wait costs nothing on the path that works.
    /// </remarks>
    private int Observe(int targetX, int targetY, out int landedX, out int landedY)
    {
        landedX = int.MinValue;
        landedY = int.MinValue;
        int error = int.MaxValue;

        for (int attempt = 0; attempt < PollAttempts && error > TolerancePixels; attempt++)
        {
            if (_input.TryGetCursorPosition(out int x, out int y))
            {
                landedX = x;
                landedY = y;
                error = Math.Max(Math.Abs(x - targetX), Math.Abs(y - targetY));
                if (error <= TolerancePixels)
                    break;
            }

            Thread.Sleep(PollIntervalMs);
        }

        return error;
    }

    /// <summary>
    /// Picks a point inside the client area: a nudge from where the pointer already is
    /// when it is inside, and a point near the centre when it is not.
    /// </summary>
    /// <remarks>
    /// The point stays inside the client on purpose. It is not what makes the
    /// measurement valid — the cursor is global and UIPI judges the foreground window —
    /// but a probe that parked the pointer over another application's hot corner would
    /// be doing something to that application, and this is meant to do nothing to
    /// anyone.
    /// </remarks>
    private static bool TryChooseProbePoint(
        in PixelRect client,
        int cursorX,
        int cursorY,
        out int targetX,
        out int targetY)
    {
        targetX = 0;
        targetY = 0;

        // Room for the nudge in both directions, and a border to keep off the edge where
        // the OS starts snapping windows.
        if (client.Width <= (2 * NudgePixels) + 2 || client.Height <= (2 * NudgePixels) + 2)
            return false;

        int left = client.X + NudgePixels + 1;
        int right = client.X + client.Width - NudgePixels - 2;
        int top = client.Y + NudgePixels + 1;
        int bottom = client.Y + client.Height - NudgePixels - 2;

        bool inside = cursorX >= left && cursorX <= right && cursorY >= top && cursorY <= bottom;
        if (!inside)
        {
            targetX = client.X + (client.Width / 2);
            targetY = client.Y + (client.Height / 2);
            return true;
        }

        // Away from the nearer edge, so the nudge never has to be clamped — a clamped
        // nudge could land on the pixel the pointer is already on, and a move to where
        // the pointer already is proves nothing.
        targetX = cursorX + NudgePixels <= right ? cursorX + NudgePixels : cursorX - NudgePixels;
        targetY = cursorY;
        return true;
    }

    private SessionAuthorityVerdict Refuse(
        IntPtr window,
        int clientProcessId,
        string reason,
        bool terminal,
        IntegrityLevel runtime = default,
        IntegrityLevel client = default,
        int pointerError = -1) =>
        new(
            IsActuating: false,
            RefusalReason: reason,
            IsTerminal: terminal,
            Runtime: runtime,
            Client: client,
            Window: window,
            ClientProcessId: clientProcessId,
            PointerErrorPixels: pointerError,
            TakenAtUtc: _clock.GetUtcNow());
}

/// <summary>The real token store.</summary>
/// <remarks>
/// Opened with <c>PROCESS_QUERY_LIMITED_INFORMATION</c>, which is the least that answers
/// the question: reading a level is not reading a process, and asking for more access
/// than the question needs would fail on exactly the high-integrity client whose level
/// matters most.
/// </remarks>
public sealed partial class Win32ProcessIntegrityReader : IProcessIntegrityReader
{
    /// <summary>Reported off Windows, where there is no mandatory label to read.</summary>
    public const string NotWindowsReason = "not_windows";

    /// <summary>Reported when the process could not be opened at all.</summary>
    public const string ProcessNotOpenedReason = "open_process_failed";

    /// <summary>Reported when the process opened but its token did not.</summary>
    public const string TokenNotOpenedReason = "open_process_token_failed";

    /// <summary>Reported when the mandatory label could not be read from the token.</summary>
    public const string LabelUnreadableReason = "token_integrity_unreadable";

    /// <summary>Reported when the label came back without a RID in it.</summary>
    public const string LabelMalformedReason = "token_integrity_malformed";

    public IntegrityLevel Read(int processId, out string? failureReason)
    {
        if (!OperatingSystem.IsWindows())
        {
            failureReason = NotWindowsReason;
            return IntegrityLevel.Unknown;
        }

        return ReadWindows(processId, out failureReason);
    }

    [SupportedOSPlatform("windows")]
    private static IntegrityLevel ReadWindows(int processId, out string? failureReason)
    {
        nint process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == 0)
        {
            failureReason = $"{ProcessNotOpenedReason}:{Marshal.GetLastWin32Error()}";
            return IntegrityLevel.Unknown;
        }

        nint token = 0;
        nint buffer = 0;
        try
        {
            if (!OpenProcessToken(process, TokenQuery, out token))
            {
                failureReason = $"{TokenNotOpenedReason}:{Marshal.GetLastWin32Error()}";
                return IntegrityLevel.Unknown;
            }

            // The first call is expected to fail: it is how the size is asked for.
            GetTokenInformation(token, TokenIntegrityLevel, 0, 0, out int needed);
            if (needed <= 0)
            {
                failureReason = $"{LabelUnreadableReason}:{Marshal.GetLastWin32Error()}";
                return IntegrityLevel.Unknown;
            }

            buffer = Marshal.AllocHGlobal(needed);
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, needed, out _))
            {
                failureReason = $"{LabelUnreadableReason}:{Marshal.GetLastWin32Error()}";
                return IntegrityLevel.Unknown;
            }

            // TOKEN_MANDATORY_LABEL is a SID_AND_ATTRIBUTES, whose first field is the SID.
            nint sid = Marshal.ReadIntPtr(buffer);
            if (sid == 0)
            {
                failureReason = LabelMalformedReason;
                return IntegrityLevel.Unknown;
            }

            nint countPointer = GetSidSubAuthorityCount(sid);
            if (countPointer == 0)
            {
                failureReason = LabelMalformedReason;
                return IntegrityLevel.Unknown;
            }

            byte count = Marshal.ReadByte(countPointer);
            if (count == 0)
            {
                failureReason = LabelMalformedReason;
                return IntegrityLevel.Unknown;
            }

            // The level is the last sub-authority, whatever the SID's shape.
            nint ridPointer = GetSidSubAuthority(sid, (uint)(count - 1));
            if (ridPointer == 0)
            {
                failureReason = LabelMalformedReason;
                return IntegrityLevel.Unknown;
            }

            failureReason = null;
            return IntegrityLevel.FromRid(unchecked((uint)Marshal.ReadInt32(ridPointer)));
        }
        finally
        {
            if (buffer != 0)
                Marshal.FreeHGlobal(buffer);
            if (token != 0)
                CloseHandle(token);
            CloseHandle(process);
        }
    }

    private const int ProcessQueryLimitedInformation = 0x1000;
    private const int TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;

    [SupportedOSPlatform("windows")]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(int desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [SupportedOSPlatform("windows")]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [SupportedOSPlatform("windows")]
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, int desiredAccess, out nint token);

    [SupportedOSPlatform("windows")]
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(nint token, int informationClass, nint information, int length, out int returnLength);

    [SupportedOSPlatform("windows")]
    [LibraryImport("advapi32.dll")]
    private static partial nint GetSidSubAuthorityCount(nint sid);

    [SupportedOSPlatform("windows")]
    [LibraryImport("advapi32.dll")]
    private static partial nint GetSidSubAuthority(nint sid, uint index);
}
