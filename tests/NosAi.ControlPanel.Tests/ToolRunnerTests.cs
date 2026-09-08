using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using NosAi.ControlPanel;
using Xunit;

namespace NosAi.ControlPanel.Tests;

/// <summary>
/// Ogni comando del runtime stampa il proprio riepilogo alla fine, e il pannello
/// decide su quel riepilogo: se le ultime righe si perdono, una card dichiara
/// «nessun dato» su una registrazione che il dato ce l'ha.
/// </summary>
public sealed class ToolRunnerTests
{
    private const int Lines = 400;

    /// <summary>Un processo che stampa molte righe e termina immediatamente.</summary>
    private static (string File, string Arguments) Chatterbox() =>
        OperatingSystem.IsWindows()
            ? ("cmd.exe", $"/c for /L %i in (1,1,{Lines}) do @echo riga%i")
            : ("/bin/sh", $"-c \"i=1; while [ $i -le {Lines} ]; do echo riga$i; i=$((i+1)); done\"");

    [Fact]
    public async Task LUltimaRigaStampataEntraNellOutput()
    {
        var (file, arguments) = Chatterbox();

        ToolResult result = await ToolRunner.RunAsync(file, arguments, Path.GetTempPath());

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"riga{Lines}", result.Output, StringComparison.Ordinal);
        Assert.Contains("riga1\n", result.Output.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NessunaRigaArrivaDopoCheLaChiamataEGiaTornata()
    {
        var (file, arguments) = Chatterbox();
        var seen = new ConcurrentQueue<string>();

        ToolResult result = await ToolRunner.RunAsync(file, arguments, Path.GetTempPath(), seen.Enqueue);
        int deliveredWhenItReturned = seen.Count;

        // Il conteggio si legge subito dopo l'await: se la lettura asincrona non
        // fosse stata attesa, qui mancherebbe la coda delle righe -- che e'
        // esattamente il difetto per cui la card T-12 mostrava zero pacchetti.
        Assert.Equal(Lines, deliveredWhenItReturned);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task UnComandoInesistenteTornaUnErrore()
    {
        await Assert.ThrowsAnyAsync<Exception>(() =>
            ToolRunner.RunAsync("nosai-questo-comando-non-esiste", "", Path.GetTempPath()));
    }
}
