namespace NetWasm.Compiler.ComponentModel;

using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.ComponentModel.Optimization;

public sealed record ComponentCoreModuleLinkRequest(
    string ApplicationModulePath,
    string RuntimeModulePath,
    string OutputPath,
    ComponentTarget Target,
    ManagedExecutableEntryPointAbi? ManagedExecutableEntryPoint = null,
    FinalWasmOptimization Optimization = FinalWasmOptimization.Oz,
    bool StructuredDiagnostics = false)
{
    public WasmOptimizationOverrides? OptimizationOverrides { get; init; }
}
