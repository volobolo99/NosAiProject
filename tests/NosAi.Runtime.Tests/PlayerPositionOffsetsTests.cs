using NosAi.LiveIntegration;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Where the player's coordinates were found, and what makes that record usable.
/// </summary>
/// <remarks>
/// F1-9 produces this file and F1-10 reads it. The rule worth enforcing is F1-9's
/// own: an offset that has not been re-verified after a restart of the client is
/// not an offset, it is an address that worked once.
/// </remarks>
public sealed class PlayerPositionOffsetsTests : IDisposable
{
    private static readonly DateTime At = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "nosai-offsets-" + Guid.NewGuid().ToString("N"));

    private string PathFor(string name) => Path.Combine(_directory, name);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void A_missing_file_is_absent_and_is_not_broken()
    {
        PlayerPositionOffsets loaded = PlayerPositionOffsets.Load(PathFor("absent"), out string? reason);

        Assert.False(loaded.IsPresent);
        Assert.False(loaded.IsUsable);
        Assert.Equal(PlayerPositionOffsets.NotFoundReason, reason);
    }

    [Fact]
    public void Offsets_survive_a_round_trip_including_the_optional_map_id()
    {
        PlayerPositionOffsets written = PlayerPositionOffsets.Found(
            "NostaleClientX.exe", 0x4A18C, 0x4A190, 0x4A1A0, verifiedRestarts: 3, At);
        string path = PathFor("player-position.offsets");
        written.Save(path);

        PlayerPositionOffsets loaded = PlayerPositionOffsets.Load(path, out string? reason);

        Assert.Null(reason);
        Assert.True(loaded.IsUsable);
        Assert.Equal("NostaleClientX.exe", loaded.ModuleName);
        Assert.Equal(0x4A18C, loaded.OffsetX);
        Assert.Equal(0x4A190, loaded.OffsetY);
        Assert.Equal(0x4A1A0, loaded.OffsetMapId);
        Assert.Equal(3, loaded.VerifiedRestarts);
        Assert.Equal(At, loaded.FoundAtUtc);
    }

    [Fact]
    public void The_map_id_offset_is_optional_and_round_trips_as_absent()
    {
        PlayerPositionOffsets written = PlayerPositionOffsets.Found(
            "NostaleClientX.exe", 0x10, 0x14, null, verifiedRestarts: 1, At);
        string path = PathFor("no-map");
        written.Save(path);

        PlayerPositionOffsets loaded = PlayerPositionOffsets.Load(path, out _);

        Assert.True(loaded.IsUsable);
        Assert.Null(loaded.OffsetMapId);
    }

    /// <summary>
    /// F1-9's rule, in code. Present is not the same as usable, and the reason
    /// says which half of the procedure is missing.
    /// </summary>
    [Fact]
    public void Offsets_never_reverified_after_a_restart_are_present_but_not_usable()
    {
        PlayerPositionOffsets written = PlayerPositionOffsets.Found(
            "NostaleClientX.exe", 0x10, 0x14, null, verifiedRestarts: 0, At);
        string path = PathFor("once");
        written.Save(path);

        PlayerPositionOffsets loaded = PlayerPositionOffsets.Load(path, out string? reason);

        Assert.True(loaded.IsPresent);
        Assert.False(loaded.IsUsable);
        Assert.Equal(PlayerPositionOffsets.NotReverifiedReason, reason);
        Assert.Equal(PlayerPositionOffsets.NotReverifiedReason, loaded.UnusableReason);
    }

    /// <summary>
    /// An offset is a distance from a module base. A negative one is a mistyped
    /// absolute address, which ASLR makes meaningless anyway.
    /// </summary>
    [Fact]
    public void A_negative_offset_is_refused()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => PlayerPositionOffsets.Found("m.exe", -1, 0x14, null, 1, At));

    [Fact]
    public void An_unnamed_module_is_refused()
        => Assert.Throws<ArgumentException>(
            () => PlayerPositionOffsets.Found("  ", 0x10, 0x14, null, 1, At));

    [Fact]
    public void The_absent_state_refuses_to_be_written()
        => Assert.Throws<InvalidOperationException>(
            () => PlayerPositionOffsets.Missing.Save(PathFor("never")));

    [Theory]
    [InlineData("garbage")]
    [InlineData("nosai-player-position-offsets 1")]
    [InlineData("nosai-player-position-offsets 1\nm.exe 0x10")]
    [InlineData("nosai-player-position-offsets 1\nm.exe zz 0x14 - 1 2026-09-01T12:00:00Z")]
    [InlineData("nosai-player-position-offsets 1\nm.exe 0x10 0x14 - -3 2026-09-01T12:00:00Z")]
    public void A_malformed_file_is_absent_with_a_reason(string contents)
    {
        string path = PathFor("broken");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, contents);

        PlayerPositionOffsets loaded = PlayerPositionOffsets.Load(path, out string? reason);

        Assert.False(loaded.IsUsable);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void A_future_version_is_refused_rather_than_guessed_at()
    {
        string path = PathFor("future");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "nosai-player-position-offsets 2\nm.exe 0x10 0x14 - 1 2026-09-01T12:00:00Z\n");

        PlayerPositionOffsets loaded = PlayerPositionOffsets.Load(path, out string? reason);

        Assert.False(loaded.IsPresent);
        Assert.Equal("player_position_offsets_version_unsupported:2", reason);
    }
}
