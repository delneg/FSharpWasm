using System;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

internal sealed class BrowserLinkExportCapture(IBrowserLinkOperationRecorder recorder) : IWasmCoreModuleExportEditor
{
    private readonly IBrowserLinkOperationRecorder _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
    public void Rewrite(string inputPath, string outputPath, WasmExportSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        _recorder.Record(new BrowserLinkExportOperation(inputPath, outputPath, selection.ComponentPrefix, selection.RemovedExports));
    }
}
