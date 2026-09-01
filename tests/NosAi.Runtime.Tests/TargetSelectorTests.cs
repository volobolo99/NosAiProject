using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Which entity the runtime aims at, and every case where it aims at none.
/// </summary>
/// <remarks>
/// The step that was missing between "there is a target" and an aimed click.
/// Before it, every entity candidate carried
/// <see cref="ActionTarget.Entity.Unidentified"/> and the effector refused all of
/// them, so the loop could attack a target somebody else had selected and could
/// never select one itself.
/// </remarks>
public sealed class TargetSelectorTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly MapPoint Standing = new(100, 100);

    private static ClassifiedValue<MapPoint> At(MapPoint point)
        => ClassifiedValue<MapPoint>.Live(point, Now);

    private static SelectableEntity Entity(
        long id, int x, int y, double? hp = 1.0, int secondsAgo = 0)
        => new(id, new MapPoint(x, y), hp, Now.AddSeconds(-secondsAgo));

    private static bool Select(
        IReadOnlyList<SelectableEntity> observed,
        out TargetChoice? choice,
        out string reason,
        ClassifiedValue<MapPoint>? player = null,
        TargetSelectionPolicy? policy = null)
        => TargetSelector.TrySelect(
            observed,
            player ?? At(Standing),
            Now,
            policy ?? TargetSelectionPolicy.Default,
            out choice,
            out reason);

    [Fact]
    public void The_nearest_entity_is_chosen()
    {
        SelectableEntity[] observed =
        [
            Entity(11, 108, 100),
            Entity(22, 103, 100),
            Entity(33, 100, 106),
        ];

        Assert.True(Select(observed, out TargetChoice? choice, out string reason), reason);

        Assert.Equal(22, choice!.Entity.EntityId);
        Assert.Equal(3.0, choice.DistanceTiles, 6);
    }

    /// <summary>
    /// A planner that alternates between two equidistant monsters on successive
    /// cycles commits to neither and fights nothing, so the tie has to break on
    /// something stable rather than on the order the sightings arrived in.
    /// </summary>
    [Fact]
    public void A_tie_breaks_the_same_way_whatever_order_the_sightings_arrive_in()
    {
        SelectableEntity[] forwards = [Entity(77, 104, 100), Entity(55, 96, 100)];
        SelectableEntity[] backwards = [Entity(55, 96, 100), Entity(77, 104, 100)];

        Assert.True(Select(forwards, out TargetChoice? first, out _));
        Assert.True(Select(backwards, out TargetChoice? second, out _));

        Assert.Equal(55, first!.Entity.EntityId);
        Assert.Equal(55, second!.Entity.EntityId);
    }

    /// <summary>
    /// Unknown is not zero. Most sightings are moves, which carry no health at
    /// all, and excluding them would leave almost nothing to aim at.
    /// </summary>
    [Fact]
    public void An_entity_of_unknown_health_is_still_selectable()
    {
        SelectableEntity[] observed = [Entity(11, 102, 100, hp: null)];

        Assert.True(Select(observed, out TargetChoice? choice, out string reason), reason);

        Assert.Equal(11, choice!.Entity.EntityId);
        Assert.Contains("non nota", choice.Rationale, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dead_entity_is_not_a_target()
    {
        SelectableEntity[] observed = [Entity(11, 102, 100, hp: 0.0)];

        Assert.False(Select(observed, out TargetChoice? choice, out string reason));

        Assert.Null(choice);
        Assert.Equal("all_observed_entities_dead:1", reason);
    }

    /// <summary>
    /// A live one behind a dead one is still chosen: the dead entity is excluded,
    /// not the whole list.
    /// </summary>
    [Fact]
    public void A_dead_entity_does_not_hide_a_live_one_further_away()
    {
        SelectableEntity[] observed = [Entity(11, 101, 100, hp: 0.0), Entity(22, 105, 100)];

        Assert.True(Select(observed, out TargetChoice? choice, out _));

        Assert.Equal(22, choice!.Entity.EntityId);
    }

    /// <summary>
    /// Past the range the client is not drawing it, the projection puts it outside
    /// the client area and the click is refused — so the reason says how far the
    /// nearest one actually is, which is what tells the operator to walk.
    /// </summary>
    [Fact]
    public void An_entity_out_of_range_is_refused_with_the_distance_that_would_reach_it()
    {
        SelectableEntity[] observed = [Entity(11, 140, 100)];

        Assert.False(Select(observed, out _, out string reason));

        Assert.Equal("no_entity_in_range:40.0_of_12_tiles", reason);
    }

    /// <summary>
    /// The click lands on a square, and a square a monster walked off is empty
    /// ground. An old position is not a position.
    /// </summary>
    [Fact]
    public void A_sighting_older_than_the_bound_is_refused_and_says_how_many()
    {
        SelectableEntity[] observed =
        [
            Entity(11, 102, 100, secondsAgo: 120),
            Entity(22, 103, 100, secondsAgo: 90),
        ];

        Assert.False(Select(observed, out _, out string reason));

        Assert.Equal("all_sightings_stale:2", reason);
    }

    /// <summary>
    /// Generously, though: a monster that has stood still has not been mentioned
    /// on the wire for as long as it has stood there.
    /// </summary>
    [Fact]
    public void A_sighting_within_the_bound_is_still_usable()
    {
        SelectableEntity[] observed = [Entity(11, 102, 100, secondsAgo: 20)];

        Assert.True(Select(observed, out TargetChoice? choice, out string reason), reason);

        Assert.Equal(11, choice!.Entity.EntityId);
    }

    [Fact]
    public void An_empty_map_is_reported_as_such_and_not_as_a_failure()
    {
        Assert.False(Select([], out TargetChoice? choice, out string reason));

        Assert.Null(choice);
        Assert.Equal(TargetSelector.NothingObservedReason, reason);
    }

    /// <summary>
    /// Without the character's own square nothing has a distance, and treating an
    /// unknown position as the map origin would make the farthest entity look like
    /// the nearest. The reason carries why the position is unknown, because a
    /// client at the login screen and a broken pointer chain need different
    /// answers.
    /// </summary>
    [Fact]
    public void An_unknown_character_position_refuses_and_carries_why()
    {
        SelectableEntity[] observed = [Entity(11, 102, 100)];

        Assert.False(Select(
            observed, out TargetChoice? choice, out string reason,
            player: ClassifiedValue<MapPoint>.Unknown("player_manager_null")));

        Assert.Null(choice);
        Assert.Equal($"{TargetSelector.PlayerPositionUnknownReason}:player_manager_null", reason);
    }

    /// <summary>The chosen entity is what an aimed click needs: an id and a square.</summary>
    [Fact]
    public void The_choice_carries_the_square_to_click()
    {
        SelectableEntity[] observed = [Entity(42, 104, 97)];

        Assert.True(Select(observed, out TargetChoice? choice, out _));

        Assert.Equal(new MapPoint(104, 97), choice!.Entity.At);
        Assert.Equal(42, choice.Entity.EntityId);
    }

    [Fact]
    public void A_wider_range_reaches_what_the_default_refuses()
    {
        SelectableEntity[] observed = [Entity(11, 120, 100)];

        Assert.False(Select(observed, out _, out _));
        Assert.True(Select(
            observed, out TargetChoice? choice, out string reason,
            policy: new TargetSelectionPolicy(MaxRangeTiles: 30.0)), reason);

        Assert.Equal(11, choice!.Entity.EntityId);
    }
}
