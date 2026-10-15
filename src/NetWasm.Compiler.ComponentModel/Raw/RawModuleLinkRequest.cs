namespace NetWasm.Compiler.ComponentModel.Raw;

using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;

public sealed record RawModuleLinkRequest(
    string ApplicationModulePath,
    string RuntimeModulePath,
    string OutputPath,
    ComponentTarget Target,
    FinalWasmOptimization Optimization = FinalWasmOptimization.Oz)
{
    public WasmOptimizationOverrides? OptimizationOverrides { get; init; }
    public ImmutableArray<WasmInternalExport> InternalRuntimeExports { get; init; } = [];
    public ImmutableArray<WasmInternalExport> InternalApplicationExports { get; init; } = [];
}
