using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;

namespace NosAi.ControlPanel;

public sealed record DisplayField(string Label, string Value, string Source);

/// <summary>One rendering shape for hosted snapshots and HTTP-attached ones.</summary>
public sealed class SnapshotView
{
    public string RuntimeStatus { get; init; } = "Stopped";
    public string Warning { get; init; } = "";
    public string CapturedAt { get; init; } = "";
    public IReadOnlyList<DisplayField> Client { get; init; } = Array.Empty<DisplayField>();
    public IReadOnlyList<DisplayField> Guard { get; init; } = Array.Empty<DisplayField>();
    public IReadOnlyList<DisplayField> Hardware { get; init; } = Array.Empty<DisplayField>();
    public IReadOnlyList<DisplayField> Safety { get; init; } = Array.Empty<DisplayField>();
    public string WireLabel { get; init; } = ChannelView.WireLabel;
    public string SlotLabel { get; init; } = "UNKNOWN";
    public string SlotHint { get; init; } = "";
    public string PhoneReminder { get; init; } = ChannelView.PhoneReminder;
    public string ContractVersion { get; init; } = "";

    public static SnapshotView Empty(string reason)
    {
        var (slot, hint) = ChannelView.Slot(null, null, null);
        return new SnapshotView
        {
            RuntimeStatus = "Offline",
            Warning = reason,
            Client = [new DisplayField("Stato", "UNKNOWN", "UNKNOWN")],
            Guard =
            [
                new DisplayField("Wire (questo build)", ChannelView.WireLabel, "DERIVED"),
                new DisplayField("Slot", slot, "UNKNOWN"),
                new DisplayField("Sessione", "UNKNOWN", "UNKNOWN")
            ],
            Hardware = [new DisplayField("Piattaforma", "UNKNOWN", "UNKNOWN")],
            Safety = [new DisplayField("Esecuzione", "UNKNOWN", "UNKNOWN")],
            SlotLabel = slot,
            SlotHint = hint
        };
    }

    public static SnapshotView From(Gate1CanonicalSnapshot snapshot)
    {
        var (slot, hint) = ChannelView.Slot(
            ChannelView.Flag(snapshot.Guard.Connected),
            ChannelView.Flag(snapshot.Guard.Authenticated),
            ChannelView.Text(snapshot.Guard.TerminationReason));
        return new SnapshotView
        {
            RuntimeStatus = snapshot.RuntimeStatus.ToString(),
            Warning = snapshot.Warning ?? snapshot.Client.Warning ?? "",
            CapturedAt = snapshot.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss"),
            ContractVersion = snapshot.ContractVersion,
            SlotLabel = slot,
            SlotHint = hint,
        Client =
        [
            Field("Stato", snapshot.Client.Status, snapshot.Client.Attached),
            Field("Processo", snapshot.Client.ProcessName),
            Field("PID", snapshot.Client.ProcessId),
            Field("Finestra", snapshot.Client.WindowTitle),
            Field("Handle", snapshot.Client.WindowHandle),
            Field("Risponde", snapshot.Client.ProcessResponding),
            Field("Visibile", snapshot.Client.WindowVisible),
            Field("Gameplay", snapshot.Client.GameplayBaseline)
        ],
        Guard =
        [
            new DisplayField("Wire (questo build)", ChannelView.WireLabel, "DERIVED"),
            new DisplayField("Slot", slot, slot == "UNKNOWN" ? "UNKNOWN" : "DERIVED"),
            Field("Collegato", snapshot.Guard.Connected),
            Field("Autenticato", snapshot.Guard.Authenticated),
            Field("Sessione", snapshot.Guard.SessionId),
            Field("Heartbeat", snapshot.Guard.LastHeartbeatUtc),
            Field("Chiusura", snapshot.Guard.TerminationReason)
        ],
        Hardware =
        [
            Field("Piattaforma", snapshot.Hardware.Platform),
            Field("CPU", snapshot.Hardware.Cpu),
            Field("Core", snapshot.Hardware.LogicalCores),
            Field("RAM processo (MB)", snapshot.Hardware.ProcessWorkingSetMb),
            Field("RAM sistema (MB)", snapshot.Hardware.SystemRamMb),
            Field("GPU", snapshot.Hardware.Gpu),
            Field("VRAM (MB)", snapshot.Hardware.GpuMemoryMb),
            Field("Refresh (Hz)", snapshot.Hardware.DisplayRefreshHz),
            Field("OS", snapshot.Hardware.OsVersion)
        ],
        Safety =
        [
            Field("Input live", snapshot.Safety.LiveInputEnabled),
            Field("Iniezione pacchetti", snapshot.Safety.PacketInjectionEnabled),
            Field("Client sano richiesto", snapshot.Safety.RequireClientHealthy),
            Field("Guard richiesto", snapshot.Safety.RequireGuardApproval),
            Field("Esecuzione", snapshot.Safety.ExecutionMode)
        ]
        };
    }

    private static DisplayField Field<T>(string label, ClassifiedValue<T> classified)
    {
        var source = classified.Source.ToWire();
        if (!classified.HasValue)
        {
            var reason = classified.FailureReason;
            return new DisplayField(label, string.IsNullOrWhiteSpace(reason) ? "UNKNOWN" : $"UNKNOWN · {reason}", "UNKNOWN");
        }

        return new DisplayField(label, $"{classified.Value} [{source}]", source);
    }

    private static DisplayField Field<T>(string label, string raw, ClassifiedValue<T> provenance)
        => new(label, $"{raw} [{provenance.Source.ToWire()}]", provenance.Source.ToWire());
}
