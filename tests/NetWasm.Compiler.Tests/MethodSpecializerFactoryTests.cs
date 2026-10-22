using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.Tests;

public sealed class MethodSpecializerFactoryTests
{
    [Fact]
    public void ConstructorRejectsMissingControlFlowCapabilities()
    {
        Assert.Throws<ArgumentNullException>(() => new MethodSpecializerFactory(
            null!,
            new ControlFlowGraphAnalyzerFactory()));
        Assert.Throws<ArgumentNullException>(() => new MethodSpecializerFactory(
            new ControlFlowGraphBuilderFactory(),
            null!));
    }
}
