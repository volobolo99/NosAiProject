using NosAi.Runtime.Hardware;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Pins the rule that decides how much video memory the runtime believes it has.
/// <c>Win32_VideoController.AdapterRAM</c> is a <c>UInt32</c> and saturates at 4095 MB, so on
/// this machine an 8151 MB card was reported as 4095 and, being one megabyte short of the
/// 4096 threshold, scored graphics tier 2 instead of 4. The rule is kept free of I/O so it
/// can be verified without a graphics card.
/// </summary>
public sealed class VramDetectionTests
{
    private const long SaturatedWmiMb = 4095;
    private const long RealCardMb = 8151;

    [Fact]
    public void SaturatedWmiReadingYieldsToTheRegistry()
    {
        Assert.Equal(RealCardMb, WindowsHardwareProbe.ChooseVramMb(SaturatedWmiMb, RealCardMb));
    }

    [Fact]
    public void TrustworthyWmiReadingIsKept()
    {
        Assert.Equal(2048, WindowsHardwareProbe.ChooseVramMb(2048, 8151));
    }

    [Fact]
    public void MissingWmiReadingFallsBackToTheRegistry()
    {
        Assert.Equal(RealCardMb, WindowsHardwareProbe.ChooseVramMb(0, RealCardMb));
    }

    [Fact]
    public void ASmallerRegistryReadingNeverShrinksASaturatedOne()
    {
        Assert.Equal(SaturatedWmiMb, WindowsHardwareProbe.ChooseVramMb(SaturatedWmiMb, 2048));
    }

    [Fact]
    public void NeitherSourceAvailableReportsNothing()
    {
        Assert.Equal(0, WindowsHardwareProbe.ChooseVramMb(0, 0));
    }

    [Fact]
    public void TheSaturatedReadingCostsAGraphicsTier()
    {
        var autoSettings = new HardwareAutoSettings();

        RuntimeSettings saturated = autoSettings.Calculate(Fingerprint(SaturatedWmiMb));
        RuntimeSettings real = autoSettings.Calculate(Fingerprint(RealCardMb));

        Assert.Equal(2, saturated.GraphicsTier);
        Assert.Equal(3, real.GraphicsTier);
    }

    /// <summary>
    /// Recorded, not asserted as desirable: a card sold as 8 GB exposes 8151 MB because part of
    /// the memory is reserved, so the top graphics tier at <c>&gt;= 8192</c> is unreachable for
    /// the whole 8 GB class. Raising that threshold changes autoscaling behaviour and is left
    /// as a separate decision rather than smuggled in with a detection fix.
    /// </summary>
    [Fact]
    public void TheTopGraphicsTierIsOutOfReachForAnEightGigabyteCard()
    {
        RuntimeSettings real = new HardwareAutoSettings().Calculate(Fingerprint(RealCardMb));

        Assert.True(RealCardMb < 8192);
        Assert.NotEqual(4, real.GraphicsTier);
    }

    private static HardwareFingerprint Fingerprint(long gpuMemoryMb) =>
        new(
            "Windows",
            "AMD Ryzen 7 260 w/ Radeon 780M Graphics",
            16,
            14140,
            "NVIDIA GeForce RTX 5060 Laptop GPU",
            gpuMemoryMb,
            60,
            "Microsoft Windows NT 10.0.26200.0");
}
