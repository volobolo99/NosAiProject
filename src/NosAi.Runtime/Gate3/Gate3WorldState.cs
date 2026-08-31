using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
// Aliased rather than importing the whole Gate 1 namespace: several type names
// are duplicated across gates, so a broad using here would make them ambiguous.
using Gate1CanonicalSnapshot = NosAi.Runtime.Gate1.Gate1CanonicalSnapshot;

namespace NosAi.Runtime.Gate3;

/// <summary>
/// The state the pipeline plans from, with the provenance of every field.
/// </summary>
/// <remarks>
/// <para>
/// The cycle used to take bare integers — <c>ExecuteCycleAsync(800, 1000, 100, …)</c>
/// — so a caller could hand the planner invented numbers and get back a confident
/// plan with nothing marking it as fiction. That is the same defect the verifier
/// had on the output side: a value with no provenance treated as an observation.
/// </para>
/// <para>
/// The rule this type enforces: <b>you may plan on simulated state, but you may
/// not act on it.</b> A dry run over hypothetical numbers is useful; carrying one
/// through to a real effector is not.
/// </para>
/// </remarks>
public sealed record Gate3WorldState(
    ClassifiedValue<int> Hp,
    ClassifiedValue<int> MaxHp,
    ClassifiedValue<int> Mp,
    ClassifiedValue<bool> HasTarget,
    ClassifiedValue<bool> InCombat)
{
    /// <summary>Whether every field carries a value at all, whatever its source.</summary>
    /// <remarks>
    /// Planning needs numbers; it does not need them to be real. What must never
    /// happen is planning over UNKNOWN, because there is nothing to reason about
    /// and any answer would be invented.
    /// </remarks>
    public bool IsPlannable =>
        Hp.HasValue && MaxHp.HasValue && Mp.HasValue && HasTarget.HasValue && InCombat.HasValue;

    /// <summary>Whether every field was actually observed from the live client.</summary>
    public bool IsFullyObserved =>
        IsPlannable
        && Hp.Source == DataSourceKind.Live
        && MaxHp.Source == DataSourceKind.Live
        && Mp.Source == DataSourceKind.Live
        && HasTarget.Source == DataSourceKind.Live
        && InCombat.Source == DataSourceKind.Live;

    /// <summary>The reason the state is unusable, or null when it can be planned on.</summary>
    public string? UnusableReason => IsPlannable
        ? null
        : Hp.FailureReason ?? MaxHp.FailureReason ?? Mp.FailureReason
          ?? HasTarget.FailureReason ?? InCombat.FailureReason
          ?? "world_state_incomplete";

    /// <summary>Nothing is known. Planning on this is refused rather than guessed.</summary>
    public static Gate3WorldState Unobserved(string reason) => new(
        ClassifiedValue<int>.Unknown(reason),
        ClassifiedValue<int>.Unknown(reason),
        ClassifiedValue<int>.Unknown(reason),
        ClassifiedValue<bool>.Unknown(reason),
        ClassifiedValue<bool>.Unknown(reason));

    /// <summary>State read from the real client.</summary>
    public static Gate3WorldState Live(int hp, int maxHp, int mp, bool hasTarget, bool inCombat, DateTime? observedAtUtc = null) => new(
        ClassifiedValue<int>.Live(hp, observedAtUtc),
        ClassifiedValue<int>.Live(maxHp, observedAtUtc),
        ClassifiedValue<int>.Live(mp, observedAtUtc),
        ClassifiedValue<bool>.Live(hasTarget, observedAtUtc),
        ClassifiedValue<bool>.Live(inCombat, observedAtUtc));

    /// <summary>
    /// Hypothetical state, for dry runs and tests.
    /// </summary>
    /// <remarks>
    /// Labelled SIMULATED on purpose, so a plan built on it can never be mistaken
    /// for one built on the game. The orchestrator refuses to let a simulated state
    /// reach a live effector.
    /// </remarks>
    public static Gate3WorldState Simulated(int hp, int maxHp, int mp, bool hasTarget, bool inCombat) => new(
        ClassifiedValue<int>.Simulated(hp),
        ClassifiedValue<int>.Simulated(maxHp),
        ClassifiedValue<int>.Simulated(mp),
        ClassifiedValue<bool>.Simulated(hasTarget),
        ClassifiedValue<bool>.Simulated(inCombat));
}

/// <summary>Supplies the state the pipeline plans from.</summary>
public interface IWorldStateSource
{
    Task<Gate3WorldState> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the planning state from the Gate 1 canonical snapshot.
/// </summary>
/// <remarks>
/// <para>
/// This is the adapter that joins Gate 3 to the real runtime. It reads the
/// gameplay observation the Gate 1 snapshot carries and converts it field by
/// field, preserving each field's classification: what the provider read stays
/// read, what it could not stays UNKNOWN with the provider's own reason.
/// </para>
/// <para>
/// With no provider attached — the default — every field is UNKNOWN with
/// <c>gameplay_provider_not_available</c> and Gate 3 refuses to plan, exactly as
/// before. That is the correct result, not a stub: Gate 3 cannot plan against the
/// live game until something reads the game, and the refusal is what stops
/// someone feeding the planner numbers by hand.
/// </para>
/// <para>
/// A partially mapped provider is the interesting case and it is handled the same
/// way. If the operator's protocol map pins HP and MP but not the combat flag,
/// three fields carry values and two say <c>combat_flag_not_mapped</c>, and
/// <see cref="Gate3WorldState.IsPlannable"/> is false because of the two. Filling
/// them with false to get a plannable state is precisely the invention this chain
/// exists to prevent.
/// </para>
/// </remarks>
public sealed class Gate1SnapshotWorldStateSource : IWorldStateSource
{
    private readonly Func<Gate1CanonicalSnapshot> _snapshot;

    public Gate1SnapshotWorldStateSource(Func<Gate1CanonicalSnapshot> snapshot)
        => _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

    public Task<Gate3WorldState> ReadAsync(CancellationToken cancellationToken = default)
    {
        Gate1CanonicalSnapshot snapshot;
        try
        {
            snapshot = _snapshot();
        }
        catch (Exception ex)
        {
            return Task.FromResult(Gate3WorldState.Unobserved($"snapshot_failed:{ex.GetType().Name}"));
        }

        // No client attached means there is nothing to plan about, and that is a
        // different reason from "attached but gameplay unreadable". Both end in
        // UNKNOWN; only the reason differs, and the operator needs the difference.
        if (snapshot.Client.Attached.Value != true)
            return Task.FromResult(Gate3WorldState.Unobserved(
                snapshot.Client.Attached.FailureReason ?? "client_not_attached"));

        if (snapshot.Client.Gameplay is not { } gameplay)
        {
            // No provider bound. The snapshot's own reason is preferred over a
            // literal here so the two can never disagree about why.
            return Task.FromResult(Gate3WorldState.Unobserved(
                snapshot.Client.GameplayBaseline.FailureReason ?? "gameplay_provider_not_available"));
        }

        return Task.FromResult(new Gate3WorldState(
            Hp: gameplay.Hp,
            MaxHp: gameplay.MaxHp,
            Mp: gameplay.Mp,
            HasTarget: gameplay.HasTarget,
            InCombat: gameplay.InCombat));
    }
}
