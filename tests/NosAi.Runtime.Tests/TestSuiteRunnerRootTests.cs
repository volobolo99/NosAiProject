using System;
using System.IO;
using NosAi.Runtime.Testing;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Criterio di accettazione della scoperta della radice del repository.
///
/// Il 2026-09-11 <see cref="TestSuiteRunner.FindRepositoryRoot"/> restituiva null,
/// perche' cercava CLAUDE.md nella radice e quel file era stato spostato in .claude/.
/// Nessun test copriva il metodo, quindi il guasto e' passato inosservato mentre
/// cinque comandi del prodotto lo chiamano: Gate1BootstrapHost, KeybindsCheck,
/// CollectCommand, ScoutCommand e SingleStepCommand.
///
/// Le conseguenze misurate: Gate1BootstrapHost restituiva null e la superficie di
/// test del ControlPanel non si inizializzava; gli altri quattro ripiegavano su
/// Directory.GetCurrentDirectory() e leggevano calibrazione e keybind dalla cartella
/// di lancio, cioe' esattamente il "guess that would make every subsequent command
/// run in the wrong directory" che il commento del metodo dichiarava di evitare.
/// </summary>
public sealed class TestSuiteRunnerRootTests
{
    [Fact]
    public void LaRadiceSiTrovaDallaCartellaDellAssembly()
    {
        string? root = TestSuiteRunner.FindRepositoryRoot();

        Assert.NotNull(root);
        Assert.True(File.Exists(Path.Combine(root!, "NosAi.sln")),
            $"la radice trovata non contiene NosAi.sln: {root}");
        Assert.True(Directory.Exists(Path.Combine(root!, "tests")),
            $"la radice trovata non contiene tests/: {root}");
    }

    [Fact]
    public void LaRadiceSiTrovaDallaCartellaCorrente()
    {
        string? root = TestSuiteRunner.FindRepositoryRoot(Environment.CurrentDirectory);

        Assert.NotNull(root);
        Assert.True(File.Exists(Path.Combine(root!, "NosAi.sln")),
            $"la radice trovata non contiene NosAi.sln: {root}");
    }

    [Fact]
    public void LaRadiceNonDipendeDaUnFileSpostabile()
    {
        // CLAUDE.md non e' piu' nella radice: se la scoperta ne dipendesse, tornerebbe null.
        string? root = TestSuiteRunner.FindRepositoryRoot();

        Assert.NotNull(root);
        Assert.False(File.Exists(Path.Combine(root!, "CLAUDE.md")),
            "CLAUDE.md e' tornato nella radice: aggiorna questo test, non il marcatore");
        Assert.True(File.Exists(Path.Combine(root!, ".claude", "CLAUDE.md")),
            "le regole vivono in .claude/CLAUDE.md");
    }

    [Fact]
    public void FuoriDaUnRepositoryRestituisceNullSenzaIndovinare()
    {
        // Il contratto del metodo e' esplicito: meglio null che una radice inventata.
        string temporanea = Path.Combine(Path.GetTempPath(), "nosai-fuori-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporanea);
        try
        {
            Assert.Null(TestSuiteRunner.FindRepositoryRoot(temporanea));
        }
        finally
        {
            Directory.Delete(temporanea, recursive: true);
        }
    }

    [Fact]
    public void LaRadiceEStabileFraLeDueChiamate()
    {
        // I cinque chiamanti usano il pattern FindRepositoryRoot(cwd) ?? FindRepositoryRoot():
        // se le due strade dessero radici diverse, leggerebbero configurazioni diverse.
        string? daCorrente = TestSuiteRunner.FindRepositoryRoot(Environment.CurrentDirectory);
        string? daAssembly = TestSuiteRunner.FindRepositoryRoot();

        Assert.NotNull(daCorrente);
        Assert.NotNull(daAssembly);
        Assert.Equal(
            Path.GetFullPath(daAssembly!).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(daCorrente!).TrimEnd(Path.DirectorySeparatorChar));
    }
}
