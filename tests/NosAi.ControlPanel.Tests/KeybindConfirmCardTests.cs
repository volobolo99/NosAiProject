using System;
using System.Text.Json;
using Xunit;

namespace NosAi.ControlPanel.Tests
{
    public sealed class KeybindConfirmCardTests
    {
        private const string DocumentJson =
@"{
  ""version"": 1,
  ""_readme"": [ ""riga di nota"" ],
  ""binds"": {
    ""consumable.1"": { ""virtualKey"": 49, ""label"": ""1"", ""confirmed"": false },
    ""skill.201"":    { ""virtualKey"": 50, ""label"": ""2"", ""confirmed"": true }
  }
}";

        [Fact]
        public void Describe_separates_confirmed_from_declared()
        {
            string description = KeybindConfirmCard.Describe(DocumentJson);

            Assert.Contains("consumable.1", description, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("skill.201", description, StringComparison.OrdinalIgnoreCase);

            // The declared bind is announced as one that will be refused when the key is pressed.
            string declaredContext = FragmentAround(description, "consumable.1");
            Assert.True(
                ContainsAny(declaredContext, "rifiut", "refus", "reject", "not valid"),
                $"The part that describes the declared bind '{declaredContext}' does not say it will be refused.");
            Assert.True(
                ContainsAny(declaredContext, "pression", "press"),
                $"The part that describes the declared bind '{declaredContext}' does not mention the key press.");

            // The confirmed bind is presented as confirmed, not as something still pending.
            string confirmedContext = FragmentAround(description, "skill.201");
            Assert.True(
                ContainsAny(confirmedContext, "confermat", "confirmed", "accepted", "valid"),
                $"The part that describes the confirmed bind '{confirmedContext}' does not present it as confirmed.");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Confirming_without_an_observation_is_refused(string? observation)
        {
            bool confirmed = KeybindConfirmCard.TryConfirm(
                DocumentJson,
                "consumable.1",
                "49",
                observation,
                out _,
                out string? refusal);

            Assert.False(confirmed);
            AssertRefusalNames(refusal, "osservazione", "observation");
        }

        [Fact]
        public void Confirming_an_unknown_intent_is_refused()
        {
            bool confirmed = KeybindConfirmCard.TryConfirm(
                DocumentJson,
                "skill.999",
                "49",
                "Osservazione di prova",
                out _,
                out string? refusal);

            Assert.False(confirmed);
            AssertRefusalNames(refusal, "intento", "intent", "skill.999");
        }

        [Theory]
        [InlineData("0")]
        [InlineData("255")]
        [InlineData("non-un-numero")]
        public void A_virtual_key_outside_the_range_is_refused(string virtualKeyText)
        {
            bool confirmed = KeybindConfirmCard.TryConfirm(
                DocumentJson,
                "consumable.1",
                virtualKeyText,
                "Osservazione di prova",
                out _,
                out string? refusal);

            Assert.False(confirmed);
            AssertRefusalNames(refusal, "virtualkey", "virtuale", "tasto", "key");
        }

        [Fact]
        public void A_confirmation_touches_only_its_own_bind()
        {
            string updatedJson = Confirm("consumable.1", "49", "Premendo 1 ho consumato l'oggetto.");

            using JsonDocument document = JsonDocument.Parse(updatedJson);
            JsonElement root = document.RootElement;

            Assert.Equal(1, root.GetProperty("version").GetInt32());

            JsonElement readme = root.GetProperty("_readme");
            Assert.Equal(JsonValueKind.Array, readme.ValueKind);
            Assert.True(readme.GetArrayLength() > 0);
            Assert.Equal("riga di nota", readme[0].GetString());

            JsonElement binds = root.GetProperty("binds");
            Assert.True(binds.GetProperty("consumable.1").GetProperty("confirmed").GetBoolean());

            JsonElement untouched = binds.GetProperty("skill.201");
            Assert.Equal(50, untouched.GetProperty("virtualKey").GetInt32());
            Assert.Equal("2", untouched.GetProperty("label").GetString());
            Assert.True(untouched.GetProperty("confirmed").GetBoolean());
        }

        [Fact]
        public void A_confirmed_bind_records_what_was_observed()
        {
            const string observation = "MP scesi di 15 premendo 1";
            string updatedJson = Confirm("consumable.1", "49", observation);

            using JsonDocument document = JsonDocument.Parse(updatedJson);
            JsonElement confirmedBind = document.RootElement
                .GetProperty("binds")
                .GetProperty("consumable.1");

            Assert.True(confirmedBind.GetProperty("confirmed").GetBoolean());
            Assert.Equal(observation, confirmedBind.GetProperty("confirmedNote").GetString());

            string? confirmedAtUtc = confirmedBind.GetProperty("confirmedAtUtc").GetString();
            Assert.True(
                DateTime.TryParse(
                    confirmedAtUtc,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out _),
                $"confirmedAtUtc '{confirmedAtUtc}' is not a readable date.");
        }

        private static string Confirm(string intent, string virtualKeyText, string observation)
        {
            bool confirmed = KeybindConfirmCard.TryConfirm(
                DocumentJson,
                intent,
                virtualKeyText,
                observation,
                out string updatedJson,
                out string? refusal);

            Assert.True(confirmed);
            Assert.Null(refusal);
            return updatedJson;
        }

        private static void AssertRefusalNames(string? refusal, params string[] terms)
        {
            Assert.NotNull(refusal);
            Assert.True(
                ContainsAny(refusal!, terms),
                $"The refusal '{refusal}' does not name the field it is about.");
        }

        private static bool ContainsAny(string text, params string[] terms)
        {
            foreach (string term in terms)
            {
                if (text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string FragmentAround(string text, string marker)
        {
            int index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            int start = Math.Max(0, index - 60);
            int length = Math.Min(text.Length - start, 180);
            return text.Substring(start, length);
        }
    }
}
