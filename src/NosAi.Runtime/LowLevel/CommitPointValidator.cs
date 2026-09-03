using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NosAi.Runtime.Perception;

namespace NosAi.Runtime.LowLevel;

/// <summary>
/// The desktop facts the commit point has to re-read, behind one seam so the decision
/// can be tested against stated answers rather than against whatever window happened
/// to be in front.
/// </summary>
public interface ICommitEnvironment
{
    /// <summary>The window that currently has the foreground, or zero.</summary>
    IntPtr ForegroundWindow();

    /// <summary>
    /// The top-level window under one exact screen pixel: <c>WindowFromPoint</c>
    /// walked up with <c>GetAncestor(GA_ROOT)</c>. Zero when there is none.
    /// </summary>
    IntPtr RootWindowFromPoint(int screenX, int screenY);

    /// <summary>
    /// Whether the window is cloaked, per the DWM attribute; null when the attribute
    /// could not be read.
    /// </summary>
    bool? IsCloaked(IntPtr window);

    /// <summary>The window's geometry now.</summary>
    GeometryEpoch ReadEpoch(IntPtr window);
}

/// <summary>What an action asks the commit point to authorise.</summary>
/// <param name="Stamp">
/// The geometry as it stood when the action was authorised, and when that was. Taken
/// once, never refreshed — see <see cref="GeometryStamp"/>.
/// </param>
/// <param name="ScreenX">The exact pixel the act will touch.</param>
/// <param name="ScreenY">The exact pixel the act will touch.</param>
/// <param name="Scale">
/// The <see cref="GeometryShape"/> the coordinate was computed under, from the
/// calibration that produced it. Its DPI is the fifth condition.
/// </param>
public readonly record struct CommitRequest(
    GeometryStamp Stamp,
    int ScreenX,
    int ScreenY,
    GeometryShape Scale);

/// <summary>
/// The verdict, and the measurement that has to accompany it either way.
/// </summary>
/// <param name="IsAuthorised">Whether the act may proceed.</param>
/// <param name="RefusalReason">Which condition failed, named. Null when authorised.</param>
/// <param name="ValidationDuration">How long the five checks themselves took.</param>
/// <param name="ValidatedAtTimestamp">
/// <see cref="Stopwatch.GetTimestamp"/> taken after the last check. What
/// <see cref="ElapsedSinceValidation"/> measures from, and the reason the risk window
/// is measured rather than assumed away.
/// </param>
public readonly record struct CommitDecision(
    bool IsAuthorised,
    string? RefusalReason,
    TimeSpan ValidationDuration,
    long ValidatedAtTimestamp)
{
    /// <summary>How long since the last check finished.</summary>
    public TimeSpan ElapsedSinceValidation =>
        ValidatedAtTimestamp == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(ValidatedAtTimestamp);
}

/// <summary>
/// The atomic revalidation immediately before the irreversible step.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md</c> § 2.1. <c>SendInput</c> does not
/// address a window — it goes to whatever holds focus — so confinement is not a
/// property of the API but one the pipeline builds, and this is where it is built.
/// Between the moment a click is authorised and the moment it leaves, the operator can
/// have moved the window, brought another application to the front, or put their hand
/// on the mouse. A click authorised on correct coordinates and emitted half a second
/// after the browser came forward is a click in the browser.
/// </para>
/// <para>
/// <b>Five conditions, and the fifth is not decoration.</b> The card names four. The
/// fifth answers the open question in § 7: <see cref="CalibratedScreenProjection"/>
/// skips its DPI comparison when either side is unreadable, which is right — not
/// knowing whether the scale moved is not knowing that it moved — but only because a
/// projection produces a pixel rather than an act. The guard that has to close on the
/// act is this one, so the scale the coordinate was computed under must be
/// <i>known</i> and must still be the live one. Without that, "skipped" would have
/// meant "proceeded", and DOMAIN-10 says unknown does not authorise a protected
/// action.
/// </para>
/// <para>
/// <b>Order.</b> Cheapest and most structural first, so a refusal names the fact
/// furthest upstream: geometry, then foreground, then the point, then the operator,
/// then the scale. All five are evaluated against readings taken inside one call, and
/// none of them is cached between calls.
/// </para>
/// </remarks>
public sealed class CommitPointValidator
{
    /// <summary>
    /// How long the act may lag behind the last check before it is abandoned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Where eight milliseconds comes from.</b> It is bounded from both sides by
    /// things that are not arbitrary. Above: a hand moving a window covers a visible
    /// distance in about fifty milliseconds, and a thread quantum on a client Windows
    /// scheduler is roughly fifteen to thirty — so a budget shorter than one quantum
    /// means that being descheduled at the wrong moment <i>aborts</i> the act instead
    /// of being absorbed silently, which is the outcome to prefer, because a thread
    /// that was not running for forty milliseconds genuinely does not know what
    /// happened to the window. Below: the five checks are a handful of Win32 calls
    /// and complete in tens of microseconds, so eight milliseconds is three orders of
    /// magnitude of headroom over the work it has to cover and will not fire on an
    /// ordinary path.
    /// </para>
    /// <para>
    /// A long garbage collection will trip it. That is the intent and not a nuisance
    /// to tune away: the pause is exactly the interval during which the checks stopped
    /// being true of anything.
    /// </para>
    /// <para>
    /// The window is never zero and this does not pretend otherwise — § 2.1 asks for a
    /// <i>measured</i> risk window, which is why
    /// <see cref="CommitDecision.ElapsedSinceValidation"/> is reported whether the act
    /// proceeds or not.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan DefaultMaxEmissionLatency = TimeSpan.FromMilliseconds(8);

    /// <summary>How recently a person must have acted for the runtime to stand down.</summary>
    /// <remarks>The card's default (§ 2.3). The operator's hand wins, and keeps winning for a while.</remarks>
    public static readonly TimeSpan DefaultCourtesyWindow = TimeSpan.FromMilliseconds(1500);

    public const string GeometryChangedPrefix = "commit_geometry_changed";
    public const string NotForegroundReason = "commit_window_not_foreground";
    public const string PointNotOursReason = "commit_point_not_session_window";
    public const string WindowCloakedReason = "commit_window_cloaked";
    public const string CloakUnknownReason = "commit_window_cloak_unknown";
    public const string HumanActiveReason = "commit_human_input_recent";
    public const string HumanUnknownReason = "commit_human_input_unknown";
    public const string ScaleUnknownReason = "commit_scale_unknown";
    public const string ScaleChangedReason = "commit_scale_changed";
    public const string LatencyExceededReason = "commit_emission_too_late";

    private readonly ICommitEnvironment _environment;
    private readonly IHumanInputMonitor _humanInput;

    public CommitPointValidator(
        ICommitEnvironment environment,
        IHumanInputMonitor humanInput,
        TimeSpan? courtesyWindow = null,
        TimeSpan? maxEmissionLatency = null)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _humanInput = humanInput ?? throw new ArgumentNullException(nameof(humanInput));
        CourtesyWindow = courtesyWindow ?? DefaultCourtesyWindow;
        MaxEmissionLatency = maxEmissionLatency ?? DefaultMaxEmissionLatency;
    }

    /// <summary>How recently a person must have acted for the runtime to stand down.</summary>
    public TimeSpan CourtesyWindow { get; }

    /// <summary>How long the act may lag behind the last check.</summary>
    public TimeSpan MaxEmissionLatency { get; }

    /// <summary>Decisions taken. Diagnostic.</summary>
    public long EvaluatedCount { get; private set; }

    /// <summary>Decisions that refused. Diagnostic.</summary>
    public long RefusedCount { get; private set; }

    /// <summary>The worst emission latency measured on an act that was allowed through.</summary>
    /// <remarks>
    /// Recorded so the risk window is a number someone can look at rather than a
    /// paragraph. It only ever grows.
    /// </remarks>
    public TimeSpan WorstEmissionLatency { get; private set; }

    /// <summary>The last decision, for the operator's report.</summary>
    public CommitDecision? LastDecision { get; private set; }

    /// <summary>
    /// The last refusal, kept through later authorised decisions.
    /// </summary>
    /// <remarks>
    /// <see cref="LastDecision"/> is whatever ran most recently, including a
    /// success. A halt dump has to photograph the last time the commit point
    /// said no, which is a different fact: a later authorised check does not
    /// erase the refusal that the breaker is about to halt on.
    /// </remarks>
    public CommitDecision? LastRefusal { get; private set; }

    /// <summary>
    /// Re-reads the world and decides whether the act may be emitted now.
    /// </summary>
    public CommitDecision Validate(in CommitRequest request)
    {
        long started = Stopwatch.GetTimestamp();
        EvaluatedCount++;

        string? refusal = Evaluate(in request);

        long finished = Stopwatch.GetTimestamp();
        var decision = new CommitDecision(
            refusal is null,
            refusal,
            Stopwatch.GetElapsedTime(started, finished),
            finished);

        if (refusal is not null)
        {
            RefusedCount++;
            LastRefusal = decision;
        }

        LastDecision = decision;
        return decision;
    }

    /// <summary>
    /// Whether an authorised decision is still fresh enough to act on, with the
    /// measurement either way.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Validate"/> because it answers about the interval
    /// <i>after</i> the checks, which is the interval the checks cannot cover. The
    /// caller evaluates it in the instant before it emits.
    /// </remarks>
    public bool MayEmit(in CommitDecision decision, out string? refusalReason, out TimeSpan latency)
    {
        latency = decision.ElapsedSinceValidation;

        if (!decision.IsAuthorised)
        {
            refusalReason = decision.RefusalReason;
            LastRefusal = decision;
            return false;
        }

        if (latency > MaxEmissionLatency)
        {
            RefusedCount++;
            refusalReason = string.Create(CultureInfo.InvariantCulture,
                $"{LatencyExceededReason}:{latency.TotalMilliseconds:F2}ms_of_{MaxEmissionLatency.TotalMilliseconds:F0}ms");
            LastRefusal = new CommitDecision(
                false,
                refusalReason,
                decision.ValidationDuration,
                decision.ValidatedAtTimestamp);
            return false;
        }

        if (latency > WorstEmissionLatency)
            WorstEmissionLatency = latency;

        refusalReason = null;
        return true;
    }

    private string? Evaluate(in CommitRequest request)
    {
        IntPtr session = request.Stamp.Epoch.Window;

        // 1. The geometry the coordinate was computed against is still the geometry.
        GeometryEpoch current = _environment.ReadEpoch(session);
        if (!GeometryEpoch.Unchanged(request.Stamp.Epoch, current, out string? changed))
            return $"{GeometryChangedPrefix}:{changed}";

        // 2. Input goes to whatever holds the foreground, so the foreground has to be us.
        if (_environment.ForegroundWindow() != session)
            return NotForegroundReason;

        // 3. The exact pixel, not the area. A small window over the click point passes
        //    an area test and intercepts the act anyway.
        if (_environment.RootWindowFromPoint(request.ScreenX, request.ScreenY) != session)
            return PointNotOursReason;

        // A cloaked window is composited away while still owning the point, so the
        // point test alone would pass on a window nobody can see. Unreadable is its
        // own refusal: not knowing whether it is hidden is not knowing it is visible.
        bool? cloaked = _environment.IsCloaked(session);
        if (cloaked is null)
            return CloakUnknownReason;
        if (cloaked.Value)
            return WindowCloakedReason;

        // 4. The operator's hand wins (DOMAIN-16). Null is a refusal, not an idle
        //    desk: a monitor that is not watching has not seen an absence of people.
        TimeSpan? sinceHuman = _humanInput.SinceLastHumanInput;
        if (!_humanInput.IsWatching)
            return HumanUnknownReason;
        if (sinceHuman is { } idle && idle < CourtesyWindow)
            return string.Create(CultureInfo.InvariantCulture,
                $"{HumanActiveReason}:{idle.TotalMilliseconds:F0}ms_of_{CourtesyWindow.TotalMilliseconds:F0}ms");

        // 5. The scale the coordinate was computed under is known and is still live.
        //    § 7's open question, answered here: the projection may skip an unreadable
        //    DPI because it produces a pixel; the act may not, because it is the act.
        if (!request.Scale.IsKnown)
            return ScaleUnknownReason;
        if (request.Scale.Dpi != current.Dpi)
            return $"{ScaleChangedReason}:{request.Scale.Dpi}_to_{current.Dpi}";

        return null;
    }
}

/// <summary>The real desktop.</summary>
public sealed partial class Win32CommitEnvironment : ICommitEnvironment
{
    public IntPtr ForegroundWindow() =>
        OperatingSystem.IsWindows() ? GetForegroundWindow() : IntPtr.Zero;

    public IntPtr RootWindowFromPoint(int screenX, int screenY)
    {
        if (!OperatingSystem.IsWindows())
            return IntPtr.Zero;

        nint hit = WindowFromPoint(new Point { X = screenX, Y = screenY });
        return hit == nint.Zero ? IntPtr.Zero : GetAncestor(hit, GaRoot);
    }

    public bool? IsCloaked(IntPtr window)
    {
        if (!OperatingSystem.IsWindows() || window == IntPtr.Zero)
            return null;

        try
        {
            int hr = DwmGetWindowAttribute(window, DwmwaCloaked, out int cloaked, sizeof(int));
            return hr < 0 ? null : cloaked != 0;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    public GeometryEpoch ReadEpoch(IntPtr window) => GeometryEpoch.Read(window);

    private const uint GaRoot = 2;
    private const int DwmwaCloaked = 14;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [SupportedOSPlatform("windows")]
    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [SupportedOSPlatform("windows")]
    [LibraryImport("user32.dll")]
    private static partial nint WindowFromPoint(Point point);

    [SupportedOSPlatform("windows")]
    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint window, uint flags);

    [SupportedOSPlatform("windows")]
    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
}
