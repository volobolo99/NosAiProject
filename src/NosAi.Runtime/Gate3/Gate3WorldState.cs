using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
// Aliased rather than importing the whole Gate 1 namespace: several type names
// are duplicated across gates, so a broad using here would make them ambiguous.
using Gate1CanonicalSnapshot = NosAi.Runtime.Gate1.Gate1CanonicalSnapshot;
using IGameplayProvider = NosAi.LiveIntegration.IGameplayProvider;
using GameplayObservation = NosAi.LiveIntegration.GameplayObservation;
using Aggressor = NosAi.Runtime.Perception.Network.Aggressor;
using TargetedEntity = NosAi.Runtime.Perception.Network.TargetedEntity;
using InventorySlotReading = NosAi.Runtime.Perception.Network.InventorySlotReading;

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
/// <param name="Entities">
/// What has been seen on the map, or null when nothing has looked. Only the
/// target-selection rule reads it, so an absent list costs that one rule and
/// leaves the others alone (ADR-0016) — the same treatment
/// <see cref="HasTarget"/> gets when it is unknown.
/// </param>
/// <param name="PlayerPosition">
/// Where the character is standing. Needed to say which observed entity is
/// nearest and to aim at it at all; unknown is a refusal rather than the map
/// origin. Null when nothing has looked; UNKNOWN with the reader's own reason
/// when something looked and could not say, which today is every reading, since
/// the wire never carries it and no memory reader is bound to the running host.
/// </param>
/// <param name="HitBy">
/// Who last hit the character, with the instant of the hit as the value's
/// <see cref="ClassifiedValue{T}.ObservedAtUtc"/>. Null when nothing has looked.
/// Only the reactive rule reads it (C6-1), and that rule owns the window past
/// which an old hit stops being a reason; the state does not age on it, because
/// a hit ten seconds ago does not make a current HP stale.
/// </param>
/// <param name="SelectedTarget">
/// Which entity the character last acted on, from the wire's <c>ct</c>. The
/// answer to <i>which</i>; <see cref="HasTarget"/> is the screen's answer to
/// <i>whether</i>, and neither stands in for the other (ADR-0018). Null when
/// nothing has looked; sticky when known, so the state does not age on it.
/// </param>
/// <param name="Inventory">
/// What the character's inventory slots were last stated to hold, from
/// <c>ivn</c>. Null when nothing has looked. It is here because
/// <c>CollectGroundItem</c>'s post-condition is an inventory predicate and
/// nothing else in the state can answer it
/// (docs/CATALOGO_AZIONI_E_POSTCONDIZIONI.md § 4.6, § 6); no planning rule reads
/// it, so an absent list costs that one post-condition and nothing else. An
/// empty list is never published: a slot nothing has mentioned is a slot nobody
/// has read, not a slot known to be empty.
/// </param>
public sealed record Gate3WorldState(
    ClassifiedValue<int> Hp,
    ClassifiedValue<int> MaxHp,
    ClassifiedValue<int> Mp,
    ClassifiedValue<bool> HasTarget,
    ClassifiedValue<bool> InCombat,
    IReadOnlyList<SelectableEntity>? Entities = null,
    ClassifiedValue<MapPoint>? PlayerPosition = null,
    ClassifiedValue<Aggressor>? HitBy = null,
    ClassifiedValue<TargetedEntity>? SelectedTarget = null,
    ClassifiedValue<IReadOnlyList<InventorySlotReading>>? Inventory = null)
{
    /// <summary>Whether the character's own vitals are all known.</summary>
    /// <remarks>
    /// The minimum for any reasoning at all: what must never happen is planning
    /// over UNKNOWN, because there is nothing to reason about and any answer would
    /// be invented. Planning needs numbers; it does not need them to be real.
    /// </remarks>
    public bool HasVitals => Hp.HasValue && MaxHp.HasValue && Mp.HasValue;

    /// <summary>Whether there is enough here to plan anything at all.</summary>
    /// <remarks>
    /// <para>
    /// The vitals, and nothing more. Every other fact gates only the rules that
    /// read it (ADR-0016). This used to demand all five fields, which meant the
    /// loop refused to plan whenever the wire had not established the targeting or
    /// combat state — including with HP critical and fully observed. One of the two
    /// fields it refused over is read by no rule at all.
    /// </para>
    /// <para>
    /// A plannable state is not a complete one. A rule whose own facts are unknown
    /// is skipped, so this means "some rule may apply", and the honest outcome when
    /// none does is that there was no candidate, not that the world was unknown.
    /// </para>
    /// </remarks>
    public bool IsPlannable => HasVitals;

    /// <summary>Whether every field was read live from the client.</summary>
    /// <remarks>
    /// The strictest tier, kept for a caller that wants it. It is no longer what
    /// gates the effector: a reading republished CACHED between two packets is a
    /// real observation with a timestamp, not a simulation, and ADR-0016 gates
    /// acting on <see cref="IsActionable"/> instead.
    /// </remarks>
    public bool IsFullyObserved =>
        HasVitals
        && HasTarget.HasValue && InCombat.HasValue
        && Hp.Source == DataSourceKind.Live
        && MaxHp.Source == DataSourceKind.Live
        && Mp.Source == DataSourceKind.Live
        && HasTarget.Source == DataSourceKind.Live
        && InCombat.Source == DataSourceKind.Live;

    /// <summary>Whether any field carrying a value came from a simulation.</summary>
    /// <remarks>
    /// One simulated field is enough. A plan may be built on it; nothing may act on
    /// it, however real the other fields are.
    /// </remarks>
    public bool IsSimulated => KnownSources().Any(source => source == DataSourceKind.Simulated);

    /// <summary>
    /// When the oldest field carrying a value was observed, or null when none is.
    /// </summary>
    /// <remarks>
    /// A state is as old as its oldest field: a current MP does not make a stale HP
    /// current.
    /// </remarks>
    public DateTime? ObservedAtUtc
    {
        get
        {
            DateTime? oldest = null;
            foreach (DateTime observed in KnownTimes())
                if (oldest is null || observed < oldest)
                    oldest = observed;
            return oldest;
        }
    }

    /// <summary>
    /// Whether this state may drive something that touches the real game.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Real and recent. Real excludes UNKNOWN, which has nothing to act on, and
    /// SIMULATED, which may be planned on and never acted on. Recent is measured
    /// per field from the time it was actually observed — not from the time the
    /// state was assembled, which would make a remembered reading look new every
    /// time it was republished.
    /// </para>
    /// <para>
    /// The bound belongs to the caller because it depends on the channel: see
    /// <c>Gate3ExecutionOrchestrator</c>, which holds one policy for the whole loop.
    /// </para>
    /// </remarks>
    public bool IsActionable(DateTime nowUtc, TimeSpan maxAge)
    {
        if (!HasVitals || IsSimulated || maxAge < TimeSpan.Zero) return false;
        if (AgeAt(nowUtc) is not { } age) return false;
        // A reading stamped in the future is a clock disagreement, not a fresh
        // observation, and is treated as unusable rather than as maximally recent.
        return age >= TimeSpan.Zero && age <= maxAge;
    }

    /// <summary>How old the oldest reading is, or null when nothing was read.</summary>
    public TimeSpan? AgeAt(DateTime nowUtc) => ObservedAtUtc is { } observed ? nowUtc - observed : null;

    /// <summary>The reason the state cannot be planned on, or null when it can.</summary>
    /// <remarks>
    /// Only the vitals can make a state unplannable now, so only their reasons
    /// appear here. An unknown flag is not a failure of the state: it is a fact
    /// some rule will be skipped over, and that rule is where it shows.
    /// </remarks>
    public string? UnusableReason => IsPlannable
        ? null
        : Hp.FailureReason ?? MaxHp.FailureReason ?? Mp.FailureReason
          ?? "world_state_incomplete";

    /// <remarks>
    /// The position and the hit count: one simulated field is enough to keep a
    /// state off a real effector, whichever field it is. The entity list does
    /// not appear here because <see cref="SelectableEntity"/> carries no
    /// provenance of its own; its classification lives on the observation that
    /// produced it, and the entities travel through the same channel as the
    /// vitals, so a simulated channel is caught on the vitals.
    /// </remarks>
    private IEnumerable<DataSourceKind> KnownSources()
    {
        if (Hp.HasValue) yield return Hp.Source;
        if (MaxHp.HasValue) yield return MaxHp.Source;
        if (Mp.HasValue) yield return Mp.Source;
        if (HasTarget.HasValue) yield return HasTarget.Source;
        if (InCombat.HasValue) yield return InCombat.Source;
        if (PlayerPosition is { HasValue: true } position) yield return position.Source;
        if (HitBy is { HasValue: true } hit) yield return hit.Source;
        if (SelectedTarget is { HasValue: true } selected) yield return selected.Source;
    }

    /// <remarks>
    /// The position ages the state: a click is aimed from it, and a square the
    /// character has since walked off is the wrong origin. The hit does not — it
    /// is an instant with its own decay in the rule that reads it — and each
    /// entity carries its own instant for the selector to measure.
    /// </remarks>
    private IEnumerable<DateTime> KnownTimes()
    {
        if (Hp.HasValue) yield return Hp.ObservedAtUtc;
        if (MaxHp.HasValue) yield return MaxHp.ObservedAtUtc;
        if (Mp.HasValue) yield return Mp.ObservedAtUtc;
        if (HasTarget.HasValue) yield return HasTarget.ObservedAtUtc;
        if (InCombat.HasValue) yield return InCombat.ObservedAtUtc;
        if (PlayerPosition is { HasValue: true } position) yield return position.ObservedAtUtc;
    }

    /// <summary>Nothing is known. Planning on this is refused rather than guessed.</summary>
    public static Gate3WorldState Unobserved(string reason) => new(
        ClassifiedValue<int>.Unknown(reason),
        ClassifiedValue<int>.Unknown(reason),
        ClassifiedValue<int>.Unknown(reason),
        ClassifiedValue<bool>.Unknown(reason),
        ClassifiedValue<bool>.Unknown(reason),
        Entities: null,
        PlayerPosition: ClassifiedValue<MapPoint>.Unknown(reason),
        HitBy: ClassifiedValue<Aggressor>.Unknown(reason),
        SelectedTarget: ClassifiedValue<TargetedEntity>.Unknown(reason),
        Inventory: ClassifiedValue<IReadOnlyList<InventorySlotReading>>.Unknown(reason));

    /// <summary>
    /// The planning state a gameplay observation implies, field by field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one place the observation becomes the state, shared by both sources so
    /// the two cannot disagree about it. What the provider read stays read, what
    /// it could not stays UNKNOWN with the provider's own reason.
    /// </para>
    /// <para>
    /// The entity list becomes null when the observation has none — nothing to
    /// aim at, and the selector's refusal is the diagnostic — and the reason
    /// stays on the observation, which is what the snapshot shows. Each entity
    /// keeps its own instant. The position and the hit keep their whole
    /// classification: an unknown position reaches the selector as a refusal
    /// with the reader's reason, never as a coordinate.
    /// </para>
    /// </remarks>
    public static Gate3WorldState FromObservation(GameplayObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return new Gate3WorldState(
            Hp: observation.Hp,
            MaxHp: observation.MaxHp,
            Mp: observation.Mp,
            HasTarget: observation.HasTarget,
            InCombat: observation.InCombat,
            Entities: observation.Entities.HasValue ? observation.Entities.Value : null,
            PlayerPosition: observation.PlayerPosition,
            HitBy: observation.HitBy,
            SelectedTarget: observation.SelectedTarget,
            Inventory: observation.Inventory);
    }

    /// <summary>State read from the real client.</summary>
    /// <remarks>
    /// Every field is stamped with one instant when the caller does not name one,
    /// so a state built here cannot be aged by the microseconds between five
    /// separate reads of the clock.
    /// </remarks>
    public static Gate3WorldState Live(int hp, int maxHp, int mp, bool hasTarget, bool inCombat, DateTime? observedAtUtc = null)
    {
        DateTime at = observedAtUtc ?? DateTime.UtcNow;
        return new(
            ClassifiedValue<int>.Live(hp, at),
            ClassifiedValue<int>.Live(maxHp, at),
            ClassifiedValue<int>.Live(mp, at),
            ClassifiedValue<bool>.Live(hasTarget, at),
            ClassifiedValue<bool>.Live(inCombat, at));
    }

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
/// Reads the planning state straight from a gameplay provider.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Gate1SnapshotWorldStateSource"/> is the path for a running host,
/// and it reaches the provider through the whole Gate 1 snapshot — client attach,
/// hardware, guard session and all. This one is for a caller that has a provider
/// and no host: replaying a <c>.noscap</c> recording through the decision loop,
/// which is how the chain can be exercised end to end with no driver, no
/// elevation and no client running.
/// </para>
/// <para>
/// It changes nothing about provenance. A recording is CACHED at the framer and
/// stays CACHED here, so a decision taken over one may be planned and — the
/// moment anything is bound that could act — refused for staleness, which is
/// exactly right: those bytes were real when they were captured and they are not
/// current now.
/// </para>
/// </remarks>
public sealed class GameplayProviderWorldStateSource : IWorldStateSource
{
    private readonly IGameplayProvider _provider;

    public GameplayProviderWorldStateSource(IGameplayProvider provider)
        => _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    public Task<Gate3WorldState> ReadAsync(CancellationToken cancellationToken = default)
    {
        GameplayObservation observation;
        try
        {
            observation = _provider.Observe();
        }
        catch (Exception ex)
        {
            return Task.FromResult(Gate3WorldState.Unobserved($"gameplay_provider_failed:{ex.GetType().Name}"));
        }

        return Task.FromResult(Gate3WorldState.FromObservation(observation));
    }
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

        return Task.FromResult(Gate3WorldState.FromObservation(gameplay));
    }
}
