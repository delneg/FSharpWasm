using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Emission;

namespace NetWasm.Compiler.Tests;

public sealed class EmissionServiceCollectionExtensionsTests
{
    [Fact]
    public void RegistrationRejectsANullServiceCollection()
    {
        IServiceCollection? services = null;

        Assert.Throws<ArgumentNullException>(() => services!.AddCompilerEmission());
    }

    [Fact]
    public void RegistrationReturnsTheOriginalServiceCollection()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddCompilerEmission());
    }
}
