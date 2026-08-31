using NosAi.Runtime.Contracts;
using NosAi.LiveIntegration;
using NosAi.Runtime.Hardware;
using NosAi.Runtime.Safety;

namespace NosAi.Runtime.Gate1;

public static class Gate1SnapshotContract
{
    /// <summary>
    /// Additive fields on <c>client</c> (processName, windowHandle, windowTitle,
    /// processResponding, windowVisible, and the network group: networkConnected,
    /// serverEndpoint, connectionState, remoteSessionCount) stay on v1: unknown
    /// keys are ignored by older readers, and the Python dashboard requires an
    /// exact version match.
    /// </summary>
    public const string Version = "gate1.snapshot.v1";
}

public enum RuntimeHealthStatus
{
    Bootstrapping = 0,
    Healthy = 1,
    Degraded = 2,
    Failed = 3,
    Stopping = 4,
    Stopped = 5
}

public sealed record Gate1HardwareView(
    ClassifiedValue<string> Platform,
    ClassifiedValue<string> Cpu,
    ClassifiedValue<int> LogicalCores,
    ClassifiedValue<long> ProcessWorkingSetMb,
    ClassifiedValue<long> SystemRamMb,
    ClassifiedValue<string> Gpu,
    ClassifiedValue<long> GpuMemoryMb,
    ClassifiedValue<int> DisplayRefreshHz,
    ClassifiedValue<string> OsVersion);

public sealed record Gate1ClientView(
    ClassifiedValue<bool> ProcessDetected,
    ClassifiedValue<bool> WindowDetected,
    ClassifiedValue<bool> Attached,
    ClassifiedValue<int?> ProcessId,
    ClassifiedValue<string> ProcessName,
    ClassifiedValue<string> WindowHandle,
    ClassifiedValue<string> WindowTitle,
    ClassifiedValue<bool> ProcessResponding,
    ClassifiedValue<bool> WindowVisible,
    ClassifiedValue<string> Availability,
    ClassifiedValue<object> GameplayBaseline,
    string Status,
    string? Warning,
    string? FailureReason,
    // Additive on v1 (see Gate1SnapshotContract): the client's own TCP state, read
    // from the operating system. Unknown keys are ignored by older readers.
    ClassifiedValue<bool> NetworkConnected,
    ClassifiedValue<string> ServerEndpoint,
    ClassifiedValue<string> ConnectionState,
    ClassifiedValue<int?> RemoteSessionCount,
    // The same reading as GameplayBaseline, kept typed for consumers inside the
    // process. GameplayBaseline is its wire form and stays the only thing
    // serialised; two serialised copies would be two things to keep in step.
    // Null means no provider is attached, which is not the same as a provider
    // that is attached and reading nothing.
    GameplayObservation? Gameplay = null);

public sealed record Gate1GuardSessionView(
    ClassifiedValue<bool> Connected,
    ClassifiedValue<bool> Authenticated,
    ClassifiedValue<string?> SessionId,
    ClassifiedValue<DateTime?> LastHeartbeatUtc,
    ClassifiedValue<string?> TerminationReason);

public sealed record Gate1SafetyView(
    ClassifiedValue<bool> LiveInputEnabled,
    ClassifiedValue<bool> PacketInjectionEnabled,
    ClassifiedValue<bool> RequireClientHealthy,
    ClassifiedValue<bool> RequireGuardApproval,
    ClassifiedValue<string> ExecutionMode);

public sealed record Gate1CanonicalSnapshot(
    string ContractVersion,
    RuntimeHealthStatus RuntimeStatus,
    DateTime CapturedAtUtc,
    string CorrelationId,
    Gate1HardwareView Hardware,
    Gate1ClientView Client,
    Gate1GuardSessionView Guard,
    Gate1SafetyView Safety,
    string? Warning)
{
    public object ToWire() => new
    {
        contractVersion = ContractVersion,
        runtimeStatus = RuntimeStatus.ToString(),
        capturedAtUtc = CapturedAtUtc,
        correlationId = CorrelationId,
        warning = Warning,
        hardware = new
        {
            platform = Hardware.Platform.ToWire(),
            cpu = Hardware.Cpu.ToWire(),
            logicalCores = Hardware.LogicalCores.ToWire(),
            processWorkingSetMb = Hardware.ProcessWorkingSetMb.ToWire(),
            systemRamMb = Hardware.SystemRamMb.ToWire(),
            gpu = Hardware.Gpu.ToWire(),
            gpuMemoryMb = Hardware.GpuMemoryMb.ToWire(),
            displayRefreshHz = Hardware.DisplayRefreshHz.ToWire(),
            osVersion = Hardware.OsVersion.ToWire()
        },
        client = new
        {
            processDetected = Client.ProcessDetected.ToWire(),
            windowDetected = Client.WindowDetected.ToWire(),
            attached = Client.Attached.ToWire(),
            processId = Client.ProcessId.ToWire(),
            processName = Client.ProcessName.ToWire(),
            windowHandle = Client.WindowHandle.ToWire(),
            windowTitle = Client.WindowTitle.ToWire(),
            processResponding = Client.ProcessResponding.ToWire(),
            windowVisible = Client.WindowVisible.ToWire(),
            availability = Client.Availability.ToWire(),
            gameplayBaseline = Client.GameplayBaseline.ToWire(),
            networkConnected = Client.NetworkConnected.ToWire(),
            serverEndpoint = Client.ServerEndpoint.ToWire(),
            connectionState = Client.ConnectionState.ToWire(),
            remoteSessionCount = Client.RemoteSessionCount.ToWire(),
            status = Client.Status,
            warning = Client.Warning,
            failureReason = Client.FailureReason
        },
        guard = new
        {
            connected = Guard.Connected.ToWire(),
            authenticated = Guard.Authenticated.ToWire(),
            sessionId = Guard.SessionId.ToWire(),
            lastHeartbeatUtc = Guard.LastHeartbeatUtc.ToWire(),
            terminationReason = Guard.TerminationReason.ToWire()
        },
        safety = new
        {
            liveInputEnabled = Safety.LiveInputEnabled.ToWire(),
            packetInjectionEnabled = Safety.PacketInjectionEnabled.ToWire(),
            requireClientHealthy = Safety.RequireClientHealthy.ToWire(),
            requireGuardApproval = Safety.RequireGuardApproval.ToWire(),
            executionMode = Safety.ExecutionMode.ToWire()
        }
    };
}

public static class Gate1SnapshotFactory
{
    public static Gate1CanonicalSnapshot Create(
        RuntimeHealthStatus runtimeStatus,
        string correlationId,
        Gate1HardwareView hardware,
        ClientBaselineSnapshot client,
        Gate1ConnectionSnapshot guard,
        RuntimeSafetyPolicy safety,
        string? warning = null,
        GameplayObservation? gameplay = null)
    {
        var now = DateTime.UtcNow;
        var clientObserved = client.ObservedAtUtc;

        // The key is the one gate1.snapshot.v1 already published, so a reader that
        // does not know about the inner fields sees what it always saw. With no
        // provider attached the value stays UNKNOWN with the same reason as before;
        // with one attached it carries a per-field classified reading, and a field
        // the provider could not read stays UNKNOWN inside it rather than becoming
        // a zero.
        ClassifiedValue<object> gameplayBaseline = gameplay is null
            ? ClassifiedValue<object>.Unknown(
                "gameplay_provider_not_available",
                "Gate 1 does not extract gameplay memory. Process/window/title are the current client baseline.")
            : gameplay.HasVitals
                ? ClassifiedValue<object>.Derived(gameplay.ToWire(), gameplay.ObservedAtUtc)
                : ClassifiedValue<object>.Unknown(
                    gameplay.UnusableReason ?? "gameplay_incomplete",
                    "A gameplay provider is attached but could not read the vitals.");

        var network = client.Network;

        return new Gate1CanonicalSnapshot(
            ContractVersion: Gate1SnapshotContract.Version,
            RuntimeStatus: runtimeStatus,
            CapturedAtUtc: now,
            CorrelationId: correlationId,
            Hardware: hardware,
            Client: new Gate1ClientView(
                ProcessDetected: ClassifiedValue<bool>.Live(client.ProcessDetected, clientObserved),
                WindowDetected: ClassifiedValue<bool>.Live(client.WindowDetected, clientObserved),
                Attached: ClassifiedValue<bool>.Live(client.ClientAttached, clientObserved),
                ProcessId: client.ProcessId is int pid
                    ? ClassifiedValue<int?>.Live(pid, clientObserved)
                    : ClassifiedValue<int?>.Unknown(client.FailureReason ?? "process_not_attached"),
                ProcessName: ClassifyText(client.ProcessName, clientObserved, client.FailureReason ?? "process_not_attached"),
                WindowHandle: client.WindowHandle != IntPtr.Zero
                    ? ClassifiedValue<string>.Live($"0x{client.WindowHandle.ToInt64():X}", clientObserved)
                    : ClassifiedValue<string>.Unknown(client.FailureReason ?? "window_not_attached"),
                WindowTitle: ClassifyText(
                    client.WindowTitle,
                    clientObserved,
                    client.WindowTitleFailureReason ?? "window_title_unavailable"),
                ProcessResponding: client.ProcessResponding is bool responding
                    ? ClassifiedValue<bool>.Live(responding, clientObserved)
                    : ClassifiedValue<bool>.Unknown(client.FailureReason ?? "process_not_attached"),
                WindowVisible: client.WindowVisible is bool visible
                    ? ClassifiedValue<bool>.Live(visible, clientObserved)
                    : ClassifiedValue<bool>.Unknown(
                        client.WindowHandle == IntPtr.Zero ? "window_not_attached" : "window_visibility_unavailable"),
                Availability: ClassifiedValue<string>.Live(client.Availability.ToString(), clientObserved),
                GameplayBaseline: gameplayBaseline,
                Gameplay: gameplay,
                Status: client.Status,
                Warning: client.Warning,
                FailureReason: client.FailureReason,
                NetworkConnected: network is { Observed: true }
                    ? ClassifiedValue<bool>.Live(network.RemoteSessions.Count > 0, clientObserved)
                    : ClassifiedValue<bool>.Unknown(network?.FailureReason ?? "network_not_observed"),
                // UNKNOWN when several remote sessions exist, not a guess: a
                // launcher and the game look alike from outside the process.
                ServerEndpoint: network?.Primary is { } primary
                    ? ClassifiedValue<string>.Live(primary.Remote.ToString(), clientObserved)
                    : ClassifiedValue<string>.Unknown(
                        network is not { Observed: true } ? network?.FailureReason ?? "network_not_observed"
                        : network.RemoteSessions.Count == 0 ? "no_remote_session"
                        : "several_remote_sessions"),
                ConnectionState: network?.Primary is { } state
                    ? ClassifiedValue<string>.Live(state.State.ToString(), clientObserved)
                    : ClassifiedValue<string>.Unknown(
                        network is not { Observed: true } ? network?.FailureReason ?? "network_not_observed"
                        : network.RemoteSessions.Count == 0 ? "no_remote_session"
                        : "several_remote_sessions"),
                RemoteSessionCount: network is { Observed: true }
                    ? ClassifiedValue<int?>.Live(network.RemoteSessions.Count, clientObserved)
                    : ClassifiedValue<int?>.Unknown(network?.FailureReason ?? "network_not_observed")),
            Guard: new Gate1GuardSessionView(
                Connected: ClassifiedValue<bool>.Live(guard.Connected, guard.LastHeartbeatUtc == default ? now : guard.LastHeartbeatUtc),
                Authenticated: ClassifiedValue<bool>.Live(guard.Authenticated, guard.LastHeartbeatUtc == default ? now : guard.LastHeartbeatUtc),
                SessionId: string.IsNullOrWhiteSpace(guard.SessionId)
                    ? ClassifiedValue<string?>.Unknown("no_active_session")
                    : ClassifiedValue<string?>.Live(guard.SessionId, now),
                LastHeartbeatUtc: guard.LastHeartbeatUtc == default
                    ? ClassifiedValue<DateTime?>.Unknown("heartbeat_not_observed")
                    : ClassifiedValue<DateTime?>.Live(guard.LastHeartbeatUtc, guard.LastHeartbeatUtc),
                TerminationReason: guard.LastTerminationReason is null
                    ? ClassifiedValue<string?>.Unknown("no_termination")
                    : ClassifiedValue<string?>.Live(guard.LastTerminationReason, now)),
            Safety: new Gate1SafetyView(
                LiveInputEnabled: ClassifiedValue<bool>.Live(safety.LiveInputEnabled, now),
                PacketInjectionEnabled: ClassifiedValue<bool>.Live(safety.PacketInjectionEnabled, now),
                RequireClientHealthy: ClassifiedValue<bool>.Live(safety.RequireClientHealthy, now),
                RequireGuardApproval: ClassifiedValue<bool>.Live(safety.RequireGuardApproval, now),
                // Derived from the policy in force, not a fixed string. It used to
                // read "disabled_in_gate1" whatever the operator had set, so the
                // snapshot could report execution off while input injection was
                // armed — a safety label that did not track the safety state.
                ExecutionMode: ClassifiedValue<string>.Live(
                    safety.LiveInputEnabled || safety.PacketInjectionEnabled
                        ? "enabled_by_operator"
                        : "disabled_by_operator",
                    now)),
            Warning: warning);
    }

    private static ClassifiedValue<string> ClassifyText(string? value, DateTime observedAtUtc, string missingReason)
        => string.IsNullOrWhiteSpace(value)
            ? ClassifiedValue<string>.Unknown(missingReason)
            : ClassifiedValue<string>.Live(value, observedAtUtc);
}
