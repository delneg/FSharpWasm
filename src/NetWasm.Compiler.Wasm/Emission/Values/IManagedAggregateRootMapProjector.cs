using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values;

internal interface IManagedAggregateRootMapProjector
{
    bool TryProject(
        ManagedAggregateRootMapProjectionRequest request,
        out MethodRootMap rootMap);
}

internal sealed record ManagedAggregateRootMapProjectionRequest(
    MethodRootMap RootMap,
    ManagedAggregateSsaPlan Plan);
