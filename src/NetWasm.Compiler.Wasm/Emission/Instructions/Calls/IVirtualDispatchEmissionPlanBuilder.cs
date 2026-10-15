using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal interface IVirtualDispatchEmissionPlanBuilder
{
    VirtualDispatchEmissionPlan Build(
        MethodInstanceModel declaration,
        ImmutableArray<DispatchTargetModel> targets);
}
