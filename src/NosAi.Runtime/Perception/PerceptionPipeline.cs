// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Perception — Pipeline: classified acquisition, ROI segmentation, glyph OCR
//              cache, 2D Kalman filter and temporal tracking
// ============================================================================
//
// The vision logic (ROI, glyph cache, Kalman, tracker) is real and
// deterministic. Pixel acquisition lives behind a classified boundary: a real
// zero-copy DXGI backend produces LIVE frames; with no backend the capture is
// UNKNOWN, never invented pixels (ADR-0002). The triple-buffered DXGI Desktop
// Duplication path is a separate real-environment milestone.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception;

/// <summary>One captured frame descriptor. Provenance says where the pixels came from.</summary>
public sealed record CaptureFrame(
    int Width,
    int Height,
    ReadOnlyMemory<byte> Bgra,
    DataSourceKind Source,
    DateTime CapturedUtc)
{
    public bool HasPixels => Width > 0 && Height > 0 && Bgra.Length >= Width * Height * 4;
}

/// <summary>A source of frames. The real DXGI backend implements this at the platform boundary.</summary>
public interface IFrameSource
{
    /// <summary>Provenance of frames from this source (Live for real capture, Simulated for synthetic).</summary>
    DataSourceKind Source { get; }

    /// <summary>Tries to acquire the next frame; false when no frame is available.</summary>
    bool TryAcquire(out CaptureFrame frame);
}

/// <summary>
/// Deterministic synthetic frame source for tests and offline pipelines. Frames
/// are explicitly SIMULATED so they can never be mistaken for real capture.
/// </summary>
public sealed class SyntheticFrameSource : IFrameSource
{
    private readonly int _width;
    private readonly int _height;
    private readonly Func<int, DateTime> _clock;
    private int _frameIndex;

    public DataSourceKind Source => DataSourceKind.Simulated;

    public SyntheticFrameSource(int width = 1920, int height = 1080, Func<int, DateTime>? clock = null)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        _width = width;
        _height = height;
        // Time is injected (indexed by frame) so pipelines stay replayable in tests.
        _clock = clock ?? (i => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(i * 16));
    }

    public bool TryAcquire(out CaptureFrame frame)
    {
        // A small deterministic pattern: enough to exercise ROI/hash logic without
        // pretending to be a real screen.
        byte[] bgra = new byte[_width * _height * 4];
        byte tone = (byte)(_frameIndex % 256);
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = tone;
            bgra[i + 1] = (byte)(255 - tone);
            bgra[i + 2] = tone;
            bgra[i + 3] = 255;
        }
        frame = new CaptureFrame(_width, _height, bgra, DataSourceKind.Simulated, _clock(_frameIndex));
        _frameIndex++;
        return true;
    }
}

/// <summary>
/// Frame source used when no real capture backend is attached. It never yields a
/// frame and never fabricates one: callers see "no observation", classified
/// UNKNOWN, rather than an invented screen.
/// </summary>
public sealed class UnavailableFrameSource : IFrameSource
{
    public DataSourceKind Source => DataSourceKind.Unknown;
    public bool TryAcquire(out CaptureFrame frame)
    {
        frame = new CaptureFrame(0, 0, ReadOnlyMemory<byte>.Empty, DataSourceKind.Unknown, DateTime.UtcNow);
        return false;
    }
}

// ---------------------------------------------------------------------------
// ROI segmentation — deterministic named regions from frame dimensions.
// ---------------------------------------------------------------------------

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsWithin(int frameWidth, int frameHeight) =>
        X >= 0 && Y >= 0 && Right <= frameWidth && Bottom <= frameHeight && Width > 0 && Height > 0;
}

public enum RoiKind : byte { PlayerHpBar, PlayerMpBar, Minimap, TargetHpBar, ChatLog }

public sealed record RegionOfInterest(RoiKind Kind, PixelRect Rect);

/// <summary>
/// Maps the client area to the fixed HUD regions. Proportional, so it holds
/// across resolutions; every region is clamped inside the frame.
/// </summary>
/// <remarks>
/// <para>
/// <b>The regions are fractions of the game's client area, not of the captured
/// frame.</b> Those are the same rectangle only when the client fills the screen,
/// and T-03 found what happens when it does not: a windowed client at 1024x768
/// inside a 1920x1200 desktop put the HP region a thousand pixels away from the
/// HUD, over the editor behind it. The reader then measured a real bar ratio of
/// entirely the wrong pixels -- which is exactly the plausible-wrong-number that
/// ADR-0012 rejects, arrived at through geometry instead of a bad offset.
/// </para>
/// <para>
/// The fractions below were measured on the real client (1024x768 client area,
/// window class <c>TNosTaleMainF</c>): the HP bar occupies x 115..237, y 28..38
/// and the MP bar x 114..237, y 48..58. NosTale's HUD is at the <i>top</i> of its
/// window; the previous values placed both bars at the bottom left, which is
/// where a different game keeps them.
/// </para>
/// </remarks>
public static class RoiSegmenter
{
    /// <summary>
    /// Segments the HUD regions.
    /// </summary>
    /// <param name="frameWidth">Captured frame width, used for clamping.</param>
    /// <param name="frameHeight">Captured frame height, used for clamping.</param>
    /// <param name="clientArea">
    /// Where the game's client area sits inside the frame. When null the client is
    /// taken to fill the frame, which is right for a fullscreen client and wrong
    /// for every windowed one -- so a caller that can locate the window should
    /// pass it.
    /// </param>
    public static ImmutableArray<RegionOfInterest> Segment(
        int frameWidth, int frameHeight, PixelRect? clientArea = null)
    {
        if (frameWidth <= 0 || frameHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameWidth));

        PixelRect area = clientArea ?? new PixelRect(0, 0, frameWidth, frameHeight);
        if (area.Width <= 0 || area.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(clientArea), "The client area has no extent.");

        RegionOfInterest Roi(RoiKind kind, double x, double y, double w, double h)
        {
            int rx = area.X + (int)Math.Round(x * area.Width);
            int ry = area.Y + (int)Math.Round(y * area.Height);
            int rw = Math.Max(1, (int)Math.Round(w * area.Width));
            int rh = Math.Max(1, (int)Math.Round(h * area.Height));

            // Clamped against the frame, not the client area: a window partly off
            // the screen still yields a readable region for the part that is on it,
            // and a region running past the frame would read whatever follows in
            // the pixel buffer.
            rx = Math.Clamp(rx, 0, Math.Max(0, frameWidth - 1));
            ry = Math.Clamp(ry, 0, Math.Max(0, frameHeight - 1));
            rw = Math.Min(rw, frameWidth - rx);
            rh = Math.Min(rh, frameHeight - ry);
            return new RegionOfInterest(kind, new PixelRect(rx, ry, rw, rh));
        }

        return ImmutableArray.Create(
            Roi(RoiKind.PlayerHpBar, 0.112, 0.036, 0.121, 0.015),
            Roi(RoiKind.PlayerMpBar, 0.111, 0.062, 0.122, 0.015),
            Roi(RoiKind.Minimap, 0.84, 0.02, 0.14, 0.20),
            Roi(RoiKind.TargetHpBar, 0.40, 0.06, 0.20, 0.02),
            Roi(RoiKind.ChatLog, 0.02, 0.70, 0.30, 0.18));
    }
}

// ---------------------------------------------------------------------------
// Glyph-hash OCR cache — hashed glyph bitmaps mapped to characters, cached.
// ---------------------------------------------------------------------------

/// <summary>
/// OCR by glyph hashing: each glyph bitmap is reduced to a stable FNV-1a hash and
/// looked up in a trained table, with a cache so repeated glyphs skip the lookup.
/// This is the deterministic core; a real font atlas is trained in via
/// <see cref="Train"/> at the platform boundary.
/// </summary>
public sealed class GlyphHashOcrCache
{
    private readonly Dictionary<ulong, char> _glyphTable = new();
    private readonly Dictionary<ulong, char> _cache = new();
    private long _hits;
    private long _misses;

    public long CacheHits => _hits;
    public long CacheMisses => _misses;
    public int TrainedGlyphCount => _glyphTable.Count;

    public static ulong HashGlyph(ReadOnlySpan<byte> glyphBitmap)
    {
        // FNV-1a 64-bit: stable across runs and machines, unlike GetHashCode.
        const ulong offset = 1469598103934665603UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        foreach (byte b in glyphBitmap)
        {
            hash ^= b;
            hash *= prime;
        }
        return hash;
    }

    public void Train(char character, ReadOnlySpan<byte> glyphBitmap) =>
        _glyphTable[HashGlyph(glyphBitmap)] = character;

    /// <summary>Recognizes a sequence of glyph bitmaps; unknown glyphs become '?'.</summary>
    public string Recognize(IEnumerable<byte[]> glyphs)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        var chars = new List<char>();
        foreach (byte[] glyph in glyphs)
        {
            ulong hash = HashGlyph(glyph);
            if (_cache.TryGetValue(hash, out char cached))
            {
                _hits++;
                chars.Add(cached);
                continue;
            }
            _misses++;
            char resolved = _glyphTable.TryGetValue(hash, out char known) ? known : '?';
            _cache[hash] = resolved;
            chars.Add(resolved);
        }
        return new string(chars.ToArray());
    }

    /// <summary>Parses a recognized integer HUD value (e.g. an HP number), or null if not numeric.</summary>
    public int? RecognizeInteger(IEnumerable<byte[]> glyphs)
    {
        string text = Recognize(glyphs);
        return int.TryParse(text, out int value) ? value : null;
    }
}

// ---------------------------------------------------------------------------
// Kalman 2D — constant-velocity filter for smoothed position/velocity tracking.
// ---------------------------------------------------------------------------

/// <summary>
/// Constant-velocity 2D Kalman filter. State is (x, y, vx, vy); it predicts with
/// a time step and corrects with a position measurement. Real linear algebra, no
/// external dependency.
/// </summary>
public sealed class Kalman2DFilter
{
    // State vector and 4x4 covariance, stored as flat arrays.
    private readonly double[] _state = new double[4];
    private readonly double[,] _p = new double[4, 4];
    private readonly double _processNoise;
    private readonly double _measurementNoise;
    private bool _initialized;

    public double X => _state[0];
    public double Y => _state[1];
    public double VelocityX => _state[2];
    public double VelocityY => _state[3];
    public bool IsInitialized => _initialized;

    public Kalman2DFilter(double processNoise = 1.0, double measurementNoise = 4.0)
    {
        if (processNoise <= 0 || measurementNoise <= 0)
            throw new ArgumentOutOfRangeException(nameof(processNoise));
        _processNoise = processNoise;
        _measurementNoise = measurementNoise;
        for (int i = 0; i < 4; i++) _p[i, i] = 1000.0; // large initial uncertainty
    }

    public void Initialize(double x, double y)
    {
        _state[0] = x; _state[1] = y; _state[2] = 0; _state[3] = 0;
        _initialized = true;
    }

    /// <summary>Predicts the state forward by dt seconds (x += vx·dt, y += vy·dt).</summary>
    public void Predict(double dt)
    {
        if (dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
        if (!_initialized) return;

        _state[0] += _state[2] * dt;
        _state[1] += _state[3] * dt;

        // P = F P Fᵀ + Q, with F the constant-velocity transition.
        double q = _processNoise;
        double[,] f = { { 1, 0, dt, 0 }, { 0, 1, 0, dt }, { 0, 0, 1, 0 }, { 0, 0, 0, 1 } };
        double[,] fp = Multiply(f, _p);
        double[,] fpft = Multiply(fp, Transpose(f));
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                _p[i, j] = fpft[i, j] + (i == j ? q : 0);
    }

    /// <summary>Corrects the prediction with a measured position.</summary>
    public void Update(double measuredX, double measuredY)
    {
        if (!_initialized)
        {
            Initialize(measuredX, measuredY);
            return;
        }

        double r = _measurementNoise;
        // Innovation on the observed position components.
        double yx = measuredX - _state[0];
        double yy = measuredY - _state[1];

        // S = H P Hᵀ + R, with H observing (x, y). S is 2x2 over the position block.
        double s00 = _p[0, 0] + r, s01 = _p[0, 1];
        double s10 = _p[1, 0], s11 = _p[1, 1] + r;
        double det = s00 * s11 - s01 * s10;
        if (Math.Abs(det) < 1e-12) return;
        double i00 = s11 / det, i01 = -s01 / det, i10 = -s10 / det, i11 = s00 / det;

        // Kalman gain K = P Hᵀ S⁻¹ (4x2).
        double[,] k = new double[4, 2];
        for (int row = 0; row < 4; row++)
        {
            double p0 = _p[row, 0], p1 = _p[row, 1];
            k[row, 0] = p0 * i00 + p1 * i10;
            k[row, 1] = p0 * i01 + p1 * i11;
        }

        for (int row = 0; row < 4; row++)
            _state[row] += k[row, 0] * yx + k[row, 1] * yy;

        // P = (I - K H) P.
        double[,] newP = new double[4, 4];
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
            {
                double khP = k[i, 0] * _p[0, j] + k[i, 1] * _p[1, j];
                newP[i, j] = _p[i, j] - khP;
            }
        Array.Copy(newP, _p, newP.Length);
    }

    private static double[,] Multiply(double[,] a, double[,] b)
    {
        double[,] r = new double[4, 4];
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
            {
                double sum = 0;
                for (int k = 0; k < 4; k++) sum += a[i, k] * b[k, j];
                r[i, j] = sum;
            }
        return r;
    }

    private static double[,] Transpose(double[,] a)
    {
        double[,] r = new double[4, 4];
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                r[i, j] = a[j, i];
        return r;
    }
}

// ---------------------------------------------------------------------------
// Temporal entity tracking — stable IDs across frames via nearest-neighbour
// association plus Kalman prediction, ageing out entities that stop appearing.
// ---------------------------------------------------------------------------

public readonly record struct Detection(string Kind, double X, double Y, double HpRatio);

public sealed record TrackedEntity(long TrackId, string Kind, double X, double Y,
    double VelocityX, double VelocityY, double HpRatio, int MissedFrames);

public sealed class TemporalEntityTracker
{
    private sealed class TrackState
    {
        public required long Id { get; init; }
        public required string Kind { get; init; }
        public required Kalman2DFilter Filter { get; init; }
        public double HpRatio { get; set; }
        public int MissedFrames { get; set; }
    }

    private readonly double _associationRadius;
    private readonly int _maxMissedFrames;
    private readonly List<TrackState> _tracks = new();
    private long _nextTrackId = 1;

    public int ActiveTrackCount => _tracks.Count;

    public TemporalEntityTracker(double associationRadius = 40.0, int maxMissedFrames = 5)
    {
        if (associationRadius <= 0) throw new ArgumentOutOfRangeException(nameof(associationRadius));
        if (maxMissedFrames < 1) throw new ArgumentOutOfRangeException(nameof(maxMissedFrames));
        _associationRadius = associationRadius;
        _maxMissedFrames = maxMissedFrames;
    }

    /// <summary>
    /// Advances every track by dt, associates detections to the nearest predicted
    /// track within the radius (greedy, closest pairs first), spawns tracks for
    /// unmatched detections and ages out tracks that were not seen.
    /// </summary>
    public ImmutableArray<TrackedEntity> Track(IReadOnlyList<Detection> detections, double dt)
    {
        ArgumentNullException.ThrowIfNull(detections);
        foreach (var track in _tracks) track.Filter.Predict(dt);

        var candidates = new List<(double Dist, TrackState Track, int DetIndex)>();
        for (int d = 0; d < detections.Count; d++)
        {
            var det = detections[d];
            foreach (var track in _tracks)
            {
                if (!string.Equals(track.Kind, det.Kind, StringComparison.Ordinal)) continue;
                double dist = Math.Sqrt(Square(track.Filter.X - det.X) + Square(track.Filter.Y - det.Y));
                if (dist <= _associationRadius) candidates.Add((dist, track, d));
            }
        }

        var usedTracks = new HashSet<long>();
        var usedDetections = new HashSet<int>();
        foreach (var (_, track, detIndex) in candidates.OrderBy(c => c.Dist))
        {
            if (usedTracks.Contains(track.Id) || usedDetections.Contains(detIndex)) continue;
            var det = detections[detIndex];
            track.Filter.Update(det.X, det.Y);
            track.HpRatio = det.HpRatio;
            track.MissedFrames = 0;
            usedTracks.Add(track.Id);
            usedDetections.Add(detIndex);
        }

        for (int d = 0; d < detections.Count; d++)
        {
            if (usedDetections.Contains(d)) continue;
            var det = detections[d];
            var filter = new Kalman2DFilter();
            filter.Initialize(det.X, det.Y);
            _tracks.Add(new TrackState { Id = _nextTrackId++, Kind = det.Kind, Filter = filter, HpRatio = det.HpRatio, MissedFrames = 0 });
        }

        foreach (var track in _tracks)
            if (!usedTracks.Contains(track.Id)) track.MissedFrames++;
        _tracks.RemoveAll(t => t.MissedFrames > _maxMissedFrames);

        return _tracks
            .OrderBy(t => t.Id)
            .Select(t => new TrackedEntity(t.Id, t.Kind, t.Filter.X, t.Filter.Y,
                t.Filter.VelocityX, t.Filter.VelocityY, t.HpRatio, t.MissedFrames))
            .ToImmutableArray();
    }

    private static double Square(double v) => v * v;
}

// ---------------------------------------------------------------------------
// Pipeline composition + adapter to the canonical WorldState.
// ---------------------------------------------------------------------------

public sealed record PerceptionResult(
    long FrameIndex,
    DataSourceKind Source,
    bool FrameAcquired,
    ImmutableArray<RegionOfInterest> Regions,
    ImmutableArray<TrackedEntity> Entities,
    string? UnavailableReason);

/// <summary>
/// Drives the vision pipeline over a frame source. When no frame is acquired the
/// result is honestly marked UNAVAILABLE/UNKNOWN — never a fabricated observation.
/// </summary>
public sealed class PerceptionPipeline
{
    private readonly IFrameSource _frameSource;
    private readonly TemporalEntityTracker _tracker;
    private readonly Func<CaptureFrame, IReadOnlyList<Detection>> _detector;
    private long _frameIndex;
    private DateTime? _lastFrameUtc;

    public PerceptionPipeline(IFrameSource frameSource,
        Func<CaptureFrame, IReadOnlyList<Detection>> detector,
        TemporalEntityTracker? tracker = null)
    {
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _tracker = tracker ?? new TemporalEntityTracker();
    }

    public PerceptionResult ProcessNext()
    {
        long index = _frameIndex++;
        if (!_frameSource.TryAcquire(out CaptureFrame frame) || !frame.HasPixels)
        {
            return new PerceptionResult(index, DataSourceKind.Unknown, false,
                ImmutableArray<RegionOfInterest>.Empty, ImmutableArray<TrackedEntity>.Empty,
                "no_frame_acquired");
        }

        double dt = _lastFrameUtc is { } last ? Math.Max(0.0, (frame.CapturedUtc - last).TotalSeconds) : 0.0;
        _lastFrameUtc = frame.CapturedUtc;

        var regions = RoiSegmenter.Segment(frame.Width, frame.Height);
        var detections = _detector(frame);
        var entities = _tracker.Track(detections, dt);
        return new PerceptionResult(index, frame.Source, true, regions, entities, null);
    }
}

/// <summary>Maps a perception result into the canonical <see cref="WorldModel.WorldState"/>.</summary>
public static class PerceptionWorldStateAdapter
{
    public static WorldModel.WorldState ToWorldState(PerceptionResult result, bool playerAlive, double playerHpRatio)
    {
        ArgumentNullException.ThrowIfNull(result);
        var entities = result.Entities
            .Select(e => new WorldModel.EntityState(
                $"{e.Kind}#{e.TrackId}", e.Kind, e.X, e.Y, e.HpRatio))
            .ToArray();
        return new WorldModel.WorldState(result.FrameIndex, playerAlive, playerHpRatio, entities);
    }
}
