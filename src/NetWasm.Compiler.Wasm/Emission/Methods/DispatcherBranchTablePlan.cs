using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed record DispatcherBranchTablePlan(
    ImmutableArray<DispatcherCasePlan> Cases,
    ImmutableArray<uint> Targets);

internal sealed record DispatcherCasePlan(
    int Target,
    StructuredDispatcherExit? Exit,
    StructuredDispatcherBlock? Block,
    DispatcherTransitionPlan? WhenTrue,
    DispatcherTransitionPlan? WhenFalse);

internal sealed record DispatcherTransitionPlan(
    StructuredBlockId Target,
    uint? DirectBranchDepth);
