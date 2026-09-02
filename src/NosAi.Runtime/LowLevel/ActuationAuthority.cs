// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// LowLevel — Under whose authority an act is emitted
// ============================================================================
//
// docs/adr/ADR-0020-one-authorisation-path-to-the-act.md § 2: a scope names its
// authority, there is no overload without one, and the audit for the act records
// which of the two it was.

using System.Globalization;
using NosAi.Runtime.Autonomy;

namespace NosAi.Runtime.LowLevel;

/// <summary>Which of the two legitimate authorities opened a scope.</summary>
public enum ActuationAuthorityKind : byte
{
    /// <summary>Neither. Not a state an act may be in.</summary>
    None = 0,

    /// <summary>The Gate 3 cycle, authorised by a <see cref="SafetyToken"/>.</summary>
    Planned = 1,

    /// <summary>A person who typed something, named by the command they typed.</summary>
    Commanded = 2
}

/// <summary>
/// The authority an act is emitted under.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two entries, and the third state is the one being forbidden</b> (ADR-0020 § 1).
/// An act is either planned — the cycle, carrying a <see cref="SafetyToken"/> — or
/// commanded, by an operator who typed something. Both are authorities. What is not
/// permitted is the state where the gate cannot say which, because an emission
/// attributable to nobody is one the audit cannot explain afterwards.
/// </para>
/// <para>
/// <b>Why an operator command counts.</b> The project needs human-driven acts against
/// the real client — <c>--input-guards</c>, <c>--step</c>, and the physical proofs of
/// P2 and P3 are all of them — so a rule of "no emission without a token" would either
/// be false in practice or would push those acts onto a path that skips the gate. A
/// named command is a real authority: someone chose it, it is written down, and it
/// appears in the audit beside the act.
/// </para>
/// <para>
/// <b>The default value is not an authority.</b> This is a struct, so
/// <c>default(ActuationAuthority)</c> exists and every uninitialised field produces one.
/// It reports <see cref="ActuationAuthorityKind.None"/> and the gate refuses it by name,
/// which is what stops "no authority" from being expressible by omission.
/// </para>
/// </remarks>
public readonly record struct ActuationAuthority
{
    private readonly SafetyToken? _token;
    private readonly string? _command;

    private ActuationAuthority(SafetyToken? token, string? command)
    {
        _token = token;
        _command = command;
    }

    /// <summary>Reported when a scope was asked for with no authority at all.</summary>
    public const string MissingReason = "actuation_authority_missing";

    /// <summary>Reported when the authorising token had already expired.</summary>
    public const string ExpiredPrefix = "actuation_authority_expired";

    /// <summary>The act the cycle planned, carrying the token the Safety Gate issued.</summary>
    public static ActuationAuthority Planned(SafetyToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return new ActuationAuthority(token, null);
    }

    /// <summary>
    /// The act a person asked for, named by the command they typed.
    /// </summary>
    /// <param name="operatorCommand">
    /// What appears in the audit. A blank name is refused rather than accepted as an
    /// anonymous authority: unattributable is the state this type exists to prevent.
    /// </param>
    public static ActuationAuthority Commanded(string operatorCommand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorCommand);
        return new ActuationAuthority(null, operatorCommand);
    }

    /// <summary>Which of the two, or neither.</summary>
    public ActuationAuthorityKind Kind => _token is not null
        ? ActuationAuthorityKind.Planned
        : _command is not null ? ActuationAuthorityKind.Commanded : ActuationAuthorityKind.None;

    /// <summary>The token, for a planned act. Null otherwise.</summary>
    public SafetyToken? Token => _token;

    /// <summary>The command, for a commanded act. Null otherwise.</summary>
    public string? Command => _command;

    /// <summary>What the audit records.</summary>
    public string Describe() => Kind switch
    {
        ActuationAuthorityKind.Planned => string.Create(CultureInfo.InvariantCulture,
            $"planned:{_token!.TokenId:N}"),
        ActuationAuthorityKind.Commanded => $"operator:{_command}",
        _ => "none"
    };

    /// <summary>
    /// Whether this authority may open a scope now, and why not when it may not.
    /// </summary>
    /// <remarks>
    /// <b>Expiry is checked here; consumption is not.</b> The token is consumed one
    /// layer above, in <c>AuthorizedActionExecutor</c>, and consuming it again at the
    /// gate would make every authorised act fail its second guard. What the gate can
    /// say without duplicating that logic is that a token which has already expired is
    /// not a live authorisation, whoever holds it.
    /// </remarks>
    public bool IsUsable(DateTime nowUtc, out string? refusalReason)
    {
        switch (Kind)
        {
            case ActuationAuthorityKind.Planned:
                TimeSpan past = nowUtc - _token!.ExpiresAtUtc;
                if (past > TimeSpan.Zero)
                {
                    refusalReason = string.Create(CultureInfo.InvariantCulture,
                        $"{ExpiredPrefix}:{past.TotalMilliseconds:F0}ms_ago");
                    return false;
                }

                refusalReason = null;
                return true;

            case ActuationAuthorityKind.Commanded:
                refusalReason = null;
                return true;

            default:
                refusalReason = MissingReason;
                return false;
        }
    }
}
