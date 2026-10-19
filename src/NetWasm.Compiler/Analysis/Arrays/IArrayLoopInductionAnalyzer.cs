using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.Analysis.Arrays;

internal sealed record ArrayLoopInduction(
    int IndexLocal,
    int ArrayLocal,
    int GuardBlock,
    int BodyEntryBlock,
    int IncrementOffset,
    ImmutableHashSet<int> LoopBlocks)
{
    // The guard tests index + Lookahead against the length, so every element
    // from index to index + Lookahead is in bounds.
    public int Lookahead { get; init; }
}

internal interface IArrayLoopInductionAnalyzer
{
    ImmutableArray<ArrayLoopInduction> Analyze(ValidatedControlFlowGraph controlFlow);
}
