namespace NosAi.Core.WorldModel;

/// <summary>The kind of a depletable/regenerating pool a <see cref="Player"/> or <see cref="Mob"/> tracks.</summary>
public enum ResourceKind
{
    Health = 0,
    Mana = 1,
    Stamina = 2,
    Experience = 3,
    Currency = 4,

    /// <summary>A game-specific pool not covered above; <see cref="Resource.CustomName"/> names it.</summary>
    Custom = 5
}

/// <summary>
/// One named, bounded resource pool (HP, MP, stamina, currency, ...). Both
/// bounds are independently classified: a HUD showing current HP without a
/// visible max (or vice versa) must not fabricate the missing half.
/// </summary>
/// <remarks>
/// <see cref="Fraction"/> extends that same rule to a third case the two
/// bounds alone cannot express: a source that states how full the pool is
/// without stating either bound. See its own remarks for the observed
/// evidence that makes this a real case rather than a hypothetical one.
/// </remarks>
public sealed record Resource
{
    public ResourceKind Kind { get; init; }
    /// <summary>The pool's current level. <c>private init</c>: see the remarks on <see cref="Fraction"/>.</summary>
    public WorldFact<double> Current { get; private init; }

    /// <summary>The pool's upper bound. <c>private init</c>: see the remarks on <see cref="Fraction"/>.</summary>
    public WorldFact<double> Maximum { get; private init; }
    public string? CustomName { get; init; }

    /// <summary>
    /// How full the pool is, in the same 0..1 sense a health bar is read --
    /// classified independently of <see cref="Current"/> and
    /// <see cref="Maximum"/> because a real source can state it while
    /// stating neither bound.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both cases are observed on this game's own wire, and the distinction
    /// is not cosmetic (docs/PROTOCOLLO_NOSTALE.md):
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>st</c> (another entity's vitals) carries the entity's <b>absolute</b>
    /// current and maximum HP. In the capture replayed by
    /// <c>NosAi.Runtime.Tests.NosTaleWorldObservationTests</c>,
    /// <c>st 3 313816 8 0 66 100 198 52 310 52 0</c> means 198/310 for monster
    /// 313816, and that maximum of 310 is corroborated independently by the
    /// <c>su</c> packet's own tail. Here both bounds are real and this
    /// fraction is <see cref="DataSourceKind.Derived"/> from them.
    /// </description></item>
    /// <item><description>
    /// <c>in</c> (an entity enters view) carries only <c>hp%</c>, an integer
    /// 0-100. Nothing in that packet, and nothing in <c>monster.dat</c>
    /// (whose <c>HP/MP</c> tag is decoded as
    /// <c>MonsterReference.MaxHpBonus</c> -- a bonus over a level-derived
    /// base this project has not verified a formula for), yields the
    /// absolute pair. Here the fraction is the only real observation and
    /// both bounds stay <see cref="DataSourceKind.Unknown"/>:
    /// <see cref="FromObservedFraction"/> is the only way to build that.
    /// </description></item>
    /// </list>
    /// <para>
    /// Deliberately not clamped into [0, 1]. A bound pair whose quotient
    /// falls outside it is a real inconsistency between two observations,
    /// and silently folding it back into range would hide exactly the kind
    /// of sensor disagreement this namespace exists to keep visible.
    /// </para>
    /// <para>
    /// Settable only from inside this type, so the constructor and
    /// <see cref="FromObservedFraction"/> are the only two doors and no
    /// caller can pair a fraction with bounds that contradict it.
    /// </para>
    /// <para>
    /// That holds only because <see cref="Current"/> and
    /// <see cref="Maximum"/> are <c>private init</c> too. While they were
    /// <c>public init</c> the guarantee above was merely a convention: an
    /// external <c>r with { Maximum = other }</c> copied the old fraction
    /// verbatim -- a record's <c>with</c> clones the fields, it does not
    /// re-run the constructor -- and produced exactly the contradiction this
    /// paragraph promised was impossible. No call site ever did it, so
    /// closing the door changed no behaviour; it only made the sentence true.
    /// Rebuild through the constructor to change a bound.
    /// </para>
    /// </remarks>
    public WorldFact<double> Fraction { get; private init; }

    public Resource(ResourceKind kind, WorldFact<double> current, WorldFact<double> maximum, string? customName = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(maximum);
        if (kind == ResourceKind.Custom)
            ArgumentException.ThrowIfNullOrWhiteSpace(customName);

        Kind = kind;
        Current = current;
        Maximum = maximum;
        CustomName = customName;
        Fraction = DeriveFraction(current, maximum);
    }

    /// <summary>
    /// A pool whose fill fraction was observed directly while neither bound
    /// was -- the <c>in</c> packet case described on <see cref="Fraction"/>.
    /// </summary>
    /// <param name="kind">Which pool this is.</param>
    /// <param name="fraction">
    /// The observed fill fraction. Its own <see cref="WorldFact{T}.Source"/>
    /// and <see cref="WorldFact{T}.ObservedAtUtc"/> are kept as given, so a
    /// percentage read straight off the wire stays
    /// <see cref="DataSourceKind.Live"/> and a remembered one stays
    /// <see cref="DataSourceKind.Cached"/>.
    /// </param>
    /// <param name="boundsUnknownReason">
    /// Why neither bound is known, recorded on both of them. Mandatory for
    /// the same reason <see cref="WorldFact{T}.Unknown"/> demands one: a gap
    /// must always be diagnosable.
    /// </param>
    /// <param name="customName">Required when <paramref name="kind"/> is <see cref="ResourceKind.Custom"/>.</param>
    /// <remarks>
    /// Both bounds are stamped from <paramref name="fraction"/>'s own instant
    /// rather than from <see cref="DateTime.UtcNow"/>, so this factory stays
    /// pure and two calls with equal inputs produce equal resources -- the
    /// determinism requirement <c>TemporalBelief.EstimateVelocity</c> already
    /// documents for its own Unknown returns.
    /// </remarks>
    public static Resource FromObservedFraction(
        ResourceKind kind,
        WorldFact<double> fraction,
        string boundsUnknownReason,
        string? customName = null)
    {
        ArgumentNullException.ThrowIfNull(fraction);
        ArgumentException.ThrowIfNullOrWhiteSpace(boundsUnknownReason);

        WorldFact<double> unknownBound = WorldFact<double>.Unknown(boundsUnknownReason, fraction.ObservedAtUtc);
        return new Resource(kind, unknownBound, unknownBound, customName) { Fraction = fraction };
    }

    /// <summary>
    /// The fill fraction implied by a known bound pair, or an Unknown
    /// carrying why it could not be computed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Confidence is the lower of the two bounds', and the instant is the
    /// <b>older</b> of the two. Both are deliberate and both differ from
    /// <c>TemporalBelief.EstimateVelocity</c>, which stamps its estimate
    /// with the later of its two positions: a velocity is genuinely an
    /// estimate <i>at</i> the later instant, whereas a quotient is only as
    /// trustworthy as the staler of the two readings it divides. A maximum
    /// read minutes ago and a current read a moment ago yield a fraction
    /// that is minutes old, and labelling it fresh would let a freshness
    /// gate accept it.
    /// </para>
    /// <para>
    /// Every return is stamped from an instant already present on an input,
    /// never from the wall clock, so this stays a pure function of its
    /// arguments.
    /// </para>
    /// </remarks>
    private static WorldFact<double> DeriveFraction(WorldFact<double> current, WorldFact<double> maximum)
    {
        DateTime stalest = current.ObservedAtUtc <= maximum.ObservedAtUtc
            ? current.ObservedAtUtc
            : maximum.ObservedAtUtc;

        if (!current.HasValue || !maximum.HasValue)
            return WorldFact<double>.Unknown("bounds_not_both_observed", stalest);
        if (maximum.Value <= 0)
            return WorldFact<double>.Unknown("maximum_not_positive", stalest);

        double fraction = current.Value / maximum.Value;
        if (!double.IsFinite(fraction))
            return WorldFact<double>.Unknown("non_finite_fraction", stalest);

        return WorldFact<double>.Derived(
            fraction,
            Math.Min(current.Confidence, maximum.Confidence),
            stalest,
            "current_over_maximum");
    }
}
