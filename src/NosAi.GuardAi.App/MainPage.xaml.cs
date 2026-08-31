using System.Text;
using NosAi.GuardClient;

namespace NosAi.GuardAi.App;

/// <summary>
/// The operator screen: pick a transport, connect, read the state.
/// </summary>
/// <remarks>
/// There is deliberately nothing here about keys, addresses or pairing. The
/// device identity is loaded and published for enrollment by
/// <see cref="DeviceIdentity"/>, and the runtime's address is discovered, so the
/// only thing left to decide is USB or Wi-Fi.
/// </remarks>
public partial class MainPage : ContentPage
{
    private readonly GuardConnectionService _connection;
    private readonly IDeviceSigner _deviceKey;
    private GuardTransport _transport;
    private bool _applyingStoredChoice;

    public MainPage()
    {
        InitializeComponent();

        _deviceKey = DeviceIdentity.LoadOrCreateSigner();
        DeviceIdentity.PublishPublicKey(_deviceKey);

        _connection = new GuardConnectionService(_deviceKey, RuntimePin.Load());
        _connection.StatusChanged += OnStatusChanged;

        // Restoring the stored choice fires CheckedChanged, which would write the
        // value straight back; the guard keeps startup from looking like a change.
        _applyingStoredChoice = true;
        _transport = TransportPreference.Load();
        UsbOption.IsChecked = _transport == GuardTransport.Usb;
        WiFiOption.IsChecked = _transport == GuardTransport.WiFi;
        _applyingStoredChoice = false;

        UpdateTransportHint();
        CustodyLabel.Text = _connection.KeyCustody switch
        {
            DeviceKeyCustody.PlatformKeyStore => "Chiave del dispositivo: Android Keystore.",
            DeviceKeyCustody.AppPrivateFile =>
                "Chiave del dispositivo: file nell'app. Il Keystore non è disponibile su questo telefono"
                + (DeviceIdentity.KeyStoreUnavailableReason is { } why ? $" ({why})." : "."),
            _ => "Chiave del dispositivo: custodia sconosciuta."
        };
        Render(GuardStatus.Idle);
    }

    private void OnTransportChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (!e.Value || _applyingStoredChoice)
            return;

        _transport = ReferenceEquals(sender, WiFiOption) ? GuardTransport.WiFi : GuardTransport.Usb;
        TransportPreference.Save(_transport);
        UpdateTransportHint();
    }

    private void UpdateTransportHint() => TransportHint.Text = _transport switch
    {
        GuardTransport.WiFi => "Il PC viene cercato sulla rete. Nessun indirizzo da inserire.",
        _ => "Telefono collegato al PC via cavo USB."
    };

    private async void OnConnectClicked(object? sender, EventArgs e)
    {
        ConnectButton.IsEnabled = false;
        await _connection.ConnectAsync(_transport);
    }

    private async void OnDisconnectClicked(object? sender, EventArgs e)
    {
        await _connection.StopAsync();
        Render(GuardStatus.Idle);
    }

    private void OnStatusChanged(GuardStatus status) => MainThread.BeginInvokeOnMainThread(() => Render(status));

    private void Render(GuardStatus status)
    {
        StateLabel.Text = status.State switch
        {
            GuardLinkState.Idle => "NON CONNESSO",
            GuardLinkState.Searching => "RICERCA…",
            GuardLinkState.Connecting => "CONNESSIONE…",
            GuardLinkState.Connected => "CONNESSO",
            // Distinct from NON CONNESSO: the app is working on it and the
            // operator has nothing to do. Failed means the opposite.
            GuardLinkState.Reconnecting => "RICONNESSIONE…",
            _ => "NON CONNESSO"
        };

        StateLabel.TextColor = status.State switch
        {
            GuardLinkState.Connected => Colors.Green,
            GuardLinkState.Failed => Colors.OrangeRed,
            GuardLinkState.Reconnecting => Colors.DarkOrange,
            _ => Colors.Gray
        };

        DetailLabel.Text = BuildDetail(status);

        // No timestamp unless connected: a stale "last seen" beside a dead link
        // reads as if the data were still current.
        ObservedLabel.Text = status is { State: GuardLinkState.Connected, ObservedAt: { } at }
            ? $"Ultimo aggiornamento: {at:HH:mm:ss}"
            : string.Empty;

        FieldsView.ItemsSource = status.Client;
        SafetyView.ItemsSource = status.Safety;
        RenderExecution(status);

        // Reconnecting counts as busy: the app is mid-attempt, so Connetti would
        // start a second one. Disconnetti stays live, because giving up is the
        // operator's decision to make at any time.
        var busy = status.State is GuardLinkState.Searching or GuardLinkState.Connecting or GuardLinkState.Reconnecting;
        ConnectButton.IsEnabled = !busy && status.State != GuardLinkState.Connected;
        DisconnectButton.IsEnabled = busy || status.State is GuardLinkState.Connected;
        UsbOption.IsEnabled = !busy && status.State != GuardLinkState.Connected;
        WiFiOption.IsEnabled = UsbOption.IsEnabled;
    }

    /// <summary>
    /// States the execution mode the runtime reported, or says it is not known.
    /// </summary>
    /// <remarks>
    /// Never "disabled" by default. Without a session there is no statement about
    /// execution, and presenting silence as a safety guarantee is exactly the
    /// error this screen used to make.
    /// </remarks>
    private void RenderExecution(GuardStatus status)
    {
        if (status.State != GuardLinkState.Connected)
        {
            ExecutionLabel.Text = "Sconosciuto: nessuna sessione in corso.";
            ExecutionLabel.TextColor = Colors.Gray;
            return;
        }

        var view = status.Safety;
        var mode = view.FirstOrDefault(f => f.Name == GuardSnapshotView.ExecutionModeField);

        if (mode is null || !mode.IsKnown)
        {
            ExecutionLabel.Text = "Il runtime non dichiara la modalità di esecuzione.";
            ExecutionLabel.TextColor = Colors.OrangeRed;
            return;
        }

        var disabled = mode.Value!.Contains("disabled", StringComparison.OrdinalIgnoreCase);
        ExecutionLabel.Text = disabled
            ? $"Esecuzione disabilitata dal runtime ({mode.Value})."
            : $"Esecuzione ATTIVA sul runtime ({mode.Value}).";
        ExecutionLabel.TextColor = disabled ? Colors.Green : Colors.OrangeRed;
    }

    private static string BuildDetail(GuardStatus status)
    {
        var parts = new StringBuilder();
        if (status.Detail is { Length: > 0 })
            parts.Append(status.Detail);
        if (status.RuntimeStatus is { Length: > 0 })
            parts.Append(parts.Length > 0 ? $" · runtime {status.RuntimeStatus}" : $"runtime {status.RuntimeStatus}");
        if (status.Endpoint is { Length: > 0 } && status.State == GuardLinkState.Connected)
            parts.Append($"\n{status.Endpoint}");
        return parts.Length > 0 ? parts.ToString() : "Nessuna sessione avviata.";
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        // The runtime drops a session that stops beating; stop deliberately rather
        // than leaving one to time out while the app is off screen.
        await _connection.StopAsync();
    }
}
