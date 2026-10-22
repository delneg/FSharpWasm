using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Loading;

namespace NetWasm.Compiler.Tests;

public sealed class LoadingServiceCollectionExtensionsTests
{
    [Fact]
    public void RegistrationRejectsANullServiceCollection()
    {
        IServiceCollection? services = null;

        Assert.Throws<ArgumentNullException>(() => services!.AddCompilerLoading());
    }

    [Fact]
    public void RegistrationReturnsTheOriginalServiceCollection()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddCompilerLoading());
    }
}
