using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;

namespace NosAi.LiveIntegration;

/// <summary>
/// One reading of the game's own state, with the provenance of every field.
/// </summary>
/// <remarks>
/// <para>
/// The thing the whole project has been missing. Everything Windows can answer
/// for about the client — process, PID, window, handle, responding, visible — has
/// been LIVE and verified since Gate 1. Everything about the <i>game</i> has been
/// UNKNOWN, because nothing read it, and that single gap is what keeps Gate 2's
/// world model without real input, Gate 3 planning over an unobserved state, and
/// Gates 4 to 6 able to demonstrate only themselves.
/// </para>
/// <para>
/// This type is deliberately per-field classified rather than a struct of plain
/// numbers. A provider that reads HP but not the combat flag must be able to say
/// so, and the difference between "in combat: false" and "nobody knows" is the
/// difference between a safe decision and a confident wrong one.
/// </para>
/// </remarks>
public sealed record GameplayObservation(
    ClassifiedValue<int> Hp,
    ClassifiedValue<int> MaxHp,
    ClassifiedValue<int> Mp,
    ClassifiedValue<bool> HasTarget,
    ClassifiedValue<bool> InCombat,
    ClassifiedValue<int> EntitiesInView,
    DateTime ObservedAtUtc)
{
    /// <summary>Nothing was read. Every field says why.</summary>
    public static GameplayObservation Unobserved(string reason, DateTime? atUtc = null) => new(
        ClassifiedValue<int>.Unknown(reason),
        ClassifiedValue<int>.Unknown(reason),
        ClassifiedValue<int>.Unknown(reason),
        ClassifiedValue<bool>.Unknown(reason),
        ClassifiedValue<bool>.Unknown(reason),
        ClassifiedValue<int>.Unknown(reason),
        atUtc ?? DateTime.UtcNow);

    /// <summary>Whether the vitals a planner needs are all present.</summary>
    public bool HasVitals => Hp.HasValue && MaxHp.HasValue && Mp.HasValue;

    /// <summary>
    /// Why this observation cannot be planned on, or null when it can.
    /// </summary>
    public string? UnusableReason => HasVitals
        ? null
        : Hp.FailureReason ?? MaxHp.FailureReason ?? Mp.FailureReason ?? "gameplay_incomplete";

    /// <summary>
    /// The shape the Gate 1 snapshot publishes under <c>gameplayBaseline</c>.
    /// </summary>
    /// <remarks>
    /// Additive on <c>gate1.snapshot.v1</c>: the key already existed and carried
    /// an UNKNOWN, so a reader that ignores the new inner fields sees what it saw
    /// before. Every field keeps its own classification on the wire, because a
    /// consumer that flattens them cannot tell an unread field from a zero.
    /// </remarks>
    public object ToWire() => new
    {
        hp = Hp.ToWire(),
        maxHp = MaxHp.ToWire(),
        mp = Mp.ToWire(),
        hasTarget = HasTarget.ToWire(),
        inCombat = InCombat.ToWire(),
        entitiesInView = EntitiesInView.ToWire(),
        observedAtUtc = ObservedAtUtc,
    };
}

/// <summary>
/// Reads the game's own state.
/// </summary>
/// <remarks>
/// <para>
/// The seam ADR-0012 left open and ADR-0014 decided who may fill. An
/// implementation must never return a value it did not read: an unobserved field
/// is UNKNOWN with a reason, and a provider that cannot tell a correct value from
/// a wrong one must not classify it LIVE.
/// </para>
/// <para>
/// Which implementation is attached is the operator's decision and carries the
/// operator's risk, per ADR-0014. The runtime's part is to make sure that
/// whichever one is attached cannot lie about what it knows.
/// </para>
/// </remarks>
public interface IGameplayProvider
{
    /// <summary>A short name for the source, for the snapshot and the logs.</summary>
    string Name { get; }

    /// <summary>Reads the current state, or says why it could not.</summary>
    GameplayObservation Observe();
}

/// <summary>
/// A gameplay provider over the scoped network observation channel.
/// </summary>
/// <remarks>
/// <para>
/// Reads what the operator's protocol map describes and nothing else. With no map
/// entry for the player vitals, HP, max HP and MP come back UNKNOWN with
/// <c>player_vitals_not_mapped</c> — and Gate 3 goes on refusing to plan, which
/// is right: a ratio is not an HP, and manufacturing a max HP to turn one into
/// the other would be the invented number this whole path exists to prevent.
/// </para>
/// <para>
/// Entities in view is reported separately because it is genuinely known whenever
/// the channel decodes at all, and it is useful before the vitals are pinned.
/// </para>
/// </remarks>
public sealed class NetworkGameplayProvider : IGameplayProvider
{
    private readonly NetworkWorldFeed _feed;
    private readonly TimeProvider _clock;

    /// <inheritdoc />
    public string Name => "network_observation";

    public NetworkGameplayProvider(NetworkWorldFeed feed, TimeProvider? clock = null)
    {
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public GameplayObservation Observe()
    {
        DateTime now = _clock.GetUtcNow().UtcDateTime;
        NetworkObservationReport report = _feed.Poll();

        if (report.Source == DataSourceKind.Unknown)
            return GameplayObservation.Unobserved("no_capture_backend_attached", now);

        // The channel is attached and running. Entities are known even when the
        // vitals are not, so they are reported rather than withheld along with them.
        int entities = report.Sightings.Count(s => s.EntityId != 0);
        ClassifiedValue<int> entitiesInView = Classify(entities, report.Source, now);

        if (report.Vitals is not { } vitals)
        {
            string reason = report.DecodedPackets == 0
                ? "nothing_decoded"
                : "player_vitals_not_mapped";
            return GameplayObservation.Unobserved(reason, now) with { EntitiesInView = entitiesInView };
        }

        return new GameplayObservation(
            Hp: Classify(vitals.Hp, vitals.Source, now),
            MaxHp: Classify(vitals.MaxHp, vitals.Source, now),
            Mp: Classify(vitals.Mp, vitals.Source, now),
            HasTarget: vitals.HasTarget is bool target
                ? Classify(target, vitals.Source, now)
                : ClassifiedValue<bool>.Unknown("target_flag_not_mapped"),
            InCombat: vitals.InCombat is bool combat
                ? Classify(combat, vitals.Source, now)
                : ClassifiedValue<bool>.Unknown("combat_flag_not_mapped"),
            EntitiesInView: entitiesInView,
            ObservedAtUtc: now);
    }

    private static ClassifiedValue<T> Classify<T>(T value, DataSourceKind source, DateTime at) => source switch
    {
        DataSourceKind.Live => ClassifiedValue<T>.Live(value, at),
        DataSourceKind.Derived => ClassifiedValue<T>.Derived(value, at),
        DataSourceKind.Cached => ClassifiedValue<T>.Cached(value, at),
        DataSourceKind.Simulated => ClassifiedValue<T>.Simulated(value, at),
        _ => ClassifiedValue<T>.Unknown("source_unknown"),
    };
}

/// <summary>
/// The provider in force when none is attached.
/// </summary>
/// <remarks>
/// Exists so that "no provider" is a provider that says no, rather than a null
/// somewhere that each caller handles its own way. The reason it gives is the one
/// the Gate 1 snapshot has been publishing since the gate closed.
/// </remarks>
public sealed class UnavailableGameplayProvider : IGameplayProvider
{
    public static readonly UnavailableGameplayProvider Instance = new();

    public string Name => "none";

    public GameplayObservation Observe() =>
        GameplayObservation.Unobserved("gameplay_provider_not_available");
}
