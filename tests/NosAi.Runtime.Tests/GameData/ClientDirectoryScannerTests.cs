using NosAi.Runtime.GameData;
using Xunit;

namespace NosAi.Runtime.Tests.GameData;

public sealed class ClientDirectoryScannerTests
{
    [Fact]
    public void AMissingDirectory_ScansToAnEmptyList_RatherThanThrowing()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"nosai-missing-{Guid.NewGuid():N}");

        Assert.Empty(ClientDirectoryScanner.Scan(missing));
    }

    [Fact]
    public void AValidNamedArchive_IsClassifiedByItsRealFormat_NotAsUnsupported()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"nosai-scan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            // One named entry: count=1, flag=0, nameLen=4 "a.SO", flag2=0, size=3 "abc".
            byte[] archive = BuildNamedArchive("a.SO", new byte[] { (byte)'a', (byte)'b', (byte)'c' });
            File.WriteAllBytes(Path.Combine(directory, "one.NOS"), archive);

            ClientInventoryEntry entry = Assert.Single(ClientDirectoryScanner.Scan(directory));

            Assert.Equal("one.NOS", entry.File);
            Assert.Equal(nameof(NosArchiveFormat.NamedEntries), entry.ArchiveType);
            Assert.Contains("entries=1", entry.Details);
            Assert.Null(entry.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AFileThatIsNotAnArchive_CarriesTheNamedFailureReason_RatherThanAFabricatedType()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"nosai-scan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "tiny.bin"), new byte[] { 1, 2 });

            ClientInventoryEntry entry = Assert.Single(ClientDirectoryScanner.Scan(directory));

            Assert.Equal("tiny.bin", entry.File);
            Assert.Null(entry.ArchiveType);
            Assert.Null(entry.Details);
            Assert.StartsWith("file_too_short", entry.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void NestedFiles_AreReported_WithAPathRelativeToTheScannedDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"nosai-scan-{Guid.NewGuid():N}");
        string nested = Path.Combine(directory, "sub");
        Directory.CreateDirectory(nested);
        try
        {
            File.WriteAllBytes(Path.Combine(nested, "tiny.bin"), new byte[] { 1, 2 });

            ClientInventoryEntry entry = Assert.Single(ClientDirectoryScanner.Scan(directory));

            Assert.Equal(Path.Combine("sub", "tiny.bin"), entry.File);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] BuildNamedArchive(string name, byte[] content)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(1u); // count
        writer.Write(0u); // flag
        writer.Write((uint)name.Length);
        writer.Write(System.Text.Encoding.Latin1.GetBytes(name));
        writer.Write(0u); // second flag
        writer.Write((uint)content.Length);
        writer.Write(content);
        writer.Write(new byte[12]); // the trailer every named archive carries
        return stream.ToArray();
    }
}
