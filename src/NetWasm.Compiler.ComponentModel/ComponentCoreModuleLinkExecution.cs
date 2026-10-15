using System;
using NetWasm.Compiler.ComponentModel.Inlining;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.ComponentModel.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel;

public interface IComponentCoreModuleLinkExecution
{
    void Run(
        ComponentCoreModuleLinkRequest request,
        ComponentCoreModuleWorkspace workspace);
}

public sealed class ComponentCoreModuleLinkExecution(
    IComponentCoreModuleMergeRunner merge,
    IEmscriptenEnvironmentShimWriter environmentShim,
    INetWasmHostComponentShimWriter hostShim,
    IManagedExecutableComponentAdapterWriter managedExecutables,
    IWasmCoreModuleExportEditor exports,
    IComponentCoreModuleOptimizer optimizer,
    IWasmOptimizationSettingsResolver optimizationSettings,
    IWasmCoreModuleInlineAnnotationEditor inlineAnnotations) : IComponentCoreModuleLinkExecution
{
    private readonly IComponentCoreModuleMergeRunner _merge = merge ??
        throw new ArgumentNullException(nameof(merge));
    private readonly IEmscriptenEnvironmentShimWriter _environmentShim =
        environmentShim ?? throw new ArgumentNullException(nameof(environmentShim));
    private readonly INetWasmHostComponentShimWriter _hostShim = hostShim ??
        throw new ArgumentNullException(nameof(hostShim));
    private readonly IManagedExecutableComponentAdapterWriter _managedExecutables =
        managedExecutables ?? throw new ArgumentNullException(nameof(managedExecutables));
    private readonly IWasmCoreModuleExportEditor _exports = exports ??
        throw new ArgumentNullException(nameof(exports));
    private readonly IComponentCoreModuleOptimizer _optimizer = optimizer ??
        throw new ArgumentNullException(nameof(optimizer));
    private readonly IWasmOptimizationSettingsResolver _optimizationSettings = optimizationSettings ??
        throw new ArgumentNullException(nameof(optimizationSettings));
    private readonly IWasmCoreModuleInlineAnnotationEditor _inlineAnnotations = inlineAnnotations ??
        throw new ArgumentNullException(nameof(inlineAnnotations));

    public void Run(
        ComponentCoreModuleLinkRequest request,
        ComponentCoreModuleWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(workspace);
        try
        {
            var settings = _optimizationSettings.Resolve(request.Optimization, request.OptimizationOverrides);
            _environmentShim.Write(workspace.EnvironmentModulePath, request.Target);
            string? hostModulePath = null;
            string? managedExecutableAdapterModulePath = null;
            if (request.ManagedExecutableEntryPoint is not null)
            {
                _hostShim.Write(new(
                    workspace.HostModulePath,
                    request.Target,
                    request.ManagedExecutableEntryPoint.CompletionShape,
                    request.StructuredDiagnostics));
                _managedExecutables.Write(new(
                    workspace.ManagedExecutableAdapterModulePath,
                    request.Target,
                    request.ManagedExecutableEntryPoint,
                    request.StructuredDiagnostics));
                hostModulePath = workspace.HostModulePath;
                managedExecutableAdapterModulePath =
                    workspace.ManagedExecutableAdapterModulePath;
            }
            _inlineAnnotations.Rewrite(request.ApplicationModulePath,
                workspace.SanitizedModulePath, settings.ManagedInlineAnnotations);
            _inlineAnnotations.Rewrite(request.RuntimeModulePath,
                workspace.SanitizedRuntimeModulePath, settings.RuntimeInlineAnnotations);
            _merge.Run(new ComponentCoreModuleMergeRequest(
                workspace.SanitizedModulePath,
                workspace.SanitizedRuntimeModulePath,
                workspace.EnvironmentModulePath,
                workspace.MergedModulePath,
                request.Target,
                hostModulePath,
                managedExecutableAdapterModulePath));
            var wasmTarget = request.Target.Width == "wasm64"
                ? WasmTarget.Wasm64
                : WasmTarget.Wasm32;
            _exports.Rewrite(
                workspace.MergedModulePath,
                workspace.SanitizedModulePath,
                new(CanonicalAbiNames.ModulePrefix(wasmTarget), []));
            _optimizer.Optimize(
                workspace.SanitizedModulePath,
                request.OutputPath,
                request.Target,
                settings.BackendPreset);
        }
        finally
        {
            workspace.Dispose();
        }
    }
}
