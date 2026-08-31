using System.Net.Sockets;

namespace NosAi.ControlPanel;

/// <summary>Loopback listen check. False is UNKNOWN-or-closed, never an invented open port.</summary>
internal static class LocalPortProbe
{
    public static bool CanConnect(int port, int timeoutMs = 80)
    {
        if (port is < 1 or > 65535)
            return false;

        try
        {
            using var client = new TcpClient();
            var connect = client.ConnectAsync("127.0.0.1", port);
            return connect.Wait(timeoutMs) && client.Connected;
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
