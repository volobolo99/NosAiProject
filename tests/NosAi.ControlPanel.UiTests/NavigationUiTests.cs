using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Automation;
using Xunit;

namespace NosAi.ControlPanel.UiTests
{
    public sealed class NavigationUiTests : IDisposable
    {
        private UiAutopilot? _autopilot;

        [Fact]
        [Trait("Category", "UiAutopilot")]
        public void ClickingClientNav_ChangesPageTitle()
        {
            string? repoRoot = FindRepoRoot();
            Assert.True(repoRoot is not null, "Radice del repository non trovata risalendo da AppContext.BaseDirectory.");

            string binRoot = Path.Combine(repoRoot!, "src", "NosAi.ControlPanel", "bin");
            string? exePath = Directory.Exists(binRoot)
                ? Directory.GetFiles(binRoot, "NosAi.ControlPanel.exe", SearchOption.AllDirectories).FirstOrDefault()
                : null;
            Assert.True(exePath is not null, "NosAi.ControlPanel.exe non trovato: eseguire prima 'dotnet build' sul ControlPanel.");

            bool launched = UiAutopilot.TryLaunch(exePath!, TimeSpan.FromSeconds(20), out _autopilot, out var unavailable);
            Assert.True(launched, $"ControlPanel non avviato: {unavailable?.Reason}");

            bool foundNav = _autopilot!.TryFindByAutomationId("NavClient", TimeSpan.FromSeconds(10), out var navButton);
            Assert.True(foundNav, "Pulsante di navigazione NavClient non trovato.");

            bool clicked = _autopilot.TryClickPhysical(navButton!);
            Assert.True(clicked, "Click fisico su NavClient non riuscito.");

            Thread.Sleep(400);

            bool foundTitle = _autopilot.TryFindByAutomationId("PageTitle", TimeSpan.FromSeconds(10), out var pageTitle);
            Assert.True(foundTitle, "TextBlock PageTitle non trovato.");

            Assert.Equal("Client NosTale", _autopilot.GetName(pageTitle!));

            if (_autopilot.TryCaptureWindowScreenshot(out var png) && png is not null)
            {
                string evidenceDir = Path.Combine(repoRoot!, "data", "ui_autopilot");
                Directory.CreateDirectory(evidenceDir);
                File.WriteAllBytes(Path.Combine(evidenceDir, "navigation_client.png"), png);
            }
        }

        private static string? FindRepoRoot()
        {
            string? current = AppContext.BaseDirectory;
            while (current is not null && !File.Exists(Path.Combine(current, "NosAi.sln")))
            {
                current = Directory.GetParent(current)?.FullName;
            }
            return current;
        }

        public void Dispose()
        {
            _autopilot?.Dispose();
        }
    }
}
