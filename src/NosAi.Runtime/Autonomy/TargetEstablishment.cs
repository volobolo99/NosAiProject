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
    /// imported under it. A vnum present there is a monster by the game's own
    /// definition, which is a stronger statement than anything derived from the
    /// wire's shared type 3.
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

        if (catalogue is null)
            return TargetVerdict.NotEstablished("reference_catalogue_not_loaded");

        try
        {
            if (catalogue.Exists(MonsterKind, vnum))
                return TargetVerdict.Established(TargetEvidence.CataloguedMonster);
        }
        catch (Exception ex)
        {
            // A catalogue that cannot be read establishes nothing. It does not
            // establish the opposite either, and it does not take the cycle down.
            return TargetVerdict.NotEstablished($"reference_catalogue_failed:{ex.GetType().Name}");
        }

        return TargetVerdict.NotEstablished(
            string.Create(CultureInfo.InvariantCulture, $"{NeverEstablishedReason}:vnum={vnum}"));
    }
}
