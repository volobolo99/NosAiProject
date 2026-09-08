using System.Text.Json;
using NosAi.ControlPanel;
using NosAi.Core.Testing;
using Xunit;

namespace NosAi.ControlPanel.Tests;

public sealed class PracticalTestCenterVerdictTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc);

    private static PracticalTestDefinition Test(string id)
        => PracticalTestCatalog.All.First(d => d.Id == id);

    [Fact]
    public void T5_with_populated_entities_is_not_blocked()
    {
        using var doc = JsonDocument.Parse("""
        {
          "client": {
            "gameplayBaseline": {
              "value": {
                "entities": {
                  "value": [ { "entityId": 1, "x": 10, "y": 20 } ],
                  "source": "LIVE",
                  "hasObservedValue": true
                }
              },
              "source": "DERIVED",
              "hasObservedValue": true
            }
          }
        }
        """);

        PracticalTestVerdict verdict = PracticalTestCenter.Evaluate(Test("T5"), doc.RootElement, Now, Now);

        Assert.Equal(PracticalTestResult.Unknown, verdict.Result);
        Assert.NotEqual(PracticalTestResult.Blocked, verdict.Result);
        Assert.Equal("canonical.client.gameplayBaseline.value.entities", verdict.Evidence);
    }

    [Fact]
    public void T5_without_entities_is_blocked_and_evidence_names_a_real_path()
    {
        using var doc = JsonDocument.Parse("""
        {
          "client": {
            "gameplayBaseline": {
              "value": { },
              "source": "DERIVED",
              "hasObservedValue": true
            }
          }
        }
        """);

        PracticalTestVerdict verdict = PracticalTestCenter.Evaluate(Test("T5"), doc.RootElement, Now, Now);

        Assert.Equal(PracticalTestResult.Blocked, verdict.Result);
        Assert.Equal("canonical.client.gameplayBaseline.value.entities", verdict.Evidence);
        Assert.DoesNotContain("map/entities", verdict.Evidence);
        Assert.Contains("entities", verdict.Evidence);
    }

    [Fact]
    public void T8_with_populated_inventory_no_longer_claims_the_contract_is_unpublished()
    {
        using var doc = JsonDocument.Parse("""
        {
          "client": {
            "gameplayBaseline": {
              "value": {
                "inventory": {
                  "value": [ { "inventoryKind": 2, "slot": 34, "vnum": 2006, "amount": 1, "rarity": 0 } ],
                  "source": "LIVE",
                  "hasObservedValue": true
                }
              },
              "source": "DERIVED",
              "hasObservedValue": true
            }
          }
        }
        """);

        PracticalTestVerdict verdict = PracticalTestCenter.Evaluate(Test("T8"), doc.RootElement, Now, Now);

        Assert.NotEqual(PracticalTestResult.Blocked, verdict.Result);
        Assert.DoesNotContain("character_inventory_state_not_published", verdict.Evidence);
        Assert.Equal("canonical.client.gameplayBaseline.value.inventory", verdict.Evidence);
    }

    [Theory]
    [InlineData("T7", "quest_state_not_published")]
    [InlineData("T17", "event-log-endpoint-required")]
    [InlineData("T18", "controlled-reconnect-required")]
    [InlineData("T20", "certification_requires_physical_e2e")]
    public void True_blocked_reasons_are_pinned(string testId, string reason)
    {
        using var doc = JsonDocument.Parse("{}");

        PracticalTestVerdict verdict = PracticalTestCenter.Evaluate(Test(testId), doc.RootElement, Now, Now);

        Assert.Equal(PracticalTestResult.Blocked, verdict.Result);
        Assert.Equal(reason, verdict.Evidence);
    }

    [Fact]
    public void Unknown_test_id_is_blocked_unsupported()
    {
        var definition = new PracticalTestDefinition(
            "T999", PracticalTestKind.WorldModel, "ignoto", "nessuno", "nessuna",
            TimeSpan.Zero, "niente", false);

        using var doc = JsonDocument.Parse("{}");

        PracticalTestVerdict verdict = PracticalTestCenter.Evaluate(definition, doc.RootElement, Now, Now);

        Assert.Equal(PracticalTestResult.Blocked, verdict.Result);
        Assert.Equal("unsupported_test", verdict.Evidence);
    }
}
