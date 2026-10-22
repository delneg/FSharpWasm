using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Analysis.ManagedCallSites;

namespace NetWasm.Compiler.Tests;

public sealed class ManagedCallSiteAnalysisServiceCollectionExtensionsTests
{
    [Fact]
    public void AddManagedCallSiteAnalysisRegistersTheLedgerWriterContract()
    {
        var services = new ServiceCollection();

        IServiceCollection returned = services.AddManagedCallSiteAnalysis();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(services, returned);
        Assert.IsType<ManagedCallSiteLedgerWriter>(
            provider.GetRequiredService<IManagedCallSiteLedgerWriter>());
    }
}
