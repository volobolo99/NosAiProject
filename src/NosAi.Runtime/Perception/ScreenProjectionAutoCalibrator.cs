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
    /// <summary>How many pairs to attempt.</summary>
    /// <remarks>
    /// Comfortably more than <see cref="MinimumFittedSamples"/>, because clicks are
    /// lost for ordinary reasons: one lands on an obstacle, one walks nowhere, one
    /// disagrees with the rest and is dropped. Collecting only as many as the fit
    /// needs means any of those turns into a calibration with nothing left to check
    /// it, which is exactly the run this count was raised after.
    /// </remarks>
    public const int SampleCount = 12;

    /// <summary>
    /// The fewest pairs this will fit a transform from, whatever survives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not three. Three pairs determine six unknowns exactly, so they reproduce
    /// themselves whatever they say and their residual is zero by construction —
    /// the failure this whole path was rewritten to stop reporting. Six leaves
    /// three degrees of freedom per axis, so the residual is measuring something
    /// the solve was not handed.
    /// </para>
    /// <para>
    /// Measured, not chosen for symmetry: a run that collected five pairs, dropped
    /// one as inconsistent and fitted the remaining four wrote a transform rotated
    /// by about thirty degrees with a reported residual of 0.74 of a tile. Four
    /// points nearly determine six unknowns, so the check had almost nothing left
    /// to check with, and a confident wrong answer is the one outcome this file
    /// exists to prevent.
    /// </para>
    /// </remarks>
    public const int MinimumFittedSamples = 6;

    /// <summary>
    /// How far from the centre the probe pixels are placed, per axis, as a
    /// fraction of the client area.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why not a circle.</b> This was one fraction of the shorter side, which
    /// draws a circle, and a circle is the wrong shape for two reasons at once. The
    /// window is wider than it is tall, so a circle on the shorter side wastes the
    /// width; and a map tile is about twice as wide as it is high, so the same
    /// number of pixels sideways buys half as many tiles. Together they made the
    /// horizontal reach about four tiles, and four tiles is not enough to measure a
    /// tile against when each reading carries a whole one of slip. Measured on the
    /// live client: eleven of twelve clicks walked, every pair agreed with the
    /// others, and the tile size still came out uncertain by 8% and 18% - refused,
    /// correctly, for want of reach rather than for want of care.
    /// </para>
    /// <para>
    /// <b>Why these fractions.</b> Far enough to span about ten tiles across and
    /// fifteen down, which simulates at around two per cent against a five per cent
    /// bar; near enough to stay inside the rendered world and clear of the
    /// interface drawn round its edges. At 1024x768 the probes run from 154 to 870
    /// across and 169 to 599 down, which leaves the quickbar along the bottom and
    /// the minimap in its corner untouched. A click on the interface is not a click
    /// on the map and produces no walk at all.
    /// </para>
    /// </remarks>
    private const double ProbeRadiusFractionX = 0.35;
    private const double ProbeRadiusFractionY = 0.28;

    /// <summary>
    /// A second, tighter ring was tried here and removed.
    /// </summary>
    /// <remarks>
    /// The idea was that the long walks were the unreliable ones, so half the
    /// probes should be short. Simulated against the quantisation the method
    /// actually carries, the near ring makes the answer worse rather than safer:
    /// it contributes offsets of two or three tiles, where a whole tile of slip is
    /// most of the reading, and it costs a far probe that would have contributed
    /// ten. Spread is what determines the scale, and there is no cheap substitute
    /// for it. The reliability problem is real, but it belongs to what the client
    /// writes into WalkTarget, not to where the probes are placed.
    /// </remarks>

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
            if (samples.Count < MinimumFittedSamples)
            {
                Console.WriteLine(
                    $"[REFUSED] not_enough_samples:{samples.Count}_of_{MinimumFittedSamples}");
                Console.WriteLine("  Too many clicks walked nowhere. Stand in open ground, away from walls,");
                Console.WriteLine("  portals and monsters, and run it again.");
                return 1;
            }

            // The scale the fit is being made at. Read from the window rather than
            // assumed, and stored beside the client size, because the two together are
            // the shape a later projection has to still be looking at.
            GeometryEpoch epoch = GeometryEpoch.Read(window.Handle);

            if (!Solve(samples, area, out ScreenProjectionCalibration calibration,
                    out int dropped, out string? solveFailure, clientDpi: epoch.Dpi))
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
                Console.WriteLine(
                    $"  {dropped} rejected as inconsistent with the rest — a click that hit an obstacle");
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
                + $" fitted over {samples.Count - dropped} pairs with"
                + $" {calibration.VerifiedAgainstSamples} to spare."));
            Console.WriteLine($"  {path}");
            Console.WriteLine("  Valid at this client size. Resize the window or go full screen and it is");
            Console.WriteLine("  refused by name — run this again and it recalibrates itself.");

            // The regime is a property of how this process was launched, so the one
            // moment it can usefully be said is the moment it gets written into a
            // file that will outlive the command.
            Console.WriteLine(calibration.ClientDpi == 0
                ? "  Window DPI: UNKNOWN (not recorded, so a scale change cannot be detected)."
                : $"  Window DPI {calibration.ClientDpi}. A different one is refused by name.");
            Console.WriteLine(
                $"  Estimated under DPI awareness {calibration.Regime} ({calibration.Regime.ToWire()}).");
            Console.WriteLine(
                "  A calibration is refused under a different regime, and the regime depends on");
            Console.WriteLine(
                "  the command: NosAi.Runtime.exe reports PerMonitorV2, dotnet NosAi.Runtime.dll");
            Console.WriteLine(
                "  reports PerMonitor. Act with the same command you calibrated with.");
            return 0;
        }
    }

    /// <summary>
    /// Fits the largest set of samples that agree with each other, never fewer
    /// than <see cref="MinimumFittedSamples"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A bad pair is expected rather than exceptional: a click can land on an
    /// obstacle and walk somewhere other than where it was aimed. Refitting without
    /// it keeps an unattended run from failing on one unlucky click.
    /// </para>
    /// <para>
    /// <b>What makes that safe is the floor, and it was missing.</b> The earlier
    /// version dropped one and refitted whatever was left, down to three pairs.
    /// Dropping samples until a fit passes is fitting the noise, and the fewer that
    /// remain the more certainly it succeeds: on the real client it dropped one of
    /// five, fitted four, and wrote a transform rotated by thirty degrees that
    /// reported a residual of less than a tile. So the search runs the other way
    /// round — it prefers the <i>largest</i> agreeing set and refuses to go below
    /// six, where three degrees of freedom per axis are still left to disagree.
    /// </para>
    /// </remarks>
    internal static bool Solve(
        IReadOnlyList<ScreenProjectionSample> samples,
        PixelRect area,
        out ScreenProjectionCalibration calibration,
        out int dropped,
        out string? failureReason,
        uint clientDpi = 0)
    {
        dropped = 0;
        calibration = ScreenProjectionCalibration.Uncalibrated;
        failureReason = null;

        if (samples.Count < MinimumFittedSamples)
        {
            failureReason = $"not_enough_samples:{samples.Count}_of_{MinimumFittedSamples}";
            return false;
        }

        DateTime at = DateTime.UtcNow;

        // Largest first, so a set is only narrowed when the wider one genuinely
        // disagrees. Among sets of the same size the one whose worst sample lands
        // closest to its prediction wins.
        for (int size = samples.Count; size >= MinimumFittedSamples; size--)
        {
            ScreenProjectionCalibration? best = null;
            string? firstFailure = null;

            foreach (int[] subset in Subsets(samples.Count, size))
            {
                var candidateSamples = new List<ScreenProjectionSample>(size);
                foreach (int index in subset)
                    candidateSamples.Add(samples[index]);

                if (!ScreenProjectionCalibration.TrySolve(
                        candidateSamples, area.Width, area.Height, at,
                        out ScreenProjectionCalibration candidate, out string? why,
                        clientDpi: clientDpi))
                {
                    firstFailure ??= why;
                    continue;
                }

                if (best is null || candidate.WorstResidualPixels < best.WorstResidualPixels)
                    best = candidate;
            }

            if (best is not null)
            {
                calibration = best;
                dropped = samples.Count - size;
                failureReason = null;
                return true;
            }

            if (firstFailure is not null)
                failureReason = firstFailure;
        }

        failureReason ??= "samples_disagree";
        return false;
    }

    /// <summary>Every choice of <paramref name="size"/> indices out of <paramref name="count"/>.</summary>
    /// <remarks>
    /// Bounded by construction: <see cref="SampleCount"/> is twelve and the floor
    /// is six, so the widest search is a few hundred solves of a three by three
    /// system.
    /// </remarks>
    private static IEnumerable<int[]> Subsets(int count, int size)
    {
        var chosen = new int[size];

        IEnumerable<int[]> Choose(int next, int depth)
        {
            if (depth == size)
            {
                yield return chosen;
                yield break;
            }

            for (int i = next; i <= count - (size - depth); i++)
            {
                chosen[depth] = i;
                foreach (int[] result in Choose(i + 1, depth + 1))
                    yield return result;
            }
        }

        return Choose(0, 0);
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
        double radiusX = area.Width * ProbeRadiusFractionX;
        double radiusY = area.Height * ProbeRadiusFractionY;

        for (var i = 0; i < count; i++)
        {
            double angle = ((2 * Math.PI * i) / count) + (Math.PI / 8);
            points.Add((
                (int)Math.Round(centreX + (radiusX * Math.Cos(angle))),
                (int)Math.Round(centreY + (radiusY * Math.Sin(angle)))));
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
