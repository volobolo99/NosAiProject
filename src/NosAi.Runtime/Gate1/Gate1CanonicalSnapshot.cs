using NosAi.Runtime.Contracts;
using NosAi.LiveIntegration;
using NosAi.Runtime.Hardware;
using NosAi.Runtime.Safety;

namespace NosAi.Runtime.Gate1;

public static class Gate1SnapshotContract
{
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
    ClassifiedValue<string> Availability,
    ClassifiedValue<object> GameplayBaseline,
    string Status,
    string? Warning,
    string? FailureReason);

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
            availability = Client.Availability.ToWire(),
            gameplayBaseline = Client.GameplayBaseline.ToWire(),
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
        string? warning = null)
    {
        var now = DateTime.UtcNow;
        var clientObserved = client.ObservedAtUtc;
        var gameplayUnknown = ClassifiedValue<object>.Unknown(
            "gameplay_provider_not_available",
            "Gate 1 does not extract gameplay memory. Process/window attachment is the current client baseline.");

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
                Availability: ClassifiedValue<string>.Live(client.Availability.ToString(), clientObserved),
                GameplayBaseline: gameplayUnknown,
                Status: client.Status,
                Warning: client.Warning,
                FailureReason: client.FailureReason),
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
                ExecutionMode: ClassifiedValue<string>.Live("disabled_in_gate1", now)),
            Warning: warning);
    }
}
