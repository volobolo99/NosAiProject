using System.IO;
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
    public void Session_authority_fields_are_additive_and_an_old_snapshot_still_parses()
    {
        var view = AttachedSnapshot.Parse("""
            {
              "contractVersion": "gate1.snapshot.v1",
              "runtimeStatus": "Healthy",
              "safety": {
                "executionMode": { "value": "disabled_by_operator", "source": "LIVE" },
                "liveInputEnabled": { "value": false, "source": "LIVE" },
                "packetInjectionEnabled": { "value": false, "source": "LIVE" }
              }
            }
            """);

        Assert.Contains(view.Safety, f => f.Label == "Esecuzione" && f.Value.Contains("disabled_by_operator"));
        Assert.Contains(view.Safety, f => f.Label == "Sessione attuante" && f.Source == "UNKNOWN");
        Assert.Equal("Sessione: UNKNOWN", view.SessionAuthorityLine);
    }

    [Fact]
    public void Actuating_session_is_named_without_a_retry()
    {
        var view = AttachedSnapshot.Parse("""
            {
              "contractVersion": "gate1.snapshot.v1",
              "safety": {
                "executionMode": { "value": "enabled_by_operator", "source": "LIVE" },
                "sessionActuating": { "value": true, "source": "DERIVED" },
                "sessionAuthorityReason": { "value": null, "source": "UNKNOWN", "failureReason": "session_actuating" },
                "sessionAuthorityTerminal": { "value": false, "source": "DERIVED" },
                "runtimeIntegrity": { "value": "medium", "source": "LIVE" },
                "clientIntegrity": { "value": "medium", "source": "LIVE" }
              }
            }
            """);

        Assert.Equal("Sessione attuante", view.SessionAuthorityLine);
        Assert.DoesNotContain("Riprova", view.SessionAuthorityLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Terminal_verdict_is_named_in_full_and_offers_no_retry()
    {
        const string reason = "authority_integrity_below_client:medium_under_high";
        var view = AttachedSnapshot.Parse($$"""
            {
              "contractVersion": "gate1.snapshot.v1",
              "safety": {
                "executionMode": { "value": "disabled_by_operator", "source": "LIVE" },
                "sessionActuating": { "value": false, "source": "DERIVED" },
                "sessionAuthorityReason": { "value": "{{reason}}", "source": "DERIVED" },
                "sessionAuthorityTerminal": { "value": true, "source": "DERIVED" },
                "runtimeIntegrity": { "value": "medium", "source": "LIVE" },
                "clientIntegrity": { "value": "high", "source": "LIVE" }
              }
            }
            """);

        Assert.Equal($"Sessione non attuante, terminale: {reason}", view.SessionAuthorityLine);
        Assert.Contains(reason, view.SessionAuthorityLine, StringComparison.Ordinal);
        Assert.DoesNotContain("Riprova", view.SessionAuthorityLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_panel_has_no_retry_control_and_cannot_mark_a_session_actuating()
    {
        string root = RepositoryRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "NosAi.ControlPanel", "MainWindow.xaml"));
        string code = File.ReadAllText(Path.Combine(root, "src", "NosAi.ControlPanel", "MainWindow.xaml.cs"));
        string attached = File.ReadAllText(Path.Combine(root, "src", "NosAi.ControlPanel", "AttachedSnapshot.cs"));

        Assert.Contains("OverviewAuthority", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Riprova", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BeginSession", code, StringComparison.Ordinal);
        Assert.DoesNotContain(".Reset(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IsActuating = true", attached, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionActuating\": true", attached, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found: no NosAi.sln above the test assembly.");
        return directory!.FullName;
    }

    [Fact]
    public void Unknown_contract_is_not_silently_accepted()
    {
        var view = AttachedSnapshot.Parse("""{"contractVersion":"gate1.snapshot.v0"}""");
        Assert.Contains("unsupported_contract_version", view.Warning);
    }
}
