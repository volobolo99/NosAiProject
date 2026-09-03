namespace NosAi.Adapter.DirectEngine;

/// <summary>The outcome of resolving a profile against a loaded module.</summary>
/// <param name="Profile">The addresses, or null when resolution did not get that far.</param>
/// <param name="Refusal">Non-null exactly when <paramref name="Profile"/> is null.</param>
/// <param name="Validation">
/// The structural verdict on the candidate profile, reported whether or not
/// resolution proceeded.
/// </param>
public sealed record EngineProfileResolution(
    EngineResolvedProfile? Profile,
    EngineRefusal? Refusal,
    EngineProfileValidation Validation)
{
    public bool Ok => Profile is not null && Refusal is null;
}

/// <summary>
/// Turns a profile into addresses in one attached process, and says no when it
/// cannot.
/// </summary>
/// <remarks>
/// <para>
/// <b>This resolves; it does not inject.</b> Locating a function inside a module
/// image is reading, and reading is a capability the runtime already governs. Being
/// able to <i>call</i> what was located is a separate decision with separate
/// machinery, and separating the two here is what lets an operator validate a
/// profile against a live client without arming anything.
/// </para>
/// <para>
/// Validation is a distinct step from resolution because they fail for different
/// reasons and at different times: a mask that does not match its pattern is wrong
/// about every client that will ever exist, while a signature that is absent is
/// wrong only about this build. Collapsing them would report a stale profile as a
/// malformed one.
/// </para>
/// </remarks>
public interface IEngineProfileResolver
{
    /// <summary>
    /// Checks a profile against itself: no process, no module, no side effects.
    /// </summary>
    EngineProfileValidation Validate(EngineClientProfile profile);

    /// <summary>
    /// Validates <paramref name="profile"/> and then looks for each of its
    /// signatures in <paramref name="moduleImage"/>.
    /// </summary>
    /// <param name="profile">The candidate build description.</param>
    /// <param name="moduleImage">The client module's bytes, starting at its base.</param>
    /// <param name="moduleBase">Where that image is loaded in the target process.</param>
    /// <param name="processId">The process those addresses will be valid in.</param>
    /// <param name="expectedArchitecture">
    /// What the attached client actually is. A profile derived for another
    /// instruction set is refused rather than resolved: its call sequences would be
    /// meaningless even if every signature happened to match.
    /// </param>
    /// <param name="resolvedAtUtc">When this resolution happened.</param>
    EngineProfileResolution Resolve(
        EngineClientProfile profile,
        ReadOnlySpan<byte> moduleImage,
        nuint moduleBase,
        int processId,
        EngineArchitecture expectedArchitecture,
        DateTime resolvedAtUtc);
}
