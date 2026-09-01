using System.IO;
using NosAi.Storage;
using Xunit;

namespace NosAi.Core.Tests;

[Trait("Category", "Gate1")]
public sealed class VolumeLocatorTests
{
    [Fact]
    public void ResolvesARealAttachedVolumeByItsActualLabel()
    {
        DriveInfo? realDrive = Array.Find(DriveInfo.GetDrives(), d => d.IsReady && !string.IsNullOrEmpty(TryGetLabel(d)));
        Assert.NotNull(realDrive); // The test host must have at least one ready, labeled volume.

        string label = realDrive!.VolumeLabel;
        Assert.True(VolumeLocator.TryResolve(label, out string root));
        Assert.Equal(realDrive.RootDirectory.FullName, root);
    }

    [Fact]
    public void ReturnsFalseForAVolumeLabelThatIsNotAttached()
    {
        string missingLabel = $"NOSAI-TEST-MISSING-{Guid.NewGuid():N}";

        Assert.False(VolumeLocator.TryResolve(missingLabel, out string root));
        Assert.Equal(string.Empty, root);
    }

    [Fact]
    public void ResolveDatabasePathThrowsRatherThanFallingBackToAnotherDrive()
    {
        var options = new SqliteJournalOptions(VolumeLabel: $"NOSAI-TEST-MISSING-{Guid.NewGuid():N}");

        Assert.Throws<InvalidOperationException>(() => VolumeLocator.ResolveDatabasePath(options));
    }

    private static string TryGetLabel(DriveInfo drive)
    {
        try
        {
            return drive.VolumeLabel;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }
}
