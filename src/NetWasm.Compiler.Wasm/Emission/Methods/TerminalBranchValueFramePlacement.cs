namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed record TerminalBranchValueFramePlacement(
    TerminalBranchValueFramePlan Plan,
    ValueFrameLayout FramedLayout);
