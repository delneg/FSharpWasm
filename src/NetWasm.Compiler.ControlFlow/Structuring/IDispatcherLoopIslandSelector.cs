using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IDispatcherLoopIslandSelector
{
    ImmutableHashSet<int> Select(
        ControlFlowStructuringState state,
        int? entry,
        ImmutableHashSet<int> component,
        ImmutableHashSet<int> allowed,
        ImmutableHashSet<int> dispatcherOwnedBlocks);
}
