using System.Collections.Immutable;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The HUD regions are fractions of the game's client area, not of the captured
/// frame. T-03 is what happens when those are conflated.
/// </summary>
/// <remarks>
/// The real client is a 1024x768 window inside a 1920x1200 desktop. Segmenting
/// against the frame put the player's HP region at the bottom left of the screen,
/// a thousand pixels from the HUD, over the editor behind the game. The reader
/// then measured a real bar ratio of entirely the wrong pixels -- the
/// plausible-wrong-number ADR-0012 rejects, reached through geometry rather than
/// a bad offset.
/// </remarks>
public sealed class RoiClientAreaTests
{
    /// <summary>The measured client area of the real client during T-03.</summary>
    private static readonly PixelRect RealClientArea = new(169, 243, 1024, 768);

    private const int DesktopWidth = 1920;
    private const int DesktopHeight = 1200;

    private static PixelRect RectOf(ImmutableArray<RegionOfInterest> regions, RoiKind kind) =>
        regions.Single(r => r.Kind == kind).Rect;

    [Fact]
    public void EveryRegionLandsInsideTheClientAreaWhenOneIsGiven()
    {
        ImmutableArray<RegionOfInterest> regions =
            RoiSegmenter.Segment(DesktopWidth, DesktopHeight, RealClientArea);

        foreach (RegionOfInterest region in regions)
        {
            Assert.True(region.Rect.X >= RealClientArea.X,
                $"{region.Kind} starts left of the client area");
            Assert.True(region.Rect.Y >= RealClientArea.Y,
                $"{region.Kind} starts above the client area");
            Assert.True(region.Rect.Right <= RealClientArea.Right,
                $"{region.Kind} runs past the right edge of the client area");
            Assert.True(region.Rect.Bottom <= RealClientArea.Bottom,
                $"{region.Kind} runs past the bottom of the client area");
        }
    }

    [Fact]
    public void ThePlayerBarsLandOnTheHudMeasuredOnTheRealClient()
    {
        // Measured off the running client: the HP bar occupies x 115..237,
        // y 28..38 of the 1024x768 client area, and MP x 114..237, y 48..58.
        // A few pixels of tolerance, because the region is deliberately a little
        // larger than the bar rather than pixel-exact.
        ImmutableArray<RegionOfInterest> regions =
            RoiSegmenter.Segment(DesktopWidth, DesktopHeight, RealClientArea);

        PixelRect hp = RectOf(regions, RoiKind.PlayerHpBar);
        PixelRect mp = RectOf(regions, RoiKind.PlayerMpBar);

        Assert.InRange(hp.X - RealClientArea.X, 110, 120);
        Assert.InRange(hp.Y - RealClientArea.Y, 24, 32);
        Assert.InRange(mp.X - RealClientArea.X, 110, 120);
        Assert.InRange(mp.Y - RealClientArea.Y, 44, 52);

        // The bars are stacked, not overlapping: reading one must not sample the
        // other, or a full MP bar would hold up an empty HP one.
        Assert.True(hp.Bottom <= mp.Y, "the HP region overlaps the MP region");
    }

    [Fact]
    public void TheHudIsAtTheTopOfTheClientNotTheBottom()
    {
        // The previous values put both player bars at 92% and 95% of the frame
        // height. NosTale keeps them at the top of its window, and this is the
        // assertion that fails if anyone restores the old ones.
        ImmutableArray<RegionOfInterest> regions =
            RoiSegmenter.Segment(DesktopWidth, DesktopHeight, RealClientArea);

        PixelRect hp = RectOf(regions, RoiKind.PlayerHpBar);

        Assert.True(
            hp.Y - RealClientArea.Y < RealClientArea.Height / 2,
            "the player HP bar is in the top half of the client area");
    }

    [Fact]
    public void WithoutAClientAreaTheFrameIsTakenToBeTheClient()
    {
        // Right for a fullscreen client, and the only sane default: the segmenter
        // cannot locate a window, so it says what it assumed rather than guessing
        // at one.
        ImmutableArray<RegionOfInterest> withNull = RoiSegmenter.Segment(1024, 768);
        ImmutableArray<RegionOfInterest> withFullFrame =
            RoiSegmenter.Segment(1024, 768, new PixelRect(0, 0, 1024, 768));

        // Compared element-wise: ImmutableArray equality is by underlying
        // reference, so two structurally identical arrays are not Equal.
        Assert.Equal(withFullFrame.ToArray(), withNull.ToArray());
    }

    [Fact]
    public void RegionsAreClampedToTheFrameWhenTheWindowHangsOffTheScreen()
    {
        // Half the window past the right edge. The regions that remain must stay
        // inside the pixel buffer: one running past it would read whatever bytes
        // follow, which is not a dark region but another part of the screen.
        var offScreen = new PixelRect(1600, 1100, 1024, 768);

        ImmutableArray<RegionOfInterest> regions =
            RoiSegmenter.Segment(DesktopWidth, DesktopHeight, offScreen);

        foreach (RegionOfInterest region in regions)
        {
            Assert.True(region.Rect.Right <= DesktopWidth, $"{region.Kind} runs past the frame width");
            Assert.True(region.Rect.Bottom <= DesktopHeight, $"{region.Kind} runs past the frame height");
            Assert.True(region.Rect.Width > 0 && region.Rect.Height > 0, $"{region.Kind} collapsed");
        }
    }

    [Fact]
    public void AClientAreaWithNoExtentIsRefusedRatherThanSegmented()
    {
        // The Delphi stub window T-03 found has a client area of 0x0. Segmenting
        // against it would divide the HUD across nothing and return regions at the
        // window's own off-screen origin.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RoiSegmenter.Segment(DesktopWidth, DesktopHeight, new PixelRect(-25600, -25600, 0, 0)));
    }
}
