using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.Analysis.Arrays;

internal sealed record ArrayLoopInduction(
    int IndexLocal,
    int ArrayLocal,
    int GuardBlock,
    int BodyEntryBlock,
    int IncrementOffset,
    ImmutableHashSet<int> LoopBlocks);

internal interface IArrayLoopInductionAnalyzer
{
    ImmutableArray<ArrayLoopInduction> Analyze(ValidatedControlFlowGraph controlFlow);
}
