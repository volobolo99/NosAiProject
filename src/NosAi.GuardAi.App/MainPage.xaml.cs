using System.Security.Cryptography;
using System.Text;

namespace NosAi.GuardAi.App;

public partial class MainPage : ContentPage
{
    private readonly GuardConnectionService _connection;
    private readonly RSA _deviceKey;

    public MainPage()
    {
        InitializeComponent();

        // Persisted in app-private storage, so the identity survives a restart and
        // the PC does not have to re-enroll on every launch. Not the Android Key
        // Store: see DeviceIdentity and README.md for what that still costs.
        _deviceKey = DeviceIdentity.LoadOrCreate();
        DeviceIdentity.PublishPublicKey(_deviceKey);

        _connection = new GuardConnectionService(_deviceKey);
        _connection.StatusChanged += OnStatusChanged;

        PublicKeyEditor.Text = _deviceKey.ExportSubjectPublicKeyInfoPem();
        Render(GuardStatus.Idle);
    }

    private async void OnConnectClicked(object? sender, EventArgs e)
    {
        var host = HostEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            Render(GuardStatus.Failure("indirizzo mancante"));
            return;
        }

        if (!int.TryParse(PortEntry.Text?.Trim(), out var port) || port is < 1 or > 65535)
        {
            Render(GuardStatus.Failure("porta non valida"));
            return;
        }

        ConnectButton.IsEnabled = false;
        await _connection.ConnectAsync(host, port);
    }

    private async void OnDisconnectClicked(object? sender, EventArgs e)
    {
        await _connection.StopAsync();
        Render(GuardStatus.Idle);
    }

    private async void OnCopyKeyClicked(object? sender, EventArgs e)
    {
        await Clipboard.Default.SetTextAsync(PublicKeyEditor.Text);
        await DisplayAlert(
            "Chiave copiata",
            "Salvala sul PC e avvia il runtime con --guard-public-key-path <file>.",
            "OK");
    }

    private void OnStatusChanged(GuardStatus status) => MainThread.BeginInvokeOnMainThread(() => Render(status));

    private void Render(GuardStatus status)
    {
        StateLabel.Text = status.State switch
        {
            GuardLinkState.Idle => "IDLE",
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
        // No timestamp while disconnected: a stale "last seen" next to a dead link
        // reads as if the data were still current.
        ObservedLabel.Text = status is { State: GuardLinkState.Connected, ObservedAt: { } at }
            ? $"Ultimo aggiornamento: {at:HH:mm:ss}"
            : string.Empty;

        FieldsView.ItemsSource = status.Client;
        ConnectButton.IsEnabled = status.State is not GuardLinkState.Connecting;
        DisconnectButton.IsEnabled = status.State is GuardLinkState.Connected;
    }

    private static string BuildDetail(GuardStatus status)
    {
        var parts = new StringBuilder();
        if (status.Detail is { Length: > 0 })
            parts.Append(status.Detail);
        if (status.RuntimeStatus is { Length: > 0 })
            parts.Append(parts.Length > 0 ? $" · runtime {status.RuntimeStatus}" : $"runtime {status.RuntimeStatus}");
        if (status.Capabilities is { Length: > 0 })
            parts.Append($"\n{status.Capabilities}");
        return parts.Length > 0 ? parts.ToString() : "Nessuna sessione avviata.";
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        // The runtime drops a session that stops beating; stop deliberately instead
        // of leaving one to time out while the app is off screen.
        await _connection.StopAsync();
    }
}
