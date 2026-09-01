using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NosAi.Runtime.Perception;

/// <summary>
/// The part of a window's geometry that survives being written to a file: what
/// decides the <i>shape</i> of a measured transform, and nothing that identifies one
/// session's window.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is separate from <see cref="GeometryEpoch"/>.</b> An epoch names one
/// window at one moment, and two of its components — the window handle and the
/// monitor handle — are meaningless outside the session that read them. A handle
/// stored in a file would never match again, so a calibration that carried a whole
/// epoch would be refused on every restart, which is a check that fires always and
/// therefore checks nothing. The epoch is for comparing an instant against another
/// instant; this is for comparing today's window against a fit made last week.
/// </para>
/// <para>
/// The DPI is here and the position is not, and that division is the point: a window
/// that moves keeps its transform — <see cref="CalibratedScreenProjection"/> adds the
/// client origin at use, so movement is already handled — while a window whose DPI
/// changed is being drawn at a different size per tile even when the rectangle
/// happens to report the same numbers.
/// </para>
/// </remarks>
/// <param name="Width">Client width in the reader's own pixel unit.</param>
/// <param name="Height">Client height in the same unit.</param>
/// <param name="Dpi">The window's DPI, or zero when it could not be read.</param>
public readonly record struct GeometryShape(int Width, int Height, uint Dpi)
{
    /// <summary>False when any component is missing. Unknown is not a shape.</summary>
    public bool IsKnown => Width > 0 && Height > 0 && Dpi > 0;
}

/// <summary>
/// A geometry epoch together with the moment it was taken: what an
/// <c>ActionEnvelope</c> carries from authorisation to the commit point.
/// </summary>
/// <remarks>
/// <para>
/// <b>How it travels.</b> The envelope carries one of these as a prerequisite
/// (<c>docs/CONTROLLO_PERSONAGGIO_ARCHITETTURA.md</c> § 4 — "versionato, con scadenza
/// e prerequisiti"). It is <see cref="Take"/>n once, where the action is authorised
/// and its screen coordinate is computed, and it is <b>never refreshed</b> while the
/// envelope is in flight. Refreshing it would make it agree with itself at every
/// moment, which is the one behaviour that turns this check into decoration: the
/// value has to be the geometry as it was <i>then</i>, or there is nothing for the
/// commit point to disagree with.
/// </para>
/// <para>
/// <b>Why the instant is in here.</b> "Unchanged since authorisation" is a claim
/// about an interval, and an interval needs both ends. The same instant is what
/// § 2.1 requires to be measured and recorded — the delay between the last check and
/// the emission, aborted past a declared threshold — so carrying it once serves both
/// and keeps the envelope from holding two clocks that could disagree.
/// </para>
/// <para>
/// It is a value, so the envelope copies it and nothing can mutate a stamp after the
/// authorisation that produced it.
/// </para>
/// </remarks>
/// <param name="Epoch">The geometry as it stood when the action was authorised.</param>
/// <param name="TakenAtUtc">When that reading was taken.</param>
public readonly record struct GeometryStamp(GeometryEpoch Epoch, DateTimeOffset TakenAtUtc)
{
    /// <summary>Reported when the stamp is older than the caller is willing to act on.</summary>
    public const string StaleReason = "geometry_stamp_stale";

    /// <summary>Reads the geometry now and stamps it with the current instant.</summary>
    public static GeometryStamp Take(IntPtr window, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return new GeometryStamp(GeometryEpoch.Read(window), clock.GetUtcNow());
    }

    /// <summary>Whether anything was read at all.</summary>
    public bool IsKnown => Epoch.IsKnown;

    /// <summary>
    /// The first condition of the commit point: the geometry has not changed since
    /// this stamp was taken, and the stamp is not older than <paramref name="maxAge"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Age is judged as well as identity, because the comparison alone has a blind
    /// spot: a window dragged away and dropped back on the same pixel compares equal,
    /// and everything that happened in between happened unobserved. The comparison
    /// bounds how much change an act may carry; the age bounds how much unobserved
    /// time it may carry. Neither substitutes for the other.
    /// </para>
    /// <para>
    /// There is no zero-risk window here and this does not pretend otherwise. § 2.1:
    /// there must be a <i>measured</i> risk window, and <paramref name="age"/> is that
    /// measurement, returned whether the check passes or fails so a caller can record
    /// it either way.
    /// </para>
    /// </remarks>
    public bool StillCurrent(
        TimeProvider clock,
        TimeSpan maxAge,
        out string? refusalReason,
        out TimeSpan age)
    {
        ArgumentNullException.ThrowIfNull(clock);

        age = clock.GetUtcNow() - TakenAtUtc;

        if (!GeometryEpoch.Unchanged(Epoch, GeometryEpoch.Read(Epoch.Window), out string? changed))
        {
            refusalReason = changed;
            return false;
        }

        // Negative is a clock that moved backwards, and an interval that cannot be
        // measured is not an interval that is short.
        if (age < TimeSpan.Zero || age > maxAge)
        {
            refusalReason =
                $"{StaleReason}:{age.TotalMilliseconds:F0}ms_of_{maxAge.TotalMilliseconds:F0}ms";
            return false;
        }

        refusalReason = null;
        return true;
    }
}

/// <summary>
/// What the commit point compares: the identity of the window geometry an action was
/// authorised against, re-readable at any instant and owned by nobody.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem it answers.</b> <c>SendInput</c> does not address a window, it goes
/// to whatever has focus, and the coordinate it carries was computed from where the
/// window was when the action was authorised. Between authorisation and emission the
/// operator can move the window, resize it, drag it to a monitor at another scale, or
/// change that monitor's scale. Every one of those makes an authorised coordinate
/// point somewhere nobody chose, and none of them is loud:
/// <c>docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md</c> § 2.1 makes "geometry epoch
/// unchanged since authorisation" the first condition of the commit, and § 6.3
/// records that there was no such value to compare.
/// </para>
/// <para>
/// <b>Nobody owns it, and that is the fix rather than a gap.</b> The two candidates
/// for an owner both failed, and for opposite reasons that turn out to be the same
/// reason. <see cref="ClientWindowLocator"/> is stateless: it re-reads and returns a
/// fresh rectangle every call and keeps no previous one, so it has nothing to compare.
/// <c>Win32ProcessAdapter</c> does keep geometry — in a field written once at attach
/// and never updated — so it has something to compare and the thing it has is wrong.
/// A stored geometry is stale from the moment the window moves, and the interval this
/// has to protect is measured in milliseconds, so no refresh rate would fix it either.
/// </para>
/// <para>
/// So this is <b>derived, never maintained</b>: a value read out of the window on
/// demand, with no counter, no owner and no cache. A derived value cannot go stale.
/// The only thing anyone holds is a <i>copy taken at a named moment</i> — the one the
/// envelope carries — and holding that copy is the whole mechanism: the commit point
/// re-reads and compares.
/// </para>
/// <para>
/// <b>It is deliberately not a counter.</b> A monotonic epoch number would need
/// someone watching every change to increment it, and a change that arrives between
/// two observations would be invisible for exactly as long as it takes to matter.
/// Comparing the geometry itself has no such window: whatever happened in between,
/// if the geometry now differs from the geometry then, the comparison says so.
/// </para>
/// <para>
/// <b>Unknown never matches, including another Unknown.</b> A geometry that could not
/// be read is not evidence that the geometry is the same; two failed readings are not
/// agreement. This is DOMAIN-10 at the one point where getting it wrong emits a real
/// click.
/// </para>
/// </remarks>
/// <param name="Window">The window that renders. A different handle is a different geometry outright.</param>
/// <param name="ClientArea">Client area in screen pixels: position and size together.</param>
/// <param name="Dpi">The window's DPI. Catches a scale change that leaves the rectangle alone.</param>
/// <param name="Monitor">The monitor it sits on.</param>
public readonly record struct GeometryEpoch(IntPtr Window, PixelRect ClientArea, uint Dpi, IntPtr Monitor)
{
    /// <summary>The geometry could not be read. Matches nothing, including itself.</summary>
    public static GeometryEpoch Unknown => default;

    /// <summary>Reported when either side of a comparison was never read.</summary>
    public const string UnknownReason = "geometry_epoch_unknown";

    /// <summary>Reported when the window that renders is not the same window.</summary>
    public const string WindowChangedReason = "geometry_window_changed";

    /// <summary>Reported when the client area changed size.</summary>
    public const string ResizedReason = "geometry_window_resized";

    /// <summary>Reported when the client area changed position without changing size.</summary>
    public const string MovedReason = "geometry_window_moved";

    /// <summary>Reported when the window's DPI changed.</summary>
    public const string DpiChangedReason = "geometry_dpi_changed";

    /// <summary>Reported when the window is on a different monitor.</summary>
    public const string MonitorChangedReason = "geometry_monitor_changed";

    /// <summary>False when the geometry could not be read.</summary>
    public bool IsKnown =>
        Window != IntPtr.Zero && Dpi > 0 && ClientArea.Width > 0 && ClientArea.Height > 0;

    /// <summary>The part of this that a calibration can store and compare against later.</summary>
    public GeometryShape Shape => new(ClientArea.Width, ClientArea.Height, Dpi);

    /// <summary>
    /// Reads the geometry of a window now.
    /// </summary>
    /// <remarks>
    /// Every component is required. A window whose DPI or monitor cannot be read is
    /// reported <see cref="Unknown"/> rather than partially: a partial epoch would
    /// compare equal on the components that were readable, which is agreement about
    /// the wrong thing.
    /// </remarks>
    public static GeometryEpoch Read(IntPtr window)
    {
        if (!OperatingSystem.IsWindows() || window == IntPtr.Zero)
            return Unknown;

        return ReadWindows(window);
    }

    /// <inheritdoc cref="Read(IntPtr)"/>
    public static GeometryEpoch Read(ClientWindow? window) =>
        window is null ? Unknown : Read(window.Handle);

    /// <summary>
    /// Whether the geometry is still the one <paramref name="stamped"/> was taken from,
    /// and if not, which part of it moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first condition of the commit point (§ 2.1). It is a comparison and not a
    /// subscription, which is what lets it be evaluated in the instant before the
    /// irreversible step rather than at whatever rate something else polls.
    /// </para>
    /// <para>
    /// The reason names the component, most structural first, because the four have
    /// different remedies: a changed window means the client was restarted under the
    /// runtime and the whole session is stale, while a move means only that the
    /// coordinate needs recomputing.
    /// </para>
    /// </remarks>
    public static bool Unchanged(in GeometryEpoch stamped, in GeometryEpoch current, out string? changeReason)
    {
        // Unknown on either side is a refusal. Two unread geometries comparing equal
        // would be the one case where this check passes by knowing nothing.
        if (!stamped.IsKnown || !current.IsKnown)
        {
            changeReason = UnknownReason;
            return false;
        }

        if (stamped.Window != current.Window)
        {
            changeReason = WindowChangedReason;
            return false;
        }

        if (stamped.ClientArea.Width != current.ClientArea.Width
            || stamped.ClientArea.Height != current.ClientArea.Height)
        {
            changeReason =
                $"{ResizedReason}:{stamped.ClientArea.Width}x{stamped.ClientArea.Height}"
                + $"_to_{current.ClientArea.Width}x{current.ClientArea.Height}";
            return false;
        }

        // Before the monitor, because a scale changed in Settings moves the DPI
        // without moving the window, and naming the monitor there would be wrong.
        if (stamped.Dpi != current.Dpi)
        {
            changeReason = $"{DpiChangedReason}:{stamped.Dpi}_to_{current.Dpi}";
            return false;
        }

        if (stamped.Monitor != current.Monitor)
        {
            changeReason = MonitorChangedReason;
            return false;
        }

        if (stamped.ClientArea.X != current.ClientArea.X
            || stamped.ClientArea.Y != current.ClientArea.Y)
        {
            changeReason =
                $"{MovedReason}:{stamped.ClientArea.X},{stamped.ClientArea.Y}"
                + $"_to_{current.ClientArea.X},{current.ClientArea.Y}";
            return false;
        }

        changeReason = null;
        return true;
    }

    /// <summary>Re-reads this epoch's window and compares. The commit-point call.</summary>
    public bool StillCurrent(out string? changeReason) =>
        Unchanged(in this, Read(Window), out changeReason);

    [SupportedOSPlatform("windows")]
    private static GeometryEpoch ReadWindows(IntPtr window)
    {
        if (!GetClientRect(window, out Rect client))
            return Unknown;

        int width = client.Right - client.Left;
        int height = client.Bottom - client.Top;
        if (width <= 0 || height <= 0)
            return Unknown;

        // The client rectangle is window-relative; the act is emitted in screen
        // coordinates, so the origin has to be the one the click will use.
        var origin = new Point { X = 0, Y = 0 };
        if (!ClientToScreen(window, ref origin))
            return Unknown;

        uint dpi = GetDpiForWindow(window);
        if (dpi == 0)
            return Unknown;

        IntPtr monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return Unknown;

        return new GeometryEpoch(
            window,
            new PixelRect(origin.X, origin.Y, width, height),
            dpi,
            monitor);
    }

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr handle, out Rect rect);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr handle, ref Point point);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr handle);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
}
