using System.Net;
using System.Net.Sockets;
using NosAi.Runtime.Gate1;

namespace NosAi.GuardClient;

/// <summary>A runtime that answered a discovery probe.</summary>
/// <param name="Address">Address the reply came from.</param>
/// <param name="GuardPort">Port its Guard channel is listening on.</param>
/// <param name="HostName">Label for the operator. Never an authorisation.</param>
public sealed record DiscoveredRuntime(string Address, int GuardPort, string HostName)
{
    public override string ToString() => $"{HostName} ({Address}:{GuardPort})";
}

/// <summary>
/// Finds the Gate 1 runtime on the local network, so the operator never types an
/// address.
/// </summary>
/// <remarks>
/// <b>A discovery reply is not trust.</b> Any host on the network can answer, so
/// finding a runtime says only that something claimed to be one. Authentication
/// still happens in the RSA handshake, and the runtime still refuses a key it
/// does not know. What discovery does not do is prove the runtime to the phone —
/// see the Wi-Fi limitation in ADR-0007.
/// </remarks>
public static class RuntimeDiscovery
{
    /// <summary>
    /// Probes the LAN and returns every runtime that answers within the timeout.
    /// </summary>
    /// <remarks>
    /// Probes are sent more than once: UDP broadcast is lossy, and a single
    /// dropped datagram would look to the operator like "no runtime on this
    /// network" rather than a lost packet.
    /// </remarks>
    public static async Task<IReadOnlyList<DiscoveredRuntime>> FindAllAsync(
        TimeSpan timeout,
        int port = DiscoveryProtocol.Port,
        CancellationToken cancellationToken = default)
    {
        using var socket = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        socket.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        var found = new Dictionary<string, DiscoveredRuntime>();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        var listening = ListenAsync(socket, found, deadline.Token);

        var request = DiscoveryProtocol.CreateRequest();
        var targets = BroadcastTargets(port);
        try
        {
            for (var attempt = 0; attempt < 3 && !deadline.IsCancellationRequested; attempt++)
            {
                foreach (var target in targets)
                {
                    try
                    {
                        await socket.SendAsync(request, target, deadline.Token).ConfigureAwait(false);
                    }
                    catch (SocketException)
                    {
                        // An interface that refuses broadcast must not stop the others.
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(300), deadline.Token).ConfigureAwait(false);
            }

            await listening.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The timeout is the normal end of a scan, not a failure.
        }

        return found.Values.ToList();
    }

    /// <summary>The first runtime that answers, or null when none does.</summary>
    public static async Task<DiscoveredRuntime?> FindFirstAsync(
        TimeSpan timeout,
        int port = DiscoveryProtocol.Port,
        CancellationToken cancellationToken = default)
    {
        var all = await FindAllAsync(timeout, port, cancellationToken).ConfigureAwait(false);
        return all.Count > 0 ? all[0] : null;
    }

    private static async Task ListenAsync(
        UdpClient socket,
        Dictionary<string, DiscoveredRuntime> found,
        CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await socket.ReceiveAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { continue; }

            if (!DiscoveryProtocol.TryReadResponse(received.Buffer, out var guardPort, out var hostName))
                continue;

            var address = received.RemoteEndPoint.Address.ToString();
            found[address] = new DiscoveredRuntime(address, guardPort, hostName);
        }
    }

    /// <summary>
    /// Broadcast destinations to probe.
    /// </summary>
    /// <remarks>
    /// The global 255.255.255.255 is not enough on its own: some Android builds and
    /// some access points drop it, while a directed subnet broadcast still gets
    /// through. Both are tried.
    /// </remarks>
    private static List<IPEndPoint> BroadcastTargets(int port)
    {
        var targets = new List<IPEndPoint> { new(IPAddress.Broadcast, port) };

        try
        {
            foreach (var nic in NetworkInterfaceAddresses())
            {
                var directed = DirectedBroadcast(nic.Address, nic.Mask);
                if (directed is not null)
                    targets.Add(new IPEndPoint(directed, port));
            }
        }
        catch (Exception)
        {
            // Enumeration is a best effort; the global broadcast remains.
        }

        return targets;
    }

    private static IEnumerable<(IPAddress Address, IPAddress Mask)> NetworkInterfaceAddresses()
    {
        foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                continue;

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork
                    && unicast.IPv4Mask is { } mask
                    && !IPAddress.IsLoopback(unicast.Address))
                {
                    yield return (unicast.Address, mask);
                }
            }
        }
    }

    private static IPAddress? DirectedBroadcast(IPAddress address, IPAddress mask)
    {
        var addressBytes = address.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        if (addressBytes.Length != 4 || maskBytes.Length != 4)
            return null;

        var broadcast = new byte[4];
        for (var i = 0; i < 4; i++)
            broadcast[i] = (byte)(addressBytes[i] | (byte)~maskBytes[i]);
        return new IPAddress(broadcast);
    }
}
