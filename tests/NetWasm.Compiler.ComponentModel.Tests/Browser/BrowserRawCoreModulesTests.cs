using NetWasm.Compiler.ComponentModel.Browser;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.ComponentModel.Inlining;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser;

public sealed class BrowserRawCoreModulesTests
{
    [Theory]
    [InlineData(WasmInlineAnnotationSelection.Preserve)]
    [InlineData(WasmInlineAnnotationSelection.SizeAware)]
    public void InvalidInlineSelectionsDoNotAdvanceTheRawPlan(WasmInlineAnnotationSelection selection)
    {
        var capture = new BrowserRawLinkPlanCapture(Request(FinalWasmOptimization.None), Workspace());
        Assert.False(capture.Exists("v/tmp/merged.wasm"));
        Assert.Throws<InvalidOperationException>(() => capture.AddEnvironment("wrong", "(module)"));
        capture.AddEnvironment("v/tmp/environment.wasm", "(module)");
        Assert.Throws<ArgumentOutOfRangeException>(() => capture.AddManagedInlining(
            "v/application.wasm", "v/tmp/sanitized.wasm", WasmInlineAnnotationSelection.Remove));
        capture.AddManagedInlining("v/application.wasm", "v/tmp/sanitized.wasm", selection);
        Assert.Throws<ArgumentOutOfRangeException>(() => capture.AddRuntimeInlining(
            "v/runtime.wasm", "v/tmp/runtime-sanitized.wasm", WasmInlineAnnotationSelection.Remove));
        capture.AddRuntimeInlining("v/runtime.wasm", "v/tmp/runtime-sanitized.wasm", selection);
        capture.AddInvocation(BinaryenToolIds.WasmMerge, []);

        var plan = FinishCopy(capture);

        Assert.Equal(new BrowserInlineAnnotationEdit("v/application.wasm", "v/tmp/sanitized.wasm", selection),
            plan.ManagedInlining);
        Assert.Equal(new BrowserInlineAnnotationEdit("v/runtime.wasm", "v/tmp/runtime-sanitized.wasm", selection),
            plan.RuntimeInlining);
    }

    [Fact]
    public void IncompleteAndOutOfOrderPlansCannotBePublished()
    {
        var capture = new BrowserRawLinkPlanCapture(Request(FinalWasmOptimization.None), Workspace());
        Assert.Throws<InvalidOperationException>(() => capture.AddInvocation(BinaryenToolIds.WasmOpt, []));
        Assert.Throws<InvalidOperationException>(() => capture.AddInvocation("unknown", []));
        Assert.Throws<InvalidOperationException>(() => capture.AddPublication("v/tmp/linked.wasm", "v/output.wasm"));
        Assert.Throws<InvalidOperationException>(() => capture.AddValidation(new("v/tmp/merged.wasm", [])));
        Assert.Throws<ArgumentNullException>(() => capture.AddValidation(null!));
        Assert.Throws<InvalidOperationException>(() => capture.Snapshot());
        capture.AddCleanup("v/tmp");
        Assert.Throws<InvalidOperationException>(() => capture.Snapshot());
    }

    [Fact]
    public void RawPruningRequiresExplicitExportsAndRejectsComponentPolicies()
    {
        var capture = MergedCapture();
        var editor = Assert.IsAssignableFrom<IWasmCoreModuleExportEditor>(new BrowserRawExportCapture(capture));
        Assert.Throws<ArgumentNullException>(() => editor.Rewrite("v/tmp/merged.wasm", "v/tmp/sanitized.wasm", null!));
        Assert.Throws<InvalidOperationException>(() => editor.Rewrite("v/tmp/merged.wasm", "v/tmp/sanitized.wasm",
            new("cm32p2", [new("temporary", 0)])));
        Assert.Throws<InvalidOperationException>(() => editor.Rewrite("v/tmp/merged.wasm", "v/tmp/sanitized.wasm", new(null, [])));
        Assert.Throws<InvalidOperationException>(() => capture.AddPruning("v/tmp/merged.wasm", "v/tmp/sanitized.wasm", []));
        Assert.True(capture.Exists("v/tmp/merged.wasm"));
        Assert.False(capture.Exists("v/tmp/sanitized.wasm"));

        var plan = FinishCopy(capture);

        Assert.Null(plan.ExportPruning);
    }

    [Fact]
    public void RawValidationAdapterRejectsOtherCommandsBeforeCapturingValidation()
    {
        var capture = MergedCapture();
        var runner = Assert.IsAssignableFrom<IComponentPackageOperationRunner>(new BrowserRawValidationCapture(capture));
        const string operation = "validate the linked core module";
        Assert.Throws<InvalidOperationException>(() => runner.Run([], "other"));
        foreach (var command in new string[][]
        {
            [], ["parse", "v/tmp/merged.wasm", "--features", "mvp"],
            ["validate", "v/tmp/merged.wasm", "--other", "mvp"],
        })
            Assert.Throws<InvalidOperationException>(() => runner.Run(command, operation));

        runner.Run(["validate", "v/tmp/merged.wasm", "--features", "mvp"], operation);
        Assert.Throws<InvalidOperationException>(() => capture.AddPublication("v/tmp/linked.wasm", "v/output.wasm"));
        capture.AddInlineAnnotationCleanup("v/tmp/merged.wasm", "v/tmp/linked.wasm");
        capture.AddPublication("v/tmp/linked.wasm", "v/output.wasm");
        capture.AddCleanup("v/tmp");

        Assert.Equal("v/tmp/merged.wasm", capture.Snapshot().Validation.Path);
    }

    private static BrowserRawLinkPlanCapture MergedCapture()
    {
        var capture = new BrowserRawLinkPlanCapture(Request(FinalWasmOptimization.None), Workspace());
        capture.AddEnvironment("v/tmp/environment.wasm", "(module)");
        capture.AddManagedInlining("v/application.wasm", "v/tmp/sanitized.wasm", WasmInlineAnnotationSelection.SizeAware);
        capture.AddRuntimeInlining("v/runtime.wasm", "v/tmp/runtime-sanitized.wasm", WasmInlineAnnotationSelection.SizeAware);
        capture.AddInvocation(BinaryenToolIds.WasmMerge, []);
        return capture;
    }

    private static BrowserRawCoreModuleLinkPlan FinishCopy(BrowserRawLinkPlanCapture capture)
    {
        capture.AddValidation(new("v/tmp/merged.wasm", []));
        capture.AddInlineAnnotationCleanup("v/tmp/merged.wasm", "v/tmp/linked.wasm");
        capture.AddPublication("v/tmp/linked.wasm", "v/output.wasm");
        capture.AddCleanup("v/tmp");
        return capture.Snapshot();
    }

    private static readonly string[] ExpectedMergePrefix =
    [
        "v/tmp/runtime-sanitized.wasm", "netwasm.runtime.v1", "v/tmp/sanitized.wasm",
        "netwasm.application.v1", "v/tmp/environment.wasm", "env",
    ];

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void PlansAuthoritativeRawLinkAndTemporaryExportRemoval(FinalWasmOptimization optimization)
    {
        var request = Request(optimization) with
        {
            InternalRuntimeExports = [new("LZ4_compressBound", 0), new("__heap_base", 3)],
        };

        var plan = BrowserRawCoreModules.CreateLinkPlan(request, Workspace());

        Assert.Equal("v/tmp/environment.wasm", Assert.Single(plan.TextModules).OutputPath);
        Assert.Equal(BinaryenToolIds.WasmMerge, plan.Merge.ToolId);
        Assert.Equal(ExpectedMergePrefix, plan.Merge.Arguments.Take(6));
        Assert.Equal(new BrowserInlineAnnotationEdit("v/application.wasm", "v/tmp/sanitized.wasm",
            WasmInlineAnnotationSelection.SizeAware), plan.ManagedInlining);
        Assert.Equal(new BrowserInlineAnnotationEdit("v/runtime.wasm", "v/tmp/runtime-sanitized.wasm",
            WasmInlineAnnotationSelection.SizeAware), plan.RuntimeInlining);
        Assert.Equal(request.InternalRuntimeExports, plan.ExportPruning!.RemovedExports);
        Assert.Equal("v/tmp/merged.wasm", plan.ExportPruning.InputPath);
        Assert.Equal("v/tmp/sanitized.wasm", plan.ExportPruning.OutputPath);
        Assert.Equal(new BrowserFileMove("v/tmp/linked.wasm", "v/output.wasm"), plan.Publication);
        Assert.Equal("v/tmp", plan.CleanupDirectory);
        if (optimization == FinalWasmOptimization.None)
        {
            Assert.Null(plan.Optimization);
            Assert.Equal(new BrowserInlineAnnotationEdit("v/tmp/sanitized.wasm", "v/tmp/linked.wasm",
                WasmInlineAnnotationSelection.Remove), plan.InlineAnnotationCleanup);
            Assert.Equal("v/tmp/sanitized.wasm", plan.Validation.Path);
        }
        else
        {
            Assert.Equal(BinaryenToolIds.WasmOpt, plan.Optimization!.ToolId);
            Assert.Equal(new BrowserInlineAnnotationEdit("v/tmp/linked.wasm", "v/tmp/linked.wasm",
                WasmInlineAnnotationSelection.Remove), plan.InlineAnnotationCleanup);
            Assert.Equal("v/tmp/linked.wasm", plan.Validation.Path);
        }
    }

    [Fact]
    public void OmitsPruningWhenNoTemporaryExportsExist()
    {
        var plan = BrowserRawCoreModules.CreateLinkPlan(Request(FinalWasmOptimization.None), Workspace());

        Assert.Null(plan.ExportPruning);
        Assert.Equal(new BrowserInlineAnnotationEdit("v/tmp/merged.wasm", "v/tmp/linked.wasm",
            WasmInlineAnnotationSelection.Remove), plan.InlineAnnotationCleanup);
        Assert.Equal("v/tmp/merged.wasm", plan.Validation.Path);
    }

    [Fact]
    public void RejectsAliasedAndUninitializedInputs()
    {
        Assert.Throws<ArgumentException>(() => BrowserRawCoreModules.CreateLinkPlan(
            Request(FinalWasmOptimization.Oz), Workspace() with { LinkedModulePath = "v/application.wasm" }));
        Assert.Throws<NetWasm.Compiler.Core.CompilerException>(() => BrowserRawCoreModules.CreateLinkPlan(
            Request(FinalWasmOptimization.Oz) with { InternalRuntimeExports = default }, Workspace()));
    }

    [Fact]
    public void RemovesTheExactPlannedExportsFromAnInMemoryModule()
    {
        var module = Convert.FromHexString(
            "0061736D010000000709020161000001620001");

        var rewritten = BrowserRawCoreModules.RemoveExports(
            module, [new("a", 0)]);

        Assert.Equal("0061736D0100000007050101620001", Convert.ToHexString(rewritten));
    }

    private static RawModuleLinkRequest Request(FinalWasmOptimization optimization) =>
        new("v/application.wasm", "v/runtime.wasm", "v/output.wasm", ComponentTarget.Wasm32Wasi02, optimization);

    private static BrowserRawCoreModuleWorkspace Workspace() => new("v/tmp", "v/tmp/linked.wasm");
}
