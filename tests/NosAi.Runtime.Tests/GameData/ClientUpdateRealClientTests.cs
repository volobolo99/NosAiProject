using NosAi.Runtime.GameData;
using NosAi.Runtime.Observability;
using Xunit;
using Xunit.Abstractions;

namespace NosAi.Runtime.Tests.GameData;

public sealed class ClientUpdateRealClientTests
{
    private readonly ITestOutputHelper _output;

    public ClientUpdateRealClientTests(ITestOutputHelper output) => _output = output;

    [NosTaleClientFact]
    public void ClientUpdatesCommand_RunsAgainstTheRealClient_AndReportsWhatItFound()
    {
        string directory = NosTaleClientFactAttribute.ResolveDirectory()!;
        using GameReferenceDatabase database = GameReferenceDatabase.OpenInMemory();

        string report = ClientUpdateCommand.Run(database, directory);

        Evidence.Live(_output, "clientUpdatesReport", report);

        Assert.Contains("inventario file:", report);
        Assert.NotEmpty(database.ClientInventory());
    }
}
