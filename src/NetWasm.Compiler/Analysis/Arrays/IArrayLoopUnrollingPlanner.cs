using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Arrays;

internal sealed record ArrayLoopUnrollingPlan(
    int IndexLocal,
    int ArrayLocal,
    int GuardOffset,
    ImmutableArray<int> EntryBranches,
    ImmutableArray<CilInstruction> Body,
    int Factor);

internal interface IArrayLoopUnrollingPlanner
{
    ImmutableArray<ArrayLoopUnrollingPlan> Plan(ValidatedControlFlowGraph controlFlow);
}
