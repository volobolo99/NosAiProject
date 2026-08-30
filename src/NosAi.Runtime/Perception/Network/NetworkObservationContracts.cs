// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Percezione — Osservazione di rete: contratti del canale di traffico di gioco
// ============================================================================
//
// Questo canale OSSERVA il traffico del client NosTale controllato dal runtime e
// nient'altro. Non è un intercettore generico: per costruzione è vincolato
// all'endpoint del gioco (ScopedGameTrafficFilter), non decifra TLS, non tocca
// traffico di altre applicazioni e non inietta né modifica pacchetti. È la stessa
// linea del boundary read-only già dichiarato da IPacketManipulator e del divieto
// di automazione evasiva del progetto (docs/PERSISTENZA_SQLITE_E_SHARED_MEMORY.md).
//
// Ogni osservazione dichiara la propria provenienza: cattura reale scoped = LIVE,
// sorgente sintetica = SIMULATED, nessun backend = UNKNOWN. Mai pixel — qui, byte
// — inventati (ADR-0002).

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
