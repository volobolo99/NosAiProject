using System.Net;
using System.Net.Sockets;

namespace NosAi.Runtime.Gate1;

/// <summary>
/// Answers LAN discovery requests so the phone finds this runtime without being
/// told an address.
/// </summary>
/// <remarks>
/// <para>
/// Answering is all it does. It grants nothing, carries no state, and accepts no
/// commands: a reply only says "a Gate 1 runtime is reachable here, on this
/// port". Every authorisation decision still happens in the RSA handshake on the
/// Guard channel.
/// </para>
/// <para>
/// Like the operator dashboard and unlike the Guard channel, a failure to bind
/// degrades this feature instead of taking the runtime down. Discovery is a
/// convenience: without it the phone can still be pointed at an address by hand.
/// </para>
/// </remarks>
public sealed class DiscoveryResponder : IAsyncDisposable
{
    private readonly int _guardPort;
    private readonly string _hostName;
    private readonly int _listenPort;
    private UdpClient? _socket;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public DiscoveryResponder(int guardPort, string? hostName = null, int listenPort = DiscoveryProtocol.Port)
    {
        _guardPort = guardPort;
        _hostName = string.IsNullOrWhiteSpace(hostName) ? Environment.MachineName : hostName;
        _listenPort = listenPort;
    }

    public bool IsListening { get; private set; }

    /// <summary>Why discovery is not answering; null while it is.</summary>
    public string? FailureReason { get; private set; }

    public bool TryStart(out string? failureReason)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsListening)
        {
            failureReason = null;
            return true;
        }

        UdpClient socket;
        try
        {
            socket = new UdpClient(AddressFamily.InterNetwork);
            // Another responder on this machine must not silently split the replies
            // between them, so the bind is exclusive.
            socket.ExclusiveAddressUse = true;
            socket.EnableBroadcast = true;
            socket.Client.Bind(new IPEndPoint(IPAddress.Any, _listenPort));
        }
        catch (SocketException ex)
        {
            failureReason = ex.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? $"discovery_port_in_use:{_listenPort}"
                : $"discovery_bind_failed:{_listenPort}:{ex.SocketErrorCode}";
            FailureReason = failureReason;
            return false;
        }

        _socket = socket;
        _cts = new CancellationTokenSource();
        IsListening = true;
        FailureReason = null;
        _ = LoopAsync(_cts.Token);
        failureReason = null;
        return true;
    }

    private async Task LoopAsync(CancellationToken token)
    {
        var socket = _socket;
        if (socket is null)
            return;

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

            if (!DiscoveryProtocol.IsRequest(received.Buffer))
                continue; // Anything can arrive on an open UDP port; ignore it quietly.

            try
            {
                var reply = DiscoveryProtocol.CreateResponse(_guardPort, _hostName);
                await socket.SendAsync(reply, received.RemoteEndPoint, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                // One unanswered probe is not worth ending discovery: the phone retries.
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;
        _disposed = true;
        IsListening = false;
        _cts?.Cancel();
        _socket?.Dispose();
        _cts?.Dispose();
        _socket = null;
        _cts = null;
        return ValueTask.CompletedTask;
    }
}
