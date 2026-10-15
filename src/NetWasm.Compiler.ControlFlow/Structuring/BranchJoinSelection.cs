using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed record BranchJoinSelection(
    int? Join,
    ImmutableHashSet<int> AnalysisBlocks,
    ImmutableHashSet<int> TerminalTransfers,
    ImmutableHashSet<int> DuplicatedPrefixBlocks);

internal sealed record BranchJoinSelectionRequest(
    ControlFlowStructuringState State,
    int First,
    int Second,
    int? Stop,
    ImmutableHashSet<int> AllowedBlocks,
    int? ContinueTarget,
    int? BreakTarget);
