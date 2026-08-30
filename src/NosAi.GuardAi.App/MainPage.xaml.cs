using System.Security.Cryptography;
using System.Text;

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
    private readonly RSA _deviceKey;
    private GuardTransport _transport;
    private bool _applyingStoredChoice;

    public MainPage()
    {
        InitializeComponent();

        _deviceKey = DeviceIdentity.LoadOrCreate();
        DeviceIdentity.PublishPublicKey(_deviceKey);

        _connection = new GuardConnectionService(_deviceKey);
        _connection.StatusChanged += OnStatusChanged;

        // Restoring the stored choice fires CheckedChanged, which would write the
        // value straight back; the guard keeps startup from looking like a change.
        _applyingStoredChoice = true;
        _transport = TransportPreference.Load();
        UsbOption.IsChecked = _transport == GuardTransport.Usb;
        WiFiOption.IsChecked = _transport == GuardTransport.WiFi;
        _applyingStoredChoice = false;

        UpdateTransportHint();
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
            _ => "NON CONNESSO"
        };

        StateLabel.TextColor = status.State switch
        {
            GuardLinkState.Connected => Colors.Green,
            GuardLinkState.Failed => Colors.OrangeRed,
            _ => Colors.Gray
        };

        DetailLabel.Text = BuildDetail(status);

        // No timestamp unless connected: a stale "last seen" beside a dead link
        // reads as if the data were still current.
        ObservedLabel.Text = status is { State: GuardLinkState.Connected, ObservedAt: { } at }
            ? $"Ultimo aggiornamento: {at:HH:mm:ss}"
            : string.Empty;

        FieldsView.ItemsSource = status.Client;

        var busy = status.State is GuardLinkState.Searching or GuardLinkState.Connecting;
        ConnectButton.IsEnabled = !busy && status.State != GuardLinkState.Connected;
        DisconnectButton.IsEnabled = status.State is GuardLinkState.Connected;
        UsbOption.IsEnabled = !busy && status.State != GuardLinkState.Connected;
        WiFiOption.IsEnabled = UsbOption.IsEnabled;
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
