using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IDispatcherFallthroughExitClassifier
{
    bool Classify(
        ControlFlowStructuringState state,
        ImmutableHashSet<int> dispatcher,
        ImmutableHashSet<int> allowed);
}
