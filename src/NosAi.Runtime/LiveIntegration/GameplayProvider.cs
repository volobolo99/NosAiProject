using System.Globalization;
using NosAi.Runtime.Autonomy;
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
/// <para>
/// The fields C1 added — <see cref="Entities"/>, <see cref="PlayerPosition"/>,
/// <see cref="HitBy"/>, <see cref="SkillsReady"/>, <see cref="Inventory"/>,
/// <see cref="LastPickup"/>, <see cref="GroundItems"/> — are init-only rather
/// than positional so that every existing construction site keeps compiling and
/// keeps meaning what it meant, the same treatment the new fields on
/// <see cref="NetworkObservationReport"/> received. A provider that does not
/// set them publishes them UNKNOWN with a reason, which is the truth about that
/// provider and is not a zero, an empty list or the map origin.
/// </para>
/// </remarks>
public sealed record GameplayObservation(
    ClassifiedValue<int> Hp,
    ClassifiedValue<int> MaxHp,
    ClassifiedValue<int> Mp,
    ClassifiedValue<int> MaxMp,
    ClassifiedValue<bool> HasTarget,
    ClassifiedValue<bool> InCombat,
    ClassifiedValue<int> EntitiesInView,
    DateTime ObservedAtUtc)
{
    /// <summary>
    /// The reason a C1 field carries when the provider that built this
    /// observation never set it.
    /// </summary>
    public const string NotPublishedReason = "not_published_by_provider";

    /// <summary>
    /// The reason the position carries when nothing has read it: a provider
    /// that has no memory reader bound has nothing to say here, and says so.
    /// </summary>
    public const string PlayerPositionNotReadReason = "player_position_not_read";

    /// <summary>
    /// Every entity the provider currently holds a position for, each with the
    /// instant that position was stated.
    /// </summary>
    /// <remarks>
    /// The list the target-selection rule reads. Its source is the weakest of
    /// its members' — a member remembered from an earlier poll is CACHED, as a
    /// remembered vitals reading is — and its instant is the newest statement
    /// among them. An entity's health is null when no packet ever stated it,
    /// never full and never zero.
    /// </remarks>
    public ClassifiedValue<IReadOnlyList<SelectableEntity>> Entities { get; init; }
        = ClassifiedValue<IReadOnlyList<SelectableEntity>>.Unknown(NotPublishedReason);

    /// <summary>
    /// Where the character is standing, or why nobody knows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default is UNKNOWN with a reason, never the map origin. The server never
    /// sends the player's own position (docs/PROTOCOLLO_NOSTALE.md): it is
    /// client-authoritative, so <see cref="NetworkGameplayProvider"/> publishes
    /// it UNKNOWN with <c>player_position_not_on_wire</c>, and
    /// <see cref="PositionAwareGameplayProvider"/> is where a memory reader fills
    /// it in. Until one is bound it stays UNKNOWN: not the map origin, not the
    /// last known square.
    /// </para>
    /// </remarks>
    public ClassifiedValue<MapPoint> PlayerPosition { get; init; }
        = ClassifiedValue<MapPoint>.Unknown(PlayerPositionNotReadReason);

    /// <summary>
    /// Who last hit the controlled character, with the instant of the hit as the
    /// value's <see cref="ClassifiedValue{T}.ObservedAtUtc"/>.
    /// </summary>
    /// <remarks>
    /// Published only once the character's own id is known from the wire, and
    /// kept across polls without expiring: how long an aggression stays a reason
    /// is the reactive rule's decay window (C6-1), measured from this instant.
    /// </remarks>
    public ClassifiedValue<Aggressor> HitBy { get; init; }
        = ClassifiedValue<Aggressor>.Unknown(NotPublishedReason);

    /// <summary>
    /// The entity the controlled character last acted on, from <c>ct</c>, with
    /// the instant of that packet as the value's
    /// <see cref="ClassifiedValue{T}.ObservedAtUtc"/>.
    /// </summary>
    /// <remarks>
    /// The wire's <i>which</i>; <see cref="HasTarget"/> is the screen's
    /// <i>whether</i> (ADR-0018). Sticky by nature — nothing on the wire clears a
    /// selection — so it names the last selection and never establishes that one
    /// still exists.
    /// </remarks>
    public ClassifiedValue<TargetedEntity> SelectedTarget { get; init; }
        = ClassifiedValue<TargetedEntity>.Unknown(NotPublishedReason);

    /// <summary>The most recent <c>sr</c> per skill slot, ordered by slot.</summary>
    public ClassifiedValue<IReadOnlyList<SkillReady>> SkillsReady { get; init; }
        = ClassifiedValue<IReadOnlyList<SkillReady>>.Unknown(NotPublishedReason);

    /// <summary>The most recent <c>ivn</c> per inventory slot, ordered by kind then slot.</summary>
    public ClassifiedValue<IReadOnlyList<InventorySlotReading>> Inventory { get; init; }
        = ClassifiedValue<IReadOnlyList<InventorySlotReading>>.Unknown(NotPublishedReason);

    /// <summary>The most recent <c>get</c>, with its instant.</summary>
    public ClassifiedValue<ItemPickup> LastPickup { get; init; }
        = ClassifiedValue<ItemPickup>.Unknown(NotPublishedReason);

    /// <summary>Items seen on the ground and not yet seen picked up, ordered by drop id.</summary>
    public ClassifiedValue<IReadOnlyList<GroundItem>> GroundItems { get; init; }
        = ClassifiedValue<IReadOnlyList<GroundItem>>.Unknown(NotPublishedReason);

    /// <summary>Nothing was read. Every field says why.</summary>
    public static GameplayObservation Unobserved(string reason, DateTime? atUtc = null) => new(
        ClassifiedValue<int>.Unknown(reason),
        ClassifiedValue<int>.Unknown(reason),
        ClassifiedValue<int>.Unknown(reason),
        ClassifiedValue<int>.Unknown(reason),
        ClassifiedValue<bool>.Unknown(reason),
        ClassifiedValue<bool>.Unknown(reason),
        ClassifiedValue<int>.Unknown(reason),
        atUtc ?? DateTime.UtcNow)
    {
        Entities = ClassifiedValue<IReadOnlyList<SelectableEntity>>.Unknown(reason),
        PlayerPosition = ClassifiedValue<MapPoint>.Unknown(reason),
        HitBy = ClassifiedValue<Aggressor>.Unknown(reason),
        SelectedTarget = ClassifiedValue<TargetedEntity>.Unknown(reason),
        SkillsReady = ClassifiedValue<IReadOnlyList<SkillReady>>.Unknown(reason),
        Inventory = ClassifiedValue<IReadOnlyList<InventorySlotReading>>.Unknown(reason),
        LastPickup = ClassifiedValue<ItemPickup>.Unknown(reason),
        GroundItems = ClassifiedValue<IReadOnlyList<GroundItem>>.Unknown(reason),
    };

    /// <summary>Whether the vitals a planner needs are all present.</summary>
    /// <remarks>
    /// <see cref="MaxMp"/> is deliberately not among them. No rule reads it yet,
    /// and requiring it would make every decoder that does not map it unable to
    /// plan — a behaviour change smuggled in behind a published field. The C1
    /// fields are not among them either, for the same reason: each gates only
    /// the rule that reads it (ADR-0016).
    /// </remarks>
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
    /// <remarks>
    /// <para>
    /// <c>maxMp</c> was added by F4-1b and is why the version did not move.
    /// ADR-0005 requires a contract change to be versioned <i>when compatibility
    /// can be affected</i>, and an added key inside a value a reader already
    /// treats as opaque cannot affect it: <c>GuardSnapshotView</c> and
    /// <c>AttachedSnapshot</c> both read <c>gameplayBaseline</c> as one
    /// classified value and never enumerate what is inside it. Contract tests
    /// hold that open, so the day a reader does start enumerating, the version
    /// has to move with it.
    /// </para>
    /// <para>
    /// C1 adds <c>entities</c>, <c>playerPosition</c>, <c>hitBy</c>,
    /// <c>skillsReady</c>, <c>inventory</c>, <c>lastPickup</c> and
    /// <c>groundItems</c> on the same precedent: new keys beside the existing
    /// ones, no existing key renamed or given a new meaning. Each is one
    /// classified value whose inner shape is spelled out here rather than left
    /// to a serializer, so the C# and Python sides cannot drift on casing, and
    /// every instant inside is formatted the way
    /// <see cref="ClassifiedValue{T}.ToWire"/> formats its own.
    /// </para>
    /// </remarks>
    public object ToWire() => new
    {
        hp = Hp.ToWire(),
        maxHp = MaxHp.ToWire(),
        mp = Mp.ToWire(),
        maxMp = MaxMp.ToWire(),
        hasTarget = HasTarget.ToWire(),
        inCombat = InCombat.ToWire(),
        entitiesInView = EntitiesInView.ToWire(),
        observedAtUtc = ObservedAtUtc,
        entities = Project(Entities, list => list.Select(e => new
        {
            entityId = e.EntityId,
            x = e.At.X,
            y = e.At.Y,
            hpRatio = e.HpRatio,
            vnum = e.Vnum,
            observedAtUtc = Iso(e.ObservedAtUtc),
        }).ToArray()),
        playerPosition = Project(PlayerPosition, p => new { x = p.X, y = p.Y }),
        hitBy = Project(HitBy, a => new { entityId = a.EntityId, entityType = a.EntityType }),
        selectedTarget = Project(SelectedTarget, t => new { entityId = t.EntityId, entityType = t.EntityType }),
        skillsReady = Project(SkillsReady, list => list.Select(s => new
        {
            slot = s.Slot,
            observedAtUtc = Iso(s.ObservedAtUtc),
        }).ToArray()),
        inventory = Project(Inventory, list => list.Select(i => new
        {
            inventoryKind = i.InventoryKind,
            slot = i.Slot,
            vnum = i.Vnum,
            amount = i.Amount,
            rarity = i.Rarity,
            observedAtUtc = Iso(i.ObservedAtUtc),
        }).ToArray()),
        lastPickup = Project(LastPickup, p => new
        {
            dropId = p.DropId,
            takerType = p.TakerType,
            takerId = p.TakerId,
            byPlayer = p.ByPlayer,
        }),
        groundItems = Project(GroundItems, list => list.Select(g => new
        {
            dropId = g.DropId,
            vnum = g.Vnum,
            x = g.X,
            y = g.Y,
            amount = g.Amount,
            ownerId = g.OwnerId,
            observedAtUtc = Iso(g.ObservedAtUtc),
        }).ToArray()),
    };

    /// <summary>
    /// The wire form of a classified value whose inner shape this type decides,
    /// keeping the classification untouched: source, instant, warning and
    /// reason travel exactly as <see cref="ClassifiedValue{T}.ToWire"/> sends them.
    /// </summary>
    private static object Project<T>(ClassifiedValue<T> value, Func<T, object> shape)
        => new ClassifiedValue<object>(
            value.HasValue ? shape(value.Value) : null!,
            value.Source,
            value.ObservedAtUtc,
            value.HasObservedValue,
            value.Warning,
            value.FailureReason).ToWire();

    private static string Iso(DateTime at) => at.ToUniversalTime()
        .ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
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
/// <para>
/// <b>What C1 retains, and by what rule.</b> The wire mentions an entity only
/// when something happens to it, so a monster standing still is absent from
/// every batch after the one that placed it; the entity table, the skill slots,
/// the inventory slots and the ground items are therefore kept across polls.
/// One rule governs all of them, and it is the vitals' rule: what this poll's
/// packets stated keeps their provenance, and what is remembered from an
/// earlier poll is CACHED, carrying the instant it was really observed. The
/// age is on every entry, so the consumer decides what is too old —
/// <c>TargetSelector</c> with its own bound for entities, the reactive rule's
/// decay window for the hit. What this provider bounds is only its memory:
/// an entity or ground item not mentioned within <see cref="MaxEntityRetention"/>
/// is forgotten, which is a statement about this table and not a claim that
/// the thing left the map. The hit and the pickup are single most-recent facts
/// and are kept until a newer one; the own id is read off the feed, where it
/// is kept for the same reason.
/// </para>
/// <para>
/// <b>The player's own position is not on the wire.</b> It is published UNKNOWN
/// with <c>player_position_not_on_wire</c>, and
/// <see cref="PositionAwareGameplayProvider"/> is where a memory reader joins
/// in. Nothing here substitutes for it.
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

    /// <summary>
    /// How long an entity or a ground item stays in the table after the wire
    /// last mentioned it.
    /// </summary>
    /// <remarks>
    /// Longer than <c>TargetSelectionPolicy</c>'s default sighting age on
    /// purpose: the selector's bound is the one that decides what may be aimed
    /// at, and this one only keeps the table from growing without limit over a
    /// long session. An entry older than the selector's bound is published with
    /// its age and refused there by name, which is a better diagnostic than a
    /// table that silently emptied.
    /// </remarks>
    public static readonly TimeSpan DefaultMaxEntityRetention = TimeSpan.FromSeconds(60);

    /// <summary>The reason the position carries while nothing but the wire is bound.</summary>
    public const string PlayerPositionNotOnWireReason = "player_position_not_on_wire";

    private readonly NetworkWorldFeed _feed;
    private readonly TimeProvider _clock;
    private PlayerVitals? _lastVitals;
    private DateTime _lastVitalsAtUtc;

    private readonly Dictionary<long, Retained<SelectableEntity>> _entities = new();
    private bool _entityEverSeen;
    private string? _entityWarning;
    private PlayerHit? _lastHit;
    private bool _lastHitFresh;
    private PlayerTargetSelection? _lastTarget;
    private bool _lastTargetFresh;
    private readonly Dictionary<int, Retained<SkillReady>> _skillsReady = new();
    private readonly Dictionary<(int Kind, int Slot), Retained<InventorySlotReading>> _inventory = new();
    private ItemPickup? _lastPickup;
    private bool _lastPickupFresh;
    private readonly Dictionary<long, Retained<GroundItem>> _groundItems = new();
    private bool _groundItemEverSeen;

    /// <inheritdoc />
    public string Name => "network_observation";

    /// <summary>How old a reading may be and still be republished as CACHED.</summary>
    public TimeSpan MaxVitalsAge { get; }

    /// <summary>How long an unmentioned entity or ground item stays in the table.</summary>
    public TimeSpan MaxEntityRetention { get; }

    /// <param name="feed">The network channel to read.</param>
    /// <param name="clock">Time source; the system clock unless a test supplies one.</param>
    /// <param name="maxVitalsAge">
    /// How long the last reading stays publishable as CACHED once no new one
    /// arrives. <see cref="DefaultMaxVitalsAge"/> when omitted;
    /// <see cref="TimeSpan.Zero"/> turns retention off, so a batch without vitals
    /// reports UNKNOWN at once.
    /// </param>
    /// <param name="maxEntityRetention">
    /// How long an entity or ground item stays in the table after its last
    /// mention. <see cref="DefaultMaxEntityRetention"/> when omitted.
    /// </param>
    public NetworkGameplayProvider(
        NetworkWorldFeed feed,
        TimeProvider? clock = null,
        TimeSpan? maxVitalsAge = null,
        TimeSpan? maxEntityRetention = null)
    {
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
        _clock = clock ?? TimeProvider.System;
        TimeSpan age = maxVitalsAge ?? DefaultMaxVitalsAge;
        if (age < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxVitalsAge));
        MaxVitalsAge = age;
        TimeSpan retention = maxEntityRetention ?? DefaultMaxEntityRetention;
        if (retention < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxEntityRetention));
        MaxEntityRetention = retention;
    }

    /// <inheritdoc />
    public GameplayObservation Observe()
    {
        DateTime now = _clock.GetUtcNow().UtcDateTime;
        NetworkObservationReport report = _feed.Poll();

        if (report.Source == DataSourceKind.Unknown)
            return GameplayObservation.Unobserved("no_capture_backend_attached", now);

        ClassifiedValue<int> entitiesInView = CountEntities(report, now);
        // Absorbed before the vitals are decided, so a batch with no stat in it
        // still moves the entities, the hit and the inventory forward.
        Absorb(report, now);

        if (report.Vitals is { } fresh)
        {
            // The time the packet crossed the wire, not the time this poll ran.
            // They are the same thing on a live capture and hours apart on a
            // replay, and the difference is exactly what a freshness rule needs to
            // tell a current reading from a recorded one (ADR-0016).
            DateTime observedAt = fresh.ObservedAtUtc ?? now;
            _lastVitals = fresh;
            _lastVitalsAtUtc = observedAt;
            return WithWorld(Publish(fresh, fresh.Source, observedAt, now, entitiesInView));
        }

        // Nothing in this batch. The last reading is still a reading, for a while,
        // and it is published as what it is: observed, and no longer current.
        if (_lastVitals is { } remembered && now - _lastVitalsAtUtc <= MaxVitalsAge)
            return WithWorld(Publish(remembered, DataSourceKind.Cached, _lastVitalsAtUtc, now, entitiesInView));

        return WithWorld(
            GameplayObservation.Unobserved(MissingVitalsReason(report), now) with { EntitiesInView = entitiesInView });
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
            // Absent when the map does not describe the field, which is not the
            // same as a maximum of zero: a decoder that cannot read it says so,
            // and the snapshot carries the reason rather than a number.
            MaxMp: vitals.MaxMp is { } maxMp
                ? Classify(maxMp, source, observedAtUtc)
                : ClassifiedValue<int>.Unknown("max_mp_not_mapped"),
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

    // ------------------------------------------------------------------ C1

    /// <summary>
    /// A retained reading: the value, its own provenance, when the wire last
    /// stated it, and whether this poll stated it.
    /// </summary>
    private sealed record Retained<T>(T Value, DataSourceKind Source, DateTime StatedAtUtc, bool Fresh);

    /// <summary>Moves every retained table forward by one batch.</summary>
    private void Absorb(NetworkObservationReport report, DateTime now)
    {
        AbsorbEntities(report, now);
        AbsorbHit(report);
        AbsorbTarget(report);
        AbsorbSkills(report);
        AbsorbInventory(report);
        AbsorbGroundItems(report, now);
        AbsorbPickup(report);
    }

    private void AbsorbEntities(NetworkObservationReport report, DateTime now)
    {
        foreach (Retained<SelectableEntity> retained in _entities.Values.ToList())
            _entities[retained.Value.EntityId] = retained with { Fresh = false };

        int withoutInstant = 0, offGrid = 0;
        foreach (EntitySighting sighting in report.Sightings)
        {
            // Entity id 0 is the channel's convention for the controlled player,
            // and the player is not something the player aims at.
            if (sighting.EntityId == 0)
                continue;

            // A sighting without an instant cannot become a selectable entity:
            // the selector measures its age, and stamping it with this poll's
            // clock would make a replayed position look current (ADR-0016).
            if (sighting.PositionObservedAtUtc is not { } positionAt)
            {
                withoutInstant++;
                continue;
            }

            // The wire states whole squares. A fractional coordinate is a
            // reading nobody sent, and rounding it would aim at a square nobody
            // observed the entity on.
            if (!TryToPoint(sighting.X, sighting.Y, out MapPoint at))
            {
                offGrid++;
                continue;
            }

            // Last mentioned by whichever half this packet stated.
            DateTime stated = sighting.HpObservedAtUtc is { } hpAt && hpAt > positionAt ? hpAt : positionAt;
            _entities[sighting.EntityId] = new Retained<SelectableEntity>(
                new SelectableEntity(sighting.EntityId, at, sighting.HpRatio, positionAt, sighting.Vnum),
                sighting.Source, stated, Fresh: true);
            _entityEverSeen = true;
        }

        foreach (GameEvent gameEvent in report.Events)
        {
            if (gameEvent.Kind == GameEventKind.EntityDeath)
                _entities.Remove(gameEvent.EntityId);
        }

        Expire(_entities, now);

        _entityWarning = (withoutInstant, offGrid) switch
        {
            (0, 0) => null,
            (> 0, 0) => $"{withoutInstant} sighting(s) carried no observation instant and were not published",
            (0, > 0) => $"{offGrid} sighting(s) had a fractional coordinate and were not published",
            _ => $"{withoutInstant} sighting(s) without an instant and {offGrid} off the grid were not published",
        };
    }

    private void AbsorbHit(NetworkObservationReport report)
    {
        _lastHitFresh = false;
        // Never moves backwards: an out-of-order batch must not make the most
        // recent hit older than one already seen.
        if (report.LastPlayerHit is { } hit
            && (_lastHit is null || hit.ObservedAtUtc > _lastHit.ObservedAtUtc))
        {
            _lastHit = hit;
            _lastHitFresh = true;
        }
    }

    private void AbsorbTarget(NetworkObservationReport report)
    {
        _lastTargetFresh = false;
        if (report.LastPlayerTarget is { } target
            && (_lastTarget is null || target.ObservedAtUtc > _lastTarget.ObservedAtUtc))
        {
            _lastTarget = target;
            _lastTargetFresh = true;
        }
    }

    private void AbsorbSkills(NetworkObservationReport report)
    {
        foreach (Retained<SkillReady> retained in _skillsReady.Values.ToList())
            _skillsReady[retained.Value.Slot] = retained with { Fresh = false };

        foreach (SkillReady ready in report.SkillsReady)
        {
            if (_skillsReady.TryGetValue(ready.Slot, out Retained<SkillReady>? known)
                && known.Value.ObservedAtUtc > ready.ObservedAtUtc)
                continue;
            _skillsReady[ready.Slot] = new Retained<SkillReady>(ready, ready.Source, ready.ObservedAtUtc, Fresh: true);
        }
    }

    private void AbsorbInventory(NetworkObservationReport report)
    {
        foreach (Retained<InventorySlotReading> retained in _inventory.Values.ToList())
            _inventory[(retained.Value.InventoryKind, retained.Value.Slot)] = retained with { Fresh = false };

        foreach (InventorySlotReading slot in report.InventorySlots)
        {
            (int, int) key = (slot.InventoryKind, slot.Slot);
            if (_inventory.TryGetValue(key, out Retained<InventorySlotReading>? known)
                && known.Value.ObservedAtUtc > slot.ObservedAtUtc)
                continue;
            _inventory[key] = new Retained<InventorySlotReading>(slot, slot.Source, slot.ObservedAtUtc, Fresh: true);
        }
    }

    private void AbsorbGroundItems(NetworkObservationReport report, DateTime now)
    {
        foreach (Retained<GroundItem> retained in _groundItems.Values.ToList())
            _groundItems[retained.Value.DropId] = retained with { Fresh = false };

        foreach (GroundItem item in report.GroundItems)
        {
            _groundItems[item.DropId] = new Retained<GroundItem>(item, item.Source, item.ObservedAtUtc, Fresh: true);
            _groundItemEverSeen = true;
        }

        // A pickup takes the item off the ground — the catalogue matched the ids
        // — but only an item dropped before it: the report lists drops and
        // pickups separately, and a drop reusing an id after a pickup in the
        // same batch must not be erased by that earlier pickup.
        foreach (ItemPickup pickup in report.Pickups)
        {
            if (_groundItems.TryGetValue(pickup.DropId, out Retained<GroundItem>? onGround)
                && onGround.Value.ObservedAtUtc <= pickup.ObservedAtUtc)
                _groundItems.Remove(pickup.DropId);
        }

        Expire(_groundItems, now);
    }

    private void AbsorbPickup(NetworkObservationReport report)
    {
        _lastPickupFresh = false;
        foreach (ItemPickup pickup in report.Pickups)
        {
            if (_lastPickup is null || pickup.ObservedAtUtc >= _lastPickup.ObservedAtUtc)
            {
                _lastPickup = pickup;
                _lastPickupFresh = true;
            }
        }
    }

    /// <summary>Forgets what the wire has not mentioned within the retention bound.</summary>
    private void Expire<TKey, T>(Dictionary<TKey, Retained<T>> table, DateTime now) where TKey : notnull
    {
        List<TKey> expired = table
            .Where(entry => !entry.Value.Fresh && now - entry.Value.StatedAtUtc > MaxEntityRetention)
            .Select(entry => entry.Key)
            .ToList();
        foreach (TKey key in expired)
            table.Remove(key);
    }

    /// <summary>Puts the retained world onto an observation whose vitals are already decided.</summary>
    private GameplayObservation WithWorld(GameplayObservation observation) => observation with
    {
        Entities = PublishEntities(),
        PlayerPosition = ClassifiedValue<MapPoint>.Unknown(PlayerPositionNotOnWireReason),
        HitBy = _lastHit is { } hit
            ? Classify(hit.By, _lastHitFresh ? hit.Source : Remembered(hit.Source), hit.ObservedAtUtc)
            : ClassifiedValue<Aggressor>.Unknown(
                _feed.PlayerEntityId is null ? "player_entity_id_not_observed" : "no_hit_on_player_observed"),
        SelectedTarget = _lastTarget is { } selected
            ? Classify(selected.Target, _lastTargetFresh ? selected.Source : Remembered(selected.Source), selected.ObservedAtUtc)
            : ClassifiedValue<TargetedEntity>.Unknown(
                _feed.PlayerEntityId is null ? "player_entity_id_not_observed" : "no_target_selection_observed"),
        SkillsReady = PublishList(
            _skillsReady.Values.OrderBy(r => r.Value.Slot), "no_skill_ready_observed"),
        Inventory = PublishList(
            _inventory.Values.OrderBy(r => r.Value.InventoryKind).ThenBy(r => r.Value.Slot),
            "no_inventory_slot_observed"),
        LastPickup = _lastPickup is { } pickup
            ? Classify(pickup, _lastPickupFresh ? pickup.Source : Remembered(pickup.Source), pickup.ObservedAtUtc)
            : ClassifiedValue<ItemPickup>.Unknown("no_pickup_observed"),
        GroundItems = PublishList(
            _groundItems.Values.OrderBy(r => r.Value.DropId),
            _groundItemEverSeen ? "no_ground_item_retained" : "no_ground_item_observed_yet"),
    };

    private ClassifiedValue<IReadOnlyList<SelectableEntity>> PublishEntities()
    {
        ClassifiedValue<IReadOnlyList<SelectableEntity>> entities = PublishList(
            _entities.Values.OrderBy(r => r.Value.EntityId),
            _entityEverSeen ? "no_entity_retained" : "no_entities_observed_yet");
        return _entityWarning is null ? entities : entities with { Warning = _entityWarning };
    }

    /// <summary>
    /// A retained table as one classified list: the weakest member's provenance,
    /// with remembered members counted as CACHED, and the newest statement as
    /// the instant.
    /// </summary>
    private static ClassifiedValue<IReadOnlyList<T>> PublishList<T>(IEnumerable<Retained<T>> ordered, string emptyReason)
    {
        List<Retained<T>> members = ordered.ToList();
        if (members.Count == 0)
            return ClassifiedValue<IReadOnlyList<T>>.Unknown(emptyReason);

        DataSourceKind source = Weakest(members.Select(m => m.Fresh ? m.Source : Remembered(m.Source)));
        DateTime newest = members.Max(m => m.StatedAtUtc);
        return Classify<IReadOnlyList<T>>(members.Select(m => m.Value).ToList(), source, newest);
    }

    /// <summary>
    /// The provenance of a reading republished from an earlier poll: really
    /// observed, no longer current. The rule the vitals already follow.
    /// </summary>
    private static DataSourceKind Remembered(DataSourceKind source) => source switch
    {
        DataSourceKind.Live or DataSourceKind.Derived => DataSourceKind.Cached,
        _ => source,
    };

    private static DataSourceKind Weakest(IEnumerable<DataSourceKind> sources)
    {
        static int Rank(DataSourceKind kind) => kind switch
        {
            DataSourceKind.Live => 4,
            DataSourceKind.Derived => 3,
            DataSourceKind.Cached => 2,
            DataSourceKind.Simulated => 1,
            _ => 0,
        };
        DataSourceKind weakest = DataSourceKind.Live;
        foreach (DataSourceKind source in sources)
            if (Rank(source) < Rank(weakest)) weakest = source;
        return weakest;
    }

    private static bool TryToPoint(double x, double y, out MapPoint point)
    {
        point = default;
        if (double.IsNaN(x) || double.IsNaN(y) || x != Math.Floor(x) || y != Math.Floor(y))
            return false;
        if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue)
            return false;
        point = new MapPoint((int)x, (int)y);
        return true;
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
