using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.GarbageCollection;

namespace NetWasm.Compiler.Tests;

public sealed class GarbageCollectionServiceCollectionExtensionsTests
{
    [Fact]
    public void RegistrationExposesRootMapCapabilitiesThroughTheirInterfaces()
    {
        using var provider = new ServiceCollection()
            .AddCompilerGarbageCollection()
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IRootSlotAllocator>());
        Assert.NotNull(provider.GetRequiredService<IRootMapAnalyzerFactory>());
    }
}
