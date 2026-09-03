using System.Text.Json;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Observability;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Where the gameplay baseline actually sits in the operator API document.
/// </summary>
/// <remarks>
/// <para>
/// Two probes read it from the document root. The API serves it two levels
/// deeper and wrapped — <c>client.gameplayBaseline.value</c> — because the
/// baseline is a classified value. Both therefore refused every call with
/// <c>gameplay_provider_not_available</c> while the provider was running and
/// decoding 1590 packets, which is the worst shape a refusal can take: correctly
/// named, and about something that was not happening.
/// </para>
/// <para>
/// The shapes here are the ones a live runtime served on 3 September 2026, not
/// invented ones. The flat shape is kept as a test of its own: it is what the
/// probes used to expect, and it must now be refused rather than quietly
/// tolerated, or the two forms drift apart again.
/// </para>
/// </remarks>
public sealed class OperatorApiSnapshotTests
{
    /// <summary>The document as the runtime serves it.</summary>
    private const string Served = """
    {
      "client": {
        "gameplayBaseline": {
          "value": {
            "hp":   { "value": 7305, "source": "CACHED", "failureReason": null },
            "maxHp":{ "value": 7305, "source": "CACHED", "failureReason": null },
            "skillsReady": {
              "value": [
                { "slot": 0, "observedAtUtc": "2026-09-03T10:38:58.1431978Z" },
                { "slot": 2, "observedAtUtc": "2026-09-03T10:38:58.3401284Z" }
              ],
              "source": "LIVE",
              "failureReason": null
            }
          },
          "source": "LIVE",
          "failureReason": null
        }
      }
    }
    """;

    /// <summary>The same document while the wire has announced no skill.</summary>
    private const string NothingAnnounced = """
    {
      "client": {
        "gameplayBaseline": {
          "value": {
            "skillsReady": {
              "value": null,
              "source": "UNKNOWN",
              "failureReason": "no_skill_ready_observed"
            }
          },
          "source": "LIVE",
          "failureReason": null
        }
      }
    }
    """;

    /// <summary>The shape the probes used to expect, which was never served.</summary>
    private const string Flat = """
    { "gameplayBaseline": { "skillsReady": { "value": [ { "slot": 2, "observedAtUtc": "2026-09-03T10:38:58Z" } ] } } }
    """;

    private static JsonElement Root(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // ---------- navigation

    [Fact]
    public void TheBaselineIsFoundWhereTheRuntimeActuallyPutsIt()
    {
        Assert.True(OperatorApiSnapshot.TryGameplayBaseline(Root(Served), out JsonElement baseline, out string? why));
        Assert.Null(why);
        Assert.True(baseline.TryGetProperty("skillsReady", out _), "the unwrapped baseline exposes its fields by name");
    }

    [Fact]
    public void TheShapeTheProbesUsedToExpectIsRefused()
    {
        // Not tolerated for compatibility: nothing serves it, so accepting it
        // would only let the two forms drift apart again.
        Assert.False(OperatorApiSnapshot.TryGameplayBaseline(Root(Flat), out _, out string? why));
        Assert.Equal(OperatorApiSnapshot.BaselineUnavailableReason, why);
    }

    [Fact]
    public void AFieldIsUnwrappedPastItsOwnClassification()
    {
        OperatorApiSnapshot.TryGameplayBaseline(Root(Served), out JsonElement baseline, out _);

        Assert.True(OperatorApiSnapshot.TryField(baseline, "skillsReady", out JsonElement list, out _));
        Assert.Equal(JsonValueKind.Array, list.ValueKind);
        Assert.Equal(2, list.GetArrayLength());
    }

    [Fact]
    public void AFieldThatWasNotObservedAnswersWithItsOwnReason()
    {
        // "no_skill_ready_observed" tells the operator the wire has been quiet.
        // A generic absence would have them looking for a broken provider.
        OperatorApiSnapshot.TryGameplayBaseline(Root(NothingAnnounced), out JsonElement baseline, out _);

        Assert.False(OperatorApiSnapshot.TryField(baseline, "skillsReady", out _, out string? why));
        Assert.Equal("no_skill_ready_observed", why);
    }

    [Fact]
    public void AFieldThatIsNotThereAtAllIsNamedForWhatItIs()
    {
        OperatorApiSnapshot.TryGameplayBaseline(Root(Served), out JsonElement baseline, out _);

        Assert.False(OperatorApiSnapshot.TryField(baseline, "selectedTarget", out _, out string? why));
        Assert.Equal("selectedTarget_not_in_snapshot", why);
    }

    // ---------- the probe that depends on it

    [Fact]
    public void TheCooldownProbeReadsASlotOutOfTheServedDocument()
    {
        // This is the end-to-end point of the fix: against the real shape, the
        // probe now finds slot 2 instead of refusing that the provider is absent.
        Assert.True(SkillCooldownProbe.TryReadSkillReady(Served, slot: 2, out DateTime at, out string? why));
        Assert.Null(why);
        Assert.Equal(
            new DateTime(2026, 9, 3, 10, 38, 58, 340, DateTimeKind.Utc).AddTicks(1284),
            at);
    }

    [Fact]
    public void ASlotTheWireNeverAnnouncedIsRefusedRatherThanGuessed()
    {
        Assert.False(SkillCooldownProbe.TryReadSkillReady(Served, slot: 7, out _, out string? why));
        Assert.False(string.IsNullOrWhiteSpace(why));
    }

    [Fact]
    public void TheCooldownProbeNoLongerBlamesTheProviderForTheServedShape()
    {
        // The regression in one assertion: this document used to produce
        // "gameplay_provider_not_available" from a running provider.
        SkillCooldownProbe.TryReadSkillReady(Served, slot: 2, out _, out string? why);

        Assert.NotEqual(OperatorApiSnapshot.BaselineUnavailableReason, why);
    }
}
