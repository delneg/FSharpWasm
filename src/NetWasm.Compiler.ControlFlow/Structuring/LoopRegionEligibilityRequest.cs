using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed record LoopRegionEligibilityRequest(
    ControlFlowGraph Graph,
    ImmutableHashSet<int> NaturalLoop,
    LoopRegion Region,
    ControlFlowDomain Domain,
    ImmutableArray<ControlFlowDomain> Domains);
