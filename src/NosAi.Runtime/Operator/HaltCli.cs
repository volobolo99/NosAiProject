using System.Net.Http;
using NosAi.Runtime.Configuration;
using NosAi.Runtime.Safety;

namespace NosAi.Runtime.Operator;

/// <summary>
/// One-shot CLI for <c>--halt</c>: posts the operator halt to a running runtime.
/// </summary>
/// <remarks>
/// The halt has to reach the process that holds the switches and the open act.
/// Starting a new composition and disarming that would stop nothing that is
/// actually armed. This talks to the operator dashboard of the runtime already
/// listening, which is the same path the Control Panel uses.
/// </remarks>
public static class HaltCli
{
    public static async Task<int> RunAsync(int dashboardPort, HttpMessageHandler? handler = null)
    {
        if (dashboardPort is < 1 or > 65535)
        {
            Console.Error.WriteLine($"--halt needs a dashboard port between 1 and 65535, got {dashboardPort}.");
            return 2;
        }

        using HttpClient http = handler is null
            ? new HttpClient { Timeout = TimeSpan.FromSeconds(3) }
            : new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(3) };

        string url = $"http://127.0.0.1:{dashboardPort}/api/command";
        try
        {
            using var content = new StringContent(ImmediateHalt.CommandName);
            using HttpResponseMessage response = await http.PostAsync(url, content).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"Halt refused by the runtime: HTTP {(int)response.StatusCode} {body}");
                return 1;
            }

            Console.WriteLine($"Halt accepted on {url}.");
            if (!string.IsNullOrWhiteSpace(body))
                Console.WriteLine(body);
            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            Console.Error.WriteLine(
                $"Nessun runtime in ascolto su {url} ({ex.GetType().Name}). Avvia il runtime, poi --halt.");
            return 1;
        }
    }

    public static int PortFromArgs(string[] args)
    {
        int flag = Array.FindIndex(args, a =>
            string.Equals(a, "--dashboard-port", StringComparison.OrdinalIgnoreCase));
        if (flag >= 0 && flag + 1 < args.Length
            && int.TryParse(args[flag + 1], out int parsed)
            && parsed is >= 1 and <= 65535)
            return parsed;

        return Gate1HostOptions.DefaultDashboardPort;
    }
}
