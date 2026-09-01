using System.Collections.Immutable;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The world state the network channel produces, and the one it refuses to.
/// </summary>
/// <remarks>
/// <see cref="NosAi.Runtime.WorldModel.WorldState"/> carries no provenance: once a
/// value is in it, nothing downstream can tell an observation from a default. So
/// the refusal has to happen at the last place that still knows the difference,
/// which is the fold from the report into the state.
/// </remarks>
public sealed class NetworkWorldStateTests
{
    private static readonly GameEndpoint Server = new("79.110.84.175", 4006);

    private static NetworkObservationReport Report(
        DataSourceKind source, params EntitySighting[] sightings) =>
        new(
            Frame: 1,
            Sightings: sightings.ToImmutableArray(),
            Events: ImmutableArray<GameEvent>.Empty,
            ObservedPackets: sightings.Length,
            ScopedOutPackets: 0,
            DecodedPackets: sightings.Length,
            UndecodablePackets: 0,
            Source: source);

    private static GameTrafficObserver Observer() => new(
        new UnavailableNetworkSource(),
        new ScopedGameTrafficFilter(Server),
        new SyntheticProtocolDecoder());

    [Fact]
    public void The_player_sighting_becomes_the_players_state()
    {
        NetworkObservationReport report = Report(
            DataSourceKind.Live,
            new EntitySighting(0, "Player", 10, 20, 0.6, DataSourceKind.Live),
            new EntitySighting(101, "Monster", 30, 40, 0.9, DataSourceKind.Live));

        Assert.True(Observer().TryToWorldState(report, out var world, out string? reason));

        Assert.Null(reason);
        Assert.Equal(0.6, world.PlayerHpRatio, 9);
        Assert.True(world.PlayerAlive);
        Assert.Equal("Monster#101", Assert.Single(world.Entities).Id);
    }

    /// <summary>
    /// The defect this replaced. With no player sighting the fold used to return a
    /// state anyway, defaulted to full health and alive — the two values a policy
    /// is least likely to intervene on. A channel that had decoded nothing
    /// produced the world state most likely to be acted upon.
    /// </summary>
    [Fact]
    public void A_report_without_the_player_yields_no_state_at_all()
    {
        NetworkObservationReport report = Report(
            DataSourceKind.Live,
            new EntitySighting(101, "Monster", 30, 40, 0.9, DataSourceKind.Live));

        Assert.False(Observer().TryToWorldState(report, out _, out string? reason));
        Assert.Equal("player_not_sighted", reason);
    }

    [Fact]
    public void An_empty_report_yields_no_state_at_all()
    {
        Assert.False(Observer().TryToWorldState(Report(DataSourceKind.Live), out _, out string? reason));
        Assert.Equal("player_not_sighted", reason);
    }

    /// <summary>
    /// No source at all is a different reason from a source that saw no player,
    /// and the operator needs the difference: one is a channel that is not
    /// attached, the other is a channel that is attached and finding nothing.
    /// </summary>
    [Fact]
    public void No_observation_at_all_says_so_rather_than_blaming_the_player()
    {
        Assert.False(Observer().TryToWorldState(Report(DataSourceKind.Unknown), out _, out string? reason));
        Assert.Equal("no_network_observation", reason);
    }

    /// <summary>
    /// A sighting with no health must never become a detection at zero. Zero is
    /// the reading a world model treats as a dead mob, and it is the value a
    /// sentinel or a default would have produced here.
    /// </summary>
    [Fact]
    public void A_sighting_without_health_projects_to_no_detection_at_all()
    {
        var seen = new EntitySighting(101, "Monster", 30, 40, null, DataSourceKind.Live);

        Assert.Null(seen.ToDetection());
    }

    /// <summary>The source that always has health keeps producing one every time.</summary>
    [Fact]
    public void A_sighting_with_health_still_projects_to_a_detection()
    {
        var seen = new EntitySighting(101, "Monster", 30, 40, 0.9, DataSourceKind.Live);

        Detection? projected = seen.ToDetection();

        Assert.True(projected.HasValue);
        Detection detection = projected!.Value;
        Assert.Equal("Monster", detection.Kind);
        Assert.Equal(30, detection.X);
        Assert.Equal(40, detection.Y);
        Assert.Equal(0.9, detection.HpRatio, 9);
    }

    /// <summary>
    /// The whole point of the change: the entity reaches the world model on the
    /// strength of its position, and its health arrives as unknown rather than as
    /// a zero nothing downstream could tell from an observation.
    /// </summary>
    [Fact]
    public void An_entity_seen_without_health_keeps_its_position_in_the_world_state()
    {
        NetworkObservationReport report = Report(
            DataSourceKind.Live,
            new EntitySighting(0, "Player", 10, 20, 0.6, DataSourceKind.Live),
            new EntitySighting(101, "Monster", 30, 40, null, DataSourceKind.Live));

        Assert.True(Observer().TryToWorldState(report, out var world, out _));

        NosAi.Runtime.WorldModel.EntityState entity = Assert.Single(world.Entities);
        Assert.Equal("Monster#101", entity.Id);
        Assert.Equal(30, entity.X);
        Assert.Equal(40, entity.Y);
        Assert.Null(entity.HpRatio);
    }

    /// <summary>
    /// A player sighted without health is not a player at zero health, and not a
    /// player who was never sighted either: the fold refuses, as it does for any
    /// health it cannot read.
    /// </summary>
    [Fact]
    public void A_player_sighted_without_health_yields_no_state_at_all()
    {
        NetworkObservationReport report = Report(
            DataSourceKind.Live,
            new EntitySighting(0, "Player", 10, 20, null, DataSourceKind.Live));

        Assert.False(Observer().TryToWorldState(report, out _, out string? reason));
        Assert.Equal("player_not_sighted", reason);
    }

    [Fact]
    public void A_dead_player_is_reported_dead_not_refused()
    {
        NetworkObservationReport report = Report(
            DataSourceKind.Live,
            new EntitySighting(0, "Player", 10, 20, 0.0, DataSourceKind.Live));

        Assert.True(Observer().TryToWorldState(report, out var world, out _));

        Assert.False(world.PlayerAlive);
        Assert.Equal(0.0, world.PlayerHpRatio, 9);
    }

    /// <summary>The feed refuses for the same reason the observer does.</summary>
    [Fact]
    public void The_feed_refuses_when_nothing_has_been_observed()
    {
        var feed = new NetworkWorldFeed(Observer());

        Assert.False(feed.TryToWorldState(out _, out string? reason));
        Assert.Equal("no_network_observation", reason);
    }

    /// <summary>
    /// And the decision context reports the facts as UNKNOWN with a reason rather
    /// than omitting them: a rule that needs an unobserved fact can only be
    /// skipped if the fact is present as unknown instead of missing by accident.
    /// </summary>
    [Fact]
    public void The_decision_context_carries_unknown_facts_rather_than_dropping_them()
    {
        var feed = new NetworkWorldFeed(Observer());

        var context = feed.ToDecisionContext();

        Assert.Contains("player.hp_ratio", context.FactNames);
        Assert.False(context.TryRead("player.hp_ratio", out _, out DataSourceKind source));
        Assert.Equal(DataSourceKind.Unknown, source);
    }
}
