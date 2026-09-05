using NosAi.Core.Testing;

namespace NosAi.Core.Tests;

public sealed class PracticalTestCatalogTests
{
    [Fact]
    public void CatalogContainsExactlyTwentyOrderedTests()
    {
        IReadOnlyList<PracticalTestDefinition> tests = PracticalTestCatalog.All;

        Assert.Equal(20, tests.Count);
        Assert.Equal(Enumerable.Range(1, 20).Select(i => $"T{i}"), tests.Select(x => x.Id));
        Assert.All(tests, test =>
        {
            Assert.False(string.IsNullOrWhiteSpace(test.Name));
            Assert.False(string.IsNullOrWhiteSpace(test.Preconditions));
            Assert.False(string.IsNullOrWhiteSpace(test.OperatorAction));
            Assert.False(string.IsNullOrWhiteSpace(test.ExpectedObservation));
            Assert.True(test.Timeout > TimeSpan.Zero);
        });
    }

    [Fact]
    public void FinalCertificationRequiresLiveClient()
    {
        PracticalTestDefinition finalTest = Assert.Single(PracticalTestCatalog.All, x => x.Id == "T20");

        Assert.Equal(PracticalTestKind.EndToEndCertification, finalTest.Kind);
        Assert.True(finalTest.RequiresLiveClient);
        Assert.Contains("T1-T19", finalTest.Preconditions, StringComparison.Ordinal);
    }

    [Fact]
    public void NoTestDefinitionCanUsePrivilegedServerStateAsPrecondition()
    {
        string[] forbidden = ["server DB", "GM", "admin", "moderator", "server console", "hidden state"];

        foreach (PracticalTestDefinition test in PracticalTestCatalog.All)
        {
            string material = $"{test.Preconditions} {test.OperatorAction} {test.ExpectedObservation}";
            foreach (string term in forbidden)
                Assert.DoesNotContain(term, material, StringComparison.OrdinalIgnoreCase);
        }
    }
}
