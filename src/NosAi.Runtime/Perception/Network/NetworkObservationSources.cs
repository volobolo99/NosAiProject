// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Perception — Network observation sources (synthetic, replay, absent)
// ============================================================================
//
// The real scoped capture backend lives behind IRawScopedCaptureBackend: it must
// filter at the OS/pcap level on the game's endpoint alone and must NEVER be
// promiscuous. It is not implemented here because it is real-environment work
// (it needs a capture library and the client's live endpoint), exactly as the
// real DXGI backend is separate from the perception pipeline. With no backend
// the source is UnavailableNetworkSource: no invented bytes.

using System;
using System.Collections.Generic;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception.Network;

/// <summary>
/// Deterministic synthetic packet source for tests and offline pipelines. Every
/// packet is explicitly SIMULATED, so it can never be mistaken for real capture.
/// </summary>
public sealed class SyntheticNetworkSource : INetworkObservationSource
{
    private readonly Queue<ObservedPacket> _packets;

    public DataSourceKind Source => DataSourceKind.Simulated;

    public SyntheticNetworkSource(IEnumerable<ObservedPacket> packets)
    {
        ArgumentNullException.ThrowIfNull(packets);
        _packets = new Queue<ObservedPacket>();
        foreach (ObservedPacket packet in packets)
        {
            // A synthetic source may only emit SIMULATED packets: it must not be
            // able to mint a LIVE-looking observation.
            _packets.Enqueue(packet with { Source = DataSourceKind.Simulated });
        }
    }

    public bool TryObserve(out ObservedPacket packet)
    {
        if (_packets.Count > 0)
        {
            packet = _packets.Dequeue();
            return true;
        }
        packet = null!;
        return false;
    }
}

/// <summary>
/// Replays packets recorded earlier. A recording is not a live observation, so
/// every replayed packet is downgraded to CACHED regardless of how it was
/// captured: policy that requires LIVE data will refuse it.
/// </summary>
public sealed class ReplayNetworkSource : INetworkObservationSource
{
    private readonly Queue<ObservedPacket> _packets;

    public DataSourceKind Source => DataSourceKind.Cached;

    public ReplayNetworkSource(IEnumerable<ObservedPacket> recorded)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        _packets = new Queue<ObservedPacket>();
        foreach (ObservedPacket packet in recorded)
            _packets.Enqueue(packet with { Source = DataSourceKind.Cached });
    }

    public bool TryObserve(out ObservedPacket packet)
    {
        if (_packets.Count > 0)
        {
            packet = _packets.Dequeue();
            return true;
        }
        packet = null!;
        return false;
    }
}

/// <summary>
/// The source used when no real capture backend is attached. It never yields a
/// packet and never fabricates one: consumers see "no observation", classified
/// UNKNOWN, rather than an invented conversation.
/// </summary>
public sealed class UnavailableNetworkSource : INetworkObservationSource
{
    public DataSourceKind Source => DataSourceKind.Unknown;

    public bool TryObserve(out ObservedPacket packet)
    {
        packet = null!;
        return false;
    }
}

/// <summary>
/// Integration seam for a real scoped capture backend.
/// </summary>
/// <remarks>
/// <para>
/// A conforming implementation MUST deliver only packets of the scoped game
/// connection — filtered at the OS or capture-library level — and MUST NOT open a
/// promiscuous capture of the whole interface. Capturing everything and filtering
/// afterwards would pull unrelated applications' traffic into the process, which
/// this channel exists specifically not to do.
/// </para>
/// <para>
/// It also observes only: no send, inject or modify. A backend that needs those
/// is not this interface.
/// </para>
/// <para>
/// No implementation ships in 1.0 Beta: a real scoped capture needs a capture
/// library and the client's live endpoint, and is real-environment work like the
/// DXGI backend's real-desktop validation.
/// </para>
/// </remarks>
public interface IRawScopedCaptureBackend : INetworkObservationSource, IDisposable
{
    /// <summary>The game connection this backend is bound to.</summary>
    GameEndpoint Endpoint { get; }

    /// <summary>False (with a named reason) when the backend could not bind.</summary>
    bool IsCapturing { get; }
}
