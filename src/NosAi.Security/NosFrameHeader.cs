using System.Runtime.InteropServices;

namespace NosAi.Security;

/// <summary>
/// The 12-byte header of the Gate 1 wire frame (docs/ROADMAP_ESECUTIVA.md S:2.2).
/// This is an in-memory, native-endianness view of a decoded frame; it is never
/// blitted directly to or from the wire. <see cref="FrameCodec"/> always reads
/// and writes each field explicitly as big-endian (INV-07's zero-allocation
/// goal does not license reinterpreting bytes in whatever order the local CPU
/// happens to use).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = Size)]
public readonly struct NosFrameHeader
{
    /// <summary>Fixed protocol version. Any other value is rejected before payload processing.</summary>
    public const byte CurrentVersion = 0x01;

    /// <summary>Header size on the wire, in bytes.</summary>
    public const int Size = 12;

    /// <summary>Payload length limit: frames declaring more are discarded before any allocation.</summary>
    public const int MaxPayloadLength = 4096;

    public readonly byte Version;
    public readonly byte OpCode;
    public readonly ushort Length;
    public readonly uint Sequence;
    public readonly uint Tag;

    public NosFrameHeader(byte version, byte opCode, ushort length, uint sequence, uint tag)
    {
        Version = version;
        OpCode = opCode;
        Length = length;
        Sequence = sequence;
        Tag = tag;
    }
}
