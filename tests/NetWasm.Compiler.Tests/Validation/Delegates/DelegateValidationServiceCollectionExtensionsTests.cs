using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Validation;
using NetWasm.Compiler.Validation.Delegates;

namespace NetWasm.Compiler.Tests.Validation.Delegates;

public sealed class DelegateValidationServiceCollectionExtensionsTests
{
    [Fact]
    public void RegistrationExposesTheDelegateBindingValidator()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICompilerInvariantExceptionFactory,
            CompilerInvariantExceptionFactory>();

        services.AddDelegateValidation();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<DelegateBindingInvariantValidator>(
            provider.GetRequiredService<IDelegateBindingInvariantValidator>());
    }

    [Fact]
    public void RegistrationRejectsMissingServices()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DelegateValidationServiceCollectionExtensions.AddDelegateValidation(null!));
    }
}
