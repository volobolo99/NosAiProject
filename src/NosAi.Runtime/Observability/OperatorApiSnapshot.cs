using System.Text.Json;

namespace NosAi.Runtime.Observability;

/// <summary>
/// Where the operator API actually puts the gameplay baseline.
/// </summary>
/// <remarks>
/// <para>
/// Two probes navigated to <c>gameplayBaseline</c> from the document root and
/// read its fields directly. The API serves it two levels deeper and wrapped:
/// <c>client.gameplayBaseline.value.&lt;field&gt;</c>, because the baseline is a
/// classified value and classified values carry their provenance beside the
/// number. Both probes therefore refused every call with
/// <see cref="BaselineUnavailableReason"/> — a named refusal for a cause that was
/// not true, since the provider was running and decoding packets the whole time.
/// </para>
/// <para>
/// The Control Panel had it right, which is how the shape was settled: it is
/// handed the <c>client</c> element and unwraps the value. Three readers of one
/// document had drifted into two shapes, so the navigation lives in one place
/// now and the drift has somewhere to be fixed.
/// </para>
/// </remarks>
public static class OperatorApiSnapshot
{
    public const string BaselineUnavailableReason = "gameplay_provider_not_available";

    /// <summary>
    /// The gameplay baseline's fields, unwrapped, or a named refusal.
    /// </summary>
    /// <param name="root">The parsed operator API document.</param>
    /// <param name="baseline">
    /// The object holding <c>skillsReady</c>, <c>selectedTarget</c> and the rest —
    /// already past the classified wrapper, so a caller reads a field by name.
    /// </param>
    public static bool TryGameplayBaseline(
        JsonElement root, out JsonElement baseline, out string? failureReason)
    {
        baseline = default;

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("client", out JsonElement client)
            || client.ValueKind != JsonValueKind.Object)
        {
            failureReason = BaselineUnavailableReason;
            return false;
        }

        if (!client.TryGetProperty("gameplayBaseline", out JsonElement classified)
            || classified.ValueKind != JsonValueKind.Object)
        {
            failureReason = BaselineUnavailableReason;
            return false;
        }

        // The wrapper's own failureReason is more useful than a generic one: it
        // says why the provider had nothing, rather than that it was absent.
        if (!classified.TryGetProperty("value", out JsonElement value)
            || value.ValueKind != JsonValueKind.Object)
        {
            failureReason =
                classified.TryGetProperty("failureReason", out JsonElement why)
                && why.ValueKind == JsonValueKind.String
                    ? why.GetString()
                    : BaselineUnavailableReason;
            return false;
        }

        baseline = value;
        failureReason = null;
        return true;
    }

    /// <summary>Unwraps one classified field of the baseline by name.</summary>
    /// <remarks>
    /// Every field under the baseline is itself classified, so reading one means
    /// going through <c>value</c> again. A field that is present but unobserved
    /// carries its own reason, and that reason is the answer rather than an
    /// absence.
    /// </remarks>
    public static bool TryField(
        JsonElement baseline, string name, out JsonElement value, out string? failureReason)
    {
        value = default;
        failureReason = null;

        if (!baseline.TryGetProperty(name, out JsonElement field)
            || field.ValueKind != JsonValueKind.Object)
        {
            failureReason = $"{name}_not_in_snapshot";
            return false;
        }

        if (!field.TryGetProperty("value", out JsonElement inner)
            || inner.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            failureReason =
                field.TryGetProperty("failureReason", out JsonElement why)
                && why.ValueKind == JsonValueKind.String
                    ? why.GetString()
                    : $"{name}_not_observed";
            return false;
        }

        value = inner;
        return true;
    }
}
