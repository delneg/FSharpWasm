using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal sealed record VirtualDispatchEmissionPlan(
    ImmutableArray<DispatchTargetModel> CheckedTargets,
    DispatchTargetModel? FallbackTarget);
