using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Arrays;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ArrayStoreCheckPlannerTests
{
    [Fact]
    public void RejectsMissingResolversAndMethod()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ArrayStoreCheckPlanner(null!, new UnusedTypeDefinitionResolver()));
        Assert.Throws<ArgumentNullException>(() => new ArrayStoreCheckPlanner(new Arguments(), null!));
        Assert.Throws<ArgumentNullException>(() =>
            new ArrayStoreCheckPlanner(new Arguments(), new UnusedTypeDefinitionResolver()).Plan(null!, true));
    }

    private sealed class Arguments : IArgumentSignatureTypeResolver
    {
        public CliTypeIdentity Resolve(StructuredMethodHeader header, int index) =>
            throw new InvalidOperationException("This test does not resolve arguments.");
    }
}
