using System.Text.Json;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Hardware;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Orchestration;
using NosAi.Runtime.Safety;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// X-P3: the session-authority surface — CLI formatting, the decision-cycle
/// refresh, and the additive snapshot fields.
/// </summary>
public sealed class InputAuthoritySurfaceTests
{
    private static readonly string[] ExistingSafetyKeys =
    [
        "liveInputEnabled",
        "packetInjectionEnabled",
        "requireClientHealthy",
        "requireGuardApproval",
        "executionMode"
    ];

    private static readonly string[] SessionAuthorityKeys =
    [
        "sessionActuating",
        "sessionAuthorityReason",
        "sessionAuthorityTerminal",
        "runtimeIntegrity",
        "clientIntegrity"
    ];

    [Fact]
    public void TheRuntimeWiresTheInputAuthorityFlag()
    {
        string root = RepositoryRoot();
        string program = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "Program.cs"));
        Assert.Contains("--input-authority", program, StringComparison.Ordinal);
        Assert.Contains("InputAuthorityProbe.Run", program, StringComparison.Ordinal);
        Assert.Contains("\"--input-authority\"", program, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatNamesAnActuatingSession()
    {
        var reading = new InputAuthorityReading(
            Window: "pid=4321 handle=0x4100",
            RuntimeIntegrity: "medium",
            ClientIntegrity: "medium",
            IsActuating: true,
            RefusalReason: null,
            IsTerminal: false,
            PointerErrorPixels: 0,
            Age: "12ms");

        string report = InputAuthorityProbe.Format(reading);

        Assert.Contains("window:    pid=4321 handle=0x4100", report, StringComparison.Ordinal);
        Assert.Contains("runtime:   medium", report, StringComparison.Ordinal);
        Assert.Contains("client:    medium", report, StringComparison.Ordinal);
        Assert.Contains("session:   actuating", report, StringComparison.Ordinal);
        Assert.DoesNotContain("not-actuating", report, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatNamesANonTerminalRefusal()
    {
        var reading = new InputAuthorityReading(
            Window: "pid=4321 handle=0x4100",
            RuntimeIntegrity: "medium",
            ClientIntegrity: "medium",
            IsActuating: false,
            RefusalReason: SessionActuationAuthority.WindowNotForegroundReason,
            IsTerminal: false,
            PointerErrorPixels: -1,
            Age: "5ms");

        string report = InputAuthorityProbe.Format(reading);

        Assert.Contains("not-actuating", report, StringComparison.Ordinal);
        Assert.Contains(SessionActuationAuthority.WindowNotForegroundReason, report, StringComparison.Ordinal);
        Assert.Contains("terminal=false", report, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatNamesATerminalRefusal()
    {
        const string reason = "authority_integrity_below_client:medium_under_high";
        var reading = new InputAuthorityReading(
            Window: "pid=4321 handle=0x4100",
            RuntimeIntegrity: "medium",
            ClientIntegrity: "high",
            IsActuating: false,
            RefusalReason: reason,
            IsTerminal: true,
            PointerErrorPixels: -1,
            Age: "8ms");

        string report = InputAuthorityProbe.Format(reading);

        Assert.Contains("not-actuating", report, StringComparison.Ordinal);
        Assert.Contains(reason, report, StringComparison.Ordinal);
        Assert.Contains(SessionActuationAuthority.IntegrityBelowClientPrefix, report, StringComparison.Ordinal);
        Assert.Contains("terminal=true", report, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatNamesTheAbsenceOfASession()
    {
        var components = RuntimeComposition.CreateSafe();
        Assert.NotNull(components.SessionAuthority);

        InputAuthorityReading reading = InputAuthorityProbe.Observe(components.SessionAuthority!);
        string report = InputAuthorityProbe.Format(reading);

        Assert.Equal(SessionActuationAuthority.NoSessionReason, reading.Window);
        Assert.Equal(SessionActuationAuthority.NoSessionReason, reading.RefusalReason);
        Assert.False(reading.IsActuating);
        Assert.False(reading.IsTerminal);
        Assert.Equal("unknown", reading.RuntimeIntegrity);
        Assert.Equal("unknown", reading.ClientIntegrity);
        Assert.Equal("never", reading.Age);
        Assert.Contains(SessionActuationAuthority.NoSessionReason, report, StringComparison.Ordinal);
        Assert.Contains("not-actuating", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDecisionCycleCallsEnsureVerifiedBeforeComposingThePlan()
    {
        var order = new List<string>();
        var orchestrator = new Gate3ExecutionOrchestrator(
            ensureSessionVerified: () => order.Add("ensure"));

        Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(
            Gate3WorldState.Live(200, 5000, 1420, false, false));

        Assert.Contains("ensure", order);
        Assert.Equal("ensure", order[0]);
        Assert.NotEqual(CycleOutcome.NoWorldState, result.Outcome);

        string cycle = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "NosAi.Runtime", "Gate3", "Gate3Runtime.cs"));
        int invoke = cycle.IndexOf("_ensureSessionVerified?.Invoke()", StringComparison.Ordinal);
        int canExecute = cycle.IndexOf("if (CanExecute && state.IsSimulated)", StringComparison.Ordinal);
        int plan = cycle.IndexOf("_planner.PlanCandidates(state)", StringComparison.Ordinal);
        Assert.True(invoke >= 0 && canExecute >= 0 && plan >= 0, "cycle call sites missing");
        Assert.True(invoke < canExecute, "EnsureVerified must run before the effector is queried.");
        Assert.True(invoke < plan, "EnsureVerified must run before the plan is composed.");

        string host = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "NosAi.Runtime", "Gate1", "Gate1BootstrapHost.cs"));
        Assert.Contains("ensureSessionVerified: () => runtime.SessionAuthority?.EnsureVerified()", host, StringComparison.Ordinal);
        Assert.Contains("NoteForegroundRestored", host, StringComparison.Ordinal);
        Assert.Contains("--input-authority --watch", host, StringComparison.Ordinal);
    }

    [Fact]
    public void OperatorSnapshotCarriesTheNewFieldsAndKeepsTheExistingOnes()
    {
        var components = RuntimeComposition.CreateSafe();
        Gate1CanonicalSnapshot snapshot = Snapshot(components.SessionAuthority);

        Assert.False(snapshot.Safety.SessionActuating.Value);
        Assert.Equal(SessionActuationAuthority.NoSessionReason, snapshot.Safety.SessionAuthorityReason.Value);
        Assert.False(snapshot.Safety.SessionAuthorityTerminal.Value);
        Assert.Equal("unreadable", snapshot.Safety.RuntimeIntegrity.FailureReason);
        Assert.Equal("unreadable", snapshot.Safety.ClientIntegrity.FailureReason);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(snapshot.ToWire()));
        Assert.Equal(Gate1SnapshotContract.Version, document.RootElement.GetProperty("contractVersion").GetString());
        JsonElement safety = document.RootElement.GetProperty("safety");

        foreach (string name in ExistingSafetyKeys)
            Assert.True(safety.TryGetProperty(name, out _), $"lost existing field {name}");
        foreach (string name in SessionAuthorityKeys)
            Assert.True(safety.TryGetProperty(name, out _), $"missing additive field {name}");

        Assert.Equal("disabled_by_operator", safety.GetProperty("executionMode").GetProperty("value").GetString());
        Assert.False(safety.GetProperty("sessionActuating").GetProperty("value").GetBoolean());
        Assert.Equal(
            SessionActuationAuthority.NoSessionReason,
            safety.GetProperty("sessionAuthorityReason").GetProperty("value").GetString());
    }

    [Fact]
    public void AdditiveSnapshotFieldsDoNotRenameTheExistingContract()
    {
        Gate1CanonicalSnapshot withoutAuthority = Snapshot(sessionAuthority: null);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(withoutAuthority.ToWire()));
        JsonElement safety = document.RootElement.GetProperty("safety");

        foreach (string name in ExistingSafetyKeys)
            Assert.True(safety.TryGetProperty(name, out JsonElement field) && field.TryGetProperty("value", out _), name);

        Assert.Equal("UNKNOWN", safety.GetProperty("sessionActuating").GetProperty("source").GetString());
        Assert.Equal("authority_not_bound", safety.GetProperty("sessionActuating").GetProperty("failureReason").GetString());
        Assert.Equal(Gate1SnapshotContract.Version, document.RootElement.GetProperty("contractVersion").GetString());
    }

    [Fact]
    public void ProbeRefusesOffWindows()
    {
        if (OperatingSystem.IsWindows())
            return;

        Assert.Equal(2, InputAuthorityProbe.Run());
    }

    private static Gate1CanonicalSnapshot Snapshot(SessionActuationAuthority? sessionAuthority)
    {
        var client = new ClientBaselineSnapshot(
            ProcessDetected: true,
            WindowDetected: true,
            ClientAttached: true,
            ProcessId: 4242,
            WindowHandle: (nint)0xABC,
            Source: "live_process_attach",
            ObservedAtUtc: DateTime.UtcNow,
            Availability: ClientBaselineAvailability.BaselineReady,
            Status: "attached_os_session",
            Warning: null,
            FailureReason: null,
            ProcessName: "NostaleClientX",
            WindowTitle: "NosTale",
            ProcessResponding: true,
            WindowVisible: true);

        return Gate1SnapshotFactory.Create(
            RuntimeHealthStatus.Healthy,
            "test",
            new LiveHardwareTelemetry(new FallbackHardwareProbe()).Capture().View,
            client,
            new Gate1ConnectionSnapshot(string.Empty, false, false, default, null),
            RuntimeSafetyPolicy.SafeDefault,
            sessionAuthority: sessionAuthority);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found: no NosAi.sln above the test assembly.");
        return directory!.FullName;
    }
}
