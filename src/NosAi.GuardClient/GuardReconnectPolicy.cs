namespace NosAi.GuardClient;

/// <summary>What the client should do after a session ends.</summary>
public enum ReconnectDecision
{
    /// <summary>Wait and try again. The condition is expected to pass.</summary>
    Retry,

    /// <summary>
    /// Stop and tell the operator. Retrying cannot fix this and would bury it.
    /// </summary>
    Stop
}

/// <summary>
/// Decides whether a lost session is worth retrying, and how long to wait.
/// </summary>
/// <remarks>
/// <para>
/// The distinction this type exists for is between a link that is <i>down</i> and
/// a link that is <i>refused</i>. A runtime that has not started yet, a Wi-Fi
/// handover, a reverse tunnel that dropped: those come back, and a phone on a
/// desk should recover without being touched. A device the runtime does not
/// trust, a runtime the phone did not pin, a wire version neither can speak:
/// those never come back on their own, and retrying them every few seconds turns
/// a clear message into a scrolling one.
/// </para>
/// <para>
/// Getting this backwards in either direction is its own failure. Retrying a
/// refusal hides it; refusing to retry a blip makes the operator press a button
/// the app could have pressed itself.
/// </para>
/// </remarks>
public sealed class GuardReconnectPolicy
{
    /// <summary>First wait. Short enough that a runtime restart is barely noticed.</summary>
    public static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Longest wait between attempts.
    /// </summary>
    /// <remarks>
    /// Capped rather than unbounded: a phone left connected overnight must still
    /// find the runtime within seconds of it coming back, not after an hour of
    /// doubling.
    /// </remarks>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Reasons that will not resolve by waiting.
    /// </summary>
    /// <remarks>
    /// Each needs the operator to do something specific — re-pair, rebuild, or
    /// look at the network — and each is reported with that remedy rather than
    /// retried in silence.
    /// </remarks>
    private static readonly HashSet<string> Terminal = new(StringComparer.Ordinal)
    {
        // The runtime does not trust this device, or this device did not pin this
        // runtime. Both mean pairing, and no amount of waiting changes them.
        "authentication_refused",
        "runtime_proof_rejected",
        "invalid_server_ephemeral_key",

        // The two ends do not speak the same protocol. One of them must be rebuilt.
        "unsupported_contract_version",
        "invalid_header",
        "invalid_challenge_length",

        // A frame that would not open, or a session with no keys. Over TCP this is
        // not a glitch, and retrying it would paper over tampering or a defect.
        "decrypt_failed",
        "cipher_unavailable",
        "plaintext_after_handshake",

        // A sequence that does not line up cannot be recovered by reconnecting
        // under the same assumption.
        "sequence_violation",
    };

    private int _attempt;

    /// <summary>How many consecutive failures have been seen. Zero after a success.</summary>
    public int Attempt => _attempt;

    /// <summary>Whether waiting could plausibly help.</summary>
    public static bool IsTerminal(string? reason) => reason is not null && Terminal.Contains(reason);

    /// <summary>
    /// Records a failure and says what to do about it.
    /// </summary>
    /// <param name="reason">
    /// The structured reason from <see cref="GuardProtocolException.Reason"/>, or
    /// null when the link simply went quiet.
    /// </param>
    public ReconnectDecision OnFailure(string? reason, out TimeSpan delay)
    {
        if (IsTerminal(reason))
        {
            delay = TimeSpan.Zero;
            _attempt = 0;
            return ReconnectDecision.Stop;
        }

        _attempt++;
        delay = DelayFor(_attempt);
        return ReconnectDecision.Retry;
    }

    /// <summary>Clears the backoff after a session opens.</summary>
    public void OnSuccess() => _attempt = 0;

    /// <summary>
    /// Exponential backoff from <see cref="InitialDelay"/>, capped at
    /// <see cref="MaxDelay"/>.
    /// </summary>
    public static TimeSpan DelayFor(int attempt)
    {
        if (attempt <= 1)
            return InitialDelay;

        // Shift rather than Pow, and stop shifting well before it could overflow:
        // a phone left running for days must not wrap into a negative delay.
        int steps = Math.Min(attempt - 1, 16);
        var scaled = TimeSpan.FromTicks(InitialDelay.Ticks << steps);
        return scaled > MaxDelay ? MaxDelay : scaled;
    }
}
