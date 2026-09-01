using System;

namespace NosAi.Runtime.Navigation;

/// <summary>
/// Turns the bytes of one <c>.grid</c> file into a <see cref="MapGrid"/>, or refuses
/// and says why.
/// </summary>
/// <remarks>
/// <para>
/// <b>The format</b> (<c>docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md</c> § 3):
/// <c>uint16</c> little-endian width, <c>uint16</c> little-endian height, then
/// exactly width × height bytes, row-major, one per cell, with the bit meanings in
/// <see cref="MapCellFlags"/>.
/// </para>
/// <para>
/// <b>Refusing is a normal outcome, and it is never an exception.</b> A malformed
/// file is a thing that happens after a client patch, and it has to arrive at the
/// caller as a named reason it can log and act on. Throwing would make "the client
/// changed" indistinguishable from a bug, and the natural handler for that is a
/// catch that keeps going with no grid — which is the same as loading an empty one.
/// </para>
/// <para>
/// <b>Strict about length, where <see cref="MapGrid"/> is lenient.</b> The struct
/// accepts a buffer longer than the rectangle so a loader can hand over a pooled
/// array without slicing it. A <i>file</i> longer than its rectangle is a different
/// thing: the parse and the file disagree about the format, and the one explanation
/// that must not be assumed is the convenient one. Refuse it.
/// </para>
/// <para>
/// The implementation is expected to satisfy every case in
/// <c>MapGridLoaderContractTests</c>. That class is the specification; this
/// interface is only its shape.
/// </para>
/// </remarks>
public interface IMapGridLoader
{
    /// <summary>Parses one grid file.</summary>
    /// <param name="mapId">
    /// The map the file is for, taken from its name. The format carries no id of its
    /// own, so this is the caller's to supply and the loader's to record.
    /// </param>
    /// <param name="fileBytes">The file exactly as stored. Must not be modified.</param>
    /// <param name="grid">
    /// The parsed grid on success. On failure it must be <c>default</c>, so that a
    /// caller which ignores the return value gets a grid that blocks everything
    /// rather than one that is half-filled.
    /// </param>
    /// <param name="failureReason">
    /// A short stable token on failure — the vocabulary is in
    /// <see cref="MapGridFormat"/> — and null on success.
    /// </param>
    bool TryLoad(int mapId, ReadOnlySpan<byte> fileBytes, out MapGrid grid, out string? failureReason);
}

/// <summary>The constants and refusal vocabulary of the <c>.grid</c> format.</summary>
/// <remarks>
/// Named here rather than left as literals in the loader so that the tests and the
/// implementation cannot drift into disagreeing about what the format is, and so a
/// refusal reason is a token the caller can match on rather than a sentence someone
/// reworded.
/// </remarks>
public static class MapGridFormat
{
    /// <summary>Bytes before the cells: two <c>uint16</c> little-endian.</summary>
    public const int HeaderBytes = 4;

    /// <summary>The largest either dimension can express.</summary>
    public const int MaxDimension = ushort.MaxValue;

    /// <summary>
    /// The most cells a grid may declare before it is refused as implausible.
    /// </summary>
    /// <remarks>
    /// A header may declare 65535 × 65535, which is over four billion cells: past
    /// <see cref="int.MaxValue"/>, impossible to allocate, and reachable from four
    /// bytes of a corrupted file. The ceiling is generous against any real map and
    /// still refuses that in arithmetic rather than in an allocation that fails.
    /// </remarks>
    public const int MaxCells = 64 * 1024 * 1024;

    /// <summary>Fewer than <see cref="HeaderBytes"/> bytes: there is no header.</summary>
    public const string HeaderTruncated = "grid_header_truncated";

    /// <summary>The payload is shorter than the rectangle the header declares.</summary>
    public const string PayloadTruncated = "grid_payload_truncated";

    /// <summary>The payload is longer than the rectangle the header declares.</summary>
    public const string PayloadOversized = "grid_payload_oversized";

    /// <summary>A width or height of zero. A map with no cells is not a map.</summary>
    public const string EmptyRectangle = "grid_empty_rectangle";

    /// <summary>The declared rectangle is larger than <see cref="MaxCells"/>.</summary>
    public const string RectangleImplausible = "grid_rectangle_implausible";
}
