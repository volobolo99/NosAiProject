using Xunit;

namespace NosAi.ControlPanel.Tests;

public sealed class OperatorHealthTests
{
    [Fact]
    public void Idle_or_unreachable_does_not_invent_ok()
    {
        var idle = OperatorHealth.From(SnapshotView.Empty("offline"), SessionKind.Idle);
        Assert.Contains(idle, f => f.Label == "API ok" && f.Source == "UNKNOWN");
        Assert.Contains(idle, f => f.Label == "Stream eventi" && f.Source == "UNKNOWN");

        var lost = OperatorHealth.From(SnapshotView.Empty("runtime_unreachable: HttpRequestException"), SessionKind.Attached);
        Assert.Contains(lost, f => f.Label == "API ok" && f.Source == "UNKNOWN");
    }

    [Fact]
    public void Healthy_snapshot_is_ok_without_inventing_events()
    {
        var snapshot = new SnapshotView
        {
            RuntimeStatus = "Healthy",
            ContractVersion = "gate1.snapshot.v1"
        };
        var fields = OperatorHealth.From(snapshot, SessionKind.Hosted);
        Assert.Contains(fields, f => f.Label == "API ok" && f.Value == "sì" && f.Source == "LIVE");
        Assert.Contains(fields, f => f.Label == "Contratto snapshot" && f.Value == "gate1.snapshot.v1");
        Assert.Contains(fields, f => f.Label == "Stream eventi" && f.Source == "UNKNOWN");
    }
}
