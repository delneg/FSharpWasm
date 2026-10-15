using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IControlFlowSubgraphCycleClassifier
{
    bool Classify(
        ControlFlowGraph graph,
        ImmutableHashSet<int> blocks);
}
