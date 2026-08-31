// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Perception — Network observation: contracts of the game traffic channel
// ============================================================================
//
// This channel OBSERVES the traffic of the NosTale client the runtime controls,
// and nothing else. It is not a general interceptor: by construction it is bound
// to the game's endpoint (ScopedGameTrafficFilter), does not decrypt TLS, does
// not touch other applications' traffic, and neither injects nor modifies
// packets. That is the same line as the read-only boundary IPacketManipulator
// already declares, and as the project's ban on evasive automation
// (docs/PERSISTENZA_SQLITE_E_SHARED_MEMORY.md).
//
// Every observation declares its own provenance: real scoped capture = LIVE,
// synthetic source = SIMULATED, no backend = UNKNOWN. Never invented pixels —
// here, bytes (ADR-0002).

using System;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception.Network;

/// <summary>Direction of an observed packet relative to the controlled client.</summary>
public enum NetworkDirection : byte
{
    Inbound = 0,
    Outbound = 1,
}

/// <summary>
/// The single game connection this channel is scoped to. A source that does not
/// name a real host and port cannot be used to capture: the scope is the whole
/// point, so an unspecified endpoint matches nothing.
/// </summary>
public sealed record GameEndpoint(string Host, int Port)
{
    public bool IsSpecified => !string.IsNullOrWhiteSpace(Host) && Port is > 0 and <= 65535;
}

/// <summary>
/// One observed packet on the game connection. The payload is whatever crossed
/// the wire; <see cref="Source"/> says whether it was really captured (LIVE),
/// produced synthetically (SIMULATED), or replayed from a recording (CACHED).
/// </summary>
public sealed record ObservedPacket(
    DateTime CapturedUtc,
    NetworkDirection Direction,
    string RemoteHost,
    int RemotePort,
    ReadOnlyMemory<byte> Payload,
    DataSourceKind Source);

/// <summary>
/// A source of observed packets.
/// </summary>
/// <remarks>
/// Deliberately observation-only: there is no member that sends, injects, writes
/// or modifies. The channel reads the game's own traffic to build the world
/// model; it never actuates the wire. A backend that could inject would be a
/// different interface, gated like every other privileged action.
/// </remarks>
public interface INetworkObservationSource
{
    /// <summary>Provenance of packets from this source.</summary>
    DataSourceKind Source { get; }

    /// <summary>Tries to observe the next packet; false when none is available.</summary>
    bool TryObserve(out ObservedPacket packet);
}
