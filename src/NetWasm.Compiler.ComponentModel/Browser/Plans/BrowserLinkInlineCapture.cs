using NetWasm.Compiler.ComponentModel.Inlining;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

internal sealed class BrowserLinkInlineCapture(IBrowserLinkOperationRecorder recorder) : IWasmCoreModuleInlineAnnotationEditor
{
    private readonly IBrowserLinkOperationRecorder _recorder = recorder ?? throw new System.ArgumentNullException(nameof(recorder));
    public void Rewrite(string inputPath, string outputPath, WasmInlineAnnotationSelection selection) =>
        _recorder.Record(new BrowserLinkInlineOperation(inputPath, outputPath, selection));
}
