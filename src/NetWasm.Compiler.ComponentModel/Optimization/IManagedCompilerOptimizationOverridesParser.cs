namespace NetWasm.Compiler.ComponentModel.Optimization;

public interface IManagedCompilerOptimizationOverridesParser
{
    WasmOptimizationOverrides? Parse(
        string? loopInvariantFieldHoisting,
        string? staticDataViewElimination,
        string? addressTakenValuePromotion,
        string? managedMethodInlining = null,
        string? branchPrefixDuplication = null,
        string? scalarCallPreservation = null,
        string? arrayBoundsCheckElimination = null,
        string? loopUnrolling = null,
        string? constantArgumentPropagation = null,
        string? staticArrayPopulationElimination = null);
}
