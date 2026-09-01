using System.Runtime.InteropServices;
using NosAi.Core;

namespace NosAi.Adapter;

/// <summary>
/// The window's screen rectangle at the moment it was last read.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct WindowGeometry
{
    public readonly int X;
    public readonly int Y;
    public readonly int Width;
    public readonly int Height;

    public WindowGeometry(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}

/// <summary>
/// Attaches to, and reads raw bytes from, a real running process
/// (docs/ROADMAP_ESECUTIVA.md S:2.2). This is a mechanical byte source, not a
/// data classifier: per <c>.cursor/rules/25-connection-and-ban-risk.mdc</c>,
/// "a source that cannot detect its own errors is never LIVE" -- this
/// interface only ever answers "did the read succeed and how many bytes came
/// back", never "is this value trustworthy". Whatever consumes
/// <see cref="ReadRegion"/> (a future Perception layer) is where the
/// LIVE/DERIVED/UNKNOWN classification actually happens, against its own
/// validity checks (bounded range, continuity against the previous read),
/// not against anything this adapter asserts.
/// </summary>
public interface IGameProcessAdapter : IDisposable
{
    /// <summary>The OS process id once attached, or 0 before attach.</summary>
    int ProcessId { get; }

    bool IsAttached { get; }

    /// <summary>
    /// Attempts to attach to the process named in <paramref name="options"/>,
    /// after verifying its expected module's SHA-256 hash. Never throws for an
    /// expected failure (process absent, module mismatch, access denied):
    /// those return <see langword="false"/> with a specific <see cref="FaultCode"/>.
    /// </summary>
    bool TryAttach(in ProcessAttachOptions options, out FaultCode fault);

    /// <summary>
    /// Copies up to <c>destination.Length</c> bytes starting at
    /// <paramref name="address"/> in the attached process's address space.
    /// </summary>
    /// <returns>
    /// The number of bytes actually read. A failed read returns 0 -- never a
    /// partially-filled or stale buffer presented as if it were complete.
    /// </returns>
    int ReadRegion(nuint address, Span<byte> destination);

    /// <summary>The target's window rectangle, refreshed on each successful <see cref="TryAttach"/>.</summary>
    WindowGeometry Geometry { get; }
}
