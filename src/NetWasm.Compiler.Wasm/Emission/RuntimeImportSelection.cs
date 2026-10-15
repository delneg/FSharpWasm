namespace NetWasm.Compiler.Wasm.Emission;

internal readonly record struct RuntimeImportSelection(
    WasmModuleProfile ModuleProfile,
    bool IncludeTerminalExceptionReporter,
    bool IncludeStackTrace = false,
    bool IncludeExceptionCapture = false,
    bool IncludeEphemeronHandles = false,
    bool IncludeTerminalExceptionRaise = true,
    bool IncludeFinalization = false,
    bool IncludeWeakHandles = false,
    bool IncludeRandomSeed = false);
