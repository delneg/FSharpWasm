using NetWasm.Compiler.ComponentModel.Inlining;

namespace NetWasm.Compiler.ComponentModel.Browser;

public sealed record BrowserInlineAnnotationEdit(
    string InputPath, string OutputPath, WasmInlineAnnotationSelection Selection);
