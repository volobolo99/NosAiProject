namespace NosAi.Core.WorldModel.Reconstruction;

/// <summary>
/// One map-id/position reading, as already produced by the same live
/// sources <c>ScoutCommand</c>/<c>WalkCommand</c> already poll each cycle
/// (<c>ClientMemorySession.TryReadMapId</c>/<c>TryReadPlayer</c>) -- this
/// type adds no new observation channel, only names the pair
/// <see cref="PortalCrossingDetector"/> needs.
/// </summary>
public readonly record struct MapPositionReading(MapId MapId, WorldPosition Position, DateTime ObservedAtUtc);

/// <summary>
/// Detects a map transition between two consecutive live readings and, when
/// one happened, derives the <see cref="Portal"/> fact it implies -- the
/// "observe while crossing" source
/// docs/agents/DEEPSEEK_TASKS.md's "Candidati da investigare" identified as
/// the one buildable path to real portal data (no client file or table
/// names a portal's destination anywhere in this repository; a hardcoded
/// routing graph is all <c>WorldMapPortalRouter</c> has today). Pure and
/// stateless like every other planner in this project
/// (<see cref="Exploration.ExplorationPlanner"/>,
/// <see cref="WorldActionProjector"/>): the caller owns remembering the
/// previous reading across polls, this type only judges one pair of them.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this observes, and what it does not.</b> A map-id change between
/// two readings means the player crossed *something* that moves between
/// maps -- it does not confirm the mechanism was a portal object rather
/// than, say, a scripted teleport, and it never claims to. The derived
/// <see cref="Portal.SourcePosition"/> is the last position observed on the
/// source map before the change, not a confirmed portal tile: the true
/// crossing point lies somewhere between that reading and the next poll,
/// and this is the closest honest estimate a polling observer can produce
/// without instrumenting the crossing itself.
/// </para>
/// <para>
/// <b>Why <see cref="Portal.Id"/> is derived from a rounded position.</b>
/// <c>MapReconstructionFusion.MergeByKey</c> merges incoming
/// <see cref="Portal"/> facts by <see cref="Portal.Id"/> -- an id that
/// changed on every crossing of the same physical portal (float position
/// jitter between polls) would accumulate one row per crossing instead of
/// refining one. Rounding to <see cref="PositionRoundingUnits"/> world
/// units trades a small, documented loss of position precision for stable
/// identity across repeated crossings of the same portal. This is a
/// judgment call, not a measured constant -- tune
/// <see cref="PositionRoundingUnits"/> if real client crossings show it is
/// too coarse (merging two distinct nearby portals) or too fine (splitting
/// one portal into several ids).
/// </para>
/// </remarks>
public static class PortalCrossingDetector
{
    /// <summary>
    /// World-position rounding granularity used to build a stable
    /// <see cref="Portal.Id"/> for repeated crossings of the same portal.
    /// See the class remarks for why this is a judgment call, not a
    /// calibrated constant.
    /// </summary>
    public const double PositionRoundingUnits = 1.0;

    /// <summary>
    /// Judges whether <paramref name="current"/> shows the player on a
    /// different map than <paramref name="previous"/> and, if so, returns
    /// the <see cref="Portal"/> fact that crossing implies. Returns
    /// <see langword="null"/> when both readings share the same
    /// <see cref="MapPositionReading.MapId"/> -- no crossing happened, not
    /// an unobserved one.
    /// </summary>
    /// <param name="previous">The most recent reading before this one, on whichever map the player was on then.</param>
    /// <param name="current">This cycle's reading.</param>
    /// <param name="confidence">
    /// Confidence recorded on every fact of the derived <see cref="Portal"/>.
    /// Defaults to <c>1.0</c>: both readings are themselves live, direct
    /// memory reads (the same confidence <c>ScoutCommand</c> already
    /// assigns its own live position facts) -- a caller polling at a wider
    /// interval, where the true crossing point is less certain, may pass a
    /// lower value.
    /// </param>
    public static Portal? DetectCrossing(MapPositionReading previous, MapPositionReading current, double confidence = 1.0)
    {
        if (previous.MapId == current.MapId)
            return null;

        var id = new PortalId(string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"observed:{previous.MapId.Value}:{Round(previous.Position.X)}:{Round(previous.Position.Y)}"));

        return new Portal(
            id,
            previous.MapId,
            WorldFact<WorldPosition>.Live(previous.Position, confidence, previous.ObservedAtUtc),
            WorldFact<MapId>.Live(current.MapId, confidence, current.ObservedAtUtc),
            WorldFact<bool>.Live(true, confidence, current.ObservedAtUtc));
    }

    private static double Round(float value) =>
        Math.Round(value / PositionRoundingUnits, MidpointRounding.AwayFromZero) * PositionRoundingUnits;
}
