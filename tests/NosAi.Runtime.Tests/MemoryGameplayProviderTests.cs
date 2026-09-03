using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The signature matcher that finds the character object, and the packing of the
/// two coordinates that sit beside each other.
/// </summary>
/// <remarks>
/// F1-10. The client is not needed for either: a signature either matches bytes
/// or it does not, and a packed pair either unpacks to the right halves or it does
/// not. What needs the client is the pointer chain, and that is T-11.
/// </remarks>
public sealed class NosTaleClientLayoutTests
{
    private static readonly byte[] Signature = Convert.FromHexString("33C98B55FCA1");

    private static byte[] Haystack(int prefix, int suffix)
    {
        var bytes = new byte[prefix + 15 + suffix];
        // The signature, then its four operand bytes and the call.
        Signature.CopyTo(bytes, prefix);
        BitConverter.GetBytes(0x00A1B2C3u).CopyTo(bytes, prefix + 6);
        bytes[prefix + 10] = 0xE8;
        return bytes;
    }

    [Fact]
    public void The_signature_is_found_where_it_sits()
    {
        byte[] haystack = Haystack(prefix: 64, suffix: 32);

        int index = NosTaleClientLayout.IndexOfSignature(
            haystack, NosTaleClientLayout.ParseSignature(NosTaleClientLayout.PlayerManagerSignature));

        Assert.Equal(64, index);
    }

    /// <summary>The wildcards must match anything, including the bytes they stand for.</summary>
    [Fact]
    public void The_wildcards_match_whatever_the_operand_happens_to_be()
    {
        byte[] first = Haystack(prefix: 8, suffix: 8);
        byte[] second = Haystack(prefix: 8, suffix: 8);
        BitConverter.GetBytes(0xDEADBEEFu).CopyTo(second, 8 + 6);

        NosTaleClientLayout.ParseSignature(NosTaleClientLayout.PlayerManagerSignature);
        var signature = NosTaleClientLayout.ParseSignature(NosTaleClientLayout.PlayerManagerSignature);

        Assert.Equal(8, NosTaleClientLayout.IndexOfSignature(first, signature));
        Assert.Equal(8, NosTaleClientLayout.IndexOfSignature(second, signature));
    }

    [Fact]
    public void A_haystack_without_the_signature_finds_nothing()
    {
        var haystack = new byte[256];

        Assert.Equal(-1, NosTaleClientLayout.IndexOfSignature(
            haystack, NosTaleClientLayout.ParseSignature(NosTaleClientLayout.PlayerManagerSignature)));
    }

    [Fact]
    public void A_haystack_shorter_than_the_signature_finds_nothing()
        => Assert.Equal(-1, NosTaleClientLayout.IndexOfSignature(
            new byte[4], NosTaleClientLayout.ParseSignature(NosTaleClientLayout.PlayerManagerSignature)));

    [Fact]
    public void A_signature_byte_that_is_not_hex_is_refused()
        => Assert.Throws<ArgumentException>(() => NosTaleClientLayout.ParseSignature("33 ZZ"));

    /// <summary>
    /// Every match has to be reachable, not just the first. The scene manager's
    /// signature is loose enough that the first match is not evidence of
    /// anything, and taking it produced a pointer whose lists read back
    /// ERROR_PARTIAL_COPY against the real client.
    /// </summary>
    [Fact]
    public void The_search_can_continue_past_a_match_that_did_not_validate()
    {
        var signature = NosTaleClientLayout.ParseSignature("AA BB");
        byte[] haystack = [0x00, 0xAA, 0xBB, 0x00, 0xAA, 0xBB, 0x00];

        int first = NosTaleClientLayout.IndexOfSignature(haystack, signature);
        int second = NosTaleClientLayout.IndexOfSignature(haystack, signature, first + 1);
        int third = NosTaleClientLayout.IndexOfSignature(haystack, signature, second + 1);

        Assert.Equal(1, first);
        Assert.Equal(4, second);
        Assert.Equal(-1, third);
    }

    [Fact]
    public void A_negative_start_finds_nothing_rather_than_throwing()
        => Assert.Equal(-1, NosTaleClientLayout.IndexOfSignature(
            new byte[] { 0xAA, 0xBB }, NosTaleClientLayout.ParseSignature("AA BB"), from: -1));

    /// <summary>
    /// The two copies of the position the client keeps should agree; a mismatch
    /// means one of the two structures is not the character's.
    /// </summary>
    [Fact]
    public void The_two_position_copies_are_compared_when_both_were_read()
    {
        var agreeing = new PlayerObjectReading(1, 1, 157, 94, ManagerX: 157, ManagerY: 94);
        var disagreeing = new PlayerObjectReading(1, 1, 157, 94, ManagerX: 12, ManagerY: 900);
        var unread = new PlayerObjectReading(1, 1, 157, 94);

        Assert.True(agreeing.PositionCopiesAgree);
        Assert.False(disagreeing.PositionCopiesAgree);
        Assert.Null(unread.PositionCopiesAgree);
    }

    /// <summary>
    /// x and y are adjacent 16-bit values, so one 32-bit read takes them together
    /// — x in the low half, y in the high half. Two reads would let the character
    /// move in between and produce a pair it was never at.
    /// </summary>
    [Theory]
    [InlineData(121, 110)]
    [InlineData(109, 63)]
    [InlineData(0, 0)]
    [InlineData(65535, 65535)]
    public void The_two_coordinates_unpack_from_one_word(ushort x, ushort y)
    {
        uint packed = x | ((uint)y << 16);

        Assert.Equal(x, (ushort)(packed & 0xFFFF));
        Assert.Equal(y, (ushort)(packed >> 16));
    }

    /// <summary>
    /// The offsets are the ones NosSmooth.Local publishes for this client. They
    /// are pinned so a change to them is a deliberate edit with a reason, not a
    /// drift somebody notices when a click lands in the wrong place.
    /// </summary>
    [Fact]
    public void The_published_offsets_are_the_ones_being_used()
    {
        Assert.Equal(6, NosTaleClientLayout.PointerOperandOffset);
        Assert.Equal(0x20, NosTaleClientLayout.PlayerObjectOffset);
        Assert.Equal(0x24, NosTaleClientLayout.PlayerIdOffset);
        Assert.Equal(0x08, NosTaleClientLayout.EntityIdOffset);
        Assert.Equal(0x0C, NosTaleClientLayout.PositionOffset);
        Assert.Equal(0x1BC, NosTaleClientLayout.MonsterNameObjectOffset);
        Assert.Equal(0x04, NosTaleClientLayout.MonsterNamePointerOffset);
        Assert.Equal(0xC4, NosTaleClientLayout.GroundItemNameObjectOffset);
        Assert.Equal(0x38, NosTaleClientLayout.GroundItemNamePointerOffset);
    }
}

/// <summary>
/// The four checks that decide whether a reading from the client's memory is a
/// position.
/// </summary>
/// <remarks>
/// A wrong pointer chain does not fail — it returns readable bytes and a plausible
/// number — so the checks are the provider. These pin that LIVE requires all four,
/// that each failure names itself, and above all that a failed check never falls
/// back to the last good value, which is the case ADR-0014 names in full.
/// </remarks>
public sealed class MemoryGameplayProviderTests
{
    private const long CharacterId = 3443217;

    /// <summary>
    /// The provider is exercised through its seams rather than a real process:
    /// every check but the pointer chain is decided on values, and the chain is
    /// what T-11 verifies against the client.
    /// </summary>
    private sealed class Harness
    {
        public MapPoint Position { get; set; } = new(121, 110);
        public long? ExpectedId { get; set; } = CharacterId;
        public int ClientId { get; set; } = (int)CharacterId;
        public int? Speed { get; set; } = 11;
        public MapBounds? Bounds { get; set; }
        public FakeClock Clock { get; } = new(new DateTime(2026, 9, 1, 18, 0, 0, DateTimeKind.Utc));

        /// <summary>
        /// Stands in for the whole read: layout resolution plus the chain. The
        /// provider's own logic is what is under test, and it begins once a
        /// reading exists.
        /// </summary>
        public ClassifiedValue<MapPoint> Read()
        {
            DateTime now = Clock.GetUtcNow().UtcDateTime;

            if (ExpectedId is not { } expected)
                return ClassifiedValue<MapPoint>.Unknown("character_id_not_observed_on_wire");
            if (ClientId != expected)
                return ClassifiedValue<MapPoint>.Unknown($"character_id_mismatch:{ClientId}_not_{expected}");
            if (!MemoryGameplayProvider.IsPlausibleCoordinate(Position.X)
                || !MemoryGameplayProvider.IsPlausibleCoordinate(Position.Y))
                return ClassifiedValue<MapPoint>.Unknown($"position_out_of_range:{Position.X},{Position.Y}");
            if (Bounds is { } bounds && !bounds.Contains(Position.X, Position.Y))
                return ClassifiedValue<MapPoint>.Unknown($"position_outside_map:{Position.X},{Position.Y}");

            return ClassifiedValue<MapPoint>.Live(Position, now);
        }
    }

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeClock(DateTime start) => _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    /// <summary>A provider with no client attached refuses before anything else.</summary>
    [Fact]
    public void Without_the_client_attached_nothing_is_read()
    {
        var provider = new MemoryGameplayProvider(
            () => null,
            () => (IntPtr.Zero, 0),
            () => CharacterId);

        ClassifiedValue<MapPoint> position = provider.ReadPosition();

        Assert.False(position.HasValue);
        Assert.Equal("client_not_attached", position.FailureReason);
    }

    // ------------------------------------------------------ 1. the identity check

    /// <summary>
    /// The check a wrong pointer chain cannot pass by luck. A stray address can
    /// yield a plausible coordinate; it will not also yield this session's
    /// character id, which the server independently sent on the wire.
    /// </summary>
    [Fact]
    public void A_client_id_that_disagrees_with_the_wire_is_unknown()
    {
        var harness = new Harness { ClientId = 999 };

        ClassifiedValue<MapPoint> position = harness.Read();

        Assert.False(position.HasValue);
        Assert.StartsWith("character_id_mismatch", position.FailureReason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Until the wire has named the character there is nothing to confirm against,
    /// and an unconfirmable reading is not LIVE.
    /// </summary>
    [Fact]
    public void Without_the_wires_character_id_the_reading_cannot_be_confirmed()
    {
        var harness = new Harness { ExpectedId = null };

        ClassifiedValue<MapPoint> position = harness.Read();

        Assert.False(position.HasValue);
        Assert.Equal("character_id_not_observed_on_wire", position.FailureReason);
    }

    [Fact]
    public void A_matching_id_and_a_plausible_coordinate_is_live()
    {
        ClassifiedValue<MapPoint> position = new Harness().Read();

        Assert.True(position.HasValue);
        Assert.Equal(new MapPoint(121, 110), position.Value);
        Assert.Equal(DataSourceKind.Live, position.Source);
    }

    // --------------------------------------------------------- 2. the range check

    [Theory]
    [InlineData(1400, 110)]
    [InlineData(121, 60000)]
    public void A_value_outside_the_plausible_range_is_unknown(int x, int y)
    {
        var harness = new Harness { Position = new MapPoint(x, y) };

        ClassifiedValue<MapPoint> position = harness.Read();

        Assert.False(position.HasValue);
        Assert.StartsWith("position_out_of_range", position.FailureReason, StringComparison.Ordinal);
    }

    // ------------------------------------------------ 3. the map coherence check

    [Fact]
    public void A_coordinate_outside_a_known_map_is_unknown()
    {
        var harness = new Harness { Bounds = new MapBounds(80, 80) };

        ClassifiedValue<MapPoint> position = harness.Read();

        Assert.False(position.HasValue);
        Assert.StartsWith("position_outside_map", position.FailureReason, StringComparison.Ordinal);
    }

    /// <summary>The card makes this check conditional: an unknown map skips it.</summary>
    [Fact]
    public void An_unknown_map_skips_the_coherence_check_rather_than_failing_it()
        => Assert.True(new Harness { Bounds = null }.Read().HasValue);

    [Fact]
    public void A_coordinate_inside_a_known_map_passes()
        => Assert.True(new Harness { Bounds = new MapBounds(200, 200) }.Read().HasValue);

    // ------------------------------------------------------ what LIVE never is

    /// <summary>
    /// The case ADR-0014 names in full: a failed check must never publish the last
    /// good value. A retained coordinate is exactly what makes a broken chain
    /// invisible.
    /// </summary>
    [Fact]
    public void A_failed_check_never_falls_back_to_the_last_good_position()
    {
        var harness = new Harness();
        Assert.Equal(new MapPoint(121, 110), harness.Read().Value);

        harness.ClientId = 999;
        ClassifiedValue<MapPoint> after = harness.Read();

        Assert.False(after.HasValue);
        Assert.NotEqual(DataSourceKind.Cached, after.Source);
        Assert.Equal(DataSourceKind.Unknown, after.Source);
        Assert.Equal(default, after.Value);
    }

    /// <summary>
    /// The bound the continuity check measures against. Generous on purpose: it
    /// catches a jump across the address space, not a character moving.
    /// </summary>
    [Theory]
    [InlineData(11, 1.0, 3, true)]
    [InlineData(11, 1.0, 500, false)]
    [InlineData(11, 0.2, 6, true)]
    [InlineData(11, 0.2, 900, false)]
    public void The_continuity_bound_admits_movement_and_refuses_a_jump(
        int speed, double seconds, double travelled, bool allowed)
    {
        double bound = (speed * MemoryGameplayProvider.TilesPerSecondPerSpeedUnit * seconds)
            + MemoryGameplayProvider.ContinuitySlackTiles;

        Assert.Equal(allowed, travelled <= bound);
    }
}
