using Xunit;

namespace NosAi.ControlPanel.Tests;

public sealed class AttachedSnapshotTests
{
    [Fact]
    public void Occupied_slot_is_derived_from_live_flags()
    {
        var json = """
            {
              "contractVersion": "gate1.snapshot.v1",
              "runtimeStatus": "Degraded",
              "guard": {
                "connected": { "value": true, "source": "LIVE" },
                "authenticated": { "value": false, "source": "LIVE" },
                "sessionId": { "source": "UNKNOWN", "failureReason": "not_authenticated" },
                "lastHeartbeatUtc": { "source": "UNKNOWN" },
                "terminationReason": { "source": "UNKNOWN" }
              }
            }
            """;

        var view = AttachedSnapshot.Parse(json);
        Assert.Equal("SLOT OCCUPATO", view.SlotLabel);
        Assert.Contains(view.Guard, f => f.Label == "Wire (questo build)");
    }

    [Fact]
    public void Unknown_contract_is_not_silently_accepted()
    {
        var view = AttachedSnapshot.Parse("""{"contractVersion":"gate1.snapshot.v0"}""");
        Assert.Contains("unsupported_contract_version", view.Warning);
    }
}
