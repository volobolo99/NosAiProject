using System.Net;
using NosAi.LiveIntegration;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Configuration;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The T-14 wait mode of <c>--record-wire</c> (Q-146 / AP-05 / A2+A4): a capture
/// that starts before the login. The mode takes no endpoint; it waits for the
/// client process, then for its first game connection, and records from there.
/// No driver and no real client here: the loop is driven through the seams
/// <see cref="WireRecorder.RunAwaitAndRecord"/> exposes, each default preserving
/// the production behavior.
/// </summary>
/// <remarks>
/// <para>
/// The honest core of the mode is what it cannot do: packets exchanged before
/// the connection is detected are not captured. The run declares that when it
/// starts recording, and the report says how long the attach took — the measure
/// of what could be missing. The tests pin both down.
/// </para>
/// <para>
/// <c>WireRecorder.Run</c> writes to the real console in the two refusal tests,
/// so this class joins the console-capture collection: the four classes that
/// redirect the console must never overlap with writes they did not make.
/// </para>
/// </remarks>
[Collection(ConsoleCaptureCollection.Name)]
public sealed class PreLoginAwaitClientTests : IDisposable
{
    private static readonly IPEndPoint GameServer = new(IPAddress.Parse("10.20.30.40"), 4002);

    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (string path in _tempFiles)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
                // A leftover temp file is not worth failing a test over.
            }
        }
    }

    private string TempRecordingPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nosai_prelogin_{Guid.NewGuid():N}.noscap");
        _tempFiles.Add(path);
        return path;
    }

    private static ClientNetworkObservation NoSession() =>
        new(Array.Empty<ClientTcpConnection>(), null, null);

    private static ClientNetworkObservation GameSession(IPEndPoint server)
    {
        var connection = new ClientTcpConnection(
            new IPEndPoint(IPAddress.Loopback, 46000),
            server,
            ClientTcpState.Established);
        return new ClientNetworkObservation(new[] { connection }, connection, null);
    }

    [Fact]
    public void AttachesAsSoonAsTheObservationReportsARemoteSessionAndRecordsOnThatEndpoint()
    {
        string path = TempRecordingPath();
        var observations = new[]
        {
            NoSession(),
            GameSession(GameServer),
        };
        int observationCalls = 0;
        IPEndPoint? recordedEndpoint = null;
        int recordCalls = 0;

        var output = new StringWriter();
        int exit = WireRecorder.RunAwaitAndRecord(
            path,
            recordingSeconds: 0,
            clientProcessNames: "fictive-nostale-client",
            pollInterval: TimeSpan.FromMilliseconds(5),
            clientProcessTimeout: TimeSpan.FromSeconds(2),
            gameSessionTimeout: TimeSpan.FromSeconds(2),
            findClientProcessIds: _ => new[] { 4242 },
            observe: _ =>
            {
                int index = Math.Min(observationCalls, observations.Length - 1);
                observationCalls++;
                return observations[index];
            },
            recordOnEndpoint: (endpoint, target, _) =>
            {
                recordCalls++;
                recordedEndpoint = endpoint;
                return new RecordingOutcome(3, target, null);
            },
            output: output,
            cancellationToken: CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal(1, recordCalls);
        Assert.Equal(GameServer, recordedEndpoint);
        Assert.True(observationCalls >= 2, "the loop kept observing until a remote session appeared");
        string report = output.ToString();
        Assert.Contains("client process found: pid=4242", report, StringComparison.Ordinal);
        Assert.Contains("=== recording the wire (sniff only; nothing is altered) ===", report, StringComparison.Ordinal);
        Assert.Contains("3 packets ->", report, StringComparison.Ordinal);
        Assert.Contains("waited before attach:", report, StringComparison.Ordinal);
    }

    [Fact]
    public void AClientProcessThatNeverAppearsIsANamedRefusalAndWritesNoFile()
    {
        string path = TempRecordingPath();
        bool observeCalled = false;
        string? searchedNames = null;

        var output = new StringWriter();
        int exit = WireRecorder.RunAwaitAndRecord(
            path,
            recordingSeconds: 0,
            clientProcessNames: null,
            pollInterval: TimeSpan.FromMilliseconds(10),
            clientProcessTimeout: TimeSpan.FromMilliseconds(250),
            gameSessionTimeout: TimeSpan.FromSeconds(2),
            findClientProcessIds: names =>
            {
                searchedNames = names;
                return Array.Empty<int>();
            },
            observe: _ =>
            {
                observeCalled = true;
                return NoSession();
            },
            output: output,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.False(observeCalled, "no observation may run when the process never appeared");
        Assert.False(File.Exists(path), "a refused wait must not leave a recording behind");
        string report = output.ToString();
        Assert.Contains($"[REFUSED] {WireRecorder.ClientNeverAppearedReason}:", report, StringComparison.Ordinal);
        Assert.Contains("no client process", report, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("record_client_process_never_appeared", WireRecorder.ClientNeverAppearedReason);
        // The process names come from Gate1HostOptions, never from this code.
        Assert.Equal(new Gate1HostOptions().ClientProcessName, searchedNames);
    }

    [Fact]
    public void AProcessWithoutAGameSessionIsADifferentNamedRefusalAndWritesNoFile()
    {
        string path = TempRecordingPath();
        bool recordCalled = false;

        var output = new StringWriter();
        int exit = WireRecorder.RunAwaitAndRecord(
            path,
            recordingSeconds: 0,
            clientProcessNames: "fictive-nostale-client",
            pollInterval: TimeSpan.FromMilliseconds(10),
            clientProcessTimeout: TimeSpan.FromSeconds(2),
            gameSessionTimeout: TimeSpan.FromMilliseconds(250),
            findClientProcessIds: _ => new[] { 4242 },
            observe: _ => NoSession(),
            recordOnEndpoint: (_, _, _) =>
            {
                recordCalled = true;
                return new RecordingOutcome(0, path, "unexpected");
            },
            output: output,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.False(recordCalled, "no recording may start when no game connection was detected");
        Assert.False(File.Exists(path), "a refused wait must not leave a recording behind");
        string report = output.ToString();
        Assert.Contains($"[REFUSED] {WireRecorder.GameSessionNeverAppearedReason}:", report, StringComparison.Ordinal);
        Assert.Contains("no game connection", report, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("record_game_session_never_appeared", WireRecorder.GameSessionNeverAppearedReason);
        // Two different failures, two different reasons: each sends the operator
        // somewhere else.
        Assert.NotEqual(WireRecorder.ClientNeverAppearedReason, WireRecorder.GameSessionNeverAppearedReason);
    }

    [Fact]
    public void AnEndpointTogetherWithTheAwaitModeIsRefusedNotChosen()
    {
        string path = TempRecordingPath();

        int exit = WireRecorder.Run("10.20.30.40:4002", path, 0, awaitClient: true);

        Assert.Equal(2, exit);
        Assert.False(File.Exists(path), "a contradictory invocation must not record anything");
        Assert.Equal("record_endpoint_and_await_conflict", WireRecorder.EndpointAndAwaitConflictReason);
        Assert.Equal(WireRecorder.EndpointAndAwaitConflictReason,
            WireRecorder.AwaitClientConflictReason("10.20.30.40:4002", awaitClient: true));
        Assert.Null(WireRecorder.AwaitClientConflictReason("10.20.30.40:4002", awaitClient: false));
        Assert.Null(WireRecorder.AwaitClientConflictReason(null, awaitClient: true));
        Assert.Equal("--await-client", WireRecorder.AwaitClientFlag);
    }

    [Fact]
    public void TheEndpointModeWithoutTheAwaitFlagBehavesAsBefore()
    {
        string path = TempRecordingPath();

        // No endpoint, no await flag: exactly the refusal of today, exit 2, and
        // nothing waits and nothing is written.
        int exit = WireRecorder.Run(null, path, 0, awaitClient: false);
        Assert.Equal(2, exit);
        Assert.False(File.Exists(path));
        Assert.Null(WireRecorder.AwaitClientConflictReason("10.20.30.40:4002", awaitClient: false));

        // The command line still routes both modes through WireRecorder.
        string program = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "NosAi.Runtime", "Program.cs"));
        Assert.Contains("WireRecorder.Flag", program, StringComparison.Ordinal);
        Assert.Contains("WireRecorder.Run", program, StringComparison.Ordinal);
        Assert.Contains("WireRecorder.AwaitClientFlag", program, StringComparison.Ordinal);
        Assert.Equal("--record-wire", WireRecorder.Flag);
    }

    [Fact]
    public void TheFinalReportStatesHowLongTheAttachTookAndTheStartStatesWhatIsNotCaptured()
    {
        string path = TempRecordingPath();

        var output = new StringWriter();
        int exit = WireRecorder.RunAwaitAndRecord(
            path,
            recordingSeconds: 0,
            clientProcessNames: "fictive-nostale-client",
            pollInterval: TimeSpan.FromMilliseconds(5),
            clientProcessTimeout: TimeSpan.FromSeconds(2),
            gameSessionTimeout: TimeSpan.FromSeconds(2),
            findClientProcessIds: _ => new[] { 4242 },
            observe: _ => GameSession(GameServer),
            recordOnEndpoint: (_, target, _) => new RecordingOutcome(5, target, null),
            output: output,
            cancellationToken: CancellationToken.None);

        Assert.Equal(0, exit);
        string report = output.ToString();
        // The limitation is declared when recording begins, not at the end.
        Assert.Contains("exchanged BEFORE the attach are NOT captured", report, StringComparison.Ordinal);
        // The final report carries the wait, the measure of what could be missing.
        Assert.Contains("waited before attach:", report, StringComparison.Ordinal);
        Assert.Contains("5 packets ->", report, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found: no NosAi.sln above the test assembly.");
        return directory!.FullName;
    }
}
