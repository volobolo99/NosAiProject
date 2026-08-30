// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Percezione — Suite di certificazione della pipeline di visione
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception;

public static class PerceptionPipelineTestRunner
{
    /// <summary>
    /// Runs every perception check and reports each one by name (same contract as
    /// the gate runners: no short-circuit, a throwing check is a named failure).
    /// </summary>
    public static bool RunAll()
    {
        Console.WriteLine("=== Perception pipeline checks ===");

        bool allPassed = true;
        allPassed &= Run("Unavailable capture yields UNKNOWN, not a fabricated frame", TestUnavailableCaptureIsHonest);
        allPassed &= Run("Synthetic frames are labeled SIMULATED", TestSyntheticFramesAreSimulated);
        allPassed &= Run("ROI segmentation stays inside the frame at any resolution", TestRoiSegmentationBounds);
        allPassed &= Run("Glyph hashing is stable and content-addressed", TestGlyphHashStability);
        allPassed &= Run("Glyph OCR cache recognizes trained numbers and caches", TestGlyphOcrCache);
        allPassed &= Run("Kalman filter converges toward a moving target", TestKalmanConvergence);
        allPassed &= Run("Kalman predicts forward along its velocity", TestKalmanPrediction);
        allPassed &= Run("Tracker keeps stable ids for a moving entity", TestTrackerStableIds);
        allPassed &= Run("Tracker spawns and ages out entities", TestTrackerSpawnAndExpire);
        allPassed &= Run("Pipeline maps tracks into the canonical WorldState", TestPipelineToWorldState);

        Console.WriteLine(allPassed
            ? "=== Perception checks passed. Local only: no real DXGI capture backend is attached. ==="
            : "=== Perception checks FAILED. See the lines marked FAIL above. ===");
        return allPassed;
    }

    private static bool Run(string name, Func<bool> check)
    {
        try { return Report(name, check(), null); }
        catch (Exception ex) { return Report(name, false, $"{ex.GetType().Name}: {ex.Message}"); }
    }

    private static bool Report(string name, bool passed, string? error)
    {
        var detail = error is null ? string.Empty : $" [{error}]";
        Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {name}{detail}");
        return passed;
    }

    private static IReadOnlyList<Detection> NoDetections(CaptureFrame _) => Array.Empty<Detection>();

    private static bool TestUnavailableCaptureIsHonest()
    {
        var pipeline = new PerceptionPipeline(new UnavailableFrameSource(), NoDetections);
        var result = pipeline.ProcessNext();
        return !result.FrameAcquired
            && result.Source == DataSourceKind.Unknown
            && result.UnavailableReason == "no_frame_acquired"
            && result.Regions.IsEmpty
            && result.Entities.IsEmpty;
    }

    private static bool TestSyntheticFramesAreSimulated()
    {
        var source = new SyntheticFrameSource(640, 360);
        if (!source.TryAcquire(out CaptureFrame frame)) return false;
        return frame.Source == DataSourceKind.Simulated
            && frame.HasPixels
            && frame.Width == 640 && frame.Height == 360;
    }

    private static bool TestRoiSegmentationBounds()
    {
        foreach (var (w, h) in new[] { (1920, 1080), (1280, 720), (3840, 2160), (800, 600) })
        {
            var regions = RoiSegmenter.Segment(w, h);
            if (regions.Length != 5) return false;
            if (regions.Select(r => r.Kind).Distinct().Count() != 5) return false;
            foreach (var region in regions)
                if (!region.Rect.IsWithin(w, h)) return false;
        }
        return true;
    }

    private static bool TestGlyphHashStability()
    {
        byte[] glyphA = Encoding.ASCII.GetBytes("glyph-shape-A");
        byte[] glyphAClone = Encoding.ASCII.GetBytes("glyph-shape-A");
        byte[] glyphB = Encoding.ASCII.GetBytes("glyph-shape-B");
        ulong hashA = GlyphHashOcrCache.HashGlyph(glyphA);
        return hashA == GlyphHashOcrCache.HashGlyph(glyphAClone)
            && hashA != GlyphHashOcrCache.HashGlyph(glyphB);
    }

    private static bool TestGlyphOcrCache()
    {
        var ocr = new GlyphHashOcrCache();
        for (char c = '0'; c <= '9'; c++)
            ocr.Train(c, Encoding.ASCII.GetBytes($"digit-{c}"));
        if (ocr.TrainedGlyphCount != 10) return false;

        byte[][] number = "1485".Select(c => Encoding.ASCII.GetBytes($"digit-{c}")).ToArray();
        if (ocr.RecognizeInteger(number) != 1485) return false;

        // Recognize the same sequence again: the cache must serve the repeats.
        long missesBefore = ocr.CacheMisses;
        ocr.Recognize(number);
        if (ocr.CacheHits < 4 || ocr.CacheMisses != missesBefore) return false;

        // An untrained glyph resolves to '?', never a fabricated digit.
        byte[][] unknown = { Encoding.ASCII.GetBytes("digit-X") };
        return ocr.Recognize(unknown) == "?";
    }

    private static bool TestKalmanConvergence()
    {
        var filter = new Kalman2DFilter(processNoise: 1.0, measurementNoise: 9.0);
        var rng = new DeterministicNoise(seed: 12345);
        double trueX = 0, trueY = 0;
        for (int i = 0; i < 60; i++)
        {
            trueX += 2.0; trueY += 1.0; // constant velocity
            filter.Predict(1.0);
            filter.Update(trueX + rng.Next(), trueY + rng.Next());
        }
        // After convergence the estimate tracks the true position closely despite noise.
        return Math.Abs(filter.X - trueX) < 5.0 && Math.Abs(filter.Y - trueY) < 5.0
            && Math.Abs(filter.VelocityX - 2.0) < 1.0 && Math.Abs(filter.VelocityY - 1.0) < 1.0;
    }

    private static bool TestKalmanPrediction()
    {
        var filter = new Kalman2DFilter();
        filter.Initialize(10, 20);
        // Feed a few exact measurements so the filter learns the velocity.
        double x = 10, y = 20;
        for (int i = 0; i < 20; i++) { x += 3; y -= 1; filter.Predict(1.0); filter.Update(x, y); }
        double beforeX = filter.X;
        filter.Predict(1.0); // no update: pure prediction along velocity
        return filter.X > beforeX && filter.VelocityX > 2.0 && filter.VelocityY < 0.0;
    }

    private static bool TestTrackerStableIds()
    {
        var tracker = new TemporalEntityTracker(associationRadius: 40, maxMissedFrames: 5);
        long? id = null;
        double x = 100, y = 100;
        for (int i = 0; i < 10; i++)
        {
            x += 5; y += 3;
            var tracked = tracker.Track(new[] { new Detection("Monster", x, y, 1.0) }, 1.0);
            if (tracked.Length != 1) return false;
            id ??= tracked[0].TrackId;
            if (tracked[0].TrackId != id) return false; // same entity keeps its id
        }
        return tracker.ActiveTrackCount == 1;
    }

    private static bool TestTrackerSpawnAndExpire()
    {
        var tracker = new TemporalEntityTracker(associationRadius: 30, maxMissedFrames: 3);
        tracker.Track(new[] { new Detection("Mob", 50, 50, 1.0), new Detection("Mob", 500, 500, 1.0) }, 1.0);
        if (tracker.ActiveTrackCount != 2) return false;

        // One entity keeps appearing; the other vanishes and must age out.
        for (int i = 0; i < 5; i++)
            tracker.Track(new[] { new Detection("Mob", 50, 50, 1.0) }, 1.0);
        return tracker.ActiveTrackCount == 1;
    }

    private static bool TestPipelineToWorldState()
    {
        int frame = 0;
        var source = new SyntheticFrameSource(1280, 720);
        // A detector that reports one moving monster, derived from the frame index.
        var pipeline = new PerceptionPipeline(source, f =>
        {
            frame++;
            return new[] { new Detection("Monster", 100 + frame * 5, 200, 0.8) };
        });

        PerceptionResult? last = null;
        for (int i = 0; i < 4; i++) last = pipeline.ProcessNext();
        if (last is null || !last.FrameAcquired || last.Source != DataSourceKind.Simulated) return false;

        var world = PerceptionWorldStateAdapter.ToWorldState(last, playerAlive: true, playerHpRatio: 0.95);
        return world.Entities.Count == 1
            && world.Entities[0].Kind == "Monster"
            && world.PlayerHpRatio == 0.95
            && world.Tick == last.FrameIndex;
    }

    /// <summary>Deterministic zero-mean noise so the Kalman checks never flake.</summary>
    private sealed class DeterministicNoise
    {
        private uint _state;
        public DeterministicNoise(uint seed) => _state = seed == 0 ? 1 : seed;
        public double Next()
        {
            // xorshift32 mapped to [-1.5, 1.5): small bounded measurement noise.
            _state ^= _state << 13; _state ^= _state >> 17; _state ^= _state << 5;
            return (_state / (double)uint.MaxValue) * 3.0 - 1.5;
        }
    }
}
