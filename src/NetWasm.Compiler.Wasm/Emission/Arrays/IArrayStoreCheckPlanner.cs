using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Arrays;

internal interface IArrayStoreCheckPlanner
{
    ImmutableHashSet<int> Plan(StructuredMethod method, bool enabled);
}
