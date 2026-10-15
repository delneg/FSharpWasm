namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

internal sealed class BrowserLinkTextCapture(IBrowserLinkOperationRecorder recorder) : IWasmTextModuleWriter
{
    private readonly IBrowserLinkOperationRecorder _recorder = recorder ?? throw new System.ArgumentNullException(nameof(recorder));
    public void Write(string source, string outputPath) => _recorder.Record(new BrowserLinkTextOperation(outputPath, source));
}
