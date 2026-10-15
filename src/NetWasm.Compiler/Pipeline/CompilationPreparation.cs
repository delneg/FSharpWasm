using System;
using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.EntryPoints;
using NetWasm.Compiler.Analysis;

namespace NetWasm.Compiler.Pipeline;

internal sealed class CompilationPreparation(
    ComponentBoundaryContract componentContract,
    CompilationEntry entry,
    ImmutableArray<ProgramExport> exports,
    ReachabilityRoots reachabilityRoots,
    EntityKey? entryPointArgumentFactory,
    WasmOptimizationSettings optimizationSettings)
{
    public ComponentBoundaryContract ComponentContract { get; } = componentContract ??
        throw new ArgumentNullException(nameof(componentContract));

    public CompilationEntry Entry { get; } = entry ??
        throw new ArgumentNullException(nameof(entry));

    public ImmutableArray<ProgramExport> Exports { get; } = exports;

    public ReachabilityRoots ReachabilityRoots { get; } = reachabilityRoots ??
        throw new ArgumentNullException(nameof(reachabilityRoots));

    public EntityKey? EntryPointArgumentFactory { get; } = entryPointArgumentFactory;

    public WasmOptimizationSettings OptimizationSettings { get; } =
        optimizationSettings ?? throw new ArgumentNullException(nameof(optimizationSettings));
}
