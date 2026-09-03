using System.Text.RegularExpressions;
using NosAi.LiveIntegration;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The scene manager's signature is data, not code, and the four bytes it hands
/// back as a pointer are its own wildcards. These fix what happens to a match
/// whose operand cannot be an address at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this matters, from the run that produced it.</b> On 3 September 2026,
/// on a live client in game with the console elevated, <c>--entity-names</c>
/// refused all four lists with
/// <c>scene_manager_not_confirmed:1_candidates:0xFFFFFFFF:player_list_pointer_unreadable_at+0xC</c>.
/// The address was <c>0xFFFFFFFF</c> — every bit set, which no allocator hands
/// out — so the pattern had matched a run of <c>FF</c> filler and read more
/// <c>FF</c> filler as a pointer. The refusal named
/// <see cref="NosTaleClientLayout.PlayerListOffset"/>, where nothing was wrong,
/// and sent the investigation to the wrong offset. A refusal that names the
/// wrong cause is worse than none, because it is acted upon.
/// </para>
/// <para>
/// <b>Why the predicate is pure.</b> <c>TryResolveScene</c> needs a live client
/// and an open handle, so it cannot be exercised here at all — which is exactly
/// why the judgement was lifted out of it. Everything below runs with no
/// process, no handle and no platform, and the resolver's job is reduced to
/// calling it before it follows anything.
/// </para>
/// </remarks>
public sealed class SceneOperandPlausibilityTests
{
    /// <summary>A plausible base for this 32-bit client; the default for a Delphi image.</summary>
    private static readonly IntPtr ImageBase = new(0x400000);

    [Fact]
    public void TheAddressTheLiveClientReturnedIsRefusedAsFillerBeforeAnythingFollowsIt()
    {
        Assert.False(
            NosTaleClientLayout.IsPlausibleSceneOperand(0xFFFFFFFF, ImageBase, out string? why));
        Assert.Equal(NosTaleClientLayout.SceneOperandAllBitsSetReason, why);
    }

    /// <summary>
    /// <c>0xFFFFFFFF</c> is misaligned too, and reporting that would name a
    /// symptom where the evidence names filler. The order of the checks is the
    /// behaviour, so it is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void FillerIsNamedAsFillerAndNotAsMisalignment()
    {
        NosTaleClientLayout.IsPlausibleSceneOperand(0xFFFFFFFF, ImageBase, out string? why);

        Assert.NotEqual(NosTaleClientLayout.SceneOperandMisalignedReason, why);
        Assert.Equal("scene_operand_all_bits_set", why);
    }

    [Fact]
    public void AZeroOperandIsRefusedBecauseThereIsNothingToFollow()
    {
        Assert.False(NosTaleClientLayout.IsPlausibleSceneOperand(0, ImageBase, out string? why));
        Assert.Equal("scene_operand_null", why);
    }

    /// <summary>
    /// Every field this layout reads through the scene manager is a 32-bit word,
    /// so an unaligned address is not the start of an object the client allocated.
    /// </summary>
    [Theory]
    [InlineData(0x00A0B0C1u)]
    [InlineData(0x00A0B0C2u)]
    [InlineData(0x00A0B0C3u)]
    [InlineData(0xFFFFFFFEu)]
    public void AnOperandThatIsNotFourByteAlignedIsRefused(uint operand)
    {
        Assert.False(NosTaleClientLayout.IsPlausibleSceneOperand(operand, ImageBase, out string? why));
        Assert.Equal(NosTaleClientLayout.SceneOperandMisalignedReason, why);
    }

    [Fact]
    public void AnOperandBelowTheImageIsRefusedAndTheBaseItFailedIsNamed()
    {
        Assert.False(
            NosTaleClientLayout.IsPlausibleSceneOperand(0x00010000, ImageBase, out string? why));
        Assert.Equal("scene_operand_below_module_base:0x400000", why);
    }

    /// <summary>
    /// The bound is "below", not "at or below": an address equal to the base is
    /// not evidence of anything by itself, and inventing a stricter rule than the
    /// evidence supports would reject a real pointer with no way to notice.
    /// </summary>
    [Fact]
    public void TheImageBaseItselfIsNotBelowTheImageBase()
    {
        Assert.True(
            NosTaleClientLayout.IsPlausibleSceneOperand(0x400000, ImageBase, out string? why), why);
        Assert.Null(why);
    }

    /// <summary>
    /// The two object pointers this client has actually been measured handing out,
    /// recorded at <see cref="NosTaleClientLayout.TargetPointerOffset"/>. A filter
    /// that rejects the addresses the client really uses is not a filter, it is a
    /// refusal to work, so the known-good values are pinned beside the known-bad.
    /// </summary>
    [Theory]
    [InlineData(0x22C8A4F0u)]
    [InlineData(0x1F5BA4F0u)]
    public void TheObjectPointersMeasuredOnThisClientAreAccepted(uint operand)
    {
        Assert.True(
            NosTaleClientLayout.IsPlausibleSceneOperand(operand, ImageBase, out string? why), why);
        Assert.Null(why);
    }

    /// <summary>
    /// A rejection the operator cannot read is a rejection they cannot act on, and
    /// the whole defect was a reason that named the wrong thing. Each one is
    /// <c>snake_case</c> up to the first colon, in the style the rest of the layout
    /// already uses.
    /// </summary>
    [Fact]
    public void EveryRejectionIsNamedInSnakeCase()
    {
        uint[] refused = [0u, 0xFFFFFFFFu, 0x00A0B0C1u, 0x00010000u];

        foreach (uint operand in refused)
        {
            Assert.False(NosTaleClientLayout.IsPlausibleSceneOperand(operand, ImageBase, out string? why));
            Assert.NotNull(why);

            string name = why!.Split(':')[0];
            Assert.Matches(new Regex("^[a-z][a-z0-9_]*$"), name);
            Assert.StartsWith("scene_operand_", name, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Refusing and explaining are one decision here: a caller that is told "no"
    /// with no name learns nothing, and a caller told "yes" with a reason attached
    /// would not know which to believe.
    /// </summary>
    [Fact]
    public void TheReasonIsPresentExactlyWhenTheOperandIsRefused()
    {
        for (uint operand = 0x3FFFF0; operand <= 0x400020; operand++)
        {
            bool accepted = NosTaleClientLayout.IsPlausibleSceneOperand(operand, ImageBase, out string? why);
            Assert.Equal(accepted, why is null);
        }
    }

    /// <summary>
    /// A base of zero would make the "below the image" bound vacuously true for
    /// every address, which is a check that silently stops checking. The base is
    /// resolved before any match is examined, so a missing one is a caller defect
    /// and is raised as one.
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void AMissingModuleBaseIsACallerDefectRatherThanASilentlyWeakerCheck(long moduleBase)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NosTaleClientLayout.IsPlausibleSceneOperand(0x1F5BA4F0, new IntPtr(moduleBase), out _));
    }

    /// <summary>
    /// The signature is left exactly as it was measured. A replacement cannot be
    /// tested without the client in front of it, and this repository does not
    /// carry numbers nobody has checked — so the guard against its false positives
    /// is the predicate above, not a better-looking pattern.
    /// </summary>
    [Fact]
    public void TheSceneSignatureIsUnchangedAndItsOperandIsStillItsOwnWildcards()
    {
        Assert.Equal(
            "FF ?? ?? ?? ?? ?? FF FF FF 00 00 00 00 00 00 00 00 00 00 00 00 FF FF FF FF",
            NosTaleClientLayout.SceneManagerSignature);

        SignatureByte[] parsed =
            NosTaleClientLayout.ParseSignature(NosTaleClientLayout.SceneManagerSignature);

        for (int i = NosTaleClientLayout.SceneOperandOffset;
             i < NosTaleClientLayout.SceneOperandOffset + sizeof(int);
             i++)
        {
            Assert.True(parsed[i].IsWildcard);
        }
    }
}
