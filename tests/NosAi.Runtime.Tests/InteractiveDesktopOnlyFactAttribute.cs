using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// FactAttribute che skippa onestamente i test di regime DPI per-monitor quando l'ambiente non può esibire quel comportamento:
/// non Windows, oppure un runner CI headless senza sessione desktop reale.
/// </summary>
/// <remarks>
/// Il workflow GitHub Actions 'NosAi .NET (Windows)' fallisce su due test di ClientWindowDpiProbeTests.cs perché
/// il runner CI Windows-hosted riporta 'unaware' per ogni via di lancio, mentre la macchina reale dell'operatore
/// distingue permonitorv2/permonitor. Questo attributo gestisce sia il caso di sistema non Windows,
/// sia il caso di ambiente CI headless che non supporta il regime DPI per-monitor reale.
/// </remarks>
public sealed class InteractiveDesktopOnlyFactAttribute : FactAttribute
{
    public InteractiveDesktopOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Test esercita un regime DPI Windows-only.";
        }
        else if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            Skip = "Il runner CI è headless e non ha una sessione desktop interattiva con virtualizzazione DPI per-monitor reale.";
        }
    }
}
