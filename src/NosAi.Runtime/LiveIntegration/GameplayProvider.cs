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
/// Reads whatever the feed's decoder produced and nothing else. Two decoders
/// sit behind that feed: a reconstructed binary <see cref="ProtocolMap"/>, whose
/// readings can never be LIVE (the map itself is DERIVED), and
/// <see cref="NosTaleWorldProtocolDecoder"/>, which reads the world channel after
/// <see cref="NosTaleWorldDecoder"/> verified its framing. The second path is
/// the one that can publish HP as LIVE.
/// </para>
/// <para>
/// With no vitals ever read, HP, max HP and MP come back UNKNOWN — and Gate 3
/// goes on refusing to plan, which is right: a ratio is not an HP, and
/// manufacturing a max HP to turn one into the other would be the invented
/// number this whole path exists to prevent.
/// </para>
/// <para>
/// <b>Between two vitals packets the last reading is republished CACHED.</b> The
/// wire sends <c>stat</c> when the number changes, not on a schedule: 62 packets
/// in 90 s of real combat, 22 in an idle capture. Polling in small bites
/// therefore finds nothing in most batches — 63% of 64-message polls on both
/// recordings — and dropping to UNKNOWN each time would make an HP that is
/// perfectly well known unusable two polls out of three. ADR-0012 already names
/// the honest answer for a real value that is no longer fresh: CACHED, carrying
/// the time it was observed. Past <see cref="MaxVitalsAge"/> it becomes UNKNOWN
/// with <c>player_vitals_stale</c>, because an HP old enough to have been fought
/// through is not a reading any more. A consumer that needs a current number
/// checks the source and the timestamp; both are on every field.
/// </para>
/// <para>
/// <b>Entities in view is never a claimed zero.</b> A batch with no sighting in it
/// does not establish that nothing is in view — far more often the poll window
/// carried no packet that speaks about entities, which on the idle recording is
/// every single batch while 2468 movement packets go by. So zero is published as
/// UNKNOWN, and a count only when something was actually seen. It counts distinct
/// entities, not sightings: one entity moving twice in a batch is one entity.
/// </para>
/// </remarks>
public sealed class NetworkGameplayProvider : IGameplayProvider
{
    /// <summary>
    /// How long a vitals reading stays publishable as CACHED after it was observed.
    /// </summary>
    /// <remarks>
    /// Comfortably longer than the gaps between <c>stat</c> packets in the
    /// recordings, so an ordinary quiet moment does not expire a good reading,
    /// and short enough that a channel which has actually stopped delivering goes
    /// UNKNOWN rather than repeating itself.
    /// </remarks>
    public static readonly TimeSpan DefaultMaxVitalsAge = TimeSpan.FromSeconds(5);

    private readonly NetworkWorldFeed _feed;
    private readonly TimeProvider _clock;
    private PlayerVitals? _lastVitals;
    private DateTime _lastVitalsAtUtc;

    /// <inheritdoc />
    public string Name => "network_observation";

    /// <summary>How old a reading may be and still be republished as CACHED.</summary>
    public TimeSpan MaxVitalsAge { get; }

    /// <param name="feed">The network channel to read.</param>
    /// <param name="clock">Time source; the system clock unless a test supplies one.</param>
    /// <param name="maxVitalsAge">
    /// How long the last reading stays publishable as CACHED once no new one
    /// arrives. <see cref="DefaultMaxVitalsAge"/> when omitted;
    /// <see cref="TimeSpan.Zero"/> turns retention off, so a batch without vitals
    /// reports UNKNOWN at once.
    /// </param>
    public NetworkGameplayProvider(
        NetworkWorldFeed feed, TimeProvider? clock = null, TimeSpan? maxVitalsAge = null)
    {
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
        _clock = clock ?? TimeProvider.System;
        TimeSpan age = maxVitalsAge ?? DefaultMaxVitalsAge;
        if (age < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxVitalsAge));
        MaxVitalsAge = age;
    }

    /// <inheritdoc />
    public GameplayObservation Observe()
    {
        DateTime now = _clock.GetUtcNow().UtcDateTime;
        NetworkObservationReport report = _feed.Poll();

        if (report.Source == DataSourceKind.Unknown)
            return GameplayObservation.Unobserved("no_capture_backend_attached", now);

        ClassifiedValue<int> entitiesInView = CountEntities(report, now);

        if (report.Vitals is { } fresh)
        {
            // The time the packet crossed the wire, not the time this poll ran.
            // They are the same thing on a live capture and hours apart on a
            // replay, and the difference is exactly what a freshness rule needs to
            // tell a current reading from a recorded one (ADR-0016).
            DateTime observedAt = fresh.ObservedAtUtc ?? now;
            _lastVitals = fresh;
            _lastVitalsAtUtc = observedAt;
            return Publish(fresh, fresh.Source, observedAt, now, entitiesInView);
        }

        // Nothing in this batch. The last reading is still a reading, for a while,
        // and it is published as what it is: observed, and no longer current.
        if (_lastVitals is { } remembered && now - _lastVitalsAtUtc <= MaxVitalsAge)
            return Publish(remembered, DataSourceKind.Cached, _lastVitalsAtUtc, now, entitiesInView);

        return GameplayObservation.Unobserved(MissingVitalsReason(report), now)
            with { EntitiesInView = entitiesInView };
    }

    /// <summary>
    /// Why this batch has no usable vitals, distinguishing cases that look
    /// identical from the outside and are not.
    /// </summary>
    private string MissingVitalsReason(NetworkObservationReport report)
    {
        // The decoder cannot read them at all — an unfinished protocol map. No
        // amount of waiting will produce one.
        if (!report.VitalsReadable) return "player_vitals_not_mapped";
        // It can, it had one, and the one it had is too old to stand for now.
        if (_lastVitals is not null) return "player_vitals_stale";
        // It can, and nothing has carried them yet on this channel.
        return report.DecodedPackets == 0 ? "nothing_decoded" : "player_vitals_not_seen_yet";
    }

    private static GameplayObservation Publish(
        PlayerVitals vitals,
        DataSourceKind source,
        DateTime observedAtUtc,
        DateTime now,
        ClassifiedValue<int> entitiesInView)
        => new(
            Hp: Classify(vitals.Hp, source, observedAtUtc),
            MaxHp: Classify(vitals.MaxHp, source, observedAtUtc),
            Mp: Classify(vitals.Mp, source, observedAtUtc),
            HasTarget: vitals.HasTarget is bool target
                ? Classify(target, source, observedAtUtc)
                : ClassifiedValue<bool>.Unknown("target_flag_not_mapped"),
            InCombat: vitals.InCombat is bool combat
                ? Classify(combat, source, observedAtUtc)
                : ClassifiedValue<bool>.Unknown("combat_flag_not_mapped"),
            EntitiesInView: entitiesInView,
            ObservedAtUtc: now);

    /// <summary>
    /// How many distinct entities this batch actually showed, or why it cannot say.
    /// </summary>
    /// <remarks>
    /// Entity id 0 is the controlled player by the channel's convention, so it is
    /// not one of the entities in view. A batch with nothing left after that is not
    /// evidence of an empty screen — see the class remarks — so it reports UNKNOWN
    /// rather than a zero that reads exactly like an observation.
    /// </remarks>
    private static ClassifiedValue<int> CountEntities(NetworkObservationReport report, DateTime now)
    {
        int entities = report.Sightings
            .Where(s => s.EntityId != 0)
            .Select(s => s.EntityId)
            .Distinct()
            .Count();

        return entities == 0
            ? ClassifiedValue<int>.Unknown("no_entities_reported")
            : Classify(entities, report.Source, now);
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
