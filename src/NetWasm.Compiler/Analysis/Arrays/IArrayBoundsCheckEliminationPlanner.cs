using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.Analysis.Arrays;

internal interface IArrayBoundsCheckEliminationPlanner
{
    ImmutableHashSet<int> Plan(ValidatedControlFlowGraph controlFlow);
}
