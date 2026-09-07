// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Autonomy — Asking the reference catalogue what a vnum is, once per vnum
// ============================================================================

using NosAi.Runtime.GameData;

namespace NosAi.Runtime.Autonomy;

/// <summary>
/// <see cref="TargetEstablishment.ClassifyByCatalogue"/> behind a per-vnum
/// cache and a lock, so a per-cycle consumer can ask it as if it were a pure
/// function.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <see cref="GameReferenceDatabase"/> is one
/// <c>SqliteConnection</c> with no cache and no synchronisation of its own,
/// and one classification costs up to three queries
/// (<c>Exists</c>, then <c>Lookup</c>, whose own empty-result path calls
/// <c>Exists</c> again). <c>GameplayObservationProjector</c> classifies every
/// sighted entity on every fusion cycle and is contractually pure, so it takes
/// a <see cref="Func{T, TResult}"/> and must never capture the handle itself.
/// This class is the adapter between the two.
/// </para>
/// <para>
/// <b>What is cached, and what deliberately is not.</b> Only the three answers
/// that are properties of the catalogue's content --
/// <see cref="CatalogueClass.Monster"/>,
/// <see cref="CatalogueClass.SpecialNonMonsterEntity"/> and
/// <see cref="CatalogueClass.AbsentFromMonsterTable"/> -- are remembered.
/// The catalogue is a read-only import of the client's own data files and
/// does not change while the process runs (<c>ClientUpdateCommand</c> detects
/// a changed client at startup, before any of this is asked), so remembering
/// them cannot go stale within a session.
/// <see cref="CatalogueClass.CatalogueUnreadable"/> is a transient I/O failure
/// and is never cached: caching it would turn one bad read into a permanent
/// verdict for that vnum. <see cref="CatalogueClass.CatalogueNotLoaded"/> is
/// not cached either, because it needs no lookup at all.
/// </para>
/// <para>
/// <b>Thread affinity.</b> Every read is serialised on one lock, so the
/// underlying connection is never touched concurrently even if two loops share
/// an instance. The lock is held across the SQLite call rather than only
/// around the dictionary: the connection, not the cache, is the thing that
/// cannot take concurrent use.
/// </para>
/// </remarks>
public sealed class CatalogueClassifier
{
    private readonly GameReferenceDatabase? _catalogue;
    private readonly Dictionary<int, CatalogueClass> _remembered = new();
    private readonly object _gate = new();

    /// <param name="catalogue">
    /// The reference database, or <see langword="null"/> when none could be
    /// opened. Null is not a failure to handle here: every classification then
    /// answers <see cref="CatalogueClass.CatalogueNotLoaded"/>, which the
    /// consumer already treats as "nothing established".
    /// </param>
    public CatalogueClassifier(GameReferenceDatabase? catalogue) => _catalogue = catalogue;

    /// <summary>How many distinct vnums have been resolved and remembered so far.</summary>
    /// <remarks>Exposed for diagnostics and tests; it is the count of cached answers, not of calls.</remarks>
    public int RememberedCount
    {
        get { lock (_gate) return _remembered.Count; }
    }

    /// <summary>What the catalogue says this vnum is, answering from memory after the first time.</summary>
    public CatalogueClass Classify(int vnum)
    {
        if (_catalogue is null)
            return CatalogueClass.CatalogueNotLoaded;

        lock (_gate)
        {
            if (_remembered.TryGetValue(vnum, out CatalogueClass remembered))
                return remembered;

            CatalogueLookup lookup = TargetEstablishment.ClassifyByCatalogue(vnum, _catalogue);
            if (lookup.Class != CatalogueClass.CatalogueUnreadable)
                _remembered[vnum] = lookup.Class;

            return lookup.Class;
        }
    }
}
