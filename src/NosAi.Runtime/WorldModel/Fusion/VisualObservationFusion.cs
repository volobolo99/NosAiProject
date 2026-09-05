using NosAi.Core.WorldModel;
using NosAi.Runtime.Perception;

namespace NosAi.Runtime.WorldModel.Fusion;

/// <summary>
/// Combines a <see cref="WorldModelSnapshot"/> already projected from the
/// network channel (<see cref="GameplayObservationProjector"/>) with a
/// <see cref="VisualObservation"/> from the vision channel, resolving any
/// fact both channels report through <see cref="FactFusion"/> (AP-02/A3:
/// docs/agents/AGENT_COMMAND_REGISTRY.md "multimodal fusion, confidence and
/// contradiction handling").
/// </summary>
/// <remarks>
/// <para>
/// <b>Scoped to vitals only, today.</b> HP and MP are the one fact both
/// channels can genuinely report today: the network via
/// <c>GameplayObservation.Hp</c>/<c>MaxHp</c>/<c>Mp</c>/<c>MaxMp</c>, the
/// screen via <see cref="ScreenVitalReader"/>'s numeric OCR reading (once a
/// glyph atlas is trained -- see <see cref="ScreenVitalObservation.Hp"/>/<c>Mp</c>).
/// This is exactly the scenario <see cref="FactFusion"/>'s own XML doc
/// anticipated when it shipped in AP-01/A2 ("a future AP-02 screen/OCR
/// reading of HP"): two live channels reporting the same fact, resolved by
/// one deterministic precedence rule instead of each caller inventing its
/// own.
/// </para>
/// <para>
/// <b>Deliberately not scoped to entities.</b> <see cref="TrackedEntity.Kind"/>
/// is a free-text string with no established taxonomy anywhere in this
/// repository -- no trained ONNX decoder exists yet to assign it a
/// meaningful value (<c>EmptyOnnxDetectionDecoder</c> is the only decoder
/// implementation, and it never runs against a real model). Inventing a
/// "mob"/"npc" string convention here, with nothing upstream that could
/// ever produce those exact strings, would be exactly the kind of
/// speculative contract CLAUDE.md's "don't design for hypothetical future
/// requirements" rules out. Bridging <see cref="PerceptionResult.Entities"/>
/// into <see cref="WorldModelSnapshot.Mobs"/>/<see cref="WorldModelSnapshot.Npcs"/>
/// is left for whichever future task first ships a real trained decoder
/// with a documented output vocabulary to map from.
/// </para>
/// <para>
/// Pure and stateless, like <see cref="GameplayObservationProjector"/> and
/// <see cref="WorldModelTemporalEnricher"/>: the same snapshot and visual
/// observation always fuse to the same result.
/// </para>
/// </remarks>
public static class VisualObservationFusion
{
    /// <summary>How old either channel's vitals reading may be and still compete for the fused result.</summary>
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromSeconds(5);

    /// <param name="snapshot">A snapshot already projected from the network channel.</param>
    /// <param name="visual">This cycle's vision-channel reading.</param>
    /// <param name="nowUtc">The instant fusion runs at, used to judge freshness.</param>
    /// <param name="maxAge">How old a candidate may be and still compete. Defaults to <see cref="DefaultMaxAge"/>.</param>
    public static WorldModelSnapshot FuseVitals(WorldModelSnapshot snapshot, VisualObservation visual, DateTime nowUtc, TimeSpan? maxAge = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(visual);
        TimeSpan window = maxAge ?? DefaultMaxAge;

        Resource? networkHealth = FindResource(snapshot.Player.Status.Resources, ResourceKind.Health);
        Resource? networkMana = FindResource(snapshot.Player.Status.Resources, ResourceKind.Mana);

        Resource fusedHealth = FuseResource(
            ResourceKind.Health,
            networkHealth,
            visual.Vitals.Hp,
            "network-hp", "screen-hp",
            nowUtc, window);
        Resource fusedMana = FuseResource(
            ResourceKind.Mana,
            networkMana,
            visual.Vitals.Mp,
            "network-mp", "screen-mp",
            nowUtc, window);

        List<Resource> resources = new(snapshot.Player.Status.Resources.Count);
        foreach (Resource resource in snapshot.Player.Status.Resources)
        {
            if (resource.Kind == ResourceKind.Health) resources.Add(fusedHealth);
            else if (resource.Kind == ResourceKind.Mana) resources.Add(fusedMana);
            else resources.Add(resource);
        }
        if (networkHealth is null) resources.Add(fusedHealth);
        if (networkMana is null) resources.Add(fusedMana);

        Player fusedPlayer = snapshot.Player with
        {
            Status = snapshot.Player.Status with { Resources = EquatableArray<Resource>.From(resources) }
        };

        return snapshot with { Player = fusedPlayer };
    }

    private static Resource? FindResource(EquatableArray<Resource> resources, ResourceKind kind)
    {
        foreach (Resource resource in resources)
        {
            if (resource.Kind == kind) return resource;
        }
        return null;
    }

    private static Resource FuseResource(
        ResourceKind kind,
        Resource? network,
        ScreenVitalPair screen,
        string networkChannel,
        string screenChannel,
        DateTime nowUtc,
        TimeSpan maxAge)
    {
        WorldFact<double> networkCurrent = network?.Current ?? WorldFact<double>.Unknown("no_network_reading");
        WorldFact<double> networkMaximum = network?.Maximum ?? WorldFact<double>.Unknown("no_network_reading");
        WorldFact<double> screenCurrent = ClassifiedValueBridge.ToWorldFact(screen.Current, static v => (double)v);
        WorldFact<double> screenMaximum = ClassifiedValueBridge.ToWorldFact(screen.Maximum, static v => (double)v);

        FusionOutcome<double> current = FactFusion.Resolve(
            new[]
            {
                new FusionCandidate<double>(networkCurrent, networkChannel),
                new FusionCandidate<double>(screenCurrent, screenChannel)
            },
            nowUtc, maxAge);
        FusionOutcome<double> maximum = FactFusion.Resolve(
            new[]
            {
                new FusionCandidate<double>(networkMaximum, networkChannel),
                new FusionCandidate<double>(screenMaximum, screenChannel)
            },
            nowUtc, maxAge);

        return new Resource(kind, current.Result, maximum.Result);
    }
}
