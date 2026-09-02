using System.IO;
using NosAi.ControlPanel;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.ControlPanel.Tests;

/// <summary>
/// Combat row: last hit with age, and the target in its three ADR-0018 values.
/// </summary>
public sealed class CombatInspectTests
{
    private static readonly DateTime Now = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AKnownHitAndPresentTargetAreNamedWithAge()
    {
        var hit = ClassifiedValue<Aggressor>.Live(new Aggressor(77, 2), Now.AddSeconds(-5));
        var target = ClassifiedValue<bool>.Derived(true, Now);

        CombatView view = CombatInspect.Inspect(hit, target, Now);

        Assert.Equal(TargetFrameState.Present, view.TargetState);
        Assert.Contains("id=77", view.LastHitLine, StringComparison.Ordinal);
        Assert.Contains("età=5s", view.LastHitLine, StringComparison.Ordinal);
        Assert.Contains(CombatInspect.PresentLabel, view.TargetLine, StringComparison.Ordinal);
        Assert.DoesNotContain("true", view.TargetLine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("false", view.TargetLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnAbsentTargetIsNamedAbsentNotFalse()
    {
        CombatView view = CombatInspect.Inspect(
            ClassifiedValue<Aggressor>.Unknown(GameplayObservation.NotPublishedReason),
            ClassifiedValue<bool>.Derived(false, Now),
            Now);

        Assert.Equal(TargetFrameState.Absent, view.TargetState);
        Assert.Contains(CombatInspect.AbsentLabel, view.TargetLine, StringComparison.Ordinal);
        Assert.DoesNotContain("false", view.TargetLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(CombatInspect.NoObservationLabel, view.LastHitLine, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnreadableTargetKeepsTheReasonAndIsNotAbsent()
    {
        CombatView view = CombatInspect.Inspect(
            null,
            ClassifiedValue<bool>.Unknown(TargetRoiCalibration.NotCalibratedReason),
            Now);

        Assert.Equal(TargetFrameState.Unreadable, view.TargetState);
        Assert.Contains(CombatInspect.UnreadableLabel, view.TargetLine, StringComparison.Ordinal);
        Assert.Contains(TargetRoiCalibration.NotCalibratedReason, view.TargetLine, StringComparison.Ordinal);
        Assert.DoesNotContain(CombatInspect.AbsentLabel, view.TargetLine, StringComparison.Ordinal);
        Assert.DoesNotContain(CombatInspect.PresentLabel, view.TargetLine, StringComparison.Ordinal);
        Assert.Equal("UNKNOWN", view.Fields.Single(f => f.Label == "Bersaglio").Source);
    }

    [Fact]
    public void UnpublishedCombatIsNoObservationNotAnEmptyFight()
    {
        CombatView view = CombatInspect.Inspect(null, null, Now);

        Assert.Contains(CombatInspect.NoObservationLabel, view.LastHitLine, StringComparison.Ordinal);
        Assert.Contains(CombatInspect.UnreadableLabel, view.TargetLine, StringComparison.Ordinal);
        Assert.Contains(GameplayObservation.NotPublishedReason, view.LastHitLine, StringComparison.Ordinal);
        Assert.All(view.Fields, f => Assert.Equal("UNKNOWN", f.Source));
    }

    [Fact]
    public void TheThreeTargetValuesStayDistinct()
    {
        CombatView present = CombatInspect.Inspect(null, ClassifiedValue<bool>.Derived(true), Now);
        CombatView absent = CombatInspect.Inspect(null, ClassifiedValue<bool>.Derived(false), Now);
        CombatView unreadable = CombatInspect.Inspect(null, ClassifiedValue<bool>.Unknown("x"), Now);

        Assert.Equal(TargetFrameState.Present, present.TargetState);
        Assert.Equal(TargetFrameState.Absent, absent.TargetState);
        Assert.Equal(TargetFrameState.Unreadable, unreadable.TargetState);
        Assert.NotEqual(present.TargetLine, absent.TargetLine);
        Assert.NotEqual(absent.TargetLine, unreadable.TargetLine);
        Assert.NotEqual(present.TargetLine, unreadable.TargetLine);
    }

    [Fact]
    public void TheViewHasNoWritePathIntoTheRuntime()
    {
        string source = File.ReadAllText(Path.Combine(
            SurroundingsInspectTests.RepositoryRoot(), "src", "NosAi.ControlPanel", "CombatInspect.cs"));
        SurroundingsInspectTests.AssertNoWrite(source);
    }
}
