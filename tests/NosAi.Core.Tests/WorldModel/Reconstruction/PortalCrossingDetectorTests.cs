using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Reconstruction;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Reconstruction;

public sealed class PortalCrossingDetectorTests
{
    private static readonly DateTime T0 = new(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = T0.AddSeconds(2);

    private static MapPositionReading Reading(string mapId, float x, float y, DateTime at) =>
        new(new MapId(mapId), new WorldPosition(x, y), at);

    [Fact]
    public void DetectCrossing_SameMapId_ReturnsNull_NoCrossingHappened()
    {
        MapPositionReading previous = Reading("map-1", 10, 10, T0);
        MapPositionReading current = Reading("map-1", 12, 10, T1);

        Assert.Null(PortalCrossingDetector.DetectCrossing(previous, current));
    }

    [Fact]
    public void DetectCrossing_DifferentMapId_ReturnsAPortal_WithSourceAndDestinationFactsFromTheRightReading()
    {
        MapPositionReading previous = Reading("map-1", 10, 20, T0);
        MapPositionReading current = Reading("map-2", 3, 4, T1);

        Portal? portal = PortalCrossingDetector.DetectCrossing(previous, current);

        Assert.NotNull(portal);
        Assert.Equal(new MapId("map-1"), portal!.SourceMap);
        Assert.True(portal.SourcePosition.HasValue);
        Assert.Equal(new WorldPosition(10, 20), portal.SourcePosition.Value);
        Assert.Equal(T0, portal.SourcePosition.ObservedAtUtc);
        Assert.True(portal.DestinationMap.HasValue);
        Assert.Equal(new MapId("map-2"), portal.DestinationMap.Value);
        Assert.Equal(T1, portal.DestinationMap.ObservedAtUtc);
        Assert.True(portal.IsActive.HasValue);
        Assert.True(portal.IsActive.Value);
    }

    [Fact]
    public void DetectCrossing_DefaultConfidence_IsOneOnEveryFact()
    {
        Portal portal = PortalCrossingDetector.DetectCrossing(
            Reading("map-1", 0, 0, T0), Reading("map-2", 0, 0, T1))!;

        Assert.Equal(1d, portal.SourcePosition.Confidence);
        Assert.Equal(1d, portal.DestinationMap.Confidence);
        Assert.Equal(1d, portal.IsActive.Confidence);
    }

    [Fact]
    public void DetectCrossing_CustomConfidence_FlowsToEveryFact()
    {
        Portal portal = PortalCrossingDetector.DetectCrossing(
            Reading("map-1", 0, 0, T0), Reading("map-2", 0, 0, T1), confidence: 0.6)!;

        Assert.Equal(0.6, portal.SourcePosition.Confidence);
        Assert.Equal(0.6, portal.DestinationMap.Confidence);
        Assert.Equal(0.6, portal.IsActive.Confidence);
    }

    [Fact]
    public void DetectCrossing_TwoCrossingsAtTheSameRoundedPosition_ProduceTheSamePortalId()
    {
        // Small float jitter between polls (0.3 units) rounds to the same
        // identity -- this is what lets MapReconstructionFusion.MergeByKey
        // refine one portal across repeated crossings instead of
        // accumulating a new row per crossing.
        Portal first = PortalCrossingDetector.DetectCrossing(
            Reading("map-1", 10.1f, 20.4f, T0), Reading("map-2", 0, 0, T1))!;
        Portal second = PortalCrossingDetector.DetectCrossing(
            Reading("map-1", 10.4f, 19.6f, T0.AddMinutes(5)), Reading("map-2", 0, 0, T0.AddMinutes(5).AddSeconds(2)))!;

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public void DetectCrossing_CrossingsAtClearlyDifferentPositions_ProduceDifferentPortalIds()
    {
        Portal first = PortalCrossingDetector.DetectCrossing(
            Reading("map-1", 10, 10, T0), Reading("map-2", 0, 0, T1))!;
        Portal second = PortalCrossingDetector.DetectCrossing(
            Reading("map-1", 50, 50, T0), Reading("map-2", 0, 0, T1))!;

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void DetectCrossing_SameSourceMapDifferentDestination_ProducesTheSamePortalId()
    {
        // Portal.Id is derived only from the source side (source map + the
        // rounded position observed there) -- the identity of "this exit"
        // does not depend on which destination happened to be observed
        // through it, since MapReconstructionFusion merges by Id and the
        // most recent DestinationMap fact should win, not fork into a
        // second row.
        Portal first = PortalCrossingDetector.DetectCrossing(
            Reading("map-1", 10, 10, T0), Reading("map-2", 0, 0, T1))!;
        Portal second = PortalCrossingDetector.DetectCrossing(
            Reading("map-1", 10, 10, T0), Reading("map-3", 0, 0, T1))!;

        Assert.Equal(first.Id, second.Id);
    }
}
