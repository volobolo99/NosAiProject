using System;
using System.IO;
using System.Text.RegularExpressions;
using NosAi.Runtime.Gate1;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The wire format is implemented twice, in C# and in the Python package, and until now
/// nothing tied the two together: raising <see cref="WireHeader.CurrentVersion"/> without
/// touching <c>nosai/network/wire_protocol.py</c> left the two ends speaking different
/// languages with every suite still green. These tests read the Python source and fail the
/// moment the constants drift apart.
/// </summary>
public sealed class WireProtocolCrossStackTests
{
    [Fact]
    public void TheTwoStacksAgreeOnTheWireVersion()
    {
        string python = PythonWireProtocolSource();
        Match version = Regex.Match(python, @"^VERSION\s*=\s*(\d+)\s*$", RegexOptions.Multiline);

        Assert.True(version.Success, "nosai/network/wire_protocol.py must declare VERSION = <n>.");
        Assert.Equal(WireHeader.CurrentVersion, byte.Parse(version.Groups[1].Value));
    }

    [Fact]
    public void TheTwoStacksAgreeOnTheMagic()
    {
        string python = PythonWireProtocolSource();
        Match magic = Regex.Match(python, @"^MAGIC\s*=\s*b""([A-Za-z0-9]{4})""\s*$", RegexOptions.Multiline);

        Assert.True(magic.Success, @"nosai/network/wire_protocol.py must declare MAGIC = b""XXXX"".");

        // The C# side keeps the four bytes packed big-endian in a single constant.
        string csharpMagic = string.Concat(
            (char)(WireHeader.ExpectedMagic >> 24),
            (char)((WireHeader.ExpectedMagic >> 16) & 0xFF),
            (char)((WireHeader.ExpectedMagic >> 8) & 0xFF),
            (char)(WireHeader.ExpectedMagic & 0xFF));

        Assert.Equal(csharpMagic, magic.Groups[1].Value);
    }

    private static string PythonWireProtocolSource()
    {
        string path = Path.Combine(RepositoryRoot(), "nosai", "network", "wire_protocol.py");
        Assert.True(File.Exists(path), $"The Python wire implementation is missing at {path}.");
        return File.ReadAllText(path);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found.");
        return directory!.FullName;
    }
}
