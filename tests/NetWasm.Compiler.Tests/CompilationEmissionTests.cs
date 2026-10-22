using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Pipeline;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Tests;

public sealed class CompilationEmissionTests
{
    [Fact]
    public void RetainsTheEmissionAndItsLoweredMethods()
    {
        var result = new WasmModuleEmissionResult([], 0);
        var lowering = new WasmMethodLoweringResult(
            ImmutableDictionary<EntityKey, StructuredMethod>.Empty,
            ImmutableDictionary<string, StructuredMethod>.Empty);

        var emission = new CompilationEmission(result, lowering);

        Assert.Same(result, emission.Result);
        Assert.Same(lowering, emission.Lowering);
    }

    [Fact]
    public void RequiresBothResults()
    {
        var result = new WasmModuleEmissionResult([], 0);
        var lowering = new WasmMethodLoweringResult(
            ImmutableDictionary<EntityKey, StructuredMethod>.Empty,
            ImmutableDictionary<string, StructuredMethod>.Empty);

        Assert.Throws<ArgumentNullException>(() => new CompilationEmission(null!, lowering));
        Assert.Throws<ArgumentNullException>(() => new CompilationEmission(result, null!));
    }
}
