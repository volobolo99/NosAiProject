// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Percezione — Suite di certificazione dell'osservazione di rete
// ============================================================================
//
// I check dimostrano le due proprietà che rendono questo canale un osservatore
// del gioco e non uno sniffer generico: (1) il traffico di altre applicazioni
// viene scartato e non entra mai nella pipeline; (2) non esiste alcuna primitiva
// di invio/iniezione. Più la provenienza onesta (LIVE/SIMULATED/CACHED/UNKNOWN)
// e la convergenza nel World Model.

using System;
using System.Linq;
using System.Reflection;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception.Network;

public static class NetworkObservationTestRunner
{
    private static readonly GameEndpoint GameServer = new("nostale.gameforge.example", 4012);

    /// <summary>
    /// Runs every network-observation check and reports each one by name (same
    /// contract as the gate runners: no short-circuit, a throwing check is named).
    /// </summary>
    public static bool RunAll()
    {
        Console.WriteLine("=== Network observation checks ===");

        bool allPassed = true;
        allPassed &= Run("A match-all scope cannot be constructed", TestMatchAllScopeRefused);
        allPassed &= Run("Other applications' traffic is dropped, never decoded", TestOtherTrafficIsDropped);
        allPassed &= Run("The channel exposes no send/inject/modify surface", TestChannelIsObservationOnly);
        allPassed &= Run("No capture backend yields UNKNOWN, not an invented packet", TestUnavailableSourceIsHonest);
        allPassed &= Run("Synthetic packets stay SIMULATED end to end", TestSyntheticStaysSimulated);
        allPassed &= Run("A recording is CACHED, never promoted to LIVE", TestReplayIsCached);
        allPassed &= Run("The decoder refuses an unknown opcode instead of guessing", TestUnknownOpcodeYieldsNothing);
        allPassed &= Run("Decoded sightings converge into the world model", TestConvergesIntoWorldModel);
        allPassed &= Run("Combat and death events are decoded for tactics", TestTacticalEventsDecoded);
        allPassed &= Run("Scoped-out packets are counted, not silently ignored", TestScopedOutAreCounted);

        Console.WriteLine(allPassed
            ? "=== Network observation checks passed. Local only: no real NosTale capture backend is attached. ==="
            : "=== Network observation checks FAILED. See the lines marked FAIL above. ===");
        return allPassed;
    }

    private static bool Run(string name, Func<bool> check)
    {
        try { return Report(name, check(), null); }
        catch (Exception ex) { return Report(name, false, $"{ex.GetType().Name}: {ex.Message}"); }
    }

    private static bool Report(string name, bool passed, string? error)
    {
        string detail = error is null ? string.Empty : $" [{error}]";
        Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {name}{detail}");
        return passed;
    }

    private static ObservedPacket GamePacket(byte[] payload, DataSourceKind source = DataSourceKind.Simulated) => new(
        new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc), NetworkDirection.Inbound,
        GameServer.Host, GameServer.Port, payload, source);

    private static ObservedPacket ForeignPacket(string host, int port) => new(
        new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc), NetworkDirection.Inbound,
        host, port, new byte[] { 1, 2, 3 }, DataSourceKind.Simulated);

    // ------------------------------------------------------------------ scope

    private static bool TestMatchAllScopeRefused()
    {
        // A scope with no real endpoint would be a catch-all: it must be refused,
        // so "capture everything" has no way to exist.
        bool emptyRefused = false, zeroPortRefused = false, wildcardRefused = false;
        try { _ = new ScopedGameTrafficFilter(new GameEndpoint("", 0)); } catch (ArgumentException) { emptyRefused = true; }
        try { _ = new ScopedGameTrafficFilter(new GameEndpoint("host", 0)); } catch (ArgumentException) { zeroPortRefused = true; }
        try { _ = new ScopedGameTrafficFilter(new GameEndpoint("   ", 4012)); } catch (ArgumentException) { wildcardRefused = true; }
        return emptyRefused && zeroPortRefused && wildcardRefused;
    }

    private static bool TestOtherTrafficIsDropped()
    {
        var filter = new ScopedGameTrafficFilter(GameServer);
        // Browser to a CDN, mail over TLS, another game: none belong to this
        // channel and none may be admitted.
        bool cdnDropped = !filter.Admit(ForeignPacket("cdn.example.net", 443));
        bool mailDropped = !filter.Admit(ForeignPacket("imap.example.com", 993));
        bool wrongPortDropped = !filter.Admit(ForeignPacket(GameServer.Host, 443));
        bool gameAdmitted = filter.Admit(GamePacket(SyntheticProtocolDecoder.BuildFrame(SyntheticProtocolDecoder.OpSighting, 1, 10, 10, 80)));
        return cdnDropped && mailDropped && wrongPortDropped && gameAdmitted
            && filter.DroppedCount == 3 && filter.AdmittedCount == 1;
    }

    private static bool TestChannelIsObservationOnly()
    {
        // Reflection guard: no observation source may expose a way to touch the
        // wire. A future backend that could inject would be a different type,
        // gated like every other privileged action.
        Type[] sourceTypes = typeof(INetworkObservationSource).Assembly.GetTypes()
            .Where(t => typeof(INetworkObservationSource).IsAssignableFrom(t))
            .ToArray();

        foreach (Type type in sourceTypes)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                string name = method.Name.ToLowerInvariant();
                if (name.Contains("send") || name.Contains("inject") || name.Contains("write") ||
                    name.Contains("modify") || name.Contains("transmit"))
                    return false;
            }
        }
        return true;
    }

    // ------------------------------------------------------------------ provenance

    private static bool TestUnavailableSourceIsHonest()
    {
        var observer = new GameTrafficObserver(new UnavailableNetworkSource(),
            new ScopedGameTrafficFilter(GameServer), new SyntheticProtocolDecoder());
        NetworkObservationReport report = observer.ObservePending();
        return observer.Source == DataSourceKind.Unknown
            && report.Source == DataSourceKind.Unknown
            && report.ObservedPackets == 0
            && report.Sightings.IsEmpty
            && report.Events.IsEmpty;
    }

    private static bool TestSyntheticStaysSimulated()
    {
        var source = new SyntheticNetworkSource(new[]
        {
            GamePacket(SyntheticProtocolDecoder.BuildFrame(SyntheticProtocolDecoder.OpSighting, 5, 100, 200, 75),
                source: DataSourceKind.Live),   // even if asked for Live, the synthetic source downgrades it
        });
        var observer = new GameTrafficObserver(source, new ScopedGameTrafficFilter(GameServer), new SyntheticProtocolDecoder());
        NetworkObservationReport report = observer.ObservePending();
        return report.Source == DataSourceKind.Simulated
            && report.Sightings.Length == 1
            && report.Sightings[0].Source == DataSourceKind.Simulated;
    }

    private static bool TestReplayIsCached()
    {
        var source = new ReplayNetworkSource(new[]
        {
            GamePacket(SyntheticProtocolDecoder.BuildFrame(SyntheticProtocolDecoder.OpSighting, 7, 1, 2, 50),
                source: DataSourceKind.Live),
        });
        var observer = new GameTrafficObserver(source, new ScopedGameTrafficFilter(GameServer), new SyntheticProtocolDecoder());
        NetworkObservationReport report = observer.ObservePending();
        // A recording is never live: policy that demands LIVE must be able to tell.
        return report.Source == DataSourceKind.Cached
            && report.Sightings.Single().Source == DataSourceKind.Cached;
    }

    // ------------------------------------------------------------------ decoding

    private static bool TestUnknownOpcodeYieldsNothing()
    {
        var decoder = new SyntheticProtocolDecoder();
        var packet = GamePacket(SyntheticProtocolDecoder.BuildFrame(0xEE, 1, 1, 1, 1));
        DecodedObservations result = decoder.Decode(packet);
        // Wrong length is undecodable; unknown opcode decodes to nothing.
        bool badLength = !decoder.CanDecode(GamePacket(new byte[] { 1, 2, 3 }));
        return result.IsEmpty && badLength;
    }

    private static bool TestConvergesIntoWorldModel()
    {
        var source = new SyntheticNetworkSource(new[]
        {
            GamePacket(SyntheticProtocolDecoder.BuildFrame(SyntheticProtocolDecoder.OpSighting, 0, 0, 0, 60)),   // the player
            GamePacket(SyntheticProtocolDecoder.BuildFrame(SyntheticProtocolDecoder.OpSighting, 101, 30, 40, 90)),
            GamePacket(SyntheticProtocolDecoder.BuildFrame(SyntheticProtocolDecoder.OpSighting, 102, 50, 60, 25)),
        });
        var observer = new GameTrafficObserver(source, new ScopedGameTrafficFilter(GameServer), new SyntheticProtocolDecoder());
        NetworkObservationReport report = observer.ObservePending();
        NosAi.Runtime.WorldModel.WorldState world = observer.ToWorldState(report);

        var model = new NosAi.Runtime.WorldModel.WorldModel();
        model.Update(world);

        return model.Current.Entities.Count == 2
            && Math.Abs(model.Current.PlayerHpRatio - 0.60) < 1e-9
            && model.Current.PlayerAlive
            && model.Current.Entities.Any(e => e.Id == "Monster#102" && e.HpRatio < 0.3);
    }

    private static bool TestTacticalEventsDecoded()
    {
        var source = new SyntheticNetworkSource(new[]
        {
            GamePacket(SyntheticProtocolDecoder.BuildFrame(SyntheticProtocolDecoder.OpCombatHit, 101, 30, 40, 55)),
            GamePacket(SyntheticProtocolDecoder.BuildFrame(SyntheticProtocolDecoder.OpEntityDeath, 101, 30, 40, 0)),
        });
        var observer = new GameTrafficObserver(source, new ScopedGameTrafficFilter(GameServer), new SyntheticProtocolDecoder());
        NetworkObservationReport report = observer.ObservePending();
        // A hit updates the target's HP (useful for target selection); a death
        // removes it as a target. Both are what a combat AI plans on.
        return report.Events.Any(e => e.Kind == GameEventKind.CombatHit && e.EntityId == 101)
            && report.Events.Any(e => e.Kind == GameEventKind.EntityDeath && e.EntityId == 101)
            && report.Sightings.Any(s => s.EntityId == 101);
    }

    private static bool TestScopedOutAreCounted()
    {
        // A mixed stream: two game packets and three from elsewhere. Only the game
        // ones are decoded; the rest are counted as scoped-out, not hidden.
        var source = new SyntheticNetworkSource(new[]
        {
            GamePacket(SyntheticProtocolDecoder.BuildFrame(SyntheticProtocolDecoder.OpSighting, 1, 1, 1, 80)),
            ForeignPacket("cdn.example.net", 443),
            ForeignPacket("imap.example.com", 993),
            GamePacket(SyntheticProtocolDecoder.BuildFrame(SyntheticProtocolDecoder.OpSighting, 2, 2, 2, 70)),
            ForeignPacket("telemetry.example.io", 8443),
        });
        var observer = new GameTrafficObserver(source, new ScopedGameTrafficFilter(GameServer), new SyntheticProtocolDecoder());
        NetworkObservationReport report = observer.ObservePending();
        return report.ObservedPackets == 5
            && report.ScopedOutPackets == 3
            && report.DecodedPackets == 2
            && report.Sightings.Length == 2;
    }
}
