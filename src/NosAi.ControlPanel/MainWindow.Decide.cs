using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;

namespace NosAi.ControlPanel;

public partial class MainWindow
{
    private CancellationTokenSource? _decideCancellation;

    // The decision cycle is not a run that ends: the Gate 1 host keeps listening until the
    // operator stops it. Rows are appended while they arrive and classified as they go; the
    // summary is written when the process exits or the cancellation token fires.
    private async void OnStartDecideCycle(object sender, RoutedEventArgs e)
    {
        if (_decideCancellation != null)
        {
            DecideSummary.Text = "Una corsa del ciclo di decisione è già in corso: premi Ferma per arrestarla.";
            return;
        }

        string endpoint = string.IsNullOrWhiteSpace(DecideEndpoint.Text)
            ? SettingObserveGame.Text
            : DecideEndpoint.Text;

        if (!DecideCycleCard.TryValidateStart(endpoint, DecideIntervalMs.Text, out int interval, out string? refusal))
        {
            DecideSummary.Text = refusal ?? "Avvio del ciclo di decisione rifiutato.";
            return;
        }

        var dll = ResolveRuntimeDll();
        if (dll is null)
        {
            DecideSummary.Text = "Runtime non compilato. Vai su Certificazione e premi Compila runtime.";
            return;
        }

        DecideExpectation.Text = DecideCycleCard.DescribeExpectation();
        DecideOutput.Text = string.Empty;
        DecideSummary.Text = string.Empty;

        var cancellation = new CancellationTokenSource();
        _decideCancellation = cancellation;
        DecideStartButton.IsEnabled = false;
        DecideStopButton.IsEnabled = true;

        int totalLines = 0;
        int noWorldStateLines = 0;
        int observationAttachedLines = 0;
        int executionDisabledLines = 0;
        bool stoppedByOperator = false;

        try
        {
            var arguments = string.Format(
                CultureInfo.InvariantCulture,
                "\"{0}\" --decide --observe-game {1} --decide-interval-ms {2}",
                dll,
                endpoint,
                interval);

            var result = await ToolRunner.RunAsync(
                "dotnet",
                arguments,
                _repoRoot,
                line =>
                {
                    // ToolRunner invokes onLine from the output-reading thread: marshal every
                    // update onto the UI thread so rows appear while they arrive.
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        DecideOutput.AppendText(line + Environment.NewLine);
                        DecideOutput.ScrollToEnd();

                        totalLines++;
                        var kind = DecideCycleCard.ClassifyDecideLine(line);
                        if (kind == DecideCycleCard.NoWorldStateKind)
                        {
                            noWorldStateLines++;
                        }
                        else if (kind == DecideCycleCard.ObservationAttachedKind)
                        {
                            observationAttachedLines++;
                        }
                        else if (kind == DecideCycleCard.ExecutionDisabledKind)
                        {
                            executionDisabledLines++;
                        }
                    }));
                },
                cancellation.Token);

            stoppedByOperator = cancellation.IsCancellationRequested;
            if (!stoppedByOperator && result.ExitCode != 0)
            {
                _log.Operator($"Ciclo di decisione dal vivo terminato con exit code {result.ExitCode}.");
            }
        }
        catch (OperationCanceledException)
        {
            // A run stopped by the operator is not an error and is not reported as one:
            // the summary below reports it as stopped by the operator.
            stoppedByOperator = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("Esecuzione del ciclo di decisione dal vivo fallita.", ex);
            DecideSummary.Text = $"Fallito: {ex.Message}";
            return;
        }
        finally
        {
            cancellation.Dispose();
            _decideCancellation = null;
            DecideStartButton.IsEnabled = true;
            DecideStopButton.IsEnabled = false;
        }

        DecideSummary.Text = DecideCycleCard.SummariseRun(
            noWorldStateLines,
            observationAttachedLines,
            executionDisabledLines,
            totalLines,
            stoppedByOperator);
    }

    private void OnStopDecideCycle(object sender, RoutedEventArgs e)
    {
        if (_decideCancellation == null)
        {
            DecideSummary.Text = "Nessuna corsa del ciclo di decisione in corso.";
            return;
        }

        _decideCancellation.Cancel();
        DecideSummary.Text = "Corsa del ciclo di decisione fermata dall'operatore: attendo la chiusura del processo.";
    }
}
