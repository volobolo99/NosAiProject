// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Perception — Scope filter: the game client's traffic and nothing else
// ============================================================================
//
// This filter is the barrier that stops the channel becoming a general sniffer.
// There is no way to construct it as "match-all": without a real game endpoint
// it refuses construction (fail closed), and every packet that does not belong
// to that endpoint is discarded before it enters the pipeline.

using System;

namespace NosAi.Runtime.Perception.Network;

/// <summary>
/// Admits only packets belonging to the scoped game connection and drops
/// everything else. It cannot be constructed as a catch-all: a scope that does
/// not name a real host and port is refused, so "capture everything" has no
/// representation here.
/// </summary>
public sealed class ScopedGameTrafficFilter
{
    private readonly GameEndpoint _endpoint;
    private long _admittedCount;
    private long _droppedCount;

    /// <summary>Packets admitted because they belong to the game connection.</summary>
    public long AdmittedCount => System.Threading.Interlocked.Read(ref _admittedCount);

    /// <summary>Packets dropped because they belong to something other than the game.</summary>
    public long DroppedCount => System.Threading.Interlocked.Read(ref _droppedCount);

    public GameEndpoint Endpoint => _endpoint;

    public ScopedGameTrafficFilter(GameEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsSpecified)
            throw new ArgumentException(
                "A scoped capture target must name a real game host and port; a match-all scope is refused by design.",
                nameof(endpoint));
        _endpoint = endpoint;
    }

    /// <summary>
    /// True only when the packet belongs to the scoped game connection. Traffic
    /// from any other host or port — another application, another service — is
    /// not this channel's business and is dropped.
    /// </summary>
    public bool Admit(ObservedPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        bool belongsToGame =
            packet.RemotePort == _endpoint.Port &&
            string.Equals(packet.RemoteHost, _endpoint.Host, StringComparison.OrdinalIgnoreCase);

        if (belongsToGame) System.Threading.Interlocked.Increment(ref _admittedCount);
        else System.Threading.Interlocked.Increment(ref _droppedCount);
        return belongsToGame;
    }
}
