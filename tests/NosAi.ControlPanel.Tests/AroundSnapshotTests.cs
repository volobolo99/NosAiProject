using System.IO;
using NosAi.ControlPanel;
using NosAi.LiveIntegration;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Hardware;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.ControlPanel.Tests;

/// <summary>
/// Hosted and attached snapshots feed the surroundings and combat inspects
/// without collapsing empty observation into an empty map.
/// </summary>
public sealed class AroundSnapshotTests
{
    private static readonly DateTime Now = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AnEmptySnapshotIsNoObservationNotAnEmptyMap()
    {
        SnapshotView snapshot = SnapshotView.Empty("offline");
        SurroundingsView around = SurroundingsInspect.Inspect(snapshot.Entities, Now);
        CombatView combat = CombatInspect.Inspect(snapshot.HitBy, snapshot.HasTarget, Now);

        Assert.Equal(SurroundingsKind.NoObservation, around.Kind);
        Assert.Contains(SurroundingsInspect.NoObservationLabel, around.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(SurroundingsInspect.NoEntitiesAroundLabel, around.Summary, StringComparison.Ordinal);
        Assert.Contains(CombatInspect.NoObservationLabel, combat.LastHitLine, StringComparison.Ordinal);
        Assert.Contains(CombatInspect.UnreadableLabel, combat.TargetLine, StringComparison.Ordinal);
        Assert.DoesNotContain(CombatInspect.AbsentLabel, combat.TargetLine, StringComparison.Ordinal);
    }

    [Fact]
    public void AHostedSnapshotCopiesTypedGameplay()
    {
        var observation = new GameplayObservation(
            ClassifiedValue<int>.Derived(100, Now),
            ClassifiedValue<int>.Derived(100, Now),
            ClassifiedValue<int>.Derived(50, Now),
            ClassifiedValue<int>.Derived(50, Now),
            ClassifiedValue<bool>.Derived(true, Now),
            ClassifiedValue<bool>.Derived(false, Now),
            ClassifiedValue<int>.Derived(1, Now),
            Now)
        {
            Entities = ClassifiedValue<IReadOnlyList<SelectableEntity>>.Live(
            [
                new SelectableEntity(101, new MapPoint(12, 8), 0.75, Now),
                new SelectableEntity(102, new MapPoint(3, 4), null, Now.AddSeconds(-30))
            ], Now),
            HitBy = ClassifiedValue<Aggressor>.Live(new Aggressor(77, 2), Now.AddSeconds(-5))
        };

        SnapshotView snapshot = SnapshotView.From(Gate1SnapshotFactory.Create(
            RuntimeHealthStatus.Healthy,
            "test",
            new LiveHardwareTelemetry(new FallbackHardwareProbe()).Capture().View,
            new ClientBaselineSnapshot(
                ProcessDetected: false,
                WindowDetected: false,
                ClientAttached: false,
                ProcessId: null,
                WindowHandle: IntPtr.Zero,
                Source: "test",
                ObservedAtUtc: Now,
                Availability: ClientBaselineAvailability.Unavailable,
                Status: "client_unavailable",
                Warning: null,
                FailureReason: "connector_not_bound"),
            new Gate1ConnectionSnapshot(string.Empty, false, false, default, null),
            NosAi.Runtime.Safety.RuntimeSafetyPolicy.SafeDefault,
            warning: null,
            gameplay: observation));

        SurroundingsView around = SurroundingsInspect.Inspect(snapshot.Entities, Now);
        CombatView combat = CombatInspect.Inspect(snapshot.HitBy, snapshot.HasTarget, Now);

        Assert.Equal(SurroundingsKind.Populated, around.Kind);
        Assert.Equal("0s", around.Rows[0].Age);
        Assert.Equal("30s", around.Rows[1].Age);
        Assert.Contains("id=77", combat.LastHitLine, StringComparison.Ordinal);
        Assert.Contains("età=5s", combat.LastHitLine, StringComparison.Ordinal);
        Assert.Equal(NosAi.Runtime.Perception.TargetFrameState.Present, combat.TargetState);
        Assert.DoesNotContain("true", combat.TargetLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnAttachedEmptyEntityListIsNoEntitiesAround()
    {
        SnapshotView snapshot = AttachedSnapshot.Parse("""
            {
              "contractVersion": "gate1.snapshot.v1",
              "client": {
                "gameplayBaseline": {
                  "source": "DERIVED",
                  "hasObservedValue": true,
                  "value": {
                    "entities": {
                      "source": "LIVE",
                      "hasObservedValue": true,
                      "observedAtUtc": "2026-09-02T12:00:00.0000000Z",
                      "value": []
                    },
                    "hitBy": { "source": "UNKNOWN", "failureReason": "not_published_by_provider" },
                    "hasTarget": { "source": "UNKNOWN", "failureReason": "not_published_by_provider" }
                  }
                }
              }
            }
            """);

        SurroundingsView around = SurroundingsInspect.Inspect(snapshot.Entities, Now);
        Assert.Equal(SurroundingsKind.NoEntitiesAround, around.Kind);
        Assert.Equal(SurroundingsInspect.NoEntitiesAroundLabel, around.Summary);
        Assert.DoesNotContain(SurroundingsInspect.NoObservationLabel, around.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAttachedPopulatedListKeepsAgeAndThreeValuedTarget()
    {
        SnapshotView snapshot = AttachedSnapshot.Parse("""
            {
              "contractVersion": "gate1.snapshot.v1",
              "client": {
                "gameplayBaseline": {
                  "source": "DERIVED",
                  "hasObservedValue": true,
                  "value": {
                    "entities": {
                      "source": "LIVE",
                      "hasObservedValue": true,
                      "observedAtUtc": "2026-09-02T12:00:00.0000000Z",
                      "value": [
                        { "entityId": 101, "x": 12, "y": 8, "hpRatio": 0.75, "observedAtUtc": "2026-09-02T12:00:00.0000000Z" },
                        { "entityId": 102, "x": 3, "y": 4, "hpRatio": null, "observedAtUtc": "2026-09-02T11:59:30.0000000Z" }
                      ]
                    },
                    "hitBy": {
                      "source": "LIVE",
                      "hasObservedValue": true,
                      "observedAtUtc": "2026-09-02T11:59:55.0000000Z",
                      "value": { "entityId": 77, "entityType": 2 }
                    },
                    "hasTarget": {
                      "source": "DERIVED",
                      "hasObservedValue": true,
                      "value": false
                    }
                  }
                }
              }
            }
            """);

        SurroundingsView around = SurroundingsInspect.Inspect(snapshot.Entities, Now);
        CombatView combat = CombatInspect.Inspect(snapshot.HitBy, snapshot.HasTarget, Now);

        Assert.Equal(SurroundingsKind.Populated, around.Kind);
        Assert.Equal("0s", around.Rows[0].Age);
        Assert.Equal("30s", around.Rows[1].Age);
        Assert.Contains(SurroundingsInspect.HpNotStated, around.Rows[1].Life, StringComparison.Ordinal);
        Assert.Contains("età=5s", combat.LastHitLine, StringComparison.Ordinal);
        Assert.Equal(NosAi.Runtime.Perception.TargetFrameState.Absent, combat.TargetState);
        Assert.Contains(CombatInspect.AbsentLabel, combat.TargetLine, StringComparison.Ordinal);
        Assert.DoesNotContain("false", combat.TargetLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnAttachedSnapshotWithoutGameplayIsNoObservation()
    {
        SnapshotView snapshot = AttachedSnapshot.Parse("""
            {
              "contractVersion": "gate1.snapshot.v1",
              "runtimeStatus": "Degraded"
            }
            """);

        SurroundingsView around = SurroundingsInspect.Inspect(snapshot.Entities, Now);
        Assert.Equal(SurroundingsKind.NoObservation, around.Kind);
        Assert.DoesNotContain(SurroundingsInspect.NoEntitiesAroundLabel, around.Summary, StringComparison.Ordinal);
        Assert.Contains("gameplay_provider_not_available", around.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAroundViewsHaveNoWritePathAndCannotMarkASessionActuating()
    {
        string root = SurroundingsInspectTests.RepositoryRoot();
        string[] files =
        [
            Path.Combine(root, "src", "NosAi.ControlPanel", "SurroundingsInspect.cs"),
            Path.Combine(root, "src", "NosAi.ControlPanel", "CombatInspect.cs"),
            Path.Combine(root, "src", "NosAi.ControlPanel", "KeybindsInspect.cs"),
            Path.Combine(root, "src", "NosAi.ControlPanel", "GameplayWireReader.cs"),
            Path.Combine(root, "src", "NosAi.ControlPanel", "MainWindow.xaml"),
            Path.Combine(root, "src", "NosAi.ControlPanel", "MainWindow.xaml.cs")
        ];

        string xaml = File.ReadAllText(files[4]);
        string code = File.ReadAllText(files[5]);
        Assert.Contains("NavAround", xaml, StringComparison.Ordinal);
        Assert.Contains("Combattimento", xaml, StringComparison.Ordinal);
        Assert.Contains("Tasti", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Riprova", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BeginSession", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IsActuating = true", code, StringComparison.Ordinal);
        Assert.Contains("ApplyAround", code, StringComparison.Ordinal);

        for (int i = 0; i < 4; i++)
            SurroundingsInspectTests.AssertNoWrite(File.ReadAllText(files[i]));
    }
}
