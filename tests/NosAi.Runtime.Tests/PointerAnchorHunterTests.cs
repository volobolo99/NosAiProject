using NosAi.LiveIntegration;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Turning a calibrated heap address into something that survives a restart.
/// </summary>
/// <remarks>
/// A reboot during this work killed a confirmed address: HP sat at 0x1F7AEC78 in
/// one session and 0x1F52EC78 in the next. What the runtime can re-resolve is a
/// base, so the useful answer is never the address — it is which base holds a
/// pointer that reaches it, and how far past that pointer it sits.
/// </remarks>
public sealed class PointerAnchorHunterTests
{
    // The client's image on this build, and the address the calibrator confirmed.
    private static readonly IntPtr ModuleBase = new(0x00400000);
    private const long ModuleSize = 0x00578000;
    private static readonly IntPtr Manager = new(0x0E22E170);
    private static readonly IntPtr PlayerObject = new(0x0E67DFD8);
    private static readonly IntPtr Target = new(0x1F52EC78);

    private const int BaseWindow = 0x1000;

    private static AnchorKind Classify(IntPtr holder) =>
        PointerAnchorHunter.Classify(holder, ModuleBase, ModuleSize, Manager, PlayerObject, BaseWindow);

    // ---------- which base a holder belongs to

    [Fact]
    public void APointerInsideTheClientsImageIsTheDurableKind()
    {
        // A static pointer is the only kind whose distance survives a restart,
        // and it is exactly what MemoryScanner skips by scanning private regions.
        Assert.Equal(AnchorKind.Module, Classify(new IntPtr(0x004F4BA8)));
        Assert.Equal(AnchorKind.Module, Classify(ModuleBase));
    }

    [Fact]
    public void AnAddressJustPastTheImageIsNotInIt()
    {
        Assert.NotEqual(AnchorKind.Module, Classify(new IntPtr(ModuleBase.ToInt64() + ModuleSize)));
    }

    [Fact]
    public void APointerInsideAResolvedBaseIsAnchoredToThatBase()
    {
        Assert.Equal(AnchorKind.PlayerManager, Classify(new IntPtr(Manager.ToInt64() + 0x40)));
        Assert.Equal(AnchorKind.PlayerObject, Classify(new IntPtr(PlayerObject.ToInt64() + 0x8)));
    }

    [Fact]
    public void APointerBelongingToNothingIsHeapAndSaysSo()
    {
        Assert.Equal(AnchorKind.Heap, Classify(new IntPtr(0x1F52ED00)));
    }

    [Fact]
    public void AZeroBaseNeverClaimsAHolder()
    {
        // TryResolveBases can hand back a null base. It must not swallow every
        // low address by being nearest to zero.
        Assert.Equal(
            AnchorKind.Heap,
            PointerAnchorHunter.Classify(new IntPtr(0x20), ModuleBase, ModuleSize, IntPtr.Zero, IntPtr.Zero, BaseWindow));
    }

    // ---------- what offset gets reported

    [Fact]
    public void AModuleHolderIsReportedAsADistanceFromTheImageBase()
    {
        long offset = PointerAnchorHunter.OffsetFor(
            AnchorKind.Module, new IntPtr(0x004F4BA8), ModuleBase, Manager, PlayerObject);

        Assert.Equal(0x000F4BA8, offset);
    }

    [Fact]
    public void AHeapHolderHasNoBaseSoItsAddressIsReportedWhole()
    {
        long offset = PointerAnchorHunter.OffsetFor(
            AnchorKind.Heap, new IntPtr(0x1F52ED00), ModuleBase, Manager, PlayerObject);

        Assert.Equal(0x1F52ED00, offset);
    }

    // ---------- the chain the anchor describes

    [Fact]
    public void AnAnchorRecordsHowFarPastThePointerTheTargetSits()
    {
        // A pointer aims at the record, not at the field. That distance is the
        // second half of the chain and has to be carried, not rounded away.
        var pointsAt = new IntPtr(Target.ToInt64() - 0x30);
        List<PointerAnchor> anchors = PointerAnchorHunter.Anchor(
            new[] { (new IntPtr(0x004F4BA8), pointsAt) },
            Target, ModuleBase, ModuleSize, Manager, PlayerObject, BaseWindow);

        PointerAnchor anchor = Assert.Single(anchors);
        Assert.Equal(AnchorKind.Module, anchor.Kind);
        Assert.Equal(0x30, anchor.IntoTarget);
        Assert.True(anchor.IsDurable);
        Assert.Equal("Module+0xF4BA8 -> +0x30", anchor.Describe());
    }

    [Fact]
    public void AHeapAnchorDescribesItselfAsHeapSoNobodyStoresIt()
    {
        var anchor = new PointerAnchor(AnchorKind.Heap, new IntPtr(0x1F52ED00), 0x1F52ED00, Target, 0);

        Assert.False(anchor.IsDurable);
        Assert.Contains("(heap)", anchor.Describe(), StringComparison.Ordinal);
    }

    // ---------- picking one

    [Fact]
    public void ADurableAnchorBeatsAHeapOneHoweverCloseTheHeapOneAims()
    {
        var heap = new PointerAnchor(AnchorKind.Heap, new IntPtr(0x1F52EC00), 0x1F52EC00, Target, 0);
        var module = new PointerAnchor(AnchorKind.Module, new IntPtr(0x004F4BA8), 0xF4BA8, Target, 0x800);

        PointerAnchor? best = PointerAnchorHunter.Best(new[] { heap, module });

        Assert.Equal(AnchorKind.Module, best!.Value.Kind);
    }

    [Fact]
    public void AmongEqualsTheOneAimingClosestWins()
    {
        // A pointer aiming 8 bytes short is describing the record. One aiming
        // 0xF00 short is describing something that merely contains it.
        var far = new PointerAnchor(AnchorKind.Module, new IntPtr(0x00401000), 0x1000, Target, 0xF00);
        var near = new PointerAnchor(AnchorKind.Module, new IntPtr(0x00402000), 0x2000, Target, 0x8);

        PointerAnchor? best = PointerAnchorHunter.Best(new[] { far, near });

        Assert.Equal(0x8, best!.Value.IntoTarget);
    }

    [Fact]
    public void NothingToChooseFromIsNullRatherThanADefault()
    {
        Assert.Null(PointerAnchorHunter.Best(Array.Empty<PointerAnchor>()));
    }

    // ---------- verdicts

    [Fact]
    public void NoHolderAtAllIsNamed()
    {
        Assert.Equal(
            PointerAnchorHunter.NoHolderReason,
            PointerAnchorHunter.Verdict(Array.Empty<PointerAnchor>()));
    }

    [Fact]
    public void OnlyHeapHoldersIsARefusalBecauseNoneOfThemLasts()
    {
        var anchors = new[]
        {
            new PointerAnchor(AnchorKind.Heap, new IntPtr(0x1F52EC00), 0x1F52EC00, Target, 0),
            new PointerAnchor(AnchorKind.Heap, new IntPtr(0x1F52EB00), 0x1F52EB00, Target, 0x100),
        };

        Assert.Equal(PointerAnchorHunter.OnlyHeapReason, PointerAnchorHunter.Verdict(anchors));
    }

    [Fact]
    public void OneDurableHolderAmongHeapOnesIsEnough()
    {
        var anchors = new[]
        {
            new PointerAnchor(AnchorKind.Heap, new IntPtr(0x1F52EC00), 0x1F52EC00, Target, 0),
            new PointerAnchor(AnchorKind.PlayerManager, new IntPtr(0x0E22E1B0), 0x40, Target, 0x30),
        };

        Assert.Null(PointerAnchorHunter.Verdict(anchors));
    }
}
