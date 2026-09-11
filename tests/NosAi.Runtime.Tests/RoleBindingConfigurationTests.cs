using System;
using System.IO;
using NosAi.Runtime.Configuration;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Copre il canale 2 di ADR-0030 (Python -> C# in ingresso): RoleBindingConfiguration
/// legge l'export che nosai/mcp/bindings.py scrive su data/mcp/role_bindings.json.
/// </summary>
public sealed class RoleBindingConfigurationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "nosai-role-bindings-" + Guid.NewGuid().ToString("N"));

    public RoleBindingConfigurationTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private string PathFor(string name) => Path.Combine(_directory, name);

    [Fact]
    public void Load_OnAMissingFile_ReturnsEmpty_WithoutThrowing()
    {
        RoleBindingConfiguration configuration = RoleBindingConfiguration.Load(PathFor("does-not-exist.json"));

        Assert.Equal(0, configuration.StateRevision);
        Assert.Empty(configuration.Bindings);
    }

    [Fact]
    public void Load_OnAValidExport_PopulatesEveryBinding_AndIgnoresProposals()
    {
        string path = PathFor("role_bindings.json");
        File.WriteAllText(path, """
        {
          "schema_version": "mcp.role_bindings.v2",
          "state_revision": 7,
          "bindings": {
            "employee.coding": {
              "employee_id": "employee.coding",
              "primary_model": "deepseek-v4-flash",
              "fallback_models": ["qwen/qwen3-coder-30b-a3b-instruct"],
              "state": "active",
              "version": 2,
              "proposal_id": null,
              "updated_at": "2026-09-11T08:00:00Z",
              "candidate_digest": "",
              "author_id": "mcp_chief"
            },
            "employee.documentation": {
              "employee_id": "employee.documentation",
              "primary_model": "qwen2.5-coder:7b",
              "fallback_models": [],
              "state": "active",
              "version": 1,
              "proposal_id": null,
              "updated_at": "2026-09-11T08:00:00Z",
              "candidate_digest": "",
              "author_id": "mcp_chief"
            }
          },
          "proposals": {
            "prop-1": { "employee_id": "employee.coding", "state": "shadow" }
          }
        }
        """);

        RoleBindingConfiguration configuration = RoleBindingConfiguration.Load(path);

        Assert.Equal(7, configuration.StateRevision);
        Assert.Equal(2, configuration.Bindings.Count);

        RoleBinding coding = Assert.Single(configuration.Bindings, kvp => kvp.Key == "employee.coding").Value;
        Assert.Equal("employee.coding", coding.EmployeeId);
        Assert.Equal("deepseek-v4-flash", coding.PrimaryModel);
        Assert.Equal(new[] { "qwen/qwen3-coder-30b-a3b-instruct" }, coding.FallbackModels);
        Assert.Equal("active", coding.State);
        Assert.Equal(2, coding.Version);

        RoleBinding documentation = configuration.TryGetBinding("employee.documentation")!;
        Assert.NotNull(documentation);
        Assert.Empty(documentation.FallbackModels);
    }

    [Fact]
    public void Load_OnAnUnknownSchemaVersion_ThrowsAndNamesTheValueFound()
    {
        string path = PathFor("wrong-schema.json");
        File.WriteAllText(path, """{ "schema_version": "mcp.role_bindings.v1", "state_revision": 1, "bindings": {} }""");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => RoleBindingConfiguration.Load(path));
        Assert.Contains("mcp.role_bindings.v1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_OnMalformedJson_Throws()
    {
        string path = PathFor("malformed.json");
        File.WriteAllText(path, "{ not json");

        Assert.Throws<InvalidDataException>(() => RoleBindingConfiguration.Load(path));
    }

    [Fact]
    public void Load_OnANullBindingValue_ThrowsAndNamesTheEmployeeId()
    {
        string path = PathFor("null-binding.json");
        File.WriteAllText(path, """{ "schema_version": "mcp.role_bindings.v2", "state_revision": 1, "bindings": { "employee.coding": null } }""");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => RoleBindingConfiguration.Load(path));
        Assert.Contains("employee.coding", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetBinding_OnAnUnknownEmployeeId_ReturnsNull()
    {
        Assert.Null(RoleBindingConfiguration.Empty.TryGetBinding("employee.unknown"));
    }
}
