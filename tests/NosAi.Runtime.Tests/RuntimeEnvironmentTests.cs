using System.Runtime.InteropServices;
using System.Security.Cryptography;
using NosAi.Runtime.Configuration;
using NosAi.Runtime.Gate1;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// <see cref="RuntimeEnvironmentValidator"/>, the preflight M011 added: the preconditions
/// the runtime depends on are established before it boots, and a required one
/// that does not hold stops the boot instead of surfacing later as an exception
/// from inside key custody.
/// </summary>
/// <remarks>
/// No mocking framework and no fake filesystem: every filesystem assertion runs
/// against a real directory under the OS temp path, and every key assertion
/// against a real RSA key.
/// </remarks>
public sealed class RuntimeEnvironmentTests : IDisposable
{
    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "nosai-env-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
            Directory.Delete(_dataDirectory, recursive: true);
    }

    private static Gate1HostOptions OptionsWithoutKey() => new() { DashboardPort = 0, GuardPort = 0 };

    private static Gate1HostOptions OptionsWithKey(string pem, string source = "test") => new()
    {
        DashboardPort = 0,
        GuardPort = 0,
        TrustedGuardPublicKeyPem = pem,
        TrustedGuardPublicKeySource = source
    };

    [Fact]
    public void AWritableDataDirectoryPassesAndIsReportedByItsFullPath()
    {
        EnvironmentReport report = RuntimeEnvironmentValidator.Validate(OptionsWithoutKey(), _dataDirectory);

        EnvironmentCheck check = report.Checks.Single(c => c.Name == "data.directory.writable");
        Assert.Equal(EnvironmentCheckStatus.Passed, check.Status);
        Assert.True(check.Required);
        Assert.Contains(Path.GetFullPath(_dataDirectory), check.Detail);
    }

    [Fact]
    public void TheDataDirectoryIsCreatedWhenItIsMissingRatherThanReportedAsAFailure()
    {
        Assert.False(Directory.Exists(_dataDirectory));

        EnvironmentReport report = RuntimeEnvironmentValidator.Validate(OptionsWithoutKey(), _dataDirectory);

        Assert.Equal(EnvironmentCheckStatus.Passed,
            report.Checks.Single(c => c.Name == "data.directory.writable").Status);
        Assert.True(Directory.Exists(_dataDirectory));
    }

    [Fact]
    public void TheWriteProbeLeavesNothingBehindInTheDataDirectory()
    {
        RuntimeEnvironmentValidator.Validate(OptionsWithoutKey(), _dataDirectory);

        // Writability is established by writing, so the evidence has to be cleaned
        // up: a probe file left in data/ would be indistinguishable from runtime
        // state to everything that reads that directory afterwards.
        Assert.Empty(Directory.GetFileSystemEntries(_dataDirectory));
    }

    [Fact]
    public void ADataDirectoryPathBlockedByAFileFailsAndBlocksTheBoot()
    {
        // A regular file where the directory should be: CreateDirectory cannot
        // succeed, which is the same shape as a path the operator mistyped.
        File.WriteAllText(_dataDirectory, "not a directory");
        try
        {
            EnvironmentReport report = RuntimeEnvironmentValidator.Validate(OptionsWithoutKey(), _dataDirectory);

            EnvironmentCheck check = report.Checks.Single(c => c.Name == "data.directory.writable");
            Assert.Equal(EnvironmentCheckStatus.Failed, check.Status);
            Assert.False(report.IsSatisfied);
            Assert.Contains(check, report.Blocking);
        }
        finally
        {
            File.Delete(_dataDirectory);
        }
    }

    [Fact]
    public void NoConfiguredGuardKeyIsUnknownAndAdvisoryRatherThanAFailure()
    {
        EnvironmentReport report = RuntimeEnvironmentValidator.Validate(OptionsWithoutKey(), _dataDirectory);

        EnvironmentCheck check = report.Checks.Single(c => c.Name == "guard.trusted_key");
        // Absent is not the same as broken. The channel still fails closed at the
        // handshake, so this must not stop a runtime from starting unpaired.
        Assert.Equal(EnvironmentCheckStatus.Unknown, check.Status);
        Assert.False(check.Required);
        Assert.True(report.IsSatisfied);
    }

    [Fact]
    public void AConfiguredGuardKeyThatParsesPassesAndReportsItsSize()
    {
        using var key = RSA.Create(2048);

        EnvironmentReport report = RuntimeEnvironmentValidator.Validate(
            OptionsWithKey(key.ExportRSAPublicKeyPem(), "data/guard_public_key.pem"), _dataDirectory);

        EnvironmentCheck check = report.Checks.Single(c => c.Name == "guard.trusted_key");
        Assert.Equal(EnvironmentCheckStatus.Passed, check.Status);
        Assert.True(check.Required);
        Assert.Contains("2048-bit", check.Detail);
        Assert.Contains("data/guard_public_key.pem", check.Detail);
    }

    [Fact]
    public void AConfiguredGuardKeyThatCannotBeParsedFailsAndBlocksTheBoot()
    {
        EnvironmentReport report = RuntimeEnvironmentValidator.Validate(
            OptionsWithKey("-----BEGIN RSA PUBLIC KEY-----\nnot base64\n-----END RSA PUBLIC KEY-----"),
            _dataDirectory);

        EnvironmentCheck check = report.Checks.Single(c => c.Name == "guard.trusted_key");
        // The operator named a specific key. Booting past an unusable one would
        // leave the runtime trusting nothing while looking configured.
        Assert.Equal(EnvironmentCheckStatus.Failed, check.Status);
        Assert.True(check.Required);
        Assert.False(report.IsSatisfied);
    }

    [Fact]
    public void ThePlatformCheckAgreesWithTheHostThisSuiteIsRunningOn()
    {
        EnvironmentReport report = RuntimeEnvironmentValidator.Validate(OptionsWithoutKey(), _dataDirectory);

        EnvironmentCheck check = report.Checks.Single(c => c.Name == "platform.windows");
        Assert.Equal(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? EnvironmentCheckStatus.Passed
                : EnvironmentCheckStatus.Failed,
            check.Status);
        Assert.True(check.Required);
    }

    [Fact]
    public void AnUnknownRequiredCheckBlocksJustAsAFailedOneDoes()
    {
        // The invariant CLAUDE.md states, at the level that enforces it: UNKNOWN is
        // not a quiet pass. A required precondition nobody managed to confirm has
        // to stop the boot exactly as a confirmed failure does.
        var report = new EnvironmentReport(new[]
        {
            new EnvironmentCheck("confirmed.ok", EnvironmentCheckStatus.Passed, "fine", Required: true),
            new EnvironmentCheck("never.ran", EnvironmentCheckStatus.Unknown, "could not be determined", Required: true)
        });

        Assert.False(report.IsSatisfied);
        Assert.Equal("never.ran", report.Blocking.Single().Name);
    }

    [Fact]
    public void AnUnknownAdvisoryCheckDoesNotBlock()
    {
        var report = new EnvironmentReport(new[]
        {
            new EnvironmentCheck("confirmed.ok", EnvironmentCheckStatus.Passed, "fine", Required: true),
            new EnvironmentCheck("nice.to.have", EnvironmentCheckStatus.Unknown, "not configured", Required: false)
        });

        Assert.True(report.IsSatisfied);
        Assert.Empty(report.Blocking);
    }

    [Fact]
    public void TheExceptionNamesEveryBlockingCheckRatherThanOnlyTheFirst()
    {
        var report = new EnvironmentReport(new[]
        {
            new EnvironmentCheck("first.problem", EnvironmentCheckStatus.Failed, "one", Required: true),
            new EnvironmentCheck("second.problem", EnvironmentCheckStatus.Unknown, "two", Required: true),
            new EnvironmentCheck("fine", EnvironmentCheckStatus.Passed, "ok", Required: true)
        });

        var exception = new RuntimeEnvironmentException(report);

        // One restart per problem is the failure mode this replaces.
        Assert.Contains("first.problem", exception.Message);
        Assert.Contains("second.problem", exception.Message);
        Assert.DoesNotContain("fine", exception.Message);
        Assert.Same(report, exception.Report);
    }

    [Fact]
    public void TheHostRefusesToConstructWhenARequiredPreconditionDoesNotHold()
    {
        var options = new Gate1HostOptions
        {
            DashboardPort = 0,
            GuardPort = 0,
            StartDashboard = false,
            EnableDiscovery = false,
            TrustedGuardPublicKeyPem = "-----BEGIN RSA PUBLIC KEY-----\nbroken\n-----END RSA PUBLIC KEY-----",
            TrustedGuardPublicKeySource = "test"
        };

        var exception = Assert.Throws<RuntimeEnvironmentException>(() => new Gate1BootstrapHost(options));

        Assert.Contains("guard.trusted_key", exception.Message);
        Assert.False(exception.Report.IsSatisfied);
    }

    [Fact]
    public async Task AHostThatConstructsPublishesTheReportThatLetItStart()
    {
        using var key = RSA.Create(2048);
        var options = new Gate1HostOptions
        {
            DashboardPort = 0,
            GuardPort = 0,
            StartDashboard = false,
            EnableDiscovery = false,
            TrustedGuardPublicKeyPem = key.ExportRSAPublicKeyPem(),
            TrustedGuardPublicKeySource = "test"
        };

        await using var host = new Gate1BootstrapHost(options);

        Assert.True(host.EnvironmentReport.IsSatisfied);
        Assert.Empty(host.EnvironmentReport.Blocking);
        Assert.Contains(host.EnvironmentReport.Checks, c => c.Name == "platform.windows");
        Assert.Contains(host.EnvironmentReport.Checks, c => c.Name == "data.directory.writable");
        Assert.Contains(host.EnvironmentReport.Checks, c => c.Name == "guard.trusted_key");
    }
}
