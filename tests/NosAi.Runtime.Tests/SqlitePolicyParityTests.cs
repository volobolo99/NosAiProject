using NosAi.Runtime.Gate2;
using NosAi.Storage.Infrastructure;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Two SQLite policies existed and had already drifted.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SqliteStoragePolicy"/> is the one applied to a real connection and
/// kept at parity with <c>nosai/storage/sqlite_policy.py</c>.
/// <see cref="CentralizedSqlitePolicy"/> only renders a PRAGMA script, and its own
/// suite asserted the script contained the lines it had itself just written —
/// which is true of any values at all, including wrong ones.
/// </para>
/// <para>
/// These tests are the reason the two cannot drift again. They fail on the numbers
/// rather than on the shape of the string.
/// </para>
/// </remarks>
public sealed class SqlitePolicyParityTests
{
    [Fact]
    public void The_script_policy_takes_its_values_from_the_applied_policy()
    {
        var script = new CentralizedSqlitePolicy();

        Assert.Equal(SqliteStoragePolicy.JournalMode, script.JournalMode);
        Assert.Equal(SqliteStoragePolicy.Synchronous, script.SynchronousMode);
        Assert.Equal(SqliteStoragePolicy.CacheSizeKiB, script.CacheSizeKiloBytes);
        Assert.Equal(SqliteStoragePolicy.JournalSizeLimitBytes, script.JournalSizeLimitBytes);
    }

    /// <summary>
    /// The values themselves, pinned against <c>nosai/storage/sqlite_policy.py</c>.
    /// A change here has to be made on both sides deliberately.
    /// </summary>
    [Fact]
    public void The_applied_policy_matches_the_python_tooling()
    {
        Assert.Equal("WAL", SqliteStoragePolicy.JournalMode);
        Assert.Equal("FULL", SqliteStoragePolicy.Synchronous);
        Assert.Equal(65536, SqliteStoragePolicy.CacheSizeKiB);
        Assert.Equal(64L * 1024 * 1024, SqliteStoragePolicy.JournalSizeLimitBytes);
    }

    /// <summary>
    /// The pragma that was missing. Without it SQLite accepts a row whose foreign
    /// key points at nothing, and the corruption is found on the read, far from
    /// the write that caused it.
    /// </summary>
    [Fact]
    public void The_script_turns_foreign_keys_on()
    {
        string script = new CentralizedSqlitePolicy().BuildPragmaInitializationScript();

        Assert.Contains("PRAGMA foreign_keys = ON;", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_script_bounds_the_wal_the_way_the_python_policy_does()
    {
        string script = new CentralizedSqlitePolicy().BuildPragmaInitializationScript();

        Assert.Contains($"PRAGMA journal_size_limit = {64L * 1024 * 1024};", script, StringComparison.Ordinal);
        Assert.Contains("PRAGMA cache_size = -65536;", script, StringComparison.Ordinal);
    }
}
