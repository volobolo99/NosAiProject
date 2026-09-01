using System;
using System.Buffers;
using System.Buffers.Binary;

namespace NosAi.Runtime.Navigation;

/// <summary>
/// Reads one <c>.grid</c> file: little-endian width and height, then exactly that
/// many cells, row-major.
/// </summary>
/// <remarks>
/// <para>
/// The cells are copied once, from the caller's span into a buffer the grid then
/// owns. The input span is never written. A rented array may be longer than the
/// rectangle — <see cref="MapGrid"/> already accepts that — so the copy is not
/// followed by a slice, a second allocation, or a return to the pool that would
/// let the next renter overwrite a live grid.
/// </para>
/// <para>
/// A malformed file is a named refusal, never an exception. The vocabulary is
/// <see cref="MapGridFormat"/>; the tests that pin it are
/// <c>MapGridLoaderContractTests</c>.
/// </para>
/// </remarks>
public sealed class BinaryMapGridLoader : IMapGridLoader
{
    /// <inheritdoc />
    public bool TryLoad(int mapId, ReadOnlySpan<byte> fileBytes, out MapGrid grid, out string? failureReason)
    {
        grid = default;
        failureReason = null;

        if (fileBytes.Length < MapGridFormat.HeaderBytes)
        {
            failureReason = MapGridFormat.HeaderTruncated;
            return false;
        }

        int width = BinaryPrimitives.ReadUInt16LittleEndian(fileBytes);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(fileBytes.Slice(2));

        if (width == 0 || height == 0)
        {
            failureReason = MapGridFormat.EmptyRectangle;
            return false;
        }

        // The product is computed in 64-bit so 65535×65535 cannot wrap to a small
        // positive length that a truncated file would then appear to satisfy.
        long declaredCells = (long)width * height;
        if (declaredCells > MapGridFormat.MaxCells)
        {
            failureReason = MapGridFormat.RectangleImplausible;
            return false;
        }

        int payloadLength = fileBytes.Length - MapGridFormat.HeaderBytes;
        int cellCount = (int)declaredCells;

        if (payloadLength < cellCount)
        {
            failureReason = MapGridFormat.PayloadTruncated;
            return false;
        }

        if (payloadLength > cellCount)
        {
            failureReason = MapGridFormat.PayloadOversized;
            return false;
        }

        byte[] cells = ArrayPool<byte>.Shared.Rent(cellCount);
        fileBytes.Slice(MapGridFormat.HeaderBytes, cellCount).CopyTo(cells);

        grid = new MapGrid(mapId, width, height, cells);
        return true;
    }
}
