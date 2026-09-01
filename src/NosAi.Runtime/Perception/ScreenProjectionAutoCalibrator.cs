using System.Diagnostics;
using System.Globalization;
using NosAi.LiveIntegration;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Safety;

namespace NosAi.Runtime.Perception;

/// <summary>
/// Calibrates the screen projection without an operator, by clicking pixels the
/// runtime picks and reading back which map square the client resolved each one
/// to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Every earlier form of this asked a person to pair a
/// map coordinate with a pixel by hand, and each one failed for its own reason:
/// the client does not display coordinates, so there was nothing to type; the
/// camera follows the character, so its own pixel never moves; and most of the
/// samples that were recorded had to be discarded because the pointer was outside
/// the window. Underneath all three is one problem — a calibration tied to a
/// particular window size and a particular person's aim is invalidated by
/// resizing the window or switching to full screen, which is a thing players do
/// and not a thing to be asked not to do.
/// </para>
/// <para>
/// <b>What replaces it.</b> Click-to-walk makes the client answer the exact
/// question a calibration asks: <i>which square is this pixel?</i> It resolves
/// the pixel itself and writes the answer where it can be read
/// (<see cref="NosTaleClientLayout.WalkTargetOffset"/>). So the runtime chooses
/// the pixel, clicks it, and reads the client's own answer — the same move
/// ADR-0017 made when it took the glyph labels from the wire instead of from the
/// operator's typing. Nobody has to aim, and nobody has to know a coordinate.
/// </para>
/// <para>
/// <b>It costs a walk.</b> Each sample really does move the character a short
/// distance, because a click that moves nothing tells us nothing. That is why
/// this is armed explicitly rather than run as part of attaching: it is an action
/// in the world, and it goes through <see cref="GatedInputBackend"/> like every
/// other one.
/// </para>
/// <para>
/// <b>Elevation.</b> The client runs at high integrity, so a medium-integrity
/// process can neither read its memory nor send it input. Both halves of this
/// need the runtime to be elevated, and the refusal says so by name rather than
/// producing an empty result.
/// </para>
/// </remarks>
public static class ScreenProjectionAutoCalibrator
{
    /// <summary>
    /// How many pairs to collect: three to fit, the rest to check the fit.
    /// </summary>
    /// <remarks>
    /// Three alone would leave the solve exactly determined and therefore
    /// unfalsifiable — six unknowns from six equations reproduce their own input
    /// whatever it was, which is precisely how the previous calibration reported a
    /// residual of 0.00 on a transform that described nothing.
    /// </remarks>
    public const int SampleCount = 6;

    /// <summary>
    /// How far from the centre the probe pixels are placed, as a fraction of the
    /// shorter side of the client area.
    /// </summary>
    /// <remarks>
    /// Far enough that the walk is long enough to measure, close enough that the
    /// ring stays inside the rendered world and clear of the interface drawn round
    /// its edges — the quickbar along the bottom, the minimap at a corner. A click
    /// on the interface is not a click on the map and produces no walk at all.
    /// </remarks>
    private const double ProbeRadiusFraction = 0.20;

    /// <summary>Tried per probe point before giving up on it.</summary>
    /// <remarks>
    /// A click can land on an obstacle, a monster or the edge of the map, and none
    /// of those walks anywhere. Each retry pulls the point closer to the centre,
    /// over ground the character is standing on and therefore known to be
    /// walkable.
    /// </remarks>
    private const int AttemptsPerPoint = 3;

    /// <summary>How long to wait for the client to accept a click as a walk.</summary>
    private const int WalkAcceptedTimeoutMs = 1200;

    /// <summary>How long the position must hold still to count as stopped.</summary>
    private const int StillForMs = 300;

    /// <summary>How long to wait for the character to stop before sampling.</summary>
    private const int StopTimeoutMs = 8000;

    /// <summary>Between placing the cursor and clicking it.</summary>
    private const int SettleBeforeClickMs = 90;

    private const int PollIntervalMs = 20;

    /// <summary>Smallest and largest believable size of one map tile, in pixels.</summary>
    /// <remarks>
    /// Deliberately wide: this is not measuring the zoom, it is catching a fit
    /// whose scale is nonsense — which is what a client that resolved clicks to an
    /// intermediate waypoint rather than to the destination would produce,
    /// consistently enough that the residual check would not notice.
    /// </remarks>
    private const double MinTilePitchPixels = 2.0;
    private const double MaxTilePitchPixels = 200.0;

    /// <summary>
    /// Collects the samples, solves them and writes the calibration.
    /// </summary>
    /// <param name="armInput">
    /// False runs the whole thing without clicking: it reports the points it would
    /// have used and stops at the gate. Arming is a separate word the operator has
    /// to type, because this moves their character.
    /// </param>
    public static int Run(bool armInput, string? repoRoot = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Reading the client and sending it input both need Windows.");
            return 2;
        }

        repoRoot ??= Directory.GetCurrentDirectory();

        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? attachFailure))
        {
            Console.WriteLine($"[REFUSED] {attachFailure}");
            return 1;
        }

        using (session)
        {
            if (ClientWindowLocator.TryFind(session!.ProcessId, out string? windowFailure) is not { } window)
            {
                Console.WriteLine($"[REFUSED] {windowFailure ?? CalibratedScreenProjection.WindowNotLocatedReason}");
                return 1;
            }

            PixelRect area = window.ClientArea;
            var policy = new RuntimeSafetyPolicy(
                LiveInputEnabled: armInput,
                PacketInjectionEnabled: false,
                RequireClientHealthy: true,
                RequireGuardApproval: false);
            var input = new GatedInputBackend(new Win32InputBackend(), policy);

            IReadOnlyList<(int X, int Y)> points = ProbePoints(area, SampleCount);

            Console.WriteLine($"Client area {area.Width}x{area.Height} at {area.X},{area.Y} ({window.ClassName})");
            Console.WriteLine($"Probe points ({points.Count}), client-relative:");
            foreach ((int px, int py) in points)
                Console.WriteLine($"  {px},{py}");
            Console.WriteLine();

            if (!armInput)
            {
                Console.WriteLine("[DRY RUN] Nothing was clicked and the character did not move.");
                Console.WriteLine("  Each sample walks the character a short distance, so the Safety Gate");
                Console.WriteLine("  stays closed unless it is opened on purpose:");
                Console.WriteLine("    --screen-autocalibrate --arm-input");
                Console.WriteLine("  Stand somewhere open, with room to walk in every direction.");
                return 0;
            }

            if (!IsClientFocused(window.Handle))
            {
                Console.WriteLine("[REFUSED] client_window_not_focused");
                Console.WriteLine("  Click on the game window first. Input goes to whatever is in front, and");
                Console.WriteLine("  a click delivered to another window is a real click somewhere nobody");
                Console.WriteLine("  chose — the failure this whole path refuses rather than risks.");
                return 1;
            }

            var samples = new List<ScreenProjectionSample>();
            var recorded = new List<string>();

            foreach ((int px, int py) in points)
            {
                if (!TryCollect(session, input, area, px, py, out ScreenProjectionSample sample, out string? why))
                {
                    Console.WriteLine($"  point {px},{py}: skipped ({why})");
                    continue;
                }

                samples.Add(sample);
                recorded.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{sample.MapDelta.X} {sample.MapDelta.Y} {sample.ScreenX} {sample.ScreenY} {area.Width} {area.Height}"));
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  point {sample.ScreenX},{sample.ScreenY} -> offset ({sample.MapDelta.X},{sample.MapDelta.Y})"));
            }

            Console.WriteLine();
            if (samples.Count < ScreenProjectionCalibration.MinimumSamples)
            {
                Console.WriteLine(
                    $"[REFUSED] not_enough_samples:{samples.Count}_of_{ScreenProjectionCalibration.MinimumSamples}");
                Console.WriteLine("  Too many clicks walked nowhere. Stand in open ground, away from walls,");
                Console.WriteLine("  portals and monsters, and run it again.");
                return 1;
            }

            if (!Solve(samples, area, out ScreenProjectionCalibration calibration,
                    out int dropped, out string? solveFailure))
            {
                Console.WriteLine($"[REFUSED] {solveFailure}");
                Console.WriteLine("  Nothing was written. The old calibration, if any, is untouched.");
                return 1;
            }

            double pitchX = Math.Sqrt((calibration.A * calibration.A) + (calibration.D * calibration.D));
            double pitchY = Math.Sqrt((calibration.B * calibration.B) + (calibration.E * calibration.E));
            if (pitchX < MinTilePitchPixels || pitchX > MaxTilePitchPixels
                || pitchY < MinTilePitchPixels || pitchY > MaxTilePitchPixels)
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"[REFUSED] implausible_tile_pitch:{pitchX:F1}x{pitchY:F1}px"));
                Console.WriteLine("  The fit is self-consistent but its scale is not a map tile. Nothing");
                Console.WriteLine("  was written.");
                return 1;
            }

            string samplePath = Path.Combine(repoRoot, ScreenProjectionProbe.SamplesRelativePath);
            string? sampleDirectory = Path.GetDirectoryName(samplePath);
            if (!string.IsNullOrEmpty(sampleDirectory))
                Directory.CreateDirectory(sampleDirectory);
            File.WriteAllLines(samplePath, recorded);

            string path = Path.Combine(repoRoot, ScreenProjectionCalibration.RelativePath);
            calibration.Save(path);

            Console.WriteLine(
                $"Screen projection calibrated from {samples.Count - dropped} of {samples.Count} samples.");
            if (dropped > 0)
            {
                Console.WriteLine("  1 rejected as inconsistent with the rest — a click that hit an obstacle");
                Console.WriteLine("  walks somewhere other than where it was aimed.");
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  screenX = {calibration.A:F3}*dx + {calibration.B:F3}*dy + {calibration.C:F1}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  screenY = {calibration.D:F3}*dx + {calibration.E:F3}*dy + {calibration.F:F1}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  Character drawn at {calibration.Anchor.X:F0},{calibration.Anchor.Y:F0}"
                + $" of {area.Width}x{area.Height}; one tile is {pitchX:F1}x{pitchY:F1} px."));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  Worst residual {calibration.WorstResidualPixels:F2} px,"
                + $" {calibration.VerifiedAgainstSamples} sample(s) held back as a check."));
            Console.WriteLine($"  {path}");
            Console.WriteLine("  Valid at this client size. Resize the window or go full screen and it is");
            Console.WriteLine("  refused by name — run this again and it recalibrates itself.");
            return 0;
        }
    }

    /// <summary>
    /// Fits the samples, dropping at most one that the others contradict.
    /// </summary>
    /// <remarks>
    /// One bad pair is expected rather than exceptional: a click can land on an
    /// obstacle and walk somewhere other than where it was aimed. Dropping one and
    /// refitting keeps an unattended run from failing on it, and what makes that
    /// safe is that the reduced set still has to pass the residual test against
    /// its own held-back samples. Two contradictions are not an accident and are
    /// reported rather than fitted around.
    /// </remarks>
    private static bool Solve(
        IReadOnlyList<ScreenProjectionSample> samples,
        PixelRect area,
        out ScreenProjectionCalibration calibration,
        out int dropped,
        out string? failureReason)
    {
        dropped = 0;
        if (ScreenProjectionCalibration.TrySolve(
                samples, area.Width, area.Height, DateTime.UtcNow, out calibration, out failureReason))
        {
            return true;
        }

        // Only a disagreement is worth retrying. Collinear offsets, a degenerate
        // scale or an anchor off the window are properties of the whole set, and
        // dropping one sample does not change them.
        if (failureReason is null || !failureReason.StartsWith("samples_disagree", StringComparison.Ordinal))
            return false;

        if (samples.Count <= ScreenProjectionCalibration.MinimumSamples + 1)
            return false;

        for (var skip = 0; skip < samples.Count; skip++)
        {
            var reduced = new List<ScreenProjectionSample>(samples.Count - 1);
            for (var i = 0; i < samples.Count; i++)
            {
                if (i != skip)
                    reduced.Add(samples[i]);
            }

            if (ScreenProjectionCalibration.TrySolve(
                    reduced, area.Width, area.Height, DateTime.UtcNow, out calibration, out _))
            {
                dropped = 1;
                failureReason = null;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Clicks one probe point and reads back the square the client resolved it to.
    /// </summary>
    private static bool TryCollect(
        ClientMemorySession session,
        GatedInputBackend input,
        PixelRect area,
        int pixelX,
        int pixelY,
        out ScreenProjectionSample sample,
        out string? failureReason)
    {
        sample = default;
        failureReason = null;

        for (var attempt = 0; attempt < AttemptsPerPoint; attempt++)
        {
            double pull = 1.0 - (0.3 * attempt);
            int x = (int)Math.Round((area.Width / 2.0) + ((pixelX - (area.Width / 2.0)) * pull));
            int y = (int)Math.Round((area.Height / 2.0) + ((pixelY - (area.Height / 2.0)) * pull));

            if (!WaitUntilStopped(session, out PlayerObjectReading start, out failureReason))
                return false;

            if (start.WalkTargetX is not { } beforeX || start.WalkTargetY is not { } beforeY)
            {
                failureReason = "walk_target_unreadable";
                return false;
            }

            if (!input.MoveAbsolute(area.X + x, area.Y + y))
            {
                failureReason = "input_not_accepted:cursor_move";
                return false;
            }

            Thread.Sleep(SettleBeforeClickMs);

            if (!input.Click(MouseButton.Left))
            {
                failureReason = "input_not_accepted:click";
                return false;
            }

            if (!WaitForNewWalkTarget(session, beforeX, beforeY, out short targetX, out short targetY))
            {
                failureReason = "click_did_not_walk";
                continue;
            }

            // A click resolving to the square already occupied is a zero offset: it
            // constrains nothing, and three of them would be collinear.
            if (targetX == start.X && targetY == start.Y)
            {
                failureReason = "click_resolved_to_current_square";
                continue;
            }

            sample = new ScreenProjectionSample(
                new MapPoint(targetX - start.X, targetY - start.Y), x, y);
            failureReason = null;
            return true;
        }

        failureReason ??= "click_did_not_walk";
        return false;
    }

    /// <summary>
    /// Waits for the character to stand still, and returns the reading taken there.
    /// </summary>
    /// <remarks>
    /// The offset has to be measured from where the character was when the click
    /// happened. Sampling while it is still walking pairs the new destination with
    /// a position it has already left — a wrong pair that looks like a right one,
    /// and one the residual check cannot distinguish from a slightly noisy fit.
    /// </remarks>
    private static bool WaitUntilStopped(
        ClientMemorySession session, out PlayerObjectReading reading, out string? failureReason)
    {
        reading = default;

        var clock = Stopwatch.StartNew();
        var stillSince = Stopwatch.StartNew();
        ushort lastX = 0, lastY = 0;
        var haveLast = false;

        while (clock.ElapsedMilliseconds < StopTimeoutMs)
        {
            if (!session.TryReadPlayer(out PlayerObjectReading current, out failureReason))
                return false;

            if (haveLast && current.X == lastX && current.Y == lastY)
            {
                if (stillSince.ElapsedMilliseconds >= StillForMs)
                {
                    reading = current;
                    failureReason = null;
                    return true;
                }
            }
            else
            {
                stillSince.Restart();
                lastX = current.X;
                lastY = current.Y;
                haveLast = true;
            }

            Thread.Sleep(PollIntervalMs);
        }

        failureReason = "character_never_stopped_walking";
        return false;
    }

    /// <summary>Polls for the walk target to change, which is the client accepting the click.</summary>
    private static bool WaitForNewWalkTarget(
        ClientMemorySession session, short beforeX, short beforeY, out short targetX, out short targetY)
    {
        targetX = 0;
        targetY = 0;

        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < WalkAcceptedTimeoutMs)
        {
            Thread.Sleep(PollIntervalMs);

            if (!session.TryReadPlayer(out PlayerObjectReading current, out _))
                continue;
            if (current.WalkTargetX is not { } x || current.WalkTargetY is not { } y)
                continue;
            if (x == beforeX && y == beforeY)
                continue;

            targetX = x;
            targetY = y;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Points on a ring around the centre of the rendered world.
    /// </summary>
    /// <remarks>
    /// Spread around a circle so no two offsets come out parallel — three offsets
    /// on one line determine nothing, which the solver refuses by name and which a
    /// ring makes unlikely by construction. The eighth-turn start keeps the points
    /// off the axes, where an isometric projection puts several map directions
    /// onto the same screen direction.
    /// </remarks>
    internal static IReadOnlyList<(int X, int Y)> ProbePoints(PixelRect area, int count)
    {
        var points = new List<(int, int)>(count);
        double centreX = area.Width / 2.0;
        double centreY = area.Height / 2.0;
        double radius = Math.Min(area.Width, area.Height) * ProbeRadiusFraction;

        for (var i = 0; i < count; i++)
        {
            double angle = ((2 * Math.PI * i) / count) + (Math.PI / 8);
            points.Add((
                (int)Math.Round(centreX + (radius * Math.Cos(angle))),
                (int)Math.Round(centreY + (radius * Math.Sin(angle)))));
        }

        return points;
    }

    private static bool IsClientFocused(IntPtr handle)
        => OperatingSystem.IsWindows() && NativeMethods.GetForegroundWindow() == handle;

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();
    }
}
