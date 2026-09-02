// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Safety — What the token actually signs (R3, ADR-0020 § 3)
// ============================================================================
//
// The defect this closes was measured and written down before it was fixed:
// docs/GATE3_PIPELINE.md, "the token signs the identifier, not the action".
// The HMAC covered candidate.CandidateId and nothing else, so
// `candidate with { Target = ... }` produced a different action carrying the
// same Guid, and the token went on validating it. Between authorisation and
// execution the target could be swapped and the signature would not notice.

using System.Buffers.Binary;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Safety;

/// <summary>
/// The canonical bytes an action token signs: everything that changes what the act does.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fixed width, every field, no exceptions — and that is the whole anti-ambiguity
/// argument.</b> A digest built by concatenating variable-length pieces can be made to
/// collide by moving a boundary: the classic shape is two different intents whose
/// concatenations produce identical bytes because a field grew by exactly as much as
/// the next one shrank. Length prefixes fix that. <i>Having no variable-length field at
/// all</i> makes the question not arise: every intent is exactly
/// <see cref="Size"/> bytes, every field sits at a fixed offset, and two byte-identical
/// digests are two identical intents by construction.
/// </para>
/// <para>
/// <b>The version byte leads.</b> Adding a field later changes what a digest means, and
/// a digest whose meaning changed silently would let a token issued under the old rules
/// validate under the new ones. The version is inside the signed bytes, so a change to
/// this layout invalidates every token that predates it rather than reinterpreting it.
/// </para>
/// <para>
/// <b>What is signed, and why each one.</b>
/// </para>
/// <list type="bullet">
/// <item><see cref="ActionCandidate.CandidateId"/> — the identity the binding check uses.</item>
/// <item><see cref="ActionCandidate.Type"/> — an attack and a move are not the same act.</item>
/// <item>
/// The target's <b>discriminator and its fields</b>. This is the field the defect was
/// about: swapping <c>Entity(7)</c> for <c>Entity(9)</c>, or moving the point a
/// <c>Position</c> names, changes what is hit and where the click lands.
/// </item>
/// <item><see cref="ActionCandidate.SkillOrItemId"/> — which skill, which item.</item>
/// <item>
/// <see cref="ActionCandidate.RequiredTrust"/> — the tier the act was authorised
/// against. Left out, a candidate could be re-presented claiming a lower requirement
/// than the one the Trust boundary actually approved.
/// </item>
/// </list>
/// <para>
/// <b>What is deliberately not signed: <see cref="ActionCandidate.Rationale"/>.</b> It
/// is the sentence explaining the choice to a person; it does not change what happens
/// to the client. Signing it would make a reworded explanation invalidate a live token,
/// which is a refusal with no safety content — and the first time it happened somebody
/// would be tempted to widen something that matters instead.
/// </para>
/// <para>
/// <b>And what the digest cannot cover: the pixel</b> (ADR-0020 § 4). The screen
/// coordinate is computed in the effector, <i>after</i> authorisation, from the map
/// point this digest does cover. What guards the pixel is the commit point — its third
/// condition asks whether that exact point belongs to the session window, and its fifth
/// whether the scale it was computed under is still live. Two guards, two subjects, and
/// the projection is the seam between them. Nothing binds a pixel to a digest, and that
/// limit is recorded rather than left to be discovered.
/// </para>
/// </remarks>
public static class ActionIntentDigest
{
    /// <summary>The layout version, first byte of every digest.</summary>
    public const byte Version = 1;

    /// <summary>Discriminator for an action aimed at nothing.</summary>
    private const byte TargetNone = 0;
    private const byte TargetEntity = 1;
    private const byte TargetPosition = 2;
    private const byte TargetInventorySlot = 3;

    /// <summary>Bytes reserved for the target, whichever kind it is.</summary>
    /// <remarks>
    /// The widest case is an entity with a known position: eight bytes of id, one flag,
    /// and two coordinates — seventeen. The rest is zero-filled, so a
    /// <c>Position(5,5)</c> and an <c>Entity</c> whose id happens to encode the same
    /// leading bytes still differ in the discriminator that precedes them.
    /// </remarks>
    private const int TargetBytes = 24;

    private const int VersionOffset = 0;
    private const int CandidateIdOffset = 1;
    private const int TypeOffset = 17;
    private const int TargetKindOffset = 18;
    private const int TargetOffset = 19;
    private const int SkillOrItemOffset = TargetOffset + TargetBytes;
    private const int RequiredTrustOffset = SkillOrItemOffset + 4;

    /// <summary>The exact length of every intent digest.</summary>
    public const int Size = RequiredTrustOffset + 1;

    /// <summary>
    /// Writes the canonical bytes for one candidate.
    /// </summary>
    /// <remarks>
    /// Takes a span so the caller can <c>stackalloc</c> it: this sits on the
    /// authorisation path, and a digest that allocated would put a garbage collection
    /// between a decision and the act it authorises.
    /// </remarks>
    /// <exception cref="ArgumentException">The destination is not <see cref="Size"/> bytes.</exception>
    public static void Write(ActionCandidate candidate, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (destination.Length != Size)
        {
            throw new ArgumentException(
                $"An intent digest is exactly {Size} bytes; got {destination.Length}.",
                nameof(destination));
        }

        destination.Clear();
        destination[VersionOffset] = Version;

        // Big-endian on purpose: the byte order is part of the canonical form, and
        // Guid.ToByteArray()'s default mixes endianness across its components.
        if (!candidate.CandidateId.TryWriteBytes(destination[CandidateIdOffset..TypeOffset], bigEndian: true, out _))
            throw new ArgumentException("The candidate id could not be written.", nameof(candidate));

        destination[TypeOffset] = (byte)candidate.Type;

        Span<byte> target = destination.Slice(TargetOffset, TargetBytes);
        switch (candidate.Target)
        {
            case ActionTarget.Entity entity:
                destination[TargetKindOffset] = TargetEntity;
                BinaryPrimitives.WriteInt64BigEndian(target, entity.EntityId);
                // The flag is signed as well as the coordinates: "seen at 0,0" and "seen
                // nowhere" are different intents, and without it they would agree.
                target[8] = entity.At is null ? (byte)0 : (byte)1;
                if (entity.At is { } at)
                {
                    BinaryPrimitives.WriteInt32BigEndian(target[9..], at.X);
                    BinaryPrimitives.WriteInt32BigEndian(target[13..], at.Y);
                }

                break;

            case ActionTarget.Position position:
                destination[TargetKindOffset] = TargetPosition;
                BinaryPrimitives.WriteInt32BigEndian(target, position.At.X);
                BinaryPrimitives.WriteInt32BigEndian(target[4..], position.At.Y);
                break;

            case ActionTarget.InventorySlot slot:
                destination[TargetKindOffset] = TargetInventorySlot;
                BinaryPrimitives.WriteInt32BigEndian(target, slot.Slot);
                break;

            case ActionTarget.None:
                destination[TargetKindOffset] = TargetNone;
                break;

            default:
                // ActionTarget's hierarchy is closed by a private constructor, so this is
                // unreachable today. It throws rather than signing zeroes, because a
                // fifth target kind added later must break loudly here instead of
                // quietly hashing to the same digest as "aimed at nothing".
                throw new ArgumentException(
                    $"No canonical form is defined for target {candidate.Target.GetType().Name}.",
                    nameof(candidate));
        }

        BinaryPrimitives.WriteInt32BigEndian(destination[SkillOrItemOffset..], candidate.SkillOrItemId);
        destination[RequiredTrustOffset] = (byte)candidate.RequiredTrust;
    }

    /// <summary>The canonical bytes, allocated. For tests and diagnostics.</summary>
    public static byte[] Compute(ActionCandidate candidate)
    {
        var bytes = new byte[Size];
        Write(candidate, bytes);
        return bytes;
    }
}
