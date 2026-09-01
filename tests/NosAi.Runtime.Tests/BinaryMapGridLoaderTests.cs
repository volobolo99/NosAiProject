using NosAi.Runtime.Navigation;

namespace NosAi.Runtime.Tests;

/// <summary>Activates <see cref="MapGridLoaderContractTests"/> against the binary loader.</summary>
public sealed class BinaryMapGridLoaderTests : MapGridLoaderContractTests
{
    protected override IMapGridLoader CreateLoader() => new BinaryMapGridLoader();
}
