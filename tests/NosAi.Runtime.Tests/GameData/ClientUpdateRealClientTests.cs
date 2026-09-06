using NosAi.Runtime.GameData;
using NosAi.Runtime.Observability;
using Xunit;
using Xunit.Abstractions;

namespace NosAi.Runtime.Tests.GameData;

/// <summary>
/// Runs only where both a real NosTale installation and the vendored
/// <c>taletool.exe</c> are present -- <see cref="ClientUpdateCommand"/>
/// needs both to exercise its full path. A skip here is never evidence
/// that either was read, same convention as <see cref="NosTaleClientFactAttribute"/>.
/// </summary>
public sealed class TaletoolClientFactAttribute : FactAttribute
{
    public TaletoolClientFactAttribute()
    {
        if (NosTaleClientFactAttribute.ResolveDirectory() is null)
            Skip = "Nessuna installazione NosTale trovata (vedi NosTaleClientFactAttribute).";
        else if (TaletoolInvoker.ResolveExecutablePath() is null)
            Skip = "taletool.exe non trovato: atteso accanto all'eseguibile oppure indicato da "
                + TaletoolInvoker.ExecutableVariable + ".";
    }
}

public sealed class ClientUpdateRealClientTests
{
    private readonly ITestOutputHelper _output;

    public ClientUpdateRealClientTests(ITestOutputHelper output) => _output = output;

    [TaletoolClientFact]
    public void ClientUpdatesCommand_RunsAgainstTheRealClient_AndReportsWhatItFound()
    {
        string directory = NosTaleClientFactAttribute.ResolveDirectory()!;
        using GameReferenceDatabase database = GameReferenceDatabase.OpenInMemory();

        string report = ClientUpdateCommand.Run(database, directory);

        Evidence.Live(_output, "clientUpdatesReport", report);

        Assert.Contains("inventario file (taletool):", report);
        Assert.DoesNotContain("taletool_not_found", report);
        Assert.NotEmpty(database.ClientInventory());
    }
}
