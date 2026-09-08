using System.Text.Json;
using NosAi.ControlPanel;
using Xunit;

namespace NosAi.ControlPanel.Tests;

public sealed class LiveFieldRenderingTests
{
    [Fact]
    public void Unknown_classified_value_shows_unknown_and_reason_not_an_empty_cell()
    {
        using var doc = JsonDocument.Parse("""
        {
          "gameObservation": {
            "lastHp": {
              "value": null,
              "source": "UNKNOWN",
              "hasObservedValue": false,
              "failureReason": "gameplay_provider_not_available"
            }
          }
        }
        """);

        IReadOnlyList<DisplayField> fields = PracticalTestCenter.ReadLiveFields(doc.RootElement);
        DisplayField hp = fields.Single(f => f.Label == "HP");

        Assert.Contains("UNKNOWN", hp.Value, StringComparison.Ordinal);
        Assert.Contains("gameplay_provider_not_available", hp.Value, StringComparison.Ordinal);
        Assert.NotEqual("", hp.Value);
        Assert.Equal("UNKNOWN", hp.Source);
    }

    [Fact]
    public void Cached_and_live_values_with_same_content_draw_differently()
    {
        using var doc = JsonDocument.Parse("""
        {
          "gameObservation": {
            "lastHp": { "value": 5000, "source": "LIVE", "hasObservedValue": true },
            "lastMp": { "value": 5000, "source": "CACHED", "hasObservedValue": true }
          }
        }
        """);

        IReadOnlyList<DisplayField> fields = PracticalTestCenter.ReadLiveFields(doc.RootElement);
        DisplayField hp = fields.Single(f => f.Label == "HP");
        DisplayField mp = fields.Single(f => f.Label == "MP");

        Assert.Equal("5000", hp.Value);
        Assert.Equal("5000", mp.Value);
        Assert.Equal("LIVE", hp.Source);
        Assert.Equal("CACHED", mp.Source);
        Assert.NotEqual(hp.Source, mp.Source);
    }
}
