using System;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

internal sealed class BrowserLinkCaptureFileExistence(BrowserLinkCaptureState state) : IFileExistence
{
    private readonly BrowserLinkCaptureState _state = state ?? throw new ArgumentNullException(nameof(state));

    public bool Exists(string path) => _state.Available.Contains(path);
}
