using System.Buffers.Binary;
using System.Net;
using NosAi.LiveIntegration.Capture;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Demonstrates that the wide prelude respects its byte budget, drops packets older than the time
/// window, and, once focused on an endpoint, never lets the traffic of another endpoint be read.
/// </summary>
public sealed class BroadWirePreludeTests
{
    private static readonly IPAddress Server = IPAddress.Parse("79.110.84.175");
    private const int ServerPort = 4006;
    private static readonly IPAddress Client = IPAddress.Parse("192.168.0.4");
    private const int ClientPort = 56027;
    private static readonly IPAddress OtherServer = IPAddress.Parse("203.0.113.9");
    private const int OtherPort = 9999;
    private const int OtherClientPort = 40000;
    private static readonly DateTime At = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Builds a minimal IPv4/TCP packet: 0x45 version/IHL byte, total length, protocol 6, source and
    /// destination addresses, then a TCP header with the given ports, sequence number, a data offset
    /// of five 32-bit words and an optional SYN flag.
    /// </summary>
    private static byte[] TcpPacket(
        IPAddress source,
        int sourcePort,
        IPAddress destination,
        int destinationPort,
        uint sequence,
        ReadOnlySpan<byte> body,
        bool syn = false)
    {
        var packet = new byte[20 + 20 + body.Length];
        packet[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), (ushort)packet.Length);
        packet[9] = 6;
        source.GetAddressBytes().CopyTo(packet.AsSpan(12, 4));
        destination.GetAddressBytes().CopyTo(packet.AsSpan(16, 4));

        var tcp = packet.AsSpan(20);
        BinaryPrimitives.WriteUInt16BigEndian(tcp[..2], (ushort)sourcePort);
        BinaryPrimitives.WriteUInt16BigEndian(tcp.Slice(2, 2), (ushort)destinationPort);
        BinaryPrimitives.WriteUInt32BigEndian(tcp.Slice(4, 4), sequence);
        tcp[12] = 5 << 4;
        if (syn)
        {
            tcp[13] = 0x02;
        }

        body.CopyTo(tcp[20..]);
        return packet;
    }

    /// <summary>
    /// Builds a deterministic filler body of the requested length; each byte holds (byte)(i % 251).
    /// </summary>
    private static byte[] Filler(int length)
    {
        var body = new byte[length];
        for (var i = 0; i < length; i++)
        {
            body[i] = (byte)(i % 251);
        }

        return body;
    }

    /// <summary>
    /// The budget is measured in bytes, so the wide buffer can never exceed it; packets in excess of
    /// the budget leave from the head and are counted as capacity drops.
    /// </summary>
    [Fact]
    public void Capacity_evictions_keep_the_wide_buffer_within_its_byte_budget()
    {
        const int capacity = 4096;
        using var prelude = new BroadWirePrelude(capacityBytes: capacity, clock: () => At);

        for (var i = 0; i < 10; i++)
        {
            prelude.Accept(new CapturedPacket(At, TcpPacket(Server, ServerPort, Client, ClientPort, (uint)(1000 + i * 1040), Filler(1000))));
        }

        Assert.True(prelude.DroppedForCapacity > 0);
        Assert.True(prelude.BufferedBytes <= capacity);
        Assert.Equal(0L, prelude.DroppedForAge);
        Assert.Equal(10L, prelude.PacketsSeen);
    }

    /// <summary>
    /// The age window is exercised by moving the injectable clock, never by waiting for real time.
    /// </summary>
    [Fact]
    public void Packets_older_than_the_window_leave_the_wide_buffer()
    {
        DateTime now = At;
        using var prelude = new BroadWirePrelude(window: TimeSpan.FromSeconds(1), clock: () => now);

        prelude.Accept(new CapturedPacket(At, TcpPacket(Server, ServerPort, Client, ClientPort, 1000, Filler(16))));
        prelude.Accept(new CapturedPacket(At.AddMilliseconds(1), TcpPacket(Server, ServerPort, Client, ClientPort, 1001, Filler(16))));

        now = At.AddSeconds(30);
        prelude.Accept(new CapturedPacket(now, TcpPacket(Server, ServerPort, Client, ClientPort, 1002, Filler(16))));

        Assert.True(prelude.DroppedForAge >= 2);
        Assert.Equal(0L, prelude.DroppedForCapacity);
        Assert.Equal(1, prelude.BufferedPackets);
        Assert.Equal(3L, prelude.PacketsSeen);
    }

    /// <summary>
    /// This is the live-phase filter, the one that discards foreign traffic while recording goes on,
    /// distinct from the trimming that happens inside FocusOn.
    /// </summary>
    [Fact]
    public void After_focus_a_packet_of_another_endpoint_is_never_readable()
    {
        using var prelude = new BroadWirePrelude(clock: () => At);

        prelude.Accept(new CapturedPacket(At, TcpPacket(Server, ServerPort, Client, ClientPort, 1000, Filler(8))));

        PreludeSlice slice = prelude.FocusOn(Server, ServerPort);
        Assert.Single(slice.Packets);

        var foreign = new CapturedPacket(At.AddMilliseconds(1), TcpPacket(OtherServer, OtherPort, Client, OtherClientPort, 7000, Filler(8)));
        var mine = new CapturedPacket(At.AddMilliseconds(2), TcpPacket(Server, ServerPort, Client, ClientPort, 2000, Filler(8)));

        prelude.Accept(foreign);
        prelude.Accept(mine);

        Assert.True(prelude.TryRead(TimeSpan.FromMilliseconds(50), out CapturedPacket read));
        Assert.Equal(mine.Raw.ToArray(), read.Raw.ToArray());
        Assert.False(prelude.TryRead(TimeSpan.FromMilliseconds(20), out _));
        Assert.Equal(0L, prelude.DroppedForCapacity);
        Assert.Equal(3L, prelude.PacketsSeen);
    }
}
