using System.Globalization;
using System.Runtime.Versioning;
using NosAi.Core.WorldModel;
using NosAi.LiveIntegration;
using NosAi.Runtime.GameData;
using NosAi.Runtime.Perception;
using NosAi.Runtime.WorldModel.Fusion;
using CatalogueClassifier = NosAi.Runtime.Autonomy.CatalogueClassifier;

namespace NosAi.Runtime.Tactical;

/// <summary>
/// One open view of the combat-relevant world: the mobs around the character,
/// and the character's own facts, both as the canonical World Model holds them.
/// </summary>
/// <remarks>
/// <para>
/// Held open for a whole command invocation and observed once per round, so a
/// multi-round command opens the packet capture once rather than once per
/// round. The capture is the expensive half; the per-round observation is a
/// poll of what it has already collected plus two client-memory reads.
/// </para>
/// <para>
/// Shared by <see cref="CombatReportCommand"/>, which prints what it sees, and
/// <see cref="EngageCommand"/>, which refuses to act on what it cannot see.
/// One implementation on purpose: the picture an operator is shown and the
/// picture a refusal is computed from must be the same picture, or the report
/// stops predicting the refusal.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class LiveCombatObserver : IDisposable
{
    private readonly LiveObservationScope _feed;
    private readonly GameReferenceDatabase? _catalogue;
    private readonly CatalogueClassifier _classifier;
    private readonly EntityId _playerId;
    private long _version;

    private LiveCombatObserver(
        LiveObservationScope feed,
        GameReferenceDatabase? catalogue,
        EntityId playerId)
    {
        _feed = feed;
        _catalogue = catalogue;
        _classifier = new CatalogueClassifier(catalogue);
        _playerId = playerId;
    }

    /// <summary>
    /// Opens the packet capture and the reference catalogue for
    /// <paramref name="processId"/>, or names why it could not.
    /// </summary>
    /// <param name="processId">The attached client's process id.</param>
    /// <param name="failureReason">Why no observer could be opened, when none was.</param>
    /// <param name="catalogueWarning">
    /// Why the reference catalogue is missing, when the capture opened without
    /// it. Not a failure: an observer with no catalogue establishes no vnum as
    /// a monster and therefore reports no mobs, which is a different and
    /// honest answer from "could not look".
    /// </param>
    public static LiveCombatObserver? TryOpen(int processId, out string? failureReason, out string? catalogueWarning)
    {
        catalogueWarning = null;

        LiveObservationScope? feed = LiveObservationScope.TryOpen(processId, out failureReason);
        if (feed is null)
            return null;

        if (!GameReferenceLocator.TryOpen(out GameReferenceDatabase? catalogue, out catalogueWarning))
            catalogue = null;

        var playerId = new EntityId(string.Create(CultureInfo.InvariantCulture, $"player-{processId}"));
        return new LiveCombatObserver(feed, catalogue, playerId);
    }

    /// <summary>
    /// This moment's mobs and player facts, or a named reason why the client
    /// could not be read.
    /// </summary>
    /// <remarks>
    /// The player's position is required and its absence is a failure: every
    /// combat constraint is measured from it, so a verdict computed without it
    /// would only ever say <c>player_position_unknown</c>. The vitals are
    /// optional and their absence produces an explicitly Unknown health rather
    /// than a fabricated one.
    /// </remarks>
    public bool TryObserve(
        ClientMemorySession attached,
        DateTime nowUtc,
        out Player player,
        out EquatableArray<Mob> mobs,
        out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(attached);

        player = null!;
        mobs = EquatableArray<Mob>.Empty;

        if (!attached.TryReadPlayer(out PlayerObjectReading reading, out failureReason))
            return false;

        GameplayObservation observation = _feed.Gateway.Capture().Gameplay;
        mobs = GameplayObservationProjector
            .Project(observation, _playerId, Interlocked.Increment(ref _version), nowUtc, _classifier.Classify)
            .Mobs;

        player = BuildPlayer(attached, reading, nowUtc);
        failureReason = null;
        return true;
    }

    /// <summary>
    /// The player as this observer can actually read them: a real position
    /// from the client's own memory, real HP bounds when the vitals read
    /// succeeds, and everything else explicitly Unknown or empty.
    /// </summary>
    /// <remarks>
    /// <see cref="Player.Skills"/> stays empty because no observation channel
    /// in this project reads the character's skill list. That is why callers
    /// judge a candidate with
    /// <see cref="NosAi.Core.WorldModel.Combat.CombatPlanner.CheckTargetConstraints"/>
    /// rather than the full hard-constraint check, whose skill half would
    /// report a violation that is about this gap rather than about the
    /// character -- see that method's own remarks.
    /// </remarks>
    private Player BuildPlayer(ClientMemorySession attached, PlayerObjectReading reading, DateTime nowUtc)
    {
        Resource health = attached.TryReadPlayerVitals(out PlayerVitalsReading vitals, out string? vitalsFailure)
            ? new Resource(
                ResourceKind.Health,
                WorldFact<double>.Live(vitals.Hp, confidence: 1d, nowUtc),
                WorldFact<double>.Live(vitals.MaxHp, confidence: 1d, nowUtc))
            : new Resource(
                ResourceKind.Health,
                WorldFact<double>.Unknown(vitalsFailure ?? "vitals_unreadable", nowUtc),
                WorldFact<double>.Unknown(vitalsFailure ?? "vitals_unreadable", nowUtc));

        return new Player(
            _playerId,
            WorldFact<WorldPosition>.Live(new WorldPosition(reading.X, reading.Y), confidence: 1d, nowUtc),
            WorldFact<float>.Unknown("orientation_not_read", nowUtc),
            WorldFact<bool>.Unknown("alive_not_read", nowUtc),
            WorldFact<MapId>.Unknown("map_not_read", nowUtc),
            new CombatantStatus(
                EquatableArray<Resource>.From(new[] { health }),
                EquatableArray<StatusEffect>.Empty),
            EquatableArray<Skill>.Empty,
            EquatableArray<Cooldown>.Empty,
            EquatableArray<InventoryItem>.Empty,
            EquatableArray<EquipmentItem>.Empty);
    }

    public void Dispose()
    {
        _feed.Dispose();
        _catalogue?.Dispose();
    }
}
