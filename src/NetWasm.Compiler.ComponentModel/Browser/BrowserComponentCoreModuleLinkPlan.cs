using System.Collections.Immutable;

namespace NetWasm.Compiler.ComponentModel.Browser;

public sealed record BrowserWasmTextModule(string OutputPath, string Text);

public sealed record BrowserBinaryenInvocation(string ToolId, ImmutableArray<string> Arguments);

public sealed record BrowserComponentExportPruning(string InputPath, string OutputPath, string Prefix);

public sealed record BrowserCoreModuleValidation(string Path, ImmutableArray<string> Arguments);

/// <summary>
/// Parse TextModules, apply ManagedInlining to application bytes, run Merge, apply ExportPruning, and run
/// Optimization then InlineAnnotationCleanup then Validation. Without optimization,
/// run Validation then InlineAnnotationCleanup to the output. Release CleanupPaths on success or failure.
/// The plan records authoritative commands; it does not execute tools.
/// </summary>
public sealed record BrowserComponentCoreModuleLinkPlan(
    ImmutableArray<BrowserWasmTextModule> TextModules,
    BrowserInlineAnnotationEdit ManagedInlining,
    BrowserInlineAnnotationEdit RuntimeInlining,
    BrowserBinaryenInvocation Merge,
    BrowserComponentExportPruning ExportPruning,
    BrowserBinaryenInvocation? Optimization,
    BrowserCoreModuleValidation? Validation,
    BrowserInlineAnnotationEdit InlineAnnotationCleanup,
    ImmutableArray<string> CleanupPaths);
