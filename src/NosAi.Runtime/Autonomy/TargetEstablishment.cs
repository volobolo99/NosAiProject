// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Autonomy — What has been established as attackable, and what has not
// ============================================================================

using System.Globalization;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.GameData;
using Aggressor = NosAi.Runtime.Perception.Network.Aggressor;
using TargetedEntity = NosAi.Runtime.Perception.Network.TargetedEntity;

namespace NosAi.Runtime.Autonomy;

/// <summary>How an entity came to count as something the runtime may attack.</summary>
/// <remarks>
/// Ordered strongest first, which is also the order the evidence is looked for:
/// the wire's own record of a fight beats a catalogue lookup, because it is about
/// this session and the catalogue is about the game in general.
/// </remarks>
public enum TargetEvidence : byte
{
    /// <summary>Nothing established it. The unknown does not authorise an act.</summary>
    None = 0,

    /// <summary>It hit the controlled character — confirmed on <c>su</c>.</summary>
    AttackedUs = 1,

    /// <summary>The character acted on it — confirmed on <c>ct</c>, and <c>su</c> before it.</summary>
    WeActedOnIt = 2,

    /// <summary>Its vnum is in the reference catalogue's monster table.</summary>
    CataloguedMonster = 3,
}

/// <summary>Whether an entity may be attacked, and on what evidence.</summary>
/// <param name="Reason">
/// Why not, when it is not. An identifier rather than prose, because it is
/// matched and logged.
/// </param>
public readonly record struct TargetVerdict(bool IsEstablished, TargetEvidence Evidence, string Reason)
{
    /// <summary>Established, on the named evidence.</summary>
    public static TargetVerdict Established(TargetEvidence evidence) =>
        new(true, evidence, string.Empty);

    /// <summary>Not established, for the named reason.</summary>
    public static TargetVerdict NotEstablished(string reason) =>
        new(false, TargetEvidence.None, reason);
}

/// <summary>
/// What the reference catalogue alone says a vnum is -- the third and
/// weakest of <see cref="TargetEstablishment.Assess"/>'s three kinds of
/// evidence, isolated so it can be asked on its own.
/// </summary>
/// <remarks>
/// Session evidence (<see cref="TargetEvidence.AttackedUs"/>,
/// <see cref="TargetEvidence.WeActedOnIt"/>) is deliberately not represented
/// here: it authorises an act against <i>this</i> entity in <i>this</i>
/// session and says nothing about what the vnum is in general, which is the
/// only question a World Model projection can ask of a catalogue.
/// </remarks>
public enum CatalogueClass : byte
{
    /// <summary>No catalogue is loaded, so the table has said nothing either way.</summary>
    CatalogueNotLoaded = 0,

    /// <summary>The catalogue threw while being read. It establishes nothing, and denies nothing.</summary>
    CatalogueUnreadable = 1,

    /// <summary>The vnum is absent from <see cref="TargetEstablishment.MonsterKind"/>'s table.</summary>
    AbsentFromMonsterTable = 2,

    /// <summary>The vnum is in the monster table and is not one of its RaceType-8 rows: a real monster.</summary>
    Monster = 3,

    /// <summary>
    /// The vnum is one of the monster table's RaceType-8 rows -- a trap, a
    /// teleporter, a talkable NPC. It shares the table with real monsters and
    /// is never one (see <see cref="NosAi.Runtime.GameData.MonsterReference.IsSpecialNonMonsterEntity"/>).
    /// </summary>
    SpecialNonMonsterEntity = 4
}

/// <summary>The catalogue's answer about one vnum, and the failure type when it could not answer.</summary>
/// <param name="FailureType">
/// The exception type's name when <see cref="Class"/> is
/// <see cref="CatalogueClass.CatalogueUnreadable"/>; <see langword="null"/>
/// otherwise. Kept so the caller can name the cause in its own refusal
/// reason without re-running the lookup.
/// </param>
public readonly record struct CatalogueLookup(CatalogueClass Class, string? FailureType);

/// <summary>
/// Decides what the runtime is allowed to attack, without ever having to
/// recognise what it is not allowed to attack.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule</b> (docs/TASTI_E_BERSAGLIO.md § 6.2): an entity may be attacked
/// only when something <i>established</i> it as attackable. An entity nothing has
/// established stays unknown, and the unknown does not authorise an act
/// (ADR-0016).
/// </para>
/// <para>
/// <b>Why it cannot be wrong in the dangerous direction.</b> The wire's entity
/// type 3 is monster <i>and</i> NPC, confirmed, the two together — so any rule
/// built on the type alone would attack merchants. This rule never asks what an
/// entity is. An NPC is excluded not because it was recognised as an NPC but
/// because nothing ever established it as attackable, and that holds exactly
/// where classification is impossible. It is ADR-0018's asymmetry again: the
/// error it can make is refusing a legitimate target, which costs a skipped rule.
/// </para>
/// <para>
/// <b>Pure and total.</b> It takes the evidence and returns a verdict for every
/// combination, so each refusal is testable with no client and no catalogue.
/// </para>
/// </remarks>
public static class TargetEstablishment
{
    /// <summary>The catalogue table the monsters live in.</summary>
    /// <remarks>
    /// <c>ReferenceImporter</c> writes this kind, and 2 705 monsters are already
    /// imported under it. A vnum present there is stronger evidence than anything
    /// derived from the wire's shared type 3 -- but presence alone is not quite
    /// "is a monster": the same table also carries RaceType 8's nine kinds of
    /// non-monster special entity (traps, teleporters, talkable NPCs, ...), which
    /// <see cref="IsSpecialNonMonsterEntity"/> excludes.
    /// </remarks>
    public const string MonsterKind = "monster";

    /// <summary>The reason an entity whose vnum nobody read carries.</summary>
    public const string VnumNotObservedReason = "vnum_not_observed";

    /// <summary>The reason an entity nothing has established carries.</summary>
    public const string NeverEstablishedReason = "target_never_established";

    /// <summary>
    /// Whether this entity may be attacked, and on what.
    /// </summary>
    /// <param name="entity">The entity as it was last observed.</param>
    /// <param name="hitBy">
    /// Who last hit the character, or null when nobody has. It is the strongest
    /// evidence there is: an entity that attacked this character is beyond doubt
    /// something that fights.
    /// </param>
    /// <param name="selected">
    /// Which entity the character last acted on, from <c>ct</c>, or null. The
    /// character having acted on it is evidence the client accepted it as a
    /// target — the same reasoning that makes <c>F8</c> a better classifier than
    /// anything this project could derive.
    /// </param>
    /// <param name="catalogue">
    /// The reference database, or null when none is loaded. Null costs the third
    /// kind of evidence and nothing else; it never turns into an assumption
    /// either way.
    /// </param>
    public static TargetVerdict Assess(
        SelectableEntity entity,
        ClassifiedValue<Aggressor>? hitBy,
        ClassifiedValue<TargetedEntity>? selected,
        GameReferenceDatabase? catalogue)
    {
        if (hitBy is { HasValue: true } aggressor && aggressor.Value.EntityId == entity.EntityId)
            return TargetVerdict.Established(TargetEvidence.AttackedUs);

        if (selected is { HasValue: true } target && target.Value.EntityId == entity.EntityId)
            return TargetVerdict.Established(TargetEvidence.WeActedOnIt);

        if (entity.Vnum is not { } vnum)
        {
            // Only `in` carries a vnum, and a capture that started mid-session has
            // 25 of them against 7 685 moves — so most entities are located long
            // before anything says what they are. That is not a licence.
            return TargetVerdict.NotEstablished(VnumNotObservedReason);
        }

        CatalogueLookup lookup = ClassifyByCatalogue(vnum, catalogue);
        return lookup.Class switch
        {
            CatalogueClass.Monster => TargetVerdict.Established(TargetEvidence.CataloguedMonster),
            CatalogueClass.CatalogueNotLoaded => TargetVerdict.NotEstablished("reference_catalogue_not_loaded"),
            CatalogueClass.CatalogueUnreadable => TargetVerdict.NotEstablished($"reference_catalogue_failed:{lookup.FailureType}"),

            // Absent from the table, or present as one of its RaceType-8 rows:
            // both leave the entity unestablished, and both carry the same
            // reason they carried before this switch existed -- a talkable NPC
            // is refused for exactly the same "nothing established it" cause as
            // an entity the table has never heard of.
            _ => TargetVerdict.NotEstablished(
                string.Create(CultureInfo.InvariantCulture, $"{NeverEstablishedReason}:vnum={vnum}"))
        };
    }

    /// <summary>
    /// What the catalogue alone says <paramref name="vnum"/> is, with no
    /// session evidence and no authorisation decision attached.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Extracted from <see cref="Assess"/> so that the World Model's own
    /// entity projection (<c>NosAi.Runtime.WorldModel.Fusion.GameplayObservationProjector</c>)
    /// can ask the same question without re-implementing the predicate.
    /// Two independent answers to "is this vnum a monster" is precisely the
    /// divergence this project cannot afford: the one that authorises an
    /// attack and the one that fills the World Model must be the same
    /// predicate, or a future change to one silently contradicts the other.
    /// </para>
    /// <para>
    /// Total and non-throwing, exactly as the code inside <see cref="Assess"/>
    /// already was: a catalogue that throws yields
    /// <see cref="CatalogueClass.CatalogueUnreadable"/> rather than taking the
    /// cycle down, and never yields a classification by omission.
    /// </para>
    /// </remarks>
    /// <param name="vnum">The entity's own number, as the wire stated it.</param>
    /// <param name="catalogue">The reference database, or null when none is loaded.</param>
    public static CatalogueLookup ClassifyByCatalogue(int vnum, GameReferenceDatabase? catalogue)
    {
        if (catalogue is null)
            return new CatalogueLookup(CatalogueClass.CatalogueNotLoaded, null);

        try
        {
            if (!catalogue.Exists(MonsterKind, vnum))
                return new CatalogueLookup(CatalogueClass.AbsentFromMonsterTable, null);

            return IsSpecialNonMonsterEntity(catalogue, vnum)
                ? new CatalogueLookup(CatalogueClass.SpecialNonMonsterEntity, null)
                : new CatalogueLookup(CatalogueClass.Monster, null);
        }
        catch (Exception ex)
        {
            // A catalogue that cannot be read establishes nothing. It does not
            // establish the opposite either, and it does not take the cycle down.
            return new CatalogueLookup(CatalogueClass.CatalogueUnreadable, ex.GetType().Name);
        }
    }

    /// <summary>
    /// True only when the catalogue confirms <paramref name="vnum"/> is one
    /// of <c>monster.dat</c>'s own RaceType-8 entries -- traps, teleporters,
    /// talkable NPCs and the rest of that bucket (see
    /// <see cref="MonsterReference.IsSpecialNonMonsterEntity"/>), which share
    /// a table with real monsters but are never one. False whenever the row
    /// cannot be read or decoded, never true by omission: an unreadable
    /// record narrows nothing, it just leaves <see cref="MonsterKind"/>'s
    /// existence check as the only word on the matter, exactly as before
    /// this method existed.
    /// </summary>
    private static bool IsSpecialNonMonsterEntity(GameReferenceDatabase catalogue, int vnum)
    {
        IReadOnlyList<NosField>? fields = catalogue.Lookup(MonsterKind, vnum);
        if (fields is null)
            return false;

        return MonsterReferenceDecoder.Decode(new NosRecord(vnum, fields))?.IsSpecialNonMonsterEntity == true;
    }
}
