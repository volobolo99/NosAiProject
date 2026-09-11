using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NosAi.Runtime.Configuration
{
    /// <summary>
    /// Rappresenta la configurazione dei binding tra ruoli e modelli, letta dal file
    /// <c>data/mcp/role_bindings.json</c> generato da <c>nosai/mcp/bindings.py</c>.
    /// Canale 2 di ADR-0030 (Python -> C# in ingresso): sola lettura, mai per tick.
    /// </summary>
    public sealed record RoleBinding(
        [property: JsonPropertyName("employee_id")] string EmployeeId,
        [property: JsonPropertyName("primary_model")] string PrimaryModel,
        [property: JsonPropertyName("fallback_models")] IReadOnlyList<string> FallbackModels,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("version")] int Version);

    /// <summary>
    /// Lettore della configurazione dei binding tra ruoli e modelli.
    /// Legge il file <c>data/mcp/role_bindings.json</c> generato da <c>nosai/mcp/bindings.py</c>.
    /// </summary>
    public sealed class RoleBindingConfiguration
    {
        public int StateRevision { get; }

        public IReadOnlyDictionary<string, RoleBinding> Bindings { get; }

        public static RoleBindingConfiguration Empty { get; } = new RoleBindingConfiguration(0, new Dictionary<string, RoleBinding>());

        public static RoleBindingConfiguration Load(string path)
        {
            if (!File.Exists(path))
                return Empty;

            try
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("schema_version", out var schemaVersion) ||
                    schemaVersion.GetString() != "mcp.role_bindings.v2")
                {
                    throw new InvalidDataException($"Schema version '{schemaVersion.GetString()}' is not supported, expected 'mcp.role_bindings.v2'");
                }

                if (!root.TryGetProperty("state_revision", out var stateRevisionElement))
                {
                    throw new InvalidDataException("Missing 'state_revision' field");
                }

                var stateRevision = stateRevisionElement.GetInt32();

                if (!root.TryGetProperty("bindings", out var bindingsElement))
                {
                    throw new InvalidDataException("Missing 'bindings' field");
                }

                var bindings = new Dictionary<string, RoleBinding>();

                foreach (var property in bindingsElement.EnumerateObject())
                {
                    var roleBinding = JsonSerializer.Deserialize<RoleBinding>(property.Value.GetRawText(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
                    if (roleBinding is null)
                    {
                        throw new InvalidDataException($"Il binding di ruolo '{property.Name}' non può essere deserializzato perché il valore JSON è null.");
                    }
                    bindings[property.Name] = roleBinding;
                }

                return new RoleBindingConfiguration(stateRevision, bindings);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("Failed to parse JSON", ex);
            }
        }

        public RoleBinding? TryGetBinding(string employeeId)
        {
            return Bindings.TryGetValue(employeeId, out var binding) ? binding : null;
        }

        private RoleBindingConfiguration(int stateRevision, IReadOnlyDictionary<string, RoleBinding> bindings)
        {
            StateRevision = stateRevision;
            Bindings = bindings;
        }
    }
}
