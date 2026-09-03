using System.Net;
using System.Net.Http;
using NosAi.Runtime.Configuration;
using NosAi.Runtime.Operator;
using NosAi.Runtime.Safety;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// <c>--halt</c> posts the operator halt to the runtime already listening.
/// Disarm-then-abort is <see cref="ImmediateHaltTests"/>; this is the CLI path
/// that reaches that call.
/// </summary>
public sealed class HaltCliTests
{
    [Fact]
    public void PortFromArgsUsesTheDashboardPortFlag()
    {
        Assert.Equal(17480, HaltCli.PortFromArgs(["--halt", "--dashboard-port", "17480"]));
    }

    [Fact]
    public void PortFromArgsFallsBackToTheGate1Default()
    {
        Assert.Equal(Gate1HostOptions.DefaultDashboardPort, HaltCli.PortFromArgs(["--halt"]));
    }

    [Fact]
    public async Task AnOutOfRangePortIsRefusedWithoutTalkingToAnyone()
    {
        int code = await HaltCli.RunAsync(0);
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task PostsTheHaltCommandToTheListeningRuntime()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"allowed":true,"reason":"halt_accepted"}""");

        int code = await HaltCli.RunAsync(8766, handler);

        Assert.Equal(0, code);
        Assert.Equal(HttpMethod.Post, handler.Last!.Method);
        Assert.Equal("http://127.0.0.1:8766/api/command", handler.Last.RequestUri!.ToString());
        Assert.Equal(ImmediateHalt.CommandName, handler.PostedBody);
    }

    [Fact]
    public async Task ARefusedHaltIsANonZeroExit()
    {
        var handler = new RecordingHandler(HttpStatusCode.Forbidden, """{"error":"halt_refused"}""");

        int code = await HaltCli.RunAsync(8766, handler);

        Assert.Equal(1, code);
        Assert.Equal(ImmediateHalt.CommandName, handler.PostedBody);
    }

    [Fact]
    public async Task NoRuntimeListeningIsANonZeroExit()
    {
        var handler = new FailingHandler();

        int code = await HaltCli.RunAsync(8766, handler);

        Assert.Equal(1, code);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public RecordingHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public HttpRequestMessage? Last { get; private set; }
        public string? PostedBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Last = request;
            if (request.Content is not null)
                PostedBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body)
            };
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new HttpRequestException("connection refused");
    }
}
