using System.Text.Json;
using NosAi.GuardClient;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// What <c>gameplayBaseline</c> puts on the wire, and why adding to it did not
/// move the contract version.
/// </summary>
/// <remarks>
/// <para>
/// F4-1b. <c>maxMp</c> is an addition to <c>gate1.snapshot.v1</c>, so ADR-0005
/// applies: a contract change must be versioned <i>when compatibility can be
/// affected</i>. These decide whether it can, rather than asserting that it
/// cannot — the older reader is exercised against the newer payload, with the
/// real <see cref="GuardSnapshotView"/> rather than a stand-in for it.
/// </para>
/// <para>
/// The day a reader does start enumerating the inside of this value, the last
/// test here fails and the version has to move with it. That is what makes the
/// decision reviewable instead of a claim in a comment.
/// </para>
/// </remarks>
public sealed class GameplayWireContractTests
{
    private static readonly DateTime At = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static GameplayObservation Full() => new(
        Hp: ClassifiedValue<int>.Live(7305, At),
        MaxHp: ClassifiedValue<int>.Live(7305, At),
        Mp: ClassifiedValue<int>.Live(1362, At),
        MaxMp: ClassifiedValue<int>.Live(1420, At),
        HasTarget: ClassifiedValue<bool>.Unknown("target_flag_not_mapped"),
        InCombat: ClassifiedValue<bool>.Unknown("combat_flag_not_mapped"),
        EntitiesInView: ClassifiedValue<int>.Live(4, At),
        ObservedAtUtc: At);

    private static JsonElement Wire(GameplayObservation observation)
        => JsonDocument.Parse(JsonSerializer.Serialize(observation.ToWire())).RootElement;

    // ------------------------------------------------------------ the addition

    [Fact]
    public void The_snapshot_carries_max_mp_with_its_own_classification()
    {
        JsonElement wire = Wire(Full());

        JsonElement maxMp = wire.GetProperty("maxMp");
        Assert.Equal(1420, maxMp.GetProperty("value").GetInt32());
        Assert.Equal("LIVE", maxMp.GetProperty("source").GetString());
    }

    /// <summary>
    /// Absent is not zero. A decoder whose map does not describe the field says
    /// so, and the snapshot carries the reason instead of a maximum of nought —
    /// which a consumer would divide by.
    /// </summary>
    [Fact]
    public void An_unmapped_max_mp_is_unknown_with_a_reason_and_not_a_zero()
    {
        GameplayObservation observation = Full() with
        {
            MaxMp = ClassifiedValue<int>.Unknown("max_mp_not_mapped")
        };

        JsonElement maxMp = Wire(observation).GetProperty("maxMp");

        Assert.Equal("UNKNOWN", maxMp.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, maxMp.GetProperty("value").ValueKind);
        Assert.Equal("max_mp_not_mapped", maxMp.GetProperty("failureReason").GetString());
    }

    [Fact]
    public void Unobserved_says_why_for_max_mp_as_it_does_for_every_other_field()
    {
        JsonElement wire = Wire(GameplayObservation.Unobserved("no_capture_backend_attached", At));

        JsonElement maxMp = wire.GetProperty("maxMp");
        Assert.Equal("UNKNOWN", maxMp.GetProperty("source").GetString());
        Assert.Equal("no_capture_backend_attached", maxMp.GetProperty("failureReason").GetString());
    }

    /// <summary>Everything that was published before is still published, unmoved.</summary>
    [Theory]
    [InlineData("hp")]
    [InlineData("maxHp")]
    [InlineData("mp")]
    [InlineData("hasTarget")]
    [InlineData("inCombat")]
    [InlineData("entitiesInView")]
    [InlineData("observedAtUtc")]
    public void Every_field_that_existed_before_still_exists(string field)
        => Assert.True(Wire(Full()).TryGetProperty(field, out _));

    /// <summary>
    /// Max MP is not among the vitals a planner needs. Requiring it would stop
    /// every decoder that does not map it from planning at all — a behaviour
    /// change smuggled in behind a published field.
    /// </summary>
    [Fact]
    public void Publishing_max_mp_did_not_make_it_a_precondition_for_planning()
    {
        GameplayObservation without = Full() with
        {
            MaxMp = ClassifiedValue<int>.Unknown("max_mp_not_mapped")
        };

        Assert.True(without.HasVitals);
        Assert.Null(without.UnusableReason);
    }

    // -------------------------------------------- the reader that came before

    /// <summary>
    /// The real Guard client, given a snapshot carrying the new field. It reads
    /// <c>gameplayBaseline</c> as one classified value and never looks inside, so
    /// the addition cannot affect it — demonstrated rather than asserted.
    /// </summary>
    [Fact]
    public void The_guard_client_reads_a_snapshot_that_carries_the_new_field()
    {
        string json = Snapshot(withMaxMp: true);

        GuardSnapshotView view = GuardSnapshotView.Parse(json);

        ClassifiedField gameplay = Assert.Single(
            view.Client, f => f.Name == "Gameplay");
        Assert.Equal("LIVE", gameplay.Source);
    }

    /// <summary>
    /// And it reads the older payload identically, which is the property that
    /// makes this additive rather than a version bump.
    /// </summary>
    [Fact]
    public void The_guard_client_sees_the_same_thing_with_and_without_the_new_field()
    {
        GuardSnapshotView before = GuardSnapshotView.Parse(Snapshot(withMaxMp: false));
        GuardSnapshotView after = GuardSnapshotView.Parse(Snapshot(withMaxMp: true));

        ClassifiedField gameplayBefore = Assert.Single(before.Client, f => f.Name == "Gameplay");
        ClassifiedField gameplayAfter = Assert.Single(after.Client, f => f.Name == "Gameplay");

        Assert.Equal(gameplayBefore.Source, gameplayAfter.Source);
        Assert.Equal(before.RuntimeStatus, after.RuntimeStatus);
        Assert.Equal(before.ClientStatus, after.ClientStatus);
    }

    /// <summary>
    /// The version stays where it is only while no shipped reader enumerates the
    /// inside of this value. When one does, this fails and the version moves with
    /// it (ADR-0005).
    /// </summary>
    [Fact]
    public void No_shipped_reader_enumerates_the_inside_of_the_gameplay_value()
    {
        string[] readers =
        [
            Path.Combine("src", "NosAi.GuardClient", "GuardSnapshotView.cs"),
            Path.Combine("src", "NosAi.ControlPanel", "AttachedSnapshot.cs"),
        ];

        string root = RepositoryRoot();
        foreach (string reader in readers)
        {
            string path = Path.Combine(root, reader);
            Assert.True(File.Exists(path), path);
            string source = File.ReadAllText(path);

            Assert.DoesNotContain("\"maxMp\"", source, StringComparison.Ordinal);
            Assert.DoesNotContain("\"maxHp\"", source, StringComparison.Ordinal);
            // C1 added these on the same precedent; the same rule holds them open.
            foreach (string key in C1Keys)
                Assert.DoesNotContain($"\"{key}\"", source, StringComparison.Ordinal);
            foreach (string key in S5Keys)
                Assert.DoesNotContain($"\"{key}\"", source, StringComparison.Ordinal);
        }
    }

    /// <summary>The keys C1 added inside <c>gameplayBaseline</c>, additively.</summary>
    private static readonly string[] C1Keys =
    [
        "entities", "playerPosition", "hitBy", "selectedTarget",
        "skillsReady", "inventory", "lastPickup", "groundItems",
    ];

    /// <summary>The keys S5 added inside <c>gameplayBaseline</c>, additively.</summary>
    private static readonly string[] S5Keys = ["mapId", "standingCell"];

    /// <summary>
    /// Each C1 key is one classified value beside the existing ones, unknown with a
    /// reason when the provider did not publish it — never a zero, an empty list or
    /// a coordinate.
    /// </summary>
    [Fact]
    public void Every_c1_field_is_published_as_one_classified_value_with_its_reason()
    {
        JsonElement wire = Wire(Full());

        foreach (string key in C1Keys)
        {
            JsonElement field = wire.GetProperty(key);
            Assert.Equal("UNKNOWN", field.GetProperty("source").GetString());
            Assert.Equal(JsonValueKind.Null, field.GetProperty("value").ValueKind);
            Assert.Equal(
                key == "playerPosition"
                    ? GameplayObservation.PlayerPositionNotReadReason
                    : GameplayObservation.NotPublishedReason,
                field.GetProperty("failureReason").GetString());
        }
    }

    /// <summary>And none of them made planning wait for it (ADR-0016).</summary>
    [Fact]
    public void Publishing_the_c1_fields_did_not_make_any_of_them_a_precondition_for_planning()
    {
        GameplayObservation observation = Full();

        Assert.True(observation.HasVitals);
        Assert.Null(observation.UnusableReason);
        Assert.False(observation.Entities.HasValue);
        Assert.False(observation.PlayerPosition.HasValue);
    }

    [Fact]
    public void Every_s5_field_is_published_as_one_classified_value_with_its_reason()
    {
        JsonElement wire = Wire(Full());

        JsonElement mapId = wire.GetProperty("mapId");
        Assert.Equal("UNKNOWN", mapId.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, mapId.GetProperty("value").ValueKind);
        Assert.Equal(GameplayObservation.MapIdNotReadReason, mapId.GetProperty("failureReason").GetString());

        JsonElement standing = wire.GetProperty("standingCell");
        Assert.Equal("UNKNOWN", standing.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, standing.GetProperty("value").ValueKind);
        Assert.Equal(GameplayObservation.StandingCellNotReadReason, standing.GetProperty("failureReason").GetString());
    }

    [Fact]
    public void Publishing_map_id_and_standing_cell_did_not_make_them_a_precondition_for_planning()
    {
        GameplayObservation observation = Full();

        Assert.True(observation.HasVitals);
        Assert.Null(observation.UnusableReason);
        Assert.False(observation.MapId.HasValue);
        Assert.False(observation.StandingCell.HasValue);
    }

    private static string Snapshot(bool withMaxMp)
    {
        string gameplayInner = withMaxMp
            ? """{"hp":{"value":7305,"source":"LIVE"},"maxMp":{"value":1420,"source":"LIVE"}}"""
            : """{"hp":{"value":7305,"source":"LIVE"}}""";

        return $$"""
        {
          "contractVersion": "gate1.snapshot.v1",
          "runtimeStatus": "Running",
          "capturedAtUtc": "2026-09-01T12:00:00Z",
          "client": {
            "status": "Attached",
            "processName": {"value":"NostaleClientX","source":"LIVE"},
            "processId": {"value":4242,"source":"LIVE"},
            "windowTitle": {"value":"NosTale","source":"LIVE"},
            "processResponding": {"value":true,"source":"LIVE"},
            "windowVisible": {"value":true,"source":"LIVE"},
            "gameplayBaseline": {"value":{{gameplayInner}},"source":"LIVE"}
          },
          "safety": {
            "executionMode": {"value":"Safe","source":"DERIVED"},
            "liveInputEnabled": {"value":false,"source":"DERIVED"},
            "packetInjectionEnabled": {"value":false,"source":"DERIVED"}
          }
        }
        """;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
