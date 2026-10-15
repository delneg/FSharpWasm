using System;
using System.IO;
using NetWasm.Compiler.ComponentModel.Inlining;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.ComponentModel.Raw;

public interface IRawModuleLinkExecution
{
    void Run(RawModuleLinkRequest request, ComponentPackageWorkspace workspace);
}

public sealed class RawModuleLinkExecution(
    IEmscriptenEnvironmentShimWriter environment,
    IComponentCoreModuleMergeRunner merge,
    IComponentCoreModuleOptimizer optimizer,
    IFileMover files,
    IWasmCoreModuleExportEditor exports,
    IWasmOptimizationSettingsResolver optimizationSettings,
    IWasmCoreModuleInlineAnnotationEditor inlineAnnotations) : IRawModuleLinkExecution
{
    private readonly IEmscriptenEnvironmentShimWriter _environment = environment ??
        throw new ArgumentNullException(nameof(environment));
    private readonly IComponentCoreModuleMergeRunner _merge = merge ??
        throw new ArgumentNullException(nameof(merge));
    private readonly IComponentCoreModuleOptimizer _optimizer = optimizer ??
        throw new ArgumentNullException(nameof(optimizer));
    private readonly IFileMover _files = files ??
        throw new ArgumentNullException(nameof(files));
    private readonly IWasmCoreModuleExportEditor _exports = exports ??
        throw new ArgumentNullException(nameof(exports));
    private readonly IWasmOptimizationSettingsResolver _optimizationSettings = optimizationSettings ??
        throw new ArgumentNullException(nameof(optimizationSettings));
    private readonly IWasmCoreModuleInlineAnnotationEditor _inlineAnnotations = inlineAnnotations ??
        throw new ArgumentNullException(nameof(inlineAnnotations));

    public void Run(RawModuleLinkRequest request, ComponentPackageWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(workspace);
        if (request.InternalRuntimeExports.IsDefault ||
            request.InternalApplicationExports.IsDefault)
            throw ComponentException.Invalid("raw module internal exports are uninitialized");
        try
        {
            var settings = _optimizationSettings.Resolve(request.Optimization, request.OptimizationOverrides);
            var environment = Path.Combine(workspace.TemporaryDirectory, "environment.wasm");
            var merged = Path.Combine(workspace.TemporaryDirectory, "merged.wasm");
            var sanitized = Path.Combine(workspace.TemporaryDirectory, "sanitized.wasm");
            var sanitizedRuntime = Path.Combine(
                workspace.TemporaryDirectory,
                "runtime-sanitized.wasm");
            _environment.Write(environment, request.Target);
            _inlineAnnotations.Rewrite(
                request.ApplicationModulePath,
                sanitized,
                settings.ManagedInlineAnnotations);
            _inlineAnnotations.Rewrite(
                request.RuntimeModulePath,
                sanitizedRuntime,
                settings.RuntimeInlineAnnotations);
            _merge.Run(new ComponentCoreModuleMergeRequest(
                sanitized,
                sanitizedRuntime,
                environment,
                merged,
                request.Target));
            var optimizerInput = merged;
            var removedExports = request.InternalRuntimeExports
                .AddRange(request.InternalApplicationExports);
            if (!removedExports.IsEmpty)
            {
                optimizerInput = sanitized;
                _exports.Rewrite(merged, optimizerInput, new(null, removedExports));
            }
            _optimizer.Optimize(
                optimizerInput,
                workspace.LinkedModulePath,
                request.Target,
                settings.BackendPreset);
            _files.Move(workspace.LinkedModulePath, request.OutputPath);
        }
        finally
        {
            workspace.Dispose();
        }
    }
}
