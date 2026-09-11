using System;
using System.Windows;
using NosAi.LiveIntegration;

namespace NosAi.ControlPanel;

public partial class MainWindow
{
    // Endpoint-autodetect state: where the current text comes from, when the
    // last detection happened, for which process it happened, and the text the
    // box held before the current edit.
    private EndpointOrigin _endpointOrigin = EndpointOrigin.None;
    private DateTime? _endpointDetectedAtUtc;
    private int? _endpointDetectedForPid;
    private string _endpointTextObserved = "";

    /// <summary>
    /// Called by the panel polling, on the UI thread. When the client is known
    /// and the box is empty or holds a value detected for another process, asks
    /// the network observer for a fresh endpoint and writes the box only when
    /// the suggestion succeeds. The provenance line is refreshed in every case
    /// and the method never throws: a failed observation is a false
    /// <see cref="ObserveGameDetector.TrySuggest"/> that the line reports.
    /// </summary>
    private void RefreshEndpointAutoDetect()
    {
        string? failure = null;
        string? absenceReason = null;

        try
        {
            var classified = _lastSnapshot.ClientProcessId;
            int? clientPid = null;
            if (classified.HasValue && classified.Value is int pid && pid > 0)
            {
                clientPid = pid;
            }
            else
            {
                absenceReason = string.IsNullOrWhiteSpace(classified.FailureReason)
                    ? "process_not_attached"
                    : classified.FailureReason;
            }

            string currentText = SettingObserveGame.Text ?? "";
            if (clientPid.HasValue
                && EndpointAutoDetect.ShouldDetect(true, clientPid.Value, _endpointDetectedForPid, currentText, _endpointOrigin))
            {
                ClientNetworkObservation observation = ClientNetworkObserver.Observe(clientPid.Value);
                if (ObserveGameDetector.TrySuggest(observation, out string endpoint, out string status))
                {
                    SettingObserveGame.Text = endpoint;
                    _endpointOrigin = EndpointOrigin.Detected;
                    _endpointDetectedAtUtc = DateTime.UtcNow;
                    _endpointDetectedForPid = clientPid.Value;
                    _log.Operator($"Endpoint osservato da solo: {endpoint} (PID {clientPid.Value}).");
                }
                else
                {
                    failure = status;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error("Rilevamento automatico dell'endpoint fallito.", ex);
            failure = ex.Message;
        }

        EndpointProvenance.Text = EndpointAutoDetect.DescribeOrigin(
            _endpointOrigin,
            SettingObserveGame.Text ?? "",
            _endpointDetectedAtUtc,
            DateTime.UtcNow,
            failure ?? absenceReason);
    }

    /// <summary>
    /// Wired to <c>SettingObserveGame.TextChanged</c>. Keeps the provenance
    /// honest: a changed value that is not the automatism's own write becomes
    /// <see cref="EndpointOrigin.TypedByOperator"/> and is never overwritten
    /// afterwards; an emptied box returns to <see cref="EndpointOrigin.None"/>.
    /// The automatism's own writes are re-labelled to
    /// <see cref="EndpointOrigin.Detected"/> by <see cref="RefreshEndpointAutoDetect"/>
    /// right after they land.
    /// </summary>
    private void OnEndpointTextChanged(object sender, RoutedEventArgs e)
    {
        string newText = SettingObserveGame.Text ?? "";
        EndpointOrigin next = EndpointAutoDetect.OriginAfterEdit(_endpointOrigin, _endpointTextObserved, newText);

        if (next == EndpointOrigin.TypedByOperator
            && _endpointOrigin == EndpointOrigin.None
            && !_endpointDetectedForPid.HasValue
            && string.IsNullOrWhiteSpace(_endpointTextObserved)
            && string.Equals(newText.Trim(), (_settings.ObserveGame ?? "").Trim(), StringComparison.Ordinal))
        {
            // First fill performed by LoadSettingsIntoForm: the box is receiving
            // the saved endpoint, not a hand-typed one.
            next = EndpointOrigin.FromSettings;
        }

        _endpointOrigin = next;
        _endpointTextObserved = newText;
        EndpointProvenance.Text = EndpointAutoDetect.DescribeOrigin(
            _endpointOrigin,
            newText,
            _endpointDetectedAtUtc,
            DateTime.UtcNow,
            null);
    }
}
