namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IBranchJoinSelector
{
    BranchJoinSelection Select(BranchJoinSelectionRequest request);
}
